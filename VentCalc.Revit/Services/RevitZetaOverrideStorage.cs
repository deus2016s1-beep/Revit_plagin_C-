using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using VentCalc.Core.Models;
using VentCalc.Core.Services;

namespace VentCalc.Revit.Services
{
    public static class RevitZetaOverrideStorage
    {
        private static readonly Guid SchemaGuid = new Guid("7E73C1F8-9A5B-4E7E-96C1-7A1F50B8D2E4");
        private static readonly Guid CatalogSchemaGuid = new Guid("B7B9A8A1-9D67-4D66-8A74-0F3C1896F2D1");
        private const string SchemaName = "VentCalcData";
        private const string CatalogSchemaName = "VentCalcProjectZetaCatalog";
        private const string DataFieldName = "ZetaOverrides";
        private const string CatalogFieldName = "ProjectZetaCatalog";
        private const string StorageName = "VentCalcData";
        private const string CatalogStorageName = "VentCalcProjectZetaCatalog";

        public static IReadOnlyList<ZetaOverrideInfo> ReadOverrides(Document document)
        {
            Schema schema = GetOrCreateSchema();
            DataStorage? storage = FindStorage(document, schema);
            if (storage == null)
            {
                return Array.Empty<ZetaOverrideInfo>();
            }

            Entity entity = storage.GetEntity(schema);
            if (!entity.IsValid())
            {
                return Array.Empty<ZetaOverrideInfo>();
            }

            string serialized = entity.Get<string>(schema.GetField(DataFieldName)) ?? string.Empty;
            return Deserialize(serialized);
        }

        public static IReadOnlyList<ZetaOverrideInfo> SaveOverrides(Document document, IEnumerable<ZetaOverrideInfo> overrides)
        {
            Schema schema = GetOrCreateSchema();
            DataStorage storage = FindStorage(document, schema) ?? DataStorage.Create(document);
            storage.Name = StorageName;

            var merged = ReadOverrides(document)
                .Where(existing => !overrides.Any(item => string.Equals(item.OverrideKey, existing.OverrideKey, StringComparison.Ordinal)))
                .Concat(overrides)
                .OrderBy(item => item.SystemName, StringComparer.Ordinal)
                .ThenBy(item => item.ElementId)
                .ThenBy(item => item.PathRole, StringComparer.Ordinal)
                .ToList();

            var entity = new Entity(schema);
            entity.Set(schema.GetField(DataFieldName), Serialize(merged));
            storage.SetEntity(entity);
            return merged;
        }


        public static IReadOnlyList<ZetaOverrideInfo> DeleteOverrides(Document document, IEnumerable<string> overrideKeys)
        {
            var keys = new HashSet<string>(overrideKeys.Where(key => !string.IsNullOrWhiteSpace(key)), StringComparer.Ordinal);
            Schema schema = GetOrCreateSchema();
            DataStorage storage = FindStorage(document, schema) ?? DataStorage.Create(document);
            storage.Name = StorageName;
            var remaining = ReadOverrides(document)
                .Where(item => !keys.Contains(item.OverrideKey))
                .ToList();
            var entity = new Entity(schema);
            entity.Set(schema.GetField(DataFieldName), Serialize(remaining));
            storage.SetEntity(entity);
            return remaining;
        }

        public static void ClearOverrides(Document document)
        {
            Schema schema = GetOrCreateSchema();
            DataStorage storage = FindStorage(document, schema) ?? DataStorage.Create(document);
            storage.Name = StorageName;
            var entity = new Entity(schema);
            entity.Set(schema.GetField(DataFieldName), string.Empty);
            storage.SetEntity(entity);
        }

        public static IReadOnlyList<ProjectZetaCatalogItem> ReadProjectCatalog(Document document)
        {
            Schema schema = GetOrCreateCatalogSchema();
            DataStorage? storage = FindStorage(document, schema);
            if (storage == null)
            {
                return Array.Empty<ProjectZetaCatalogItem>();
            }

            Entity entity = storage.GetEntity(schema);
            if (!entity.IsValid())
            {
                return Array.Empty<ProjectZetaCatalogItem>();
            }

            string serialized = entity.Get<string>(schema.GetField(CatalogFieldName)) ?? string.Empty;
            return DeserializeCatalog(serialized);
        }

