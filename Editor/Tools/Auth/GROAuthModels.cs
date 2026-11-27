using System;

namespace GameRebellionSdk.Unity.Editor
{
    // OAuth and OIDC documents, with JSON field names as the specs spell them, so
    // JsonUtility reads them directly. Fields a server leaves out stay null.

    /// <summary>The parts of /.well-known/openid-configuration the Editor uses.</summary>
    [Serializable]
    [System.Reflection.Obfuscation(Exclude = true, ApplyToMembers = true)]
    internal sealed class GROidcConfiguration
    {
        public string issuer;
        public string authorization_endpoint;
        public string token_endpoint;
        public string userinfo_endpoint;
        public string revocation_endpoint;
    }

    [Serializable]
    [System.Reflection.Obfuscation(Exclude = true, ApplyToMembers = true)]
    internal sealed class GRTokenResponse
    {
        public string access_token;
        public string token_type;
        public int expires_in;
        public string refresh_token;
        public string id_token;
        public string scope;
        public string error;
        public string error_description;
    }

    /// <summary>What /userinfo (or the ID token) says about the developer.</summary>
    [Serializable]
    [System.Reflection.Obfuscation(Exclude = true, ApplyToMembers = true)]
    internal sealed class GRClaims
    {
        public string sub;
        public string name;
        public string given_name;
        public string family_name;
        public string email;
    }
}
