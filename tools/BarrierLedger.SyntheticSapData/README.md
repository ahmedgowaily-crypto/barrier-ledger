# Synthetic SAP master data

Task T1 of the SAP import module (`docs/specs/sap-import.md`, sections 2 and 10).

This tool writes a fictional refinery's functional location master (File A) and
equipment master (File B) in the shape of an SAP list export, with a known set
of defects planted in it. Every later SAP import test loads this data set and
asserts against its manifest.

Every name, code, tag, vendor and description is invented. Nothing is derived
from a real company, plant or document.

## Commands

```
dotnet run -c Release --project tools/BarrierLedger.SyntheticSapData -- generate --out <dir> [--seed <n>]
dotnet run -c Release --project tools/BarrierLedger.SyntheticSapData -- verify --dir <dir>
dotnet run -c Release --project tools/BarrierLedger.SyntheticSapData -- check --fixtures tests/fixtures/sap-import/synthetic-refinery
```

- `generate` writes the seven files. The default seed is 20261113.
- `verify` re-derives every seeded condition from the files alone and compares
  the result with the manifest and with the counts in the spec.
- `check` is what CI runs: generate, verify, confirm that a second run is
  byte-identical, and confirm that the committed fixtures are up to date.

To change the data set, edit the generator, run `generate` into the fixtures
folder, and commit the regenerated files with the code change.

## Files

| File | Format |
| --- | --- |
| `fl_master.csv`, `equipment_master.csv` | UTF-8 with BOM, comma separated, CRLF |
| `fl_master.cp1256.csv`, `equipment_master.cp1256.csv` | Same text in Windows-1256 |
| `fl_master.xlsx`, `equipment_master.xlsx` | One sheet, every cell stored as text |
| `manifest.json` | Structure, column map, object types and every seeded condition |

The `.csv` files and the manifest are byte-identical for a given seed on every
platform. The `.xlsx` files hold identical cells, but their compressed bytes can
differ between .NET versions, so CI compares their content and not their bytes.

## Structure

FL codes have up to four segments separated by `-`, mapped to the ISO 14224
taxonomy as the spec requires:

| Segment | Example | ISO 14224 level |
| --- | --- | --- |
| 1 | `TZ1` | 3 Installation |
| 2 | `TZ1-CDU` | 4 Plant / Unit |
| 3 | `TZ1-CDU-S01` | 5 Section / System |
| 4 | `TZ1-CDU-S01-P101A` | 5, sub-level 1 (tag-level position) |

Equipment (level 6) is installed at tag-level positions. There are 2
installations and 6 process plant/units, plus one building subtree
(`TZ1-BLD`) that exists to be excluded.

## Seeded conditions

The first seven come from the spec. The rest were added so that every cleansing
rule and data quality check has at least one row that triggers it.

| ID | Condition | Count | Triggers |
| --- | --- | --- | --- |
| S01 | Status carries `DLFL` or `INAC` | 40 objects (8 FL, 32 equipment) | C2 |
| S02 | Blank superior FL | 25 FLs, 3 of them with the parent absent from File A | C3, D7 |
| S03 | Equipment installed at an FL missing from File A | 15 equipment | D2 |
| S04 | Key appears twice | 10 keys (4 FL, 6 equipment) | D1 |
| S05 | Equipment attribute differs from its FL | 3 equipment | C5 |
| S06 | Object type unknown to any default map | 12 object types | D5 |
| S07 | Building subtree | 1 subtree | C1 |
| S08 | No equipment on the FL or below it | 31 FLs | C4 |
| S09 | FL code breaks the structure mask | 5 FLs | D9 |
| S10 | Description of 3 characters or fewer | 24 rows | D4 |
| S11 | Sort field shared by two equipment | 8 equipment (4 tags) | D6 |
| S12 | Blank sort field | about 5% of equipment | C8 |
| S13 | Status outside the default status map | 12 equipment | C6, D8 |
| S14 | No manufacturer and no model | about 35% of equipment | D10 |

No row carries more than one of S01 to S13, so each count can be asserted
exactly. The manifest lists the affected keys for each of them.

## Design notes

- The generator uses its own SplitMix64 generator, not `System.Random`, so the
  sequence is fixed by this source code.
- The tool has no package dependencies. The `.xlsx` files are written and read
  with the base class library only.
