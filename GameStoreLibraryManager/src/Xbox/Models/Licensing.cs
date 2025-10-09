using System.Collections.Generic;
using Newtonsoft.Json;

namespace GameStoreLibraryManager.Xbox.Models
{
    public class Entitlement
    {
        [JsonProperty("productId")]
        public string ProductId { get; set; }
    }

    public class LicensingResponse
    {
        [JsonProperty("entitlements")]
        public List<Entitlement> Entitlements { get; set; }
    }
}