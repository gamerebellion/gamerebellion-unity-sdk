using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace GameRebellionSdk.Unity.Editor
{
    /// <summary>
    /// The coding agents the window can set up, and where each keeps its MCP servers.
    /// Claude Code is set up through its CLI; the others through their user-level
    /// config file, so nothing lands in the developer's repository. Those files are
    /// global, so their Unity MCP entry serves one Unity project at a time: the one
    /// connected last.
    /// </summary>
    internal static class GRAgentClients
    {
        private static readonly GRIdeClient[] All =
        {
            GRIdeClient.ClaudeCode, GRIdeClient.Codex, GRIdeClient.Cursor, GRIdeClient.VSCode, GRIdeClient.GeminiCli,
        };

        public static string Name(GRIdeClient client)
        {
            switch (client)
            {
                case GRIdeClient.Codex: return "Codex";
                case GRIdeClient.Cursor: return "Cursor";
                case GRIdeClient.VSCode: return "VS Code";
                case GRIdeClient.GeminiCli: return "Gemini CLI";
                default: return "Claude Code";
            }
        }

        /// <summary>Claude Code always; the others when their config folder is on this machine.</summary>
        public static IReadOnlyList<GRIdeClient> Detected() =>
            All.Where(client => client == GRIdeClient.ClaudeCode || Directory.Exists(Path.GetDirectoryName(ConfigFor(client).Path))).ToArray();

        /// <summary>The file a client reads its MCP servers from; null for Claude Code, whose CLI owns its config.</summary>
        public static GRMcpConfigFile ConfigFor(GRIdeClient client)
        {
            switch (client)
            {
                case GRIdeClient.Codex:
                    return new GRCodexConfig(Path.Combine(Environment.GetEnvironmentVariable("CODEX_HOME") is string codexHome && codexHome.Length > 0 ? codexHome : Path.Combine(Home, ".codex"), "config.toml"));
                case GRIdeClient.Cursor:
                    return new GRJsonMcpConfig(Path.Combine(Home, ".cursor", "mcp.json"), "mcpServers", "url", typed: false);
                case GRIdeClient.VSCode:
                    return new GRJsonMcpConfig(Path.Combine(VSCodeUserFolder, "mcp.json"), "servers", "url", typed: true);
                case GRIdeClient.GeminiCli:
                    return new GRJsonMcpConfig(Path.Combine(Home, ".gemini", "settings.json"), "mcpServers", "httpUrl", typed: false);
                default:
                    return null;
            }
        }

        private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        private static string VSCodeUserFolder =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Code", "User")
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? Path.Combine(Home, "Library", "Application Support", "Code", "User")
            : Path.Combine(Home, ".config", "Code", "User");
    }

    /// <summary>One MCP server as a client's config file has it.</summary>
    internal sealed class GRServerEntry
    {
        public string Url { get; set; }
        public string Command { get; set; }
        public IReadOnlyList<string> Args { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// A coding agent's own MCP config file. Only the window's two entries are read or
    /// changed, and an entry that already matches is left as it is. The file before a
    /// change is kept next to it (.gamerebellion.bak). A file the window can't read
    /// safely throws FormatException and is never written.
    /// </summary>
    internal abstract class GRMcpConfigFile
    {
        protected GRMcpConfigFile(string path)
        {
            Path = path;
        }

        public string Path { get; }

        /// <summary>The path with the home folder as ~, for messages.</summary>
        public string DisplayPath
        {
            get
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return Path.StartsWith(home, StringComparison.Ordinal) ? "~" + Path.Substring(home.Length) : Path;
            }
        }

        /// <summary>The gr-mcp and Unity MCP entries; null where there is none.</summary>
        public abstract (GRServerEntry Hosted, GRServerEntry Local) Read(GRMcpEndpoints endpoints);

        /// <summary>Puts both entries in; true if the file changed.</summary>
        public abstract bool Write(GRMcpEndpoints endpoints);

        public static bool HostedMatches(GRServerEntry entry, GRMcpEndpoints endpoints) =>
            entry?.Url != null && string.Equals(entry.Url.TrimEnd('/'), endpoints.HostedUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

        public static bool LocalMatches(GRServerEntry entry, GRMcpEndpoints endpoints) =>
            entry?.Command != null && SamePath(entry.Command, endpoints.RelayPath)
            && entry.Args.SequenceEqual(new[] { "--project", endpoints.ProjectPath });

        /// <summary>What the file says about <paramref name="client"/>; reading it is all a file-based client allows.</summary>
        public GRAgentStatus Status(GRIdeClient client, GRMcpEndpoints endpoints)
        {
            (GRServerEntry Hosted, GRServerEntry Local) entries = (null, null);
            try
            {
                if (File.Exists(Path))
                {
                    entries = Read(endpoints);
                }
            }
            catch (FormatException e)
            {
                return new GRAgentStatus(GRAgentState.Problem, $"{e.Message}, so the window leaves it alone. Add the servers by hand: Copy config.");
            }
            catch (IOException e)
            {
                return new GRAgentStatus(GRAgentState.Problem, $"Couldn't read {DisplayPath}: {e.Message}");
            }

            var (hosted, local) = entries;
            var hostedName = endpoints.HostedServerName;
            var localName = GRMcpEndpoints.LocalServerName;
            if (hosted == null && local == null)
            {
                return new GRAgentStatus(GRAgentState.NotSetUp, $"Not set up yet. Connect adds both servers to {DisplayPath}.");
            }

            if (hosted == null)
            {
                return new GRAgentStatus(GRAgentState.NotSetUp, $"{hostedName} isn't in {DisplayPath}. Reconnect adds it.");
            }

            if (!HostedMatches(hosted, endpoints))
            {
                return new GRAgentStatus(GRAgentState.NotSetUp, $"{hostedName} points at {hosted.Url ?? "something else"}, not {endpoints.HostedUrl}. Reconnect fixes it.");
            }

            if (local == null)
            {
                return new GRAgentStatus(GRAgentState.NotSetUp, $"{localName} isn't in {DisplayPath}. Reconnect adds it.");
            }

            if (!local.Args.Contains(endpoints.ProjectPath))
            {
                return new GRAgentStatus(GRAgentState.NotSetUp, $"{localName} serves another Unity project. Reconnect points it at this one.");
            }

            if (!LocalMatches(local, endpoints))
            {
                return new GRAgentStatus(GRAgentState.NotSetUp, $"{localName} doesn't start the GameRebellion relay. Reconnect fixes it.");
            }

            if (!File.Exists(endpoints.RelayPath))
            {
                return new GRAgentStatus(GRAgentState.Problem, $"The relay isn't installed at {endpoints.RelayPath}. Reconnect installs it.");
            }

            return new GRAgentStatus(GRAgentState.Ready,
                $"Set up in {DisplayPath}. {GRAgentClients.Name(client)} asks you to sign in to gr-mcp the first time it uses it.");
        }

        /// <summary>The file as it was before the window's last change to it.</summary>
        public string BackupPath => Path + ".gamerebellion.bak";

        /// <summary>Writes the new text, keeping the old file as <see cref="BackupPath"/>.</summary>
        protected void Save(string text)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            if (File.Exists(Path))
            {
                File.Copy(Path, BackupPath, overwrite: true);
            }

            File.WriteAllText(Path, text);
        }

        internal static bool SamePath(string a, string b) =>
            string.Equals(a?.Replace('\\', '/'), b?.Replace('\\', '/'),
                RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// mcp.json-style files: Cursor (mcpServers, url), VS Code (servers, with a type)
    /// and Gemini CLI (mcpServers in settings.json, httpUrl for streamable HTTP).
    /// Read and written as plain JSON: everything else in the file is kept, in order,
    /// though its indentation becomes two spaces. JSON with comments is refused.
    /// </summary>
    internal sealed class GRJsonMcpConfig : GRMcpConfigFile
    {
        private readonly string _container;
        private readonly string _urlKey;
        private readonly bool _typed;

        public GRJsonMcpConfig(string path, string container, string urlKey, bool typed) : base(path)
        {
            _container = container;
            _urlKey = urlKey;
            _typed = typed;
        }

        public override (GRServerEntry Hosted, GRServerEntry Local) Read(GRMcpEndpoints endpoints)
        {
            var servers = Servers(Load(), create: false);
            return (Entry(servers, endpoints.HostedServerName), Entry(servers, GRMcpEndpoints.LocalServerName));
        }

        public override bool Write(GRMcpEndpoints endpoints)
        {
            var root = Load();
            var servers = Servers(root, create: true);
            var changed = false;
            if (!HostedMatches(Entry(servers, endpoints.HostedServerName), endpoints))
            {
                var hosted = new Dictionary<string, object>();
                if (_typed) hosted["type"] = "http";
                hosted[_urlKey] = endpoints.HostedUrl;
                servers[endpoints.HostedServerName] = hosted;
                changed = true;
            }

            if (!LocalMatches(Entry(servers, GRMcpEndpoints.LocalServerName), endpoints))
            {
                var local = new Dictionary<string, object>();
                if (_typed) local["type"] = "stdio";
                local["command"] = endpoints.RelayPath;
                local["args"] = new List<object> { "--project", endpoints.ProjectPath };
                servers[GRMcpEndpoints.LocalServerName] = local;
                changed = true;
            }

            if (changed)
            {
                Save(GRJson.Write(root) + "\n");
            }

            return changed;
        }

        private Dictionary<string, object> Load()
        {
            if (!File.Exists(Path))
            {
                return new Dictionary<string, object>();
            }

            var text = File.ReadAllText(Path);
            if (text.Trim().Length == 0)
            {
                return new Dictionary<string, object>();
            }

            object parsed;
            try
            {
                parsed = GRJson.Parse(text);
            }
            catch (FormatException e)
            {
                throw new FormatException($"{DisplayPath} isn't plain JSON ({e.Message.TrimEnd('.')})");
            }

            return parsed as Dictionary<string, object> ?? throw new FormatException($"{DisplayPath} doesn't hold a JSON object");
        }

        private Dictionary<string, object> Servers(Dictionary<string, object> root, bool create)
        {
            if (root.TryGetValue(_container, out var value))
            {
                return value as Dictionary<string, object> ?? throw new FormatException($"\"{_container}\" in {DisplayPath} isn't an object");
            }

            if (!create)
            {
                return null;
            }

            var servers = new Dictionary<string, object>();
            root[_container] = servers;
            return servers;
        }

        private static GRServerEntry Entry(Dictionary<string, object> servers, string name)
        {
            if (servers == null || !servers.TryGetValue(name, out var value) || !(value is Dictionary<string, object> entry))
            {
                return null;
            }

            return new GRServerEntry
            {
                Url = GRJson.Text(entry, "url", "httpUrl", "serverUrl"),
                Command = GRJson.Text(entry, "command"),
                Args = GRJson.GetArray(entry, "args")?.OfType<string>().ToList() ?? new List<string>(),
            };
        }
    }

    /// <summary>
    /// Codex's config.toml. Only the [mcp_servers.gamerebellion-…] tables are touched:
    /// each is replaced where it stands, or appended, and every other line stays
    /// exactly as it was. A file that defines mcp_servers another way (a plain
    /// [mcp_servers] table, or dotted or inline keys) is refused rather than guessed at.
    /// </summary>
    internal sealed class GRCodexConfig : GRMcpConfigFile
    {
        private static readonly Regex Header = new Regex(@"^\s*\[\[?\s*(?<key>[^\]]+?)\s*\]\]?\s*(#.*)?$", RegexOptions.Compiled);
        private static readonly Regex TopLevelMcp = new Regex(@"^\s*""?mcp_servers""?\s*[.=]", RegexOptions.Compiled);
        private static readonly Regex StringValue = new Regex(@"""((?:[^""\\]|\\.)*)""|'([^']*)'", RegexOptions.Compiled);

        public GRCodexConfig(string path) : base(path)
        {
        }

        public override (GRServerEntry Hosted, GRServerEntry Local) Read(GRMcpEndpoints endpoints)
        {
            var lines = Load(out _);
            return (Entry(lines, endpoints.HostedServerName), Entry(lines, GRMcpEndpoints.LocalServerName));
        }

        public override bool Write(GRMcpEndpoints endpoints)
        {
            var lines = Load(out var newline);
            var changed = false;
            if (!HostedMatches(Entry(lines, endpoints.HostedServerName), endpoints))
            {
                Put(lines, endpoints.HostedServerName, new[] { $"url = {Quote(endpoints.HostedUrl)}" });
                changed = true;
            }

            if (!LocalMatches(Entry(lines, GRMcpEndpoints.LocalServerName), endpoints))
            {
                Put(lines, GRMcpEndpoints.LocalServerName, new[]
                {
                    $"command = {Quote(endpoints.RelayPath)}",
                    $"args = [\"--project\", {Quote(endpoints.ProjectPath)}]",
                });
                changed = true;
            }

            if (changed)
            {
                Save(string.Join(newline, lines) + newline);
            }

            return changed;
        }

        private List<string> Load(out string newline)
        {
            var text = File.Exists(Path) ? File.ReadAllText(Path) : string.Empty;
            newline = text.Contains("\r\n") ? "\r\n" : "\n";
            var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
            while (lines.Count > 0 && lines[lines.Count - 1].Length == 0)
            {
                lines.RemoveAt(lines.Count - 1);
            }

            var firstHeader = lines.FindIndex(line => Header.IsMatch(line));
            var preamble = firstHeader < 0 ? lines : lines.Take(firstHeader);
            if (preamble.Any(line => TopLevelMcp.IsMatch(line)) || lines.Any(line => KeyOf(line) == "mcp_servers"))
            {
                throw new FormatException($"{DisplayPath} defines mcp_servers in a layout the window doesn't edit");
            }

            return lines;
        }

        /// <summary>The table header's key with quotes and spaces around dots removed; null if the line isn't a header.</summary>
        private static string KeyOf(string line)
        {
            var match = Header.Match(line);
            return match.Success ? Regex.Replace(match.Groups["key"].Value, @"\s*\.\s*", ".").Replace("\"", string.Empty).Replace("'", string.Empty) : null;
        }

        /// <summary>The lines of [mcp_servers.name] and its sub-tables: start, and end (exclusive, trailing blank lines left out).</summary>
        private static (int Start, int End) Block(List<string> lines, string name)
        {
            var key = "mcp_servers." + name;
            var start = lines.FindIndex(line => KeyOf(line) == key);
            if (start < 0)
            {
                return (-1, -1);
            }

            var end = start + 1;
            while (end < lines.Count)
            {
                var other = KeyOf(lines[end]);
                if (other != null && other != key && !other.StartsWith(key + ".", StringComparison.Ordinal))
                {
                    break;
                }

                end++;
            }

            while (end > start + 1 && lines[end - 1].Trim().Length == 0)
            {
                end--;
            }

            return (start, end);
        }

        private static GRServerEntry Entry(List<string> lines, string name)
        {
            var (start, end) = Block(lines, name);
            if (start < 0)
            {
                return null;
            }

            // The table's own keys, before any sub-table such as [mcp_servers.name.env].
            var own = lines.Skip(start + 1).Take(end - start - 1).TakeWhile(line => KeyOf(line) == null).ToList();
            var args = new List<string>();
            var argsLine = own.FindIndex(line => Regex.IsMatch(line, @"^\s*args\s*="));
            if (argsLine >= 0)
            {
                var array = string.Join(" ", own.Skip(argsLine).TakeWhile((line, i) => i == 0 || !own[argsLine + i - 1].Contains("]")));
                array = array.Substring(array.IndexOf('=') + 1);
                args.AddRange(StringValue.Matches(array.Substring(0, array.Contains("]") ? array.IndexOf(']') + 1 : array.Length)).Cast<Match>().Select(Unquote));
            }

            return new GRServerEntry { Url = Value(own, "url"), Command = Value(own, "command"), Args = args };
        }

        private static string Value(List<string> lines, string key)
        {
            var line = lines.FirstOrDefault(text => Regex.IsMatch(text, $@"^\s*{key}\s*="));
            var match = line == null ? null : StringValue.Match(line.Substring(line.IndexOf('=') + 1));
            return match != null && match.Success ? Unquote(match) : null;
        }

        /// <summary>Replaces the table where it stands, or appends it after a blank line.</summary>
        private static void Put(List<string> lines, string name, IEnumerable<string> body)
        {
            var block = new List<string> { $"[mcp_servers.{name}]" };
            block.AddRange(body);
            var (start, end) = Block(lines, name);
            if (start >= 0)
            {
                lines.RemoveRange(start, end - start);
                lines.InsertRange(start, block);
                return;
            }

            if (lines.Count > 0 && lines[lines.Count - 1].Trim().Length > 0)
            {
                lines.Add(string.Empty);
            }

            lines.AddRange(block);
        }

        private static string Unquote(Match match)
        {
            if (match.Groups[2].Success)
            {
                return match.Groups[2].Value;
            }

            var value = match.Groups[1].Value;
            var text = new StringBuilder();
            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] != '\\' || i + 1 >= value.Length)
                {
                    text.Append(value[i]);
                    continue;
                }

                var next = value[++i];
                switch (next)
                {
                    case 'n': text.Append('\n'); break;
                    case 't': text.Append('\t'); break;
                    case 'r': text.Append('\r'); break;
                    case 'u' when i + 4 < value.Length:
                        text.Append((char)Convert.ToInt32(value.Substring(i + 1, 4), 16));
                        i += 4;
                        break;
                    default: text.Append(next); break;
                }
            }

            return text.ToString();
        }

        private static string Quote(string value)
        {
            var text = new StringBuilder("\"");
            foreach (var c in value)
            {
                if (c == '"' || c == '\\') text.Append('\\').Append(c);
                else if (c < ' ') text.Append("\\u").Append(((int)c).ToString("x4"));
                else text.Append(c);
            }

            return text.Append('"').ToString();
        }
    }
}
