using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace GameRebellionSdk.Unity.Editor
{
    /// <summary>
    /// Keeps the native purchase observers out of the build unless purchase
    /// auto-tracking is on: the StoreKit observer (Swift) on iOS, the Play Billing
    /// observer (AAR) on Android.
    ///
    /// Their importers carry a GR_IAP_AUTOTRACK define constraint, but Unity does
    /// not apply define constraints to native plugins -- ShouldIncludeInBuild()
    /// returns true for both with the define absent, and the AAR lands in the
    /// Gradle project regardless. An include-in-build delegate is the supported
    /// way to make that decision, so it is made here, against the same per-target
    /// define GRIapDefineSync maintains.
    ///
    /// Found by GUID rather than path, so it works both from Assets/ and from the
    /// UPM package. The delegate is not serialized, hence registering on every
    /// domain load.
    /// </summary>
    [InitializeOnLoad]
    internal static class GRIapPluginGate
    {
        private const string PlayBillingAarGuid = "9140db63aa354d80b34bcb88a4947d9b";
        private const string StoreKitObserverGuid = "cd1d6385bf9f4733a3063c76ddb43e06";

        static GRIapPluginGate()
        {
            // Old editor scripts in the project gate the plugins themselves (GRLegacySources).
            if (GRLegacySources.Found)
            {
                return;
            }

            GateAll();

            // Again once the asset database has settled: on a fresh import the
            // GUIDs may not resolve yet during the static constructor.
            EditorApplication.delayCall += GateAll;
        }

        private static void GateAll()
        {
            Gate(PlayBillingAarGuid, NamedBuildTarget.Android);
            Gate(StoreKitObserverGuid, NamedBuildTarget.iOS);
        }

        private static void Gate(string guid, NamedBuildTarget target)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) return;

            if (AssetImporter.GetAtPath(path) is PluginImporter importer)
            {
                importer.SetIncludeInBuildDelegate(_ => GRIapDefineSync.IsEnabledFor(target));
            }
            else
            {
                Debug.LogWarning($"[GRC] {path} is not a plugin; purchase auto-tracking cannot gate it.");
            }
        }
    }
}
