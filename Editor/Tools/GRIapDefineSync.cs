using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using GameRebellionSdk.Unity;

using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace GameRebellionSdk.Unity.Editor
{
    /// <summary>
    /// Keeps the GR_IAP_AUTOTRACK scripting define in step with the
    /// "Auto-track purchases" tick-box in GameRebellionSettings.
    ///
    /// The setting is consumed here and nowhere else: runtime code never reads
    /// it. With the define absent, the observer, its P/Invoke declarations, the
    /// Swift plugin, the Play Billing AAR and its Gradle dependency are all
    /// excluded from the build rather than compiled and branched around -- so a developer who leaves the box unticked carries no
    /// IL2CPP weight, no extra symbols, and no chance of the feature switching
    /// itself on.
    ///
    /// The define is written when the asset changes, never during a build.
    /// Mutating PlayerSettings from IPreprocessBuildWithReport can trigger a
    /// domain reload mid-build and leaves the project dirty afterwards.
    /// </summary>
    internal sealed class GRIapDefineSync : AssetPostprocessor
    {
        private const string Define = "GR_IAP_AUTOTRACK";
        private const string SettingsAssetName = "GameRebellionSettings";

        /// <summary>
        /// Targets the define is maintained for: the platforms with a native store
        /// observer -- StoreKit 2 on iOS, Play Billing on Android.
        /// </summary>
        private static readonly NamedBuildTarget[] Targets =
        {
            NamedBuildTarget.iOS,
            NamedBuildTarget.Android
        };

        private static void OnPostprocessAllAssets(
            string[] imported,
            string[] deleted,
            string[] movedTo,
            string[] movedFrom)
        {
            // Old editor scripts in the project keep the define in sync themselves (GRLegacySources).
            if (GRLegacySources.Found)
            {
                return;
            }

            var touched = imported.Concat(deleted).Concat(movedTo).Concat(movedFrom);

            foreach (var path in touched)
            {
                if (!path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) continue;

                // A deleted asset can no longer be loaded and typed, so it is
                // matched by name instead. Everything else is checked by type,
                // rather than re-syncing on every .asset in the project.
                var isSettings = AssetDatabase.LoadAssetAtPath<GameRebellionSettings>(path) != null;
                var wasSettings = Path.GetFileNameWithoutExtension(path) == SettingsAssetName;

                if (isSettings || wasSettings)
                {
                    Sync();
                    return;
                }
            }
        }

        /// <summary>
        /// Re-reads the settings and rewrites the define. Only needed when the
        /// asset was changed outside Unity -- a branch switch, a merge -- since
        /// the postprocessor covers every edit made in the editor.
        ///
        /// Deliberately not a toggle. The define has exactly one source of truth,
        /// the tick-box; a menu item that set it directly would be a second one,
        /// and the two would disagree on the next domain reload.
        /// </summary>
        [MenuItem("Tools/GameRebellion/Re-sync purchase tracking", priority = 100)]
        private static void SyncFromMenu()
        {
            Sync();

            Debug.Log(IsEnabled()
                ? "[GRC] Purchase auto-tracking is ON for iOS and Android."
                : "[GRC] Purchase auto-tracking is OFF. Tick 'Auto-track purchases' on a " +
                  "GameRebellionSettings asset you can edit. Installed from a git URL? The " +
                  "bundled asset is read-only -- create your own via " +
                  "Assets > Create > GameRebellion > Settings.");
        }

        private static bool IsEnabled()
        {
            return IsEnabledFor(Targets[0]);
        }

        /// <summary>
        /// Whether the define is set for one build target. Read from PlayerSettings
        /// rather than from <c>#if</c>, so it answers for any target, not just the
        /// one the editor assemblies were compiled for.
        /// </summary>
        internal static bool IsEnabledFor(NamedBuildTarget target)
        {
            try
            {
                return PlayerSettings.GetScriptingDefineSymbols(target)
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Any(d => d.Trim() == Define);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Also runs on domain reload, so a fresh clone or a package upgrade lands
        /// on the right define without anyone having to re-save the asset.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void SyncOnLoad()
        {
            if (GRLegacySources.Found)
            {
                return;
            }

            // Deferred: asset loading during a domain reload is not reliable, and
            // an unnecessary PlayerSettings write here would reload again.
            EditorApplication.delayCall += Sync;
        }

        /// <summary>
        /// Every GameRebellionSettings asset in the project is consulted, not just
        /// the one Resources.Load happens to resolve. A package installed from a
        /// git URL ships its asset read-only, and the documented way to customize
        /// is to create your own -- so the project can legitimately hold two, and
        /// the one the developer can actually edit is the one they will tick.
        /// </summary>
        internal static void Sync()
        {
            var wanted = false;

            foreach (var guid in AssetDatabase.FindAssets("t:GameRebellionSettings"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var settings = AssetDatabase.LoadAssetAtPath<GameRebellionSettings>(path);

                if (settings != null && settings.AutoTrackPurchases)
                {
                    wanted = true;
                    break;
                }
            }

            foreach (var target in Targets)
            {
                Apply(target, wanted);
            }
        }

        private static void Apply(NamedBuildTarget target, bool wanted)
        {
            string existing;
            try
            {
                existing = PlayerSettings.GetScriptingDefineSymbols(target);
            }
            catch (Exception)
            {
                // A build target whose module is not installed. Nothing to do, and
                // certainly nothing worth an error in the console.
                return;
            }

            var defines = new List<string>(
                existing.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(d => d.Trim())
                        .Where(d => d.Length > 0));

            var present = defines.Contains(Define);
            if (present == wanted) return;

            if (wanted)
            {
                defines.Add(Define);
            }
            else
            {
                defines.RemoveAll(d => d == Define);
            }

            PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defines));

            Debug.Log(wanted
                ? $"[GRC] Purchase auto-tracking enabled for {target.TargetName}. Rebuild to pick it up."
                : $"[GRC] Purchase auto-tracking disabled for {target.TargetName}. Rebuild to pick it up.");
        }
    }
}
