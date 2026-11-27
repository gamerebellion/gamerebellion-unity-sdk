namespace GameRebellionSdk.Unity.Editor
{
    /// <summary>
    /// The Editor's OAuth client and where each GameRebellion environment lives
    /// (gr-backend docs/OAUTH.md, "Unity plugin client"). The Editor signs in as the
    /// first-party public client <c>gr-unity</c>, for the GameRebellion API.
    ///
    /// Coding agents sign in to gr-mcp separately, with their own client
    /// registration; nothing here is involved in that.
    /// </summary>
    internal static class GRAuthConfig
    {
        /// <summary>Public and first-party: no client secret, no consent screen.</summary>
        public const string ClientId = "gr-unity";

        /// <summary>The audience every token call names (RFC 8707), the same on every environment.</summary>
        public const string ApiResource = "https://api.gamerebellion.com";

        // Identity, a refresh token, and read access to the developer's studios and games.
        // Tokens from before studios:read get 403 insufficient_scope: the window asks to sign in again.
        public const string Scopes = "openid profile email offline_access studios:read";

        /// <summary>
        /// The environment the Editor signs in to, and that Connect points the agent's
        /// gr-mcp at. Always Production: the window has no environment picker.
        /// </summary>
        public const GrEnvironment SignInEnvironment = GrEnvironment.Production;

        /// <summary>
        /// The authorization server. A fixed list, never a value from a file or a
        /// setting: the window signs in to <see cref="SignInEnvironment"/> only.
        /// </summary>
        public static string AuthBaseUrl(GrEnvironment environment)
        {
            // Production is the Development host without "dev-". Staging follows the same
            // naming and is unconfirmed until it ships.
            switch (environment)
            {
                case GrEnvironment.Development: return "https://dev-auth.gamerebellion.com";
                case GrEnvironment.Staging: return "https://staging-auth.gamerebellion.com";
                default: return "https://auth.gamerebellion.com";
            }
        }

        /// <summary>The GameRebellion app API (studios, games). A fixed list, like <see cref="AuthBaseUrl"/>.</summary>
        public static string ApiBaseUrl(GrEnvironment environment)
        {
            switch (environment)
            {
                case GrEnvironment.Development: return "https://dev-app-api.gamerebellion.com";
                case GrEnvironment.Staging: return "https://staging-app-api.gamerebellion.com";
                default: return "https://app-api.gamerebellion.com";
            }
        }

        /// <summary>gr-mcp for <paramref name="environment"/>: what Connect registers with the agent.</summary>
        public static string McpUrlFor(GrEnvironment environment)
        {
            switch (environment)
            {
                case GrEnvironment.Development: return "https://dev-api.gamerebellion.com/mcp";
                case GrEnvironment.Staging: return "https://staging-api.gamerebellion.com/mcp";
                default: return "https://api.gamerebellion.com/mcp";
            }
        }
    }
}
