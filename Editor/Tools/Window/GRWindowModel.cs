using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using UnityEditor;

namespace GameRebellionSdk.Unity.Editor
{
    internal enum GRAuthPhase
    {
        SignedOut,
        SigningIn,
        SignedIn,
    }

    /// <summary>The views behind the tabs of the signed-in window.</summary>
    internal enum GRTab
    {
        Game,
        Mcp,
        LiveEvents,
    }

    /// <summary>Whether the connected game's SDK key can be shown, and if not, why.</summary>
    internal enum GRKeyAccess
    {
        NoGame,
        Available,

        /// <summary>is_disabled: the studio's plan switched the game off, so the server sends no key.</summary>
        GameDisabled,

        /// <summary>The server filtered the keys out for the developer's role.</summary>
        HiddenByRole,

        /// <summary>The game has no active key.</summary>
        NoActiveKey,
    }

    internal enum GRCheckState
    {
        Done,
        Partial,
        Todo,
    }

    /// <summary>One line of the integration checklist.</summary>
    internal sealed class GRCheck
    {
        public GRCheck(string label, string detail, GRCheckState state)
        {
            Label = label;
            Detail = detail;
            State = state;
        }

        public string Label { get; }
        public string Detail { get; }
        public GRCheckState State { get; }
    }

    /// <summary>One Track* call seen in play mode, with the script line that made it.</summary>
    internal sealed class GRCapturedEvent
    {
        public GRCapturedEvent(GrCapturedEvent captured)
        {
            Time = captured.time.Length >= 8 ? captured.time.Substring(0, 8) : captured.time;
            Name = captured.name;
            Payload = captured.payload;
            Problem = captured.problem;
            Stack = captured.stack;

            // Assets/Scripts/Boot.cs:34
            var colon = captured.site.LastIndexOf(':');
            AssetPath = colon > 0 ? captured.site.Substring(0, colon) : captured.site;
            Line = colon > 0 && int.TryParse(captured.site.Substring(colon + 1), out var line) ? line : 0;
            Site = AssetPath.Length == 0 ? string.Empty : System.IO.Path.GetFileName(AssetPath) + (Line > 0 ? $":{Line}" : string.Empty);
        }

        public string Time { get; }
        public string Name { get; }
        public string Payload { get; }

        /// <summary>Why the SDK dropped or rejected it; empty when it was sent on.</summary>
        public string Problem { get; }

        public string Stack { get; }
        public string AssetPath { get; }
        public int Line { get; }

        /// <summary>File name and line, for the row.</summary>
        public string Site { get; }

        public bool IsInvalid => Problem.Length > 0;
    }

    /// <summary>
    /// Everything the GameRebellion window shows, and every action its buttons
    /// trigger. Views only read this and call its methods; they never talk to a
    /// service, so swapping a mock for the real implementation changes nothing in
    /// the UI.
    ///
    /// Async work runs on the Editor's main thread (Unity's synchronization
    /// context) and is cancelled when the window closes or reloads.
    /// </summary>
    internal sealed class GRWindowModel : IDisposable
    {
        // Per project, in UserSettings/: which game this project is connected to. Not secret.
        internal const string GameIdKey = "GameRebellion.GameId";
        internal const string GameNameKey = "GameRebellion.GameName";
        internal const string StudioIdKey = "GameRebellion.StudioId";
        internal const string StudioNameKey = "GameRebellion.StudioName";
        private const string SdkTokenIdKey = "GameRebellion.SdkTokenId";

        // Per Editor session: the open tab survives domain reloads.
        private const string TabKey = "GameRebellion.Window.Tab";

        // What Apply did, kept across the script reload that a key change sets off.
        private const string ApplyMessageKey = "GameRebellion.Window.ApplyMessage";
        private const string ApplyMessageKindKey = "GameRebellion.Window.ApplyMessageKind";

        private readonly IGRAuthService _auth;
        private readonly IGRPlatformApi _api;
        private readonly IGRIdeConnector _ide;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private CancellationTokenSource _signIn;

        // Bumped by every game change and by sign-out, so a slow load the
        // developer has already moved past can't overwrite the newer selection.
        private int _loadVersion;
        private int _scanVersion;
        private int _agentCheckVersion;
        private int _eventsVersion = -1;
        private double _eventsPolledAt;
        private DateTime _agentsCheckedUtc = DateTime.MinValue;
        private readonly Dictionary<GRIdeClient, GRAgentStatus> _agents = new Dictionary<GRIdeClient, GRAgentStatus>();
        private bool _disposed;

