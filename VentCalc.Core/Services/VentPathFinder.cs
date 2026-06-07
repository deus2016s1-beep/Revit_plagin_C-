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
            foreach (long startElementId in endpointSelection.StartElementIds)
            {
                foreach (long endElementId in endpointSelection.EndElementIds)
                {
                    if (startElementId == endElementId)
                    {
                        continue;
                    }

                    List<long>? shortestPath = FindShortestPath(adjacency, startElementId, endElementId, maxPathDepth);
                    if (shortestPath != null)
                    {
                        paths.Add(CreatePath(shortestPath, nodesById, endpointSelection.SystemDirection));
                    }
                }
            }

            paths = paths
                .OrderByDescending(path => path.TotalDuctLengthMm)
                .ThenByDescending(path => path.TotalElementCount)
                .ThenBy(path => path.StartElementId)
                .ThenBy(path => path.EndElementId)
                .Select((path, index) => ReindexPath(path, index + 1))
                .ToList();

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

        private static List<long>? FindShortestPath(
            IReadOnlyDictionary<long, HashSet<long>> adjacency,
            long startId,
            long endId,
            int maxPathDepth)
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
                    if (path.Contains(next))
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
            string pathKind)
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
                path.AccessoryCount,
                path.TerminalCount,
                path.EquipmentCount,
                path.TotalElementCount,
                path.TotalDuctLengthMm,
                path.MaxFlowM3h,
                path.PathKind);
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
