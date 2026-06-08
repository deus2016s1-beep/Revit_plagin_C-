using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace VentCalc.Revit.Services
{
    public sealed class RevitSelectionReader
    {
        private static readonly HashSet<BuiltInCategory> SupportedCategories = new HashSet<BuiltInCategory>
        {
            BuiltInCategory.OST_DuctCurves,
            BuiltInCategory.OST_DuctFitting,
            BuiltInCategory.OST_DuctAccessory,
            BuiltInCategory.OST_DuctTerminal,
            BuiltInCategory.OST_MechanicalEquipment
        };

        public bool TryGetSingleSelectedVentElement(
            UIDocument uiDocument,
            [NotNullWhen(true)] out Element? element,
            [NotNullWhen(false)] out string? errorMessage)
        {
            element = null;
            errorMessage = null;

            ICollection<ElementId> selectedIds = uiDocument.Selection.GetElementIds();
            if (selectedIds.Count != 1)
            {
                errorMessage = "Выберите ровно один элемент воздуховодной системы.";
                return false;
            }

            Element? selectedElement = uiDocument.Document.GetElement(selectedIds.First());
            if (!IsSupportedVentilationElement(selectedElement))
            {
                errorMessage = "Выберите элемент воздуховодной системы.";
                return false;
            }

            element = selectedElement;
            return true;
        }

        private static bool IsSupportedVentilationElement(Element? element)
        {
            if (element?.Category == null)
            {
                return false;
            }

            var category = (BuiltInCategory)element.Category.Id.Value;
            return SupportedCategories.Contains(category);
        }
    }
}
