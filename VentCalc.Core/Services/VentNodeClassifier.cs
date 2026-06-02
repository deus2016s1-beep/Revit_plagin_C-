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

            if (IsCategory(node, "OST_DuctTerminal"))
            {
                SetRole(node, VentNodeRole.TerminalCandidate, "Категория OST_DuctTerminal: терминал/решётка.");
                return;
            }

            if (IsCategory(node, "OST_MechanicalEquipment"))
            {
                SetRole(node, VentNodeRole.EquipmentCandidate, "Категория OST_MechanicalEquipment: оборудование.");
                return;
            }

            if (IsCapLike(node))
            {
                SetRole(node, VentNodeRole.Cap, "Элемент похож на заглушку по имени/типу/семейству.");
                node.IsIgnoredForPathSearch = true;
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

            string text = string.Join(" ",
                node.Name ?? string.Empty,
                node.TypeName ?? string.Empty,
                node.FamilyName ?? string.Empty,
                node.CategoryName ?? string.Empty);

            return text.IndexOf("Заглушка", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("Cap", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsCategory(VentNetworkNode node, string categoryKey)
        {
            return string.Equals(node.CategoryKey, categoryKey, StringComparison.OrdinalIgnoreCase);
        }
    }
}
