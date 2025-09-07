using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using Windows.Media.Ocr;
using Windows.Graphics.Imaging;
using System.Threading.Tasks;
using System.Runtime.InteropServices.WindowsRuntime;

namespace GameStoreLibraryManager.Common
{
    public enum OcrButtonColor { Yellow, Blue }

    public static class OcrHelper
    {
        public static bool TryOcrClickInWindow(IntPtr hwnd, IEnumerable<string> keywords, Action<int,int> click, OcrButtonColor buttonColor = OcrButtonColor.Yellow, Action<string> log = null, string dumpDir = null)
        {
            if (hwnd == IntPtr.Zero || keywords == null) return false;
            var kw = keywords
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim().ToLowerInvariant())
                .Distinct()
                .ToArray();
            if (kw.Length == 0) return false;

            if (!TryCaptureWindow(hwnd, out var bmp))
            {
                log?.Invoke("Capture failed (PrintWindow). Trying CopyFromScreen fallback...");
                if (!TryCopyFromScreen(hwnd, out bmp))
                {
                    log?.Invoke("CopyFromScreen fallback failed.");
                    return false;
                }
            }

            try
            {
                var (left, top, right, bottom) = GetWindowRectPixels(hwnd);
                log?.Invoke($"Window rect: L={left},T={top},R={right},B={bottom}; Capture size: {bmp.Width}x{bmp.Height}");

                if (IsBitmapMostlyBlack(bmp))
                {
                    log?.Invoke("PrintWindow produced a black frame; using CopyFromScreen fallback...");
                    bmp.Dispose();
                    if (!TryCopyFromScreen(hwnd, out bmp)) { log?.Invoke("CopyFromScreen fallback failed."); return false; }
                    log?.Invoke($"Fallback capture size: {bmp.Width}x{bmp.Height}");
                }

                var coloredRegions = FindAllColoredRegions(bmp, buttonColor, log);
                if (coloredRegions.Any())
                {
                    log?.Invoke($"Found {coloredRegions.Count} potential {buttonColor} button regions. Performing OCR on each.");
                    foreach (var r in coloredRegions)
                    {
                        using (var regionBmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb))
                        {
                            using (var g = Graphics.FromImage(regionBmp))
                            {
                                g.DrawImage(bmp, new Rectangle(0, 0, r.Width, r.Height), r, GraphicsUnit.Pixel);
                            }

                            if (!string.IsNullOrEmpty(dumpDir))
                            {
                                try
                                {
                                    string path = Path.Combine(dumpDir, $"ocr_region_{r.X}_{r.Y}_{DateTime.Now:HHmmss_fff}.png");
                                    regionBmp.Save(path, ImageFormat.Png);
                                    log?.Invoke($"Saved OCR region to {path}");
                                }
                                catch (Exception ex)
                                {
                                    log?.Invoke($"Failed to save OCR debug image: {ex.Message}");
                                }
                            }

                            log?.Invoke($"Starting Windows OCR pass on region at ({r.X},{r.Y})...");
                            var winOcrRect = TryOcrWithWindowsOcr(regionBmp, kw, log).GetAwaiter().GetResult();
                            if (winOcrRect.HasValue)
                            {
                                var wr = winOcrRect.Value;
                                int cx = left + r.Left + wr.Left + wr.Width / 2;
                                int cy = top + r.Top + wr.Top + wr.Height / 2;
                                log?.Invoke($"Windows OCR matched at local [{wr.Left},{wr.Top},{wr.Right},{wr.Bottom}] -> click at ({cx},{cy}).");
                                click(cx, cy);
                                return true;
                            }
                            log?.Invoke("Windows OCR pass did not find a match in this region.");
                        }
                    }
                }
                else
                {
                    log?.Invoke($"No {buttonColor} button regions found.");
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"[OCR] Exception in TryOcrClickInWindow: {ex}");
            }
            finally
            {
                bmp?.Dispose();
            }

