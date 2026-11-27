using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GameRebellionSdk.Unity.Editor
{
    /// <summary>
    /// Receives the browser's redirect back from the authorize page (RFC 8252
    /// loopback redirect): a one-request HTTP server on 127.0.0.1. Plain TcpListener
    /// rather than HttpListener, which can't take port 0 and behaves differently on
    /// Mono across platforms.
    /// </summary>
    internal sealed class GRLoopbackListener : IDisposable
    {
        private const int MaxRequestBytes = 16 * 1024;

        private readonly TcpListener _listener;

        private GRLoopbackListener(TcpListener listener)
        {
            _listener = listener;
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        public int Port { get; }

        public string RedirectUri => $"http://127.0.0.1:{Port}/callback";

        /// <summary>Listens on a free port. The authorization server accepts any port on 127.0.0.1 with the path /callback.</summary>
        public static GRLoopbackListener Start()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return new GRLoopbackListener(listener);
        }

        /// <summary>
        /// Waits for GET /callback and returns its query parameters. Other requests,
        /// such as a browser's favicon, get a 404 and the wait goes on.
        /// </summary>
        public async Task<Dictionary<string, string>> WaitForCallbackAsync(CancellationToken cancellationToken)
        {
            using (cancellationToken.Register(() => _listener.Stop()))
            {
                while (true)
                {
                    TcpClient client;
                    try
                    {
                        client = await _listener.AcceptTcpClientAsync();
                    }
                    catch (Exception) when (cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(cancellationToken);
                    }

                    using (client)
                    {
                        var stream = client.GetStream();
                        var target = await ReadRequestTargetAsync(stream);
                        var queryStart = target?.IndexOf('?') ?? -1;
                        var path = queryStart < 0 ? target : target.Substring(0, queryStart);
                        if (path != "/callback" && path != "/callback/")
                        {
                            await RespondAsync(stream, "404 Not Found", "Not found.");
                            continue;
                        }

                        var query = ParseQuery(queryStart < 0 ? string.Empty : target.Substring(queryStart + 1));
                        await RespondAsync(stream, "200 OK", query.ContainsKey("code") ? SignedInPage : FailedPage);
                        return query;
                    }
                }
            }
        }

        /// <summary>The target of "GET /callback?… HTTP/1.1", after reading the whole request head.</summary>
        private static async Task<string> ReadRequestTargetAsync(NetworkStream stream)
        {
            var head = new StringBuilder();
            var buffer = new byte[2048];
            while (head.Length < MaxRequestBytes && !head.ToString().Contains("\r\n\r\n"))
            {
                var read = await stream.ReadAsync(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    break;
                }

                head.Append(Encoding.ASCII.GetString(buffer, 0, read));
            }

            var requestLine = head.ToString().Split(new[] { "\r\n" }, StringSplitOptions.None)[0].Split(' ');
            return requestLine.Length >= 2 && requestLine[0] == "GET" ? requestLine[1] : null;
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in query.Split('&'))
            {
                if (pair.Length == 0)
                {
                    continue;
                }

                var equals = pair.IndexOf('=');
                var key = Uri.UnescapeDataString((equals < 0 ? pair : pair.Substring(0, equals)).Replace('+', ' '));
                var value = equals < 0 ? string.Empty : Uri.UnescapeDataString(pair.Substring(equals + 1).Replace('+', ' '));
                values[key] = value;
            }

            return values;
        }

        private static async Task RespondAsync(Stream stream, string status, string html)
        {
            var body = Encoding.UTF8.GetBytes(html);
            var head = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\n" +
                "Cache-Control: no-store\r\nConnection: close\r\n\r\n");
            try
            {
                await stream.WriteAsync(head, 0, head.Length);
                await stream.WriteAsync(body, 0, body.Length);
            }
            catch (IOException)
            {
                // The browser went away; the code in the query is all that matters.
            }
        }

        private const string PageStyle =
            "<style>body{font-family:-apple-system,Segoe UI,sans-serif;background:#1e1e1e;color:#e8e8e8;display:flex;" +
            "align-items:center;justify-content:center;height:100vh;margin:0}main{text-align:center}" +
            "h1{font-size:20px;margin:0 0 8px}p{color:#b0b0b0;margin:0}b{color:#DF07B9}</style>";

        private const string SignedInPage =
            "<!doctype html><html><head><meta charset=\"utf-8\"><title>GameRebellion</title>" + PageStyle +
            "</head><body><main><h1><b>GameRebellion</b>: you're signed in</h1><p>You can close this tab and go back to Unity.</p></main></body></html>";

        private const string FailedPage =
            "<!doctype html><html><head><meta charset=\"utf-8\"><title>GameRebellion</title>" + PageStyle +
            "</head><body><main><h1>Sign-in didn't complete</h1><p>Go back to Unity to see why, and try again.</p></main></body></html>";

        public void Dispose()
        {
            try
            {
                _listener.Stop();
            }
            catch (SocketException)
            {
                // Already stopped.
            }
        }
    }
}
