using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

using UnityEditor;
using UnityEngine;

namespace GameRebellionSdk.Unity.Editor
{
    /// <summary>
    /// Secrets in the OS credential store, as the auth contract asks ("avoid plain
    /// EditorPrefs: they aren't encrypted"): the macOS Keychain, the Windows Credential
    /// Manager, or on Linux the desktop keyring through libsecret (GNOME Keyring, KDE
    /// Wallet, KeePassXC: anything serving the Secret Service API).
    ///
    /// A Linux machine with no keyring keeps secrets in <see cref="SessionState"/>
    /// instead: in memory until Unity closes, never on disk. <see cref="KeyringProblem"/>
    /// says why, for the window.
    ///
    /// Items belong to <see cref="Service"/>, one per <c>account</c> name. Values are
    /// never logged.
    /// </summary>
    internal static class GRCredentialStore
    {
        public const string Service = "com.gamerebellion.unity-editor";

        /// <summary>False when secrets last only until Unity closes: Linux without a keyring.</summary>
        public static bool IsSecure => KeyringProblem == null;

        /// <summary>Why this machine keeps secrets only until Unity closes; null when they're saved.</summary>
        public static string KeyringProblem => IsMac || IsWindows ? null : Linux.KeyringProblem;

        private static bool IsMac => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        /// <summary>The stored value, or null if there is none.</summary>
        public static string Read(string account)
        {
            if (IsMac) return Keychain.Read(Service, account);
            if (IsWindows) return CredentialManager.Read($"{Service}/{account}");
            return Linux.Read(account);
        }

        public static void Write(string account, string secret)
        {
            if (IsMac) Keychain.Write(Service, account, secret);
            else if (IsWindows) CredentialManager.Write($"{Service}/{account}", secret);
            else Linux.Write(account, secret);
        }

        public static void Delete(string account)
        {
            if (IsMac) Keychain.Delete(Service, account);
            else if (IsWindows) CredentialManager.Delete($"{Service}/{account}");
            else Linux.Delete(account);
        }

        /// <summary>
        /// Earlier builds kept Linux secrets in EditorPrefs, unencrypted. Moves the one for
        /// <paramref name="account"/>, if any, to where secrets go now. Nothing to do elsewhere.
        /// </summary>
        public static void MoveOutOfEditorPrefs(string account)
        {
            if (!IsMac && !IsWindows)
            {
                Linux.MoveOutOfEditorPrefs(account);
            }
        }

        /// <summary>
        /// Security.framework's generic-password calls. Items the Editor creates are
        /// readable by the Editor without a prompt; a new Unity install may ask once.
        /// </summary>
        private static class Keychain
        {
            private const string Security = "/System/Library/Frameworks/Security.framework/Security";
            private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
            private const int ErrSecItemNotFound = -25300;

            [DllImport(Security)]
            private static extern int SecKeychainFindGenericPassword(IntPtr keychainOrArray, uint serviceNameLength, byte[] serviceName,
                uint accountNameLength, byte[] accountName, out uint passwordLength, out IntPtr passwordData, out IntPtr itemRef);

            [DllImport(Security)]
            private static extern int SecKeychainAddGenericPassword(IntPtr keychain, uint serviceNameLength, byte[] serviceName,
                uint accountNameLength, byte[] accountName, uint passwordLength, byte[] passwordData, out IntPtr itemRef);

            [DllImport(Security)]
            private static extern int SecKeychainItemModifyAttributesAndData(IntPtr itemRef, IntPtr attrList, uint length, byte[] data);

            [DllImport(Security)]
            private static extern int SecKeychainItemDelete(IntPtr itemRef);

            [DllImport(Security)]
            private static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);

            [DllImport(CoreFoundation)]
            private static extern void CFRelease(IntPtr cf);

            public static string Read(string service, string account)
            {
                var status = Find(service, account, out var item, out var secret);
                if (status == ErrSecItemNotFound) return null;
                Check(status, "read");
                CFRelease(item);
                return secret;
            }

