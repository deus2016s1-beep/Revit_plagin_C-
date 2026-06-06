using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.IO;
using System.Text;

namespace VentCalc.UI.Services
{
    public static class VentCalcActionLogService
    {
        public static string GetLogPath(DateTime? date = null)
        {
            DateTime actualDate = date ?? DateTime.Now;
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VentCalc",
                "logs",
                $"ventcalc_actions_{actualDate:yyyyMMdd}.txt");
        }

        public static IReadOnlyList<string> ReadLastEntries(int maxCount)
        {
            try
            {
                string path = GetLogPath();
                return File.Exists(path)
                    ? File.ReadLines(path).TakeLast(maxCount).ToList()
                    : Array.Empty<string>();
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
        }

        public static void Append(string message)
        {
            try
            {
                string path = GetLogPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
                File.AppendAllText(path, $"{DateTime.Now.ToString("O", CultureInfo.InvariantCulture)} | {message}{Environment.NewLine}", Encoding.UTF8);
            }
            catch (Exception)
            {
                // Action logging is diagnostic-only and must never break VentCalc/Revit interaction.
            }
        }
    }
}