        public GRWindowModel(IGRAuthService auth, IGRPlatformApi api, IGRIdeConnector ide, bool usesMocks)
        {
            _auth = auth;
            _api = api;
            _ide = ide;
            Clients = ide.Clients;
            UsesMocks = usesMocks;
            SelectedTab = (GRTab)SessionState.GetInt(TabKey, (int)GRTab.Game);

            var settings = GRProjectInfo.FindSettings(out _);
            if (settings != null)
            {
                SettingsEnvironment = settings.Environment;
                DebugLogs = settings.IsDebug;
            }

            User = _auth.CurrentUser;
            AuthPhase = User == null ? GRAuthPhase.SignedOut : GRAuthPhase.SignedIn;
            RefreshChecks();
            ScanProject();
            GRMcpBridge.StatusChanged += Raise;
            EditorApplication.update += PollEvents;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            Events.AddRange(GrEditorEventCapture.Snapshot().Select(captured => new GRCapturedEvent(captured)));
            _eventsVersion = GrEditorEventCapture.Version;

            if (IsSignedIn)
            {
                LoadGames();
                VerifySession();
                if (SelectedTab == GRTab.Mcp)
                {
                    CheckAgents();
                }
            }
        }

        /// <summary>Raised after any change; the window re-renders every view.</summary>
        public event Action Changed;

        /// <summary>Games, keys and sample events are mock data (the badge and mock-only controls).</summary>
        public bool UsesMocks { get; }

        /// <summary>Sign-in itself is simulated.</summary>
        public bool IsAuthSimulated => _auth.IsSimulated;

        /// <summary>The GameRebellion environment the developer signs in to, and that the agent's gr-mcp points at.</summary>
        public GrEnvironment AuthEnvironment => _auth.Environment;

        public GRTab SelectedTab { get; private set; }

        // Account

        public GRAuthPhase AuthPhase { get; private set; }
        public bool IsSignedIn => AuthPhase == GRAuthPhase.SignedIn;
        public GRUser User { get; private set; }
        public GRMessage AccountMessage { get; private set; }

        /// <summary>Linux without a keyring: the sign-in lasts only until Unity closes. Null when it's saved.</summary>
        public GRMessage StorageNotice
        {
            get
            {
                if (_auth.IsSimulated)
                {
                    return null;
                }

                var problem = GRCredentialStore.KeyringProblem;
                return problem == null
                    ? null
                    : new GRMessage($"Unity can't save your sign-in on this machine: no keyring answered ({problem}). " +
                                    "You'll sign in again each time Unity starts. A Secret Service keyring such as GNOME Keyring, KDE Wallet or KeePassXC fixes this.",
                        GRMessageKind.Warn);
            }
        }

        // Games

        /// <summary>The developer's studios; the games list belongs to <see cref="SelectedStudio"/>.</summary>
        public IReadOnlyList<GRStudio> Studios { get; private set; } = Array.Empty<GRStudio>();
        public GRStudio SelectedStudio { get; private set; }

        public IReadOnlyList<GRGame> Games { get; private set; } = Array.Empty<GRGame>();
        public GRGame SelectedGame { get; private set; }
        public bool IsLoadingGames { get; private set; }
        public GRMessage GamesMessage { get; private set; }

        // Game settings

        /// <summary>What the settings asset targets now. Apply sets it to <see cref="AuthEnvironment"/>.</summary>
        public GrEnvironment SettingsEnvironment { get; private set; } = GrEnvironment.Production;
        public bool DebugLogs { get; private set; }
        public bool IsKeyRevealed { get; private set; }
        public GRMessage SettingsMessage { get; private set; }

        /// <summary>The connected game's active SDK keys; null when the developer's role can't see them.</summary>
        public IReadOnlyList<GRSdkToken> SdkTokens => SelectedGame?.SdkTokens;

        /// <summary>The key picked among <see cref="SdkTokens"/> (the first unless the project chose another).</summary>
        public GRSdkToken SelectedToken { get; private set; }

        /// <summary>What the game passes to GameRebellion.Initialize; null when there isn't one to use.</summary>
        public string ApiKey => SelectedToken?.Value;

        public string ApiKeyDisplay
        {
            get
            {
                var key = ApiKey;
                if (key != null)
                {
                    return IsKeyRevealed || key.Length <= 4 ? key : "••••••••" + key.Substring(key.Length - 4);
                }

                switch (KeyAccess)
                {
                    case GRKeyAccess.NoGame: return IsLoadingGames ? "Loading…" : "None";
                    case GRKeyAccess.GameDisabled: return "None: the game is disabled";
                    case GRKeyAccess.HiddenByRole: return "Hidden for your role";
                    default: return "None";
                }
            }
        }

        public GRKeyAccess KeyAccess =>
            SelectedGame == null ? GRKeyAccess.NoGame
            : SelectedGame.IsDisabled ? GRKeyAccess.GameDisabled
            : SdkTokens == null ? GRKeyAccess.HiddenByRole
            : ApiKey == null ? GRKeyAccess.NoActiveKey
            : GRKeyAccess.Available;

