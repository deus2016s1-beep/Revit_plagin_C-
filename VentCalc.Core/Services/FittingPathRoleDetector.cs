using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using VentCalc.Core.Models;

namespace VentCalc.Core.Services
{
    public sealed class FittingPathRoleDetector
    {
        private static readonly (double Angle, string Role)[] ElbowAngleRoles =
        {
            (15.0, "Elbow15"),
            (30.0, "Elbow30"),
            (45.0, "Elbow45"),
            (60.0, "Elbow60"),
            (90.0, "Elbow90")
        };

        public IReadOnlyList<FittingPathRoleInfo> Detect(
            VentPathInfo path,
            IReadOnlyDictionary<long, LocalResistanceElementData> localDataByElementId,
            IReadOnlyDictionary<long, DuctCalculationInfo> ductCalculationsByElementId)
        {
            var result = new List<FittingPathRoleInfo>();
            for (int index = 0; index < path.ElementIds.Count; index++)
            {
                if (!TryParseElementId(path.ElementIds[index], out long elementId) || IsDuct(path.Nodes[index]))
                {
                    continue;
                }

                LocalResistanceElementData data = localDataByElementId.TryGetValue(elementId, out LocalResistanceElementData? found)
                    ? found
                    : new LocalResistanceElementData
                    {
                        ElementId = elementId,
                        CategoryKey = path.Nodes[index].CategoryKey,
                        CategoryName = path.Nodes[index].CategoryName,
                        FamilyName = path.Nodes[index].FamilyName,
                        TypeName = path.Nodes[index].TypeName,
                        Name = path.Nodes[index].TypeName,
                        Size = path.Nodes[index].Size
                    };

                DuctCalculationInfo? previous = FindPreviousDuct(path, index, ductCalculationsByElementId);
                DuctCalculationInfo? next = FindNextDuct(path, index, ductCalculationsByElementId);
                result.Add(DetectRole(path.PathIndex, data, previous, next, ductCalculationsByElementId));
            }

            return result;
        }

        private static FittingPathRoleInfo DetectRole(
            int pathIndex,
            LocalResistanceElementData data,
            DuctCalculationInfo? previous,
            DuctCalculationInfo? next,
            IReadOnlyDictionary<long, DuctCalculationInfo> ductsByElementId)
        {
            string text = string.Join(" ", data.Name, data.TypeName, data.FamilyName, data.CategoryName);
            var role = new FittingPathRoleInfo
            {
                ElementId = data.ElementId,
                PathIndex = pathIndex,
                PreviousDuctElementId = previous?.ElementId,
                NextDuctElementId = next?.ElementId,
                PreviousAreaM2 = previous?.AreaM2 ?? 0,
                NextAreaM2 = next?.AreaM2 ?? 0,
                PreviousFlowM3h = previous?.FlowM3h ?? 0,
                NextFlowM3h = next?.FlowM3h ?? 0
            };

            if (ContainsAny(text, "Заглушка", "Cap"))
            {
                return Set(role, "Cap", "Cap", "Элемент распознан как заглушка; ζ=0.");
            }

            if (ContainsAny(text, "Реш", "Grille", "Diffuser")) return Set(role, "Terminal", "Grille", "Решётка/диффузор по имени или категории.");
            if (ContainsAny(text, "Зонт", "Hood", "Canopy", "местный отсос")) return Set(role, "Terminal", "Hood", "Зонт по имени или семейству.");
            if (IsTerminalLikeMechanicalEquipment(data, previous, next)) return Set(role, "Terminal", "Hood", "Листовое оборудование с одним соседним воздуховодом рассматривается как зонт/местный отсос.");
            if (ContainsAny(text, "Противопожар", "Fire")) return Set(role, "Damper", "FireDamper", "Противопожарный клапан.");
            if (ContainsAny(text, "Обрат", "Backdraft", "Check")) return Set(role, "Damper", "BackdraftDamper", "Обратный клапан.");
            if (ContainsAny(text, "Дроссель", "Damper")) return Set(role, "Damper", "Damper", "Клапан/дроссель.");
            if (ContainsAny(text, "Врезка", "Tap")) return Set(role, "Tap", "TapBranch", "Врезка считается ответвлением первого этапа.");
            if (ContainsAny(text, "Крестовина", "Cross")) return Set(role, "Cross", ResolveCrossRole(data, previous, next, ductsByElementId, role), role.Reason);
            if (ContainsAny(text, "Тройник", "Tee")) return Set(role, "Tee", ResolveTeeRole(data, previous, next, ductsByElementId, role), role.Reason);
            if (ContainsAny(text, "Переход", "Transition")) return Set(role, "Transition", ResolveTransitionRole(previous, next, role), "Переход определён сравнением площадей соседних воздуховодов.");
            if (ContainsAny(text, "Отвод", "Bend", "Elbow")) return Set(role, "Elbow", ResolveElbowRole(text, data, previous, next, role), role.Reason);

            role.FittingKind = "Unknown";
            role.PathRole = "Unknown";
            role.Reason = "Не удалось определить роль фитинга в трассе по данным элемента.";
            role.Warnings.Add("Местное сопротивление не рассчитано: роль фитинга в трассе не определена.");
            return role;
        }

