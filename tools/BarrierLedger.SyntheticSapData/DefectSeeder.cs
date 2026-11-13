using System.Text.RegularExpressions;

namespace BarrierLedger.SyntheticSapData;

/// <summary>
/// Plants the defects listed in section 10 of the SAP import spec, plus a few
/// extra conditions that exercise rules the spec does not seed explicitly.
/// Every planted row is locked so that no row carries two defects, which keeps
/// the expected counts exact.
/// </summary>
internal sealed class DefectSeeder
{
    // Counts fixed by the spec (section 10).
    public const int StatusObjects = 40;
    public const int StatusFls = 8;
    public const int BlankSuperiorFls = 25;
    public const int BlankSuperiorWithMissingParent = 3;
    public const int OrphanEquipment = 15;
    public const int DuplicateFlKeys = 4;
    public const int DuplicateEquipmentKeys = 6;
    public const int AttributeConflicts = 3;
    public const int UnmappedObjectTypes = 12;

    // Extra conditions chosen for this dataset (see the manifest and README).
    public const int EmptyLeafFls = 26;
    public const int EmptySectionPositions = 4;
    public const int MaskViolations = 5;
    public const int ShortDescriptionEquipment = 18;
    public const int ShortDescriptionFls = 6;
    public const int DuplicateTagPairs = 4;
    public const int UnknownStatusEquipment = 12;
    public const int BlankSortFieldPercent = 5;
    public const int OrphanFlCodes = 10;

    private static readonly Regex LastSegment = new(@"^([A-Z]+)(\d{3})([A-Z]?)$", RegexOptions.CultureInvariant);

    private readonly DatasetBuilder _builder;
    private readonly Prng _rng;
    private readonly Dataset _data;
    private readonly Dictionary<FlRow, List<EquipRow>> _equipmentAt = new();

    public DefectSeeder(DatasetBuilder builder)
    {
        _builder = builder;
        _rng = builder.Rng;
        _data = builder.Data;
    }

    public void Apply()
    {
        // Structures that must exist before the per-row index is built.
        List<FlRow> missingParentChildren = AddSectionWithMissingParentRow();
        List<FlRow> emptySection = AddEmptySection();
        IndexEquipment();

        SeedEmptyFls(emptySection);
        SeedStatus();
        SeedBlankSuperior(missingParentChildren);
        SeedDuplicateKeys();
        SeedConflicts();
        SeedUnmappedObjectTypes();
        SeedMaskViolations();
        SeedShortDescriptions();
        SeedDuplicateTags();
        SeedUnknownStatus();
        SeedBlankSortField();
        SeedOrphans();
        RecordBuildingSubtree();
        RecordMissingManufacturer();

        _data.Defects.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
    }

    private static PlantUnit Unit(string code) => Catalog.ProcessUnits.Single(u => u.Code == code);

    private static PositionType Type(string prefix) => Catalog.ProcessPositionTypes.Single(t => t.Prefix == prefix);

    private List<FlRow> AddSectionWithMissingParentRow()
    {
        // Section TZ1-UTL-S11 is deliberately left out of File A, and its three
        // children carry a blank superior: C3 has to create a placeholder parent.
        PlantUnit unit = Unit("UTL");
        FlRow section = _builder.AddSection(unit, 11, "Chemical Storage", "تخزين الكيماويات", emit: false);
        var children = new List<FlRow>();
        foreach (string prefix in new[] { "P", "V", "PSV" })
        {
            PositionType type = Type(prefix);
            FlRow child = _builder.AddPosition(unit, section, type, _builder.NextNumber(unit, prefix), "");
            child.SuperiorFl = "";
            child.Locked = true;
            children.Add(child);
        }

        return children;
    }

