#if UNITY_EDITOR
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using UnityEngine;

// GameRebellion.EditorTools is the editor code. GameRebellion.Editor holds the
// platform hooks, and the old editor scripts an upgrade over a source version
// leaves behind, which must still compile (see GRLegacySources).
[assembly: InternalsVisibleTo("GameRebellion.EditorTools")]
[assembly: InternalsVisibleTo("GameRebellion.Editor")]

namespace GameRebellionSdk.Unity
{
    /// <summary>One Track call as the Editor saw it. Fields, for JsonUtility.</summary>
    [Serializable]
    internal sealed class GrCapturedEvent
    {
        /// <summary>Local time, HH:mm:ss.fff.</summary>
        public string time = "";

        /// <summary>Time.frameCount, or -1 when the call came from another thread.</summary>
        public int frame;

        public string name = "";

        /// <summary>The fields the game set, as Key=Value; a custom event's JSON as it was passed.</summary>
        public string payload = "";

        /// <summary>The first project script line below the SDK, e.g. Assets/Scripts/Boot.cs:34.</summary>
        public string site = "";

        /// <summary>Up to six project frames, innermost first, one per line.</summary>
        public string stack = "";

        /// <summary>Why the SDK dropped or rejected it; empty when it was sent on.</summary>
        public string problem = "";
    }

    /// <summary>
    /// Editor only: records every GameRebellion Track call, sent or dropped, for the
    /// GameRebellion window's Live events tab. Nothing here goes anywhere: the Editor
    /// adapter sends no data. Kept in memory and in Library/GameRebellion/capture.jsonl,
    /// so a script reload doesn't lose it. Cleared when play mode starts, and by the
    /// window's Clear button. Safe to call from any thread.
    /// </summary>
    internal static class GrEditorEventCapture
    {
        private const int MaxEvents = 2000;
        private const int SiteFrames = 6;

        private static readonly object Gate = new object();
        private static readonly List<GrCapturedEvent> Events = new List<GrCapturedEvent>();
        private static readonly Assembly SdkAssembly = typeof(GrEditorEventCapture).Assembly;

        // The Editor's working directory is the project folder, and reading it is
        // thread-safe, unlike Application.dataPath.
        private static readonly string ProjectRoot = Directory.GetCurrentDirectory().Replace('\\', '/');
        private static readonly string FilePath = Path.Combine(ProjectRoot, "Library", "GameRebellion", "capture.jsonl");

        // Unity's asset import workers load Editor code too, but never play the game;
        // recording there would only race the Editor over the same file.
        private static readonly bool IsImportWorker =
            Environment.GetCommandLineArgs().Any(arg => arg.IndexOf("AssetImportWorker", StringComparison.Ordinal) >= 0);

        private static bool _loaded;
        private static int _linesInFile;
        private static int _version;

        /// <summary>Bumped by every change, so the window can tell cheaply when to redraw.</summary>
        public static int Version => Volatile.Read(ref _version);

        public static GrCapturedEvent[] Snapshot()
        {
            lock (Gate)
            {
                LoadOnce();
                return Events.ToArray();
            }
        }

        public static void Clear()
        {
            lock (Gate)
            {
                Events.Clear();
                _loaded = true;
                _linesInFile = 0;
                TryWrite(() => File.Delete(FilePath));
                Interlocked.Increment(ref _version);
            }
        }

        public static void Record(string name, object? payload, string? problem)
        {
            if (IsImportWorker)
            {
                return;
            }

            var (site, stack) = CallSite();
            var entry = new GrCapturedEvent
            {
                time = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
                frame = Frame(),
                name = name,
                payload = Describe(payload),
                site = site,
                stack = stack,
                problem = problem ?? "",
            };

            lock (Gate)
            {
                LoadOnce();
                Events.Add(entry);
                if (Events.Count > MaxEvents)
                {
                    Events.RemoveRange(0, Events.Count - MaxEvents);
                }

                // Appended line by line; rewritten from memory once it grows well past the cap.
                if (_linesInFile >= MaxEvents + MaxEvents / 4)
                {
                    TryWrite(() => File.WriteAllLines(FilePath, Events.Select(captured => JsonUtility.ToJson(captured))));
                    _linesInFile = Events.Count;
                }
                else
                {
                    TryWrite(() => File.AppendAllText(FilePath, JsonUtility.ToJson(entry) + "\n"));
                    _linesInFile++;
                }

                Interlocked.Increment(ref _version);
            }
        }

