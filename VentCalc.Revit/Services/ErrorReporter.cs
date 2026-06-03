using System;
using System.Diagnostics;
using System.IO;
using Autodesk.Revit.UI;

namespace VentCalc.Revit.Services
{
    public static class ErrorReporter
    {
        public static string CreateLaunchLog()
        {
            try
            {
                string path = CreateLogPath("ventcalc_launch");
                File.WriteAllText(path, $"{DateTime.Now:O} | VentCalc launch log created{Environment.NewLine}");
                return path;
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception.ToString());
                return string.Empty;
            }
        }

        public static void WriteTrace(string? logPath, string message)
        {
            try
            {
                string line = $"{DateTime.Now:O} | {message}{Environment.NewLine}";
                Debug.WriteLine($"VentCalc: {message}");
                if (!string.IsNullOrWhiteSpace(logPath))
                {
                    File.AppendAllText(logPath, line);
                }
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception.ToString());
            }
        }

        public static string Report(UIApplication uiApplication, string description, Exception exception, string? launchLogPath = null)
        {
            string logPath = string.Empty;
            try
            {
                logPath = CreateLogPath("ventcalc_error");
                File.WriteAllText(logPath, BuildLogContent(uiApplication, description, exception, launchLogPath));
            }
            catch (Exception logException)
            {
                Debug.WriteLine(logException.ToString());
            }

            try
            {
                string logText = string.IsNullOrWhiteSpace(logPath) ? "Лог не удалось записать." : $"Лог: {logPath}";
                string launchLogText = string.IsNullOrWhiteSpace(launchLogPath) ? string.Empty : $"\nLaunch log: {launchLogPath}";
                TaskDialog.Show(
                    "VentCalc — ошибка",
                    $"{description}\n\n{exception.Message}\n\n{logText}{launchLogText}\n\n{exception}");
            }
            catch (Exception dialogException)
            {
                Debug.WriteLine(dialogException.ToString());
            }

            return logPath;
        }

        private static string BuildLogContent(UIApplication uiApplication, string description, Exception exception, string? launchLogPath)
        {
            return $"DateTime: {DateTime.Now:O}{Environment.NewLine}" +
                $"Command: {description}{Environment.NewLine}" +
                $"Revit Version: {SafeReadRevitVersion(uiApplication)}{Environment.NewLine}" +
                $"Launch Log: {launchLogPath ?? string.Empty}{Environment.NewLine}" +
                $"Exception:{Environment.NewLine}{exception}{Environment.NewLine}" +
                $"Inner Exception:{Environment.NewLine}{exception.InnerException}{Environment.NewLine}" +
                $"Stack Trace:{Environment.NewLine}{exception.StackTrace}{Environment.NewLine}";
        }

        private static string SafeReadRevitVersion(UIApplication uiApplication)
        {
            try
            {
                return uiApplication.Application.VersionNumber;
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception.ToString());
                return "unknown";
            }
        }

        private static string CreateLogPath(string prefix)
        {
            string logsDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VentCalc",
                "logs");
            Directory.CreateDirectory(logsDirectory);
            return Path.Combine(logsDirectory, $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.txt");
        }
    }
}