    private List<FlRow> AddEmptySection()
    {
        // A whole section with no equipment anywhere below it: C4 must remove
        // the section and its positions, not only leaf nodes.
        PlantUnit unit = Unit("TKF");
        FlRow section = _builder.AddSection(unit, 11, "Decommissioned Area", "منطقة خارج الخدمة");
        var rows = new List<FlRow> { section };
        foreach (string prefix in new[] { "T", "T", "P", "MOV" })
        {
            PositionType type = Type(prefix);
            FlRow position = _builder.AddPosition(
                unit, section, type, _builder.NextNumber(unit, prefix), "", withEquipment: false);
            position.Locked = true;
            rows.Add(position);
        }

        return rows;
    }

    private void IndexEquipment()
    {
        foreach (FlRow fl in _data.Fls.Where(f => f.IsPosition))
        {
            _equipmentAt[fl] = new List<EquipRow>();
        }

        foreach (EquipRow equipment in _data.Equipment)
        {
            if (equipment.Position is not null)
            {
                _equipmentAt[equipment.Position].Add(equipment);
            }
        }
    }

    private List<FlRow> FreePositions(Func<FlRow, bool>? filter = null)
    {
        var list = _data.Fls
            .Where(f => f.IsPosition && f.Emit && !f.Locked && f.UnitCode != Catalog.BuildingUnitCode)
            .Where(f => filter is null || filter(f))
            .ToList();
        _rng.Shuffle(list);
        return list;
    }

    private List<EquipRow> FreeEquipment(Func<EquipRow, bool>? filter = null)
    {
        var list = _data.Equipment
            .Where(e => !e.Locked && e.Position is { Locked: false } p && p.UnitCode != Catalog.BuildingUnitCode)
            .Where(e => filter is null || filter(e))
            .ToList();
        _rng.Shuffle(list);
        return list;
    }

    private static List<T> Take<T>(List<T> candidates, int count, string what)
    {
        if (candidates.Count < count)
        {
            throw new InvalidOperationException($"Not enough candidates for {what}: {candidates.Count} < {count}.");
        }

        return candidates.GetRange(0, count);
    }

    /// <summary>Takes equipment from distinct positions and locks both the row and its position.</summary>
    private List<EquipRow> TakeEquipment(int count, string what, Func<EquipRow, bool>? filter = null)
    {
        var taken = new List<EquipRow>();
        foreach (EquipRow candidate in FreeEquipment(filter))
        {
            if (candidate.Position!.Locked)
            {
                continue;
            }

            candidate.Locked = true;
            candidate.Position.Locked = true;
            taken.Add(candidate);
            if (taken.Count == count)
            {
                return taken;
            }
        }

        throw new InvalidOperationException($"Not enough candidates for {what}: {taken.Count} < {count}.");
    }

    private List<FlRow> TakePositions(int count, string what, Func<FlRow, bool>? filter = null)
    {
        List<FlRow> taken = Take(FreePositions(filter), count, what);
        foreach (FlRow position in taken)
        {
            position.Locked = true;
        }

        return taken;
    }

    private SeededDefect Add(string id, string title, string[] triggers, string unit, string expected, int count)
    {
        var defect = new SeededDefect
        {
            Id = id,
            Title = title,
            Triggers = triggers,
            CountUnit = unit,
            Expected = expected,
            ExpectedCount = count,
        };
        _data.Defects.Add(defect);
        return defect;
    }

    private void SeedEmptyFls(List<FlRow> emptySection)
    {
        SeededDefect defect = Add(
            "S08", "Functional locations with no equipment on themselves or any descendant",
            new[] { "C4" }, "functional_locations",
            "C4 excludes exactly these rows; every ancestor that still has equipment below it is kept.",
            EmptyLeafFls + emptySection.Count);

        foreach (FlRow position in TakePositions(EmptyLeafFls, "empty positions"))
        {
            foreach (EquipRow equipment in _equipmentAt[position])
            {
                _data.Equipment.Remove(equipment);
            }

            _equipmentAt[position].Clear();
            defect.Rows.Add(new DefectRow(FileKind.Fl, position.Code, "leaf position"));
        }

        foreach (FlRow row in emptySection)
        {
            defect.Rows.Add(new DefectRow(FileKind.Fl, row.Code, row.IsPosition ? "position in empty section" : "empty section"));
        }
    }