        /// <summary>Why there is no key, and who in the studio can fix it; null when there is one.</summary>
        public GRMessage KeyNotice
        {
            get
            {
                var game = SelectedGame;
                var studio = SelectedStudio?.Name ?? "your studio";
                switch (KeyAccess)
                {
                    case GRKeyAccess.GameDisabled:
                        return new GRMessage(
                            $"{game.Name} is disabled by {studio}'s plan, so GameRebellion sends no SDK key for it. " +
                            "Ask a studio owner to re-enable it.", GRMessageKind.Warn);
                    case GRKeyAccess.HiddenByRole:
                        var role = string.IsNullOrEmpty(SelectedStudio?.RoleName) ? string.Empty : $" ({SelectedStudio.RoleName})";
                        return new GRMessage(
                            $"Your role in {studio}{role} can't see SDK keys. " +
                            "Ask a studio owner or admin for a role that can, or for the key itself.", GRMessageKind.Warn);
                    case GRKeyAccess.NoActiveKey:
                        return new GRMessage(
                            $"{game.Name} has no active SDK key. Ask a studio owner or admin to create one in the GameRebellion dashboard.",
                            GRMessageKind.Warn);
                    default:
                        return null;
                }
            }
        }

        /// <summary>
        /// Keys belong to the environment the developer signed in to. Says so when the SDK
        /// settings target another one, which Apply switches; null when they match.
        /// </summary>
        public string KeyEnvironmentNote =>
            ApiKey == null || SettingsEnvironment == AuthEnvironment
                ? null
                : $"Your SDK settings target {SettingsEnvironment}. Apply switches them to {AuthEnvironment}, where this key works.";

        // MCP

        public GRMcpEndpoints Endpoints => new GRMcpEndpoints(AuthEnvironment);
        public GRIdeClient? ConnectingClient { get; private set; }
        public GRMessage McpMessage { get; private set; }

        public bool IsSimulated(GRIdeClient client) => _ide.IsSimulated(client);

        /// <summary>The agents the MCP tab offers, fixed for the window's lifetime.</summary>
        public IReadOnlyList<GRIdeClient> Clients { get; }

        public bool IsCheckingAgents { get; private set; }

        /// <summary>
        /// What the agent reports for this project. Ready also needs this Editor's bridge,
        /// which the agent's own check doesn't reach: the relay answers it alone.
        /// </summary>
        public GRAgentStatus AgentStatus(GRIdeClient client)
        {
            var status = _agents.TryGetValue(client, out var known) ? known : GRAgentStatus.Checking;
            if (status.State != GRAgentState.Ready || IsSimulated(client))
            {
                return status;
            }

            if (!GRMcpBridge.IsRunning)
            {
                return new GRAgentStatus(GRAgentState.Problem, "Set up, but this Editor's bridge isn't running, so the Unity MCP can't reach it.");
            }

            return GRMcpBridge.LastCallUtc is DateTime last
                ? new GRAgentStatus(GRAgentState.Ready, $"{status.Detail} Last Unity MCP call {last.ToLocalTime():HH:mm}.")
                : status;
        }

        /// <summary>The servers Connect sets up, for the environment signed in to.</summary>
        public string ServersNote =>
            $"gr-mcp: {Endpoints.HostedServerName}, {Endpoints.HostedUrl}. Unity MCP: {GRMcpEndpoints.LocalServerName}, served by this Editor.";

        /// <summary>Why the Unity MCP can't work on this machine; null when nothing is wrong.</summary>
        public string BridgeProblem
        {
            get
            {
                if (!GRMcpBridge.IsRunning)
                {
                    return $"This Editor's bridge isn't running: {GRMcpBridge.LastError ?? "it starts with the Editor, so reopening Unity should start it."}";
                }

                if (!GRRelayInstaller.IsPackaged)
                {
                    return $"The relay isn't built for {GRRelayInstaller.Rid}: run tools/gr-editor-mcp/build.sh.";
                }

                return GRRelayInstaller.IsInstalled ? null : "The relay isn't installed yet: Connect installs it.";
            }
        }

        /// <summary>Asks each agent for its status again, in the background.</summary>
        public void RefreshAgents()
        {
            if (!_disposed && IsSignedIn)
            {
                CheckAgents();
            }
        }

        /// <summary>The developer may have just run /mcp in their agent: check again if the last check is old.</summary>
        public void OnWindowFocused()
        {
            if (!_disposed && IsSignedIn && SelectedTab == GRTab.Mcp && DateTime.UtcNow - _agentsCheckedUtc > TimeSpan.FromSeconds(10))
            {
                CheckAgents();
            }
        }

