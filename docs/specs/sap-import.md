# Barrier Ledger SCE — SAP Import Module Spec

Exported from the product owner's spec document on 13 Nov 2026. That document is the source of truth; update this copy when it changes.

## ملخص

الـ module ده بيحوّل ملفات SAP الخام (functional locations + equipment) إلى asset backbone نضيف داخل الـ tenant، من غير تدخل يدوي في Excel. المستخدم بيرفع الملفين، يراجع الـ column mapping وقواعد التنظيف والـ class mapping، يشوف الـ DQ score، وبعدين يعتمد الـ commit. كل القواعد لها default وقابلة للتعديل لكل tenant. باقي المستند بالإنجليزي لأنه موجّه للـ coding agent.

## 1. Purpose and scope

The module loads a tenant's SAP PM master data into the Barrier Ledger asset backbone through a guided, repeatable import. It is the entry point for every new customer and the data source for SCE identification.

**In scope**

- Upload of two flat files: functional location master and equipment master (`.xlsx`, `.csv`).
- Column mapping, validation, merge, cleansing, class mapping, DQ scoring, preview, commit.
- Re-import (delta) against an existing backbone with a change report.
- Full audit trail and rollback of a committed import batch.

**Out of scope for this release**

- Live SAP integration (OData, RFC, IDoc). Files only.
- Transactional data: notifications, work orders, measurement documents, maintenance plans.
- Criticality analysis and SCE identification. They consume the backbone; they are separate modules.
- AI-suggested mappings. The module exposes the hooks (section 6), the agent comes in a later phase.

**Hard rule:** the repository, tests and demo environment use the synthetic dataset only. No customer extract is ever committed.

## 2. Input files

Two files per import, typically exported from the SAP list reports for functional locations and equipment. Column headers vary by SAP language and layout, so the importer maps by user confirmation, not by fixed header names.

**File A: functional location master**

| Logical field | Typical SAP field | Required | Notes |
| --- | --- | --- | --- |
| `fl_code` | TPLNR | Yes | Unique key within the file |
| `fl_description` | PLTXT | Yes |  |
| `superior_fl` | TPLMA | No | Derived when blank (rule C3) |
| `object_type` | EQART | No |  |
| `sort_field` | EQFNR | No | Often holds the P&ID tag |
| `plant_section` | BEBER | No |  |
| `planner_group` | INGRP | No |  |
| `main_work_center` | ARBPL | No |  |
| `system_status` | STTXT | No | Needed for rule C2 |
| `abc_indicator` | ABCKZ | No | Stored as reference, never used as criticality result |

**File B: equipment master**

| Logical field | Typical SAP field | Required | Notes |
| --- | --- | --- | --- |
| `equipment_no` | EQUNR | Yes | Unique key within the file |
| `equipment_description` | EQKTX | Yes |  |
| `installed_at_fl` | TPLNR | Yes | Join key to File A |
| `object_type` | EQART | Yes | Drives class mapping |
| `sort_field` | EQFNR | No |  |
| `manufacturer`, `model`, `serial_no` | HERST, TYPBZ, SERGE | No |  |
| `system_status` | STTXT | No |  |
| `abc_indicator` | ABCKZ | No |  |
| `planner_group`, `main_work_center` | INGRP, ARBPL | No |  |

**File constraints:** max 50 MB and 500,000 rows per file; first sheet only for `.xlsx`; UTF-8 or Windows-1256 for `.csv` (Arabic descriptions must survive the round trip); header row auto-detected within the first 10 rows.

## 3. Target data model

Staging tables hold raw and transformed rows per import batch; backbone tables are written only at commit. Every table carries `tenant_id` and is filtered by it at the data-access layer.

