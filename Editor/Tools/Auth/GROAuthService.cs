using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using UnityEditor;
using UnityEngine;

namespace GameRebellionSdk.Unity.Editor
{
    /// <summary>
    /// The Editor's sign-in, per gr-backend's "Unity plugin client" contract
    /// (EDITOR_MCP_ARCHITECTURE.md §4): OAuth 2.1 authorization code with PKCE, in
    /// the system browser, with a loopback redirect (RFC 8252), as the first-party
    /// public client <c>gr-unity</c>, for <c>resource=https://api.gamerebellion.com</c>.
    ///
    /// The refresh token lives in the OS credential store and rotates on every use;
    /// the access token (15 minutes) only in memory and SessionState, so it survives
    /// domain reloads but not a restart. Tokens are never logged.
    /// </summary>
    internal sealed class GROAuthService : IGRAuthService
    {
        private static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(1);

        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        private readonly Dictionary<string, GROidcConfiguration> _configurations = new Dictionary<string, GROidcConfiguration>();

        // One refresh at a time, shared by every caller: a rotated refresh token reused
        // after the server's 10-second grace window revokes the whole session.
        private Task<string> _refreshing;
        private string _userAgent;

        public GROAuthService()
        {
            ForgetLegacySession();
        }

        public bool IsSimulated => false;

        public GrEnvironment Environment => GRAuthConfig.SignInEnvironment;

        // Per environment, so a Development session never answers for Production.
        private string PrefKey(string name) => $"GameRebellion.Auth.{Environment}.{name}";
        private string RefreshTokenAccount => $"{Environment}.refresh_token";

        /// <summary>From the saved profile, without touching the credential store; a refresh tells whether the session still holds.</summary>
        public GRUser CurrentUser
        {
            get
            {
                var json = EditorPrefs.GetString(PrefKey("User"), string.Empty);
                if (json.Length == 0)
                {
                    json = SessionState.GetString(PrefKey("User"), string.Empty);
                }

                return json.Length == 0 ? null : JsonUtility.FromJson<GRUser>(json);
            }
        }

        public async Task<GRUser> SignInAsync(CancellationToken cancellationToken)
        {
            var configuration = await DiscoverAsync(cancellationToken);

            using (var listener = GRLoopbackListener.Start())
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(SignInTimeout);
                var verifier = GROAuthUtil.NewVerifier();
                var state = GROAuthUtil.NewState();

                // The system browser, never an embedded view: the Editor never sees the password.
                Application.OpenURL(configuration.authorization_endpoint + "?" + Form(
                    ("response_type", "code"),
                    ("client_id", GRAuthConfig.ClientId),
                    ("redirect_uri", listener.RedirectUri),
                    ("code_challenge", GROAuthUtil.Challenge(verifier)),
                    ("code_challenge_method", "S256"),
                    ("state", state),
                    ("resource", GRAuthConfig.ApiResource),
                    ("scope", GRAuthConfig.Scopes)));

                Dictionary<string, string> callback;
                try
                {
                    callback = await listener.WaitForCallbackAsync(timeout.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new GRAuthException($"Sign-in timed out after {SignInTimeout.TotalMinutes:0} minutes. Try again.");
                }

                if (!callback.TryGetValue("state", out var returnedState) || returnedState != state)
                {
                    throw new GRAuthException("The sign-in answer didn't match this request, so it was ignored. Try again.");
                }

                // RFC 9207: the answer must come from the server this request went to.
                if (!callback.TryGetValue("iss", out var issuer) || !SameIssuer(issuer, configuration.issuer))
                {
                    throw new GRAuthException("The sign-in answer came from an unexpected server, so it was ignored. Try again.");
                }

                if (callback.TryGetValue("error", out var error))
                {
                    callback.TryGetValue("error_description", out var description);
                    throw new GRAuthException(error == "access_denied"
                        ? "Sign-in was cancelled in the browser."
                        : $"Sign-in failed: {description ?? error}.");
                }

                if (!callback.TryGetValue("code", out var code) || code.Length == 0)
                {
                    throw new GRAuthException("The sign-in answer had no authorization code. Try again.");
                }

                // The code is short-lived (about a minute): exchange it straight away.
                var tokens = await PostFormAsync<GRTokenResponse>(configuration.token_endpoint, cancellationToken,
                    ("grant_type", "authorization_code"),
                    ("client_id", GRAuthConfig.ClientId),
                    ("code", code),
                    ("redirect_uri", listener.RedirectUri),
                    ("code_verifier", verifier),
                    ("resource", GRAuthConfig.ApiResource));
                ThrowIfError(tokens, "Couldn't finish signing in");
                if (string.IsNullOrEmpty(tokens.refresh_token))
                {
                    throw new GRAuthException("The sign-in server returned no refresh token, so the session couldn't be kept.");
                }

                StoreTokens(tokens);
                var user = await ReadUserAsync(configuration, tokens, cancellationToken);
                SaveUser(user);
                return user;
            }
        }

