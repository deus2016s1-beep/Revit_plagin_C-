using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VentCalc.Core.Models
{
    public sealed class LocalResistanceCalculationInfo : INotifyPropertyChanged
    {
        private double zeta;
        private double autoZeta;
        private double? manualZeta;
        private double effectiveZeta;
        private string source = string.Empty;
        private string zetaSource = string.Empty;
        private string zetaComment = string.Empty;
        private double localPressureLossPa;
        private bool wasWrittenToRevitComment;

        public event PropertyChangedEventHandler? PropertyChanged;

        public long ElementId { get; set; }

        public string CategoryName { get; set; } = string.Empty;

        public string FamilyName { get; set; } = string.Empty;

        public string TypeName { get; set; } = string.Empty;

        public string Size { get; set; } = string.Empty;

        public string LocalKind { get; set; } = string.Empty;

        public string PathRole { get; set; } = string.Empty;

        public string RoleReason { get; set; } = string.Empty;

        public long? PreviousDuctElementId { get; set; }

        public long? NextDuctElementId { get; set; }

        public double PreviousAreaM2 { get; set; }

        public double NextAreaM2 { get; set; }

        public double PreviousFlowM3h { get; set; }

        public double NextFlowM3h { get; set; }

        public double Zeta
        {
            get => zeta;
            set => SetProperty(ref zeta, value);
        }

        public double AutoZeta
        {
            get => autoZeta;
            set => SetProperty(ref autoZeta, value);
        }

        public double? ManualZeta
        {
            get => manualZeta;
            set => SetProperty(ref manualZeta, value);
        }

        public double EffectiveZeta
        {
            get => effectiveZeta;
            set => SetProperty(ref effectiveZeta, value);
        }

        public string ZetaSource
        {
            get => zetaSource;
            set => SetProperty(ref zetaSource, value);
        }

        public string ZetaComment
        {
            get => zetaComment;
            set => SetProperty(ref zetaComment, value);
        }

        public bool WasWrittenToRevitComment
        {
            get => wasWrittenToRevitComment;
            set => SetProperty(ref wasWrittenToRevitComment, value);
        }

        public double FlowM3h { get; set; }

        public double AreaM2 { get; set; }

        public double VelocityMs { get; set; }

        public double DynamicPressurePa { get; set; }

        public double LocalPressureLossPa
        {
            get => localPressureLossPa;
            set => SetProperty(ref localPressureLossPa, value);
        }

        public string Source
        {
            get => source;
            set => SetProperty(ref source, value);
        }

        public List<string> Warnings { get; set; } = new List<string>();

        public string WarningText => string.Join("; ", Warnings);

        private void SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(storage, value))
            {
                return;
            }

            storage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