| Table | Layer | Purpose | Key columns |
| --- | --- | --- | --- |
| `import_batch` | Staging | One row per import run | `id`, `tenant_id`, `status`, `created_by`, `rule_set_snapshot` (jsonb), `dq_score`, `committed_at` |
| `import_file` | Staging | Uploaded file metadata | `batch_id`, `kind` (FL / EQUIP), `file_hash`, `row_count`, `column_map` (jsonb) |
| `stg_fl_row` | Staging | Raw + normalised FL rows | `batch_id`, `row_no`, `raw` (jsonb), normalised fields, `disposition`, `disposition_reason` |
| `stg_equip_row` | Staging | Raw + normalised equipment rows | same pattern |
| `import_issue` | Staging | One row per rule hit | `batch_id`, `row_ref`, `rule_code`, `severity`, `message` |
| `installation`, `plant_unit`, `section_system` | Backbone | ISO 14224 taxonomy levels 3, 4 and 5 | `code`, `name`, `parent_id` |
| `functional_location` | Backbone | Hierarchy node | `fl_code`, `parent_fl_id`, `level`, `plant_unit_id`, `source_batch_id` |
| `asset` | Backbone | Maintainable item | `asset_tag`, `fl_id`, `equipment_class_id`, `sap_equipment_no`, `sap_object_type`, `operational_status`, `sap_abc_indicator`, `source_batch_id` |
| `equipment_class` | Reference | ISO 14224-style taxonomy | `code`, `name`, `category` |
| `object_type_map` | Config | Tenant mapping SAP object type to class | `tenant_id`, `sap_object_type`, `equipment_class_id`, `confirmed_by` |
| `cleansing_rule_config` | Config | Tenant overrides of default rules | `tenant_id`, `rule_code`, `enabled`, `params` (jsonb) |

**Design constraints**

- `disposition` is one of `KEEP`, `EXCLUDE`, `MERGED`, `ERROR`. Excluded rows stay in staging with their reason; nothing is silently dropped.
- `rule_set_snapshot` freezes the rules and mapping used, so any batch can be reproduced and audited later.
- Backbone rows keep `source_batch_id` so a batch can be rolled back.
- Master data only. No table in this module stores criticality results or SCE flags.

**Hierarchy follows the ISO 14224 taxonomy**

| ISO 14224 level | Name | Where it lives in the backbone |
| --- | --- | --- |
| 1 | Industry | Tenant attribute |
| 2 | Business category | Attribute of `installation` |
| 3 | Installation | `installation` |
| 4 | Plant / Unit | `plant_unit` |
| 5 | Section / System | `section_system`, linked from `functional_location` |
| 6 | Equipment class / unit | `asset` with its `equipment_class` |
| 7 | Subunit | Reserved: `asset.parent_asset_id` for SAP sub-equipment |
| 8 | Component / Maintainable item | Reserved, not loaded in this release |
| 9 | Part | Out of scope |

`functional_location.level` stores the ISO level (3 to 5). When a tenant's FL code has more segments than levels 3 to 5, the extra segments are stored as nested nodes within level 5, with `sub_level` 1, 2, 3 and so on.

## 4. Import pipeline

A batch moves through eight stages in order; each stage is idempotent and can be re-run after the user changes a mapping or rule.

1. **Upload.** Store files in tenant-scoped object storage, compute hash, create `import_batch` in status `UPLOADED`. Reject a file whose hash matches an already committed batch unless the user confirms.
2. **Parse and map columns.** Detect header row, propose a column map from a synonym dictionary (English, German and Arabic SAP headers), user confirms. Block if a required logical field is unmapped.
3. **Normalise.** Trim, collapse whitespace, upper-case codes, standardise blanks, keep original values in `raw`.
4. **Validate.** Structural checks: duplicate keys, orphan equipment, circular or broken hierarchy. Write `import_issue` rows.
5. **Merge and cleanse.** Join equipment to FL, then apply the cleansing rule set (section 5) in rule-code order. Set `disposition` per row.
6. **Map classes.** Resolve `sap_object_type` to `equipment_class` through `object_type_map` (section 6). Unmapped types go to a review queue.
7. **Score and preview.** Compute the DQ score (section 7). Show counts by disposition, issues by rule, hierarchy tree preview, and for a re-import the delta report.
8. **Commit.** Single database transaction writes backbone rows and sets status `COMMITTED`. Blocked while any `ERROR`-severity issue is open.

**Batch statuses:** `UPLOADED` → `MAPPED` → `VALIDATED` → `READY` → `COMMITTED`, plus `FAILED` and `ROLLED_BACK`.

**Re-import (delta):** match FL on `fl_code` and asset on `sap_equipment_no`. Classify each row as new, changed (field-level diff), unchanged, or missing from the new file. Missing rows are never deleted automatically; they are flagged `NOT_IN_SOURCE` for the user to decide.