        private async void CheckAgents()
        {
            var version = ++_agentCheckVersion;
            IsCheckingAgents = true;
            Raise();

            var endpoints = Endpoints;
            var clients = Clients;
            var checks = clients.Select(async client =>
            {
                try
                {
                    return await _ide.GetStatusAsync(client, endpoints, _lifetime.Token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    return new GRAgentStatus(GRAgentState.Problem, $"Couldn't ask {ClientName(client)}: {e.Message}");
                }
            }).ToArray();

            GRAgentStatus[] results;
            try
            {
                results = await Task.WhenAll(checks);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (_disposed || version != _agentCheckVersion)
            {
                return;
            }

            for (var i = 0; i < clients.Count; i++)
            {
                _agents[clients[i]] = results[i];
            }

            _agentsCheckedUtc = DateTime.UtcNow;
            IsCheckingAgents = false;
            Raise();
        }

        // Integration and live events

        public IReadOnlyList<GRCheck> Checks { get; private set; } = Array.Empty<GRCheck>();

        /// <summary>The ListView's items source, so a List rather than a read-only view.</summary>
        public List<GRCapturedEvent> Events { get; } = new List<GRCapturedEvent>();

        public bool IsPlaying => EditorApplication.isPlaying;

        /// <summary>Bumped whenever <see cref="Events"/> is reloaded, so the list redraws only then.</summary>
        public int EventsRevision { get; private set; }

        /// <summary>Header text: how many, how many were dropped, and whether play mode is recording.</summary>
        public string EventsSummary
        {
            get
            {
                var dropped = Events.Count(captured => captured.IsInvalid);
                var count = Events.Count == 1 ? "1 event" : $"{Events.Count} events";
                var problems = dropped == 0 ? string.Empty : $", {dropped} dropped";
                return IsPlaying ? $"Recording: {count}{problems}" : Events.Count == 0 ? string.Empty : $"Last play session: {count}{problems}";
            }
        }

        public string StudioName(GRGame game) =>
            Studios.FirstOrDefault(studio => studio.Id == game.StudioId)?.Name ?? SelectedStudio?.Name ?? string.Empty;

        public static string ClientName(GRIdeClient client) => GRAgentClients.Name(client);

        public void SelectTab(GRTab tab)
        {
            if (_disposed || tab == SelectedTab)
            {
                return;
            }

            SelectedTab = tab;
            SessionState.SetInt(TabKey, (int)tab);
            if (tab == GRTab.Mcp && IsSignedIn && DateTime.UtcNow - _agentsCheckedUtc > TimeSpan.FromSeconds(20))
            {
                CheckAgents();
            }

            Raise();
        }

        // ---- Account -------------------------------------------------------------

        public async void SignIn()
        {
            if (_disposed || AuthPhase != GRAuthPhase.SignedOut)
            {
                return;
            }

            AuthPhase = GRAuthPhase.SigningIn;
            AccountMessage = null;
            _signIn = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            Raise();

            try
            {
                var user = await _auth.SignInAsync(_signIn.Token);
                if (_disposed)
                {
                    return;
                }

                User = user;
                AuthPhase = GRAuthPhase.SignedIn;
                SelectedTab = GRTab.Game;
                SessionState.SetInt(TabKey, (int)GRTab.Game);
                RefreshChecks();
                Raise();
                LoadGames();
            }
            catch (OperationCanceledException)
            {
                if (!_disposed)
                {
                    AuthPhase = GRAuthPhase.SignedOut;
                    Raise();
                }
            }
            catch (Exception e)
            {
                if (!_disposed)
                {
                    AuthPhase = GRAuthPhase.SignedOut;
                    AccountMessage = new GRMessage(e is GRAuthException ? e.Message : $"Sign-in failed: {e.Message}", GRMessageKind.Error);
                    Raise();
                }
            }
            finally
            {
                _signIn?.Dispose();
                _signIn = null;
            }
        }

        public void CancelSignIn()
        {
            _signIn?.Cancel();
        }

        /// <summary>
        /// A restored session may have been revoked or have expired since: refreshing
        /// the access token tells. Offline, the window stays signed in.
        /// </summary>
        private async void VerifySession()
        {
            try
            {
                if (await _auth.GetAccessTokenAsync(_lifetime.Token) != null || _disposed || !IsSignedIn)
                {
                    return;
                }

                EndSession(new GRMessage(GRCredentialStore.IsSecure
                    ? "Your GameRebellion session has ended. Sign in again."
                    : "Your GameRebellion session has ended. Sign in again: without a keyring, Unity keeps your sign-in only until it closes.",
                    GRMessageKind.Error));
            }
            catch (OperationCanceledException)
            {
                // The window closed.
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"[GameRebellion] Couldn't check the saved session: {e.Message}");
            }
        }

        public void SignOut()
        {
            if (_disposed || !IsSignedIn)
            {
                return;
            }

            _auth.SignOut();
            EndSession(new GRMessage("Signed out."));
        }

        private void EndSession(GRMessage message)
        {
            _loadVersion++;

            User = null;
            AuthPhase = GRAuthPhase.SignedOut;
            Studios = Array.Empty<GRStudio>();
            SelectedStudio = null;
            Games = Array.Empty<GRGame>();
            SelectedGame = null;
            SelectedToken = null;
            IsLoadingGames = false;
            IsKeyRevealed = false;
            GamesMessage = null;
            SettingsMessage = null;
            McpMessage = null;
            AccountMessage = message;
            RefreshChecks();
            Raise();
        }

        // ---- Games ---------------------------------------------------------------

        /// <summary>Connects this project to <paramref name="game"/>. A game disabled by the plan can't be newly connected.</summary>
        public void SelectGame(GRGame game)
        {
            if (_disposed || !IsSignedIn || game == null || game == SelectedGame || game.IsDisabled)
            {
                return;
            }

            ConnectGame(game);
            RefreshChecks();
            Raise();
        }

        /// <summary>Picks which of the connected game's SDK keys the project uses.</summary>
        public void SelectToken(GRSdkToken token)
        {
            if (_disposed || token == null || token == SelectedToken)
            {
                return;
            }

            SelectedToken = token;
            IsKeyRevealed = false;
            EditorUserSettings.SetConfigValue(SdkTokenIdKey, token.Id);
            SettingsMessage = null;
            RefreshChecks();
            Raise();
        }

        /// <summary>Switches to <paramref name="studio"/>'s games.</summary>
        public async void SelectStudio(GRStudio studio)
        {
            if (_disposed || !IsSignedIn || studio == null || studio == SelectedStudio)
            {
                return;
            }

            var version = BeginLoad();
            try
            {
                await LoadStudioGamesAsync(studio, version);
            }
            catch (Exception e)
            {
                FailLoad(version, e);
            }
        }

        private async void LoadGames()
        {
            var version = BeginLoad();
            try
            {
                var studios = await _api.GetStudiosAsync(_lifetime.Token);
                if (!IsCurrent(version))
                {
                    return;
                }

                Studios = studios;

                // The project's earlier studio, else the account's default, else the first.
                // A studio is safe to default; the game never is (see below).
                var savedStudioId = EditorUserSettings.GetConfigValue(StudioIdKey);
                await LoadStudioGamesAsync(
                    studios.FirstOrDefault(studio => studio.Id == savedStudioId)
                    ?? studios.FirstOrDefault(studio => studio.IsDefault)
                    ?? studios.FirstOrDefault(),
                    version);
            }
            catch (Exception e)
            {
                FailLoad(version, e);
            }
        }

        private async Task LoadStudioGamesAsync(GRStudio studio, int version)
        {
            SelectedStudio = studio;
            Games = Array.Empty<GRGame>();
            ConnectGame(null);
            if (studio == null)
            {
                EndLoad(version);
                return;
            }

            EditorUserSettings.SetConfigValue(StudioIdKey, studio.Id);
            EditorUserSettings.SetConfigValue(StudioNameKey, studio.Name);
            Raise();

            var games = await _api.GetGamesAsync(studio.Id, _lifetime.Token);
            if (!IsCurrent(version))
            {
                return;
            }

            Games = games;

            // Restore the project's earlier choice, but never pick for the developer:
            // which game a project belongs to is theirs to say. A game disabled since
            // stays connected, so the window can say why its key is gone.
            var savedGameId = EditorUserSettings.GetConfigValue(GameIdKey);
            ConnectGame(games.FirstOrDefault(game => game.Id == savedGameId));
            EndLoad(version);
        }

        /// <summary>The game and its key, which arrive together with the games list.</summary>
        private void ConnectGame(GRGame game)
        {
            SelectedGame = game;
            IsKeyRevealed = false;
            SettingsMessage = game != null ? TakeApplyMessage() : null;
            var tokens = game?.SdkTokens;
            var savedTokenId = EditorUserSettings.GetConfigValue(SdkTokenIdKey);
            SelectedToken = tokens?.FirstOrDefault(token => token.Id == savedTokenId) ?? tokens?.FirstOrDefault();
            if (game != null)
            {
                EditorUserSettings.SetConfigValue(GameIdKey, game.Id);
                EditorUserSettings.SetConfigValue(GameNameKey, game.Name);
            }
        }

        private int BeginLoad()
        {
            var version = ++_loadVersion;
            IsLoadingGames = true;
            GamesMessage = null;
            Raise();
            return version;
        }

        private bool IsCurrent(int version) => !_disposed && version == _loadVersion;

        private void EndLoad(int version)
        {
            if (!IsCurrent(version))
            {
                return;
            }

            IsLoadingGames = false;
            RefreshChecks();
            Raise();
        }

        private void FailLoad(int version, Exception e)
        {
            if (e is OperationCanceledException || !IsCurrent(version))
            {
                return;
            }

            if (e is GRApiSignInAgainException)
            {
                // The saved session is no use any more: forget it (and revoke it) for real.
                _auth.SignOut();
                EndSession(new GRMessage(e.Message, GRMessageKind.Error));
                return;
            }

            if (e is GRApiNotFoundException && SelectedStudio != null)
            {
                // A studio the developer has left: forget the project's choice so the next load picks another.
                EditorUserSettings.SetConfigValue(StudioIdKey, string.Empty);
            }

            IsLoadingGames = false;
            GamesMessage = new GRMessage(e is GRApiException ? e.Message : $"Couldn't load your games: {e.Message}", GRMessageKind.Error);
            Raise();
        }

        // ---- Game settings -------------------------------------------------------

        public void SetDebugLogs(bool enabled)
        {
            if (_disposed || enabled == DebugLogs)
            {
                return;
            }

            DebugLogs = enabled;
            SettingsMessage = null;
            Raise();
        }

        public void ToggleKeyReveal()
        {
            if (_disposed || ApiKey == null)
            {
                return;
            }

            IsKeyRevealed = !IsKeyRevealed;
            Raise();
        }

        public void CopyKey()
        {
            if (_disposed || ApiKey == null)
            {
                return;
            }

            EditorGUIUtility.systemCopyBuffer = ApiKey;
            SettingsMessage = new GRMessage("API key copied to the clipboard.", GRMessageKind.Ok);
            Raise();
        }

        /// <summary>
        /// Saves Environment (the key's, Production) and Debug logs to the settings
        /// asset, and puts the selected key into the project's GameRebellion.Initialize
        /// call when that is safe: one call, its key a string in the call or in a
        /// constant of the same file, and the developer confirms. Otherwise it says
        /// where the key has to go. An Initialize call that names another environment
        /// changes nothing.
        /// </summary>
        public void Apply()
        {
            if (_disposed || ApiKey == null)
            {
                return;
            }

            if (InitializeCalls == null)
            {
                SettingsMessage = new GRMessage("Still looking for your GameRebellion.Initialize call. Try again in a moment.");
                Raise();
                return;
            }

            var call = InitializeCalls.Count == 1 ? InitializeCalls[0] : null;
            if (call?.Environment != null && call.Environment != AuthEnvironment)
            {
                SettingsMessage = new GRMessage(
                    $"{call.Where} initializes for {call.Environment}, but the selected key is for {AuthEnvironment}. " +
                    $"Change it to GrEnvironment.{AuthEnvironment} there, then Apply again. Nothing was changed.",
                    GRMessageKind.Error);
                Raise();
                return;
            }

            var notes = new List<string>();
            var settingsNote = SaveSettings(out var settingsSaved);
            if (settingsNote != null)
            {
                notes.Add(settingsNote);
            }

            notes.Add(ApplyKey(call, out var keyApplied, out var replaced));
            SettingsMessage = new GRMessage(string.Join(" ", notes), settingsSaved && keyApplied ? GRMessageKind.Ok : GRMessageKind.Warn);
            if (replaced)
            {
                SessionState.SetString(ApplyMessageKey, SettingsMessage.Text);
                SessionState.SetInt(ApplyMessageKindKey, (int)SettingsMessage.Kind);
            }

            RefreshChecks();
            Raise();
        }

        /// <summary>Environment and Debug logs into the settings asset; null when they already match.</summary>
        private string SaveSettings(out bool saved)
        {
            saved = false;
            var settings = GRProjectInfo.FindSettings(out var path);
            if (settings == null)
            {
                return "There's no settings asset yet, so Environment and Debug logs weren't saved (Assets > Create > GameRebellion > Settings).";
            }

            if (!IsWritable(path))
            {
                return $"{path} is inside a read-only package, so Environment and Debug logs weren't saved.";
            }

            saved = true;
            SettingsEnvironment = settings.Environment;
            if (settings.Environment == AuthEnvironment && settings.IsDebug == DebugLogs)
            {
                return null;
            }

            Undo.RecordObject(settings, "Apply GameRebellion settings");
            settings.Environment = AuthEnvironment;
            settings.IsDebug = DebugLogs;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
            SettingsEnvironment = AuthEnvironment;
            return $"Saved Environment = {AuthEnvironment} and Debug logs = {(DebugLogs ? "on" : "off")} to {path}.";
        }

        private static bool IsWritable(string assetPath)
        {
            if (assetPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                return true;
            }

            var source = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath)?.source;
            return source == UnityEditor.PackageManager.PackageSource.Embedded || source == UnityEditor.PackageManager.PackageSource.Local;
        }

