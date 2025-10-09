using System.Collections.Generic;

namespace GameStoreLibraryManager.Xbox.Models
{
    public class GamePassCatalogProduct
    {
        public string id { get; set; }
        public List<string> categories { get; set; }
        public LocalizedProperties localizedProperties { get; set; }
        public MarketProperties marketProperties { get; set; }
        public GamePassProductProperties properties { get; set; }
        public List<DisplaySkuAvailability> displaySkuAvailabilities { get; set; }
    }

    public class LocalizedProperties
    {
        public string productTitle { get; set; }
        public string shortTitle { get; set; }
        public string productDescription { get; set; }
        public List<Image> images { get; set; }
    }

    public class MarketProperties
    {
        public string originalReleaseDate { get; set; }
    }

    public class GamePassProductProperties
    {
        public List<string> pfns { get; set; }
    }

    public class DisplaySkuAvailability
    {
        public Sku sku { get; set; }
    }

    public class Sku
    {
        public Properties properties { get; set; }
    }

    public class Properties
    {
        public List<FulfillmentData> fulfillmentData { get; set; }
    }

    public class FulfillmentData
    {
        public string productId { get; set; }
        public string wuCategoryId { get; set; }
    }

    public class Image
    {
        public string imageType { get; set; }
        public string uri { get; set; }
        public int height { get; set; }
        public int width { get; set; }
    }
}