        private static bool IsTerminalLikeMechanicalEquipment(LocalResistanceElementData data, DuctCalculationInfo? previous, DuctCalculationInfo? next)
        {
            bool isMechanicalEquipment = string.Equals(data.CategoryKey, "OST_MechanicalEquipment", StringComparison.OrdinalIgnoreCase)
                || data.CategoryName.IndexOf("Оборуд", StringComparison.OrdinalIgnoreCase) >= 0
                || data.CategoryName.IndexOf("Mechanical Equipment", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!isMechanicalEquipment)
            {
                return false;
            }

            string text = string.Join(" ", data.Name, data.TypeName, data.FamilyName, data.CategoryName);
            if (ContainsAny(text, "вентилятор", "fan", "ahu", "вентустановка", "установка", "калорифер", "filter", "фильтр"))
            {
                return false;
            }

            return previous == null ^ next == null;
        }

        private static string ResolveTransitionRole(DuctCalculationInfo? previous, DuctCalculationInfo? next, FittingPathRoleInfo role)
        {
            if (previous == null || next == null || previous.AreaM2 <= 0 || next.AreaM2 <= 0)
            {
                role.Warnings.Add("Не удалось определить сужение/расширение перехода: нет соседних воздуховодов или площадей.");
                return "TransitionUnknown";
            }

            const double tolerance = 0.000001;
            if (previous.AreaM2 - next.AreaM2 > tolerance) return "TransitionNarrowing";
            if (next.AreaM2 - previous.AreaM2 > tolerance) return "TransitionExpansion";
            role.Warnings.Add("Площади соседних воздуховодов перехода совпадают; роль перехода требует проверки.");
            return "TransitionUnknown";
        }

        private static string ResolveTeeRole(
            LocalResistanceElementData data,
            DuctCalculationInfo? previous,
            DuctCalculationInfo? next,
            IReadOnlyDictionary<long, DuctCalculationInfo> ductsByElementId,
            FittingPathRoleInfo role)
        {
            if (previous == null || next == null)
            {
                role.Reason = "Тройник: нет previous/next воздуховодов по текущей трассе.";
                role.Warnings.Add("Не удалось уверенно определить проход/ответвление тройника: нет соседних воздуховодов.");
                return "TeeUnknown";
            }

            List<long> connectedDuctIds = data.ConnectedDucts
                .Select(item => item.ElementId)
                .Where(id => ductsByElementId.ContainsKey(id))
                .Distinct()
                .ToList();
            if (!connectedDuctIds.Contains(previous.ElementId)) connectedDuctIds.Add(previous.ElementId);
            if (!connectedDuctIds.Contains(next.ElementId)) connectedDuctIds.Add(next.ElementId);

            IReadOnlySet<long>? mainByDirection = TryFindMainLineByDirection(data);
            if (mainByDirection != null)
            {
                role.Reason = "Главная линия тройника определена по наиболее соосным/противоположным направлениям коннекторов.";
                return ResolvePassOrBranch(previous.ElementId, next.ElementId, mainByDirection);
            }

            IReadOnlySet<long>? mainByCapacity = TryFindMainLineByCapacity(connectedDuctIds, ductsByElementId);
            if (mainByCapacity != null)
            {
                role.Reason = "Главная линия тройника определена по двум самым большим расходам/площадям подключенных воздуховодов.";
                return ResolvePassOrBranch(previous.ElementId, next.ElementId, mainByCapacity);
            }

            if (TryResolveTeeByCurrentPath(previous, next, role, out string pathRole))
            {
                return pathRole;
            }

            role.Reason = "Тройник: недостаточно данных для определения главной линии.";
            role.Warnings.Add("Не удалось уверенно определить проход/ответвление тройника.");
            return "TeeUnknown";
        }

        private static string ResolveCrossRole(
            LocalResistanceElementData data,
            DuctCalculationInfo? previous,
            DuctCalculationInfo? next,
            IReadOnlyDictionary<long, DuctCalculationInfo> ductsByElementId,
            FittingPathRoleInfo role)
        {
            string teeRole = ResolveTeeRole(data, previous, next, ductsByElementId, role);
            return teeRole == "TeePass" ? "CrossPass" : teeRole == "TeeBranch" ? "CrossBranch" : "CrossUnknown";
        }

