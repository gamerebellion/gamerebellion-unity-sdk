using System;
using System.Security.Cryptography;
using System.Text;

using UnityEngine;

namespace GameRebellionSdk.Unity.Editor
{
    /// <summary>PKCE (RFC 7636), random state, and reading a JWT's claims.</summary>
    internal static class GROAuthUtil
    {
        /// <summary>A code verifier: 32 random bytes, base64url, so 43 characters.</summary>
        public static string NewVerifier() => Base64Url(RandomBytes(32));

        public static string NewState() => Base64Url(RandomBytes(16));

        /// <summary>The S256 code challenge for <paramref name="verifier"/>.</summary>
        public static string Challenge(string verifier)
        {
            using (var sha = SHA256.Create())
            {
                return Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
            }
        }

        /// <summary>
        /// The payload of a JWT, unverified. Only for showing who signed in: the
        /// token came straight from the token endpoint over TLS, and every API that
        /// accepts it verifies its signature itself.
        /// </summary>
        public static GRClaims ReadClaims(string jwt)
        {
            var parts = jwt?.Split('.');
            if (parts == null || parts.Length < 2)
            {
                return null;
            }

            try
            {
                var payload = parts[1].Replace('-', '+').Replace('_', '/');
                payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
                return JsonUtility.FromJson<GRClaims>(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static byte[] RandomBytes(int count)
        {
            var bytes = new byte[count];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }

            return bytes;
        }

        private static string Base64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
