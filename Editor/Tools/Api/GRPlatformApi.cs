using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;

namespace GameRebellionSdk.Unity.Editor
{
    /// <summary>
    /// The GameRebellion app API, read with the window's <c>gr-unity</c> token, which
    /// needs the <c>studios:read</c> scope (EDITOR_BACKEND_REQUIREMENTS.md):
    ///
    /// - <c>GET /v1/studios/</c>: the developer's studios, with their role; <c>is_default</c> marks one.
    /// - <c>GET /v1/studios/{id}/games/</c>: that studio's connected games, newest first,
    ///   with their SDK keys (<c>sdk_tokens</c>) for owners, admins and members.
    ///
    /// The studio is in the URL, so no <c>X-GR-Studio</c> header (a mismatching one is a 400).
    /// Answers: 401 after one refresh means the session ended; 403 insufficient_scope
    /// means the token predates <c>studios:read</c>; both mean signing in again. 404
    /// means the developer isn't a member of that studio.
    ///
    /// Fields are read leniently (a bare array or DRF's {results, next}, numeric or
    /// text ids), and each call's shape is described, without values, in
    /// Library/GameRebellion/api-shapes.json.
    /// </summary>
    internal sealed class GRPlatformApi : IGRPlatformApi
    {
        private const int MaxPages = 20;

        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        private readonly IGRAuthService _auth;
        private string _userAgent;

        public GRPlatformApi(IGRAuthService auth)
        {
            _auth = auth;
        }

        private string BaseUrl => GRAuthConfig.ApiBaseUrl(_auth.Environment);

        public async Task<IReadOnlyList<GRStudio>> GetStudiosAsync(CancellationToken cancellationToken)
        {
            var studios = new List<GRStudio>();
            foreach (var item in await GetListAsync("studios", "/v1/studios/", cancellationToken))
            {
                var id = GRJson.Text(item, "id", "uuid", "pk");
                if (id != null)
                {
                    studios.Add(new GRStudio(
                        id,
                        GRJson.Text(item, "name", "title", "display_name", "slug") ?? id,
                        GRJson.Text(item, "role") ?? GRJson.Text(GRJson.GetObject(item, "role"), "name", "code"),
                        GRJson.Bool(item, "is_default"),
                        GRJson.Text(item, "role_name")));
                }
            }

            return studios;
        }

        public async Task<IReadOnlyList<GRGame>> GetGamesAsync(string studioId, CancellationToken cancellationToken)
        {
            var games = new List<GRGame>();
            var path = $"/v1/studios/{Uri.EscapeDataString(studioId)}/games/";
            foreach (var item in await GetListAsync("studio-games", path, cancellationToken))
            {
                var id = GRJson.Text(item, "id", "uuid", "pk");
                if (id == null)
                {
                    continue;
                }

                games.Add(new GRGame(
                    id,
                    studioId,
                    GRJson.Text(item, "name", "title") ?? id,
                    GRJson.Text(item, "main_genre") ?? GRJson.Text(GRJson.GetObject(item, "main_genre"), "name", "title"),
                    Join(GRJson.GetArray(item, "platforms")) ?? GRJson.Text(item, "platform"),
                    GRJson.Bool(item, "is_disabled"),
                    Tokens(item),
                    GRJson.Text(item, "icon", "icon_url"),
                    GRJson.Text(item, "store_page_url"),
                    GRJson.Text(item, "created")));
            }

            return games;
        }

        /// <summary>
        /// sdk_tokens: active keys only. The server filters them by role, by leaving the
        /// field out or, possibly, by sending keys without a token_value. Either way the
        /// developer can't see them: null. An empty array means the game has no active key.
        /// </summary>
        private static IReadOnlyList<GRSdkToken> Tokens(Dictionary<string, object> game)
        {
            var entries = GRJson.GetArray(game, "sdk_tokens");
            if (entries == null)
            {
                return null;
            }

            var tokens = entries
                .OfType<Dictionary<string, object>>()
                .Select(token => new GRSdkToken(
                    GRJson.Text(token, "id") ?? string.Empty,
                    GRJson.Text(token, "token_label") ?? "SDK key",
                    GRJson.Text(token, "token_value"),
                    GRJson.Text(token, "created")))
                .Where(token => !string.IsNullOrEmpty(token.Value))
                .ToList();
            return tokens.Count == 0 && entries.Count > 0 ? null : tokens;
        }

