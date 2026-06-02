using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VentCalc.Core.Models;

namespace VentCalc.Core.Services
{
    public sealed class AerodynamicCalculator
    {
        private readonly LocalResistanceCalculator localResistanceCalculator = new LocalResistanceCalculator();
        public DuctCalculationInfo CalculateDuct(DuctGeometryData duct, AerodynamicSettings settings)
        {
            var result = new DuctCalculationInfo
            {
                ElementId = duct.ElementId,
                Size = duct.Size,
                FlowM3h = duct.FlowM3h,
                FlowM3s = duct.FlowM3h / 3600.0,
                LengthM = duct.LengthM,
                WidthM = duct.WidthM,
                HeightM = duct.HeightM,
                DiameterM = duct.DiameterM,
                IsRound = duct.IsRound,
                IsRectangular = duct.IsRectangular
            };

            result.Warnings.AddRange(duct.Warnings);

            if (result.LengthM <= 0)
            {
                result.Warnings.Add("Длина воздуховода не найдена или равна 0.");
            }

            if (result.FlowM3h <= 0)
            {
                result.Warnings.Add("Расход воздуха не найден или равен 0.");
            }

            if (duct.IsRound)
            {
                if (duct.DiameterM <= 0)
                {
                    result.Warnings.Add("Диаметр круглого воздуховода не найден или равен 0.");
                    return result;
                }

                result.AreaM2 = Math.PI * duct.DiameterM * duct.DiameterM / 4.0;
                result.EquivalentDiameterM = duct.DiameterM;
            }
            else if (duct.IsRectangular)
            {
                if (duct.WidthM <= 0 || duct.HeightM <= 0)
                {
                    result.Warnings.Add("Ширина или высота прямоугольного воздуховода не найдена или равна 0.");
                    return result;
                }

                result.AreaM2 = duct.WidthM * duct.HeightM;
                result.EquivalentDiameterM = 2.0 * duct.WidthM * duct.HeightM / (duct.WidthM + duct.HeightM);
            }
            else
            {
                result.Warnings.Add("Не удалось определить форму воздуховода: нет диаметра или пары ширина/высота.");
                return result;
            }

            if (result.AreaM2 <= 0 || result.EquivalentDiameterM <= 0)
            {
                result.Warnings.Add("Площадь сечения или эквивалентный диаметр равны 0; расчёт воздуховода пропущен.");
                return result;
            }

            result.VelocityMs = result.FlowM3s / result.AreaM2;
            result.Reynolds = settings.AirDensityKgM3 * result.VelocityMs * result.EquivalentDiameterM / settings.AirDynamicViscosityPaS;
            result.DynamicPressurePa = settings.AirDensityKgM3 * result.VelocityMs * result.VelocityMs / 2.0;
            result.Lambda = CalculateLambda(result.Reynolds, settings.RoughnessM, result.EquivalentDiameterM);
            result.SpecificPressureLossPaPerM = result.Lambda * result.DynamicPressurePa / result.EquivalentDiameterM;
            result.FrictionPressureLossPa = result.SpecificPressureLossPaPerM * result.LengthM;

            return result;
        }

        public PathCalculationInfo CalculatePath(
            VentPathInfo path,
            IReadOnlyDictionary<long, DuctGeometryData> ductDataByElementId,
            AerodynamicSettings settings)
        {
            return CalculatePath(path, ductDataByElementId, new Dictionary<long, LocalResistanceElementData>(), settings);
        }

        public PathCalculationInfo CalculatePath(
            VentPathInfo path,
            IReadOnlyDictionary<long, DuctGeometryData> ductDataByElementId,
            IReadOnlyDictionary<long, LocalResistanceElementData> localDataByElementId,
            AerodynamicSettings settings)
        {
            var result = new PathCalculationInfo
            {
                PathIndex = path.PathIndex,
                StartElementId = ParseElementId(path.StartElementId),
                EndElementId = ParseElementId(path.EndElementId),
                ElementIds = path.ElementIds.Select(ParseElementId).Where(id => id != 0).ToList()
            };

            foreach (long elementId in result.ElementIds)
            {
                if (!ductDataByElementId.TryGetValue(elementId, out DuctGeometryData? ductData))
                {
                    continue;
                }

                result.Ducts.Add(CalculateDuct(ductData, settings));
            }

            Dictionary<long, DuctCalculationInfo> ductCalculationsByElementId = result.Ducts.ToDictionary(duct => duct.ElementId);
            result.LocalResistances.AddRange(localResistanceCalculator.CalculatePathLocalResistances(path, localDataByElementId, ductCalculationsByElementId));

            result.TotalDuctLengthM = result.Ducts.Sum(duct => duct.LengthM);
            result.TotalFrictionPressureLossPa = result.Ducts.Sum(duct => duct.FrictionPressureLossPa);
            result.TotalLocalPressureLossPa = result.LocalResistances.Sum(local => local.LocalPressureLossPa);
            result.TotalPressureLossPa = result.TotalFrictionPressureLossPa + result.TotalLocalPressureLossPa;

            var calculatedVelocities = result.Ducts
                .Where(duct => duct.VelocityMs > 0)
                .Select(duct => duct.VelocityMs)
                .ToList();
            result.MaxVelocityMs = calculatedVelocities.Count == 0 ? 0 : calculatedVelocities.Max();
            result.MinVelocityMs = calculatedVelocities.Count == 0 ? 0 : calculatedVelocities.Min();

            if (result.Ducts.Count == 0)
            {
                result.Warnings.Add("В трассе нет воздуховодов OST_DuctCurves с геометрическими данными.");
            }

            foreach (DuctCalculationInfo duct in result.Ducts.Where(duct => duct.Warnings.Count > 0))
            {
                result.Warnings.Add($"Воздуховод {duct.ElementId}: {string.Join("; ", duct.Warnings)}");
            }

            foreach (LocalResistanceCalculationInfo local in result.LocalResistances.Where(local => local.Warnings.Count > 0))
            {
                result.Warnings.Add($"Местное сопротивление {local.ElementId}: {string.Join("; ", local.Warnings)}");
            }

            return result;
        }

        public AerodynamicCalculationSummary CalculatePaths(
            IEnumerable<VentPathInfo> paths,
            IReadOnlyDictionary<long, DuctGeometryData> ductDataByElementId,
            AerodynamicSettings settings)
        {
            return CalculatePaths(paths, ductDataByElementId, new Dictionary<long, LocalResistanceElementData>(), settings);
        }

        public AerodynamicCalculationSummary CalculatePaths(
            IEnumerable<VentPathInfo> paths,
            IReadOnlyDictionary<long, DuctGeometryData> ductDataByElementId,
            IReadOnlyDictionary<long, LocalResistanceElementData> localDataByElementId,
            AerodynamicSettings settings)
        {
            return new AerodynamicCalculationSummary(paths.Select(path => CalculatePath(path, ductDataByElementId, localDataByElementId, settings)));
        }

        private static double CalculateLambda(double reynolds, double roughnessM, double equivalentDiameterM)
        {
            if (reynolds <= 0 || equivalentDiameterM <= 0)
            {
                return 0;
            }

            if (reynolds < 2300)
            {
                return 64.0 / reynolds;
            }

            double argument = roughnessM / (3.7 * equivalentDiameterM) + 5.74 / Math.Pow(reynolds, 0.9);
            if (argument <= 0)
            {
                return 0;
            }

            return 0.25 / Math.Pow(Math.Log10(argument), 2.0);
        }

        private static long ParseElementId(string value)
        {
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
                ? parsed
                : 0;
        }
    }
}
