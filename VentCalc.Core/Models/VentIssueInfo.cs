using System;

namespace VentCalc.Core.Models
{
    public sealed class VentIssueInfo
    {
        public string Severity { get; set; } = string.Empty;

        public string ElementId { get; set; } = string.Empty;

        public string Category { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public string Recommendation { get; set; } = string.Empty;

        public string DisplaySeverity => string.Equals(Severity, "Error", StringComparison.OrdinalIgnoreCase)
            ? "Ошибка"
            : string.Equals(Severity, "Warning", StringComparison.OrdinalIgnoreCase)
                ? "Предупреждение"
                : "Сведение";

        public string DisplayMessage => Message
            .Replace("OST_DuctCurves", "воздуховод", StringComparison.OrdinalIgnoreCase)
            .Replace("OST_DuctFitting", "фитинг", StringComparison.OrdinalIgnoreCase)
            .Replace("ConnectorKey", "коннектор", StringComparison.OrdinalIgnoreCase)
            .Replace("CandidateRole", "роль", StringComparison.OrdinalIgnoreCase)
            .Replace("DataStorage", "хранилище проекта", StringComparison.OrdinalIgnoreCase)
            .Replace("PathRole", "роль трассы", StringComparison.OrdinalIgnoreCase);

        public string DisplaySection
        {
            get
            {
                string text = $"{Category} {Message}";
                if (Contains(text, "скорост")) return "Скорости";
                if (Contains(text, "расход")) return "Расходы";
                if (Contains(text, "коротк") || Contains(text, "геометр") || Contains(text, "размер")) return "Геометрия";
                if (Contains(text, "коннектор") || Contains(text, "соедин")) return "Соединения";
                if (Contains(text, "ζ") || Contains(text, "zeta") || Contains(text, "местн")) return "Местные сопротивления";
                if (Contains(text, "оборуд")) return "Оборудование";
                if (Contains(text, "трасс") || Contains(text, "старт") || Contains(text, "конец")) return "Трассировка";
                return "Система";
            }
        }

        private static bool Contains(string value, string fragment)
        {
            return value.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
