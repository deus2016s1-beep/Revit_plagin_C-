using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace VentCalc.UI.Services
{
    public sealed class SpecRuleService
    {
        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions { WriteIndented = true };

        public string RulesPath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VentCalc",
            "spec_rules.json");

        public Dictionary<string, SpecRule> LoadRules()
        {
            try
            {
                if (!File.Exists(RulesPath))
                {
                    return new Dictionary<string, SpecRule>(StringComparer.OrdinalIgnoreCase);
                }

                string json = File.ReadAllText(RulesPath);
                List<SpecRule> rules = JsonSerializer.Deserialize<List<SpecRule>>(json) ?? new List<SpecRule>();
                var result = new Dictionary<string, SpecRule>(StringComparer.OrdinalIgnoreCase);
                foreach (SpecRule rule in rules)
                {
                    result[GetStorageKey(rule)] = rule;
                }

                return result;
            }
            catch (Exception)
            {
                return new Dictionary<string, SpecRule>(StringComparer.OrdinalIgnoreCase);
            }
        }

        public void SaveRules(IEnumerable<SpecItemRow> rows, string scope)
        {
            Dictionary<string, SpecRule> rules = LoadRules();
            foreach (SpecItemRow row in rows.Where(row => !string.IsNullOrWhiteSpace(row.FamilyName) || !string.IsNullOrWhiteSpace(row.TypeName) || !string.IsNullOrWhiteSpace(row.UniqueId)))
            {
                SpecRuleKey key = row.GetRuleKey(scope);
                rules[key.ToStorageKey()] = new SpecRule
                {
                    SchemaVersion = 2,
                    Scope = key.Scope,
                    Category = row.Category,
                    FamilyName = row.FamilyName,
                    TypeName = key.Scope == "Family" ? string.Empty : row.TypeName,
                    UniqueId = key.Scope == "Element" ? row.UniqueId : string.Empty,
                    ElementId = key.Scope == "Element" ? row.ElementId : 0,
                    Group = row.Group,
                    Name = row.Name,
                    TypeMark = row.TypeMark,
                    Unit = row.Unit,
                    Note = row.Note
                };
                row.ApplySystemValues(item => item.Source = "ManualRule");
            }

            Save(rules.Values.OrderBy(rule => rule.Scope).ThenBy(rule => rule.Category).ThenBy(rule => rule.FamilyName).ThenBy(rule => rule.TypeName));
        }

        public bool RemoveRule(SpecItemRow row, string scope)
        {
            Dictionary<string, SpecRule> rules = LoadRules();
            bool removed = rules.Remove(row.GetRuleKey(scope).ToStorageKey());
            if (removed)
            {
                Save(rules.Values.OrderBy(rule => rule.Scope).ThenBy(rule => rule.Category).ThenBy(rule => rule.FamilyName).ThenBy(rule => rule.TypeName));
            }

            return removed;
        }

        public static bool TryGetRule(SpecItemRow row, IReadOnlyDictionary<string, SpecRule> rules, out SpecRule? rule)
        {
            return rules.TryGetValue(row.GetRuleKey("Element").ToStorageKey(), out rule)
                || rules.TryGetValue(row.GetRuleKey("Type").ToStorageKey(), out rule)
                || rules.TryGetValue(row.GetRuleKey("Family").ToStorageKey(), out rule);
        }

        private static string GetStorageKey(SpecRule rule)
        {
            string scope = string.IsNullOrWhiteSpace(rule.Scope) ? "Type" : rule.Scope;
            return SpecRuleKey.Create(scope, rule.Category, rule.FamilyName, rule.TypeName, rule.UniqueId, rule.ElementId).ToStorageKey();
        }

        private void Save(IEnumerable<SpecRule> rules)
        {
            string? directory = Path.GetDirectoryName(RulesPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(RulesPath, JsonSerializer.Serialize(rules.ToList(), SerializerOptions));
        }
    }
}
