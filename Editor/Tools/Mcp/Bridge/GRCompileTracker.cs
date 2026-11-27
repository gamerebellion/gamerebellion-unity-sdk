using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace GameRebellionSdk.Unity.Editor
{
    [Serializable]
    [System.Reflection.Obfuscation(Exclude = true, ApplyToMembers = true)]
    internal sealed class GRCompileResult
    {
        public string finishedUtc;
        public int errorCount;
        public int warningCount;
        public List<string> errors = new List<string>();
        public List<string> warnings = new List<string>();
    }

    /// <summary>A unity_compile call in progress. Kept on disk because the compile it waits for reloads the domain.</summary>
    [Serializable]
    [System.Reflection.Obfuscation(Exclude = true, ApplyToMembers = true)]
    internal sealed class GRCompileJob
    {
        public string id;
        public string startedUtc;
        public bool compileStarted;
        public bool done;
        public string text;
        public bool isError;
    }

    /// <summary>
    /// Records every script compilation and drives unity_compile jobs.
    ///
    /// A job refreshes the AssetDatabase (Unity only notices the agent's edits on
    /// focus otherwise) and then waits for the compile that follows. The result is
    /// written when compilation finishes, before Unity reloads the domain, so the
    /// relay reads it from the new domain once the bridge is back.
    /// </summary>
    [InitializeOnLoad]
    internal static class GRCompileTracker
    {
        private const int MaxMessages = 50;

        // After a refresh, compilation starts within a frame or two. If none has started
        // after this long, nothing changed.
        private static readonly TimeSpan NoChangesGrace = TimeSpan.FromSeconds(3);

        private static GRCompileResult _current;

        static GRCompileTracker()
        {
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
            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompiled;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
            DeleteOldJobs();
        }

        /// <summary>A job only matters while the relay polls it; anything a day old is litter.</summary>
        private static void DeleteOldJobs()
        {
            foreach (var file in Directory.GetFiles(GRBridgePaths.JobsFolder, "*.json"))
            {
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > TimeSpan.FromDays(1))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (IOException)
                    {
                        // In use; the next load tries again.
                    }
                }
            }
        }

        /// <summary>Starts a compile job and returns its id.</summary>
        public static string StartJob()
        {
            var job = new GRCompileJob
            {
                id = Guid.NewGuid().ToString("N").Substring(0, 12),
                startedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            };
            Save(job);

            // May start compilation, which marks the job through OnCompilationStarted.
            AssetDatabase.Refresh();
            return job.id;
        }

        public static GRBridgeResponse GetJob(string id)
        {
            var job = Load(id);
            if (job == null)
            {
                return GRBridgeResponse.Failure($"No compile job {id}.");
            }

            if (job.done)
            {
                return GRBridgeResponse.Result(job.text, job.isError);
            }

            var started = DateTime.Parse(job.startedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            if (!job.compileStarted && !EditorApplication.isCompiling && DateTime.UtcNow - started > NoChangesGrace)
            {
                job.done = true;
                job.text = "No script changes to compile. " + LastCompileSummary();
                Save(job);
                return GRBridgeResponse.Result(job.text);
            }

            return GRBridgeResponse.Pending(job.id);
        }

        /// <summary>One line about the most recent compile this session, for unity_status.</summary>
        public static string LastCompileSummary()
        {
            var last = LoadLastCompile();
            return last == null
                ? "No compile recorded yet."
                : $"Last compile: {last.errorCount} errors, {last.warningCount} warnings, finished {last.finishedUtc} (UTC).";
        }

        private static void OnCompilationStarted(object context)
        {
            _current = new GRCompileResult();
            foreach (var job in RunningJobs().Where(job => !job.compileStarted))
            {
                job.compileStarted = true;
                Save(job);
            }
        }

        private static void OnAssemblyCompiled(string assemblyPath, CompilerMessage[] messages)
        {
            _current = _current ?? new GRCompileResult();
            foreach (var message in messages)
            {
                if (message.type == CompilerMessageType.Error)
                {
                    _current.errorCount++;
                    if (_current.errors.Count < MaxMessages)
                    {
                        _current.errors.Add(message.message);
                    }
                }
                else if (message.type == CompilerMessageType.Warning)
                {
                    _current.warningCount++;
                    if (_current.warnings.Count < MaxMessages)
                    {
                        _current.warnings.Add(message.message);
                    }
                }
            }
        }

        private static void OnCompilationFinished(object context)
        {
            var result = _current ?? new GRCompileResult();
            _current = null;
            result.finishedUtc = DateTime.UtcNow.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            WriteAtomically(GRBridgePaths.LastCompileFile, JsonUtility.ToJson(result, true));

            var text = Describe(result);
            foreach (var job in RunningJobs().Where(job => job.compileStarted))
            {
                job.done = true;
                job.text = text;
                Save(job);
            }
        }

        private static string Describe(GRCompileResult result)
        {
            var text = new StringBuilder();
            text.Append($"Compiled: {result.errorCount} errors, {result.warningCount} warnings.");
            if (result.errorCount == 0)
            {
                text.Append(" Unity is reloading scripts; the next call waits for it.");
            }

            AppendList(text, "Errors", result.errors, result.errorCount);
            AppendList(text, "Warnings", result.warnings, result.warningCount);
            return text.ToString();
        }

        private static void AppendList(StringBuilder text, string title, List<string> items, int total)
        {
            if (items.Count == 0)
            {
                return;
            }

            text.Append($"\n\n{title}{(total > items.Count ? $" (first {items.Count} of {total})" : string.Empty)}:");
            foreach (var item in items)
            {
                text.Append("\n- ").Append(item);
            }
        }

        // ---- Persistence ---------------------------------------------------------

        private static IEnumerable<GRCompileJob> RunningJobs()
        {
            if (!Directory.Exists(GRBridgePaths.JobsFolder))
            {
                yield break;
            }

            foreach (var file in Directory.GetFiles(GRBridgePaths.JobsFolder, "*.json"))
            {
                var job = Read<GRCompileJob>(file);
                if (job != null && !job.done)
                {
                    yield return job;
                }
            }
        }

        private static GRCompileJob Load(string id)
        {
            return string.IsNullOrEmpty(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id.Contains("..")
                ? null
                : Read<GRCompileJob>(Path.Combine(GRBridgePaths.JobsFolder, id + ".json"));
        }

        private static GRCompileResult LoadLastCompile() => Read<GRCompileResult>(GRBridgePaths.LastCompileFile);

        private static void Save(GRCompileJob job)
        {
            WriteAtomically(Path.Combine(GRBridgePaths.JobsFolder, job.id + ".json"), JsonUtility.ToJson(job, true));
        }

        private static T Read<T>(string path) where T : class
        {
            try
            {
                return File.Exists(path) ? JsonUtility.FromJson<T>(File.ReadAllText(path)) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void WriteAtomically(string path, string contents)
        {
            var temp = path + ".tmp";
            File.WriteAllText(temp, contents);
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(temp, path);
        }
    }
}
