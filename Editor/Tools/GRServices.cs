namespace GameRebellionSdk.Unity.Editor
{
    /// <summary>
    /// The services behind the GameRebellion window. Sign-in (gr-unity) and the
    /// studios and games come from GameRebellion; there is no SDK key endpoint yet.
    /// IDE registration is real for Claude Code and simulated for Codex. The mocks
    /// stay for working on the window offline: swap them in here.
    /// </summary>
    internal static class GRServices
    {
        /// <summary>Shows the "Mock games" badge and the mock-only controls (sample live events).</summary>
        public static bool UsesMocks => false;

        public static IGRAuthService Auth { get; } = new GROAuthService();
        public static IGRPlatformApi Api { get; } = new GRPlatformApi(Auth);
        public static IGRIdeConnector Ide { get; } = new GRIdeConnector();
    }
}
