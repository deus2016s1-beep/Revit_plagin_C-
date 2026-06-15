using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VentCalc.UI.Services
{
    public sealed class SpecItemRow : INotifyPropertyChanged
    {
        private string status = "OK";
        private string group = "Неопознано";
        private string name = "Неопознанный элемент";
        private string typeMark = "—";
        private string size = "—";
        private string unit = "шт";
        private double quantity = 1;
        private string system = "—";
        private string level = "—";
        private string source = "Unknown";
        private string note = string.Empty;
        private bool suppressManualTracking;

        public event PropertyChangedEventHandler? PropertyChanged;

        public long ElementId { get; set; }
        public string UniqueId { get; set; } = string.Empty;
        public string InternalName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string FamilyName { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;
        public bool IsManualEdited { get; private set; }
        public string Problem => Note;

        public string Status { get => status; set => SetProperty(ref status, value); }
        public string Group { get => group; set => SetEditableProperty(ref group, value); }
        public string Name { get => name; set => SetEditableProperty(ref name, value); }
        public string TypeMark { get => typeMark; set => SetEditableProperty(ref typeMark, value); }
        public string Size { get => size; set => SetProperty(ref size, value); }
        public string Unit { get => unit; set => SetEditableProperty(ref unit, value); }
        public double Quantity { get => quantity; set => SetProperty(ref quantity, value); }
        public string System { get => system; set => SetProperty(ref system, value); }
        public string Level { get => level; set => SetProperty(ref level, value); }
        public string Source { get => source; set => SetProperty(ref source, value); }
        public string Note { get => note; set { if (SetEditableProperty(ref note, value)) OnPropertyChanged(nameof(Problem)); } }

        public SpecRuleKey RuleKey => new SpecRuleKey(Category, FamilyName, TypeName);

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
            if (EqualityComparer<T>.Default.Equals(storage, value))
            {
                return false;
            }

            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public readonly record struct SpecRuleKey(string Category, string FamilyName, string TypeName)
    {
        public string ToStorageKey() => $"{Category}|{FamilyName}|{TypeName}";
    }

    public sealed class SpecRule
    {
        public string Category { get; set; } = string.Empty;
        public string FamilyName { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;
        public string Group { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string TypeMark { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
    }
}
