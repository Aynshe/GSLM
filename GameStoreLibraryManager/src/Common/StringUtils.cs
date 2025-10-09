using System.IO;
using System.Linq;

namespace GameStoreLibraryManager.Common
{
    public static class StringUtils
    {
        public static string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return string.Empty;
            }

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(c.ToString(), "");
            }
            return fileName;
        }

        public static string NormalizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            return new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLower();
        }

    }
}
