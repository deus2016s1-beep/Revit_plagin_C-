using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using VentCalc.Core.Models;

namespace VentCalc.Core.Services
{
    public sealed class VentPathFinder
    {
        public VentPathSummary FindPaths(VentNetworkInfo networkInfo)
        {
            string systemType = DetectSystemType(networkInfo);
            SystemDirection direction = DetectDirection(systemType);
            bool approximateDirection = direction == SystemDirection.Unknown;
            string directionText = ToDirectionText(direction);

            IReadOnlyList<VentNetworkNode> startNodes = FindStartNodes(networkInfo, direction);
            IReadOnlyList<VentNetworkNode> endNodes = FindEndNodes(networkInfo, direction);
            string note = BuildDirectionNote(direction, startNodes, endNodes);

            if (startNodes.Count == 0 || endNodes.Count == 0)
            {
                return new VentPathSummary(
                    systemType,
                    directionText,
                    approximateDirection,
                    note,
                    startNodes.Select(node => node.ElementId),
                    endNodes.Select(node => node.ElementId),
                    Array.Empty<VentPathInfo>(),
                    BuildNoPathReason(startNodes.Count, endNodes.Count));
            }

            Dictionary<string, VentNetworkNode> nodesById = networkInfo.Elements.ToDictionary(node => node.ElementId);
            Dictionary<string, List<string>> graph = BuildGraph(networkInfo);
            var rawPaths = new List<IReadOnlyList<string>>();
            var endIds = new HashSet<string>(endNodes.Select(node => node.ElementId));

            foreach (VentNetworkNode startNode in startNodes)
            {
                FindSimplePaths(startNode.ElementId, endIds, graph, new HashSet<string>(), new List<string>(), rawPaths);
            }

            var paths = rawPaths
                .Select(path => CreatePath(path, nodesById, directionText))
                .OrderByDescending(path => path.TotalDuctLengthMm)
                .ThenByDescending(path => path.TotalElementCount)
                .ThenBy(path => path.StartElementId)
                .ThenBy(path => path.EndElementId)
                .Select((path, index) => ReindexPath(path, index + 1))
                .ToList();

            return new VentPathSummary(
                systemType,
                directionText,
                approximateDirection,
                note,
                startNodes.Select(node => node.ElementId),
                endNodes.Select(node => node.ElementId),
                paths,
                paths.Count == 0 ? "Между найденными стартовыми и конечными точками нет трасс по графу сети." : string.Empty);
        }

        private static Dictionary<string, List<string>> BuildGraph(VentNetworkInfo networkInfo)
        {
            var graph = networkInfo.Elements.ToDictionary(node => node.ElementId, _ => new List<string>());
            foreach (VentNetworkConnection connection in networkInfo.Connections)
            {
                AddEdge(graph, connection.FromElementId, connection.ToElementId);
                AddEdge(graph, connection.ToElementId, connection.FromElementId);
            }

            foreach (List<string> neighbors in graph.Values)
            {
                neighbors.Sort(StringComparer.Ordinal);
            }

            return graph;
        }

        private static void AddEdge(IDictionary<string, List<string>> graph, string fromElementId, string toElementId)
        {
            if (!graph.TryGetValue(fromElementId, out List<string>? neighbors))
            {
                return;
            }

            if (!neighbors.Contains(toElementId))
            {
                neighbors.Add(toElementId);
            }
        }

        private static void FindSimplePaths(
            string currentElementId,
            ISet<string> endElementIds,
            IReadOnlyDictionary<string, List<string>> graph,
            ISet<string> visitedElementIds,
            IList<string> currentPath,
            ICollection<IReadOnlyList<string>> paths)
        {
            visitedElementIds.Add(currentElementId);
            currentPath.Add(currentElementId);

            if (endElementIds.Contains(currentElementId) && currentPath.Count > 1)
            {
                paths.Add(currentPath.ToList());
            }
            else if (graph.TryGetValue(currentElementId, out List<string>? neighbors))
            {
                foreach (string neighborElementId in neighbors)
                {
                    if (!visitedElementIds.Contains(neighborElementId))
                    {
                        FindSimplePaths(neighborElementId, endElementIds, graph, visitedElementIds, currentPath, paths);
                    }
                }
            }

            currentPath.RemoveAt(currentPath.Count - 1);
            visitedElementIds.Remove(currentElementId);
        }

