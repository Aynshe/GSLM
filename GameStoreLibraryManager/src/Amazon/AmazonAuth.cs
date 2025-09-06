using System.Collections.Generic;
using Newtonsoft.Json;

namespace GameStoreLibraryManager.Amazon
{
    // Request Models
    public class DeviceRegistrationRequest
    {
        [JsonProperty("auth_data")]
        public AuthData AuthData { get; set; } = new AuthData();

        [JsonProperty("registration_data")]
        public RegistrationData RegistrationData { get; set; } = new RegistrationData();

        [JsonProperty("requested_extensions")]
        public List<string> RequestedExtensions { get; set; } = new List<string>();

        [JsonProperty("requested_token_type")]
        public List<string> RequestedTokenType { get; set; } = new List<string>();
    }

    public class AuthData
    {
        [JsonProperty("use_global_authentication")]
        public bool UseGlobalAuthentication { get; set; }

        [JsonProperty("authorization_code")]
        public string AuthorizationCode { get; set; }

        [JsonProperty("code_verifier")]
        public string CodeVerifier { get; set; }

        [JsonProperty("code_algorithm")]
        public string CodeAlgorithm { get; set; }

        [JsonProperty("client_id")]
        public string ClientId { get; set; }

        [JsonProperty("client_domain")]
        public string ClientDomain { get; set; }
    }

    public class RegistrationData
    {
        [JsonProperty("app_name")]
        public string AppName { get; set; }

        [JsonProperty("app_version")]
        public string AppVersion { get; set; }

        [JsonProperty("device_model")]
        public string DeviceModel { get; set; }

        [JsonProperty("device_serial")]
        public string DeviceSerial { get; set; }

        [JsonProperty("device_type")]
        public string DeviceType { get; set; }

        [JsonProperty("domain")]
        public string Domain { get; set; }

        [JsonProperty("os_version")]
        public string OsVersion { get; set; }
    }

    // Response Models
    public class DeviceRegistrationResponse
    {
        [JsonProperty("response")]
        public ResponseData Response { get; set; }
    }

    public class ResponseData
    {
        [JsonProperty("success")]
        public SuccessData Success { get; set; }
    }

    public class SuccessData
    {
        [JsonProperty("tokens")]
        public TokensData Tokens { get; set; }
    }

    public class TokensData
    {
        [JsonProperty("bearer")]
        public BearerToken Bearer { get; set; }
    }

    public class BearerToken
    {
        [JsonProperty("access_token")]
        public string AccessToken { get; set; }

        [JsonProperty("refresh_token")]
        public string RefreshToken { get; set; }

        [JsonProperty("expires_in")]
        public int ExpiresIn { get; set; }
    }
}
