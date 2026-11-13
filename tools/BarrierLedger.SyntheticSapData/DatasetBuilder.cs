namespace BarrierLedger.SyntheticSapData;

/// <summary>
/// Builds the clean refinery model: hierarchy, tag-level positions and their
/// equipment. Defects are planted afterwards by <see cref="DefectSeeder"/>.
/// </summary>
internal sealed class DatasetBuilder
{
    public const ulong DefaultSeed = 20261113;

    private const int ArabicPercent = 30;
    private const int UpperCasePercent = 60;
    private const int MissingMakerPercent = 35;
    private const int MinPositionsPerSection = 42;
    private const int MaxPositionsPerSection = 55;
    private const int PairedPercent = 55;
    private const int BuildingPositionsPerSection = 8;

    private readonly Prng _rng;
    private readonly Dataset _data;
    private readonly Dictionary<string, int> _counters = new(StringComparer.Ordinal);
    private readonly HashSet<string> _tags = new(StringComparer.Ordinal);
    private int _nextEquipmentNo = 10000001;

    private DatasetBuilder(ulong seed)
    {
        _rng = new Prng(seed);
        _data = new Dataset { Seed = seed };
    }

    public Prng Rng => _rng;

    public Dataset Data => _data;

    public static Dataset Build(ulong seed)
    {
        Catalog.Validate();
        var builder = new DatasetBuilder(seed);
        builder.BuildHierarchy();
        new DefectSeeder(builder).Apply();
        return builder._data;
    }

    public static Installation InstallationOf(PlantUnit unit) =>
        Catalog.Installations.Single(i => i.Code == unit.InstallationCode);

    public static string PlannerGroupOf(PlantUnit unit) => "P" + unit.TagNo;

    public static string WorkCenterOf(string discipline, Installation installation) =>
        $"{discipline}-{installation.WorkCenterSuffix}";

    public static string UnitFlCode(PlantUnit unit) => $"{unit.InstallationCode}{Catalog.Separator}{unit.Code}";

    public string NextEquipmentNo() => (_nextEquipmentNo++).ToString(System.Globalization.CultureInfo.InvariantCulture);

    public int NextNumber(PlantUnit unit, string prefix)
    {
        string key = unit.Code + "|" + prefix;
        int next = _counters.TryGetValue(key, out int current) ? current + 1 : 101;
        if (next > 899)
        {
            throw new InvalidOperationException($"Position numbers exhausted for {key}.");
        }

        _counters[key] = next;
        return next;
    }

    public FlRow AddSection(PlantUnit unit, int number, string en, string ar, bool emit = true)
    {
        string unitCode = UnitFlCode(unit);
        var section = new FlRow
        {
            Code = $"{unitCode}{Catalog.Separator}{SectionLetter(unit)}{number:00}",
            Description = Describe(en, ar, null),
            SuperiorFl = unitCode,
            PlantSection = unit.Code,
            PlannerGroup = PlannerGroupOf(unit),
            UnitCode = unit.Code,
            Emit = emit,
            Locked = true,
        };
        _data.Fls.Add(section);
        return section;
    }

    public FlRow AddPosition(
        PlantUnit unit, FlRow section, PositionType type, int number, string suffix, bool withEquipment = true)
    {
        Installation installation = InstallationOf(unit);
        string mainTag = Tag(unit, type.Items[0], number, suffix);
        var position = new FlRow
        {
            Code = $"{section.Code}{Catalog.Separator}{type.Prefix}{number}{suffix}",
            Description = Describe(type.En, type.Ar, mainTag),
            SuperiorFl = section.Code,
            SortField = mainTag,
            PlantSection = unit.Code,
            PlannerGroup = PlannerGroupOf(unit),
            MainWorkCenter = WorkCenterOf(type.Discipline, installation),
            AbcIndicator = PickAbc(),
            IsPosition = true,
            UnitCode = unit.Code,
        };
        _data.Fls.Add(position);

        if (withEquipment)
        {
            for (int i = 0; i < type.Items.Length; i++)
            {
                EquipTemplate item = type.Items[i];
                if (i == 0 || _rng.Chance(item.Percent))
                {
                    AddEquipment(position, unit, item, number, suffix);
                }
            }
        }

        return position;
    }

