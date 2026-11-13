namespace BarrierLedger.SyntheticSapData;

/// <summary>One row of File A (functional location master).</summary>
internal sealed class FlRow
{
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public string SuperiorFl { get; set; } = "";
    public string ObjectType { get; set; } = "";
    public string SortField { get; set; } = "";
    public string PlantSection { get; set; } = "";
    public string PlannerGroup { get; set; } = "";
    public string MainWorkCenter { get; set; } = "";
    public string SystemStatus { get; set; } = SapStatus.Active;
    public string AbcIndicator { get; set; } = "";

    // Generator bookkeeping, never written to the files.
    public bool Emit { get; set; } = true;
    public bool IsPosition { get; set; }
    public bool Locked { get; set; }
    public string UnitCode { get; set; } = "";

    public FlRow Clone() => (FlRow)MemberwiseClone();

    public string[] ToFields() => new[]
    {
        Code, Description, SuperiorFl, ObjectType, SortField,
        PlantSection, PlannerGroup, MainWorkCenter, SystemStatus, AbcIndicator,
    };
}

/// <summary>One row of File B (equipment master).</summary>
internal sealed class EquipRow
{
    public string EquipmentNo { get; set; } = "";
    public string Description { get; set; } = "";
    public string InstalledAtFl { get; set; } = "";
    public string ObjectType { get; set; } = "";
    public string SortField { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string Model { get; set; } = "";
    public string SerialNo { get; set; } = "";
    public string SystemStatus { get; set; } = SapStatus.Active;
    public string AbcIndicator { get; set; } = "";
    public string PlannerGroup { get; set; } = "";
    public string MainWorkCenter { get; set; } = "";

    // Generator bookkeeping, never written to the files.
    public bool Locked { get; set; }
    public FlRow? Position { get; set; }

    public EquipRow Clone() => (EquipRow)MemberwiseClone();

    public string[] ToFields() => new[]
    {
        EquipmentNo, Description, InstalledAtFl, ObjectType, SortField, Manufacturer,
        Model, SerialNo, SystemStatus, AbcIndicator, PlannerGroup, MainWorkCenter,
    };
}

internal static class SapStatus
{
    public const string Active = "CRTE";
    public const string Deleted = "CRTE DLFL";
    public const string Inactive = "CRTE INAC";
    public const string Available = "AVLB";
}

internal static class FileKind
{
    public const string Fl = "FL";
    public const string Equipment = "EQUIP";
}

internal sealed record DefectRow(string File, string Key, string? Note = null);

/// <summary>
/// A condition deliberately planted in the dataset, with the rule or check of
/// the SAP import spec it is meant to trigger.
/// </summary>
internal sealed class SeededDefect
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string[] Triggers { get; init; }
    public required string CountUnit { get; init; }
    public required string Expected { get; init; }
    public int ExpectedCount { get; set; }
    public bool RowsListed { get; init; } = true;
    public List<DefectRow> Rows { get; } = new();
}

internal sealed class Dataset
{
    public required ulong Seed { get; init; }
    public List<FlRow> Fls { get; } = new();
    public List<EquipRow> Equipment { get; } = new();
    public List<SeededDefect> Defects { get; } = new();

    public IEnumerable<FlRow> EmittedFls => Fls.Where(f => f.Emit);
}