            public static void Write(string service, string account, string secret)
            {
                var data = Encoding.UTF8.GetBytes(secret);
                var status = Find(service, account, out var item, out _);
                if (status == 0)
                {
                    try
                    {
                        Check(SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)data.Length, data), "update");
                    }
                    finally
                    {
                        CFRelease(item);
                    }

                    return;
                }

                if (status != ErrSecItemNotFound) Check(status, "read");
                var serviceBytes = Encoding.UTF8.GetBytes(service);
                var accountBytes = Encoding.UTF8.GetBytes(account);
                Check(SecKeychainAddGenericPassword(IntPtr.Zero, (uint)serviceBytes.Length, serviceBytes, (uint)accountBytes.Length, accountBytes,
                    (uint)data.Length, data, out var added), "save");
                CFRelease(added);
            }

            public static void Delete(string service, string account)
            {
                var status = Find(service, account, out var item, out _);
                if (status == ErrSecItemNotFound) return;
                Check(status, "read");
                try
                {
                    Check(SecKeychainItemDelete(item), "delete");
                }
                finally
                {
                    CFRelease(item);
                }
            }

            private static int Find(string service, string account, out IntPtr item, out string secret)
            {
                var serviceBytes = Encoding.UTF8.GetBytes(service);
                var accountBytes = Encoding.UTF8.GetBytes(account);
                var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)serviceBytes.Length, serviceBytes, (uint)accountBytes.Length, accountBytes,
                    out var length, out var data, out item);
                secret = null;
                if (status == 0 && data != IntPtr.Zero)
                {
                    var bytes = new byte[length];
                    Marshal.Copy(data, bytes, 0, (int)length);
                    secret = Encoding.UTF8.GetString(bytes);
                    SecKeychainItemFreeContent(IntPtr.Zero, data);
                }

                return status;
            }

            private static void Check(int status, string action)
            {
                if (status != 0)
                {
                    throw new InvalidOperationException($"Couldn't {action} the GameRebellion sign-in in the macOS Keychain (OSStatus {status}).");
                }
            }
        }

        /// <summary>advapi32's generic credentials, persisted for this user on this machine.</summary>
        private static class CredentialManager
        {
            private const int GenericCredential = 1;
            private const int PersistLocalMachine = 2;
            private const int ErrorNotFound = 1168;

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            private struct Credential
            {
                public int Flags;
                public int Type;
                public string TargetName;
                public string Comment;
                public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
                public int CredentialBlobSize;
                public IntPtr CredentialBlob;
                public int Persist;
                public int AttributeCount;
                public IntPtr Attributes;
                public string TargetAlias;
                public string UserName;
            }

            [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

            [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern bool CredWrite(ref Credential credential, int flags);

            [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern bool CredDelete(string target, int type, int flags);

            [DllImport("advapi32.dll")]
            private static extern void CredFree(IntPtr buffer);

            public static string Read(string target)
            {
                if (!CredRead(target, GenericCredential, 0, out var pointer))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == ErrorNotFound) return null;
                    throw new InvalidOperationException($"Couldn't read the GameRebellion sign-in from Windows Credential Manager (error {error}).");
                }

                try
                {
                    var credential = (Credential)Marshal.PtrToStructure(pointer, typeof(Credential));
                    var bytes = new byte[credential.CredentialBlobSize];
                    Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                    return Encoding.UTF8.GetString(bytes);
                }
                finally
                {
                    CredFree(pointer);
                }
            }

            public static void Write(string target, string secret)
            {
                var bytes = Encoding.UTF8.GetBytes(secret);
                var blob = Marshal.AllocHGlobal(bytes.Length);
                try
                {
                    Marshal.Copy(bytes, 0, blob, bytes.Length);
                    var credential = new Credential
                    {
                        Type = GenericCredential,
                        TargetName = target,
                        CredentialBlobSize = bytes.Length,
                        CredentialBlob = blob,
                        Persist = PersistLocalMachine,
                        UserName = Environment.UserName,
                    };
                    if (!CredWrite(ref credential, 0))
                    {
                        throw new InvalidOperationException($"Couldn't save the GameRebellion sign-in in Windows Credential Manager (error {Marshal.GetLastWin32Error()}).");
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(blob);
                }
            }

            public static void Delete(string target)
            {
                if (!CredDelete(target, GenericCredential, 0) && Marshal.GetLastWin32Error() != ErrorNotFound)
                {
                    throw new InvalidOperationException($"Couldn't delete the GameRebellion sign-in from Windows Credential Manager (error {Marshal.GetLastWin32Error()}).");
                }
            }
        }

        /// <summary>
        /// Linux: the keyring when there is one, otherwise SessionState. The first time the
        /// keyring fails in an Editor session, the rest of that session uses SessionState,
        /// so a domain reload never looks for a secret in the other place.
        /// </summary>
        private static class Linux
        {
            private const string CheckedKey = "GameRebellion.Credentials.KeyringChecked";
            private const string ProblemKey = "GameRebellion.Credentials.KeyringProblem";

            // An item that never exists: looking it up shows whether a keyring answers,
            // without unlocking anything.
            private const string CheckAccount = "keyring-check";

            private static string SessionKey(string account) => $"GameRebellion.Credentials.{account}";

            // Where earlier builds kept the secret.
            private static string EditorPrefsKey(string account) => $"{Service}.{account}";

            public static string KeyringProblem
            {
                get
                {
                    if (!SessionState.GetBool(CheckedKey, false))
                    {
                        SessionState.SetBool(CheckedKey, true);
                        try
                        {
                            SecretService.Read(CheckAccount);
                        }
                        catch (Exception e) when (IsKeyringFailure(e))
                        {
                            GiveUp(e);
                        }
                    }

                    var problem = SessionState.GetString(ProblemKey, string.Empty);
                    return problem.Length == 0 ? null : problem;
                }
            }

            public static string Read(string account)
            {
                MoveOutOfEditorPrefs(account);
                if (KeyringProblem == null)
                {
                    try
                    {
                        return SecretService.Read(account);
                    }
                    catch (Exception e) when (IsKeyringFailure(e))
                    {
                        GiveUp(e);
                    }
                }

                var secret = SessionState.GetString(SessionKey(account), string.Empty);
                return secret.Length == 0 ? null : secret;
            }

            public static void Write(string account, string secret)
            {
                EditorPrefs.DeleteKey(EditorPrefsKey(account));
                if (KeyringProblem == null)
                {
                    try
                    {
                        SecretService.Write(account, secret);
                        SessionState.EraseString(SessionKey(account));
                        return;
                    }
                    catch (Exception e) when (IsKeyringFailure(e))
                    {
                        GiveUp(e);
                    }
                }

                SessionState.SetString(SessionKey(account), secret);
            }

            public static void Delete(string account)
            {
                EditorPrefs.DeleteKey(EditorPrefsKey(account));
                SessionState.EraseString(SessionKey(account));
                if (KeyringProblem != null)
                {
                    return;
                }

                try
                {
                    SecretService.Delete(account);
                }
                catch (Exception e) when (IsKeyringFailure(e))
                {
                    GiveUp(e);
                    throw new InvalidOperationException($"Couldn't delete the GameRebellion sign-in from the keyring: {e.Message}");
                }
            }

            public static void MoveOutOfEditorPrefs(string account)
            {
                var key = EditorPrefsKey(account);
                if (!EditorPrefs.HasKey(key))
                {
                    return;
                }

                var secret = EditorPrefs.GetString(key, string.Empty);
                EditorPrefs.DeleteKey(key);
                if (secret.Length > 0)
                {
                    Write(account, secret);
                }
            }

            private static bool IsKeyringFailure(Exception e) =>
                e is SecretService.KeyringException || e is DllNotFoundException || e is EntryPointNotFoundException;

            private static void GiveUp(Exception e)
            {
                var problem = e is DllNotFoundException ? "libsecret isn't installed" : e.Message;
                SessionState.SetString(ProblemKey, problem);
                Debug.LogWarning($"[GameRebellion] No keyring for the sign-in ({problem}): it's kept only until Unity closes.");
            }
        }

        /// <summary>
        /// libsecret's Secret Service calls, loaded on first use: a machine without
        /// libsecret gets <see cref="DllNotFoundException"/>, not a broken window. Only
        /// the <c>v</c> variants, which take a GHashTable of attributes: the others take
        /// C varargs, which P/Invoke can't call portably.
        /// </summary>
        private static class SecretService
        {
            private const string LibSecret = "libsecret-1.so.0";
            private const string LibGLib = "libglib-2.0.so.0";
            private const int RtldNow = 2;

            // SECRET_SCHEMA_NONE: items also match on the schema name (xdg:schema).
            private const int SchemaFlags = 0;

            // GINT_TO_POINTER(SECRET_SCHEMA_ATTRIBUTE_STRING).
            private static readonly IntPtr StringAttribute = IntPtr.Zero;

            /// <summary>The keyring answered with an error, such as no D-Bus session or nothing serving the Secret Service.</summary>
            public sealed class KeyringException : Exception
            {
                public KeyringException(string message) : base(message)
                {
                }
            }

            [DllImport(LibSecret)]
            private static extern IntPtr secret_schema_newv(byte[] name, int flags, IntPtr attributeNamesAndTypes);

            [DllImport(LibSecret)]
            private static extern void secret_schema_unref(IntPtr schema);

            [DllImport(LibSecret)]
            private static extern int secret_password_storev_sync(IntPtr schema, IntPtr attributes, IntPtr collection, byte[] label, byte[] password,
                IntPtr cancellable, ref IntPtr error);

            [DllImport(LibSecret)]
            private static extern IntPtr secret_password_lookupv_sync(IntPtr schema, IntPtr attributes, IntPtr cancellable, ref IntPtr error);

            [DllImport(LibSecret)]
            private static extern int secret_password_clearv_sync(IntPtr schema, IntPtr attributes, IntPtr cancellable, ref IntPtr error);

            [DllImport(LibSecret)]
            private static extern void secret_password_free(IntPtr password);

            [DllImport(LibGLib)]
            private static extern IntPtr g_hash_table_new(IntPtr hashFunc, IntPtr keyEqualFunc);

            [DllImport(LibGLib)]
            private static extern void g_hash_table_insert(IntPtr table, IntPtr key, IntPtr value);

            [DllImport(LibGLib)]
            private static extern void g_hash_table_unref(IntPtr table);

            [DllImport(LibGLib)]
            private static extern void g_error_free(IntPtr error);

            // g_str_hash and g_str_equal are passed by address, which only dlsym gives.
            // libdl.so.2 is still there (as a stub) on glibc 2.34 and later.
            [DllImport("libdl.so.2")]
            private static extern IntPtr dlopen(string file, int mode);

            [DllImport("libdl.so.2")]
            private static extern IntPtr dlsym(IntPtr handle, string name);

            private static IntPtr _strHash;
            private static IntPtr _strEqual;

            /// <summary>The secret, or null if there is none.</summary>
            public static string Read(string account)
            {
                return WithAttributes(account, (schema, attributes) =>
                {
                    var error = IntPtr.Zero;
                    var secret = secret_password_lookupv_sync(schema, attributes, IntPtr.Zero, ref error);
                    ThrowIfError(error);
                    if (secret == IntPtr.Zero)
                    {
                        return null;
                    }

                    try
                    {
                        return FromUtf8(secret);
                    }
                    finally
                    {
                        secret_password_free(secret); // Also wipes it.
                    }
                });
            }

            /// <summary>Saves the secret in the default keyring, replacing the item with the same attributes.</summary>
            public static void Write(string account, string secret)
            {
                var label = ToUtf8($"GameRebellion sign-in for the Unity Editor ({account})");
                var password = ToUtf8(secret);
                try
                {
                    WithAttributes(account, (schema, attributes) =>
                    {
                        var error = IntPtr.Zero;
                        var stored = secret_password_storev_sync(schema, attributes, IntPtr.Zero, label, password, IntPtr.Zero, ref error);
                        ThrowIfError(error);
                        if (stored == 0)
                        {
                            throw new KeyringException("the keyring didn't save it");
                        }

                        return (string)null;
                    });
                }
                finally
                {
                    Array.Clear(password, 0, password.Length);
                }
            }

            /// <summary>Removes the item; nothing happens if there is none.</summary>
            public static void Delete(string account)
            {
                WithAttributes(account, (schema, attributes) =>
                {
                    var error = IntPtr.Zero;
                    secret_password_clearv_sync(schema, attributes, IntPtr.Zero, ref error); // False with no error: nothing to remove.
                    ThrowIfError(error);
                    return (string)null;
                });
            }

            /// <summary>
            /// Runs <paramref name="call"/> with this store's schema and the attributes that
            /// name the item: service and account, as on macOS and Windows. So
            /// <c>secret-tool lookup service com.gamerebellion.unity-editor account …</c> finds it too.
            /// </summary>
            private static string WithAttributes(string account, Func<IntPtr, IntPtr, string> call)
            {
                LoadStringFunctions();
                var strings = new List<IntPtr>();
                IntPtr Native(string value)
                {
                    var bytes = ToUtf8(value);
                    var pointer = Marshal.AllocHGlobal(bytes.Length);
                    Marshal.Copy(bytes, 0, pointer, bytes.Length);
                    strings.Add(pointer);
                    return pointer;
                }

                var types = g_hash_table_new(_strHash, _strEqual);
                var attributes = g_hash_table_new(_strHash, _strEqual);
                var schema = IntPtr.Zero;
                try
                {
                    g_hash_table_insert(types, Native("service"), StringAttribute);
                    g_hash_table_insert(types, Native("account"), StringAttribute);
                    schema = secret_schema_newv(ToUtf8(Service), SchemaFlags, types);
                    if (schema == IntPtr.Zero)
                    {
                        throw new KeyringException("libsecret rejected the item's schema");
                    }

                    g_hash_table_insert(attributes, Native("service"), Native(Service));
                    g_hash_table_insert(attributes, Native("account"), Native(account));
                    return call(schema, attributes);
                }
                finally
                {
                    if (schema != IntPtr.Zero)
                    {
                        secret_schema_unref(schema);
                    }

                    g_hash_table_unref(attributes);
                    g_hash_table_unref(types);
                    foreach (var pointer in strings)
                    {
                        Marshal.FreeHGlobal(pointer);
                    }
                }
            }

            private static void LoadStringFunctions()
            {
                if (_strHash != IntPtr.Zero)
                {
                    return;
                }

                var glib = dlopen(LibGLib, RtldNow);
                if (glib == IntPtr.Zero)
                {
                    throw new DllNotFoundException(LibGLib);
                }

                var hash = dlsym(glib, "g_str_hash");
                var equal = dlsym(glib, "g_str_equal");
                if (hash == IntPtr.Zero || equal == IntPtr.Zero)
                {
                    throw new EntryPointNotFoundException("g_str_hash");
                }

                _strEqual = equal;
                _strHash = hash;
            }

            /// <summary>Throws the GError's message, freeing it.</summary>
            private static void ThrowIfError(IntPtr error)
            {
                if (error == IntPtr.Zero)
                {
                    return;
                }

                // struct GError { GQuark domain; gint code; gchar *message; }
                var message = Marshal.ReadIntPtr(error, 8);
                var text = message == IntPtr.Zero ? "unknown error" : FromUtf8(message);
                g_error_free(error);
                throw new KeyringException(text);
            }

            /// <summary>UTF-8 with the terminating NUL C expects.</summary>
            private static byte[] ToUtf8(string value)
            {
                var bytes = new byte[Encoding.UTF8.GetByteCount(value) + 1];
                Encoding.UTF8.GetBytes(value, 0, value.Length, bytes, 0);
                return bytes;
            }

            private static string FromUtf8(IntPtr text)
            {
                var length = 0;
                while (Marshal.ReadByte(text, length) != 0)
                {
                    length++;
                }

                var bytes = new byte[length];
                Marshal.Copy(text, bytes, 0, length);
                try
                {
                    return Encoding.UTF8.GetString(bytes);
                }
                finally
                {
                    Array.Clear(bytes, 0, bytes.Length);
                }
            }
        }
    }
}
