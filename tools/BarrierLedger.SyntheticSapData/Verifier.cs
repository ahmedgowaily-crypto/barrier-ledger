using System.Text.Json;
using System.Text.RegularExpressions;

namespace BarrierLedger.SyntheticSapData;

/// <summary>
/// Checks a generated dataset against the spec. It works only from the output
/// files: every seeded condition is re-derived from the data and compared with
/// what the manifest claims, so a generator bug cannot hide behind its own
/// bookkeeping.
/// </summary>
internal sealed class Verifier
{
    private const int MinFlRows = 2700;
    private const int MaxFlRows = 3300;
    private const int MinEquipmentRows = 7200;
    private const int MaxEquipmentRows = 8800;
    private const int MinArabicPercent = 15;

    private readonly Dictionary<string, byte[]> _files;
    private readonly List<(string Name, bool Passed, string Detail)> _results = new();

    public Verifier(Dictionary<string, byte[]> files) => _files = files;

    public IReadOnlyList<(string Name, bool Passed, string Detail)> Results => _results;

    public bool AllPassed => _results.All(r => r.Passed);

    public static Dictionary<string, byte[]> Load(string directory)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (string name in FileNames.All)
        {
            string path = Path.Combine(directory, name);
            if (File.Exists(path))
            {
                files[name] = File.ReadAllBytes(path);
            }
        }

