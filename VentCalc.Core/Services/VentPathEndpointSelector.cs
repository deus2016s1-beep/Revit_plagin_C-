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
            ApplyDirection(selection, network);

            List<VentNetworkNode> openEnds = NodesByRole(network, VentNodeRole.OpenEndCandidate);
            List<VentNetworkNode> hoods = NodesByRole(network, VentNodeRole.HoodCandidate);
            List<VentNetworkNode> terminals = NodesByRole(network, VentNodeRole.TerminalCandidate);
            List<VentNetworkNode> fanCandidates = NodesByRole(network, VentNodeRole.FanCandidate);
            List<VentNetworkNode> equipment = NodesByRole(network, VentNodeRole.EquipmentCandidate);
            List<VentNetworkNode> caps = NodesByRole(network, VentNodeRole.Cap);
            List<VentNetworkNode> terminalSide = terminals.Concat(hoods).OrderBy(node => node.ElementId).ToList();

            selection.IgnoredCapElementIds = ToIds(caps);
            Dictionary<long, VentNetworkNode> nodesById = BuildNodeMap(network.Elements);
            List<VentConnectorEndpointStartInfo> connectorStarts = BuildConnectorStarts(terminalSide, network.Connections, nodesById);
            selection.ConnectorStarts = connectorStarts;
            selection.ConnectorLevelStartCount = connectorStarts.Count;
            selection.TerminalElementStartCount = terminalSide.Count;
            selection.OpenEndCandidates = ToCandidateInfos(openEnds, connectorStarts);
            selection.HoodCandidates = ToCandidateInfos(hoods, connectorStarts);
            selection.FanCandidates = ToCandidateInfos(fanCandidates, connectorStarts);

            if (selection.SystemDirection == "Supply")
            {
                List<VentNetworkNode> starts = fanCandidates.Count > 0 ? fanCandidates : equipment.Count > 0 ? equipment : openEnds;
                List<VentNetworkNode> ends = terminalSide;
                if (fanCandidates.Count == 0 && equipment.Count == 0 && openEnds.Count > 0)
                {
                    selection.Warnings.Add("Оборудование не найдено; используется открытый магистральный конец.");
                }

                ApplyCandidates(selection, starts, ends, network.Elements);
            }
            else if (selection.SystemDirection == "Exhaust")
            {
                List<VentNetworkNode> starts = terminalSide;
                List<VentNetworkNode> ends = fanCandidates.Count > 0 ? fanCandidates : ChooseMainOpenEnds(openEnds);
                if (fanCandidates.Count == 0 && openEnds.Count > 0)
                {
                    selection.Warnings.Add("Вентилятор/вытяжная установка не найдены; используется один открытый магистральный конец.");
                }

                ApplyCandidates(selection, starts, ends, network.Elements);
            }
            else
            {
                selection.FallbackUsed = true;
                selection.Warnings.Add("Направление системы определено приблизительно по топологии сети.");
                List<VentNetworkNode> starts = terminalSide;
                List<VentNetworkNode> ends = fanCandidates.Count > 0 ? fanCandidates : ChooseMainOpenEnds(openEnds);
                ApplyCandidates(selection, starts, ends, network.Elements);
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
            IReadOnlyList<VentNetworkNode> endNodes,
            IReadOnlyList<VentNetworkNode> allNodes)
        {
            List<VentNetworkNode> starts = startNodes.Where(node => node.Role != VentNodeRole.Cap).ToList();
            List<VentNetworkNode> ends = endNodes.Where(node => node.Role != VentNodeRole.Cap).ToList();
            var selectedIds = new HashSet<string>(starts.Concat(ends).Select(node => node.ElementId));

            foreach (VentNetworkNode node in starts)
            {
                node.IsStartCandidate = true;
            }

            foreach (VentNetworkNode node in ends)
            {
                node.IsEndCandidate = true;
            }

            selection.StartElementIds = ToIds(starts);
            selection.EndElementIds = ToIds(ends);
            selection.StartCandidates = ToCandidateInfos(starts, selection.ConnectorStarts);
            selection.EndCandidates = ToCandidateInfos(ends, selection.ConnectorStarts);
            selection.RejectedCandidates = ToCandidateInfos(allNodes
                .Where(node => IsEndpointRelevantRole(node.Role) && node.Role != VentNodeRole.Cap && !selectedIds.Contains(node.ElementId))
                .OrderBy(node => node.ElementId), selection.ConnectorStarts);
        }

        private static bool IsEndpointRelevantRole(VentNodeRole role)
        {
            return role == VentNodeRole.TerminalCandidate
                || role == VentNodeRole.HoodCandidate
                || role == VentNodeRole.FanCandidate
                || role == VentNodeRole.EquipmentCandidate
                || role == VentNodeRole.OpenEndCandidate
                || role == VentNodeRole.InlineEquipment;
        }

        private static void ApplyDirection(VentPathEndpointSelection selection, VentNetworkInfo network)
        {
            string systemTypeText = string.Join(" ", network.Elements.Select(node => node.SystemType));
            if (ContainsSupply(systemTypeText))
            {
                selection.SystemDirection = "Supply";
                selection.DirectionReason = "SystemType соответствует SupplyAir/Приточный воздух.";
                return;
            }

            if (ContainsExhaust(systemTypeText))
            {
                selection.SystemDirection = "Exhaust";
                selection.DirectionReason = "SystemType соответствует ReturnAir/Отработанный воздух.";
                return;
            }

            string systemNameText = string.Join(" ", network.Elements.Select(node => node.SystemName));
            if (ContainsSupply(systemNameText) || HasSupplyNameFallback(systemNameText))
            {
                selection.SystemDirection = "Supply";
                selection.DirectionReason = "SystemName содержит признак приточной системы; fallback по имени использован после SystemType.";
                return;
            }

            if (ContainsExhaust(systemNameText) || HasExhaustNameFallback(systemNameText))
            {
                selection.SystemDirection = "Exhaust";
                selection.DirectionReason = "SystemName содержит признак вытяжной системы; fallback по имени использован после SystemType.";
                return;
            }

            selection.SystemDirection = "Unknown";
            selection.DirectionReason = "SystemType/SystemName не содержит явного признака приточной или вытяжной системы.";
        }

        private static bool ContainsSupply(string text)
        {
            return ContainsAny(text, "SupplyAir", "Supply Air", "Приточный воздух", "приточная", "приток", "supply");
        }

        private static bool ContainsExhaust(string text)
        {
            return ContainsAny(text,
                "ExhaustAir",
                "Exhaust Air",
                "ReturnAir",
                "Return Air",
                "Отработанный воздух",
                "Вытяжной воздух",
                "вытяжная",
                "вытяжка",
                "exhaust",
                "return");
        }

        private static bool HasSupplyNameFallback(string text)
        {
            return ContainsAny(text, "П1", "П 1", "П2", "П 2", "П3", "П 3");
        }

        private static bool HasExhaustNameFallback(string text)
        {
            return ContainsAny(text, "В1", "В 1", "В2", "В 2", "В3", "В 3");
        }

        private static List<VentNetworkNode> ChooseMainOpenEnds(IReadOnlyList<VentNetworkNode> openEnds)
        {
            VentNetworkNode? selected = openEnds
                .OrderByDescending(node => node.ConnectedElementIds.Count)
                .ThenBy(node => ParseElementId(node.ElementId) ?? long.MaxValue)
                .FirstOrDefault();
            return selected == null ? new List<VentNetworkNode>() : new List<VentNetworkNode> { selected };
        }

        private static Dictionary<long, VentNetworkNode> BuildNodeMap(IEnumerable<VentNetworkNode> nodes)
        {
            return nodes
                .Select(node => new { Node = node, Id = ParseElementId(node.ElementId) })
                .Where(item => item.Id.HasValue)
                .GroupBy(item => item.Id.GetValueOrDefault())
                .ToDictionary(group => group.Key, group => group.First().Node);
        }

        private static List<VentConnectorEndpointStartInfo> BuildConnectorStarts(
            IReadOnlyList<VentNetworkNode> terminalSide,
            IReadOnlyList<VentNetworkConnection> connections,
            IReadOnlyDictionary<long, VentNetworkNode> nodesById)
        {
            var terminalIds = new HashSet<long>(terminalSide.Select(node => ParseElementId(node.ElementId) ?? 0).Where(id => id != 0));
            var starts = new List<VentConnectorEndpointStartInfo>();
            foreach (VentNetworkConnection connection in connections)
            {
                long? fromId = ParseElementId(connection.FromElementId);
                long? toId = ParseElementId(connection.ToElementId);
                if (!fromId.HasValue || !toId.HasValue)
                {
                    continue;
                }

                AddConnectorStartIfTerminal(starts, terminalIds, nodesById, fromId.Value, connection.FromConnectorIndex, toId.Value);
                AddConnectorStartIfTerminal(starts, terminalIds, nodesById, toId.Value, connection.ToConnectorIndex, fromId.Value);
            }

            return starts
                .GroupBy(start => start.LogicalKey)
                .Select(group => group.First())
                .OrderBy(start => start.ElementId)
                .ThenBy(start => start.ConnectorKey, StringComparer.Ordinal)
                .ThenBy(start => start.ConnectedElementId)
                .ToList();
        }

        private static void AddConnectorStartIfTerminal(
            ICollection<VentConnectorEndpointStartInfo> starts,
            ISet<long> terminalIds,
            IReadOnlyDictionary<long, VentNetworkNode> nodesById,
            long terminalId,
            int connectorIndex,
            long connectedElementId)
        {
            if (!terminalIds.Contains(terminalId)
                || !nodesById.TryGetValue(terminalId, out VentNetworkNode? terminalNode)
                || !nodesById.TryGetValue(connectedElementId, out VentNetworkNode? connectedNode))
            {
                return;
            }

            starts.Add(new VentConnectorEndpointStartInfo
            {
                ElementId = terminalId,
                ConnectorKey = $"C{connectorIndex}",
                ConnectorOrigin = "—",
                ConnectorDirection = "—",
                ConnectedElementId = connectedElementId,
                ConnectedElementCategory = connectedNode.CategoryName,
                SystemName = terminalNode.SystemName,
                EndpointRole = terminalNode.Role.ToString(),
                FlowM3h = ParseFlow(connectedNode.FlowM3h)
            });
        }

        private static List<VentNetworkNode> NodesByRole(VentNetworkInfo network, VentNodeRole role)
        {
            return network.Elements
                .Where(node => node.Role == role)
                .OrderBy(node => ParseElementId(node.ElementId) ?? long.MaxValue)
                .ToList();
        }

        private static List<long> ToIds(IEnumerable<VentNetworkNode> nodes)
        {
            return nodes
                .Select(node => ParseElementId(node.ElementId) ?? 0)
                .Where(id => id != 0)
                .Distinct()
                .ToList();
        }

        private static List<VentEndpointCandidateInfo> ToCandidateInfos(
            IEnumerable<VentNetworkNode> nodes,
            IReadOnlyList<VentConnectorEndpointStartInfo> connectorStarts)
        {
            return nodes
                .OrderBy(node => ParseElementId(node.ElementId) ?? long.MaxValue)
                .Select(node =>
                {
                    long elementId = ParseElementId(node.ElementId) ?? 0;
                    return new VentEndpointCandidateInfo
                    {
                        ElementId = node.ElementId,
                        Category = node.CategoryName,
                        FamilyName = node.FamilyName,
                        TypeName = node.TypeName,
                        Role = node.Role.ToString(),
                        ConnectorCount = node.ConnectorCount,
                        ConnectedHvacConnectorCount = Math.Max(0, node.ConnectorCount - node.OpenConnectorCount),
                        GraphDegree = node.ConnectedElementIds.Count,
                        Reason = node.PathRoleReason,
                        ConnectorStarts = connectorStarts.Where(start => start.ElementId == elementId).ToList()
                    };
                })
                .ToList();
        }

        private static double ParseFlow(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "—")
            {
                return 0;
            }

            string number = new string(value.Replace(',', '.').Where(ch => char.IsDigit(ch) || ch == '.' || ch == '-' || ch == '+').ToArray());
            return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : 0;
        }

        private static long? ParseElementId(string elementId)
        {
            return long.TryParse(elementId, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) ? parsed : null;
        }

        private static bool ContainsAny(string text, params string[] patterns)
        {
            foreach (string pattern in patterns)
            {
                if (text.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
