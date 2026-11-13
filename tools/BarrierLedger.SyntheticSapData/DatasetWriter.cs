using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace BarrierLedger.SyntheticSapData;

internal static class FileNames
{
    public const string FlCsv = "fl_master.csv";
    public const string FlCsv1256 = "fl_master.cp1256.csv";
    public const string FlXlsx = "fl_master.xlsx";
    public const string EquipmentCsv = "equipment_master.csv";
    public const string EquipmentCsv1256 = "equipment_master.cp1256.csv";
    public const string EquipmentXlsx = "equipment_master.xlsx";
    public const string Manifest = "manifest.json";

    /// <summary>Files whose bytes are fully determined by the seed on every platform.</summary>
    public static readonly string[] ByteStable = { FlCsv, FlCsv1256, EquipmentCsv, EquipmentCsv1256, Manifest };

    public static readonly string[] All =
    {
        FlCsv, FlCsv1256, FlXlsx, EquipmentCsv, EquipmentCsv1256, EquipmentXlsx, Manifest,
    };
}

internal static class DatasetWriter
{
    public const string GeneratorVersion = "1.0.0";

    /// <summary>Renders every output file in memory, keyed by file name.</summary>
    public static Dictionary<string, byte[]> Render(Dataset data)
    {
        List<string[]> flRows = data.EmittedFls.Select(f => f.ToFields()).ToList();
        List<string[]> equipmentRows = data.Equipment.Select(e => e.ToFields()).ToList();

        string flCsv = Csv.Format(Headers.Fl, flRows);
        string equipmentCsv = Csv.Format(Headers.Equipment, equipmentRows);

        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [FileNames.FlCsv] = Csv.ToUtf8WithBom(flCsv),
            [FileNames.FlCsv1256] = Encodings.Windows1256.GetBytes(flCsv),
            [FileNames.FlXlsx] = Xlsx.Write(Headers.Fl, flRows),
            [FileNames.EquipmentCsv] = Csv.ToUtf8WithBom(equipmentCsv),
            [FileNames.EquipmentCsv1256] = Encodings.Windows1256.GetBytes(equipmentCsv),
            [FileNames.EquipmentXlsx] = Xlsx.Write(Headers.Equipment, equipmentRows),
        };
        files[FileNames.Manifest] = RenderManifest(data, flRows.Count, equipmentRows.Count, files);
        return files;
    }

    public static void WriteTo(string directory, Dictionary<string, byte[]> files)
    {
        Directory.CreateDirectory(directory);
        foreach ((string name, byte[] bytes) in files)
        {
            File.WriteAllBytes(Path.Combine(directory, name), bytes);
        }
    }

    public static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static byte[] RenderManifest(Dataset data, int flRows, int equipmentRows, Dictionary<string, byte[]> files)
    {
        using var stream = new MemoryStream();
        var options = new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        using (var json = new Utf8JsonWriter(stream, options))
        {
            json.WriteStartObject();

            json.WriteStartObject("dataset");
            json.WriteString("name", Catalog.DatasetName);
            json.WriteBoolean("fictional", true);
            json.WriteString("notice",
                "Every name, code, tag, vendor and description in this dataset is invented. "
                + "It is not derived from any real company, plant or document.");
            json.WriteNumber("seed", data.Seed);
            json.WriteString("generator_version", GeneratorVersion);
            json.WriteEndObject();

            json.WriteStartObject("structure");
            json.WriteString("segment_separator", Catalog.Separator);
            json.WriteString("fl_code_mask", Catalog.FlCodeMask);
            json.WriteStartArray("segment_levels");
            WriteLevel(json, 1, 3, null, "Installation");
            WriteLevel(json, 2, 4, null, "Plant / Unit");
            WriteLevel(json, 3, 5, null, "Section / System");
            WriteLevel(json, 4, 5, 1, "Tag-level position (sub-level 1 of Section / System)");
            json.WriteEndArray();
            json.WriteStartArray("installations");
            foreach (Installation installation in Catalog.Installations)
            {
                json.WriteStartObject();
                json.WriteString("code", installation.Code);
                json.WriteString("name", installation.En);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteStartArray("plant_units");
            foreach (PlantUnit unit in Catalog.ProcessUnits)
            {
                json.WriteStartObject();
                json.WriteString("fl_code", DatasetBuilder.UnitFlCode(unit));
                json.WriteString("name", unit.En);
                json.WriteString("tag_prefix", unit.TagNo);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteString("building_subtree_root", Catalog.BuildingSubtreeRoot);
            json.WriteEndObject();

            json.WriteStartArray("files");
            WriteFile(json, FileNames.FlCsv, FileKind.Fl, "csv", "utf-8 with BOM", flRows, files);
            WriteFile(json, FileNames.FlCsv1256, FileKind.Fl, "csv", "windows-1256", flRows, files);
            WriteFile(json, FileNames.FlXlsx, FileKind.Fl, "xlsx", null, flRows, files);
            WriteFile(json, FileNames.EquipmentCsv, FileKind.Equipment, "csv", "utf-8 with BOM", equipmentRows, files);
            WriteFile(json, FileNames.EquipmentCsv1256, FileKind.Equipment, "csv", "windows-1256", equipmentRows, files);
            WriteFile(json, FileNames.EquipmentXlsx, FileKind.Equipment, "xlsx", null, equipmentRows, files);
            json.WriteEndArray();

            json.WriteStartObject("column_map");
            WriteColumnMap(json, FileKind.Fl, Headers.Fl, Headers.FlLogical);
            WriteColumnMap(json, FileKind.Equipment, Headers.Equipment, Headers.EquipmentLogical);
            json.WriteEndObject();

            json.WriteStartObject("counts");
            json.WriteNumber("fl_rows", flRows);
            json.WriteNumber("fl_distinct_keys", data.EmittedFls.Select(f => f.Code).Distinct(StringComparer.Ordinal).Count());
            json.WriteNumber("equipment_rows", equipmentRows);
            json.WriteNumber("equipment_distinct_keys",
                data.Equipment.Select(e => e.EquipmentNo).Distinct(StringComparer.Ordinal).Count());
            json.WriteEndObject();

            json.WriteStartObject("object_types");
            json.WriteStartArray("mapped");
            foreach (ObjectTypeInfo info in Catalog.MappedObjectTypes)
            {
                json.WriteStartObject();
                json.WriteString("code", info.Code);
                json.WriteString("suggested_class", info.SuggestedClass);
                json.WriteString("category", info.Category);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteStartArray("unmapped");
            foreach (string code in Catalog.UnmappedObjectTypes)
            {
                json.WriteStringValue(code);
            }

            json.WriteEndArray();
            json.WriteEndObject();

            json.WriteStartArray("seeded_defects");
            foreach (SeededDefect defect in data.Defects)
            {
                json.WriteStartObject();
                json.WriteString("id", defect.Id);
                json.WriteString("title", defect.Title);
                json.WriteStartArray("triggers");
                foreach (string trigger in defect.Triggers)
                {
                    json.WriteStringValue(trigger);
                }

                json.WriteEndArray();
                json.WriteNumber("expected_count", defect.ExpectedCount);
                json.WriteString("count_unit", defect.CountUnit);
                json.WriteString("expected", defect.Expected);
                json.WriteBoolean("rows_listed", defect.RowsListed);
                json.WriteStartArray("rows");
                foreach (DefectRow row in defect.Rows)
                {
                    json.WriteStartObject();
                    json.WriteString("file", row.File);
                    json.WriteString("key", row.Key);
                    if (row.Note is not null)
                    {
                        json.WriteString("note", row.Note);
                    }

                    json.WriteEndObject();
                }

                json.WriteEndArray();
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        // Utf8JsonWriter indents with the platform newline; normalise so the bytes match everywhere.
        string text = Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
        return Encodings.Utf8Strict.GetBytes(text);
    }

    private static void WriteLevel(Utf8JsonWriter json, int segment, int isoLevel, int? subLevel, string name)
    {
        json.WriteStartObject();
        json.WriteNumber("segment", segment);
        json.WriteNumber("iso14224_level", isoLevel);
        if (subLevel is not null)
        {
            json.WriteNumber("sub_level", subLevel.Value);
        }

        json.WriteString("name", name);
        json.WriteEndObject();
    }

    private static void WriteFile(
        Utf8JsonWriter json, string name, string kind, string format, string? encoding, int rows,
        Dictionary<string, byte[]> files)
    {
        json.WriteStartObject();
        json.WriteString("name", name);
        json.WriteString("kind", kind);
        json.WriteString("format", format);
        if (encoding is not null)
        {
            json.WriteString("encoding", encoding);
            json.WriteString("sha256", Sha256(files[name]));
        }

        json.WriteNumber("data_rows", rows);
        json.WriteEndObject();
    }

    private static void WriteColumnMap(Utf8JsonWriter json, string kind, string[] headers, string[] logical)
    {
        json.WriteStartObject(kind);
        for (int i = 0; i < headers.Length; i++)
        {
            json.WriteString(headers[i], logical[i]);
        }

        json.WriteEndObject();
    }
}
