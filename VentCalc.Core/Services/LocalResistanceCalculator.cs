using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using VentCalc.Core.Models;

namespace VentCalc.Core.Services
{
    public sealed class LocalResistanceCalculator
    {
        public IReadOnlyList<LocalResistanceCalculationInfo> CalculatePathLocalResistances(
            VentPathInfo path,
            IReadOnlyDictionary<long, LocalResistanceElementData> localDataByElementId,
            IReadOnlyDictionary<long, DuctCalculationInfo> ductCalculationsByElementId,
            IReadOnlyDictionary<long, DuctCalculationInfo>? networkDuctCalculationsByElementId = null)
        {
            var roleDetector = new FittingPathRoleDetector();
            Dictionary<long, FittingPathRoleInfo> rolesByElementId = roleDetector
                .Detect(path, localDataByElementId, networkDuctCalculationsByElementId ?? ductCalculationsByElementId)
                .GroupBy(role => role.ElementId)
                .ToDictionary(group => group.Key, group => group.First());
            var result = new List<LocalResistanceCalculationInfo>();
            for (int index = 0; index < path.ElementIds.Count; index++)
            {
                long elementId = ParseElementId(path.ElementIds[index]);
                if (elementId == 0 || IsDuct(path.Nodes[index]))
                {
                    continue;
                }

                LocalResistanceElementData data = localDataByElementId.TryGetValue(elementId, out LocalResistanceElementData? found)
                    ? found
                    : CreateFallbackData(elementId, path.Nodes[index]);

                DuctCalculationInfo? referenceDuct = FindReferenceDuct(path, index, ductCalculationsByElementId, out string referenceWarning);
                rolesByElementId.TryGetValue(elementId, out FittingPathRoleInfo? role);
                LocalResistanceCalculationInfo item = Calculate(data, referenceDuct, role);
                item.PathIndex = path.PathIndex;
                if (!string.IsNullOrWhiteSpace(referenceWarning))
                {
                    item.Warnings.Add(referenceWarning);
                }

                result.Add(item);
            }

            return result;
        }

        private static LocalResistanceCalculationInfo Calculate(LocalResistanceElementData data, DuctCalculationInfo? referenceDuct, FittingPathRoleInfo? role)
        {
            var result = new LocalResistanceCalculationInfo
            {
                ElementId = data.ElementId,
                CategoryName = data.CategoryName,
                FamilyName = data.FamilyName,
                TypeName = data.TypeName,
                Size = data.Size,
                PathRole = role?.PathRole ?? string.Empty,
                RoleReason = role?.Reason ?? string.Empty,
                PreviousDuctElementId = role?.PreviousDuctElementId,
                NextDuctElementId = role?.NextDuctElementId,
                PreviousAreaM2 = role?.PreviousAreaM2 ?? 0,
                NextAreaM2 = role?.NextAreaM2 ?? 0,
                PreviousFlowM3h = role?.PreviousFlowM3h ?? 0,
                NextFlowM3h = role?.NextFlowM3h ?? 0,
                ActualAngleDeg = role?.ActualAngleDeg,
                RoundedAngleDeg = role?.RoundedAngleDeg,
                AngleWasRounded = role?.AngleWasRounded ?? false,
                AngleSource = role?.AngleSource ?? string.Empty,
                AngleReason = role?.AngleReason ?? string.Empty,
                AngleRoundingWarning = role?.AngleRoundingWarning ?? string.Empty
            };
            result.Warnings.AddRange(data.Warnings);
            if (role != null)
            {
                result.Warnings.AddRange(role.Warnings);
            }

            result.PathDependent = IsPathDependentRole(result.PathRole);
            result.OverrideKey = BuildOverrideKey(data.SystemName, result.ElementId, result.PathRole, result.PreviousDuctElementId, result.NextDuctElementId);
            ProjectZetaCatalogItem? projectCatalogItem = data.ProjectZetaCatalog.FirstOrDefault(item => string.Equals(item.PathRole, result.PathRole, StringComparison.Ordinal));
            result.ProjectCatalogZeta = projectCatalogItem?.ProjectZeta;
            ZetaResult autoZeta = ResolveAutoZeta(data, role);
            ZetaResult effectiveZeta = ResolveEffectiveZeta(data, role, autoZeta);
            result.AutoZeta = autoZeta.Source == "Не определено" ? 0 : autoZeta.Value;
            result.ManualZeta = null;
            result.EffectiveZeta = effectiveZeta.Value;
            result.Zeta = effectiveZeta.Value;
            result.OriginalEffectiveZeta = effectiveZeta.Value;
            result.OriginalZetaSource = effectiveZeta.Source;
            result.LocalKind = effectiveZeta.LocalKind;
            result.Source = effectiveZeta.Source;
            result.ZetaSource = effectiveZeta.Source;
            result.ZetaComment = data.Comments;
            result.OverrideStorageType = effectiveZeta.Source == "Переопределение VentCalc" ? "DataStorage" : effectiveZeta.Source == "Комментарии" ? "Comment" : string.Empty;
            result.Warnings.AddRange(effectiveZeta.Warnings);

            if (referenceDuct == null)
            {
                result.Warnings.Add("Не найден ближайший воздуховод для определения скорости местного сопротивления.");
                return result;
            }

            result.FlowM3h = referenceDuct.FlowM3h;
            result.AreaM2 = referenceDuct.AreaM2;
            result.VelocityMs = referenceDuct.VelocityMs;
            result.DynamicPressurePa = referenceDuct.DynamicPressurePa;
            result.LocalPressureLossPa = result.Zeta * result.DynamicPressurePa;

            return result;
        }

