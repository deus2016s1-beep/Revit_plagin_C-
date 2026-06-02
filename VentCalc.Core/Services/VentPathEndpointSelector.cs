using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VentCalc.Core.Models;

namespace VentCalc.Core.Services
{
    public sealed class VentPathEndpointSelector
    {
        private readonly VentNodeClassifier classifier = new VentNodeClassifier();

        public VentPathEndpointSelection SelectEndpoints(VentNetworkInfo network)
        {
            classifier.Classify(network.Elements);
            ClearEndpointFlags(network.Elements);

            var selection = new VentPathEndpointSelection();
            string systemText = DetectSystemText(network);
            ApplyDirection(selection, systemText);

            List<VentNetworkNode> equipment = NodesByRole(network, VentNodeRole.EquipmentCandidate);
            List<VentNetworkNode> openEnds = NodesByRole(network, VentNodeRole.OpenEndCandidate);
            List<VentNetworkNode> terminals = NodesByRole(network, VentNodeRole.TerminalCandidate);
            List<VentNetworkNode> caps = NodesByRole(network, VentNodeRole.Cap);

            selection.IgnoredCapElementIds = ToIds(caps);

            if (selection.SystemDirection == "Supply")
            {
                List<VentNetworkNode> starts = equipment.Count > 0 ? equipment : openEnds;
                List<VentNetworkNode> ends = terminals;
                if (equipment.Count == 0 && openEnds.Count > 0)
                {
                    selection.Warnings.Add("Оборудование не найдено; используется открытый магистральный конец.");
                }

                ApplyCandidates(selection, starts, ends);
            }
            else if (selection.SystemDirection == "Exhaust")
            {
                List<VentNetworkNode> starts = terminals;
                List<VentNetworkNode> ends = equipment.Count > 0 ? equipment : openEnds;
                if (equipment.Count == 0 && openEnds.Count > 0)
                {
                    selection.Warnings.Add("Оборудование не найдено; используется открытый магистральный конец.");
                }

                ApplyCandidates(selection, starts, ends);
            }
            else
            {
                selection.Warnings.Add("Направление системы определено приблизительно.");
                List<VentNetworkNode> starts = terminals;
                List<VentNetworkNode> ends = equipment.Count > 0 ? equipment : openEnds;
                ApplyCandidates(selection, starts, ends);
            }

            if (selection.StartElementIds.Count == 0)
            {
                selection.Warnings.Add("Стартовые точки не найдены.");
            }

            if (selection.EndElementIds.Count == 0)
            {
                selection.Warnings.Add("Конечные точки не найдены.");
            }

            return selection;
        }

        private static void ClearEndpointFlags(IEnumerable<VentNetworkNode> nodes)
        {
            foreach (VentNetworkNode node in nodes)
            {
                node.IsStartCandidate = false;
                node.IsEndCandidate = false;
            }
        }

        private static void ApplyCandidates(
            VentPathEndpointSelection selection,
            IReadOnlyList<VentNetworkNode> startNodes,
            IReadOnlyList<VentNetworkNode> endNodes)
        {
            foreach (VentNetworkNode node in startNodes.Where(node => node.Role != VentNodeRole.Cap))
            {
                node.IsStartCandidate = true;
            }

            foreach (VentNetworkNode node in endNodes.Where(node => node.Role != VentNodeRole.Cap))
            {
                node.IsEndCandidate = true;
            }

            selection.StartElementIds = ToIds(startNodes.Where(node => node.Role != VentNodeRole.Cap));
            selection.EndElementIds = ToIds(endNodes.Where(node => node.Role != VentNodeRole.Cap));
        }

        private static void ApplyDirection(VentPathEndpointSelection selection, string systemText)
        {
            if (ContainsAny(systemText, "Приточный", "Приток", "Supply"))
            {
                selection.SystemDirection = "Supply";
                selection.DirectionReason = "SystemType/SystemName содержит признак приточной системы.";
                return;
            }

            if (ContainsAny(systemText, "Вытяжной", "Вытяжка", "Exhaust"))
            {
                selection.SystemDirection = "Exhaust";
                selection.DirectionReason = "SystemType/SystemName содержит признак вытяжной системы.";
                return;
            }

            selection.SystemDirection = "Unknown";
            selection.DirectionReason = "SystemType/SystemName не содержит явного признака приточной или вытяжной системы.";
        }

        private static string DetectSystemText(VentNetworkInfo network)
        {
            return string.Join(" ", network.Elements.Select(node => $"{node.SystemType} {node.SystemName}"));
        }

        private static List<VentNetworkNode> NodesByRole(VentNetworkInfo network, VentNodeRole role)
        {
            return network.Elements
                .Where(node => node.Role == role)
                .OrderBy(node => node.ElementId)
                .ToList();
        }

        private static List<long> ToIds(IEnumerable<VentNetworkNode> nodes)
        {
            return nodes
                .Select(node => long.TryParse(node.ElementId, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id) ? id : 0)
                .Where(id => id != 0)
                .Distinct()
                .OrderBy(id => id)
                .ToList();
        }

        private static bool ContainsAny(string text, params string[] patterns)
        {
            return patterns.Any(pattern => text.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