        private static string ResolveElbowRole(
            string text,
            LocalResistanceElementData data,
            DuctCalculationInfo? previous,
            DuctCalculationInfo? next,
            FittingPathRoleInfo role)
        {
            if (TryResolveAngleFromText(text, out string roleFromText))
            {
                role.Reason = "Угол отвода определён по имени/типу элемента.";
                return roleFromText;
            }

            double? angle = TryCalculateConnectorAngle(data, previous?.ElementId, next?.ElementId);
            if (angle.HasValue && TryClassifyElbowAngle(angle.Value, out string roleFromConnectors, out double roundedAngle, out string roundingWarning))
            {
                role.ActualAngleDeg = angle.Value;
                role.RoundedAngleDeg = roundedAngle;
                role.AngleWasRounded = Math.Abs(angle.Value - roundedAngle) > 0.1;
                role.AngleRoundingWarning = roundingWarning;
                role.Reason = role.AngleWasRounded
                    ? $"Фактический угол {angle.Value:0.#}° округлён до расчётного угла {roundedAngle:0.#}°."
                    : $"Угол отвода определён по направлениям коннекторов: {angle.Value:0.#}°.";
                if (!string.IsNullOrWhiteSpace(roundingWarning))
                {
                    role.Warnings.Add(roundingWarning);
                }
                return roleFromConnectors;
            }

            role.Reason = "Угол отвода не найден по имени/типу и не определён по коннекторам.";
            role.Warnings.Add("Угол отвода не найден по имени/типу; роль отвода требует проверки.");
            return "Unknown";
        }

        private static string ResolvePassOrBranch(long previousElementId, long nextElementId, IReadOnlySet<long> mainLine)
        {
            return mainLine.Contains(previousElementId) && mainLine.Contains(nextElementId)
                ? "TeePass"
                : "TeeBranch";
        }

        private static bool TryResolveTeeByCurrentPath(DuctCalculationInfo previous, DuctCalculationInfo next, FittingPathRoleInfo role, out string pathRole)
        {
            double previousMetric = Math.Max(previous.FlowM3h, 0) > 0 ? previous.FlowM3h : previous.AreaM2;
            double nextMetric = Math.Max(next.FlowM3h, 0) > 0 ? next.FlowM3h : next.AreaM2;
            double larger = Math.Max(previousMetric, nextMetric);
            double smaller = Math.Min(previousMetric, nextMetric);

            if (larger <= 0)
            {
                pathRole = string.Empty;
                return false;
            }

            if (smaller / larger < 0.75)
            {
                role.Reason = "Тройник определён как ответвление по разнице расходов/площадей соседних воздуховодов текущей трассы.";
                pathRole = "TeeBranch";
                return true;
            }

            role.Reason = "Тройник определён как проход по близким расходам/площадям соседних воздуховодов текущей трассы.";
            pathRole = "TeePass";
            return true;
        }

        private static IReadOnlySet<long>? TryFindMainLineByDirection(LocalResistanceElementData data)
        {
            var directed = data.ConnectedDucts.Where(item => item.HasDirection).ToList();
            if (directed.Count < 3)
            {
                return null;
            }

            double bestAxisScore = double.MinValue;
            (long First, long Second) bestPair = (0, 0);
            for (int firstIndex = 0; firstIndex < directed.Count; firstIndex++)
            {
                for (int secondIndex = firstIndex + 1; secondIndex < directed.Count; secondIndex++)
                {
                    double axisScore = Math.Abs(Dot(directed[firstIndex], directed[secondIndex]));
                    if (axisScore > bestAxisScore)
                    {
                        bestAxisScore = axisScore;
                        bestPair = (directed[firstIndex].ElementId, directed[secondIndex].ElementId);
                    }
                }
            }

            return bestPair.First == 0 || bestAxisScore < 0.85
                ? null
                : new HashSet<long> { bestPair.First, bestPair.Second };
        }

        private static IReadOnlySet<long>? TryFindMainLineByCapacity(IReadOnlyList<long> connectedDuctIds, IReadOnlyDictionary<long, DuctCalculationInfo> ductsByElementId)
        {
            List<DuctCalculationInfo> connected = connectedDuctIds
                .Where(ductsByElementId.ContainsKey)
                .Select(id => ductsByElementId[id])
                .OrderByDescending(duct => Math.Max(duct.FlowM3h, 0))
                .ThenByDescending(duct => Math.Max(duct.AreaM2, 0))
                .ToList();
            if (connected.Count < 3)
            {
                return null;
            }

            return new HashSet<long> { connected[0].ElementId, connected[1].ElementId };
        }