        /// <summary>The selected key into <paramref name="call"/>, after the developer confirms; otherwise where it goes.</summary>
        private string ApplyKey(GRInitializeCall call, out bool applied, out bool replaced)
        {
            applied = false;
            replaced = false;
            var label = SelectedToken.Label;
            if (InitializeCalls.Count == 0)
            {
                return "There's no GameRebellion.Initialize call under Assets/ yet. Copy the key into one that runs at startup, or ask your agent to add it.";
            }

            if (call == null)
            {
                return $"GameRebellion.Initialize is called in {InitializeCalls.Count} places ({Places()}), so the window changed none of them. Copy the key into the one that runs.";
            }

            if (call.Key == null)
            {
                return $"{call.Where} takes the key from {call.KeySource}, which the window can't change. Copy the key there.";
            }

            if (call.Key == ApiKey)
            {
                applied = true;
                return $"{call.Where} already uses the \"{label}\" key.";
            }

            var where = call.KeyLine == call.Line
                ? $"{call.AssetPath}, line {call.Line}"
                : $"{call.AssetPath}, line {call.KeyLine}: {call.KeySource}, used by the call on line {call.Line}";
            var confirmed = EditorUtility.DisplayDialog(
                "Replace the SDK key?",
                $"{where}\n\nNow:  {Mask(call.Key)}\nNew:  {Mask(ApiKey)}  (\"{label}\", {AuthEnvironment})\n\n" +
                "The script changes on disk and Unity recompiles it. Your version control has the old line.",
                "Replace",
                "Cancel");
            if (!confirmed)
            {
                return $"Left the key in {call.Where} as it was.";
            }

            if (!GRInitializeScanner.ReplaceKey(GRProjectInfo.ProjectPath, call, ApiKey, out var error))
            {
                return error;
            }

            // The path and line only: a key never goes to the Console.
            UnityEngine.Debug.Log($"[GameRebellion] Replaced the SDK key in {call.AssetPath}, line {call.KeyLine}, with the \"{label}\" key ({AuthEnvironment}).");
            AssetDatabase.ImportAsset(call.AssetPath);
            applied = true;
            replaced = true;
            return $"Put the \"{label}\" key into {System.IO.Path.GetFileName(call.AssetPath)}:{call.KeyLine}. Unity is recompiling.";
        }

