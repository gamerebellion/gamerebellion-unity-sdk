using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GameRebellionSdk.Unity.Editor
{
    /// <summary>One of the developer's studios (GET /v1/studios/).</summary>
    internal sealed class GRStudio
    {
        public GRStudio(string id, string name, string role = null, bool isDefault = false, string roleName = null)
        {
            Id = id;
            Name = name;
            Role = role ?? string.Empty;
            RoleName = string.IsNullOrEmpty(roleName) ? Role : roleName;
            IsDefault = isDefault;
        }

        public string Id { get; }
        public string Name { get; }

        /// <summary>owner, admin, member or viewer; empty when the API doesn't say.</summary>
        public string Role { get; }

        /// <summary>role_name, the role as the dashboard shows it ("Viewer"); falls back to <see cref="Role"/>.</summary>
        public string RoleName { get; }

        /// <summary>The studio picked when the project hasn't chosen one.</summary>
        public bool IsDefault { get; }
    }

    /// <summary>An active SDK key of a game (sdk_tokens).</summary>
    internal sealed class GRSdkToken
    {
        public GRSdkToken(string id, string label, string value, string created)
        {
            Id = id;
            Label = label;
            Value = value;
            Created = created;
        }

        public string Id { get; }
        public string Label { get; }

        /// <summary>What the game passes to GameRebellion.Initialize(apiKey).</summary>
        public string Value { get; }

        public string Created { get; }
    }

    /// <summary>One of a studio's connected games (GET /v1/studios/{id}/games/, newest first).</summary>
    internal sealed class GRGame
    {
        public GRGame(string id, string studioId, string name, string genre, string platform = null,
            bool isDisabled = false, IReadOnlyList<GRSdkToken> sdkTokens = null, string iconUrl = null, string storePageUrl = null, string created = null)
        {
            Id = id;
            StudioId = studioId;
            Name = name;
            Genre = genre ?? string.Empty;
            Platform = platform ?? string.Empty;
            IsDisabled = isDisabled;
            SdkTokens = sdkTokens;
            IconUrl = iconUrl;
            StorePageUrl = storePageUrl;
            Created = created;
        }

        public string Id { get; }
        public string StudioId { get; }
        public string Name { get; }

        /// <summary>main_genre; empty when the API doesn't say.</summary>
        public string Genre { get; }

        /// <summary>The platforms, joined ("ios, android"); empty when the API doesn't say.</summary>
        public string Platform { get; }

        /// <summary>is_disabled: switched off by the studio's plan. The server sends no SDK key for it.</summary>
        public bool IsDisabled { get; }

        /// <summary>
        /// The game's active SDK keys. Null when the server withholds them from the
        /// developer's role; empty when the game has no active key.
        /// </summary>
        public IReadOnlyList<GRSdkToken> SdkTokens { get; }

        public string IconUrl { get; }
        public string StorePageUrl { get; }
        public string Created { get; }
    }

    /// <summary>
    /// The GameRebellion app API, read with the signed-in developer's token
    /// (EDITOR_BACKEND_REQUIREMENTS.md). Read-only: the Editor's tokens can't write.
    /// </summary>
    internal interface IGRPlatformApi
    {
        Task<IReadOnlyList<GRStudio>> GetStudiosAsync(CancellationToken cancellationToken);

        /// <summary>The studio's connected games, newest first, each with its SDK keys when the role allows.</summary>
        Task<IReadOnlyList<GRGame>> GetGamesAsync(string studioId, CancellationToken cancellationToken);
    }

    /// <summary>An API call that failed, with a message meant for the developer.</summary>
    internal class GRApiException : Exception
    {
        public GRApiException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// The developer has to sign in again: the session ended (401 after a refresh), or the
    /// token predates a scope the API now needs (403 insufficient_scope).
    /// </summary>
    internal sealed class GRApiSignInAgainException : GRApiException
    {
        public GRApiSignInAgainException(string message) : base(message)
        {
        }
    }

    /// <summary>404: the developer isn't a member of that studio (any more).</summary>
    internal sealed class GRApiNotFoundException : GRApiException
    {
        public GRApiNotFoundException(string message) : base(message)
        {
        }
    }
}
