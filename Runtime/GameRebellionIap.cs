using System;
using UnityEngine;

namespace GameRebellionSdk.Unity
{
    /// <summary>
    /// Automatic in-app purchase reporting.
    ///
    /// With "Auto-track purchases" ticked in GameRebellionSettings, the SDK
    /// observes the platform store directly -- StoreKit 2 on iOS, Google Play
    /// Billing on Android -- and reports every purchase with no per-purchase game
    /// code. Nothing else is required: on Android the observer starts before the
    /// first scene loads, on iOS <c>GameRebellion.Initialize</c> starts it. The
    /// methods here exist for games that want explicit control over when
    /// observation runs.
    ///
    /// Everything in this class is inert unless the tick-box is on. The type and
    /// its methods are always compiled, though: unticking the box must not turn a
    /// customer's working call site into a compile error.
    /// </summary>
    public static class GameRebellionIap
    {
        /// <summary>
        /// True when this build can observe purchases: the setting is on, the
        /// platform has an observer, and the device can run it (iOS 15 for
        /// StoreKit 2; Play Billing present in the build on Android).
        /// </summary>
        public static bool IsSupported
        {
            get
            {
#if GR_IAP_AUTOTRACK && (UNITY_IOS || UNITY_ANDROID) && !UNITY_EDITOR
                return GrIapBridge.IsSupported;
#else
                return false;
#endif
            }
        }

        /// <summary>True once the store observer is running.</summary>
        public static bool IsObserving
        {
#if GR_IAP_AUTOTRACK && (UNITY_IOS || UNITY_ANDROID) && !UNITY_EDITOR
            get { return GrIapBridge.IsObserving; }
#else
            get { return false; }
#endif
        }

        /// <summary>
        /// Starts observing the store. Idempotent. Observation starts on its own --
        /// before the first scene on Android, after a successful
        /// <c>GameRebellion.Initialize</c> on iOS; call this yourself only if you
        /// stopped observation and want it back.
        /// </summary>
        public static void StartObservingTransactions()
        {
#if GR_IAP_AUTOTRACK && (UNITY_IOS || UNITY_ANDROID) && !UNITY_EDITOR
            GrIapBridge.Start();
#else
            Debug.LogWarning(
                "[GRC] Purchase auto-tracking is not enabled in this build. " +
                "Tick 'Auto-track purchases' in GameRebellionSettings and rebuild, " +
                "or keep calling GameRebellion.TrackTransaction yourself.");
#endif
        }

        /// <summary>
        /// Stops observing. The game's own store code is untouched -- this only
        /// detaches GameRebellion's listener.
        /// </summary>
        public static void StopObservingTransactions()
        {
#if GR_IAP_AUTOTRACK && (UNITY_IOS || UNITY_ANDROID) && !UNITY_EDITOR
            GrIapBridge.Stop();
#endif
        }

        /// <summary>
        /// Called by GameRebellion.Initialize. Separate from the public start so
        /// that the automatic path can stay silent when the feature is off, while
        /// an explicit call still explains why nothing happened.
        /// </summary>
        internal static void AutoStart()
        {
#if GR_IAP_AUTOTRACK && (UNITY_IOS || UNITY_ANDROID) && !UNITY_EDITOR
            GrIapBridge.Start();
#endif
        }
    }
}