        public static IReadOnlyList<ProjectZetaCatalogItem> SaveProjectCatalog(Document document, IEnumerable<ProjectZetaCatalogItem> catalogItems)
        {
            Schema schema = GetOrCreateCatalogSchema();
            DataStorage storage = FindStorage(document, schema) ?? DataStorage.Create(document);
            storage.Name = CatalogStorageName;

            var saved = catalogItems
                .Where(item => item.ProjectZeta.HasValue)
                .GroupBy(item => item.PathRole, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(item => item.PathRole, StringComparer.Ordinal)
                .ToList();

            var entity = new Entity(schema);
            entity.Set(schema.GetField(CatalogFieldName), SerializeCatalog(saved));
            storage.SetEntity(entity);
            return saved;
        }

        public static void ClearProjectCatalog(Document document)
        {
            Schema schema = GetOrCreateCatalogSchema();
            DataStorage storage = FindStorage(document, schema) ?? DataStorage.Create(document);
            storage.Name = CatalogStorageName;
            var entity = new Entity(schema);
            entity.Set(schema.GetField(CatalogFieldName), string.Empty);
            storage.SetEntity(entity);
        }

        public static string BuildOverrideKey(string systemName, long elementId, string pathRole, long? previousDuctElementId, long? nextDuctElementId)
        {
            return LocalResistanceCalculator.BuildOverrideKey(systemName, elementId, pathRole, previousDuctElementId, nextDuctElementId);
        }

        private static Schema GetOrCreateSchema()
        {
            Schema? schema = Schema.Lookup(SchemaGuid);
            if (schema != null)
            {
                return schema;
            }

            var builder = new SchemaBuilder(SchemaGuid);
            builder.SetSchemaName(SchemaName);
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField(DataFieldName, typeof(string));
            return builder.Finish();
        }

        private static Schema GetOrCreateCatalogSchema()
        {
            Schema? schema = Schema.Lookup(CatalogSchemaGuid);
            if (schema != null)
            {
                return schema;
            }

            var builder = new SchemaBuilder(CatalogSchemaGuid);
            builder.SetSchemaName(CatalogSchemaName);
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField(CatalogFieldName, typeof(string));
            return builder.Finish();
        }

        private static DataStorage? FindStorage(Document document, Schema schema)
        {
            return new FilteredElementCollector(document)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .FirstOrDefault(storage => storage.GetEntity(schema).IsValid());
        }

        private static string Serialize(IEnumerable<ZetaOverrideInfo> overrides)
        {
            return string.Join("\n", overrides.Select(item => string.Join("\t",
                Escape(item.SystemName),
                item.ElementId.ToString(CultureInfo.InvariantCulture),
                Escape(item.PathRole),
                item.PreviousDuctElementId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                item.NextDuctElementId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                item.Zeta.ToString("R", CultureInfo.InvariantCulture),
                Escape(item.OverrideKey))));
        }

        private static IReadOnlyList<ZetaOverrideInfo> Deserialize(string serialized)
        {
            if (string.IsNullOrWhiteSpace(serialized))
            {
                return Array.Empty<ZetaOverrideInfo>();
            }

            var result = new List<ZetaOverrideInfo>();
            foreach (string line in serialized.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = line.TrimEnd('\r').Split('\t');
                if (parts.Length < 7
                    || !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long elementId)
                    || !double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out double zeta))
                {
                    continue;
                }

                result.Add(new ZetaOverrideInfo
                {
                    SystemName = Unescape(parts[0]),
                    ElementId = elementId,
                    PathRole = Unescape(parts[2]),
                    PreviousDuctElementId = TryParseNullableLong(parts[3]),
                    NextDuctElementId = TryParseNullableLong(parts[4]),
                    Zeta = zeta,
                    OverrideKey = Unescape(parts[6])
                });
            }

            return result;
        }

        private static string SerializeCatalog(IEnumerable<ProjectZetaCatalogItem> catalogItems)
        {
            return string.Join("\n", catalogItems.Select(item => string.Join("\t",
                Escape(item.PathRole),
                item.AutoZeta.ToString("R", CultureInfo.InvariantCulture),
                item.ProjectZeta?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty)));
        }

        private static IReadOnlyList<ProjectZetaCatalogItem> DeserializeCatalog(string serialized)
        {
            if (string.IsNullOrWhiteSpace(serialized))
            {
                return Array.Empty<ProjectZetaCatalogItem>();
            }

            var result = new List<ProjectZetaCatalogItem>();
            foreach (string line in serialized.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = line.TrimEnd('\r').Split('\t');
                if (parts.Length < 3)
                {
                    continue;
                }

                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double autoZeta);
                double? projectZeta = double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedProjectZeta) ? parsedProjectZeta : null;
                result.Add(new ProjectZetaCatalogItem
                {
                    PathRole = Unescape(parts[0]),
                    AutoZeta = autoZeta,
                    ProjectZeta = projectZeta
                });
            }

            return result;
        }

        private static long? TryParseNullableLong(string value)
        {
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) ? parsed : null;
        }

        private static string Escape(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        private static string Unescape(string value)
        {
            return (value ?? string.Empty).Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t").Replace("\\\\", "\\");
        }
    }
}
