using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace VentCalc.Core.Models
{
    public sealed class LocalResistanceCalculationInfo : INotifyPropertyChanged
    {
        private double zeta;
        private double autoZeta;
        private double? manualZeta;
        private string manualZetaText = string.Empty;
        private double effectiveZeta;
        private string source = string.Empty;
        private string zetaSource = string.Empty;
        private string zetaComment = string.Empty;
        private string validationMessage = string.Empty;
        private string lastWriteError = string.Empty;
        private double localPressureLossPa;
        private bool wasWrittenToRevitComment;
        private bool wasSavedToVentCalcStorage;

        public event PropertyChangedEventHandler? PropertyChanged;

        public int DisplayNumber { get; set; }

        public long ElementId { get; set; }

        public int PathIndex { get; set; }

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

        public double? ActualAngleDeg { get; set; }

        public double? RoundedAngleDeg { get; set; }

        public bool AngleWasRounded { get; set; }

        public string AngleRoundingWarning { get; set; } = string.Empty;

        public double Zeta
        {
            get => zeta;
            set => SetProperty(ref zeta, value);
        }

        public double AutoZeta
        {
            get => autoZeta;
            set
            {
                if (SetProperty(ref autoZeta, value))
                {
                    OnPropertyChanged(nameof(AutoZetaText));
                }
            }
        }

        public double? ProjectCatalogZeta { get; set; }

        public double? ManualZeta
        {
            get => manualZeta;
            set
            {
                if (!SetProperty(ref manualZeta, value))
                {
                    return;
                }

                manualZetaText = value.HasValue ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : string.Empty;
                OnPropertyChanged(nameof(ManualZetaText));
                OnPropertyChanged(nameof(UsedZetaText));
                OnPropertyChanged(nameof(HasManualOverride));
                if (value.HasValue)
                {
                    ValidationMessage = string.Empty;
                    ApplyManualZeta(value.Value);
                }
            }
        }

        public string ManualZetaText
        {
            get => manualZetaText;
            set
            {
                if (!SetProperty(ref manualZetaText, value ?? string.Empty))
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(manualZetaText))
                {
                    manualZeta = null;
                    ValidationMessage = string.Empty;
                    EffectiveZeta = OriginalEffectiveZeta;
                    Zeta = OriginalEffectiveZeta;
                    Source = string.IsNullOrWhiteSpace(OriginalZetaSource) ? Source : OriginalZetaSource;
                    ZetaSource = Source;
                    LocalPressureLossPa = EffectiveZeta * DynamicPressurePa;
                    OnPropertyChanged(nameof(ManualZeta));
                    OnPropertyChanged(nameof(UsedZetaText));
                    OnPropertyChanged(nameof(HasManualOverride));
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(LocalizedSource));
                    return;
                }

                if (!double.TryParse(manualZetaText.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) || parsed < 0)
                {
                    ValidationMessage = "Некорректное значение ζ";
                    return;
                }

                ValidationMessage = string.Empty;
                manualZeta = parsed;
                OnPropertyChanged(nameof(ManualZeta));
                ApplyManualZeta(parsed);
            }
        }

        public string UsedZetaText
        {
            get => (ManualZeta ?? EffectiveZeta).ToString("0.###", CultureInfo.InvariantCulture);
            set => ManualZetaText = value;
        }

        public string AutoZetaText => AutoZeta.ToString("0.###", CultureInfo.InvariantCulture);

        public string AngleDisplay => RoundedAngleDeg.HasValue && PathRole.StartsWith("Elbow", StringComparison.OrdinalIgnoreCase)
            ? $"{RoundedAngleDeg.Value:0.#}°"
            : string.Empty;

        public string LocalizedKind => LocalizeRole(PathRole, LocalKind);

        public string LocalizedSource => LocalizeSource(ZetaSource);

        public double EffectiveZeta
        {
            get => effectiveZeta;
            set
            {
                if (SetProperty(ref effectiveZeta, value))
                {
                    OnPropertyChanged(nameof(UsedZetaText));
                }
            }
        }

        public string ZetaSource
        {
            get => zetaSource;
            set
            {
                if (SetProperty(ref zetaSource, value))
                {
                    OnPropertyChanged(nameof(LocalizedSource));
                }
            }
        }

        public string ZetaComment
        {
            get => zetaComment;
            set => SetProperty(ref zetaComment, value);
        }

        public bool PathDependent { get; set; }

        public string OverrideKey { get; set; } = string.Empty;

        public string OverrideStorageType { get; set; } = string.Empty;

        public double OriginalEffectiveZeta { get; set; }

        public string OriginalZetaSource { get; set; } = string.Empty;

        public string StatusText => string.IsNullOrWhiteSpace(ValidationMessage) ? (PathDependent ? "Path-specific" : "Element") : ValidationMessage;

        public string DisplayRole => string.IsNullOrWhiteSpace(PathRole) ? LocalKind : PathRole;

        public string TechnicalDetails => $"Prev={PreviousDuctElementId}; Next={NextDuctElementId}; PrevA={PreviousAreaM2:0.####}; NextA={NextAreaM2:0.####}; PrevQ={PreviousFlowM3h:0.###}; NextQ={NextFlowM3h:0.###}; ActualAngle={ActualAngleDeg:0.#}; RoundedAngle={RoundedAngleDeg:0.#}; Reason={RoleReason}; Comment={ZetaComment}; Verification={ValidationMessage}";

        public string ValidationMessage
        {
            get => validationMessage;
            set => SetProperty(ref validationMessage, value);
        }

        public string LastWriteError
        {
            get => lastWriteError;
            set => SetProperty(ref lastWriteError, value);
        }

        public bool CanEditManualZeta => true;

        public bool CanWriteComment => ElementId > 0;

        public bool WasWrittenToRevitComment
        {
            get => wasWrittenToRevitComment;
            set => SetProperty(ref wasWrittenToRevitComment, value);
        }

        public bool WasSavedToVentCalcStorage
        {
            get => wasSavedToVentCalcStorage;
            set => SetProperty(ref wasSavedToVentCalcStorage, value);
        }

        public bool HasManualOverride => ManualZeta.HasValue;

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

        private void ApplyManualZeta(double value)
        {
            EffectiveZeta = value;
            Zeta = value;
            Source = "Вручную";
            ZetaSource = "Вручную";
            OverrideStorageType = PathDependent ? "DataStorage" : "Comment";
            LocalPressureLossPa = value * DynamicPressurePa;
            OnPropertyChanged(nameof(HasManualOverride));
            OnPropertyChanged(nameof(StatusText));
        }

        private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(storage, value))
            {
                return false;
            }

            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