**Rollback:** allowed for the most recent committed batch only, and only while no downstream record (criticality, SCE) references its rows.

## 5. Cleansing rules

Eight default rules ship with the product; each tenant can enable, disable or re-parameterise them, and the choice is frozen in the batch snapshot.

| Code | Rule | Default | Parameters |
| --- | --- | --- | --- |
| C1 | Exclude FLs matching the tenant exclusion list, with all their descendants and equipment | On, empty list | List of FL codes or prefix patterns (e.g. buildings, workshops) |
| C2 | Exclude objects whose system status contains a deletion or inactive flag | On | Status tokens, default `DLFL`, `INAC` |
| C3 | Derive missing `superior_fl` from the FL code by removing its last segment | On | Segment separator, default `-` |
| C4 | Exclude FLs with no equipment on themselves or on any descendant | On | None |
| C5 | On conflict between FL and equipment values for the same attribute, the equipment value wins | On | Attribute list: object type, planner group, work center, ABC indicator |
| C6 | Map SAP system status to `operational_status` | On | Map, default `CRTE` → In Service; unmapped → Unknown |
| C7 | Assign ISO 14224 taxonomy level (3 to 5) and plant/unit from FL code position | On | Map of each FL code segment to an ISO 14224 level: 3 Installation, 4 Plant/Unit, 5 Section/System (extra segments become sub-levels of 5) |
| C8 | Generate `asset_tag` | On | Source priority: `sort_field`, then `equipment_no` |

**Behaviour notes**

- C3 runs before C4 so that derived parents exist when descendants are evaluated.
- C3: if the derived parent code does not exist in File A, create a placeholder FL flagged `DERIVED` and raise a warning.
- C4 keeps every ancestor of a kept FL, so the hierarchy is never broken by the rule.
- C5 records both values in `import_issue` at `INFO` severity so the overwrite is visible.
- Every exclusion writes `disposition_reason` with the rule code. The preview lets the user override a single row to `KEEP` with a mandatory comment.

## 6. Class mapping

Every kept asset must resolve to exactly one `equipment_class`, because later SCE rules attach to the class, not to the SAP object type.

- `equipment_class` is a product-level reference table seeded with an ISO 14224-style taxonomy (category → class), for example Rotating → Centrifugal pump, Safety and control → Pressure safety valve, Safety and control → Fire and gas detector.
- `object_type_map` is tenant-level: one row per distinct `sap_object_type` found in the files.
- Resolution order: (1) tenant map entry confirmed by a user; (2) product default map for common object type codes; (3) unmapped.
- The mapping screen lists each distinct object type with its row count and five sample descriptions, sorted by row count, so the user maps the largest groups first.
- One object type may map to one class only. If a tenant uses one object type for mixed equipment, the user can add a description-pattern sub-rule (`sap_object_type` + regex on description → class).
- Unmapped types do not block commit; their assets load with class `UNCLASSIFIED` and count against the DQ score.

**Hook for the later AI agent:** expose `POST /imports/{id}/class-map/suggestions`, which accepts the list of unmapped object types with samples and returns `{sap_object_type, suggested_class, confidence, rationale}`. In this release the endpoint returns the product default map only. Suggestions are never auto-confirmed; `confirmed_by` must be a human user.

## 7. Data quality rules and score

The DQ score is a 0–100 weighted pass rate over rows with disposition `KEEP`, shown before commit and stored on the batch.

| Code | Check | Applies to | Severity | Weight |
| --- | --- | --- | --- | --- |
| D1 | Key is unique within the file | FL, equipment | Error | Blocking, not scored |
| D2 | Equipment's `installed_at_fl` exists in File A | Equipment | Error | Blocking, not scored |
| D3 | No circular parent reference | FL | Error | Blocking, not scored |
| D4 | Description is present and longer than 3 characters | FL, equipment | Warning | 15 |
| D5 | Asset resolves to a class other than `UNCLASSIFIED` | Equipment | Warning | 30 |
| D6 | `asset_tag` is present and unique within the tenant | Equipment | Warning | 20 |
| D7 | FL has a real (not derived) parent | FL | Warning | 10 |
| D8 | `operational_status` is not Unknown | Equipment | Warning | 10 |
| D9 | FL code matches the tenant's declared structure mask | FL | Warning | 10 |
| D10 | Manufacturer and model are present | Equipment | Info | 5 |

