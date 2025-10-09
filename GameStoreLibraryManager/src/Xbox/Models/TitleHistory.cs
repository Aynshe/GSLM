using System;
using System.Collections.Generic;

namespace GameStoreLibraryManager.Xbox.Models
{
    public class TitleHistoryResponse
    {
        public List<Title> titles { get; set; }
    }

    public class Title
    {
        public string titleId { get; set; }
        public string pfn { get; set; }
        public string type { get; set; }
        public string name { get; set; }
        public List<string> devices { get; set; }
        public Detail detail { get; set; }
        public TitleHistory titleHistory { get; set; }
        public string minutesPlayed { get; set; }
    }

    public class Detail
    {
        public DateTime? releaseDate { get; set; }
        public string publisherName { get; set; }
        public string developerName { get; set; }
    }

    public class TitleHistory
    {
        public DateTime lastTimePlayed { get; set; }
    }
}