using VentCalc.UI.ViewModels;

namespace VentCalc.UI.Services
{
    public static class VentCalcSessionState
    {
        public static VentCalcCenterData? CurrentData { get; private set; }

        public static HighlightStateInfo LastHighlightState { get; } = new HighlightStateInfo();

        public static void StoreData(VentCalcCenterData data)
        {
            if (data.Success)
            {
                CurrentData = data;
            }
        }

        public static void StoreHighlight(HighlightStateInfo state)
        {
            LastHighlightState.ActiveMode = state.ActiveMode;
            LastHighlightState.ActiveViewId = state.ActiveViewId;
            LastHighlightState.RequestedElementCount = state.RequestedElementCount;
            LastHighlightState.HighlightedElementCount = state.HighlightedElementCount;
            LastHighlightState.SkippedElementCount = state.SkippedElementCount;
            LastHighlightState.FailedElementCount = state.FailedElementCount;
            LastHighlightState.RestoredElementCount = state.RestoredElementCount;
            LastHighlightState.SnapshotCount = state.SnapshotCount;
            LastHighlightState.SelectionChangedByVentCalc = state.SelectionChangedByVentCalc;
            LastHighlightState.SelectionElementCountBefore = state.SelectionElementCountBefore;
            LastHighlightState.SelectionElementCountAfter = state.SelectionElementCountAfter;
            LastHighlightState.ShowElementsUsed = state.ShowElementsUsed;
            LastHighlightState.WindowSource = state.WindowSource;
            LastHighlightState.ActiveDisplayMode = state.ActiveDisplayMode;
            LastHighlightState.SystemNameAtApply = state.SystemNameAtApply;
            LastHighlightState.PathIndexAtApply = state.PathIndexAtApply;
            LastHighlightState.IsCriticalPath = state.IsCriticalPath;
            LastHighlightState.HighlightedPathElementCount = state.HighlightedPathElementCount;
            LastHighlightState.DimmedSystemElementCount = state.DimmedSystemElementCount;
            LastHighlightState.StartElementId = state.StartElementId;
            LastHighlightState.EndElementId = state.EndElementId;
            LastHighlightState.LastApplySucceeded = state.LastApplySucceeded;
            LastHighlightState.LastClearSucceeded = state.LastClearSucceeded;
            LastHighlightState.HighlightApplySucceeded = state.HighlightApplySucceeded;
            LastHighlightState.HighlightClearSucceeded = state.HighlightClearSucceeded;
            LastHighlightState.OriginalOverridesRestored = state.OriginalOverridesRestored;
            LastHighlightState.Errors = state.Errors;
        }
    }
}
