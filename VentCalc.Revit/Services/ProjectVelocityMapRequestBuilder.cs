using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using VentCalc.Core.Models;
using VentCalc.UI.Services;
using VentCalc.UI.ViewModels;

namespace VentCalc.Revit.Services
{
    internal sealed class ProjectVelocityMapRequestBuilder
    {
        public HighlightRequest Build(Document document, VentCalcCenterViewModel viewModel)
        {
            Dictionary<long, double> calculatedDuctVelocities = (viewModel.AerodynamicSummary?.Paths ?? Enumerable.Empty<PathCalculationInfo>())
                .SelectMany(path => path.Ducts)
                .Where(duct => duct.ElementId > 0 && IsValidVelocity(duct.VelocityMs))
                .GroupBy(duct => duct.ElementId)
                .ToDictionary(group => group.Key, group => group.Max(duct => duct.VelocityMs));

            var ductVelocities = new Dictionary<long, double>();
            var notCalculatedDucts = new HashSet<long>();
            foreach (Element duct in new FilteredElementCollector(document).OfCategory(BuiltInCategory.OST_DuctCurves).WhereElementIsNotElementType())
            {
                long elementId = duct.Id.Value;
                if (calculatedDuctVelocities.TryGetValue(elementId, out double calculatedVelocity))
                {
                    ductVelocities[elementId] = calculatedVelocity;
                    continue;
                }

                double velocity = TryReadDuctVelocity(duct);
                if (IsValidVelocity(velocity))
                {
                    ductVelocities[elementId] = velocity;
                }
                else
                {
                    notCalculatedDucts.Add(elementId);
                }
            }

            Dictionary<long, double> localFittingVelocities = (viewModel.AerodynamicSummary?.Paths ?? Enumerable.Empty<PathCalculationInfo>())
                .SelectMany(path => path.LocalResistances)
                .Where(local => local.ElementId > 0 && IsValidVelocity(local.VelocityMs))
                .GroupBy(local => local.ElementId)
                .ToDictionary(group => group.Key, group => group.Max(local => local.VelocityMs));

            var fittingVelocities = new Dictionary<long, double>();
            var notCalculatedFittings = new HashSet<long>();
            foreach (Element fitting in new FilteredElementCollector(document).OfCategory(BuiltInCategory.OST_DuctFitting).WhereElementIsNotElementType())
            {
                long elementId = fitting.Id.Value;
                if (IsCapLike(fitting))
                {
                    continue;
                }

                if (localFittingVelocities.TryGetValue(elementId, out double localVelocity))
                {
                    fittingVelocities[elementId] = localVelocity;
                    continue;
                }

                double neighborVelocity = GetNeighborDuctVelocities(fitting, ductVelocities).DefaultIfEmpty(0).Max();
                if (IsValidVelocity(neighborVelocity))
                {
                    fittingVelocities[elementId] = neighborVelocity;
                }
                else
                {
                    notCalculatedFittings.Add(elementId);
                }
            }

            var belowMin = new List<long>();
            var normal = new List<long>();
            var aboveMax = new List<long>();
            var critical = new List<long>();
            foreach (KeyValuePair<long, double> item in ductVelocities.Concat(fittingVelocities))
            {
                AddToVelocityGroup(item.Key, item.Value, viewModel.Settings.MinVelocityMs, viewModel.Settings.MaxVelocityMs, viewModel.Settings.CriticalVelocityMs, belowMin, normal, aboveMax, critical);
            }

            var velocityGroups = new HighlightVelocityGroupsInfo
            {
                BelowMin = belowMin.Count,
                Normal = normal.Count,
                AboveMax = aboveMax.Count,
                Critical = critical.Count,
                NotCalculated = notCalculatedDucts.Count + notCalculatedFittings.Count,
                ColoredDuctCount = ductVelocities.Count,
                ColoredFittingCount = fittingVelocities.Count,
                NotCalculatedDuctCount = notCalculatedDucts.Count,
                NotCalculatedFittingCount = notCalculatedFittings.Count,
                Scope = "Project"
            };

            return new HighlightRequest
            {
                Action = HighlightAction.Apply,
                Mode = HighlightMode.Velocity,
                SelectElements = false,
                ShowElements = false,
                WindowSource = "Ribbon: Карта скоростей",
                DisplayMode = HighlightDisplayMode.Normal,
                SystemName = viewModel.SystemName,
                VelocityGroups = velocityGroups,
                StatusMessage = $"Карта скоростей применена по проекту: воздуховодов {ductVelocities.Count}, фитингов {fittingVelocities.Count}.",
                Groups = new List<HighlightElementGroup>
                {
                    CreateGroup($"Ниже {viewModel.Settings.MinVelocityMs:0.###} м/с", viewModel.Settings.LowVelocityColorHex, belowMin, 6, 35),
                    CreateGroup($"От {viewModel.Settings.MinVelocityMs:0.###} до {viewModel.Settings.MaxVelocityMs:0.###} м/с", viewModel.Settings.NormalVelocityColorHex, normal, 5, 45),
                    CreateGroup($"Выше {viewModel.Settings.MaxVelocityMs:0.###} м/с", viewModel.Settings.HighVelocityColorHex, aboveMax, 7, 30),
                    CreateGroup($"От {viewModel.Settings.CriticalVelocityMs:0.###} м/с", viewModel.Settings.CriticalVelocityColorHex, critical, 9, 15)
                }.Where(group => group.ElementIds.Count > 0).ToList()
            };
        }

