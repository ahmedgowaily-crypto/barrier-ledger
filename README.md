# Barrier Ledger SCE

Safety Critical Element (SCE) management for high-hazard industries.

## Repository layout

| Path | Contents |
| --- | --- |
| `docs/specs/` | Module specifications. Start with `sap-import.md`. |
| `tools/BarrierLedger.SyntheticSapData/` | Generator and verifier for the synthetic SAP master data set. |
| `tests/fixtures/sap-import/synthetic-refinery/` | The generated data set and its manifest, used by all SAP import tests. |

## Build and check

Requires the .NET 8 SDK.

```
dotnet build BarrierLedger.sln -c Release
dotnet run -c Release --no-build --project tools/BarrierLedger.SyntheticSapData -- check --fixtures tests/fixtures/sap-import/synthetic-refinery
```

The second command is what CI runs. It exits with 0 only when every check passes.

## Data rule

The repository, tests and demo environment use the synthetic data set only.
No customer extract, tag list or document is ever committed.
