using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace VentCalc.UI.Services
{
    public static class SpecTextRuleService
    {
        public static void ApplyRules(IEnumerable<SpecGroupRow> rows, IEnumerable<SpecTextRule> rules)
        {
            foreach (SpecTextRule rule in rules.Where(rule => rule.Enabled).OrderBy(rule => rule.Order))
            {
                foreach (SpecGroupRow row in rows.Where(row => MatchesGroup(row, rule.Group)))
                {
                    SetField(row, rule.Field, ApplyAction(GetField(row, rule.Field), row.Size, rule));
                }
            }
        }

        public static IReadOnlyList<SpecTextRulePreviewRow> Preview(IEnumerable<SpecGroupRow> rows, SpecTextRule rule, int take = 20)
        {
            return rows
                .Where(row => MatchesGroup(row, rule.Group))
                .Take(take)
                .Select(row =>
                {
                    string before = GetField(row, rule.Field);
                    return new SpecTextRulePreviewRow { Before = before, After = ApplyAction(before, row.Size, rule) };
                })
                .ToList();
        }

        private static bool MatchesGroup(SpecGroupRow row, string group)
        {
            return string.IsNullOrWhiteSpace(group) || group == "Все" || string.Equals(row.Group, group, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetField(SpecGroupRow row, string field)
        {
            return field switch
            {
                "Размер" => row.Size,
                "Тип / марка" => row.TypeMark,
                "Примечание" => row.Note,
                _ => row.Name
            };
        }

        private static void SetField(SpecGroupRow row, string field, string value)
        {
            switch (field)
            {
                case "Размер": row.Size = value; break;
                case "Тип / марка": row.TypeMark = value; break;
                case "Примечание": row.Note = value; break;
                default: row.Name = value; break;
            }
            row.Refresh();
        }

        private static string ApplyAction(string value, string size, SpecTextRule rule)
        {
            string result = value ?? string.Empty;
            switch (rule.Action)
            {
                case "Заменить текст":
                    if (!string.IsNullOrEmpty(rule.FindText)) result = result.Replace(rule.FindText, rule.ReplaceText ?? string.Empty);
                    break;
                case "Удалить текст":
                    if (!string.IsNullOrEmpty(rule.FindText)) result = result.Replace(rule.FindText, string.Empty);
                    break;
                case "Добавить в начало":
                    result = (rule.AddText ?? string.Empty) + result;
                    break;
                case "Добавить в конец":
                    result += rule.AddText ?? string.Empty;
                    break;
                case "Убрать размер из наименования":
                    result = RemoveSizeFromName(result, size);
                    break;
                case "Нормализовать Ø":
                    result = NormalizeDiameter(result);
                    break;
                case "Убрать повтор размера":
                    result = RemoveRepeatedSize(result);
                    break;
            }
            return CleanText(result);
        }

        private static string RemoveSizeFromName(string value, string size)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(size) || size == "—") return value;
            string result = value;
            foreach (string token in BuildSizeVariants(size))
            {
                result = Regex.Replace(result, @"\s*[,;/-]?\s*" + Regex.Escape(token) + @"\s*$", string.Empty, RegexOptions.IgnoreCase);
            }
            return result;
        }

        private static IEnumerable<string> BuildSizeVariants(string size)
        {
            string normalized = NormalizeSeparators(size);
            yield return normalized;
            yield return normalized.Replace("Ø", "ø");
            yield return normalized.Replace("Ø", "ф");
            yield return normalized.Replace('×', 'x');
            yield return normalized.Replace('×', 'х');
        }

        private static string NormalizeDiameter(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            string result = Regex.Replace(value, @"(?i)(?:ф|ø|d|диам\.?)\s*(\d{2,4})", "Ø$1");
            return result.Replace("Ф", "Ø").Replace("ø", "Ø");
        }

        private static string RemoveRepeatedSize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            string normalized = NormalizeSeparators(NormalizeDiameter(value));
            string[] parts = Regex.Split(normalized, @"\s*(?:-|/)\s*").Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part.Trim()).ToArray();
            if (parts.Length <= 1) return normalized;
            string[] distinct = parts.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return distinct.Length == 1 ? distinct[0] : string.Join(" / ", distinct);
        }

        private static string NormalizeSeparators(string value)
        {
            return value.Trim().Replace('х', '×').Replace('Х', '×').Replace('x', '×').Replace('X', '×').Replace('*', '×');
        }

        private static string CleanText(string value)
        {
            string result = Regex.Replace(value ?? string.Empty, @"\s+", " ");
            result = Regex.Replace(result, @"\s+,", ",");
            result = Regex.Replace(result, @"[,;\s-]+$", string.Empty);
            return result.Trim();
        }
    }
}