        private static HighlightElementGroup CreateGroup(string name, string colorHex, IEnumerable<long> elementIds, int lineWeight, int transparency)
        {
            return new HighlightElementGroup
            {
                Name = name,
                ColorHex = colorHex,
                LineWeight = lineWeight,
                Transparency = transparency,
                ElementIds = elementIds.Distinct().ToList()
            };
        }

        private static void AddToVelocityGroup(long elementId, double velocity, double min, double max, double criticalLimit, ICollection<long> belowMin, ICollection<long> normal, ICollection<long> aboveMax, ICollection<long> critical)
        {
            if (velocity < min)
            {
                belowMin.Add(elementId);
            }
            else if (velocity <= max)
            {
                normal.Add(elementId);
            }
            else if (velocity < criticalLimit)
            {
                aboveMax.Add(elementId);
            }
            else
            {
                critical.Add(elementId);
            }
        }

        private static double TryReadDuctVelocity(Element duct)
        {
            DuctGeometryData? geometry = new RevitDuctGeometryReader().ReadDuct(duct);
            if (geometry == null || geometry.FlowM3h <= 0)
            {
                return 0;
            }

            double area = geometry.IsRound && geometry.DiameterM > 0
                ? Math.PI * geometry.DiameterM * geometry.DiameterM / 4.0
                : geometry.IsRectangular && geometry.WidthM > 0 && geometry.HeightM > 0
                    ? geometry.WidthM * geometry.HeightM
                    : 0;
            return area > 0 ? geometry.FlowM3h / 3600.0 / area : 0;
        }

        private static IEnumerable<double> GetNeighborDuctVelocities(Element fitting, IReadOnlyDictionary<long, double> ductVelocities)
        {
            ConnectorSet? connectors = GetConnectorSet(fitting);
            if (connectors == null)
            {
                yield break;
            }

            foreach (Connector connector in connectors)
            {
                foreach (Connector reference in connector.AllRefs)
                {
                    Element? owner = reference.Owner;
                    if (owner == null || owner.Category == null || owner.Id.Value == fitting.Id.Value)
                    {
                        continue;
                    }

                    if ((BuiltInCategory)owner.Category.Id.Value == BuiltInCategory.OST_DuctCurves && ductVelocities.TryGetValue(owner.Id.Value, out double velocity) && IsValidVelocity(velocity))
                    {
                        yield return velocity;
                    }
                }
            }
        }

        private static ConnectorSet? GetConnectorSet(Element element)
        {
            if (element is FamilyInstance familyInstance)
            {
                return familyInstance.MEPModel?.ConnectorManager?.Connectors;
            }

            return element is MEPCurve mepCurve ? mepCurve.ConnectorManager?.Connectors : null;
        }

        private static bool IsCapLike(Element element)
        {
            string text = string.Join(" ", element.Name, element.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString(), element.get_Parameter(BuiltInParameter.ELEM_TYPE_PARAM)?.AsValueString());
            return text.IndexOf("заглуш", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("cap", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("plug", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsValidVelocity(double velocityMs)
        {
            return velocityMs > 0 && !double.IsNaN(velocityMs) && !double.IsInfinity(velocityMs);
        }
    }
}
