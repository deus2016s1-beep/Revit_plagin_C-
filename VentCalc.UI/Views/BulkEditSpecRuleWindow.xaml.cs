using System.Collections.Generic;
using System.Linq;
using System.Windows;
using VentCalc.UI.Services;

namespace VentCalc.UI.Views
{
    public partial class BulkEditSpecRuleWindow : Window
    {
        public BulkEditSpecRuleWindow(IReadOnlyList<SpecGroupRow> rows, string scope)
        {
            InitializeComponent();
            RuleScope = scope;
            if (rows.Count == 1)
            {
                SpecGroupRow row = rows[0];
                SectionValue = row.Section;
                GroupValue = row.Group;
                NameValue = row.Name;
                TypeMarkValue = row.TypeMark;
                SizeValue = row.Size;
                UnitValue = row.Unit;
                NoteValue = row.Note;
            }
            DataContext = this;
        }

        public string SectionValue { get; set; } = string.Empty;
        public string GroupValue { get; set; } = string.Empty;
        public string NameValue { get; set; } = string.Empty;
        public string TypeMarkValue { get; set; } = string.Empty;
        public string SizeValue { get; set; } = string.Empty;
        public string UnitValue { get; set; } = string.Empty;
        public string NoteValue { get; set; } = string.Empty;
        public string RuleScope { get; set; } = "Type";
        public bool TargetFiltered { get; set; }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
