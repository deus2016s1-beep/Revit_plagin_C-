using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VentCalc.Core.Models;

namespace VentCalc.Core.Services
{
    public sealed class FittingPathRoleDetector
    {
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
                result.Add(DetectRole(path.PathIndex, data, previous, next));
            }

            return result;
        }

        private static FittingPathRoleInfo DetectRole(int pathIndex, LocalResistanceElementData data, DuctCalculationInfo? previous, DuctCalculationInfo? next)
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
                role.FittingKind = "Cap";
                role.PathRole = "Cap";
                role.Reason = "Элемент распознан как заглушка; ζ=0.";
                return role;
            }

            if (ContainsAny(text, "Реш", "Grille", "Diffuser")) return Set(role, "Terminal", "Grille", "Решётка/диффузор по имени или категории.");
            if (ContainsAny(text, "Зонт", "Hood")) return Set(role, "Terminal", "Hood", "Зонт по имени или семейству.");
            if (ContainsAny(text, "Противопожар", "Fire")) return Set(role, "Damper", "FireDamper", "Противопожарный клапан.");
            if (ContainsAny(text, "Обрат", "Backdraft", "Check")) return Set(role, "Damper", "BackdraftDamper", "Обратный клапан.");
            if (ContainsAny(text, "Дроссель", "Damper")) return Set(role, "Damper", "Damper", "Клапан/дроссель.");
            if (ContainsAny(text, "Врезка", "Tap")) return Set(role, "Tap", "TapBranch", "Врезка считается ответвлением первого этапа.");
            if (ContainsAny(text, "Крестовина", "Cross")) return Set(role, "Cross", ResolveCrossRole(previous, next, role), "Крестовина определена эвристикой по соседним воздуховодам.");
            if (ContainsAny(text, "Тройник", "Tee")) return Set(role, "Tee", ResolveTeeRole(previous, next, role), "Тройник определён эвристикой по площадям/расходам соседних воздуховодов.");
            if (ContainsAny(text, "Переход", "Transition")) return Set(role, "Transition", ResolveTransitionRole(previous, next, role), "Переход определён сравнением площадей соседних воздуховодов.");
            if (ContainsAny(text, "Отвод", "Bend", "Elbow")) return Set(role, "Elbow", ResolveElbowRole(text, role), "Угол отвода определён по имени/типу элемента.");

            role.FittingKind = "Unknown";
            role.PathRole = "Unknown";
            role.Reason = "Не удалось определить роль фитинга в трассе по данным элемента.";
            role.Warnings.Add("Местное сопротивление не рассчитано: роль фитинга в трассе не определена.");
            return role;
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

        private static string ResolveTeeRole(DuctCalculationInfo? previous, DuctCalculationInfo? next, FittingPathRoleInfo role)
        {
            if (previous == null || next == null)
            {
                role.Warnings.Add("Не удалось уверенно определить проход/ответвление тройника: нет соседних воздуховодов.");
                return "TeeUnknown";
            }

            double maxArea = Math.Max(previous.AreaM2, next.AreaM2);
            double minArea = Math.Min(previous.AreaM2, next.AreaM2);
            double maxFlow = Math.Max(previous.FlowM3h, next.FlowM3h);
            double minFlow = Math.Min(previous.FlowM3h, next.FlowM3h);
            if (maxArea > 0 && minArea > 0 && minArea / maxArea < 0.75)
            {
                return "TeeBranch";
            }

            if (maxFlow > 0 && minFlow > 0 && minFlow / maxFlow < 0.75)
            {
                return "TeeBranch";
            }

            if (maxArea > 0 || maxFlow > 0)
            {
                return "TeePass";
            }

            role.Warnings.Add("Не удалось уверенно определить проход/ответвление тройника.");
            return "TeeUnknown";
        }

        private static string ResolveCrossRole(DuctCalculationInfo? previous, DuctCalculationInfo? next, FittingPathRoleInfo role)
        {
            string teeRole = ResolveTeeRole(previous, next, role);
            return teeRole == "TeePass" ? "CrossPass" : teeRole == "TeeBranch" ? "CrossBranch" : "CrossUnknown";
        }

        private static string ResolveElbowRole(string text, FittingPathRoleInfo role)
        {
            if (ContainsAny(text, "15")) return "Elbow15";
            if (ContainsAny(text, "30")) return "Elbow30";
            if (ContainsAny(text, "45")) return "Elbow45";
            if (ContainsAny(text, "60")) return "Elbow60";
            if (ContainsAny(text, "90")) return "Elbow90";
            role.Warnings.Add("Угол отвода не найден по имени/типу; роль отвода требует проверки.");
            return "Unknown";
        }

        private static FittingPathRoleInfo Set(FittingPathRoleInfo role, string kind, string pathRole, string reason)
        {
            role.FittingKind = kind;
            role.PathRole = pathRole;
            role.Reason = reason;
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