        private static VentPathInfo CreatePath(IReadOnlyList<string> elementIds, IReadOnlyDictionary<string, VentNetworkNode> nodesById, string pathKind)
        {
            var nodes = elementIds
                .Select(elementId => nodesById[elementId])
                .ToList();

            var pathNodes = nodes
                .Select(node => new VentPathNode(
                    node.ElementId,
                    node.CategoryName,
                    node.TypeName,
                    node.FamilyName,
                    node.Size,
                    node.FlowM3h,
                    node.DuctLengthMm))
                .ToList();

            return new VentPathInfo(
                0,
                elementIds.First(),
                elementIds.Last(),
                elementIds,
                pathNodes,
                nodes.Count(IsDuct),
                nodes.Count(IsFitting),
                nodes.Count(IsTerminal),
                nodes.Count(IsEquipment),
                nodes.Count,
                nodes.Where(IsDuct).Sum(node => node.DuctLengthMm),
                FormatFlow(nodes.Max(node => ParseNumber(node.FlowM3h))),
                pathKind);
        }

        private static VentPathInfo ReindexPath(VentPathInfo path, int pathIndex)
        {
            return new VentPathInfo(
                pathIndex,
                path.StartElementId,
                path.EndElementId,
                path.ElementIds,
                path.Nodes,
                path.DuctCount,
                path.FittingCount,
                path.TerminalCount,
                path.EquipmentCount,
                path.TotalElementCount,
                path.TotalDuctLengthMm,
                path.MaxFlowM3h,
                path.PathKind);
        }

        private static IReadOnlyList<VentNetworkNode> FindStartNodes(VentNetworkInfo networkInfo, SystemDirection direction)
        {
            List<VentNetworkNode> equipment = networkInfo.Elements.Where(IsEquipment).ToList();
            List<VentNetworkNode> terminals = networkInfo.Elements.Where(IsTerminal).ToList();
            List<VentNetworkNode> openEnds = networkInfo.Elements.Where(node => node.OpenConnectorCount > 0).ToList();
            List<VentNetworkNode> leaves = FindLeaves(networkInfo);

            IEnumerable<VentNetworkNode> result = direction switch
            {
                SystemDirection.Supply => equipment.Count > 0
                    ? equipment
                    : openEnds.Concat(leaves.Where(node => !IsTerminal(node))),
                SystemDirection.Exhaust => terminals,
                _ => terminals.Count > 0 ? terminals : openEnds.Concat(equipment).Concat(leaves)
            };

            return DistinctAndSort(result);
        }

        private static IReadOnlyList<VentNetworkNode> FindEndNodes(VentNetworkInfo networkInfo, SystemDirection direction)
        {
            List<VentNetworkNode> equipment = networkInfo.Elements.Where(IsEquipment).ToList();
            List<VentNetworkNode> terminals = networkInfo.Elements.Where(IsTerminal).ToList();
            List<VentNetworkNode> openEnds = networkInfo.Elements.Where(node => node.OpenConnectorCount > 0).ToList();
            List<VentNetworkNode> leaves = FindLeaves(networkInfo);

            IEnumerable<VentNetworkNode> result = direction switch
            {
                SystemDirection.Supply => terminals,
                SystemDirection.Exhaust => equipment.Count > 0
                    ? equipment.Concat(openEnds)
                    : openEnds.Concat(leaves.Where(node => !IsTerminal(node))),
                _ => openEnds.Concat(equipment).Concat(leaves.Where(node => !IsTerminal(node)))
            };

            return DistinctAndSort(result);
        }

        private static List<VentNetworkNode> FindLeaves(VentNetworkInfo networkInfo)
        {
            Dictionary<string, int> degreeByElementId = networkInfo.Elements.ToDictionary(node => node.ElementId, _ => 0);
            foreach (VentNetworkConnection connection in networkInfo.Connections)
            {
                if (degreeByElementId.ContainsKey(connection.FromElementId))
                {
                    degreeByElementId[connection.FromElementId]++;
                }

                if (degreeByElementId.ContainsKey(connection.ToElementId))
                {
                    degreeByElementId[connection.ToElementId]++;
                }
            }

            return networkInfo.Elements
                .Where(node => degreeByElementId.TryGetValue(node.ElementId, out int degree) && degree <= 1)
                .ToList();
        }