```latex
\text{DQ score} = \sum_{i=4}^{10} w_i \cdot \frac{\text{rows passing } D_i}{\text{rows where } D_i \text{ applies}}
```

- Weights sum to 100 and are tenant-configurable.
- Bands: 85 and above Good, 70 to 84 Acceptable, below 70 Poor. A batch in band Poor can be committed only after a second user holding the import.commit permission approves it. Error-severity issues always block commit.
- The preview breaks the score down per rule and per unit, and exports the failing rows as `.xlsx` so the customer can fix them in SAP.

## 8. Wizard, API, permissions and audit

The user sees a six-step wizard that maps one-to-one onto the pipeline and can be left and resumed at any step.

**Wizard steps**

1. Upload files.
2. Confirm column mapping.
3. Review rules (toggle and parameters, with live count of affected rows).
4. Map object types to classes.
5. Preview: DQ score, issues, hierarchy tree, delta report.
6. Commit, with a typed confirmation and summary of rows to be written.

The UI is bilingual (English and Arabic, RTL-aware). Codes, tags and SAP values are always shown left-to-right.

**API (REST, all under `/api/v1`, tenant resolved from the token)**

| Method and path | Purpose |
| --- | --- |
| `POST /imports` | Create batch |
| `POST /imports/{id}/files` | Upload a file (`kind` = FL or EQUIP) |
| `GET, PUT /imports/{id}/column-map` | Read proposal, save confirmed map |
| `GET, PUT /imports/{id}/rules` | Read and override rule set for this batch |
| `POST /imports/{id}/run` | Run stages 3 to 7 as a background job |
| `GET /imports/{id}` | Status, progress, counts, DQ score |
| `GET /imports/{id}/issues` | Paged, filter by rule and severity |
| `GET, PUT /imports/{id}/class-map` | Read and confirm object type mappings |
| `POST /imports/{id}/class-map/suggestions` | Mapping suggestions (hook, section 6) |
| `PATCH /imports/{id}/rows/{rowRef}` | Override a row disposition with comment |
| `GET /imports/{id}/preview` | Hierarchy preview and delta report |
| `POST /imports/{id}/commit` | Commit |
| `POST /imports/{id}/rollback` | Roll back |

**Permissions**

| Permission | Allows | Default role template |
| --- | --- | --- |
| `import.view` | See batches, issues, previews | Asset data viewer |
| `import.manage` | Create batches, map, edit rules, run | Asset data steward |
| `import.commit` | Commit and roll back | Tenant administrator |

Permissions respect the user's organizational scope: a user scoped to one installation can only import and see rows under that installation.

**Audit:** log upload, map confirmation, rule change, row override, class map confirmation, commit and rollback, each with user, timestamp and before/after values. Audit rows are append-only.

## 9. Non-functional requirements

- **Stack:** ASP.NET Core module inside the modular monolith, React + TypeScript wizard, PostgreSQL. No new infrastructure beyond object storage and a background job runner.
- **Performance:** 100,000 equipment rows plus 30,000 FL rows complete stages 3 to 7 in under 5 minutes on the reference environment; commit under 2 minutes.
- **Tenant isolation:** every query filtered by `tenant_id`; an automated test proves tenant A cannot read, run or commit tenant B's batch by guessing ids.
- **Same code for both deployments:** multi-tenant cloud and dedicated deployment differ by configuration only.
- **File safety:** validate type by content, not extension; reject macros (`.xlsm`); never evaluate formulas, read cached values only; neutralise cell values starting with `=`, `+`, `-`, `@` on export.
- **Retention:** raw files and staging rows deleted 90 days after commit (tenant-configurable); batch summary, snapshot and audit kept.
- **Resilience:** background job is resumable; a failed stage leaves the batch in `FAILED` with the stage name and can be re-run.
- **Observability:** structured logs per batch id; no cell values from customer files in logs.

## 10. Acceptance criteria

The module is done when the synthetic dataset loads end to end with no manual step outside the wizard and all tests below pass in CI.