        private static DuctCalculationInfo? FindReferenceDuct(
            VentPathInfo path,
            int localIndex,
            IReadOnlyDictionary<long, DuctCalculationInfo> ductCalculationsByElementId,
            out string warning)
        {
            warning = string.Empty;

            for (int index = localIndex + 1; index < path.ElementIds.Count; index++)
            {
                long elementId = ParseElementId(path.ElementIds[index]);
                if (ductCalculationsByElementId.TryGetValue(elementId, out DuctCalculationInfo? duct))
                {
                    return duct;
                }
            }

            for (int index = localIndex - 1; index >= 0; index--)
            {
                long elementId = ParseElementId(path.ElementIds[index]);
                if (ductCalculationsByElementId.TryGetValue(elementId, out DuctCalculationInfo? duct))
                {
                    return duct;
                }
            }

            DuctCalculationInfo? nearest = path.ElementIds
                .Select(ParseElementId)
                .Where(ductCalculationsByElementId.ContainsKey)
                .Select(id => ductCalculationsByElementId[id])
                .FirstOrDefault();
            if (nearest != null)
            {
                warning = "Скорость взята по ближайшему воздуховоду, так как соседний воздуховод по направлению не найден.";
            }

            return nearest;
        }

        private static ZetaResult ResolveAutoZeta(LocalResistanceElementData data, FittingPathRoleInfo? role)
        {
            if (role != null && !string.IsNullOrWhiteSpace(role.PathRole))
            {
                return ResolveRoleZeta(role.PathRole);
            }

            return ResolveRecommendedZeta(data);
        }

        private static ZetaResult ResolveEffectiveZeta(LocalResistanceElementData data, FittingPathRoleInfo? role, ZetaResult autoZeta)
        {
            if (TryFindVentCalcOverride(data, role, out ZetaOverrideInfo? zetaOverride))
            {
                return new ZetaResult(zetaOverride.Zeta, "Переопределение VentCalc", autoZeta.LocalKind, Array.Empty<string>());
            }

            if (!IsPathDependentRole(role?.PathRole ?? string.Empty) && TryReadZetaFromComments(data.Comments, out double zetaFromComments))
            {
                return new ZetaResult(zetaFromComments, "Комментарии", autoZeta.LocalKind, Array.Empty<string>());
            }

            ProjectZetaCatalogItem? catalogItem = data.ProjectZetaCatalog.FirstOrDefault(item => string.Equals(item.PathRole, role?.PathRole ?? string.Empty, StringComparison.Ordinal));
            if (catalogItem?.ProjectZeta.HasValue == true)
            {
                return new ZetaResult(catalogItem.ProjectZeta.GetValueOrDefault(), "Каталог проекта", autoZeta.LocalKind, Array.Empty<string>());
            }

            return autoZeta.Source == "Рекомендовано" ? new ZetaResult(autoZeta.Value, "Автоматически", autoZeta.LocalKind, autoZeta.Warnings) : autoZeta;
        }