        private GRMessage TakeApplyMessage()
        {
            var text = SessionState.GetString(ApplyMessageKey, string.Empty);
            if (text.Length == 0)
            {
                return null;
            }

            var kind = (GRMessageKind)SessionState.GetInt(ApplyMessageKindKey, (int)GRMessageKind.Ok);
            SessionState.EraseString(ApplyMessageKey);
            SessionState.EraseInt(ApplyMessageKindKey);
            return new GRMessage(text, kind);
        }

        private static string Mask(string key) =>
            key.Length == 0 ? "(empty)" : key.Length <= 4 ? "••••" : "••••" + key.Substring(key.Length - 4);

        private string Places() =>
            string.Join(", ", InitializeCalls.Take(3).Select(call => call.Where)) + (InitializeCalls.Count > 3 ? ", …" : string.Empty);

        // ---- MCP -----------------------------------------------------------------

        public async void Connect(GRIdeClient client)
        {
            if (_disposed || !IsSignedIn || ConnectingClient != null)
            {
                return;
            }

            ConnectingClient = client;
            McpMessage = null;
            Raise();

            try
            {
                McpMessage = new GRMessage(await _ide.ConnectAsync(client, Endpoints, _lifetime.Token), GRMessageKind.Ok);
            }
            catch (OperationCanceledException)
            {
                // The window closed mid-connect.
            }
            catch (Exception e)
            {
                McpMessage = new GRMessage($"Couldn't connect {ClientName(client)}: {e.Message}", GRMessageKind.Error);
            }

            if (_disposed)
            {
                return;
            }

            ConnectingClient = null;
            CheckAgents();
        }