        /// <summary>All items of a list endpoint, following DRF's "next" links.</summary>
        private async Task<List<Dictionary<string, object>>> GetListAsync(string name, string path, CancellationToken cancellationToken)
        {
            var items = new List<Dictionary<string, object>>();
            object firstPage = null;
            var url = BaseUrl + path;
            for (var page = 0; url != null && page < MaxPages; page++)
            {
                var root = await GetJsonAsync(name, url, path, cancellationToken);
                firstPage = firstPage ?? root;
                var envelope = root as Dictionary<string, object>;
                var list = root as List<object>
                           ?? GRJson.GetArray(envelope, "results") ?? GRJson.GetArray(envelope, "data") ?? GRJson.GetArray(envelope, "items");
                if (list == null)
                {
                    GRApiShapes.Record(name, path, root, items);
                    throw new GRApiException($"{path} answered in a shape the window doesn't know yet. See Library/GameRebellion/api-shapes.json.");
                }

                items.AddRange(list.OfType<Dictionary<string, object>>());
                url = GRJson.Text(envelope, "next");
            }

            GRApiShapes.Record(name, path, firstPage, items);
            return items;
        }

        private async Task<object> GetJsonAsync(string name, string url, string path, CancellationToken cancellationToken)
        {
            for (var attempt = 0; ; attempt++)
            {
                // A 401 gets one retry with a freshly refreshed token.
                var token = await _auth.GetAccessTokenAsync(cancellationToken, forceRefresh: attempt > 0);
                if (token == null)
                {
                    throw new GRApiSignInAgainException("Your GameRebellion session has ended. Sign in again.");
                }

                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

                    HttpResponseMessage response;
                    try
                    {
                        response = await Http.SendAsync(request, cancellationToken);
                    }
                    catch (HttpRequestException e)
                    {
                        throw new GRApiException($"Couldn't reach {request.RequestUri.Host}: {e.Message}");
                    }
                    catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        throw new GRApiException($"{request.RequestUri.Host} didn't answer within {Http.Timeout.TotalSeconds:0} s.");
                    }

                    using (response)
                    {
                        var body = await response.Content.ReadAsStringAsync();
                        if (response.IsSuccessStatusCode)
                        {
                            try
                            {
                                return GRJson.Parse(body);
                            }
                            catch (FormatException)
                            {
                                throw new GRApiException($"{path} didn't answer with JSON.");
                            }
                        }

                        if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                        {
                            continue;
                        }

                        var error = ReadError(body);
                        var detail = error.Detail ?? response.ReasonPhrase;
                        GRApiShapes.RecordFailure(name, path, (int)response.StatusCode, error.Code == null ? detail : $"{detail} ({error.Code})");

                        switch (response.StatusCode)
                        {
                            case HttpStatusCode.Unauthorized:
                                throw new GRApiSignInAgainException("Your GameRebellion session has ended. Sign in again.");
                            case HttpStatusCode.Forbidden when error.Code == "insufficient_scope" || ChallengeError(response) == "insufficient_scope":
                                throw new GRApiSignInAgainException("Sign in again to let the Editor read your studios: your sign-in is from before that permission existed.");
                            case HttpStatusCode.Forbidden:
                                // Filtered by role: the API's own detail stays in api-shapes.json.
                                throw new GRApiException("Your role in this studio doesn't allow this. Ask a studio owner or admin for access.");
                            case HttpStatusCode.NotFound:
                                throw new GRApiNotFoundException("You're not a member of that studio any more.");
                            default:
                                throw new GRApiException($"{path} answered {(int)response.StatusCode}: {detail}");
                        }
                    }
                }
            }
        }

        private string UserAgent => _userAgent ?? (_userAgent = $"GameRebellion-Unity/{GRProjectInfo.SdkVersion() ?? "dev"} (Unity {Application.unityVersion})");

        /// <summary>The error code a 401/403 names in WWW-Authenticate (RFC 6750), e.g. insufficient_scope.</summary>
        private static string ChallengeError(HttpResponseMessage response)
        {
            foreach (var challenge in response.Headers.WwwAuthenticate)
            {
                var parameter = challenge.Parameter ?? string.Empty;
                var start = parameter.IndexOf("error=\"", StringComparison.Ordinal);
                if (start >= 0)
                {
                    start += "error=\"".Length;
                    var end = parameter.IndexOf('"', start);
                    if (end > start) return parameter.Substring(start, end - start);
                }
            }

            return null;
        }

        /// <summary>DRF's {"detail", "code"}, or OAuth's {"error", "error_description"}.</summary>
        private static (string Detail, string Code) ReadError(string body)
        {
            try
            {
                var root = GRJson.Parse(body) as Dictionary<string, object>;
                return (GRJson.Text(root, "detail", "error_description", "message", "error"), GRJson.Text(root, "code", "error"));
            }
            catch (FormatException)
            {
                return (null, null);
            }
        }

        private static string Join(List<object> values)
        {
            if (values == null)
            {
                return null;
            }

            var names = values
                .Select(value => value as string ?? GRJson.Text(value as Dictionary<string, object>, "name", "platform", "slug"))
                .Where(name => !string.IsNullOrEmpty(name));
            var joined = string.Join(", ", names);
            return joined.Length == 0 ? null : joined;
        }
    }
}
