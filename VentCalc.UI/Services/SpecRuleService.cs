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
                return rules.ToDictionary(rule => new SpecRuleKey(rule.Category, rule.FamilyName, rule.TypeName).ToStorageKey(), StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return new Dictionary<string, SpecRule>(StringComparer.OrdinalIgnoreCase);
            }
        }

        public void SaveRules(IEnumerable<SpecItemRow> rows)
        {
            Dictionary<string, SpecRule> rules = LoadRules();
            foreach (SpecItemRow row in rows.Where(row => !string.IsNullOrWhiteSpace(row.FamilyName) || !string.IsNullOrWhiteSpace(row.TypeName)))
            {
                rules[row.RuleKey.ToStorageKey()] = new SpecRule
                {
                    Category = row.Category,
                    FamilyName = row.FamilyName,
                    TypeName = row.TypeName,
                    Group = row.Group,
                    Name = row.Name,
                    TypeMark = row.TypeMark,
                    Unit = row.Unit,
                    Note = row.Note
                };
                row.ApplySystemValues(item => item.Source = "ManualRule");
            }

            Save(rules.Values.OrderBy(rule => rule.Category).ThenBy(rule => rule.FamilyName).ThenBy(rule => rule.TypeName));
        }

        public bool RemoveRule(SpecItemRow row)
        {
            Dictionary<string, SpecRule> rules = LoadRules();
            bool removed = rules.Remove(row.RuleKey.ToStorageKey());
            if (removed)
            {
                Save(rules.Values.OrderBy(rule => rule.Category).ThenBy(rule => rule.FamilyName).ThenBy(rule => rule.TypeName));
            }

            return removed;
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
