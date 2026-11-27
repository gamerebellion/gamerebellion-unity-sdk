#if GR_IAP_AUTOTRACK && (UNITY_IOS || UNITY_ANDROID) && !UNITY_EDITOR
using System;
using System.Collections.Concurrent;
#if UNITY_ANDROID
using System.Globalization;
#else
using System.Runtime.InteropServices;
#endif
using UnityEngine;
using UnityEngine.Scripting;

namespace GameRebellionSdk.Unity
{
    /// <summary>
    /// Carries purchases from the native store observer into the core: StoreKit 2
    /// on iOS, Play Billing on Android.
    ///
    /// Neither store delivers on Unity's main thread -- StoreKit calls back on a
    /// background thread, Play Billing on the Android main thread -- so nothing
    /// here touches the core from the callback itself: the document is copied into
    /// a queue and replayed on the main thread by <see cref="GRIapPump"/>. The cost
    /// is a frame of latency on an event that is already asynchronous; the
    /// alternative is reasoning about Unity's threading rules from inside somebody
    /// else's dispatch queue.
    ///
    /// Everything the purchase then goes through -- duplicate suppression against
    /// earlier launches, the pre-init outbox, failures to the log channel, the
    /// diagnostics -- lives in the core, shared with the Unreal host. This file is
    /// a transport and nothing more.
    /// </summary>
    internal static class GrIapBridge
    {
        private static readonly ConcurrentQueue<string> Inbox = new ConcurrentQueue<string>();
        private static readonly object StartLock = new object();

        private static bool _observing;
        private static bool _drained;

        internal static bool IsObserving
        {
            get { lock (StartLock) { return _observing; } }
        }

        /// <summary>True when this device and build can observe purchases.</summary>
        internal static bool IsSupported
        {
            get
            {
                try
                {
                    return PlatformIsSupported();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[GRC] IAP support probe failed: {ex.Message}");
                    return false;
                }
            }
        }

        internal static void Start()
        {
            lock (StartLock)
            {
                if (_observing) return;

                try
                {
                    if (!PlatformStart()) return;
                    _observing = true;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[GRC] Failed to start the store observer: {ex.Message}");
                    return;
                }
            }

            GRIapPump.Ensure();
            Debug.Log($"[GRC] Observing {StoreName} purchases.");
        }

        internal static void Stop()
        {
            lock (StartLock)
            {
                if (!_observing) return;

                try
                {
                    PlatformStop();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[GRC] Failed to stop the store observer: {ex.Message}");
                }

                _observing = false;
            }
        }

        /// <summary>
        /// Queues one observer document. Safe from any thread; called from the
        /// native callback, so it must never throw.
        /// </summary>
        private static void Enqueue(string json)
        {
            try
            {
                if (!string.IsNullOrEmpty(json))
                {
                    Inbox.Enqueue(json);
                }
            }
            catch
            {
                // Deliberately silent: there is no safe way to log from here, and
                // the player has their item either way.
            }
        }

