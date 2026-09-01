//
//  GRiOSNativeAdapter.mm
//  iOS Native Adapter Implementation
//

#import "GRiOSNativeAdapter.h"
#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import <AdSupport/AdSupport.h>
#import <AppTrackingTransparency/AppTrackingTransparency.h>
#import <AdServices/AdServices.h>
#import <CoreTelephony/CTTelephonyNetworkInfo.h>
#import <CoreTelephony/CTCarrier.h>
#import <SystemConfiguration/SystemConfiguration.h>
#import <Security/Security.h>
#import <mach/mach.h>
#include <netinet/in.h>
#include <arpa/inet.h>
#include <sys/utsname.h>

static NSString* FormatDateIso8601(NSDate* date) {
    if (!date) return nil;
    static NSDateFormatter* formatter = nil;
    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        formatter = [[NSDateFormatter alloc] init];
        formatter.locale = [NSLocale localeWithLocaleIdentifier:@"en_US_POSIX"];
        // Pin the Gregorian calendar so a non-Gregorian device calendar (e.g. Buddhist,
        // Islamic) cannot shift the formatted year in ingested timestamps.
        formatter.calendar = [[NSCalendar alloc] initWithCalendarIdentifier:NSCalendarIdentifierGregorian];
        formatter.timeZone = [NSTimeZone timeZoneWithAbbreviation:@"UTC"];
        // Lowercase 'xxxxx' forces a numeric zero offset (+00:00) instead of 'Z',
        // keeping the format byte-identical to the Android and PC adapters.
        formatter.dateFormat = @"yyyy-MM-dd'T'HH:mm:ss.SSSxxxxx";
    });
    return [formatter stringFromDate:date];
}

