using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

using UnityEditor;
using UnityEngine;

namespace GameRebellionSdk.Unity.Editor
{
    /// <summary>
    /// The Editor side of the local Unity MCP (EDITOR_MCP_ARCHITECTURE.md §6): a
    /// small JSON-lines server on 127.0.0.1 that the gr-editor-mcp relay talks to.
    ///
    /// Every domain reload tears it down and this static constructor brings it back,
    /// on the same port and with the same secret where possible, so a relay that is
    /// mid-call only has to reconnect. Requests run on the main thread; the socket
    /// threads only read, check the handshake and hand lines over.
    ///
    /// Nothing here holds or returns a GameRebellion token.
    /// </summary>
    [InitializeOnLoad]
    internal static class GRMcpBridge
    {
        public const int Protocol = 1;

        // Per Editor session, so they survive domain reloads but not a restart.
        private const string PortKey = "GameRebellion.Bridge.Port";
        private const string SecretKey = "GameRebellion.Bridge.Secret";
        private const int MaxLineLength = 1 << 20;

        private static readonly object Gate = new object();
        private static readonly List<Connection> Connections = new List<Connection>();
        private static TcpListener _listener;
        private static string _secret;
        private static string _helloReply;
        private static volatile bool _running;

        static GRMcpBridge()
        {
            // CI and batch builds get no listening socket.
            if (Application.isBatchMode)
            {
                return;
            }

            // Old editor scripts in the project start their own (GRLegacySources).
            if (GRLegacySources.Found)
            {
                return;
            }

            GRBridgePaths.Init();
            GRMainThread.Init();
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += () =>
            {
                Stop();
                TryDelete(GRBridgePaths.BridgeFile);
            };

            Start();
        }

        /// <summary>Raised on the main thread when the listener starts or stops, a client comes or goes, or a call is handled.</summary>
        public static event Action StatusChanged;

        public static bool IsRunning => _running;
        public static int Port { get; private set; }
        public static string LastError { get; private set; }
        public static DateTime? LastCallUtc { get; private set; }

        public static int ClientCount
        {
            get
            {
                lock (Gate)
                {
                    return Connections.Count;
                }
            }
        }

        private static void Start()
        {
            _secret = SessionState.GetString(SecretKey, string.Empty);
            if (_secret.Length == 0)
            {
                _secret = NewSecret();
                SessionState.SetString(SecretKey, _secret);
            }

            var previousPort = SessionState.GetInt(PortKey, 0);
            if (!TryListen(previousPort) && !(previousPort != 0 && TryListen(0)))
            {
                RaiseStatus();
                return;
            }

            SessionState.SetInt(PortKey, Port);
            // Built here: JsonUtility and Application.* belong to the main thread, not the socket threads.
            _helloReply = JsonUtility.ToJson(new GRBridgeResponse { ok = true, done = true, text = Application.unityVersion });
            try
            {
                WriteBridgeFile();
            }
            catch (Exception e)
            {
                LastError = $"Couldn't write {GRBridgePaths.BridgeFile}: {e.Message}";
                Stop();
                return;
            }

            _running = true;
            new Thread(AcceptLoop) { IsBackground = true, Name = "GameRebellion MCP bridge" }.Start();
            RaiseStatus();
        }

