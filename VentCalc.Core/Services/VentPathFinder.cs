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
        private readonly VentPathEndpointSelector endpointSelector = new VentPathEndpointSelector();

        public VentPathSummary FindPaths(VentNetworkInfo networkInfo)
        {
            VentPathEndpointSelection endpointSelection = endpointSelector.SelectEndpoints(networkInfo);
            Dictionary<long, VentNetworkNode> nodesById = BuildNodeMap(networkInfo);
            Dictionary<long, HashSet<long>> adjacency = BuildAdjacency(networkInfo, nodesById);
            int maxPathDepth = networkInfo.Elements.Count + 5;

            var paths = new List<VentPathInfo>();
            if (endpointSelection.SystemDirection == "Exhaust" && endpointSelection.ConnectorStarts.Count > 0)
            {
                BuildConnectorLevelPaths(endpointSelection, nodesById, adjacency, maxPathDepth, paths);
            }
            else
            {
                foreach (long startElementId in endpointSelection.StartElementIds)
                {
                    foreach (long endElementId in endpointSelection.EndElementIds)
                    {
                        if (startElementId == endElementId)
                        {
                            continue;
                        }

                        List<long>? shortestPath = FindShortestPath(adjacency, startElementId, endElementId, maxPathDepth, new HashSet<long>());
                        if (shortestPath != null)
                        {
                            paths.Add(CreatePath(shortestPath, nodesById, endpointSelection.SystemDirection));
                        }
                    }
                }
            }

            paths = DeduplicateConnectorPaths(paths, endpointSelection);

            paths = paths
                .OrderByDescending(path => path.TotalDuctLengthMm)
                .ThenByDescending(path => path.TotalElementCount)
                .ThenBy(path => path.StartElementId)
                .ThenBy(path => path.EndElementId)
                .Select((path, index) => ReindexPath(path, index + 1))
                .ToList();

            UpdateConnectorStartResults(endpointSelection, paths);

            return new VentPathSummary(
                DetectSystemType(networkInfo),
                FormatDirection(endpointSelection.SystemDirection),
                endpointSelection.DirectionReason,
                endpointSelection.SystemDirection == "Unknown",
                BuildCandidateDetails(endpointSelection.StartElementIds, nodesById),
                BuildCandidateDetails(endpointSelection.EndElementIds, nodesById),
                BuildCandidateDetails(endpointSelection.IgnoredCapElementIds, nodesById),
                endpointSelection.Warnings,
                endpointSelection.StartElementIds.Select(id => id.ToString(CultureInfo.InvariantCulture)),
                endpointSelection.EndElementIds.Select(id => id.ToString(CultureInfo.InvariantCulture)),
                endpointSelection,
                paths,
                paths.Count == 0 ? BuildNoPathReason(endpointSelection) : string.Empty);
        }

        private static Dictionary<long, VentNetworkNode> BuildNodeMap(VentNetworkInfo networkInfo)
        {
            return networkInfo.Elements
                .Select(node => new { Node = node, Id = ParseElementId(node.ElementId) })
                .Where(item => item.Id.HasValue)
                .GroupBy(item => item.Id.Value)
                .ToDictionary(group => group.Key, group => group.First().Node);
        }

        private static Dictionary<long, HashSet<long>> BuildAdjacency(
            VentNetworkInfo networkInfo,
            IReadOnlyDictionary<long, VentNetworkNode> nodesById)
        {
            var adjacency = nodesById.Keys.ToDictionary(id => id, _ => new HashSet<long>());
            var connectionKeys = new HashSet<string>();

            foreach (VentNetworkConnection connection in networkInfo.Connections)
            {
                long? fromElementId = ParseElementId(connection.FromElementId);
                long? toElementId = ParseElementId(connection.ToElementId);
                if (!fromElementId.HasValue || !toElementId.HasValue || fromElementId.Value == toElementId.Value)
                {
                    continue;
                }

                if (!nodesById.ContainsKey(fromElementId.Value) || !nodesById.ContainsKey(toElementId.Value))
                {
                    continue;
                }

                long min = Math.Min(fromElementId.Value, toElementId.Value);
                long max = Math.Max(fromElementId.Value, toElementId.Value);
                if (!connectionKeys.Add($"{min}->{max}"))
                {
                    continue;
                }

                adjacency[fromElementId.Value].Add(toElementId.Value);
                adjacency[toElementId.Value].Add(fromElementId.Value);
            }

            return adjacency;
        }

        private static void BuildConnectorLevelPaths(
            VentPathEndpointSelection endpointSelection,
            IReadOnlyDictionary<long, VentNetworkNode> nodesById,
            IReadOnlyDictionary<long, HashSet<long>> adjacency,
            int maxPathDepth,
            ICollection<VentPathInfo> paths)
        {
            HashSet<long> terminalBoundaryIds = nodesById
                .Where(item => item.Value.Role == VentNodeRole.HoodCandidate || item.Value.Role == VentNodeRole.TerminalCandidate)
                .Select(item => item.Key)
                .ToHashSet();
            var pathKeys = new HashSet<string>();

            foreach (VentConnectorEndpointStartInfo start in endpointSelection.ConnectorStarts)
            {
                if (!nodesById.ContainsKey(start.ElementId) || !nodesById.ContainsKey(start.ConnectedElementId))
                {
                    start.RejectionReason = "Стартовый элемент или соседний элемент коннектора отсутствует в графе сети.";
                    continue;
                }

                bool foundPathForStart = false;
                foreach (long endElementId in endpointSelection.EndElementIds)
                {
                    if (start.ConnectedElementId == endElementId)
                    {
                        var directPath = new List<long> { start.ElementId, endElementId };
                        AddConnectorPath(paths, pathKeys, directPath, nodesById, endpointSelection.SystemDirection, start);
                        foundPathForStart = true;
                        continue;
                    }

                    HashSet<long> forbiddenTransit = terminalBoundaryIds.Where(id => id != endElementId).ToHashSet();
                    List<long>? shortestPath = FindShortestPath(adjacency, start.ConnectedElementId, endElementId, maxPathDepth, forbiddenTransit);
                    if (shortestPath == null)
                    {
                        continue;
                    }

                    var fullPath = new List<long> { start.ElementId };
                    fullPath.AddRange(shortestPath);
                    AddConnectorPath(paths, pathKeys, fullPath, nodesById, endpointSelection.SystemDirection, start);
                    foundPathForStart = true;
                }

                if (!foundPathForStart && string.IsNullOrWhiteSpace(start.RejectionReason))
                {
                    start.RejectionReason = "Для connector-level старта не найден путь до конечной стороны.";
                }
            }
        }

        private static void AddConnectorPath(
            ICollection<VentPathInfo> paths,
            ISet<string> pathKeys,
            IReadOnlyList<long> elementIds,
            IReadOnlyDictionary<long, VentNetworkNode> nodesById,
            string systemDirection,
            VentConnectorEndpointStartInfo start)
        {
            string key = $"{start.LogicalKey}|{string.Join(">", elementIds)}";
            if (!pathKeys.Add(key))
            {
                return;
            }

            paths.Add(CreatePath(
                elementIds,
                nodesById,
                systemDirection,
                start.ConnectorKey,
                start.ConnectedElementId.ToString(CultureInfo.InvariantCulture),
                FormatFlow(start.FlowM3h)));
        }

        private static List<VentPathInfo> DeduplicateConnectorPaths(IReadOnlyList<VentPathInfo> paths, VentPathEndpointSelection endpointSelection)
        {
            var result = new List<VentPathInfo>();
            foreach (IGrouping<string, VentPathInfo> group in paths.GroupBy(BuildDuplicatePathKey))
            {
                VentPathInfo selected = group
                    .OrderBy(path => string.Equals(path.StartConnectorKey, "C0", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                    .ThenBy(path => path.StartConnectorKey, StringComparer.Ordinal)
                    .First();
                result.Add(selected);
            }

            endpointSelection.DuplicatePathsRemoved = Math.Max(0, paths.Count - result.Count);
            return result;
        }

        private static string BuildDuplicatePathKey(VentPathInfo path)
        {
            return string.Join("|",
                path.StartElementId,
                path.ConnectedStartElementId,
                path.EndElementId,
                string.Join(">", path.ElementIds));
        }

        private static void UpdateConnectorStartResults(VentPathEndpointSelection endpointSelection, IReadOnlyList<VentPathInfo> paths)
        {
            foreach (VentConnectorEndpointStartInfo start in endpointSelection.ConnectorStarts)
            {
                VentPathInfo? path = paths.FirstOrDefault(item => item.StartElementId == start.ElementId.ToString(CultureInfo.InvariantCulture)
                    && item.StartConnectorKey == start.ConnectorKey
                    && item.ConnectedStartElementId == start.ConnectedElementId.ToString(CultureInfo.InvariantCulture));
                start.PathFound = path != null;
                start.PathIndex = path?.PathIndex;
                if (path != null)
                {
                    start.RejectionReason = string.Empty;
                }
            }

            endpointSelection.PathsBuiltCount = paths.Count;
            endpointSelection.ConnectorStartsWithoutPathCount = endpointSelection.ConnectorStarts.Count(start => !start.PathFound);
        }

        private static List<long>? FindShortestPath(
            IReadOnlyDictionary<long, HashSet<long>> adjacency,
            long startId,
            long endId,
            int maxPathDepth,
            ISet<long> forbiddenTransitElementIds)
        {
            var queue = new Queue<List<long>>();
            var visitedBestDepth = new Dictionary<long, int>();

            queue.Enqueue(new List<long> { startId });
            visitedBestDepth[startId] = 1;

            while (queue.Count > 0)
            {
                List<long> path = queue.Dequeue();
                long current = path[path.Count - 1];

                if (current == endId)
                {
                    return path;
                }

                if (path.Count >= maxPathDepth || !adjacency.TryGetValue(current, out HashSet<long>? neighbors))
                {
                    continue;
                }

                foreach (long next in neighbors.OrderBy(id => id))
                {
                    if (path.Contains(next) || (next != endId && forbiddenTransitElementIds.Contains(next)))
                    {
                        continue;
                    }

                    int nextDepth = path.Count + 1;
                    if (visitedBestDepth.TryGetValue(next, out int bestDepth) && bestDepth <= nextDepth)
                    {
                        continue;
                    }

                    visitedBestDepth[next] = nextDepth;
                    var newPath = new List<long>(path) { next };
                    queue.Enqueue(newPath);
                }
            }

            return null;
        }

        private static VentPathInfo CreatePath(
            IReadOnlyList<long> elementIds,
            IReadOnlyDictionary<long, VentNetworkNode> nodesById,
            string pathKind,
            string startConnectorKey = "",
            string connectedStartElementId = "",
            string startFlowM3h = "")
        {
            var nodes = elementIds.Select(elementId => nodesById[elementId]).ToList();
            var pathNodes = nodes
                .Select(node => new VentPathNode(
                    node.ElementId,
                    node.CategoryName,
                    node.CategoryKey,
                    node.TypeName,
                    node.FamilyName,
                    node.Size,
                    node.FlowM3h,
                    node.DuctLengthMm))
                .ToList();

            return new VentPathInfo(
                0,
                elementIds.First().ToString(CultureInfo.InvariantCulture),
                elementIds.Last().ToString(CultureInfo.InvariantCulture),
                elementIds.Select(id => id.ToString(CultureInfo.InvariantCulture)),
                pathNodes,
                nodes.Count(node => IsCategory(node, "OST_DuctCurves")),
                nodes.Count(node => IsCategory(node, "OST_DuctFitting")),
                nodes.Count(node => IsCategory(node, "OST_DuctAccessory")),
                nodes.Count(node => IsCategory(node, "OST_DuctTerminal")),
                nodes.Count(node => IsCategory(node, "OST_MechanicalEquipment")),
                elementIds.Count,
                nodes.Where(node => IsCategory(node, "OST_DuctCurves")).Sum(node => node.DuctLengthMm),
                FormatFlow(nodes.Max(node => ParseNumber(node.FlowM3h))),
                pathKind,
                startConnectorKey,
                connectedStartElementId,
                string.IsNullOrWhiteSpace(startFlowM3h) || startFlowM3h == "—" ? FormatStartFlow(nodes) : startFlowM3h,
                FormatEndFlow(nodes));
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
                path.AccessoryCount,
                path.TerminalCount,
                path.EquipmentCount,
                path.TotalElementCount,
                path.TotalDuctLengthMm,
                path.MaxFlowM3h,
                path.PathKind,
                path.StartConnectorKey,
                path.ConnectedStartElementId,
                path.StartFlowM3h,
                path.EndFlowM3h);
        }

        private static IEnumerable<string> BuildCandidateDetails(
            IEnumerable<long> elementIds,
            IReadOnlyDictionary<long, VentNetworkNode> nodesById)
        {
            foreach (long elementId in elementIds.OrderBy(id => id))
            {
                if (!nodesById.TryGetValue(elementId, out VentNetworkNode? node))
                {
                    continue;
                }

                yield return $"{elementId} | Role={node.Role} | {node.PathRoleReason} | Family={node.FamilyName} | Type={node.TypeName}";
            }
        }

        private static bool IsCategory(VentNetworkNode node, string categoryKey)
        {
            return string.Equals(node.CategoryKey, categoryKey, StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatStartFlow(IReadOnlyList<VentNetworkNode> nodes)
        {
            VentNetworkNode? firstDuct = nodes.FirstOrDefault(node => IsCategory(node, "OST_DuctCurves"));
            return firstDuct == null ? "—" : FormatFlow(ParseNumber(firstDuct.FlowM3h));
        }

        private static string FormatEndFlow(IReadOnlyList<VentNetworkNode> nodes)
        {
            VentNetworkNode? lastDuct = nodes.LastOrDefault(node => IsCategory(node, "OST_DuctCurves"));
            return lastDuct == null ? "—" : FormatFlow(ParseNumber(lastDuct.FlowM3h));
        }

        private static string DetectSystemType(VentNetworkInfo networkInfo)
        {
            return networkInfo.Elements
                .Select(node => node.SystemType)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value) && value != "—")
                ?? "—";
        }

        private static string FormatDirection(string systemDirection)
        {
            return systemDirection switch
            {
                "Supply" => "Supply / Приточная система",
                "Exhaust" => "Exhaust / Вытяжная система",
                _ => "Unknown / Направление определено приблизительно"
            };
        }

        private static string BuildNoPathReason(VentPathEndpointSelection selection)
        {
            if (selection.ConnectorStarts.Count > 0 && selection.ConnectorStartsWithoutPathCount == selection.ConnectorStarts.Count)
            {
                return $"Найдено {selection.ConnectorStarts.Count} connector-level стартов, но ни один не имеет пути до конечной стороны.";
            }

            if (selection.StartElementIds.Count == 0 && selection.EndElementIds.Count == 0)
            {
                return "Стартовые и конечные точки не найдены. Проверьте тип системы, терминалы, оборудование и открытые коннекторы.";
            }

            if (selection.StartElementIds.Count == 0)
            {
                if (selection.HoodCandidates.Count > 0 || selection.OpenEndCandidates.Count > 0)
                {
                    return $"Найдено {selection.HoodCandidates.Count} зонтов и {selection.OpenEndCandidates.Count} открытых концов, но не сформирована стартовая сторона трассировки.";
                }

                return "Стартовые точки не найдены. Заглушки не используются как старты; для притока нужен элемент оборудования или открытый магистральный конец.";
            }

            if (selection.EndElementIds.Count == 0)
            {
                return "Конечные точки не найдены. Заглушки не используются как концы; для притока нужны терминалы/решётки, а для вытяжки — вентилятор или открытый магистральный конец.";
            }

            return "Между найденными стартовыми и конечными точками нет трасс по графу сети.";
        }

        private static long? ParseElementId(string elementId)
        {
            return long.TryParse(elementId, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
                ? parsed
                : null;
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
    }
}
