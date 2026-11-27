using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace GameRebellionSdk.Unity.Editor
{
    /// <summary>
    /// Runs commands through the developer's login shell, so tools installed in
    /// their own PATH (the claude CLI, node, Homebrew) are found even though the
    /// Editor was started from the Dock with a minimal environment. Blocking: call
    /// it off the main thread.
    ///
    /// Values such as paths never become shell code: <see cref="RunProgram"/> hands
    /// each argument to the shell as an environment variable, which it expands once,
    /// inside double quotes. A project folder named <c>Game $(rm -rf ~)</c> is then
    /// only a folder name.
    /// </summary>
    internal static class GRShell
    {
        // Lower case: some runtimes lower-case the keys of ProcessStartInfo.EnvironmentVariables.
        private const string ArgumentVariable = "gamerebellion_arg_";

        // Safe bare in sh, zsh and cmd alike: no %, which cmd expands, and no = or , which it splits on.
        private static readonly Regex PlainWord = new Regex(@"^[A-Za-z0-9_@+:./-]+$");

        public readonly struct Result
        {
            public Result(int exitCode, string output, string error)
            {
                ExitCode = exitCode;
                Output = output;
                Error = error;
            }

            public int ExitCode { get; }
            public string Output { get; }
            public string Error { get; }
        }

        private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        /// <summary>
        /// Runs <paramref name="program"/> with <paramref name="arguments"/>. The shell only
        /// finds the program on the developer's PATH; it never parses an argument.
        /// </summary>
        /// <param name="program">A command name looked up on PATH ("claude"), or a full path. On Windows, a command name only.</param>
        public static Result RunProgram(string program, IReadOnlyList<string> arguments, string workingDirectory, int timeoutMs)
        {
            var values = new List<string> { program };
            values.AddRange(arguments);

            var script = new StringBuilder();
            if (IsWindows)
            {
                // cmd strips quotes from a line that starts with one, so the program goes in bare.
                if (!PlainWord.IsMatch(program))
                {
                    throw new ArgumentException($"'{program}' isn't a plain command name.", nameof(program));
                }

                script.Append(program);
                for (var i = 1; i < values.Count; i++)
                {
                    // Windows file names can't contain a quote; anything that does would end the quoted %variable%.
                    if (values[i].IndexOf('"') >= 0)
                    {
                        throw new ArgumentException($"Argument {i} contains a double quote.", nameof(arguments));
                    }

                    // An empty variable is unset on Windows, and cmd would print its name instead.
                    script.Append(values[i].Length == 0 ? " \"\"" : $" \"%{ArgumentVariable}{i}%\"");
                }
            }
            else
            {
                script.Append("exec");
                for (var i = 0; i < values.Count; i++)
                {
                    script.Append($" \"${ArgumentVariable}{i}\"");
                }
            }

            var environment = new Dictionary<string, string>();
            for (var i = 0; i < values.Count; i++)
            {
                environment[ArgumentVariable + i] = values[i];
            }

            return Start(script.ToString(), environment, Display(program, arguments), workingDirectory, timeoutMs);
        }

        /// <summary>
        /// Runs a fixed command line, such as <c>command -v claude</c>. Never build one from a
        /// path, a name or any other value: pass those to <see cref="RunProgram"/>.
        /// </summary>
        public static Result Run(string command, string workingDirectory, int timeoutMs) =>
            Start(command, null, command, workingDirectory, timeoutMs);

        /// <summary>
        /// The command as a developer would type it in their terminal: arguments that need it
        /// are single-quoted on macOS and Linux, so <c>$(…)</c> in a path stays text when pasted;
        /// double-quoted on Windows, where file names can't contain a quote.
        /// </summary>
        public static string Display(string program, IEnumerable<string> arguments) =>
            string.Join(" ", new[] { program }.Concat(arguments).Select(Quote));

        private static string Quote(string value)
        {
            if (value.Length > 0 && PlainWord.IsMatch(value))
            {
                return value;
            }

            return IsWindows ? "\"" + value + "\"" : "'" + value.Replace("'", "'\\''") + "'";
        }

        private static Result Start(string script, IDictionary<string, string> environment, string label, string workingDirectory, int timeoutMs)
        {
            var shell = IsWindows ? "cmd.exe" : File.Exists("/bin/zsh") ? "/bin/zsh" : "/bin/bash";
            var shellArguments = IsWindows ? "/c " + script : "-lc \"" + script.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

            var start = new ProcessStartInfo(shell, shellArguments)
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            if (environment != null)
            {
                foreach (var pair in environment)
                {
                    start.EnvironmentVariables[pair.Key] = pair.Value;
                }
            }

            using (var process = Process.Start(start))
            {
                if (process == null)
                {
                    return new Result(-1, string.Empty, $"Couldn't start {shell}.");
                }

                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(timeoutMs))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (Exception)
                    {
                        // Exited in the meantime.
                    }

                    return new Result(-1, string.Empty, $"'{label}' timed out after {timeoutMs / 1000} s.");
                }

                return new Result(process.ExitCode, output.Result, error.Result);
            }
        }
    }
}
