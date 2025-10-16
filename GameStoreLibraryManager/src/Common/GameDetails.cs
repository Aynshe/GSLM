using System.Collections.Generic;

namespace GameStoreLibraryManager.Common
{
    public class GameDetails
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Developer { get; set; }
        public string Publisher { get; set; }
        public string ReleaseDate { get; set; }
        public Dictionary<string, string> MediaUrls { get; set; }
    }
}