#ifdef __cplusplus
extern "C" {
#endif

// Helper function to convert NSString to C string
static char* CopyNSString(NSString* str) {
    if (!str) return nullptr;
    const char* cStr = [str UTF8String];
    size_t len = strlen(cStr) + 1;
    char* result = (char*)malloc(len);
    strcpy(result, cStr);
    return result;
}

// Cache for values that don't change
static NSString* g_bundleId = nil;
static NSString* g_appVersion = nil;
static NSString* g_buildNumber = nil;
static NSString* g_lastDeepLink = nil;

// Initialize cached values
__attribute__((constructor))
static void InitializeCache() {
    g_bundleId = [[NSBundle mainBundle] bundleIdentifier];
    g_appVersion = [[NSBundle mainBundle] objectForInfoDictionaryKey:@"CFBundleShortVersionString"];
    g_buildNumber = [[NSBundle mainBundle] objectForInfoDictionaryKey:@"CFBundleVersion"];
}

#pragma mark - Attribution & Advertising

const char* gr_ios_get_idfa() {
    if (@available(iOS 14.0, *)) {
        ATTrackingManagerAuthorizationStatus status = [ATTrackingManager trackingAuthorizationStatus];
        if (status != ATTrackingManagerAuthorizationStatusAuthorized) {
            return nullptr;
        }
    }
    
    NSString* idfa = [[[ASIdentifierManager sharedManager] advertisingIdentifier] UUIDString];
    return CopyNSString(idfa);
}

const char* gr_ios_get_idfv() {
    NSString* idfv = [[[UIDevice currentDevice] identifierForVendor] UUIDString];
    return CopyNSString(idfv);
}

const char* gr_ios_get_device_id() {
    static NSString* const kService = @"com.gamerebellion.sdk";
    static NSString* const kAccount = @"gr_device_id";

    // Try to read existing UUID from Keychain
    NSDictionary* query = @{
        (__bridge id)kSecClass:        (__bridge id)kSecClassGenericPassword,
        (__bridge id)kSecAttrService:  kService,
        (__bridge id)kSecAttrAccount:  kAccount,
        (__bridge id)kSecReturnData:   @YES,
        (__bridge id)kSecMatchLimit:   (__bridge id)kSecMatchLimitOne
    };
    CFTypeRef result = NULL;
    OSStatus status = SecItemCopyMatching((__bridge CFDictionaryRef)query, &result);
    if (status == errSecSuccess && result) {
        NSData* data = (__bridge_transfer NSData*)result;
        NSString* stored = [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding];
        if (stored.length > 0) return CopyNSString(stored);
    }

    // First launch — generate and persist a new UUID
    NSString* newId = [[NSUUID UUID] UUIDString];
    NSDictionary* addQuery = @{
        (__bridge id)kSecClass:           (__bridge id)kSecClassGenericPassword,
        (__bridge id)kSecAttrService:     kService,
        (__bridge id)kSecAttrAccount:     kAccount,
        (__bridge id)kSecAttrAccessible:  (__bridge id)kSecAttrAccessibleAfterFirstUnlock,
        (__bridge id)kSecValueData:       [newId dataUsingEncoding:NSUTF8StringEncoding]
    };
    SecItemAdd((__bridge CFDictionaryRef)addQuery, NULL);
    return CopyNSString(newId);
}

int gr_ios_get_att_status() {
    if (@available(iOS 14.0, *)) {
        return (int)[ATTrackingManager trackingAuthorizationStatus];
    }
    // Pre-iOS 14, tracking is implicitly authorized
    return 3;  // Authorized
}

void gr_ios_request_att() {
    if (@available(iOS 14.5, *)) {
        dispatch_async(dispatch_get_main_queue(), ^{
            [ATTrackingManager requestTrackingAuthorizationWithCompletionHandler:^(ATTrackingManagerAuthorizationStatus status) {
                NSLog(@"[GRiOS] ATT Status: %ld", (long)status);
            }];
        });
    }
}

#pragma mark - Device Information

const char* gr_ios_get_device_model() {
    struct utsname systemInfo;
    uname(&systemInfo);
    NSString* model = [NSString stringWithCString:systemInfo.machine encoding:NSUTF8StringEncoding];
    return CopyNSString(model);
}

const char* gr_ios_get_device_name() {
    // The user-assigned name ("Dmytro's iPhone"). From iOS 16 this returns the device
    // model unless the app holds the user-assigned-device-name entitlement, which is
    // fine -- it is still the name the system reports for the device.
    return CopyNSString([[UIDevice currentDevice] name]);
}

const char* gr_ios_get_os_version() {
    NSString* version = [[UIDevice currentDevice] systemVersion];
    NSString* osVersion = [NSString stringWithFormat:@"iOS %@", version];
    return CopyNSString(osVersion);
}

const char* gr_ios_get_bundle_id() {
    return CopyNSString(g_bundleId);
}

const char* gr_ios_get_app_version() {
    return CopyNSString(g_appVersion);
}

const char* gr_ios_get_build_number() {
    return CopyNSString(g_buildNumber);
}

const char* gr_ios_get_app_install_time() {
    NSString* bundlePath = [[NSBundle mainBundle] bundlePath];
    NSDictionary* attrs = [[NSFileManager defaultManager] attributesOfItemAtPath:bundlePath error:nil];
    NSDate* created = attrs[NSFileCreationDate];
    return CopyNSString(FormatDateIso8601(created));
}

const char* gr_ios_get_app_update_time() {
    NSString* bundlePath = [[NSBundle mainBundle] bundlePath];
    NSDictionary* attrs = [[NSFileManager defaultManager] attributesOfItemAtPath:bundlePath error:nil];
    NSDate* modified = attrs[NSFileModificationDate];
    return CopyNSString(FormatDateIso8601(modified));
}

const char* gr_ios_get_screen_resolution() {
    CGRect screenBounds = [[UIScreen mainScreen] bounds];
    CGFloat scale = [[UIScreen mainScreen] scale];
    NSString* resolution = [NSString stringWithFormat:@"%.0fx%.0f",
                            screenBounds.size.width * scale,
                            screenBounds.size.height * scale];
    return CopyNSString(resolution);
}

float gr_ios_get_battery_level() {
    UIDevice* device = [UIDevice currentDevice];
    device.batteryMonitoringEnabled = YES;
    float level = device.batteryLevel;
    device.batteryMonitoringEnabled = NO;
    return level;  // Returns -1.0 if battery monitoring is unavailable
}

double gr_ios_get_free_storage_mb() {
    NSDictionary* attributes = [[NSFileManager defaultManager] attributesOfFileSystemForPath:@"/" error:nil];
    if (!attributes) return -1.0;
    NSNumber* freeSize = [attributes objectForKey:NSFileSystemFreeSize];
    if (!freeSize) return -1.0;
    return [freeSize doubleValue] / (1024.0 * 1024.0);
}

double gr_ios_get_memory_usage_mb() {
    task_vm_info_data_t vmInfo;
    mach_msg_type_number_t count = TASK_VM_INFO_COUNT;
    kern_return_t kr = task_info(mach_task_self(), TASK_VM_INFO, (task_info_t)&vmInfo, &count);
    if (kr != KERN_SUCCESS) return -1.0;
    return (double)vmInfo.phys_footprint / (1024.0 * 1024.0);
}

#pragma mark - Location & Locale

const char* gr_ios_get_timezone() {
    NSTimeZone* tz = [NSTimeZone localTimeZone];
    return CopyNSString([tz name]);
}

const char* gr_ios_get_locale() {
    NSLocale* locale = [NSLocale currentLocale];
    return CopyNSString([locale localeIdentifier]);
}

const char* gr_ios_get_country_code() {
    NSLocale* locale = [NSLocale currentLocale];
    NSString* countryCode = [locale objectForKey:NSLocaleCountryCode];
    return CopyNSString(countryCode);
}

#pragma mark - Deep Links

const char* gr_ios_get_last_deep_link() {
    return CopyNSString(g_lastDeepLink);
}

void gr_ios_set_deep_link(const char* url) {
    if (url) {
        g_lastDeepLink = [NSString stringWithUTF8String:url];
    }
}

#pragma mark - Search Ads

const char* gr_ios_get_search_ads_token() {
    if (@available(iOS 14.3, *)) {
        __block char* token = nullptr;
        dispatch_semaphore_t sem = dispatch_semaphore_create(0);
        
        NSError* error = nil;
        NSString* attributionToken = [AAAttribution attributionTokenWithError:&error];
        
        if (!error && attributionToken) {
            token = CopyNSString(attributionToken);
        }
        
        dispatch_semaphore_signal(sem);
        dispatch_semaphore_wait(sem, DISPATCH_TIME_FOREVER);
        
        return token;
    }
    return nullptr;
}

#pragma mark - Network

const char* gr_ios_get_connection_type() {
    // This is a simplified implementation
    // For production, use Reachability or Network framework
    struct sockaddr_in zeroAddress;
    bzero(&zeroAddress, sizeof(zeroAddress));
    zeroAddress.sin_len = sizeof(zeroAddress);
    zeroAddress.sin_family = AF_INET;
    
    SCNetworkReachabilityRef reachability = SCNetworkReachabilityCreateWithAddress(kCFAllocatorDefault, (const struct sockaddr*)&zeroAddress);
    SCNetworkReachabilityFlags flags;
    
    if (SCNetworkReachabilityGetFlags(reachability, &flags)) {
        CFRelease(reachability);
        
        if ((flags & kSCNetworkReachabilityFlagsReachable) == 0) {
            return CopyNSString(@"none");
        }
        
        if ((flags & kSCNetworkReachabilityFlagsIsWWAN) != 0) {
            return CopyNSString(@"cellular");
        }
        
        return CopyNSString(@"wifi");
    }
    
    CFRelease(reachability);
    return CopyNSString(@"unknown");
}

#ifdef __cplusplus
}
#endif