**Synthetic dataset (to be generated first, task T1):** one fictional refinery, 2 installations, 6 plant/units, about 3,000 FLs and 8,000 equipment rows, with seeded defects: 40 deleted or inactive objects, 25 FLs with blank superior, 15 orphan equipment, 10 duplicate keys, 3 conflicting FL/equipment attributes, 12 unmapped object types, mixed English and Arabic descriptions, and one excluded building subtree.

| ID | Scenario | Expected result |
| --- | --- | --- |
| A1 | Upload both files, accept proposed column map | All required fields mapped automatically |
| A2 | Run with default rules | Exactly 40 rows excluded by C2; 25 parents derived by C3; building subtree excluded by C1 once added to the list |
| A3 | Duplicate keys and orphans present | Commit blocked; 10 D1 and 15 D2 issues listed with row references |
| A4 | Fix errors by row override or corrected file, re-run | Batch reaches `READY` |
| A5 | Conflict between FL and equipment planner group | Asset carries the equipment value; INFO issue records both |
| A6 | 12 unmapped object types | Listed with counts and samples; commit allowed; assets load as `UNCLASSIFIED`; D5 pass rate reflects them |
| A7 | Commit | Backbone row counts equal `KEEP` counts in preview; hierarchy has no orphan nodes |
| A8 | Re-import same files | Delta report shows 0 new, 0 changed |
| A9 | Re-import with 20 edited and 5 removed rows | 20 changed with field-level diff; 5 flagged `NOT_IN_SOURCE`; nothing deleted |
| A10 | Rollback latest batch | Backbone returns to prior state; batch status `ROLLED_BACK` |
| A11 | Tenant B calls tenant A's batch id | 404, and audit entry written |
| A12 | User without `import.commit` calls commit | 403 |
| A13 | Arabic descriptions in `.csv` (Windows-1256) | Stored and displayed correctly |
| A14 | Run same batch twice with same snapshot | Identical dispositions and DQ score |
| A15 | Load test at 100,000 equipment rows | Meets section 9 timings |

## 11. Task breakdown and decisions

Build in ten tasks, one pull request each, in this order; each PR includes its tests and must pass CI before the next starts.

| Task | Deliverable | Covers tests |
| --- | --- | --- |
| T1 | Synthetic dataset generator and fixture files, with a manifest of seeded defects | Basis for all |
| T2 | Migrations for staging, config and backbone tables; `equipment_class` seed |  |
| T3 | Upload, file safety checks, parser, header detection, column map proposal | A1, A13 |
| T4 | Normalise and validate stages; `import_issue` | A3 |
| T5 | Rule engine with C1 to C8, tenant config, batch snapshot | A2, A5, A14 |
| T6 | Class mapping, default map, sub-rules, suggestions hook | A6 |
| T7 | DQ scoring and failing-rows export | A6 |
| T8 | Commit, delta re-import, rollback | A4, A7 to A10 |
| T9 | REST API, permissions, audit, tenant isolation tests | A11, A12 |
| T10 | React wizard (bilingual, RTL), background job progress, load test | A15, end-to-end |

**Instructions to the coding agent**

- Read this spec and the existing tenancy and role code before writing anything. Reuse the existing tenant filter and permission attributes; do not create parallel ones.
- Ask before adding any new dependency or infrastructure component.
- Do not change tables outside this module. If a change is needed, stop and report it.
- Where this spec is silent, choose the simplest behaviour, implement it behind a clearly named constant, and list it in the PR description under "Assumptions".

**Decisions confirmed by the product owner on 13 Nov 2026**

1. **Equipment classes:** release 1 seeds about 40 classes covering the main ISO 14224 equipment categories (rotating, static, electrical, safety and control, piping and valves). The list is data, extendable without code change.
2. **Hierarchy:** strictly per the ISO 14224 taxonomy levels, as tabled in section 3. C7 maps FL code segments to levels 3 to 5; assets sit at level 6.
3. **DQ weights and bands:** as in section 7, to be recalibrated after the first pilot.
4. **Commit with DQ band Poor:** allowed with a second approver. Add status `PENDING_APPROVAL` between `READY` and `COMMITTED`, endpoint `POST /imports/{id}/approve`, and the rule that approver and requester are different users. Add test A16: Poor batch cannot be committed by its requester alone.
5. **Staging retention:** 90 days after commit, tenant-configurable.
6. **SAP ABC indicator:** shown in the asset view under the label "SAP reference", visually separate from any calculated criticality result.
