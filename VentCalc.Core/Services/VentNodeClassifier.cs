using System;
using System.Collections.Generic;
using VentCalc.Core.Models;

namespace VentCalc.Core.Services
{
    public sealed class VentNodeClassifier
    {
        public void Classify(IEnumerable<VentNetworkNode> nodes)
        {
            foreach (VentNetworkNode node in nodes)
            {
                Classify(node);
            }
        }

        public void Classify(VentNetworkNode node)
        {
            node.IsStartCandidate = false;
            node.IsEndCandidate = false;
            node.IsIgnoredForPathSearch = false;

            if (IsCapLike(node))
            {
                SetRole(node, VentNodeRole.Cap, "Элемент похож на заглушку по имени/типу/семейству.");
                node.IsIgnoredForPathSearch = true;
                return;
            }

            if (IsCategory(node, "OST_DuctTerminal"))
            {
                if (IsHoodLike(node))
                {
                    SetRole(node, VentNodeRole.HoodCandidate, "Терминал распознан как зонт/местный отсос по имени, типу или семейству.");
                    return;
                }

                SetRole(node, VentNodeRole.TerminalCandidate, "Категория OST_DuctTerminal: терминал/решётка.");
                return;
            }

            if (IsCategory(node, "OST_MechanicalEquipment"))
            {
                ClassifyMechanicalEquipment(node);
                return;
            }

            if (node.OpenConnectorCount > 0)
            {
                SetRole(node, VentNodeRole.OpenEndCandidate, "Есть открытый коннектор без реального соседнего вентиляционного элемента.");
                return;
            }

            if (IsCategory(node, "OST_DuctCurves"))
            {
                SetRole(node, VentNodeRole.InlineDuct, "Категория OST_DuctCurves: участок воздуховода.");
                return;
            }

            if (IsCategory(node, "OST_DuctFitting"))
            {
                SetRole(node, VentNodeRole.Fitting, "Категория OST_DuctFitting: фитинг.");
                return;
            }

            if (IsCategory(node, "OST_DuctAccessory"))
            {
                SetRole(node, VentNodeRole.Accessory, "Категория OST_DuctAccessory: арматура.");
                return;
            }

            SetRole(node, VentNodeRole.Unknown, "Роль узла не определена.");
        }

        private static void ClassifyMechanicalEquipment(VentNetworkNode node)
        {
            if (IsFanOrAhuLike(node))
            {
                SetRole(node, VentNodeRole.FanCandidate, "Оборудование распознано как вентилятор/вентустановка по имени, типу или семейству.");
                return;
            }

            if (IsHoodLike(node))
            {
                SetRole(node, VentNodeRole.HoodCandidate, "Оборудование распознано как зонт/местный отсос по имени, типу или семейству.");
                return;
            }

            if (IsTerminalLikeEquipment(node))
            {
                SetRole(node, VentNodeRole.HoodCandidate, "Листовое оборудование с одним HVAC-коннектором рассматривается как терминальное устройство вытяжки.");
                return;
            }

            if (node.ConnectorCount >= 2 || node.ConnectedElementIds.Count >= 2)
            {
                SetRole(node, VentNodeRole.InlineEquipment, "Оборудование имеет два и более вентиляционных соединения и считается проходным.");
                return;
            }

            SetRole(node, VentNodeRole.EquipmentCandidate, "Категория OST_MechanicalEquipment: оборудование без признаков терминала или проходного элемента.");
        }

        private static bool IsTerminalLikeEquipment(VentNetworkNode node)
        {
            return node.ConnectorCount == 1
                && node.ConnectedElementIds.Count <= 1
                && !IsFanOrAhuLike(node)
                && !IsPassThroughEquipment(node);
        }

        private static bool IsHoodLike(VentNetworkNode node)
        {
            string text = BuildSearchText(node);
            return ContainsAny(text,
                "зонт",
                "hood",
                "вытяжной зонт",
                "кухонный зонт",
                "местный отсос",
                "canopy");
        }

        private static bool IsFanOrAhuLike(VentNetworkNode node)
        {
            string text = BuildSearchText(node);
            return ContainsAny(text,
                "вентилятор",
                "fan",
                "ahu",
                "air handling",
                "вентустановка",
                "установка",
                "агрегат");
        }

        private static bool IsPassThroughEquipment(VentNetworkNode node)
        {
            string text = BuildSearchText(node);
            return ContainsAny(text, "теплообмен", "калорифер", "heater", "cooler", "coil", "filter", "фильтр");
        }

        private static void SetRole(VentNetworkNode node, VentNodeRole role, string reason)
        {
            node.Role = role;
            node.PathRoleReason = reason;
        }

        private static bool IsCapLike(VentNetworkNode node)
        {
            if (!IsCategory(node, "OST_DuctFitting") && !IsCategory(node, "OST_DuctAccessory"))
            {
                return false;
            }

            string text = BuildSearchText(node);
            return ContainsAny(text, "Заглушка", "Cap");
        }

        private static string BuildSearchText(VentNetworkNode node)
        {
            return string.Join(" ",
                node.Name ?? string.Empty,
                node.TypeName ?? string.Empty,
                node.FamilyName ?? string.Empty,
                node.CategoryName ?? string.Empty);
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

        private static bool IsCategory(VentNetworkNode node, string categoryKey)
        {
            return string.Equals(node.CategoryKey, categoryKey, StringComparison.OrdinalIgnoreCase);
        }
    }
}