            log?.Invoke("No keyword matched in OCR scan.");
            return false;
        }

        private static bool IsKeywordFuzzyMatch(string text, IEnumerable<string> keywords)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var parts = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                foreach (var k in keywords)
                {
                    if (string.IsNullOrWhiteSpace(k)) continue;
                    if (part.Contains(k)) return true;
                    int d = LevenshteinDistance(part, k);
                    int tol = k.Length >= 8 ? 2 : 1;
                    if (d <= tol) return true;
                }
            }
            return false;
        }

        private static int LevenshteinDistance(string a, string b)
        {
            if (a == null) return b?.Length ?? 0;
            if (b == null) return a.Length;
            int n = a.Length, m = b.Length;
            var dp = new int[n + 1, m + 1];
            for (int i = 0; i <= n; i++) dp[i, 0] = i;
            for (int j = 0; j <= m; j++) dp[0, j] = j;
            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    dp[i, j] = Math.Min(Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1), dp[i - 1, j - 1] + cost);
                }
            }
            return dp[n, m];
        }

        private static string Normalize(string s)
        {
            return new string((s ?? string.Empty).Trim().ToLowerInvariant().Where(ch => char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch)).ToArray());
        }

        private static (int left, int top, int right, int bottom) GetWindowRectPixels(IntPtr hwnd)
        {
            GetWindowRect(hwnd, out RECT r);
            return (r.Left, r.Top, r.Right, r.Bottom);
        }

        private static double ComputeColorScore(Bitmap bmp, Rectangle rect, OcrButtonColor targetColor)
        {
            try
            {
                rect.Intersect(new Rectangle(0, 0, bmp.Width, bmp.Height));
                if (rect.Width <= 1 || rect.Height <= 1) return 0;
                int samples = 0; int colorMatch = 0;
                for (int y = 0; y < 8; y++)
                {
                    for (int x = 0; x < 12; x++)
                    {
                        int sx = rect.Left + x * (rect.Width - 1) / 11;
                        int sy = rect.Top + y * (rect.Height - 1) / 7;
                        var c = bmp.GetPixel(sx, sy);
                        RgbToHsv(c, out double h, out double s, out double v);

                        bool isMatch = false;
                        switch (targetColor)
                        {
                            case OcrButtonColor.Yellow:
                                if (h >= 35 && h <= 70 && s >= 0.28 && v >= 0.50) isMatch = true;
                                break;
                            case OcrButtonColor.Blue:
                                if (h >= 200 && h <= 260 && s >= 0.50 && v >= 0.50) isMatch = true;
                                break;
                        }

                        if (isMatch) colorMatch++;
                        samples++;
                    }
                }
                return samples == 0 ? 0 : (double)colorMatch / samples;
            }
            catch { return 0; }
        }

        private static List<Rectangle> FindAllColoredRegions(Bitmap bmp, OcrButtonColor targetColor, Action<string> log, Rectangle? searchAreaBmp = null)
        {
            var candidates = new List<(Rectangle region, double score)>();
            try
            {
                int W = bmp.Width, H = bmp.Height;
                Rectangle area = searchAreaBmp ?? new Rectangle(0, 0, W, H);
                area.Intersect(new Rectangle(0, 0, W, H));
                if (area.Width <= 0 || area.Height <= 0) return new List<Rectangle>();

                int aL = area.Left;
                int aT = area.Top + (int)(area.Height * 0.40);
                int aR = area.Right;
                int aB = area.Top + (int)(area.Height * 0.95);
                aB = Math.Min(aB, area.Bottom);

                int minW = Math.Max(80, (int)(W * 0.10));
                int maxW = Math.Min(W, (int)(W * 0.30));
                int minH = Math.Max(28, (int)(H * 0.045));
                int maxH = Math.Min(H, (int)(H * 0.12));

                int stepX = Math.Max(8, W / 80);
                int stepY = Math.Max(8, H / 80);

                for (int h = minH; h <= maxH; h += Math.Max(8, minH / 2))
                {
                    for (int w = minW; w <= maxW; w += Math.Max(12, minW / 2))
                    {
                        for (int y = aT; y <= aB - h; y += stepY)
                        {
                            int xStart = aL + (int)((aR - aL) * 0.10);
                            int xEnd = aL + (int)((aR - aL) * 0.90);
                            for (int x = xStart; x <= xEnd - w; x += stepX)
                            {
                                var rect = new Rectangle(x, y, w, h);
                                double score = ComputeColorScore(bmp, rect, targetColor);
                                if (score >= 0.35)
                                {
                                    candidates.Add((rect, score));
                                }
                            }
                        }
                    }
                }

                // De-duplicate overlapping regions, keeping the one with the highest score
                var finalRegions = new List<Rectangle>();
                foreach (var candidate in candidates.OrderByDescending(c => c.score))
                {
                    var candidateRect = candidate.region;
                    bool isOverlapping = false;
                    foreach (var existingRect in finalRegions)
                    {
                        var intersection = Rectangle.Intersect(candidateRect, existingRect);
                        if (intersection.Width * intersection.Height > 0.5 * (candidateRect.Width * candidateRect.Height))
                        {
                            isOverlapping = true;
                            break;
                        }
                    }
                    if (!isOverlapping)
                    {
                        finalRegions.Add(candidateRect);
                    }
                }
                log?.Invoke($"Found {finalRegions.Count} distinct {targetColor} regions.");
                return finalRegions;
            }
            catch { return new List<Rectangle>(); }
        }

        private static void RgbToHsv(Color c, out double h, out double s, out double v)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            v = max;
            double d = max - min;
            s = max == 0 ? 0 : d / max;
            if (d == 0) { h = 0; return; }
            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * (((b - r) / d) + 2);
            else h = 60 * (((r - g) / d) + 4);
            if (h < 0) h += 360;
        }

        private static bool TryCaptureWindow(IntPtr hwnd, out Bitmap bmp)
        {
            bmp = null;
            try
            {
                var r = GetDwmOrWindowRect(hwnd);
                int width = Math.Max(1, r.Right - r.Left);
                int height = Math.Max(1, r.Bottom - r.Top);
                bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                using var g = Graphics.FromImage(bmp);
                IntPtr hdc = g.GetHdc();
                const uint PW_RENDERFULLCONTENT = 0x00000002;
                bool ok = PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT);
                g.ReleaseHdc(hdc);
                if (!ok) { bmp.Dispose(); bmp = null; return false; }
                return true;
            }
            catch { try { bmp?.Dispose(); } catch { } bmp = null; return false; }
        }

        private static bool TryCopyFromScreen(IntPtr hwnd, out Bitmap bmp)
        {
            bmp = null;
            try
            {
                var r = GetDwmOrWindowRect(hwnd);
                int width = Math.Max(1, r.Right - r.Left);
                int height = Math.Max(1, r.Bottom - r.Top);
                bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                using var g = Graphics.FromImage(bmp);
                g.CopyFromScreen(r.Left, r.Top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
                return true;
            }
            catch { try { bmp?.Dispose(); } catch { } bmp = null; return false; }
        }

        private static RECT GetDwmOrWindowRect(IntPtr hwnd)
        {
            try
            {
                RECT r;
                int cb = Marshal.SizeOf<RECT>();
                int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
                if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out r, cb) == 0) return r;
            }
            catch { }
            GetWindowRect(hwnd, out RECT wr);
            return wr;
        }

        private static bool IsBitmapMostlyBlack(Bitmap bmp)
        {
            try
            {
                int w = bmp.Width, h = bmp.Height;
                if (w < 4 || h < 4) return false;
                int samples = 0, blackish = 0;
                for (int y = 0; y < 10; y++)
                {
                    for (int x = 0; x < 10; x++)
                    {
                        int sx = x * (w - 1) / 9;
                        int sy = y * (h - 1) / 9;
                        var c = bmp.GetPixel(sx, sy);
                        if (c.R < 8 && c.G < 8 && c.B < 8) blackish++;
                        samples++;
                    }
                }
                return blackish > samples * 0.9;
            }
            catch { return false; }
        }

        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

        private static async Task<Rectangle?> TryOcrWithWindowsOcr(Bitmap bmp, IEnumerable<string> keywords, Action<string> log)
        {
            const int upscaleFactor = 3;
            try
            {
                using var upscaledBmp = new Bitmap(bmp.Width * upscaleFactor, bmp.Height * upscaleFactor);
                using (var g = Graphics.FromImage(upscaledBmp))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(bmp, new Rectangle(0, 0, upscaledBmp.Width, upscaledBmp.Height));
                }
                log?.Invoke($"[WinOCR] Upscaled button region from {bmp.Width}x{bmp.Height} to {upscaledBmp.Width}x{upscaledBmp.Height}.");

                var softwareBitmap = await ConvertToSoftwareBitmap(upscaledBmp);
                if (softwareBitmap == null)
                {
                    log?.Invoke("[WinOCR] SoftwareBitmap conversion failed.");
                    return null;
                }

                var ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages();
                if (ocrEngine == null)
                {
                    log?.Invoke("[WinOCR] Could not create OCR engine from user profile languages. Trying default.");
                    try
                    {
                        var lang = new Windows.Globalization.Language("en-US");
                        if (OcrEngine.IsLanguageSupported(lang)) ocrEngine = OcrEngine.TryCreateFromLanguage(lang);
                    }
                    catch (Exception ex) { log?.Invoke($"[WinOCR] Failed to create fallback engine: {ex.Message}"); }
                }

                if (ocrEngine == null) { log?.Invoke("[WinOCR] OcrEngine is null, cannot proceed."); return null; }

                log?.Invoke($"[WinOCR] Engine created. Language: {ocrEngine.RecognizerLanguage.LanguageTag}");
                var result = await ocrEngine.RecognizeAsync(softwareBitmap);
                log?.Invoke($"[WinOCR] Recognition completed. Lines: {result.Lines.Count}");

                foreach (var line in result.Lines)
                {
                    foreach (var word in line.Words)
                    {
                        var norm = Normalize(word.Text);
                        if (string.IsNullOrWhiteSpace(norm)) continue;
                        var match = keywords.FirstOrDefault(k => norm.Contains(k) || IsKeywordFuzzyMatch(norm, new[] { k }));
                        if (!string.IsNullOrEmpty(match))
                        {
                            log?.Invoke($"[WinOCR] Matched '{word.Text}' -> '{norm}' at upscaled [{word.BoundingRect.X},{word.BoundingRect.Y},{word.BoundingRect.Width},{word.BoundingRect.Height}]");
                            return new Rectangle((int)(word.BoundingRect.X / upscaleFactor), (int)(word.BoundingRect.Y / upscaleFactor), (int)(word.BoundingRect.Width / upscaleFactor), (int)(word.BoundingRect.Height / upscaleFactor));
                        }
                    }
                }
            }
            catch (Exception ex) { log?.Invoke($"[WinOCR] Exception: {ex.ToString()}"); }
            return null;
        }

        private static async Task<SoftwareBitmap> ConvertToSoftwareBitmap(Bitmap bmp)
        {
            if (bmp == null) return null;
            try
            {
                using (var stream = new MemoryStream())
                {
                    bmp.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                    var decoder = await BitmapDecoder.CreateAsync(stream.AsRandomAccessStream());
                    return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                }
            }
            catch { return null; }
        }
    }
}
