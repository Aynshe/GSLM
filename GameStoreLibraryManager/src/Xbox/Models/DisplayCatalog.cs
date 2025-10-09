using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace GameStoreLibraryManager.Xbox.Models
{
    public class DisplayCatalogResponse
    {
        public List<Product> Products { get; set; }
    }

    public class Product
    {
        public string ProductId { get; set; }

        [JsonProperty("LocalizedProperties")]
        public List<LocalizedProperty> LocalizedProperties { get; set; }

        [JsonProperty("Properties")]
        public ProductProperties Properties { get; set; }

        public string GetPFN() => Properties?.PackageFamilyName;
        public string GetTitle() => LocalizedProperties?.FirstOrDefault()?.ProductTitle;
    }

    public class LocalizedProperty
    {
        public string ProductTitle { get; set; }
    }

    public class ProductProperties
    {
        public string PackageFamilyName { get; set; }
    }
}