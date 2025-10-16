using System;
using System.Collections.Generic;

namespace GameStoreLibraryManager.Xbox.Models
{
    public class XCloudTitle
    {
        public string ProductId { get; set; }
        public string TitleId { get; set; }
        public string DisplayName { get; set; }
        public List<XCloudImage> Images { get; set; }
    }

    public class XCloudImage
    {
        public string Uri { get; set; }
        public string Type { get; set; }
    }
}