        private static IReadOnlyList<VentNetworkNode> DistinctAndSort(IEnumerable<VentNetworkNode> nodes)
        {
            return nodes
                .GroupBy(node => node.ElementId)
                .Select(group => group.First())
                .OrderBy(node => node.ElementId)
                .ToList();
        }

        private static string DetectSystemType(VentNetworkInfo networkInfo)
        {
            return networkInfo.Elements
                .Select(node => node.SystemType)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value) && value != "—")
                ?? "—";
        }

        private static SystemDirection DetectDirection(string systemType)
        {
            if (ContainsAny(systemType, "Приточный", "Приток", "Supply"))
            {
                return SystemDirection.Supply;
            }

            if (ContainsAny(systemType, "Вытяжной", "Вытяжка", "Exhaust"))
            {
                return SystemDirection.Exhaust;
            }

            return SystemDirection.Unknown;
        }

        private static string ToDirectionText(SystemDirection direction)
        {
            return direction switch
            {
                SystemDirection.Supply => "Приточная сеть: от оборудования/открытого магистрального конца к терминалам",
                SystemDirection.Exhaust => "Вытяжная сеть: от терминалов к оборудованию/открытому магистральному концу",
                _ => "Неизвестное направление: fallback терминалы ↔ оборудование/открытые концы"
            };
        }

        private static string BuildDirectionNote(
            SystemDirection direction,
            IReadOnlyList<VentNetworkNode> startNodes,
            IReadOnlyList<VentNetworkNode> endNodes)
        {
            if (direction == SystemDirection.Supply && startNodes.Count > 0 && startNodes.All(node => !IsEquipment(node)))
            {
                return "Оборудование не найдено; открытый магистральный конец/лист сети используется как стартовый кандидат.";
            }

            if (direction == SystemDirection.Unknown)
            {
                return "Тип системы не содержит явного признака приточной или вытяжной системы.";
            }

            return startNodes.Count == 0 || endNodes.Count == 0 ? "Недостаточно стартовых или конечных точек для построения трасс." : string.Empty;
        }

        private static string BuildNoPathReason(int startCount, int endCount)
        {
            if (startCount == 0 && endCount == 0)
            {
                return "Стартовые и конечные точки не найдены. Проверьте тип системы, терминалы, оборудование и открытые коннекторы.";
            }

            if (startCount == 0)
            {
                return "Стартовые точки не найдены. Для приточной системы нужен элемент оборудования или открытый магистральный конец; для вытяжной — терминал/решётка.";
            }

            if (endCount == 0)
            {
                return "Конечные точки не найдены. Для приточной системы нужны терминалы/решётки; для вытяжной — оборудование или открытый магистральный конец.";
            }

            return "Трассы не найдены.";
        }

        private static bool IsDuct(VentNetworkNode node)
        {
            return MatchesCategory(node, "OST_DuctCurves", "ductcurves", "воздуховод", "duct");
        }

        private static bool IsFitting(VentNetworkNode node)
        {
            return MatchesCategory(node, "OST_DuctFitting", "ductfitting", "соедин", "fitting");
        }

        private static bool IsTerminal(VentNetworkNode node)
        {
            return MatchesCategory(node, "OST_DuctTerminal", "ductterminal", "термин", "реш", "terminal", "diffuser", "grille");
        }

        private static bool IsEquipment(VentNetworkNode node)
        {
            return MatchesCategory(node, "OST_MechanicalEquipment", "mechanicalequipment", "оборуд", "equipment", "fan", "вентил");
        }

        private static bool MatchesCategory(VentNetworkNode node, params string[] patterns)
        {
            string text = $"{node.CategoryKey} {node.CategoryName} {node.TypeName} {node.FamilyName}";
            return ContainsAny(text, patterns);
        }

        private static bool ContainsAny(string text, params string[] patterns)
        {
            return patterns.Any(pattern => text.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static double ParseNumber(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "—")
            {
                return 0;
            }

            Match match = Regex.Match(value.Replace(',', '.'), @"[-+]?\d+(\.\d+)?");
            return match.Success && double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
                ? number
                : 0;
        }

        private static string FormatFlow(double flow)
        {
            return flow > 0 ? $"{flow:0.###} м³/ч" : "—";
        }

        private enum SystemDirection
        {
            Supply,
            Exhaust,
            Unknown
        }
    }
}