        private static double? TryCalculateConnectorAngle(LocalResistanceElementData data, long? previousElementId, long? nextElementId)
        {
            FittingConnectedDuctInfo? previous = null;
            FittingConnectedDuctInfo? next = null;

            if (previousElementId.HasValue && nextElementId.HasValue)
            {
                previous = data.ConnectedDucts.FirstOrDefault(item => item.ElementId == previousElementId.Value && item.HasDirection);
                next = data.ConnectedDucts.FirstOrDefault(item => item.ElementId == nextElementId.Value && item.HasDirection);
            }

            if ((previous == null || next == null) && data.ConnectedDucts.Count(item => item.HasDirection) == 2)
            {
                var directed = data.ConnectedDucts.Where(item => item.HasDirection).ToList();
                previous = directed[0];
                next = directed[1];
            }

            if (previous == null || next == null)
            {
                return null;
            }

            double dot = Math.Clamp(Dot(previous, next), -1.0, 1.0);
            return Math.Acos(dot) * 180.0 / Math.PI;
        }

        private static bool TryResolveAngleFromText(string text, out string pathRole)
        {
            foreach ((double angle, string role) in ElbowAngleRoles)
            {
                string pattern = $@"(^|[^0-9]){angle.ToString("0", CultureInfo.InvariantCulture)}([^0-9]|$)";
                if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                {
                    pathRole = role;
                    return true;
                }
            }

            pathRole = string.Empty;
            return false;
        }

        private static bool TryClassifyElbowAngle(double angle, out string pathRole, out double roundedAngle, out string warning)
        {
            warning = string.Empty;
            if (double.IsNaN(angle) || double.IsInfinity(angle))
            {
                pathRole = string.Empty;
                roundedAngle = 0;
                return false;
            }

            if (angle < 7.5)
            {
                roundedAngle = 15.0;
                pathRole = "Elbow15";
                warning = "Малый угол округлён до 15°.";
                return true;
            }

            if (angle > 90.0)
            {
                roundedAngle = 90.0;
                pathRole = "Elbow90";
                warning = "Угол больше 90° ограничен расчётным значением 90°. Требуется проверка.";
                return true;
            }

            (double Angle, string Role) best = ElbowAngleRoles
                .OrderBy(candidate => Math.Abs(angle - candidate.Angle))
                .ThenByDescending(candidate => candidate.Angle)
                .First();
            roundedAngle = best.Angle;
            pathRole = best.Role;
            return true;
        }

        private static double Dot(FittingConnectedDuctInfo first, FittingConnectedDuctInfo second)
        {
            double firstLength = Math.Sqrt(first.DirectionX * first.DirectionX + first.DirectionY * first.DirectionY + first.DirectionZ * first.DirectionZ);
            double secondLength = Math.Sqrt(second.DirectionX * second.DirectionX + second.DirectionY * second.DirectionY + second.DirectionZ * second.DirectionZ);
            if (firstLength <= 0 || secondLength <= 0)
            {
                return 0;
            }

            return (first.DirectionX * second.DirectionX + first.DirectionY * second.DirectionY + first.DirectionZ * second.DirectionZ) / (firstLength * secondLength);
        }

        private static FittingPathRoleInfo Set(FittingPathRoleInfo role, string kind, string pathRole, string reason)
        {
            role.FittingKind = kind;
            role.PathRole = pathRole;
            if (string.IsNullOrWhiteSpace(role.Reason))
            {
                role.Reason = reason;
            }

            return role;
        }

        private static DuctCalculationInfo? FindPreviousDuct(VentPathInfo path, int localIndex, IReadOnlyDictionary<long, DuctCalculationInfo> ducts)
        {
            for (int index = localIndex - 1; index >= 0; index--)
            {
                if (TryParseElementId(path.ElementIds[index], out long elementId) && ducts.TryGetValue(elementId, out DuctCalculationInfo? duct))
                {
                    return duct;
                }
            }

            return null;
        }

        private static DuctCalculationInfo? FindNextDuct(VentPathInfo path, int localIndex, IReadOnlyDictionary<long, DuctCalculationInfo> ducts)
        {
            for (int index = localIndex + 1; index < path.ElementIds.Count; index++)
            {
                if (TryParseElementId(path.ElementIds[index], out long elementId) && ducts.TryGetValue(elementId, out DuctCalculationInfo? duct))
                {
                    return duct;
                }
            }

            return null;
        }

        private static bool IsDuct(VentPathNode node)
        {
            return string.Equals(node.CategoryKey, "OST_DuctCurves", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParseElementId(string value, out long elementId)
        {
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out elementId);
        }

        private static bool ContainsAny(string text, params string[] patterns)
        {
            return patterns.Any(pattern => text.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