    public EquipRow AddEquipment(FlRow? position, PlantUnit unit, EquipTemplate item, int number, string suffix)
    {
        string tag = Tag(unit, item, number, suffix);
        if (!_tags.Add(tag))
        {
            throw new InvalidOperationException($"Tag '{tag}' generated twice.");
        }

        string manufacturer = "";
        string model = "";
        if (!_rng.Chance(MissingMakerPercent))
        {
            manufacturer = _rng.Pick(Catalog.Manufacturers);
            model = $"{manufacturer[..3].ToUpperInvariant()}-{_rng.Next(100, 999)}{(char)('A' + _rng.Next(6))}";
        }

        var equipment = new EquipRow
        {
            EquipmentNo = NextEquipmentNo(),
            Description = Describe(item.En, item.Ar, tag),
            InstalledAtFl = position?.Code ?? "",
            ObjectType = item.ObjectType,
            SortField = tag,
            Manufacturer = manufacturer,
            Model = model,
            SerialNo = $"SN{_rng.Next(1000000, 9999999)}",
            AbcIndicator = position?.AbcIndicator ?? "",
            PlannerGroup = position?.PlannerGroup ?? PlannerGroupOf(unit),
            MainWorkCenter = position?.MainWorkCenter ?? "",
            Position = position,
        };
        _data.Equipment.Add(equipment);
        return equipment;
    }

    private static string SectionLetter(PlantUnit unit) => unit.Code == Catalog.BuildingUnitCode ? "B" : "S";

    private static string Tag(PlantUnit unit, EquipTemplate item, int number, string suffix) =>
        $"{unit.TagNo}-{item.TagPrefix}-{number}{suffix}";

    private void BuildHierarchy()
    {
        foreach (Installation installation in Catalog.Installations)
        {
            _data.Fls.Add(new FlRow
            {
                Code = installation.Code,
                Description = Describe(installation.En, installation.Ar, null),
                Locked = true,
            });

            foreach (PlantUnit unit in Catalog.ProcessUnits.Where(u => u.InstallationCode == installation.Code))
            {
                AddUnit(unit);
                for (int s = 0; s < unit.Sections.Length; s++)
                {
                    FlRow section = AddSection(unit, s + 1, unit.Sections[s].En, unit.Sections[s].Ar);
                    FillSection(unit, section, Catalog.ProcessPositionTypes,
                        _rng.Next(MinPositionsPerSection, MaxPositionsPerSection));
                }
            }

            if (installation.Code == Catalog.BuildingUnit.InstallationCode)
            {
                PlantUnit building = Catalog.BuildingUnit;
                AddUnit(building);
                for (int s = 0; s < building.Sections.Length; s++)
                {
                    FlRow section = AddSection(building, s + 1, building.Sections[s].En, building.Sections[s].Ar);
                    FillSection(building, section, Catalog.BuildingPositionTypes, BuildingPositionsPerSection);
                }
            }
        }
    }

    private void AddUnit(PlantUnit unit)
    {
        _data.Fls.Add(new FlRow
        {
            Code = UnitFlCode(unit),
            Description = Describe(unit.En, unit.Ar, null),
            SuperiorFl = unit.InstallationCode,
            PlantSection = unit.Code,
            PlannerGroup = PlannerGroupOf(unit),
            UnitCode = unit.Code,
            Locked = true,
        });
    }

    private void FillSection(PlantUnit unit, FlRow section, PositionType[] types, int target)
    {
        var candidates = types.Where(t => t.WeightFor(unit.Code) > 0).ToArray();
        int totalWeight = candidates.Sum(t => t.WeightFor(unit.Code));
        int created = 0;
        while (created < target)
        {
            PositionType type = PickWeighted(candidates, totalWeight, unit.Code);
            int number = NextNumber(unit, type.Prefix);
            if (type.Paired && _rng.Chance(PairedPercent) && created + 2 <= target)
            {
                AddPosition(unit, section, type, number, "A");
                AddPosition(unit, section, type, number, "B");
                created += 2;
            }
            else
            {
                AddPosition(unit, section, type, number, "");
                created++;
            }
        }
    }

    private PositionType PickWeighted(PositionType[] candidates, int totalWeight, string unitCode)
    {
        int roll = _rng.Next(totalWeight);
        foreach (PositionType type in candidates)
        {
            roll -= type.WeightFor(unitCode);
            if (roll < 0)
            {
                return type;
            }
        }

        throw new InvalidOperationException("Weighted pick fell through.");
    }

    private string PickAbc()
    {
        int roll = _rng.Next(100);
        return roll < 20 ? "A" : roll < 70 ? "B" : "C";
    }

    private string Describe(string en, string ar, string? tag)
    {
        string text;
        if (_rng.Chance(ArabicPercent))
        {
            text = ar;
        }
        else
        {
            text = _rng.Chance(UpperCasePercent) ? en.ToUpperInvariant() : en;
        }

        return tag is null ? text : $"{text} {tag}";
    }
}
