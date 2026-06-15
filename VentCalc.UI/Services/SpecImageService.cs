using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace VentCalc.UI.Services
{
    public static class SpecImageService
    {
        public static string ImageDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VentCalc", "spec_images");

        public static string GetFallbackImagePath(string group, string name)
        {
            try
            {
                Directory.CreateDirectory(ImageDirectory);
                string key = NormalizeKey($"{group}_{name}");
                string path = Path.Combine(ImageDirectory, key + ".png");
                if (!File.Exists(path)) File.WriteAllBytes(path, BuildPng(group, name));
                return path;
            }
            catch (Exception)
            {
                try
                {
                    string fallbackPath = Path.Combine(Path.GetTempPath(), "VentCalc_spec_placeholder.png");
                    if (!File.Exists(fallbackPath)) File.WriteAllBytes(fallbackPath, BuildPng("Неопознано", "Нет изображения"));
                    return fallbackPath;
                }
                catch (Exception)
                {
                    return string.Empty;
                }
            }
        }

        private static string NormalizeKey(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
            return string.IsNullOrWhiteSpace(value) ? "unknown" : value.Replace(' ', '_');
        }

        private static byte[] BuildPng(string group, string name)
        {
            using var bitmap = new Bitmap(128, 128);
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.WhiteSmoke);

            using var borderPen = new Pen(Color.DimGray, 3);
            graphics.DrawRectangle(borderPen, 2, 2, 123, 123);

            using var accentBrush = new SolidBrush(Color.FromArgb(230, 240, 250));
            graphics.FillRectangle(accentBrush, 10, 10, 108, 54);

            using var textBrush = new SolidBrush(Color.FromArgb(40, 40, 40));
            using var titleFont = new Font("Arial", 18, FontStyle.Bold);
            using var smallFont = new Font("Arial", 9, FontStyle.Regular);
            using var centerFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

            graphics.DrawString(GetLabel(group, name), titleFont, textBrush, new RectangleF(8, 22, 112, 34), centerFormat);
            graphics.DrawString(TrimText(name, 18), smallFont, textBrush, new RectangleF(8, 76, 112, 34), centerFormat);

            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }

        private static string GetLabel(string group, string name)
        {
            string source = $"{group} {name}".ToUpperInvariant();
            if (source.Contains("ОТВОД")) return "ELBOW";
            if (source.Contains("ТРОЙНИК")) return "TEE";
            if (source.Contains("ЗОНТ")) return "HOOD";
            if (source.Contains("РЕШ")) return "GRILLE";
            if (source.Contains("ДИФ")) return "DIFF";
            if (source.Contains("ВЕНТ")) return "FAN";
            if (source.Contains("КЛАП")) return "VALVE";
            if (source.Contains("ГИБ")) return "FLEX";
            if (source.Contains("ВОЗДУХ")) return "DUCT";
            return "SPEC";
        }

        private static string TrimText(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return "SpecCalc";
            value = value.Trim();
            return value.Length <= maxLength ? value : value.Substring(0, maxLength - 1) + "…";
        }
    }
}
