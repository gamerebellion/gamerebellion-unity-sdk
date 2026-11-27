using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GameRebellionSdk.Unity.Editor
{
    /// <summary>A GameRebellion.Initialize(...) call in the project's scripts.</summary>
    internal sealed class GRInitializeCall
    {
        /// <summary>Assets/… path of the script.</summary>
        public string AssetPath { get; set; }

        /// <summary>1-based line of the call.</summary>
        public int Line { get; set; }

        /// <summary>
        /// The key, when it is a string literal in the call or in a constant of the same
        /// file; null otherwise. Kept in memory to compare with the selected key, never logged.
        /// </summary>
        public string Key { get; set; }

        /// <summary>Where <see cref="Key"/>'s text sits in the file: its first character, length and line.</summary>
        public int KeyStart { get; set; }
        public int KeyLength { get; set; }
        public int KeyLine { get; set; }

        /// <summary>The constant holding the key (kAPI), or the expression when the window can't read it.</summary>
        public string KeySource { get; set; }

        /// <summary>The environment the call passes; null when it passes none, or one the window can't read.</summary>
        public GrEnvironment? Environment { get; set; }

        public string Where => $"{Path.GetFileName(AssetPath)}:{Line}";
    }

    /// <summary>
    /// Finds GameRebellion.Initialize calls in the scripts under Assets/, without
    /// compiling anything: comments, strings and preprocessor lines are blanked out
    /// first, so a call inside a string or a comment doesn't count. The SDK's own
    /// Runtime and Editor code is skipped. Reads files only; <see cref="ReplaceKey"/>
    /// is the one write, and only after the developer confirms it.
    /// </summary>
    internal static class GRInitializeScanner
    {
        private static readonly Regex Call = new Regex(@"(?<![\w@])GameRebellion\s*\.\s*Initialize\s*\(", RegexOptions.Compiled);
        private static readonly Regex Identifier = new Regex(@"^[A-Za-z_]\w*$", RegexOptions.Compiled);
        private static readonly Regex NamedArgument = new Regex(@"^[A-Za-z_]\w*\s*:(?!:)", RegexOptions.Compiled);
        private static readonly Regex Literal = new Regex("^@?\"x*\"$", RegexOptions.Compiled);
        private static readonly Regex EnvironmentArgument = new Regex(@"(?:^|\.)\s*GrEnvironment\s*\.\s*(\w+)$", RegexOptions.Compiled);
        private static readonly Regex SafeKey = new Regex(@"^[A-Za-z0-9_\-]+$", RegexOptions.Compiled);

        /// <summary>
        /// All calls under <paramref name="assetsFolder"/> (Application.dataPath), skipping
        /// folders under <paramref name="skipped"/> (absolute paths). Runs off the main thread.
        /// </summary>
        public static List<GRInitializeCall> Scan(string assetsFolder, IReadOnlyList<string> skipped)
        {
            var calls = new List<GRInitializeCall>();
            foreach (var file in Directory.EnumerateFiles(assetsFolder, "*.cs", SearchOption.AllDirectories))
            {
                var full = file.Replace('\\', '/');
                if (skipped.Any(folder => full.StartsWith(folder + "/", StringComparison.Ordinal)) || IsIgnoredByUnity(full, assetsFolder))
                {
                    continue;
                }

                string text;
                try
                {
                    text = File.ReadAllText(full);
                }
                catch (IOException)
                {
                    continue;
                }

                if (text.IndexOf("Initialize", StringComparison.Ordinal) >= 0)
                {
                    calls.AddRange(ScanText(text, "Assets" + full.Substring(assetsFolder.Length)));
                }
            }

            return calls;
        }

        public static IEnumerable<GRInitializeCall> ScanText(string text, string assetPath)
        {
            var code = Blank(text);
            foreach (Match match in Call.Matches(code))
            {
                var open = match.Index + match.Length - 1;
                var arguments = Arguments(code, open);
                if (arguments == null)
                {
                    continue;
                }

                var call = new GRInitializeCall { AssetPath = assetPath, Line = LineOf(text, match.Index) };
                if (arguments.Count > 0)
                {
                    ReadKey(text, code, arguments[0], call);
                }

                if (arguments.Count > 1)
                {
                    var environment = EnvironmentArgument.Match(Trim(code, arguments[1]).Text);
                    if (environment.Success && Enum.TryParse(environment.Groups[1].Value, out GrEnvironment value))
                    {
                        call.Environment = value;
                    }
                }

                yield return call;
            }
        }

        /// <summary>
        /// Swaps the key's text for <paramref name="newKey"/>, if the file still has the
        /// text the scan found there. Keeps the file's encoding mark and line endings.
        /// </summary>
        public static bool ReplaceKey(string projectPath, GRInitializeCall call, string newKey, out string error)
        {
            error = null;
            if (call.Key == null || !SafeKey.IsMatch(newKey))
            {
                error = "The window can't write this key into a script.";
                return false;
            }

            var path = Path.Combine(projectPath, call.AssetPath);
            var bytes = File.ReadAllBytes(path);
            var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            if (!hasBom && bytes.Length >= 2 && (bytes[0] == 0xFF || bytes[0] == 0xFE))
            {
                error = $"{call.AssetPath} isn't UTF-8, so the window left it alone.";
                return false;
            }

            var offset = hasBom ? 3 : 0;
            var text = new UTF8Encoding(false).GetString(bytes, offset, bytes.Length - offset);
            if (call.KeyStart + call.KeyLength > text.Length || string.CompareOrdinal(text, call.KeyStart, call.Key, 0, call.KeyLength) != 0)
            {
                error = $"{call.AssetPath} changed since the window looked at it. Press Refresh under Integration, then Apply again.";
                return false;
            }

            var updated = text.Substring(0, call.KeyStart) + newKey + text.Substring(call.KeyStart + call.KeyLength);
            var encoded = new UTF8Encoding(false).GetBytes(updated);
            File.WriteAllBytes(path, hasBom ? new byte[] { 0xEF, 0xBB, 0xBF }.Concat(encoded).ToArray() : encoded);
            return true;
        }

        /// <summary>The first argument: a string literal, or a constant of this file holding one.</summary>
        private static void ReadKey(string text, string code, (int Start, int End) argument, GRInitializeCall call)
        {
            var (start, written) = Trim(code, argument);
            var named = NamedArgument.Match(written);
            if (named.Success)
            {
                start += named.Length;
                (start, written) = Trim(code, (start, argument.End));
            }

            if (Literal.IsMatch(written))
            {
                SetKey(text, start, written, call);
                return;
            }

            if (Identifier.IsMatch(written))
            {
                // const string kAPI = "…"; or static readonly string kAPI = "…"; in the same file.
                // A plain field isn't followed: the Inspector's value would win over the code's.
                var declaration = new Regex(@"\b(?:const|static\s+readonly|readonly\s+static)\s+string\s+" + written + "\\s*=\\s*(@?\"x*\")\\s*;");
                var found = declaration.Matches(code);
                if (found.Count == 1)
                {
                    var literal = found[0].Groups[1];
                    SetKey(text, literal.Index, literal.Value, call);
                }
            }

            call.KeySource = call.Key == null ? (written.Length > 60 ? written.Substring(0, 57) + "…" : written) : Identifier.IsMatch(written) ? written : null;
        }

        private static void SetKey(string text, int literalStart, string literal, GRInitializeCall call)
        {
            var quote = literalStart + literal.IndexOf('"');
            var length = literal.Length - (quote - literalStart) - 2;
            var key = text.Substring(quote + 1, length);

            // Keys are plain hex; anything with escapes is left for the developer.
            if (key.IndexOf('\\') >= 0 || key.IndexOf('"') >= 0)
            {
                return;
            }

            call.Key = key;
            call.KeyStart = quote + 1;
            call.KeyLength = length;
            call.KeyLine = LineOf(text, quote);
        }

        /// <summary>Top-level argument spans of the call opening at <paramref name="open"/>; null if it never closes.</summary>
        private static List<(int Start, int End)> Arguments(string code, int open)
        {
            var arguments = new List<(int, int)>();
            var depth = 0;
            var start = open + 1;
            for (var i = open + 1; i < code.Length; i++)
            {
                switch (code[i])
                {
                    case '(':
                    case '[':
                    case '{':
                        depth++;
                        break;
                    case ')':
                    case ']':
                    case '}':
                        if (depth == 0)
                        {
                            if (code[i] != ')')
                            {
                                return null;
                            }

                            if (i > start && code.Substring(start, i - start).Trim().Length > 0)
                            {
                                arguments.Add((start, i));
                            }

                            return arguments;
                        }

                        depth--;
                        break;
                    case ',':
                        if (depth == 0)
                        {
                            arguments.Add((start, i));
                            start = i + 1;
                        }

                        break;
                }
            }

            return null;
        }

        private static (int Start, string Text) Trim(string code, (int Start, int End) span)
        {
            var start = span.Start;
            var end = span.End;
            while (start < end && char.IsWhiteSpace(code[start])) start++;
            while (end > start && char.IsWhiteSpace(code[end - 1])) end--;
            return (start, code.Substring(start, end - start));
        }

        private static int LineOf(string text, int index)
        {
            var line = 1;
            for (var i = 0; i < index; i++)
            {
                if (text[i] == '\n') line++;
            }

            return line;
        }

        private static bool IsIgnoredByUnity(string file, string assetsFolder)
        {
            // Unity skips folders ending in ~ and hidden ones; so does the scan.
            var relative = file.Substring(assetsFolder.Length).Split('/');
            return relative.Take(relative.Length - 1).Any(part => part.EndsWith("~", StringComparison.Ordinal) || part.StartsWith(".", StringComparison.Ordinal));
        }

        /// <summary>
        /// The text with the same length and line breaks, but comments and preprocessor
        /// lines turned to spaces and string and char contents to 'x'. Quotes and the
        /// @ and $ prefixes stay, so a literal is still recognisable.
        /// </summary>
        private static string Blank(string text)
        {
            var code = text.ToCharArray();
            var lineStart = true;
            var i = 0;
            while (i < text.Length)
            {
                var c = text[i];
                if (c == '\n')
                {
                    lineStart = true;
                    i++;
                    continue;
                }

                if (lineStart && char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                if (lineStart && c == '#')
                {
                    i = BlankUntilLineEnd(text, code, i);
                    continue;
                }

                lineStart = false;
                var next = i + 1 < text.Length ? text[i + 1] : '\0';
                if (c == '/' && next == '/')
                {
                    i = BlankUntilLineEnd(text, code, i);
                }
                else if (c == '/' && next == '*')
                {
                    var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    end = end < 0 ? text.Length : end + 2;
                    BlankRange(text, code, i, end, ' ');
                    i = end;
                }
                else if (c == '\'')
                {
                    var end = i + 1;
                    while (end < text.Length && text[end] != '\'' && text[end] != '\n')
                    {
                        end += text[end] == '\\' ? 2 : 1;
                    }

                    end = Math.Min(end, text.Length - 1);
                    BlankRange(text, code, i + 1, end, 'x');
                    i = end + 1;
                }
                else if (c == '"' || ((c == '@' || c == '$') && (next == '"' || ((next == '@' || next == '$') && i + 2 < text.Length && text[i + 2] == '"'))))
                {
                    var quote = text.IndexOf('"', i);
                    var prefix = text.Substring(i, quote - i);
                    var end = StringEnd(text, quote, prefix.Contains("@"), prefix.Contains("$"));
                    BlankRange(text, code, quote + 1, end, 'x');
                    i = end + 1;
                }
                else
                {
                    i++;
                }
            }

            return new string(code);
        }

        /// <summary>The index of the closing quote of the string opening at <paramref name="quote"/>.</summary>
        private static int StringEnd(string text, int quote, bool verbatim, bool interpolated)
        {
            var depth = 0;
            var i = quote + 1;
            while (i < text.Length)
            {
                var c = text[i];
                if (depth > 0)
                {
                    // Inside an interpolation hole: skip nested strings, track braces.
                    if (c == '"')
                    {
                        i = StringEnd(text, i, verbatim: false, interpolated: false) + 1;
                        continue;
                    }

                    if (c == '{') depth++;
                    else if (c == '}') depth--;
                    i++;
                    continue;
                }

                if (interpolated && c == '{')
                {
                    if (i + 1 < text.Length && text[i + 1] == '{')
                    {
                        i += 2;
                        continue;
                    }

                    depth = 1;
                    i++;
                    continue;
                }

                if (verbatim)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            i += 2;
                            continue;
                        }

                        return i;
                    }
                }
                else
                {
                    if (c == '\\')
                    {
                        i += 2;
                        continue;
                    }

                    if (c == '"' || c == '\n')
                    {
                        return i;
                    }
                }

                i++;
            }

            return text.Length - 1;
        }

        private static int BlankUntilLineEnd(string text, char[] code, int start)
        {
            var end = text.IndexOf('\n', start);
            end = end < 0 ? text.Length : end;
            BlankRange(text, code, start, end, ' ');
            return end;
        }

        private static void BlankRange(string text, char[] code, int start, int end, char fill)
        {
            for (var i = start; i < end && i < text.Length; i++)
            {
                if (text[i] != '\n' && text[i] != '\r')
                {
                    code[i] = fill;
                }
            }
        }
    }
}