    private void SeedStatus()
    {
        SeededDefect defect = Add(
            "S01", "Objects whose system status carries DLFL or INAC",
            new[] { "C2" }, "objects",
            "C2 excludes exactly these 40 rows (8 functional locations and 32 equipment).",
            StatusObjects);

        // Eight decommissioned positions: the FL and its single equipment are both flagged.
        List<FlRow> positions = TakePositions(StatusFls, "flagged positions", p => _equipmentAt[p].Count == 1);
        for (int i = 0; i < positions.Count; i++)
        {
            string status = i < StatusFls / 2 ? SapStatus.Deleted : SapStatus.Inactive;
            EquipRow equipment = _equipmentAt[positions[i]][0];
            positions[i].SystemStatus = status;
            equipment.SystemStatus = status;
            equipment.Locked = true;
            defect.Rows.Add(new DefectRow(FileKind.Fl, positions[i].Code, status));
            defect.Rows.Add(new DefectRow(FileKind.Equipment, equipment.EquipmentNo, status));
        }

        // The rest are single equipment at positions that keep other active equipment.
        int standalone = StatusObjects - (2 * StatusFls);
        List<EquipRow> items = TakeEquipment(standalone, "flagged equipment", e => _equipmentAt[e.Position!].Count >= 2);
        for (int i = 0; i < items.Count; i++)
        {
            string status = i < standalone / 2 ? SapStatus.Deleted : SapStatus.Inactive;
            items[i].SystemStatus = status;
            defect.Rows.Add(new DefectRow(FileKind.Equipment, items[i].EquipmentNo, status));
        }
    }

    private void SeedBlankSuperior(List<FlRow> missingParentChildren)
    {
        SeededDefect defect = Add(
            "S02", "Functional locations with a blank superior FL",
            new[] { "C3", "D7" }, "functional_locations",
            "C3 derives the parent of all 25 from the FL code. For 22 the parent exists in File A. "
            + "For 3 the parent TZ1-UTL-S11 is absent, so one placeholder FL flagged DERIVED is created with a warning. "
            + "The two installation roots also have a blank superior and are not part of this count.",
            BlankSuperiorFls);

        foreach (FlRow position in TakePositions(BlankSuperiorFls - BlankSuperiorWithMissingParent, "blank superior"))
        {
            position.SuperiorFl = "";
            defect.Rows.Add(new DefectRow(FileKind.Fl, position.Code, "parent exists in File A"));
        }

        foreach (FlRow child in missingParentChildren)
        {
            defect.Rows.Add(new DefectRow(FileKind.Fl, child.Code, "parent absent from File A"));
        }
    }

    private void SeedDuplicateKeys()
    {
        SeededDefect defect = Add(
            "S04", "Keys that appear twice in their file",
            new[] { "D1" }, "keys",
            "D1 raises one Error issue per duplicated key: 4 in File A and 6 in File B. Commit is blocked until resolved.",
            DuplicateFlKeys + DuplicateEquipmentKeys);

        foreach (FlRow position in TakePositions(DuplicateFlKeys, "duplicate FL keys"))
        {
            FlRow copy = position.Clone();
            copy.IsPosition = false;
            _data.Fls.Add(copy);
            defect.Rows.Add(new DefectRow(FileKind.Fl, position.Code, "identical second row"));
        }

        List<EquipRow> items = TakeEquipment(DuplicateEquipmentKeys, "duplicate equipment keys");
        for (int i = 0; i < items.Count; i++)
        {
            EquipRow copy = items[i].Clone();
            copy.Position = null;
            bool altered = i < DuplicateEquipmentKeys / 2;
            if (altered)
            {
                copy.Description += " (OLD)";
            }

            _data.Equipment.Add(copy);
            defect.Rows.Add(new DefectRow(
                FileKind.Equipment, items[i].EquipmentNo,
                altered ? "second row with a different description" : "identical second row"));
        }
    }