        public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken, bool forceRefresh = false)
        {
            if (!forceRefresh)
            {
                var token = SessionState.GetString(PrefKey("AccessToken"), string.Empty);
                if (token.Length > 0 && DateTime.TryParse(SessionState.GetString(PrefKey("AccessTokenExpiry"), string.Empty),
                        CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiry) && DateTime.UtcNow + RefreshMargin < expiry)
                {
                    return token;
                }
            }

            if (_refreshing == null)
            {
                _refreshing = RefreshAsync();
            }

            var refreshing = _refreshing;
            try
            {
                return await refreshing;
            }
            finally
            {
                if (_refreshing == refreshing)
                {
                    _refreshing = null;
                }
            }
        }

        public void SignOut()
        {
            string refreshToken = null;
            try
            {
                refreshToken = GRCredentialStore.Read(RefreshTokenAccount);
            }
            catch (InvalidOperationException e)
            {
                Debug.LogWarning($"[GameRebellion] {e.Message}");
            }

            var environment = Environment;
            ForgetSession();
            if (!string.IsNullOrEmpty(refreshToken))
            {
                _ = RevokeAsync(environment, refreshToken);
            }
        }

        // ---- Steps -----------------------------------------------------------------

        private async Task<GROidcConfiguration> DiscoverAsync(CancellationToken cancellationToken)
        {
            var baseUrl = GRAuthConfig.AuthBaseUrl(Environment);
            if (_configurations.TryGetValue(baseUrl, out var known))
            {
                return known;
            }

            var configuration = await SendAsync<GROidcConfiguration>(new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/.well-known/openid-configuration"), cancellationToken);
            if (string.IsNullOrEmpty(configuration.issuer) || string.IsNullOrEmpty(configuration.authorization_endpoint) || string.IsNullOrEmpty(configuration.token_endpoint))
            {
                throw new GRAuthException($"{baseUrl} doesn't publish an OpenID configuration. Try again in a moment.");
            }

            // The contract fixes the paths; discovery may leave revocation out.
            if (string.IsNullOrEmpty(configuration.revocation_endpoint))
            {
                configuration.revocation_endpoint = $"{baseUrl}/oauth/revoke/";
            }

            _configurations[baseUrl] = configuration;
            return configuration;
        }

        private async Task<string> RefreshAsync()
        {
            string refreshToken;
            try
            {
                refreshToken = GRCredentialStore.Read(RefreshTokenAccount);
            }
            catch (InvalidOperationException e)
            {
                throw new GRAuthException(e.Message);
            }

            if (string.IsNullOrEmpty(refreshToken))
            {
                ForgetSession();
                return null;
            }

            var configuration = await DiscoverAsync(CancellationToken.None);
            var tokens = await PostFormAsync<GRTokenResponse>(configuration.token_endpoint, CancellationToken.None,
                ("grant_type", "refresh_token"),
                ("client_id", GRAuthConfig.ClientId),
                ("refresh_token", refreshToken),
                ("resource", GRAuthConfig.ApiResource));
            if (tokens.error == "invalid_grant")
            {
                ForgetSession(); // Expired (30 days unused), revoked, or reused after rotation.
                return null;
            }

            ThrowIfError(tokens, "Couldn't refresh the GameRebellion session");
            StoreTokens(tokens); // Before anything else: the old refresh token is now spent.
            return tokens.access_token;
        }

        /// <summary>Who signed in, from /userinfo; the ID token (same claims) if that call fails.</summary>
        private async Task<GRUser> ReadUserAsync(GROidcConfiguration configuration, GRTokenResponse tokens, CancellationToken cancellationToken)
        {
            GRClaims claims = null;
            if (!string.IsNullOrEmpty(configuration.userinfo_endpoint))
            {
                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, configuration.userinfo_endpoint);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.access_token);
                    claims = await SendAsync<GRClaims>(request, cancellationToken);
                }
                catch (GRAuthException e)
                {
                    Debug.LogWarning($"[GameRebellion] {e.Message} Reading the profile from the ID token instead.");
                }
            }

            if (string.IsNullOrEmpty(claims?.email) && string.IsNullOrEmpty(claims?.sub))
            {
                claims = GROAuthUtil.ReadClaims(tokens.id_token) ?? new GRClaims();
            }

            var fullName = $"{claims.given_name} {claims.family_name}".Trim();
            return new GRUser
            {
                Email = claims.email ?? string.Empty,
                Name = !string.IsNullOrEmpty(claims.name) ? claims.name : fullName.Length > 0 ? fullName : claims.email ?? string.Empty,
                Studio = string.Empty, // Comes with /me and studios:read, once the API accepts these tokens.
            };
        }

        private async Task RevokeAsync(GrEnvironment environment, string refreshToken)
        {
            try
            {
                var baseUrl = GRAuthConfig.AuthBaseUrl(environment);
                var endpoint = _configurations.TryGetValue(baseUrl, out var known) ? known.revocation_endpoint : $"{baseUrl}/oauth/revoke/";
                await PostFormAsync<GRTokenResponse>(endpoint, CancellationToken.None,
                    ("token", refreshToken),
                    ("token_type_hint", "refresh_token"),
                    ("client_id", GRAuthConfig.ClientId));
            }
            catch (Exception e)
            {
                // Signed out locally either way; an unrevoked refresh token expires after 30 days unused.
                Debug.LogWarning($"[GameRebellion] Couldn't revoke the session on the server: {e.Message}");
            }
        }

        // ---- Storage ---------------------------------------------------------------

        private void StoreTokens(GRTokenResponse tokens)
        {
            if (!string.IsNullOrEmpty(tokens.refresh_token))
            {
                try
                {
                    GRCredentialStore.Write(RefreshTokenAccount, tokens.refresh_token);
                }
                catch (InvalidOperationException e)
                {
                    throw new GRAuthException(e.Message);
                }
            }

            SessionState.SetString(PrefKey("AccessToken"), tokens.access_token ?? string.Empty);
            var lifetime = tokens.expires_in > 0 ? tokens.expires_in : 900;
            SessionState.SetString(PrefKey("AccessTokenExpiry"), DateTime.UtcNow.AddSeconds(lifetime).ToString("o", CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// The profile lasts as long as the refresh token: across restarts when that's in
        /// a credential store, otherwise (Linux without a keyring) until Unity closes, so
        /// the window doesn't greet a restarted Editor as signed in.
        /// </summary>
        private void SaveUser(GRUser user)
        {
            var json = JsonUtility.ToJson(user);
            if (GRCredentialStore.IsSecure)
            {
                EditorPrefs.SetString(PrefKey("User"), json);
                SessionState.EraseString(PrefKey("User"));
            }
            else
            {
                SessionState.SetString(PrefKey("User"), json);
                EditorPrefs.DeleteKey(PrefKey("User"));
            }
        }

        private void ForgetSession()
        {
            SessionState.EraseString(PrefKey("AccessToken"));
            SessionState.EraseString(PrefKey("AccessTokenExpiry"));
            SessionState.EraseString(PrefKey("User"));
            EditorPrefs.DeleteKey(PrefKey("User"));
            try
            {
                GRCredentialStore.Delete(RefreshTokenAccount);
            }
            catch (InvalidOperationException e)
            {
                Debug.LogWarning($"[GameRebellion] {e.Message}");
            }
        }

        /// <summary>
        /// An earlier build of this window kept a refresh token and a client registration
        /// in EditorPrefs. Clear them: EditorPrefs isn't encrypted. A later one kept the
        /// refresh token there on Linux: move that to the keyring.
        /// </summary>
        private static void ForgetLegacySession()
        {
            foreach (GrEnvironment environment in Enum.GetValues(typeof(GrEnvironment)))
            {
                GRCredentialStore.MoveOutOfEditorPrefs($"{environment}.refresh_token");

                foreach (var name in new[] { "RefreshToken", "Client" })
                {
                    var key = $"GameRebellion.Auth.{environment}.{name}";
                    if (EditorPrefs.HasKey(key))
                    {
                        EditorPrefs.DeleteKey(key);
                        EditorPrefs.DeleteKey($"GameRebellion.Auth.{environment}.User");
                    }
                }
            }
        }

        // ---- HTTP ------------------------------------------------------------------

        private Task<T> PostFormAsync<T>(string url, CancellationToken cancellationToken, params (string Key, string Value)[] fields)
        {
            var content = new FormUrlEncodedContent(Array.ConvertAll(fields, field => new KeyValuePair<string, string>(field.Key, field.Value)));
            return SendAsync<T>(new HttpRequestMessage(HttpMethod.Post, url) { Content = content }, cancellationToken);
        }

        /// <summary>
        /// Sends with a named User-Agent (Cloudflare in front of the token endpoint
        /// blocks some anonymous HTTP clients with error 1010), and parses the JSON body,
        /// error responses included: OAuth errors arrive as 400s with <c>error</c>.
        /// </summary>
        private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("User-Agent", _userAgent ?? (_userAgent = $"GameRebellion-Unity/{GRProjectInfo.SdkVersion() ?? "dev"} (Unity {Application.unityVersion})"));

            HttpResponseMessage response;
            try
            {
                response = await Http.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException e)
            {
                throw new GRAuthException($"Couldn't reach {request.RequestUri.Host}: {e.Message}");
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new GRAuthException($"{request.RequestUri.Host} didn't answer within {Http.Timeout.TotalSeconds:0} s.");
            }
            finally
            {
                request.Dispose();
            }

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync();
                if (body.TrimStart().StartsWith("{", StringComparison.Ordinal))
                {
                    return JsonUtility.FromJson<T>(body);
                }

                if (response.IsSuccessStatusCode && body.Trim().Length == 0)
                {
                    return JsonUtility.FromJson<T>("{}"); // Revocation answers 200 with no body.
                }

                var hint = body.Contains("1010") ? " Cloudflare refused this client (error 1010): tell the GameRebellion backend team." : string.Empty;
                throw new GRAuthException($"{request.RequestUri.Host} answered {(int)response.StatusCode} {response.ReasonPhrase}.{hint}");
            }
        }

        private static void ThrowIfError(GRTokenResponse tokens, string context)
        {
            if (string.IsNullOrEmpty(tokens.error))
            {
                return;
            }

            throw new GRAuthException(tokens.error == "invalid_target"
                ? $"{context}: the server rejected the API resource ({GRAuthConfig.ApiResource})."
                : $"{context}: {tokens.error_description ?? tokens.error}.");
        }

        private static bool SameIssuer(string a, string b) =>
            string.Equals(a?.TrimEnd('/'), b?.TrimEnd('/'), StringComparison.Ordinal);

        private static string Form(params (string Key, string Value)[] fields)
        {
            var query = new StringBuilder();
            foreach (var (key, value) in fields)
            {
                if (query.Length > 0) query.Append('&');
                query.Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(value));
            }

            return query.ToString();
        }
    }

    /// <summary>A sign-in failure with a message meant for the developer.</summary>
    internal sealed class GRAuthException : Exception
    {
        public GRAuthException(string message) : base(message)
        {
        }
    }
}
