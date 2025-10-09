namespace GameStoreLibraryManager.Common
{
    public class LauncherGameInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string LauncherUrl { get; set; }
        public string InstallDirectory { get; set; }
        public string ExecutableName { get; set; }
        public string Launcher { get; set; }
        public bool IsInstalled { get; set; }

        // Epic-specific fields
        public string Namespace { get; set; }
        public string CatalogItemId { get; set; }

        // Dictionnaire pour les métadonnées supplémentaires
        public Dictionary<string, string> AdditionalData { get; set; } = new Dictionary<string, string>();
    }
}