        /// <summary>
        /// Drains queued purchases on the main thread, once per frame. Before the
        /// SDK is initialized the core parks each purchase in its outbox, which
        /// Initialize drains; a failed purchase flow that early is dropped, as
        /// every IAP diagnostic is.
        /// </summary>
        internal static void Pump()
        {
            string json;
            while (Inbox.TryDequeue(out json))
            {
                try
                {
                    var result = global::GameRebellionSdk.Core.GameRebellionUnityAPI.TrackStorePurchase(json);
                    LogResult(result);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[GRC] Failed to report an observed purchase: {ex.Message}");
                }
            }

            // One drain of the core's outbox once a session exists, for purchases
            // that arrived on a previous launch before the SDK was ready.
            if (!_drained && GameRebellion.IsInitialized && GameRebellion.GetState() == GameRebellion.SdkState.Ready)
            {
                _drained = true;

                try
                {
                    var sent = global::GameRebellionSdk.Core.GameRebellionUnityAPI.DrainPendingStorePurchases();
                    if (sent > 0)
                    {
                        Debug.Log($"[GRC] Reported {sent} purchase(s) held until the SDK was ready.");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[GRC] Failed to drain held purchases: {ex.Message}");
                }
            }
        }

        private static void LogResult(int result)
        {
            switch (result)
            {
                case 0:
                    return;
                case 1:
                    // The store re-announces existing purchases on every launch;
                    // suppressing them is the ledger working, not a fault.
                    return;
                case 2:
                    Debug.Log("[GRC] Purchase arrived before the SDK was ready; held until it is.");
                    return;
                case 3:
                    // A failed purchase flow, sent to the log channel by the core.
                    return;
                case -2:
                    Debug.LogWarning("[GRC] Ignored a purchase the store described without a currency.");
                    return;
                default:
                    Debug.LogWarning($"[GRC] Failed to report an observed purchase (code {result}).");
                    return;
            }
        }

#if UNITY_IOS
        private const string StoreName = "App Store";

        // The delegate is handed to native code, so a managed reference has to
        // outlive the call that registers it or the GC will collect the thunk out
        // from under StoreKit.
        private static GrIapNative.GrPurchaseCallback _callback;

        private static bool PlatformIsSupported()
        {
            return GrIapNative.gr_ios_iap_is_supported() == 1;
        }

        private static bool PlatformStart()
        {
            if (GrIapNative.gr_ios_iap_is_supported() != 1)
            {
                Debug.LogWarning(
                    "[GRC] Purchase auto-tracking needs iOS 15 or newer (StoreKit 2). " +
                    "Purchases on this device will only be reported if the game calls " +
                    "TrackTransaction itself.");
                return false;
            }

            _callback = OnNativePurchase;
            GrIapNative.gr_ios_iap_set_callback(_callback);

            if (GrIapNative.gr_ios_iap_start_observer() != 1)
            {
                Debug.LogWarning("[GRC] StoreKit observer refused to start.");
                return false;
            }

            return true;
        }

        private static void PlatformStop()
        {
            GrIapNative.gr_ios_iap_stop_observer();
            GrIapNative.gr_ios_iap_set_callback(null);
            _callback = null;
        }

        /// <summary>
        /// The native entry point. Runs on StoreKit's thread, so it does the least
        /// possible: copy the string and return. A throw here would surface inside
        /// the app's own transaction handling, so nothing is allowed to escape.
        /// </summary>
        [AOT.MonoPInvokeCallback(typeof(GrIapNative.GrPurchaseCallback))]
        private static void OnNativePurchase(IntPtr purchaseJson)
        {
            try
            {
                if (purchaseJson == IntPtr.Zero) return;
                Enqueue(ReadUtf8(purchaseJson));
            }
            catch
            {
                // Deliberately silent: see Enqueue.
            }
        }

        /// <summary>
        /// Reads a NUL-terminated UTF-8 string. Not PtrToStringAnsi, which mangles
        /// any non-ASCII byte -- a localized product description would come through
        /// corrupted, and it would look like a backend problem.
        /// </summary>
        private static string ReadUtf8(IntPtr pointer)
        {
            var length = 0;
            while (Marshal.ReadByte(pointer, length) != 0)
            {
                length++;
            }

            if (length == 0) return string.Empty;

            var bytes = new byte[length];
            Marshal.Copy(pointer, bytes, 0, length);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
#elif UNITY_ANDROID
        private const string StoreName = "Google Play";
        private const string ObserverClass = "com.gamerebellion.iap.GRPlayBillingObserver";
        private const string ListenerInterface = "com.gamerebellion.iap.GRPurchaseListener";

        // Java holds this proxy for as long as it observes; keeping the managed
        // side alive too stops it being collected while Play Billing still calls it.
        private static PlayBillingListener _listener;

        /// <summary>
        /// Starts observing before the first scene loads rather than at Initialize.
        ///
        /// On Android the consumed-purchase hole is mostly a race at launch: the
        /// game's billing library fetches owned purchases and consumes them, and a
        /// consumed purchase never shows up in our backstop query. A connection
        /// opened before any scene's Awake wins most of that race. Anything that
        /// arrives before Initialize goes to the core's outbox -- which is why the
        /// core is pointed at its data directory here too, before the first
        /// document can reach the ledger.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void StartBeforeFirstScene()
        {
            try
            {
                global::GameRebellionSdk.Core.GameRebellionUnityAPI.SetCacheDirectory(Application.persistentDataPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GRC] Could not set the SDK data directory early: {ex.Message}");
                return;
            }

            Start();
        }

        private static bool PlatformIsSupported()
        {
            using (var observer = new AndroidJavaClass(ObserverClass))
            {
                return observer.CallStatic<bool>("isSupported");
            }
        }

        private static bool PlatformStart()
        {
            using (var observer = new AndroidJavaClass(ObserverClass))
            {
                if (!observer.CallStatic<bool>("isSupported"))
                {
                    Debug.LogWarning(
                        "[GRC] GR_IAP_BILLING_MISSING: Google Play Billing is not in this build, so " +
                        "purchases cannot be observed. The GameRebellion editor adds it when " +
                        "'Auto-track purchases' is on -- check that the Gradle dependency survived " +
                        "any custom build templates.");
                    return false;
                }

                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    if (activity == null)
                    {
                        Debug.LogWarning("[GRC] No current activity; cannot start the Play Billing observer.");
                        return false;
                    }

                    var listener = new PlayBillingListener();
                    var result = observer.CallStatic<int>("start", activity, listener, FirstLaunchEpochMillis());
                    if (result != 1)
                    {
                        Debug.LogWarning($"[GRC] Play Billing observer refused to start (code {result}).");
                        return false;
                    }

                    _listener = listener;
                    return true;
                }
            }
        }

        private static void PlatformStop()
        {
            using (var observer = new AndroidJavaClass(ObserverClass))
            {
                observer.CallStatic("stop");
            }

            _listener = null;
        }

        /// <summary>
        /// This install's first launch, for the observer's restore rule: a purchase
        /// older than the install is a restore, not a sale. Read here, before the
        /// observer's first query, so the marker exists by the time a purchase made
        /// in this very launch could be compared against it.
        /// </summary>
        private static long FirstLaunchEpochMillis()
        {
            try
            {
                var stamp = global::GameRebellionSdk.Core.Adapters.FirstLaunch.Timestamp();
                if (DateTimeOffset.TryParse(stamp, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
                {
                    return parsed.ToUnixTimeMilliseconds();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GRC] Could not read the first-launch time: {ex.Message}");
            }

            // Unknown: every purchase is reported as a sale, never as a restore.
            return 0;
        }

        /// <summary>
        /// Implements the observer's Java listener. Play Billing calls it on the
        /// Android main thread, so it only queues and returns.
        /// </summary>
        [Preserve]
        private sealed class PlayBillingListener : AndroidJavaProxy
        {
            [Preserve]
            public PlayBillingListener() : base(ListenerInterface)
            {
            }

            [Preserve]
            public void onStoreDocument(string json)
            {
                Enqueue(json);
            }

            [Preserve]
            public void onDiagnostic(string code, string message)
            {
                try
                {
                    Debug.LogWarning($"[GRC] {code}: {message}");
                }
                catch
                {
                    // Nothing may escape into Play Billing's delivery.
                }
            }
        }
#endif
    }

    /// <summary>
    /// Gives the bridge a main-thread tick. Auto-created and hidden, the same way
    /// the metrics collector is.
    /// </summary>
    internal sealed class GRIapPump : MonoBehaviour
    {
        private const string ObjectName = "GameRebellionIap";

        private static GRIapPump _instance;

        internal static void Ensure()
        {
            if (_instance != null) return;

            var obj = new GameObject(ObjectName);
            obj.hideFlags = HideFlags.HideInHierarchy;
            DontDestroyOnLoad(obj);
            _instance = obj.AddComponent<GRIapPump>();
        }

        private void Update()
        {
            GrIapBridge.Pump();
        }
    }
}
#endif