const char* gr_ios_get_carrier_name() {
    CTTelephonyNetworkInfo* networkInfo = [[CTTelephonyNetworkInfo alloc] init];
    CTCarrier* carrier = [networkInfo subscriberCellularProvider];
    return CopyNSString([carrier carrierName]);
}

#pragma mark - Memory Management

void gr_ios_free_string(char* str) {
    if (str) {
        free(str);
    }
}

#pragma mark - App Lifecycle (Optional callbacks)

void gr_ios_register_lifecycle_callbacks() {
    // Register for app lifecycle notifications
    NSNotificationCenter* nc = [NSNotificationCenter defaultCenter];
    
    [nc addObserverForName:UIApplicationDidBecomeActiveNotification
                    object:nil
                     queue:[NSOperationQueue mainQueue]
                usingBlock:^(NSNotification* note) {
        gr_ios_app_did_become_active();
    }];
    
    [nc addObserverForName:UIApplicationDidEnterBackgroundNotification
                    object:nil
                     queue:[NSOperationQueue mainQueue]
                usingBlock:^(NSNotification* note) {
        gr_ios_app_did_enter_background();
    }];
    
    [nc addObserverForName:UIApplicationWillTerminateNotification
                    object:nil
                     queue:[NSOperationQueue mainQueue]
                usingBlock:^(NSNotification* note) {
        gr_ios_app_will_terminate();
    }];
}

void gr_ios_app_did_become_active() {
    NSLog(@"[GRiOS] App became active");
}

void gr_ios_app_did_enter_background() {
    NSLog(@"[GRiOS] App entered background");
}

void gr_ios_app_will_terminate() {
    NSLog(@"[GRiOS] App will terminate");
}
