using System.Threading;
using System.Threading.Tasks;

using UnityEditor;
using UnityEngine;

namespace GameRebellionSdk.Unity.Editor
{
    /// <summary>
    /// Simulated sign-in, for working on the window offline. No browser opens and
    /// nothing goes over the network: after a short pause the same test user is
    /// signed in. The user is kept in EditorPrefs, like the real session, so it
    /// survives domain reloads and Editor restarts.
    /// </summary>
    internal sealed class GRMockAuthService : IGRAuthService
    {
        private const string UserKey = "GameRebellion.Mock.User";
        private const int SignInDelayMs = 1500;

        public bool IsSimulated => true;

        public GrEnvironment Environment => GRAuthConfig.SignInEnvironment;

        public GRUser CurrentUser
        {
            get
            {
                var json = EditorPrefs.GetString(UserKey, string.Empty);
                return json.Length == 0 ? null : JsonUtility.FromJson<GRUser>(json);
            }
        }

        public async Task<GRUser> SignInAsync(CancellationToken cancellationToken)
        {
            // Stands in for the browser round trip, long enough to see and cancel.
            await Task.Delay(SignInDelayMs, cancellationToken);

            var user = new GRUser { Name = "Dev Tester", Email = "dev@studio.test", Studio = "Rebel Studio" };
            EditorPrefs.SetString(UserKey, JsonUtility.ToJson(user));
            return user;
        }

        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken, bool forceRefresh = false)
        {
            return Task.FromResult(CurrentUser == null ? null : "mock-access-token");
        }

        public void SignOut()
        {
            EditorPrefs.DeleteKey(UserKey);
        }
    }
}