        return files;
    }

    public void Run()
    {
        try
        {
            RunChecks();
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or System.Xml.XmlException
            or System.Text.DecoderFallbackException or KeyNotFoundException or IndexOutOfRangeException
            or InvalidOperationException or ArgumentException)
        {
            // A file that cannot be read is a failed check, not a crash.
            Check("V00 files are readable and well-formed", false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private void RunChecks()
    {
        string[] missing = FileNames.All.Where(n => !_files.ContainsKey(n)).ToArray();
        Check("V01 all seven files are present", missing.Length == 0, string.Join(", ", missing));
        if (missing.Length > 0)
        {
            return;
        }

        List<string[]> flAll = Csv.Parse(Csv.FromUtf8WithBom(_files[FileNames.FlCsv]));
        List<string[]> eqAll = Csv.Parse(Csv.FromUtf8WithBom(_files[FileNames.EquipmentCsv]));

        Check("V02 headers match the documented SAP-style headers",
            flAll.Count > 0 && eqAll.Count > 0
            && flAll[0].SequenceEqual(Headers.Fl) && eqAll[0].SequenceEqual(Headers.Equipment));

        List<string[]> fl = flAll.Skip(1).ToList();
        List<string[]> eq = eqAll.Skip(1).ToList();
        Check("V03 every row has the full column count",
            fl.All(r => r.Length == Headers.Fl.Length) && eq.All(r => r.Length == Headers.Equipment.Length));

        using JsonDocument manifest = JsonDocument.Parse(_files[FileNames.Manifest]);
        JsonElement root = manifest.RootElement;

        Check("V04 row counts are within 10% of 3,000 FLs and 8,000 equipment",
            fl.Count is >= MinFlRows and <= MaxFlRows && eq.Count is >= MinEquipmentRows and <= MaxEquipmentRows,
            $"FL rows {fl.Count}, equipment rows {eq.Count}");
        JsonElement counts = root.GetProperty("counts");
        Check("V05 manifest row counts match the files",
            counts.GetProperty("fl_rows").GetInt32() == fl.Count
            && counts.GetProperty("equipment_rows").GetInt32() == eq.Count);

        CheckOtherFormats(flAll, eqAll);
        CheckHashes(root);

        Check("V09 Arabic and English descriptions are both present in each file",
            HasMixedLanguages(fl, 1) && HasMixedLanguages(eq, 1),
            $"Arabic share: FL {ArabicPercent(fl, 1)}%, equipment {ArabicPercent(eq, 1)}%");

        // Column positions (see Headers).
        const int FlCode = 0, FlDesc = 1, FlSuperior = 2, FlPlanner = 6, FlWorkCenter = 7, FlStatus = 8, FlAbc = 9;
        const int EqNo = 0, EqDesc = 1, EqFl = 2, EqType = 3, EqSort = 4, EqMaker = 5, EqModel = 6,
            EqStatus = 8, EqAbc = 9, EqPlanner = 10, EqWorkCenter = 11;

        var flByCode = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (string[] row in fl)
        {
            flByCode.TryAdd(row[FlCode], row);
        }

        var eqByNo = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (string[] row in eq)
        {
            eqByNo.TryAdd(row[EqNo], row);
        }

        string separator = root.GetProperty("structure").GetProperty("segment_separator").GetString()!;
        string buildingRoot = root.GetProperty("structure").GetProperty("building_subtree_root").GetString()!;
        var mask = new Regex(root.GetProperty("structure").GetProperty("fl_code_mask").GetString()!, RegexOptions.CultureInvariant);

        string Parent(string code)
        {
            int cut = code.LastIndexOf(separator, StringComparison.Ordinal);
            return cut < 0 ? "" : code[..cut];
        }

        int Segments(string code) => code.Split(separator).Length;
        bool InBuilding(string code) => code == buildingRoot || code.StartsWith(buildingRoot + separator, StringComparison.Ordinal);

        // Structure.
        int installations = flByCode.Keys.Count(c => Segments(c) == 1);
        int plantUnits = flByCode.Keys.Count(c => Segments(c) == 2 && !InBuilding(c));
        Check("V10 one refinery with 2 installations and 6 process plant/units",
            installations == 2 && plantUnits == 6, $"installations {installations}, plant/units {plantUnits}");

        bool hierarchyConsistent = fl.All(r => r[FlSuperior].Length == 0
            || (r[FlSuperior] == Parent(r[FlCode]) && flByCode.ContainsKey(r[FlSuperior])));
        Check("V11 every non-blank superior FL exists and equals the code minus its last segment", hierarchyConsistent);

        Dictionary<string, HashSet<string>> claimed = ReadDefects(root, out Dictionary<string, int> expectedCounts);

        // S01 status.
        bool Flagged(string status) => status.Contains("DLFL", StringComparison.Ordinal) || status.Contains("INAC", StringComparison.Ordinal);
        var s01 = new HashSet<string>(StringComparer.Ordinal);
        s01.UnionWith(flByCode.Values.Where(r => Flagged(r[FlStatus])).Select(r => Key(FileKind.Fl, r[FlCode])));
        s01.UnionWith(eqByNo.Values.Where(r => Flagged(r[EqStatus])).Select(r => Key(FileKind.Equipment, r[EqNo])));
        Compare("V12 S01 exactly 40 objects carry DLFL or INAC", s01, claimed, "S01", DefectSeeder.StatusObjects);
        Check("V13 S01 splits into 8 functional locations and 32 equipment",
            s01.Count(k => k.StartsWith(FileKind.Fl + ":", StringComparison.Ordinal)) == DefectSeeder.StatusFls);

        // S02 blank superior.
        var s02 = flByCode.Values
            .Where(r => r[FlSuperior].Length == 0 && Segments(r[FlCode]) > 1)
            .Select(r => Key(FileKind.Fl, r[FlCode]))
            .ToHashSet(StringComparer.Ordinal);
        Compare("V14 S02 exactly 25 non-root FLs have a blank superior", s02, claimed, "S02", DefectSeeder.BlankSuperiorFls);
        int missingParents = flByCode.Values.Count(r => r[FlSuperior].Length == 0 && Segments(r[FlCode]) > 1
            && !flByCode.ContainsKey(Parent(r[FlCode])));
        int distinctMissingParents = flByCode.Values
            .Where(r => r[FlSuperior].Length == 0 && Segments(r[FlCode]) > 1 && !flByCode.ContainsKey(Parent(r[FlCode])))
            .Select(r => Parent(r[FlCode])).Distinct(StringComparer.Ordinal).Count();
        Check("V15 S02 for 3 of them the derived parent is absent (one placeholder needed)",
            missingParents == DefectSeeder.BlankSuperiorWithMissingParent && distinctMissingParents == 1,
            $"rows {missingParents}, distinct parents {distinctMissingParents}");

        // S03 orphans.
        var s03 = eqByNo.Values
            .Where(r => !flByCode.ContainsKey(r[EqFl]))
            .Select(r => Key(FileKind.Equipment, r[EqNo]))
            .ToHashSet(StringComparer.Ordinal);
        Compare("V16 S03 exactly 15 equipment point at an FL missing from File A", s03, claimed, "S03", DefectSeeder.OrphanEquipment);
        Check("V17 S03 no orphan has a blank installed-at FL", eq.All(r => r[EqFl].Length > 0));

        // S04 duplicate keys.
        var s04 = new HashSet<string>(StringComparer.Ordinal);
        var dupFl = fl.GroupBy(r => r[FlCode], StringComparer.Ordinal).Where(g => g.Count() > 1).ToList();
        var dupEq = eq.GroupBy(r => r[EqNo], StringComparer.Ordinal).Where(g => g.Count() > 1).ToList();
        s04.UnionWith(dupFl.Select(g => Key(FileKind.Fl, g.Key)));
        s04.UnionWith(dupEq.Select(g => Key(FileKind.Equipment, g.Key)));
        Compare("V18 S04 exactly 10 keys are duplicated", s04, claimed, "S04",
            DefectSeeder.DuplicateFlKeys + DefectSeeder.DuplicateEquipmentKeys);
        Check("V19 S04 splits into 4 FL keys and 6 equipment keys, each appearing exactly twice",
            dupFl.Count == DefectSeeder.DuplicateFlKeys && dupEq.Count == DefectSeeder.DuplicateEquipmentKeys
            && dupFl.All(g => g.Count() == 2) && dupEq.All(g => g.Count() == 2));

        // S05 conflicts.
        var s05 = new HashSet<string>(StringComparer.Ordinal);
        foreach (string[] row in eqByNo.Values)
        {
            if (!flByCode.TryGetValue(row[EqFl], out string[]? parent))
            {
                continue;
            }

            if (Differs(parent[FlPlanner], row[EqPlanner]) || Differs(parent[FlWorkCenter], row[EqWorkCenter])
                || Differs(parent[FlAbc], row[EqAbc]))
            {
                s05.Add(Key(FileKind.Equipment, row[EqNo]));
            }
        }

        Compare("V20 S05 exactly 3 equipment conflict with their FL", s05, claimed, "S05", DefectSeeder.AttributeConflicts);

        // S06 unmapped object types.
        var mapped = root.GetProperty("object_types").GetProperty("mapped").EnumerateArray()
            .Select(e => e.GetProperty("code").GetString()!).ToHashSet(StringComparer.Ordinal);
        var declaredUnmapped = root.GetProperty("object_types").GetProperty("unmapped").EnumerateArray()
            .Select(e => e.GetString()!).ToHashSet(StringComparer.Ordinal);
        var foundUnmapped = eq.Select(r => r[EqType]).Where(t => !mapped.Contains(t)).ToHashSet(StringComparer.Ordinal);
        Check("V21 S06 exactly 12 distinct object types are outside the mapped list",
            foundUnmapped.Count == DefectSeeder.UnmappedObjectTypes && foundUnmapped.SetEquals(declaredUnmapped)
            && expectedCounts["S06"] == DefectSeeder.UnmappedObjectTypes,
            string.Join(", ", foundUnmapped.Order(StringComparer.Ordinal)));
        var s06 = eqByNo.Values.Where(r => !mapped.Contains(r[EqType])).Select(r => Key(FileKind.Equipment, r[EqNo]))
            .ToHashSet(StringComparer.Ordinal);
        Check("V22 S06 the manifest lists every equipment carrying an unmapped type", s06.SetEquals(claimed["S06"]),
            $"{s06.Count} equipment");
        Check("V23 no object type is blank", eq.All(r => r[EqType].Length > 0));

        // S07 building subtree.
        var s07 = flByCode.Keys.Where(InBuilding).Select(c => Key(FileKind.Fl, c)).ToHashSet(StringComparer.Ordinal);
        Check("V24 S07 the building subtree is present and listed in full",
            s07.Count > 1 && s07.SetEquals(claimed["S07"]) && eq.Any(r => InBuilding(r[EqFl])),
            $"{s07.Count} functional locations");

        // S08 functional locations with nothing installed below them.
        var populated = new HashSet<string>(StringComparer.Ordinal);
        foreach (string[] row in eq)
        {
            string code = row[EqFl];
            while (code.Length > 0)
            {
                populated.Add(code);
                code = Parent(code);
            }
        }

        var s08 = flByCode.Keys.Where(c => !populated.Contains(c)).Select(c => Key(FileKind.Fl, c))
            .ToHashSet(StringComparer.Ordinal);
        Compare("V25 S08 exactly 31 FLs have no equipment on themselves or below", s08, claimed, "S08",
            DefectSeeder.EmptyLeafFls + DefectSeeder.EmptySectionPositions + 1);

        // S09 mask.
        var s09 = flByCode.Keys.Where(c => !mask.IsMatch(c)).Select(c => Key(FileKind.Fl, c)).ToHashSet(StringComparer.Ordinal);
        Compare("V26 S09 exactly 5 FL codes break the structure mask", s09, claimed, "S09", DefectSeeder.MaskViolations);

        // S10 short descriptions.
        var s10 = new HashSet<string>(StringComparer.Ordinal);
        s10.UnionWith(flByCode.Values.Where(r => r[FlDesc].Trim().Length <= 3).Select(r => Key(FileKind.Fl, r[FlCode])));
        s10.UnionWith(eqByNo.Values.Where(r => r[EqDesc].Trim().Length <= 3).Select(r => Key(FileKind.Equipment, r[EqNo])));
        Compare("V27 S10 exactly 24 descriptions are 3 characters or fewer", s10, claimed, "S10",
            DefectSeeder.ShortDescriptionEquipment + DefectSeeder.ShortDescriptionFls);

        // S11 duplicate tags.
        var tagGroups = eqByNo.Values.Where(r => r[EqSort].Length > 0)
            .GroupBy(r => r[EqSort], StringComparer.Ordinal).Where(g => g.Count() > 1).ToList();
        var s11 = tagGroups.SelectMany(g => g).Select(r => Key(FileKind.Equipment, r[EqNo])).ToHashSet(StringComparer.Ordinal);
        Compare("V28 S11 exactly 8 equipment share a sort field", s11, claimed, "S11", DefectSeeder.DuplicateTagPairs * 2);
        Check("V29 S11 that is 4 tags, each on two equipment numbers",
            tagGroups.Count == DefectSeeder.DuplicateTagPairs && tagGroups.All(g => g.Count() == 2));

        // S12 blank sort field.
        var s12 = eqByNo.Values.Where(r => r[EqSort].Length == 0).Select(r => Key(FileKind.Equipment, r[EqNo]))
            .ToHashSet(StringComparer.Ordinal);
        Compare("V30 S12 equipment with a blank sort field match the manifest", s12, claimed, "S12", expectedCounts["S12"]);
        Check("V31 S12 there are some, but under 10% of equipment", s12.Count > 0 && s12.Count < eqByNo.Count / 10,
            $"{s12.Count} equipment");

        // S13 unknown status.
        var s13 = eqByNo.Values.Where(r => !r[EqStatus].Contains("CRTE", StringComparison.Ordinal))
            .Select(r => Key(FileKind.Equipment, r[EqNo])).ToHashSet(StringComparer.Ordinal);
        Compare("V32 S13 exactly 12 equipment have a status outside the default map", s13, claimed, "S13",
            DefectSeeder.UnknownStatusEquipment);
        Check("V33 every FL status contains CRTE", fl.All(r => r[FlStatus].Contains("CRTE", StringComparison.Ordinal)));

        // S14 missing manufacturer.
        int s14 = eqByNo.Values.Count(r => r[EqMaker].Length == 0 && r[EqModel].Length == 0);
        Check("V34 S14 count of equipment with no manufacturer and model matches the manifest",
            s14 == expectedCounts["S14"] && s14 > 0, $"{s14} equipment");
        Check("V35 manufacturer and model are always both present or both blank",
            eq.All(r => (r[EqMaker].Length == 0) == (r[EqModel].Length == 0)));

        // Cross-checks on the manifest itself.
        var allKeys = new HashSet<string>(StringComparer.Ordinal);
        allKeys.UnionWith(flByCode.Keys.Select(c => Key(FileKind.Fl, c)));
        allKeys.UnionWith(eqByNo.Keys.Select(n => Key(FileKind.Equipment, n)));
        Check("V36 every row the manifest lists exists in the files",
            claimed.Values.All(set => set.IsSubsetOf(allKeys)));

        string[] ids = claimed.Keys.Order(StringComparer.Ordinal).ToArray();
        var overlaps = new List<string>();
        for (int i = 0; i < ids.Length; i++)
        {
            for (int j = i + 1; j < ids.Length; j++)
            {
                if (claimed[ids[i]].Overlaps(claimed[ids[j]]))
                {
                    overlaps.Add($"{ids[i]}/{ids[j]}");
                }
            }
        }

        Check("V37 no row carries two listed defects", overlaps.Count == 0, string.Join(", ", overlaps));
        Check("V38 the manifest declares all 14 seeded conditions",
            expectedCounts.Count == 14 && Enumerable.Range(1, 14).All(n => expectedCounts.ContainsKey($"S{n:00}")));

        // Rows outside every listed defect must be clean.
        var seeded = claimed.Values.SelectMany(s => s).ToHashSet(StringComparer.Ordinal);
        bool cleanTagsUnique = eqByNo.Values
            .Where(r => r[EqSort].Length > 0 && !seeded.Contains(Key(FileKind.Equipment, r[EqNo])))
            .GroupBy(r => r[EqSort], StringComparer.Ordinal).All(g => g.Count() == 1);
        Check("V39 sort fields are unique among equipment with no seeded defect", cleanTagsUnique);
    }

    private static string Key(string file, string key) => file + ":" + key;

    private static bool Differs(string flValue, string equipmentValue) =>
        flValue.Length > 0 && equipmentValue.Length > 0 && flValue != equipmentValue;

    private static bool IsArabic(string text) => text.Any(c => c >= '؀' && c <= 'ۿ');

    private static int ArabicPercent(List<string[]> rows, int column) =>
        rows.Count == 0 ? 0 : rows.Count(r => IsArabic(r[column])) * 100 / rows.Count;

    private static bool HasMixedLanguages(List<string[]> rows, int column)
    {
        int percent = ArabicPercent(rows, column);
        return percent >= MinArabicPercent && percent <= 100 - MinArabicPercent;
    }

    private static Dictionary<string, HashSet<string>> ReadDefects(JsonElement root, out Dictionary<string, int> expectedCounts)
    {
        var claimed = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        expectedCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (JsonElement defect in root.GetProperty("seeded_defects").EnumerateArray())
        {
            string id = defect.GetProperty("id").GetString()!;
            expectedCounts[id] = defect.GetProperty("expected_count").GetInt32();
            if (!defect.GetProperty("rows_listed").GetBoolean())
            {
                continue;
            }

            claimed[id] = defect.GetProperty("rows").EnumerateArray()
                .Select(r => Key(r.GetProperty("file").GetString()!, r.GetProperty("key").GetString()!))
                .ToHashSet(StringComparer.Ordinal);
        }

        return claimed;
    }

    private void CheckOtherFormats(List<string[]> flAll, List<string[]> eqAll)
    {
        List<string[]> flXlsx = Xlsx.Read(_files[FileNames.FlXlsx], Headers.Fl.Length);
        List<string[]> eqXlsx = Xlsx.Read(_files[FileNames.EquipmentXlsx], Headers.Equipment.Length);
        Check("V06 each .xlsx holds exactly the same cells as its .csv",
            SameTable(flAll, flXlsx) && SameTable(eqAll, eqXlsx));

        bool sameFl, sameEq;
        try
        {
            sameFl = Encodings.Windows1256.GetString(_files[FileNames.FlCsv1256]) == Csv.FromUtf8WithBom(_files[FileNames.FlCsv]);
            sameEq = Encodings.Windows1256.GetString(_files[FileNames.EquipmentCsv1256])
                == Csv.FromUtf8WithBom(_files[FileNames.EquipmentCsv]);
        }
        catch (System.Text.DecoderFallbackException)
        {
            sameFl = false;
            sameEq = false;
        }

        Check("V07 each Windows-1256 .csv decodes to exactly the UTF-8 text", sameFl && sameEq);
    }

    private void CheckHashes(JsonElement root)
    {
        bool ok = true;
        foreach (JsonElement file in root.GetProperty("files").EnumerateArray())
        {
            if (file.TryGetProperty("sha256", out JsonElement hash))
            {
                ok &= DatasetWriter.Sha256(_files[file.GetProperty("name").GetString()!]) == hash.GetString();
            }
        }

        Check("V08 SHA-256 of each .csv matches the manifest", ok);
    }

    private static bool SameTable(List<string[]> a, List<string[]> b) =>
        a.Count == b.Count && a.Zip(b).All(pair => pair.First.SequenceEqual(pair.Second));

    private void Compare(
        string name, HashSet<string> derived, Dictionary<string, HashSet<string>> claimed, string id, int expected)
    {
        bool passed = derived.Count == expected && claimed.TryGetValue(id, out HashSet<string>? listed)
            && derived.SetEquals(listed);
        string detail = $"found {derived.Count}, expected {expected}";
        if (!passed && claimed.TryGetValue(id, out HashSet<string>? manifestRows))
        {
            var onlyInFiles = derived.Except(manifestRows).Take(5);
            var onlyInManifest = manifestRows.Except(derived).Take(5);
            detail += $"; only in files: [{string.Join(", ", onlyInFiles)}]; only in manifest: [{string.Join(", ", onlyInManifest)}]";
        }

        Check(name, passed, detail);
    }

    private void Check(string name, bool passed, string detail = "") => _results.Add((name, passed, detail));
}
