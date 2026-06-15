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
            string path = Path.Combine(ImageDirectory, key + ".png");
            if (!File.Exists(path)) File.WriteAllBytes(path, BuildPng());
            return path;
        }

        private static string NormalizeKey(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
            return string.IsNullOrWhiteSpace(value) ? "unknown" : value.Replace(' ', '_');
        }

        private static byte[] BuildPng()
        {
            const string png = "iVBORw0KGgoAAAANSUhEUgAAAGAAAABgCAIAAADYG0K1AAAACXBIWXMAAAsTAAALEwEAmpwYAAABGUlEQVR4nO3aMQ6CQBAF0Yz//2k2NhY2YhNwQpK8lq58mMN8ZgAAAAAAAAAAAAAAAAAA4Lx7r9sD8G0gJgImAiYCIgImAiYCIgImAiYCIgImAiYCIgImAiYCIgImAiYCIgImAiYCIgImAiYCIgImAiYCIgImAiYCIgImAiYCIgImAiYCIgImAiYCIgImAiYCIgImAiYCIgImAiYCJgImAiYCJgImAiYCJgImAiYCJgImAiYCJgImAiYCJgImAiYCJgImAiYCJgImAiYCJgImAiYCJgImAiYCJgImAiYCJgImAiYCJgImAiYCJgImAiYCJgImAiYCJgImAiYCJgImAiYCJgImAiYCJgImAiYCJgImAiYCJgImAiYCJgImAiYCJgImAiYCJgImAiYCJgImAiYCJgImAiYCPwGq3cEbfI8pl0AAAAASUVORK5CYII=";
            return Convert.FromBase64String(png);
        }

    }
}
