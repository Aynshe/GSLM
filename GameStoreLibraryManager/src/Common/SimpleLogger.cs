using System;
using System.IO;
using System.Threading;

namespace GameStoreLibraryManager.Common
{
    public class SimpleLogger
    {
        private static readonly object Sync = new object();
        private readonly string logFilePath;
        private readonly string debugLogFilePath;

        public SimpleLogger(string logFileName, bool append = false)
        {
            logFilePath = Path.Combine(AppContext.BaseDirectory, logFileName);
            debugLogFilePath = Path.Combine(AppContext.BaseDirectory, "debug.log");
            if (append)
            {
                if (!File.Exists(logFilePath))
                {
                    WriteAllTextWithRetry(logFilePath, $"Log created at {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
                }
                if (!File.Exists(debugLogFilePath))
                {
                    WriteAllTextWithRetry(debugLogFilePath, $"Debug log created at {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
                }
            }
            else
            {
                // Clear the log files at the start of a new session
                DeleteWithRetry(logFilePath);
                WriteAllTextWithRetry(logFilePath, $"Log started at {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n\n");

                DeleteWithRetry(debugLogFilePath);
                WriteAllTextWithRetry(debugLogFilePath, $"Debug log started at {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n\n");
            }
        }

        public void Log(string message)
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            Console.WriteLine(line);
            try
            {
                AppendLineWithRetry(logFilePath, line);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Logger Error] Failed to write to log file: {ex.Message}");
            }
        }

        public void Debug(string message)
        {
            // Do not write to console; only to separate debug log
            try
            {
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
                AppendLineWithRetry(debugLogFilePath, line);
            }
            catch
            {
                // Silently ignore to avoid polluting terminal
            }
        }

        private static void AppendLineWithRetry(string path, string line, int maxAttempts = 5)
        {
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    lock (Sync)
                    {
                        using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                        using (var sw = new StreamWriter(fs))
                        {
                            sw.WriteLine(line);
                        }
                    }
                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(50 * attempt);
                }
            }
        }

        private static void WriteAllTextWithRetry(string path, string content, int maxAttempts = 5)
        {
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    lock (Sync)
                    {
                        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
                        using (var sw = new StreamWriter(fs))
                        {
                            sw.Write(content);
                        }
                    }
                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(50 * attempt);
                }
            }
        }

        private static void DeleteWithRetry(string path, int maxAttempts = 5)
        {
            if (!File.Exists(path)) return;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    lock (Sync)
                    {
                        File.Delete(path);
                    }
                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(50 * attempt);
                }
                catch
                {
                    return;
                }
            }
        }
    }
}
