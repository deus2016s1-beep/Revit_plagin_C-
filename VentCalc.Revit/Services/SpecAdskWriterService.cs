using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using VentCalc.UI.Services;

namespace VentCalc.Revit.Services
{
    public sealed class SpecAdskWriteResult
    {
        public int RequestedCount { get; set; }
        public int WrittenElementCount { get; set; }
        public List<string> Messages { get; } = new List<string>();
        public string Summary => Messages.Count == 0
            ? $"ADSK-параметры записаны: {WrittenElementCount}/{RequestedCount}."
            : $"ADSK-параметры записаны: {WrittenElementCount}/{RequestedCount}. Замечания: {string.Join("; ", Messages.Take(5))}";
    }

    public sealed class SpecAdskWriterService
    {
        public SpecAdskWriteResult Write(Document document, IReadOnlyList<SpecItemRow> rows)
        {
            var result = new SpecAdskWriteResult { RequestedCount = rows.Count };
            using var transaction = new Transaction(document, "SpecCalc: запись ADSK параметров");
            transaction.Start();
            foreach (SpecItemRow row in rows)
            {
                Element? element = document.GetElement(new ElementId(row.ElementId));
                if (element == null)
                {
                    result.Messages.Add($"ElementId {row.ElementId}: элемент не найден");
                    continue;
                }

                bool wroteAny = false;
                wroteAny |= TryWriteString(element, "ADSK_Наименование", row.Name, result);
                wroteAny |= TryWriteString(element, "ADSK_Марка", row.TypeMark, result);
                wroteAny |= TryWriteString(element, "ADSK_Единица измерения", row.Unit, result);
                wroteAny |= TryWriteString(element, "ADSK_Примечание", row.Note, result);
                if (wroteAny)
                {
                    result.WrittenElementCount++;
                }
            }

            transaction.Commit();
            return result;
        }

        private static bool TryWriteString(Element element, string parameterName, string value, SpecAdskWriteResult result)
        {
            Parameter? parameter = element.LookupParameter(parameterName);
            if (parameter == null)
            {
                result.Messages.Add($"ElementId {element.Id.Value}: нет параметра {parameterName}");
                return false;
            }

            if (parameter.IsReadOnly)
            {
                result.Messages.Add($"ElementId {element.Id.Value}: параметр {parameterName} только для чтения");
                return false;
            }

            try
            {
                if (parameter.StorageType == StorageType.String)
                {
                    parameter.Set(value ?? string.Empty);
                    return true;
                }

                result.Messages.Add($"ElementId {element.Id.Value}: параметр {parameterName} не строковый");
                return false;
            }
            catch (Exception exception)
            {
                result.Messages.Add($"ElementId {element.Id.Value}: {parameterName}: {exception.Message}");
                return false;
            }
        }
    }
}