    private void SeedConflicts()
    {
        SeededDefect defect = Add(
            "S05", "Equipment whose attribute differs from the value on its functional location",
            new[] { "C5" }, "equipment",
            "C5 keeps the equipment value and records both values in an INFO issue.",
            AttributeConflicts);

        List<EquipRow> items = TakeEquipment(AttributeConflicts, "attribute conflicts");

        EquipRow byPlanner = items[0];
        string otherPlanner = Catalog.ProcessUnits
            .Select(DatasetBuilder.PlannerGroupOf)
            .First(g => g != byPlanner.Position!.PlannerGroup);
        defect.Rows.Add(Conflict(byPlanner, "planner_group", byPlanner.Position!.PlannerGroup, otherPlanner));
        byPlanner.PlannerGroup = otherPlanner;

        EquipRow byWorkCenter = items[1];
        string flWorkCenter = byWorkCenter.Position!.MainWorkCenter;
        string[] parts = flWorkCenter.Split('-');
        string otherDiscipline = new[] { "MECH", "ELEC", "INST", "STAT" }.First(d => d != parts[0]);
        string otherWorkCenter = $"{otherDiscipline}-{parts[1]}";
        defect.Rows.Add(Conflict(byWorkCenter, "main_work_center", flWorkCenter, otherWorkCenter));
        byWorkCenter.MainWorkCenter = otherWorkCenter;

        EquipRow byAbc = items[2];
        string flAbc = byAbc.Position!.AbcIndicator;
        string otherAbc = new[] { "A", "B", "C" }.First(a => a != flAbc);
        defect.Rows.Add(Conflict(byAbc, "abc_indicator", flAbc, otherAbc));
        byAbc.AbcIndicator = otherAbc;
    }

    private static DefectRow Conflict(EquipRow equipment, string attribute, string flValue, string equipmentValue) =>
        new(FileKind.Equipment, equipment.EquipmentNo, $"{attribute}: FL={flValue}, equipment={equipmentValue}");

    private void SeedUnmappedObjectTypes()
    {
        SeededDefect defect = Add(
            "S06", "Equipment carrying an object type that no default map knows",
            new[] { "D5" }, "object_types",
            "The mapping screen lists these 12 object types as unmapped. Commit is allowed; "
            + "the assets load as UNCLASSIFIED and lower the D5 pass rate.",
            UnmappedObjectTypes);

        if (Catalog.UnmappedObjectTypes.Length != UnmappedObjectTypes)
        {
            throw new InvalidOperationException("Catalog must list exactly 12 unmapped object types.");
        }

        foreach (string objectType in Catalog.UnmappedObjectTypes)
        {
            foreach (EquipRow equipment in TakeEquipment(_rng.Next(8, 30), "unmapped object type " + objectType))
            {
                equipment.ObjectType = objectType;
                defect.Rows.Add(new DefectRow(FileKind.Equipment, equipment.EquipmentNo, objectType));
            }
        }
    }

    private void SeedMaskViolations()
    {
        SeededDefect defect = Add(
            "S09", "Functional location codes that break the declared structure mask",
            new[] { "D9" }, "functional_locations",
            "D9 flags exactly these codes: the last segment starts with digits instead of letters.",
            MaskViolations);

        foreach (FlRow position in TakePositions(MaskViolations, "mask violations", p => _equipmentAt[p].Count > 0))
        {
            int cut = position.Code.LastIndexOf(Catalog.Separator, StringComparison.Ordinal);
            Match match = LastSegment.Match(position.Code[(cut + 1)..]);
            if (!match.Success)
            {
                throw new InvalidOperationException($"Unexpected position code '{position.Code}'.");
            }

            string original = position.Code;
            position.Code = $"{position.Code[..cut]}{Catalog.Separator}{match.Groups[2].Value}{match.Groups[1].Value}{match.Groups[3].Value}";
            foreach (EquipRow equipment in _equipmentAt[position])
            {
                equipment.InstalledAtFl = position.Code;
                equipment.Locked = true;
            }

            defect.Rows.Add(new DefectRow(FileKind.Fl, position.Code, $"well-formed code would be {original}"));
        }
    }

