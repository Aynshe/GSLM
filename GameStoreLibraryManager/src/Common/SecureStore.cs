using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace GameStoreLibraryManager.Common
{
    public static class SecureStore
    {
        private const string Prefix = "DPAPI:";

        public static void WriteString(string path, string value, bool protect)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (!protect)
            {
                File.WriteAllText(path, value ?? string.Empty, Encoding.UTF8);
                return;
            }
            var data = Encoding.UTF8.GetBytes(value ?? string.Empty);
            var protectedBytes = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
            var payload = Prefix + Convert.ToBase64String(protectedBytes);
            File.WriteAllText(path, payload, Encoding.UTF8);
        }

        public static string ReadString(string path)
        {
            if (!File.Exists(path)) return null;
            string content = File.ReadAllText(path, Encoding.UTF8).Trim();
            if (content.StartsWith(Prefix, StringComparison.Ordinal))
            {
                var b64 = content.Substring(Prefix.Length);
                var bytes = Convert.FromBase64String(b64);
                var unprotected = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(unprotected);
            }
            return content; // plaintext backward-compat
        }

        public static bool IsProtectedFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                using var sr = new StreamReader(path, Encoding.UTF8);
                char[] buf = new char[Math.Min(16, (int)Math.Max(1, new FileInfo(path).Length))];
                int n = sr.Read(buf, 0, buf.Length);
                var head = new string(buf, 0, n);
                return head.StartsWith(Prefix, StringComparison.Ordinal);
            }
            catch { return false; }
        }
    }
}