        private static bool TryListen(int port)
        {
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, port);
                // A reload right after a call leaves the old connection in TIME_WAIT on this port.
                listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                listener.Start();
                _listener = listener;
                Port = ((IPEndPoint)listener.LocalEndpoint).Port;
                LastError = null;
                return true;
            }
            catch (SocketException e)
            {
                LastError = $"Couldn't listen on 127.0.0.1:{port}: {e.Message}";
                return false;
            }
        }

        private static void Stop()
        {
            _running = false;
            try
            {
                _listener?.Stop();
            }
            catch (SocketException)
            {
                // Already closed.
            }

            _listener = null;
            lock (Gate)
            {
                foreach (var connection in Connections)
                {
                    connection.Close();
                }

                Connections.Clear();
            }
        }

        private static void WriteBridgeFile()
        {
            var info = new GRBridgeInfo
            {
                protocol = Protocol,
                port = Port,
                secret = _secret,
                pid = Process.GetCurrentProcess().Id,
                projectPath = GRProjectInfo.ProjectPath,
                unityVersion = Application.unityVersion,
                sdkVersion = GRProjectInfo.SdkVersion(),
            };

            // Write then rename, so the relay never reads half a file.
            var temp = GRBridgePaths.BridgeFile + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(info, true));
            TryDelete(GRBridgePaths.BridgeFile);
            File.Move(temp, GRBridgePaths.BridgeFile);
        }

        // ---- Socket threads ------------------------------------------------------

        private static void AcceptLoop()
        {
            var listener = _listener;
            while (_running && listener != null)
            {
                TcpClient client;
                try
                {
                    client = listener.AcceptTcpClient();
                }
                catch (Exception)
                {
                    break; // Stop() closed the listener.
                }

                var connection = new Connection(client);
                lock (Gate)
                {
                    Connections.Add(connection);
                }

                GRMainThread.Post(RaiseStatus);
                new Thread(() => ReadLoop(connection)) { IsBackground = true, Name = "GameRebellion MCP bridge client" }.Start();
            }
        }

        private static void ReadLoop(Connection connection)
        {
            try
            {
                var reader = new StreamReader(connection.Stream, new UTF8Encoding(false));
                var hello = ReadLine(reader);
                if (hello == null || LooksLikeHttp(hello) || !IsValidHello(hello))
                {
                    return;
                }

                connection.Send(_helloReply);

                string line;
                while (_running && (line = ReadLine(reader)) != null)
                {
                    var request = line;
                    GRMainThread.Post(() => Handle(connection, request));
                }
            }
            catch (Exception)
            {
                // The relay hung up, the line was too long, or the bridge is stopping.
            }
            finally
            {
                connection.Close();
                lock (Gate)
                {
                    Connections.Remove(connection);
                }

                GRMainThread.Post(RaiseStatus);
            }
        }

        /// <summary>Reads one line, refusing anything longer than <see cref="MaxLineLength"/>.</summary>
        private static string ReadLine(StreamReader reader)
        {
            var line = new StringBuilder();
            while (true)
            {
                var next = reader.Read();
                if (next == -1)
                {
                    return line.Length > 0 ? line.ToString() : null;
                }

                if (next == '\n')
                {
                    return line.ToString().TrimEnd('\r');
                }

                if (line.Length >= MaxLineLength)
                {
                    throw new IOException("Line too long.");
                }

                line.Append((char)next);
            }
        }

        /// <summary>A web page can reach 127.0.0.1 too; anything that speaks HTTP is dropped before the handshake.</summary>
        private static bool LooksLikeHttp(string firstLine)
        {
            foreach (var verb in new[] { "GET ", "POST ", "PUT ", "HEAD ", "OPTIONS ", "DELETE ", "PATCH ", "CONNECT " })
            {
                if (firstLine.StartsWith(verb, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return firstLine.Contains(" HTTP/");
        }

        private static bool IsValidHello(string line)
        {
            GRBridgeHello hello;
            try
            {
                hello = JsonUtility.FromJson<GRBridgeHello>(line);
            }
            catch (Exception)
            {
                return false;
            }

            return hello != null && hello.type == "hello" && hello.protocol == Protocol && FixedTimeEquals(hello.secret, _secret);
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length)
            {
                return false;
            }

            var difference = 0;
            for (var i = 0; i < a.Length; i++)
            {
                difference |= a[i] ^ b[i];
            }

            return difference == 0;
        }

        // ---- Main thread ---------------------------------------------------------

        private static void Handle(Connection connection, string line)
        {
            GRBridgeRequest request = null;
            GRBridgeResponse response;
            try
            {
                request = JsonUtility.FromJson<GRBridgeRequest>(line);
                response = GRMcpTools.Handle(request);
            }
            catch (Exception e)
            {
                response = GRBridgeResponse.Failure(e.Message);
            }

            response.id = request?.id ?? 0;
            LastCallUtc = DateTime.UtcNow;
            connection.Send(JsonUtility.ToJson(response));
            RaiseStatus();
        }

        private static void RaiseStatus()
        {
            StatusChanged?.Invoke();
        }

        private static string NewSecret()
        {
            var bytes = new byte[32];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }

            // Hex, not base64: JSON writers escape '+' and '/', and the secret must compare byte for byte.
            return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception)
            {
                // Missing or locked; the next start overwrites it.
            }
        }

        private sealed class Connection
        {
            private readonly TcpClient _client;

            public Connection(TcpClient client)
            {
                _client = client;
                Stream = client.GetStream();
            }

            public NetworkStream Stream { get; }

            public void Send(string json)
            {
                var bytes = Encoding.UTF8.GetBytes(json + "\n");
                lock (this)
                {
                    try
                    {
                        Stream.Write(bytes, 0, bytes.Length);
                        Stream.Flush();
                    }
                    catch (Exception)
                    {
                        Close();
                    }
                }
            }

            public void Close()
            {
                try
                {
                    _client.Close();
                }
                catch (Exception)
                {
                    // Already closed.
                }
            }
        }
    }
}
