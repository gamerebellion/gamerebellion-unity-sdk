using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GameRebellionSdk.Unity.Editor
{
    /// <summary>
    /// Canned studios and games, with a short delay per call so the window's loading
    /// states show. For working on the window offline: swap it in in GRServices. The
    /// keys are obviously fake and are never sent anywhere.
    /// </summary>
    internal sealed class GRMockPlatformApi : IGRPlatformApi
    {
        private const int LatencyMs = 300;

        private static readonly GRStudio[] Studios =
        {
            new GRStudio("studio-rebel", "Rebel Studio", "owner", isDefault: true, roleName: "Owner"),
            new GRStudio("studio-indie", "Indie Corner", "viewer", roleName: "Viewer"),
        };

        private static readonly GRGame[] Games =
        {
            new GRGame("game-match3", "studio-rebel", "Match3", "Puzzle", "ios, android",
                sdkTokens: new[] { new GRSdkToken("t1", "Default", "mock_dev_match3_c3a8e270", "2026-06-01T10:00:00Z") }),
            new GRGame("game-runner", "studio-rebel", "Space Runner", "Arcade", "steam", isDisabled: true,
                sdkTokens: new GRSdkToken[0]),
            new GRGame("game-tower", "studio-rebel", "Tower Siege", "Strategy", "steam",
                sdkTokens: new GRSdkToken[0]),
            new GRGame("game-farm", "studio-indie", "Farm Days", "Simulation", "ios"),
        };

        public async Task<IReadOnlyList<GRStudio>> GetStudiosAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(LatencyMs, cancellationToken);
            return Studios;
        }

        public async Task<IReadOnlyList<GRGame>> GetGamesAsync(string studioId, CancellationToken cancellationToken)
        {
            await Task.Delay(LatencyMs, cancellationToken);
            return Games.Where(game => game.StudioId == studioId).ToArray();
        }
    }
}
