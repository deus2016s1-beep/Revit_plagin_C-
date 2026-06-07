using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace VentCalc.Core.Models
{
    public sealed class VentPathInfo
    {
        public VentPathInfo(
            int pathIndex,
            string startElementId,
            string endElementId,
            IEnumerable<string> elementIds,
            IEnumerable<VentPathNode> nodes,
            int ductCount,
            int fittingCount,
            int accessoryCount,
            int terminalCount,
            int equipmentCount,
            int totalElementCount,
            double totalDuctLengthMm,
            string maxFlowM3h,
            string pathKind,
            string startConnectorKey = "",
            string connectedStartElementId = "",
            string startFlowM3h = "",
            string endFlowM3h = "")
        {
            PathIndex = pathIndex;
            StartElementId = startElementId;
            EndElementId = endElementId;
            ElementIds = new ReadOnlyCollection<string>(elementIds.ToList());
            Nodes = new ReadOnlyCollection<VentPathNode>(nodes.ToList());
            DuctCount = ductCount;
            FittingCount = fittingCount;
            AccessoryCount = accessoryCount;
            TerminalCount = terminalCount;
            EquipmentCount = equipmentCount;
            TotalElementCount = totalElementCount;
            TotalDuctLengthMm = totalDuctLengthMm;
            MaxFlowM3h = maxFlowM3h;
            PathKind = pathKind;
            StartConnectorKey = startConnectorKey;
            ConnectedStartElementId = connectedStartElementId;
            StartFlowM3h = string.IsNullOrWhiteSpace(startFlowM3h) ? maxFlowM3h : startFlowM3h;
            EndFlowM3h = string.IsNullOrWhiteSpace(endFlowM3h) ? maxFlowM3h : endFlowM3h;
        }

        public int PathIndex { get; }

        public string StartElementId { get; }

        public string EndElementId { get; }

        public IReadOnlyList<string> ElementIds { get; }

        public IReadOnlyList<VentPathNode> Nodes { get; }

        public int DuctCount { get; }

        public int FittingCount { get; }

        public int AccessoryCount { get; }

        public int TerminalCount { get; }

        public int EquipmentCount { get; }

        public int TotalElementCount { get; }

        public double TotalDuctLengthMm { get; }

        public double TotalDuctLengthM => TotalDuctLengthMm / 1000.0;

        public string MaxFlowM3h { get; }

        public string StartConnectorKey { get; }

        public string ConnectedStartElementId { get; }

        public string StartFlowM3h { get; }

        public string EndFlowM3h { get; }

        public string FlowRangeM3h => StartFlowM3h == EndFlowM3h ? MaxFlowM3h : $"{StartFlowM3h} → {EndFlowM3h}";

        public string PathKind { get; }
    }
}