        public void CopyConfig()
        {
            if (_disposed || !IsSignedIn)
            {
                return;
            }

            EditorGUIUtility.systemCopyBuffer = Endpoints.GenericConfig();
            McpMessage = new GRMessage("Config for both servers copied. Paste it into your client's MCP settings.", GRMessageKind.Ok);
            Raise();
        }

        // ---- Integration ---------------------------------------------------------

        public void RefreshIntegration()
        {
            if (_disposed)
            {
                return;
            }

            RefreshChecks();
            ScanProject();
            Raise();
        }

        /// <summary>The project's GameRebellion.Initialize calls; null until the first scan finishes.</summary>
        public IReadOnlyList<GRInitializeCall> InitializeCalls { get; private set; }

        /// <summary>Looks for GameRebellion.Initialize calls off the main thread; the SDK's own code is skipped.</summary>
        private async void ScanProject()
        {
            var version = ++_scanVersion;
            var assets = UnityEngine.Application.dataPath;
            var root = GRProjectInfo.SdkPackageRoot();
            var skipped = root != null && root.StartsWith("Assets/", StringComparison.Ordinal)
                ? new[] { $"{GRProjectInfo.ProjectPath}/{root}/Runtime", $"{GRProjectInfo.ProjectPath}/{root}/Editor" }.Select(folder => folder.Replace('\\', '/')).ToArray()
                : Array.Empty<string>();

            List<GRInitializeCall> calls;
            try
            {
                calls = await Task.Run(() => GRInitializeScanner.Scan(assets, skipped));
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"[GameRebellion] Couldn't look for GameRebellion.Initialize calls: {e.Message}");
                calls = new List<GRInitializeCall>();
            }

            if (_disposed || version != _scanVersion)
            {
                return;
            }

            // The SDK's demo, in the package folder or imported to Assets/Samples, only
            // counts when the project has no call of its own.
            var own = calls.Where(call => !IsSdkSample(call.AssetPath, root)).ToList();
            InitializeCalls = own.Count > 0 ? own : calls;
            RefreshChecks();
            Raise();
        }

        private static bool IsSdkSample(string assetPath, string sdkRoot) =>
            (sdkRoot != null && assetPath.StartsWith(sdkRoot + "/", StringComparison.Ordinal))
            || assetPath.StartsWith("Assets/Samples/GameRebellion SDK/", StringComparison.Ordinal);

