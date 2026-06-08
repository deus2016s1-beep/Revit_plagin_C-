using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace VentCalc.Core.Models
{
    public sealed class VentElementInfo
    {
        public VentElementInfo(
            string elementId,
            string categoryName,
            string name,
            string typeName,
            string familyName,
            string systemName,
            string systemType,
            string levelName,
            IEnumerable<VentParameterInfo> parameters,
            IEnumerable<VentConnectorInfo> connectors)
        {
            ElementId = elementId;
            CategoryName = categoryName;
            Name = name;
            TypeName = typeName;
            FamilyName = familyName;
            SystemName = systemName;
            SystemType = systemType;
            LevelName = levelName;
            Parameters = new ReadOnlyCollection<VentParameterInfo>(parameters.ToList());
            Connectors = new ReadOnlyCollection<VentConnectorInfo>(connectors.ToList());
        }

        public string ElementId { get; }

        public string CategoryName { get; }

        public string Name { get; }

        public string TypeName { get; }

        public string FamilyName { get; }

        public string SystemName { get; }

        public string SystemType { get; }

        public string LevelName { get; }

        public IReadOnlyList<VentParameterInfo> Parameters { get; }

        public IReadOnlyList<VentConnectorInfo> Connectors { get; }

        public string ToReportText()
        {
            var builder = new StringBuilder();
            builder.AppendLine("Инспектор выбранного элемента");
            builder.AppendLine(new string('=', 32));
            builder.AppendLine();
            builder.AppendLine("Основная информация");
            builder.AppendLine($"ElementId: {ElementId}");
            builder.AppendLine($"Категория: {CategoryName}");
            builder.AppendLine($"Имя: {Name}");
            builder.AppendLine($"Тип: {TypeName}");
            builder.AppendLine($"Семейство: {FamilyName}");
            builder.AppendLine($"Система: {SystemName}");
            builder.AppendLine($"Тип системы: {SystemType}");
            builder.AppendLine($"Уровень: {LevelName}");
            builder.AppendLine();

            builder.AppendLine("Параметры");
            if (Parameters.Count == 0)
            {
                builder.AppendLine("— Не найдены");
            }
            else
            {
                foreach (VentParameterInfo parameter in Parameters)
                {
                    builder.AppendLine($"- {parameter.Name}: {parameter.Value}");
                }
            }

            builder.AppendLine();
            builder.AppendLine("Коннекторы");
            if (Connectors.Count == 0)
            {
                builder.AppendLine("— Коннекторы не найдены");
            }
            else
            {
                foreach (VentConnectorInfo connector in Connectors)
                {
                    builder.AppendLine($"Коннектор #{connector.Number}");
                    builder.AppendLine($"  ConnectorType: {connector.ConnectorType}");
                    builder.AppendLine($"  Domain: {connector.Domain}");
                    builder.AppendLine($"  Direction / FlowDirectionType: {connector.Direction}");
                    builder.AppendLine($"  Shape: {connector.Shape}");
                    builder.AppendLine($"  Radius: {connector.Radius}");
                    builder.AppendLine($"  Width: {connector.Width}");
                    builder.AppendLine($"  Height: {connector.Height}");
                    builder.AppendLine($"  IsConnected: {connector.IsConnected}");
                    builder.AppendLine($"  AllRefs: {connector.AllRefsCount}");
                    builder.AppendLine($"  Подключенные элементы: {string.Join(", ", connector.ConnectedElementIds)}");
                    builder.AppendLine();
                }
            }

            return builder.ToString();
        }
    }
}
