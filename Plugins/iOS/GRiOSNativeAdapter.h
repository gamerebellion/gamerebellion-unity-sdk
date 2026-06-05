//
//  GRiOSNativeAdapter.h
//  iOS Native Adapter for GameRebellion Core SDK
//
//  Provides C-compatible functions that Native AOT Core can call directly via P/Invoke
//

#ifndef GRiOSNativeAdapter_h
#define GRiOSNativeAdapter_h

#ifdef __cplusplus
extern "C" {
#endif

// Attribution & Advertising IDs
const char* gr_ios_get_idfa(void);
const char* gr_ios_get_idfv(void);

// Persistent Device Identity (Keychain-backed UUID, survives reinstalls)
const char* gr_ios_get_device_id(void);
int gr_ios_get_att_status(void);  // 0=NotDetermined, 1=Restricted, 2=Denied, 3=Authorized
void gr_ios_request_att(void);

// Device Information
const char* gr_ios_get_device_model(void);
const char* gr_ios_get_os_version(void);
const char* gr_ios_get_bundle_id(void);
const char* gr_ios_get_app_version(void);
const char* gr_ios_get_build_number(void);
const char* gr_ios_get_app_install_time(void);
const char* gr_ios_get_app_update_time(void);
const char* gr_ios_get_screen_resolution(void);
float gr_ios_get_battery_level(void);  // Returns -1 if unavailable
double gr_ios_get_free_storage_mb(void);  // Returns -1 if unavailable
double gr_ios_get_memory_usage_mb(void);  // Returns -1 if unavailable

// App Lifecycle
void gr_ios_register_lifecycle_callbacks(void);
void gr_ios_app_did_become_active(void);
void gr_ios_app_did_enter_background(void);
void gr_ios_app_will_terminate(void);

// Location (optional)
const char* gr_ios_get_timezone(void);
const char* gr_ios_get_locale(void);
const char* gr_ios_get_country_code(void);

// Deep Links
const char* gr_ios_get_last_deep_link(void);
void gr_ios_set_deep_link(const char* url);

// Search Ads Attribution
const char* gr_ios_get_search_ads_token(void);

// Network
const char* gr_ios_get_connection_type(void);  // "wifi", "cellular", "none"
const char* gr_ios_get_carrier_name(void);

// Memory Management
void gr_ios_free_string(char* str);  // Free strings returned by above functions

#ifdef __cplusplus
}
#endif

#endif /* GRiOSNativeAdapter_h */