    private void SeedShortDescriptions()
    {
        SeededDefect defect = Add(
            "S10", "Descriptions of three characters or fewer",
            new[] { "D4" }, "rows",
            "D4 fails for exactly these rows: 18 equipment and 6 functional locations.",
            ShortDescriptionEquipment + ShortDescriptionFls);

        List<EquipRow> items = TakeEquipment(ShortDescriptionEquipment, "short equipment descriptions");
        for (int i = 0; i < items.Count; i++)
        {
            items[i].Description = Catalog.ShortDescriptions[i % Catalog.ShortDescriptions.Length];
            defect.Rows.Add(new DefectRow(FileKind.Equipment, items[i].EquipmentNo, items[i].Description));
        }

        List<FlRow> positions = TakePositions(ShortDescriptionFls, "short FL descriptions", p => _equipmentAt[p].Count > 0);
        for (int i = 0; i < positions.Count; i++)
        {
            positions[i].Description = Catalog.ShortDescriptions[i % Catalog.ShortDescriptions.Length];
            foreach (EquipRow equipment in _equipmentAt[positions[i]])
            {
                equipment.Locked = true;
            }

            defect.Rows.Add(new DefectRow(FileKind.Fl, positions[i].Code, positions[i].Description));
        }
    }

    private void SeedDuplicateTags()
    {
        SeededDefect defect = Add(
            "S11", "Equipment sharing a sort field with another equipment",
            new[] { "D6" }, "equipment",
            "D6 fails for exactly these 8 equipment: 4 tags, each used by two different equipment numbers.",
            DuplicateTagPairs * 2);

        List<EquipRow> items = TakeEquipment(DuplicateTagPairs * 2, "duplicate tags");
        for (int i = 0; i < items.Count; i += 2)
        {
            items[i + 1].SortField = items[i].SortField;
            defect.Rows.Add(new DefectRow(FileKind.Equipment, items[i].EquipmentNo, items[i].SortField));
            defect.Rows.Add(new DefectRow(FileKind.Equipment, items[i + 1].EquipmentNo, items[i].SortField));
        }
    }

    private void SeedUnknownStatus()
    {
        SeededDefect defect = Add(
            "S13", "Equipment with a system status the default status map does not cover",
            new[] { "C6", "D8" }, "equipment",
            "C6 maps these to operational status Unknown (status AVLB is not in the default map), and D8 fails for them.",
            UnknownStatusEquipment);

        foreach (EquipRow equipment in TakeEquipment(UnknownStatusEquipment, "unknown status"))
        {
            equipment.SystemStatus = SapStatus.Available;
            defect.Rows.Add(new DefectRow(FileKind.Equipment, equipment.EquipmentNo, SapStatus.Available));
        }
    }

    private void SeedBlankSortField()
    {
        SeededDefect defect = Add(
            "S12", "Equipment with a blank sort field",
            new[] { "C8" }, "equipment",
            "C8 falls back to the equipment number as asset_tag for exactly these rows.",
            0);

        // Stable file order, not shuffled: the roll per row is what makes the selection.
        foreach (EquipRow equipment in _data.Equipment)
        {
            if (equipment.Locked || equipment.Position is null || equipment.Position.Locked
                || equipment.Position.UnitCode == Catalog.BuildingUnitCode)
            {
                continue;
            }

            if (_rng.Chance(BlankSortFieldPercent))
            {
                equipment.SortField = "";
                equipment.Locked = true;
                defect.Rows.Add(new DefectRow(FileKind.Equipment, equipment.EquipmentNo));
            }
        }

        defect.ExpectedCount = defect.Rows.Count;
    }

