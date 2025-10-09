using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace GameStoreLibraryManager.Xbox.Models
{
    // For storing tokens locally
    public class AuthenticationData
    {
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public int ExpiresIn { get; set; }
        public DateTime CreationDate { get; set; }

        [JsonIgnore]
        public bool IsExpired => CreationDate.AddSeconds(ExpiresIn - 300) < DateTime.Now;
    }

    // For the initial OAuth token response
    public class RefreshTokenResponse
    {
        [JsonProperty("token_type")]
        public string TokenType { get; set; }
        [JsonProperty("expires_in")]
        public int ExpiresIn { get; set; }
        [JsonProperty("scope")]
        public string Scope { get; set; }
        [JsonProperty("access_token")]
        public string AccessToken { get; set; }
        [JsonProperty("refresh_token")]
        public string RefreshToken { get; set; }
        [JsonProperty("user_id")]
        public string UserId { get; set; }
    }

    // For the first step of Xbox Live auth (user.auth.xboxlive.com)
    public class AuthenticationRequest
    {
        public string RelyingParty { get; set; } = "http://auth.xboxlive.com";
        public string TokenType { get; set; } = "JWT";
        public AuthRequestProperties Properties { get; set; } = new AuthRequestProperties();

        public class AuthRequestProperties
        {
            public string AuthMethod { get; set; } = "RPS";
            public string SiteName { get; set; } = "user.auth.xboxlive.com";
            public string RpsTicket { get; set; }
        }
    }

    // For the response from both user.auth and xsts.auth
    public class AuthorizationData
    {
        public DateTime IssueInstant { get; set; }
        public DateTime NotAfter { get; set; }
        public string Token { get; set; }
        public DisplayClaims DisplayClaims { get; set; }
    }

    public class DisplayClaims
    {
        [JsonProperty("xui")]
        public List<Xui> Xui { get; set; }
    }

    public class Xui
    {
        [JsonProperty("uhs")]
        public string Userhash { get; set; }
        [JsonProperty("gtg")]
        public string Gamertag { get; set; }
        [JsonProperty("xid")]
        public string XboxUserId { get; set; }
    }

    // For the second step of Xbox Live auth (xsts.auth.xboxlive.com)
    // This uses a specific, smaller properties object to avoid the 400 Bad Request error.
    public class XSTSAuthorizationRequest
    {
        public string RelyingParty { get; set; } = "http://xboxlive.com";
        public string TokenType { get; set; } = "JWT";
        public XSTSProperties Properties { get; set; } = new XSTSProperties();
    }

    public class XSTSProperties
    {
        public List<string> UserTokens { get; set; }
        public string SandboxId { get; set; } = "RETAIL";
    }
}