using System.Net.Http;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;

namespace GameStoreLibraryManager.Common
{
    public class MediaDownloader
    {
        private readonly HttpClient _httpClient;
        private readonly SimpleLogger _logger;
        private static readonly List<string> SupportedExtensions = new List<string> { ".jpg", ".png", ".gif", ".mp4", ".webm", ".mpd", ".m3u8" };


        public MediaDownloader(SimpleLogger logger)
        {
            _logger = logger;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537.36");
        }

        public async Task<string> DownloadMedia(string url, string baseFilePath)
        {
            // Check for existing files with any supported extension before making a network request
            foreach (var ext in SupportedExtensions)
            {
                var potentialPath = baseFilePath + ext;
                if (File.Exists(potentialPath))
                {
                    _logger.Log($"  [Downloader] Media already exists locally: {potentialPath}");
                    return potentialPath;
                }
            }

            var response = await _httpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var extension = Path.GetExtension(baseFilePath);
                var contentType = response.Content.Headers.ContentType?.MediaType;
                _logger.Log($"  [Downloader] URL: {url}, Content-Type: {contentType ?? "N/A"}");

                if (string.IsNullOrEmpty(extension))
                {
                    extension = GetExtensionFromContentType(contentType);
                }

                var finalFilePath = baseFilePath + extension;

                // This check is somewhat redundant now but kept as a safeguard
                if (File.Exists(finalFilePath))
                {
                    return finalFilePath;
                }

                var directory = Path.GetDirectoryName(finalFilePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using (var fs = new FileStream(finalFilePath, FileMode.Create))
                {
                    await response.Content.CopyToAsync(fs);
                }
                return finalFilePath;
            }
            return null;
        }

        private string GetExtensionFromContentType(string contentType)
        {
            if (string.IsNullOrEmpty(contentType)) return ".jpg"; // Default extension

            switch (contentType.ToLower())
            {
                case "image/jpeg":
                    return ".jpg";
                case "image/png":
                    return ".png";
                case "image/gif":
                    return ".gif";
                case "video/mp4":
                    return ".mp4";
                case "video/webm":
                    return ".webm";
                case "application/dash+xml":
                    return ".mpd";
                case "application/x-mpegurl":
                case "application/vnd.apple.mpegurl":
                    return ".m3u8";
                default:
                    return ".jpg"; // Default to jpg for unknown image types
            }
        }
    }
}