    private void SeedOrphans()
    {
        SeededDefect defect = Add(
            "S03", "Equipment installed at a functional location that is not in File A",
            new[] { "D2" }, "equipment",
            "D2 raises one Error issue per row. Commit is blocked until resolved.",
            OrphanEquipment);

        // Only sections that already hold equipment, so an orphan never makes an
        // otherwise empty section look populated.
        var sectionsWithEquipment = _data.Equipment
            .Where(e => e.Position is not null)
            .Select(e => e.Position!.SuperiorFl.Length > 0
                ? e.Position.SuperiorFl
                : e.Position.Code[..e.Position.Code.LastIndexOf(Catalog.Separator, StringComparison.Ordinal)])
            .ToHashSet(StringComparer.Ordinal);

        var sections = _data.Fls
            .Where(f => f.Emit && !f.IsPosition && f.Code.Split(Catalog.Separator).Length == 3
                && f.UnitCode != Catalog.BuildingUnitCode
                && sectionsWithEquipment.Contains(f.Code))
            .ToList();
        _rng.Shuffle(sections);

        PositionType pumpType = Type("P");
        for (int i = 0; i < OrphanEquipment; i++)
        {
            int slot = i % OrphanFlCodes;
            FlRow section = sections[slot];
            PlantUnit unit = Unit(section.UnitCode);
            int number = 901 + slot;
            string missingFl = $"{section.Code}{Catalog.Separator}{pumpType.Prefix}{number}";

            // A different item per row at the same missing position keeps the tags unique.
            EquipTemplate item = pumpType.Items[i / OrphanFlCodes == 0 ? 0 : 1];
            EquipRow equipment = _builder.AddEquipment(null, unit, item, number, "");
            equipment.InstalledAtFl = missingFl;
            equipment.MainWorkCenter = DatasetBuilder.WorkCenterOf(pumpType.Discipline, DatasetBuilder.InstallationOf(unit));
            equipment.AbcIndicator = "B";
            equipment.Locked = true;
            defect.Rows.Add(new DefectRow(FileKind.Equipment, equipment.EquipmentNo, $"installed at {missingFl}"));
        }
    }

    private void RecordBuildingSubtree()
    {
        string root = Catalog.BuildingSubtreeRoot;
        var rows = _data.EmittedFls
            .Where(f => f.Code == root || f.Code.StartsWith(root + Catalog.Separator, StringComparison.Ordinal))
            .ToList();
        int equipmentRows = _data.Equipment.Count(e =>
            e.InstalledAtFl.StartsWith(root + Catalog.Separator, StringComparison.Ordinal));

        SeededDefect defect = Add(
            "S07", "Building subtree that a tenant would exclude",
            new[] { "C1" }, "subtrees",
            $"With {root} added to the C1 exclusion list, C1 excludes these {rows.Count} functional locations "
            + $"and the {equipmentRows} equipment installed below them. With the default empty list nothing is excluded.",
            1);

        foreach (FlRow row in rows)
        {
            defect.Rows.Add(new DefectRow(FileKind.Fl, row.Code));
        }
    }

    private void RecordMissingManufacturer()
    {
        int count = _data.Equipment
            .GroupBy(e => e.EquipmentNo, StringComparer.Ordinal)
            .Count(g => g.First().Manufacturer.Length == 0 && g.First().Model.Length == 0);

        _data.Defects.Add(new SeededDefect
        {
            Id = "S14",
            Title = "Equipment with no manufacturer and no model",
            Triggers = new[] { "D10" },
            CountUnit = "equipment",
            Expected = "D10 fails for this many distinct equipment numbers. Spread at random, so rows are not listed "
                + "and this condition may coincide with others.",
            ExpectedCount = count,
            RowsListed = false,
        });
    }
}
