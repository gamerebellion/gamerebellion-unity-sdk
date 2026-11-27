using System;
using System.Threading;
using System.Threading.Tasks;

namespace GameRebellionSdk.Unity.Editor
{
    /// <summary>The developer signed in to the Editor window.</summary>
    [Serializable]
    [System.Reflection.Obfuscation(Exclude = true, ApplyToMembers = true)]
    internal sealed class GRUser
    {
        public string Name;
        public string Email;

        /// <summary>The developer's studio; empty until /me accepts the Editor's token (studios:read).</summary>
        public string Studio;
    }

    /// <summary>
    /// Signs the developer in to GameRebellion from the Editor (EDITOR_MCP_ARCHITECTURE.md §4).
    ///
    /// The tokens behind the session never leave the implementation: callers see
    /// the user, and API clients ask for an access token right before a request.
    /// </summary>
    internal interface IGRAuthService
    {
        /// <summary>True when sign-in is only simulated.</summary>
        bool IsSimulated { get; }

        /// <summary>The GameRebellion environment to sign in to: always Production. Each one keeps its own session.</summary>
        GrEnvironment Environment { get; }

        /// <summary>The signed-in user for <see cref="Environment"/>, restored across reloads and restarts; null when signed out.</summary>
        GRUser CurrentUser { get; }

        /// <summary>Completes once the developer has signed in in the browser. Cancelling abandons the attempt.</summary>
        Task<GRUser> SignInAsync(CancellationToken cancellationToken);

        /// <summary>
        /// A current access token, refreshed when it's within a minute of expiring, or
        /// when <paramref name="forceRefresh"/> is set (after a 401). Null when there is
        /// no session, or it was revoked or expired for good; the caller then treats
        /// the developer as signed out.
        /// </summary>
        Task<string> GetAccessTokenAsync(CancellationToken cancellationToken, bool forceRefresh = false);

        /// <summary>Forgets the session locally and revokes it on the server.</summary>
        void SignOut();
    }
}