        // Runs as play mode starts, with or without a domain reload: each session starts a new list.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void OnPlayModeStarting()
        {
            if (!IsImportWorker)
            {
                Clear();
            }
        }

        /// <summary>After a script reload, picks up what the file kept.</summary>
        private static void LoadOnce()
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            try
            {
                if (!File.Exists(FilePath))
                {
                    return;
                }

                foreach (var line in File.ReadAllLines(FilePath))
                {
                    if (line.Length > 0)
                    {
                        Events.Add(JsonUtility.FromJson<GrCapturedEvent>(line));
                    }
                }

                _linesInFile = Events.Count;
                if (Events.Count > MaxEvents)
                {
                    Events.RemoveRange(0, Events.Count - MaxEvents);
                }
            }
            catch (Exception)
            {
                // A damaged capture file only loses the old list.
                Events.Clear();
            }
        }

        private static void TryWrite(Action write)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                write();
            }
            catch (IOException)
            {
                // The list in memory still has it; only a reload would lose it.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static int Frame()
        {
            try
            {
                return Time.frameCount;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        /// <summary>The frames below the SDK that have a source file: the game's own code.</summary>
        private static (string Site, string Stack) CallSite()
        {
            var frames = new StackTrace(2, true).GetFrames() ?? Array.Empty<StackFrame>();
            var project = frames
                .Where(frame => frame.GetMethod()?.DeclaringType?.Assembly != SdkAssembly && !string.IsNullOrEmpty(frame.GetFileName()))
                .Take(SiteFrames)
                .Select(frame => (Path: Relative(frame.GetFileName()!), Line: frame.GetFileLineNumber(), Method: frame.GetMethod()))
                .ToList();
            if (project.Count == 0)
            {
                return ("", "");
            }

            var site = $"{project[0].Path}:{project[0].Line}";
            var stack = string.Join("\n", project.Select(frame => $"{frame.Method?.DeclaringType?.Name}.{frame.Method?.Name} ({frame.Path}:{frame.Line})"));
            return (site, stack);
        }

        private static string Relative(string file)
        {
            file = file.Replace('\\', '/');
            return file.StartsWith(ProjectRoot + "/", StringComparison.OrdinalIgnoreCase) ? file.Substring(ProjectRoot.Length + 1) : file;
        }

        /// <summary>The fields that are set, Key=Value, in declaration order.</summary>
        private static string Describe(object? payload)
        {
            switch (payload)
            {
                case null:
                    return "";
                case string text:
                    return text.Length > 500 ? text.Substring(0, 500) + "…" : text;
            }

            var parts = payload.GetType()
                .GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Select(field => (field.Name, Value: field.GetValue(payload)))
                .Where(pair => pair.Value != null)
                .Select(pair => $"{pair.Name}={Format(pair.Value!)}");
            return string.Join("  ", parts);
        }

        private static string Format(object value)
        {
            switch (value)
            {
                case double number: return number.ToString("G", CultureInfo.InvariantCulture);
                case float number: return number.ToString("G", CultureInfo.InvariantCulture);
                case bool flag: return flag ? "true" : "false";
                case string text: return text.IndexOf(' ') >= 0 ? $"\"{text}\"" : text;
                case IFormattable formattable: return formattable.ToString(null, CultureInfo.InvariantCulture);
                default: return value.ToString() ?? "";
            }
        }
    }
}
#endif
