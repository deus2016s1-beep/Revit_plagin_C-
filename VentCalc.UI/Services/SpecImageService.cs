using System;
using System.IO;

namespace VentCalc.UI.Services
{
    public static class SpecImageService
    {
        public static string ImageDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VentCalc", "spec_images");

        public static string GetFallbackImagePath(string group, string name)
        {
            Directory.CreateDirectory(ImageDirectory);
            string key = NormalizeKey($"{group}_{name}");
            string path = Path.Combine(ImageDirectory, key + ".svg");
            if (!File.Exists(path)) File.WriteAllText(path, BuildSvg(group, name));
            return path;
        }

        private static string NormalizeKey(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
            return string.IsNullOrWhiteSpace(value) ? "unknown" : value.Replace(' ', '_');
        }

        private static string BuildSvg(string group, string name)
        {
            string label = string.IsNullOrWhiteSpace(name) ? group : name;
            if (label.Length > 18) label = label.Substring(0, 18);
            return $"<svg xmlns='http://www.w3.org/2000/svg' width='128' height='96'><rect x='1' y='1' width='126' height='94' fill='#f5f5f5' stroke='#777'/><text x='64' y='50' text-anchor='middle' font-size='12' font-family='Arial'>{Escape(label)}</text></svg>";
        }

        private static string Escape(string value) => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