        private void RefreshChecks()
        {
            var sdkVersion = GRProjectInfo.SdkVersion();
            var settings = GRProjectInfo.FindSettings(out var settingsPath);

            Checks = new[]
            {
                sdkVersion != null
                    ? new GRCheck("SDK installed", $"Version {sdkVersion}", GRCheckState.Done)
                    : new GRCheck("SDK installed", "Couldn't read the package version.", GRCheckState.Partial),
                settings != null
                    ? new GRCheck("Settings asset", settingsPath, GRCheckState.Done)
                    : new GRCheck("Settings asset", "None yet. Assets > Create > GameRebellion > Settings.", GRCheckState.Todo),
                SelectedGame != null
                    ? new GRCheck("Game connected", $"{SelectedGame.Name} ({StudioName(SelectedGame)})", GRCheckState.Done)
                    : new GRCheck("Game connected", "Pick the game this project belongs to.", GRCheckState.Todo),
                KeyCheck(),
                // The last step: the window has no event plan, so whether events are
                // placed and fire is the agent's job (unity_scan_project) and the Live events tab's.
                InitializeCheck(),
            };
        }

        private GRCheck InitializeCheck()
        {
            const string label = "Initialize() call";
            var calls = InitializeCalls;
            if (calls == null) return new GRCheck(label, "Looking for it…", GRCheckState.Todo);
            if (calls.Count == 0) return new GRCheck(label, "Not found under Assets/. Call GameRebellion.Initialize(apiKey) once at startup, or ask your agent to add it.", GRCheckState.Todo);
            if (calls.Count > 1) return new GRCheck(label, $"Called in {calls.Count} places: {Places()}. The window can't tell which one runs.", GRCheckState.Partial);

            var call = calls[0];
            if (ApiKey != null && call.Environment != null && call.Environment != AuthEnvironment)
            {
                return new GRCheck(label, $"{call.Where} initializes for {call.Environment}, but the selected key is for {AuthEnvironment}.", GRCheckState.Partial);
            }

            if (call.Key == null) return new GRCheck(label, $"{call.Where}, with the key from {call.KeySource}: the window can't check it.", GRCheckState.Partial);
            if (ApiKey == null) return new GRCheck(label, call.Where, GRCheckState.Done);
            return call.Key == ApiKey
                ? new GRCheck(label, $"{call.Where}, with the selected key.", GRCheckState.Done)
                : new GRCheck(label, $"{call.Where} uses a different key. Apply puts the selected one in.", GRCheckState.Partial);
        }

        private GRCheck KeyCheck()
        {
            switch (KeyAccess)
            {
                case GRKeyAccess.Available: return new GRCheck("SDK key", SelectedToken.Label, GRCheckState.Done);
                case GRKeyAccess.GameDisabled: return new GRCheck("SDK key", "None: the game is disabled by the studio's plan. Ask a studio owner.", GRCheckState.Partial);
                case GRKeyAccess.HiddenByRole: return new GRCheck("SDK key", "Hidden for your role. Ask a studio owner or admin.", GRCheckState.Partial);
                case GRKeyAccess.NoActiveKey: return new GRCheck("SDK key", "The game has no active key. Ask a studio owner or admin.", GRCheckState.Partial);
                default: return new GRCheck("SDK key", "Connect a game first.", GRCheckState.Todo);
            }
        }

        // ---- Live events ---------------------------------------------------------

        /// <summary>Mock mode only: fills the list so the event rows can be reviewed before capture exists.</summary>
        public void ClearEvents()
        {
            if (_disposed)
            {
                return;
            }

            GrEditorEventCapture.Clear();
            RefreshEvents();
        }

        /// <summary>Opens the script that sent the event, at that line.</summary>
        public void OpenEventSite(int index)
        {
            if (_disposed || index < 0 || index >= Events.Count || Events[index].AssetPath.Length == 0)
            {
                return;
            }

            var captured = Events[index];
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(captured.AssetPath);
            if (script != null)
            {
                AssetDatabase.OpenAsset(script, captured.Line);
            }
        }

        private void RefreshEvents()
        {
            _eventsVersion = GrEditorEventCapture.Version;
            Events.Clear();
            Events.AddRange(GrEditorEventCapture.Snapshot().Select(captured => new GRCapturedEvent(captured)));
            EventsRevision++;
            Raise();
        }

        // The capture can fill fast in play mode: redraw at most four times a second.
        private void PollEvents()
        {
            if (_disposed || GrEditorEventCapture.Version == _eventsVersion || EditorApplication.timeSinceStartup - _eventsPolledAt < 0.25)
            {
                return;
            }

            _eventsPolledAt = EditorApplication.timeSinceStartup;
            RefreshEvents();
        }

        private void OnPlayModeChanged(PlayModeStateChange change)
        {
            RefreshEvents();
        }

        // ---- Lifetime ------------------------------------------------------------

        private void Raise()
        {
            if (!_disposed)
            {
                Changed?.Invoke();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Changed = null;
            GRMcpBridge.StatusChanged -= Raise;
            EditorApplication.update -= PollEvents;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            _lifetime.Cancel();
            _lifetime.Dispose();
        }
    }
}
