using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VentCalc.UI.Services
{
    public class SpecItemRow : INotifyPropertyChanged
    {
        private string status = "OK";
        private string section = "Вентиляция";
        private string group = "Неопознано";
        private string name = "Неопознанный элемент";
        private string typeMark = "—";
        private string size = "—";
        private string unit = "шт";
        private double quantity = 1;
        private double lengthM;
        private double areaM2;
        private string system = "—";
        private string level = "—";
        private string material = string.Empty;
        private string source = "Unknown";
        private string note = string.Empty;
        private string recommendation = string.Empty;
        private bool suppressManualTracking;

        public event PropertyChangedEventHandler? PropertyChanged;

        public long ElementId { get; set; }
        public string UniqueId { get; set; } = string.Empty;
        public long TypeId { get; set; }
        public string InternalName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string FamilyName { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;
        public bool IsManualEdited { get; private set; }
        public bool HasAdskName { get; set; }
        public bool MissingSize { get; set; }
        public bool MissingSystem { get; set; }
        public bool IsUnrecognized { get; set; }
        public string AdskName { get; set; } = string.Empty;
        public string AdskMark { get; set; } = string.Empty;
        public string AdskSize { get; set; } = string.Empty;
        public string ImagePath { get; set; } = string.Empty;
        public string Problem => Note;

        public string Status { get => status; set => SetProperty(ref status, value); }
        public string Section { get => section; set => SetEditableProperty(ref section, value); }
        public string Group { get => group; set => SetEditableProperty(ref group, value); }
        public string Name { get => name; set => SetEditableProperty(ref name, value); }
        public string TypeMark { get => typeMark; set => SetEditableProperty(ref typeMark, value); }
        public string Size { get => size; set => SetProperty(ref size, value); }
        public string Unit { get => unit; set => SetEditableProperty(ref unit, value); }
        public double Quantity { get => quantity; set => SetProperty(ref quantity, value); }
        public double LengthM { get => lengthM; set => SetProperty(ref lengthM, value); }
        public double AreaM2 { get => areaM2; set => SetProperty(ref areaM2, value); }
        public string System { get => system; set => SetProperty(ref system, value); }
        public string Level { get => level; set => SetProperty(ref level, value); }
        public string Material { get => material; set => SetProperty(ref material, value); }
        public string Source { get => source; set => SetProperty(ref source, value); }
        public string Note { get => note; set { if (SetEditableProperty(ref note, value)) OnPropertyChanged(nameof(Problem)); } }
        public string Recommendation { get => recommendation; set => SetProperty(ref recommendation, value); }

        public SpecRuleKey GetRuleKey(string scope) => SpecRuleKey.Create(scope, Category, FamilyName, TypeName, UniqueId, ElementId, Group);

        public void ApplySystemValues(Action<SpecItemRow> apply)
        {
            suppressManualTracking = true;
            try
            {
                apply(this);
                IsManualEdited = false;
            }
            finally
            {
                suppressManualTracking = false;
            }
        }

        private bool SetEditableProperty(ref string storage, string value, [CallerMemberName] string propertyName = "")
        {
            bool changed = SetProperty(ref storage, value, propertyName);
            if (changed && !suppressManualTracking)
            {
                IsManualEdited = true;
                OnPropertyChanged(nameof(IsManualEdited));
            }

            return changed;
        }

        private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = "")
        {
            if (EqualityComparer<T>.Default.Equals(storage, value)) return false;
            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = "") => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public sealed class RawSpecItem : SpecItemRow
    {
    }

    public sealed class SpecGroupRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public List<long> ElementIds { get; } = new List<long>();
        public List<string> UniqueIds { get; } = new List<string>();
        public List<SpecItemRow> SourceItems { get; } = new List<SpecItemRow>();
        public bool IsSelected { get; set; }
        public string Section { get; set; } = "Вентиляция";
        public string Group { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string TypeMark { get; set; } = string.Empty;
        public string Size { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public double Quantity { get; set; }
        public double LengthM { get; set; }
        public double AreaM2 { get; set; }
        public string System { get; set; } = string.Empty;
        public string Level { get; set; } = string.Empty;
        public string Material { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
        public string Status { get; set; } = "OK";
        public string Source { get; set; } = "Auto";
        public bool IsManual { get; set; }
        public string ImagePath { get; set; } = string.Empty;
        public string Problem => string.Join("; ", SourceItems.ConvertAll(item => item.Problem).FindAll(text => !string.IsNullOrWhiteSpace(text)));
        public string Recommendation => string.Join("; ", SourceItems.ConvertAll(item => item.Recommendation).FindAll(text => !string.IsNullOrWhiteSpace(text)));
        public long FirstElementId => ElementIds.Count > 0 ? ElementIds[0] : 0;
        public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    public sealed class SpecColumnLayout : INotifyPropertyChanged
    {
        private bool visibleInMain = true;
        private bool visibleInExcel = true;
        private bool groupBy;
        private bool sum;
        private int order;
        public event PropertyChangedEventHandler? PropertyChanged;
        public string FieldName { get; set; } = string.Empty;
        public string Header { get; set; } = string.Empty;
        public string Format { get; set; } = string.Empty;
        public bool IsNumeric { get; set; }
        public bool VisibleInMain { get => visibleInMain; set { visibleInMain = value; OnChanged(); } }
        public bool VisibleInExcel { get => visibleInExcel; set { visibleInExcel = value; OnChanged(); } }
        public bool GroupBy { get => groupBy; set { groupBy = value; OnChanged(); } }
        public bool Sum { get => sum; set { sum = value; OnChanged(); } }
        public int Order { get => order; set { order = value; OnChanged(); } }
        private void OnChanged([CallerMemberName] string propertyName = "") => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public sealed class SpecCalcSettings
    {
        public int SchemaVersion { get; set; } = 1;
        public string SelectedExcelProfile { get; set; } = "Проектная спецификация";
        public List<SpecColumnLayout> Columns { get; set; } = new List<SpecColumnLayout>();
    }

    public readonly record struct SpecRuleKey(string Scope, string Category, string FamilyName, string TypeName, string UniqueId, long ElementId, string GroupName)
    {
        public static SpecRuleKey Create(string scope, string category, string familyName, string typeName, string uniqueId, long elementId, string groupName = "")
        {
            string normalizedScope = string.IsNullOrWhiteSpace(scope) ? "Type" : scope;
            return normalizedScope switch
            {
                "Element" => new SpecRuleKey("Element", category, familyName, typeName, uniqueId, elementId, string.Empty),
                "Family" => new SpecRuleKey("Family", category, familyName, string.Empty, string.Empty, 0, string.Empty),
                "Group" => new SpecRuleKey("Group", string.Empty, string.Empty, string.Empty, string.Empty, 0, groupName),
                _ => new SpecRuleKey("Type", category, familyName, typeName, string.Empty, 0, string.Empty)
            };
        }

        public string ToStorageKey() => Scope == "Element"
            ? $"Element|{UniqueId}|{ElementId}"
            : Scope == "Family"
                ? $"Family|{Category}|{FamilyName}"
                : Scope == "Group"
                    ? $"Group|{GroupName}"
                    : $"Type|{Category}|{FamilyName}|{TypeName}";
    }

    public sealed class SpecRule
    {
        public int SchemaVersion { get; set; } = 2;
        public string Scope { get; set; } = "Type";
        public string Category { get; set; } = string.Empty;
        public string FamilyName { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;
        public string UniqueId { get; set; } = string.Empty;
        public long ElementId { get; set; }
        public string GroupName { get; set; } = string.Empty;
        public string Section { get; set; } = string.Empty;
        public string Group { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string TypeMark { get; set; } = string.Empty;
        public string Size { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
    }
}
