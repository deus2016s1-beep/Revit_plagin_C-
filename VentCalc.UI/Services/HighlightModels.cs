using System;
using System.Collections.Generic;
using System.Linq;

namespace VentCalc.UI.Services
{
    public enum HighlightMode
    {
        None = 0,
        SelectedPath = 1,
        CriticalPath = 2,
        Velocity = 3,
        Issues = 4
    }

    public enum HighlightAction
    {
        Apply = 0,
        Clear = 1
    }

    public sealed class HighlightElementGroup
    {
        public string Name { get; set; } = string.Empty;

        public string ColorHex { get; set; } = "#FF0000";

        public int LineWeight { get; set; } = 6;

        public int Transparency { get; set; } = 35;

        public List<long> ElementIds { get; set; } = new List<long>();
    }

    public sealed class HighlightRequest
    {
        public HighlightAction Action { get; set; }

        public HighlightMode Mode { get; set; }

        public string StatusMessage { get; set; } = string.Empty;

        public bool SelectElements { get; set; }

        public bool ShowElements { get; set; }

        public List<HighlightElementGroup> Groups { get; set; } = new List<HighlightElementGroup>();

        public int RequestedElementCount => Groups.SelectMany(group => group.ElementIds).Distinct().Count();
    }

    public sealed class HighlightResult
    {
        public HighlightMode ActiveMode { get; set; }

        public long ActiveViewId { get; set; }

        public int RequestedElementCount { get; set; }

        public int HighlightedElementCount { get; set; }

        public int SkippedElementCount { get; set; }

        public int FailedElementCount { get; set; }

        public int RestoredElementCount { get; set; }

        public int SnapshotCount { get; set; }

        public bool SelectionChangedByVentCalc { get; set; }

        public int SelectionElementCountBefore { get; set; }

        public int SelectionElementCountAfter { get; set; }

        public bool ShowElementsUsed { get; set; }

        public string Message { get; set; } = string.Empty;

        public List<string> Errors { get; set; } = new List<string>();

        public bool ApplySucceeded => FailedElementCount == 0 && (RequestedElementCount == 0 || HighlightedElementCount > 0 || ActiveMode == HighlightMode.None);

        public bool ClearSucceeded => FailedElementCount == 0;

        public bool OriginalOverridesRestored => ActiveMode != HighlightMode.None || SnapshotCount == 0;
    }

    public sealed class HighlightVelocityGroupsInfo
    {
        public int BelowMin { get; set; }

        public int Normal { get; set; }

        public int AboveMax { get; set; }

        public int Critical { get; set; }

        public int NotCalculated { get; set; }
    }

    public sealed class HighlightStateInfo
    {
        public HighlightMode ActiveMode { get; set; } = HighlightMode.None;

        public long ActiveViewId { get; set; }

        public int RequestedElementCount { get; set; }

        public int HighlightedElementCount { get; set; }

        public int SkippedElementCount { get; set; }

        public int FailedElementCount { get; set; }

        public int RestoredElementCount { get; set; }

        public int SnapshotCount { get; set; }

        public bool SelectionChangedByVentCalc { get; set; }

        public int SelectionElementCountBefore { get; set; }

        public int SelectionElementCountAfter { get; set; }

        public bool ShowElementsUsed { get; set; }

        public HighlightVelocityGroupsInfo VelocityGroups { get; set; } = new HighlightVelocityGroupsInfo();

        public int IssueElementCount { get; set; }

        public bool HighlightApplySucceeded { get; set; } = true;

        public bool HighlightClearSucceeded { get; set; } = true;

        public bool OriginalOverridesRestored { get; set; } = true;

        public List<string> Errors { get; set; } = new List<string>();
    }
}