        private static ZetaResult ResolveZeta(LocalResistanceElementData data, FittingPathRoleInfo? role)
        {
            if (TryReadZetaFromComments(data.Comments, out double zetaFromComments))
            {
                return new ZetaResult(zetaFromComments, "Комментарии", "Значение ζ из параметра Комментарии.", Array.Empty<string>());
            }

            if (role != null && !string.IsNullOrWhiteSpace(role.PathRole))
            {
                ZetaResult byRole = ResolveRoleZeta(role.PathRole);
                if (byRole.Source != "Не определено")
                {
                    return byRole;
                }

                return byRole;
            }

            ZetaResult recommended = ResolveRecommendedZeta(data);
            return recommended;
        }

        public static bool IsPathDependentRole(string pathRole)
        {
            return pathRole == "TeePass"
                || pathRole == "TeeBranch"
                || pathRole == "TeeUnknown"
                || pathRole == "CrossPass"
                || pathRole == "CrossBranch"
                || pathRole == "CrossUnknown"
                || pathRole == "TransitionNarrowing"
                || pathRole == "TransitionExpansion"
                || pathRole == "TransitionUnknown"
                || pathRole == "TapBranch";
        }

        public static string BuildOverrideKey(string systemName, long elementId, string pathRole, long? previousDuctElementId, long? nextDuctElementId)
        {
            return string.Join("|",
                systemName ?? string.Empty,
                elementId.ToString(CultureInfo.InvariantCulture),
                pathRole ?? string.Empty,
                previousDuctElementId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                nextDuctElementId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
        }

        private static bool TryFindVentCalcOverride(LocalResistanceElementData data, FittingPathRoleInfo? role, out ZetaOverrideInfo? zetaOverride)
        {
            zetaOverride = null;
            if (role == null)
            {
                return false;
            }

            string key = BuildOverrideKey(data.SystemName, data.ElementId, role.PathRole, role.PreviousDuctElementId, role.NextDuctElementId);
            zetaOverride = data.ZetaOverrides.FirstOrDefault(item => string.Equals(item.OverrideKey, key, StringComparison.Ordinal));
            return zetaOverride != null;
        }

        private static bool TryReadZetaFromComments(string comments, out double zeta)
        {
            zeta = 0;
            if (string.IsNullOrWhiteSpace(comments))
            {
                return false;
            }

            Match match = Regex.Match(
                comments,
                @"(?:ζ|zeta|z)\s*=\s*([-+]?\d+(?:[\.,]\d+)?)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                return false;
            }

            return double.TryParse(match.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out zeta);
        }


        private static ZetaResult ResolveRoleZeta(string pathRole)
        {
            return pathRole switch
            {
                "Elbow15" => Recommended(0.08, "Отвод 15°"),
                "Elbow30" => Recommended(0.12, "Отвод 30°"),
                "Elbow45" => Recommended(0.18, "Отвод 45°"),
                "Elbow60" => Recommended(0.25, "Отвод 60°"),
                "Elbow90" => Recommended(0.35, "Отвод 90°"),
                "TransitionNarrowing" => Recommended(0.10, "Переход сужение"),
                "TransitionExpansion" => Recommended(0.20, "Переход расширение"),
                "TeePass" => Recommended(0.30, "Тройник проход"),
                "TeeBranch" => Recommended(1.20, "Тройник ответвление"),
                "TapBranch" => Recommended(1.20, "Врезка ответвление"),
                "CrossPass" => Recommended(0.50, "Крестовина проход"),
                "CrossBranch" => Recommended(1.50, "Крестовина ответвление"),
                "Grille" => Recommended(2.00, "Решетка"),
                "Hood" => new ZetaResult(0, "Не определено", "Зонт", new[] { "Для зонта не задан коэффициент ζ." }),
                "Damper" => Recommended(0.40, "Дроссель-клапан"),
                "FireDamper" => Recommended(0.50, "Противопожарный клапан"),
                "BackdraftDamper" => Recommended(2.00, "Обратный клапан"),
                "Cap" => Recommended(0.00, "Заглушка"),
                _ => new ZetaResult(0, "Не определено", pathRole, new[] { "Местное сопротивление не рассчитано: роль фитинга в трассе не определена." })
            };
        }

        private static ZetaResult ResolveRecommendedZeta(LocalResistanceElementData data)
        {
            string text = string.Join(" ", data.Name, data.TypeName, data.FamilyName, data.CategoryName);

            if (ContainsAny(text, "Заглушка", "Cap"))
            {
                return Recommended(0, "Заглушка");
            }

            if (ContainsAny(text, "Отвод", "Bend", "Elbow"))
            {
                if (ContainsAny(text, "15")) return Recommended(0.08, "Отвод 15°");
                if (ContainsAny(text, "30")) return Recommended(0.12, "Отвод 30°");
                if (ContainsAny(text, "45")) return Recommended(0.18, "Отвод 45°");
                if (ContainsAny(text, "60")) return Recommended(0.25, "Отвод 60°");
                return Recommended(0.35, "Отвод 90°");
            }

            if (ContainsAny(text, "Переход", "Transition"))
            {
                if (ContainsAny(text, "расшир", "Expansion")) return Recommended(0.20, "Переход расширение");
                if (ContainsAny(text, "суж", "Contraction")) return Recommended(0.10, "Переход сужение");
                return Recommended(0.10, "Переход");
            }

            if (ContainsAny(text, "Тройник", "Tee")) return Recommended(1.20, "Тройник");
            if (ContainsAny(text, "Врезка", "Tap")) return Recommended(1.20, "Врезка");
            if (ContainsAny(text, "Крестовина", "Cross")) return Recommended(1.50, "Крестовина ответвление");
            if (ContainsAny(text, "Утка", "Offset")) return Recommended(0.40, "Утка");
            if (ContainsAny(text, "Дроссель", "Damper")) return Recommended(0.40, "Дроссель-клапан");
            if (ContainsAny(text, "Противопожар", "Fire")) return Recommended(0.50, "Противопожарный клапан");
            if (ContainsAny(text, "Обрат", "Backdraft", "Check")) return Recommended(2.00, "Обратный клапан");
            if (ContainsAny(text, "Вход", "Inlet")) return Recommended(0.50, "Вход");
            if (ContainsAny(text, "Выход", "Outlet")) return Recommended(1.00, "Выход");
            if (ContainsAny(text, "Реш", "Grille", "Diffuser")) return Recommended(2.00, "Решетка");
            if (ContainsAny(text, "Зонт", "Hood", "Canopy", "местный отсос")) return new ZetaResult(0, "Не определено", "Зонт", new[] { "Для зонта не задан коэффициент ζ." });
            if (ContainsAny(text, "Дефлектор", "Deflector")) return Recommended(1.00, "Дефлектор");

            return new ZetaResult(0, "Не найдено", "Не классифицировано", new[] { "ζ не найден в комментариях и не подобран по рекомендациям." });
        }

        private static ZetaResult Recommended(double value, string localKind)
        {
            return new ZetaResult(value, "Рекомендовано", localKind, Array.Empty<string>());
        }

        private static bool IsDuct(VentPathNode node)
        {
            return string.Equals(node.CategoryKey, "OST_DuctCurves", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsAny(string text, params string[] patterns)
        {
            return patterns.Any(pattern => text.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static long ParseElementId(string value)
        {
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
                ? parsed
                : 0;
        }

        private static LocalResistanceElementData CreateFallbackData(long elementId, VentPathNode node)
        {
            return new LocalResistanceElementData
            {
                ElementId = elementId,
                CategoryKey = node.CategoryKey,
                CategoryName = node.CategoryName,
                FamilyName = node.FamilyName,
                TypeName = node.TypeName,
                Size = node.Size
            };
        }

        private sealed class ZetaResult
        {
            public ZetaResult(double value, string source, string localKind, IEnumerable<string> warnings)
            {
                Value = value;
                Source = source;
                LocalKind = localKind;
                Warnings = warnings.ToList();
            }

            public double Value { get; }

            public string Source { get; }

            public string LocalKind { get; }

            public IReadOnlyList<string> Warnings { get; }
        }
    }
}
