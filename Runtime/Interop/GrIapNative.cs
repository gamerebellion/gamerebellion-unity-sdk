#if GR_IAP_AUTOTRACK && UNITY_IOS && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;

namespace GameRebellionSdk.Unity
{
    /// <summary>
    /// P/Invoke surface for the StoreKit observer in GRStoreKitObserver.swift.
    ///
    /// Compiled only into iOS player builds with auto-tracking enabled. IL2CPP
    /// emits a C++ extern stub for every DllImport it sees called, and those stubs
    /// have to be satisfied by the linker -- so on any other platform, or with the
    /// feature off, these declarations must not exist at all rather than merely go
    /// unused.
    /// </summary>
    internal static class GrIapNative
    {
        /// <summary>
        /// Receives one flat JSON purchase document. Called on a background thread
        /// by StoreKit, with a pointer valid only for the duration of the call.
        /// </summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void GrPurchaseCallback(IntPtr purchaseJson);

        [DllImport("__Internal")]
        internal static extern void gr_ios_iap_set_callback(GrPurchaseCallback callback);

        [DllImport("__Internal")]
        internal static extern int gr_ios_iap_start_observer();

        [DllImport("__Internal")]
        internal static extern void gr_ios_iap_stop_observer();

        [DllImport("__Internal")]
        internal static extern int gr_ios_iap_is_supported();
    }
}
#endif
