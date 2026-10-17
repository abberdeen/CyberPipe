---
name: netrunner-script-package
description: >-
  Standardized specification, mandatory file layout (netrunner.manifest.json, schema.input.json, schema.output.json, SKILL.md, main script, test fixtures),
  and Mini-n8n DAG workflow rules (SkillNode, NetrunnerScriptNode, LlmProcessNode, HitlApprovalNode) for CyberPipe Netrunner ETL scripts.
  Use when creating, validating, or chaining Netrunner scripts and cascading ETL workflows.
---

# Netrunner Script Package & Mini-n8n Workflow Specification

This skill defines the strict contract for **CyberPipe Netrunner** — the deterministic ETL and cascading workflow subsystem (`Mini-n8n`) designed to eliminate repetitive AI token consumption by turning recurring tasks into reusable, schema-validated scripts and DAG pipelines.

---

## 1. Mandatory Package Directory Layout

Every Netrunner script MUST be packaged in its own isolated versioned directory:
`data/netrunner-packages/<slug>/<version>/`

A package is **INVALID** and will be rejected by `NetrunnerPackageValidator` unless **all 6 mandatory files** exist:

```text
data/netrunner-packages/<slug>/<version>/
├── netrunner.manifest.json       # [MANDATORY] Standardized package descriptor (analogous to package.json)
├── SKILL.md                      # [MANDATORY] Human & Tier-1 Router semantic documentation + trigger phrases
├── schema.input.json             # [MANDATORY] JSON Schema (Draft 2020-12) for input JSON payload
├── schema.output.json            # [MANDATORY] JSON Schema (Draft 2020-12) for output JSON payload
├── main.csx | main.py | main.js  # [MANDATORY] Deterministic entrypoint script (must match runtime.entrypoint)
└── tests/
    └── sample.fixture.json       # [MANDATORY] Deterministic test case (sampleInput + expectedOutput) for dry-run verification
```

---

## 2. `netrunner.manifest.json` Specification (The `package.json` of Netrunner)

Every `netrunner.manifest.json` MUST conform to the following structure and explicitly declare its **input/output formats**, **file contracts**, **sandbox permissions**, and **SHA-256 file integrity hashes**:

```json
{
  "$schema": "https://cyberpipe.local/schemas/netrunner.manifest.v1.json",
  "slug": "cbr-currency-rates-etl",
  "name": "Курсы валют ЦБ РФ и расчет дневной дельты",
  "version": "1.0.0",
  "description": "Парсит XML/JSON котировок ЦБ РФ, вычисляет дельту по выбранным валютам и сохраняет CSV-таблицу.",
  "author": {
    "origin": "AiSynthesizedTier3",
    "synthesizedByModel": "claude-3-7-sonnet",
    "approvedByUserId": "123456789",
    "createdAtUtc": "2026-10-17T09:50:00Z"
  },
  "runtime": {
    "engine": "CSharpRoslyn",
    "entrypoint": "main.csx",
    "timeoutMs": 15000,
    "maxMemoryMb": 64,
    "deterministic": true
  },
  "io": {
    "inputSchemaFile": "schema.input.json",
    "outputSchemaFile": "schema.output.json",
    "inputFormats": ["application/json", "application/xml"],
    "outputFormats": ["application/json", "text/csv"],
    "inputFiles": [
      {
        "alias": "rawPayloadFile",
        "required": true,
        "allowedExtensions": [".xml", ".json"],
        "mimeTypes": ["application/xml", "text/xml", "application/json"],
        "maxSizeBytes": 2097152,
        "description": "Сырой файл котировок от предыдущего узла SkillNode (HttpSafeFetcher)"
      }
    ],
    "outputFiles": [
      {
        "alias": "ratesSummaryCsv",
        "filenamePattern": "cbr_rates_delta.csv",
        "mimeType": "text/csv",
        "required": true,
        "maxSizeBytes": 524288,
        "description": "Готовая CSV-таблица с курсами и процентами изменений"
      }
    ]
  },
  "permissions": {
    "allowNetwork": false,
    "allowedDomains": [],
    "filesystemScope": "IsolatedRunWorkspaceOnly",
    "requiredVaultSecretRefs": []
  },
  "economics": {
    "estimatedTokensSavedPerRun": 3200
  },
  "integrity": {
    "filesSha256": {
      "main.csx": "<sha256-hex>",
      "schema.input.json": "<sha256-hex>",
      "schema.output.json": "<sha256-hex>",
      "SKILL.md": "<sha256-hex>",
      "tests/sample.fixture.json": "<sha256-hex>"
    }
  }
}
```

### Mandatory Manifest Sections Explained:
1. **`slug` & `version`:** Immutable identifier in `kebab-case` and Semantic Versioning (`MAJOR.MINOR.PATCH`).
2. **`runtime`:**
   * `engine`: `"CSharpRoslyn"` (compiled in-memory with restricted assembly whitelist), `"PythonSandbox"` (executed in isolated sandbox), or `"JavaScript"`.
   * `entrypoint`: Relative filename of the main script (`main.csx`, `main.py`, `main.js`).
   * `timeoutMs` & `maxMemoryMb`: Hard resource caps enforced by the runner.
3. **`io` (Input/Output Contract):**
   * `inputSchemaFile` / `outputSchemaFile`: Must point to `schema.input.json` and `schema.output.json`.
   * `inputFormats` / `outputFormats`: Explicit MIME types processed and produced by the script.
   * `inputFiles`: Array of file descriptors expected in the run workspace (`alias`, `required`, `allowedExtensions`, `mimeTypes`, `maxSizeBytes`).
   * `outputFiles`: Array of file artifacts that the script guarantees to write into the run output workspace (`alias`, `filenamePattern`, `mimeType`, `required`, `maxSizeBytes`).
4. **`permissions` (Zero-Trust Sandbox Scope):**
   * By default, `allowNetwork` SHOULD be `false` (prefer fetching data in an upstream `SkillNode` and passing it into `NetrunnerScriptNode` for pure offline ETL transformation).
   * `filesystemScope` is strictly `"IsolatedRunWorkspaceOnly"` (`data/netrunner-artifacts/{RunId}/{NodeId}/`). Direct access to host user folders or `data/subsystems/*.db` is forbidden.
5. **`integrity.filesSha256`:**
   * Contains SHA-256 hashes of every file in the package. Verified before every execution to prevent tampering.

---

## 3. Input & Output JSON Schemas (`schema.input.json` & `schema.output.json`)

Both files MUST use **JSON Schema Draft 2020-12** (`"additionalProperties": false` is strongly recommended to prevent schema drift):

* **Pre-Run Validation:** Before executing `main.*`, the engine validates the incoming JSON payload against `schema.input.json` and verifies all `io.inputFiles`.
* **Post-Run Validation:** After `main.*` finishes, the engine validates the returned JSON stdout/result against `schema.output.json` and verifies that every `required: true` file in `io.outputFiles` exists and is within `maxSizeBytes`.
* **DAG Edge Type-Checking:** When connecting Node $A \to$ Node $B$ in a workflow, the builder checks compatibility between Node $A$'s `schema.output.json` and Node $B$'s `schema.input.json`.

---

## 4. `SKILL.md` Inside the Package

Every Netrunner package includes a `SKILL.md` file with YAML frontmatter so that both humans and the **Tier 1 ONNX Semantic Router** can discover and invoke the script for **0 tokens**:

```markdown
---
slug: cbr-currency-rates-etl
name: Курсы валют ЦБ РФ и расчет дневной дельты
version: 1.0.0
triggers:
  - "какой сегодня курс валют"
  - "посчитай дельту доллара и евро цб"
  - "сводка курсов валют в таблицу"
---

# Назначение
Детерминированный ETL-скрипт для разбора котировок ЦБ РФ без вызова LLM.

## Входные данные
* JSON по схеме `schema.input.json` (список кодов валют и порог алерта в %).
* Файл `rawPayloadFile` (`.xml` или `.json`).

## Выходные данные
* JSON по схеме `schema.output.json` с флагом `hasSignificantChange`.
* Файл `ratesSummaryCsv` (`cbr_rates_delta.csv`).
```

---

## 5. Cascading Workflows (Mini-n8n DAG) with AI & Human-in-the-Loop (HITL)

Netrunner scripts can run standalone or inside a **Directed Acyclic Graph (DAG) Workflow** (`NetrunnerWorkflowDefinition`) combining 8 node types:

1. **`TriggerNode`:** `Cron` (via Hangfire), `TelegramCommand`, `SystemEvent`, or `ManualUI`.
2. **`SkillNode` (0 tokens):** Built-in C# host skill (e.g., `HttpSafeFetcherSkill`, `GitRepoRadarSkill`, `ScreenOcrSkill`) that fetches raw data or files.
3. **`NetrunnerScriptNode` (0 tokens):** Executes `<slug>@<version>` over the output of upstream nodes. Filters, parses, aggregates, and transforms data deterministically.
4. **`ConditionNode` / `SwitchNode` (0 tokens):** Evaluates deterministic JSONPath conditions (e.g., `$.steps.etl.output.hasSignificantChange == true`). Stops silently if no action is needed.
5. **`ParallelFanOutNode` & `JoinNode` (0 tokens):** Runs multiple branches concurrently and merges their outputs.
6. **`LlmProcessNode` (Budget-Governed AI Step):** Runs `Tier 2` or `Tier 3` (`Local` or `RemoteCloud`) **only on the pre-filtered, compact output** of `NetrunnerScriptNode` (spending ~200 tokens instead of 50,000!). Enforces `CentralDataSanitizer` (on Egress) and `TokenBudgetGovernor`.
7. **`HitlApprovalNode` (Human-in-the-Loop):**
   * Persists workflow state (`WaitingForHumanApproval`) into `data/subsystems/netrunner.db` and releases all memory/threads.
   * Sends an interactive preview card with attached output files to Telegram and Avalonia Desktop UI:
     * `[✅ Approve & Continue]` — resumes workflow execution from the `approved` branch.
     * `[✏️ Edit Payload]` — allows modifying the JSON/text before continuing.
     * `[🛑 Reject / Abort]` — routes to `rejected` branch or aborts the workflow.
8. **`DeliveryNode` (0 tokens):** Sends final output/files to Telegram, writes to `FinanceLedger` / `KnowledgeVault` / `PersonalCrm`, or triggers a child workflow.

### Waiting for Parameters & Conditional Retries (`DeferRetry` & `untilExpression`)
Never block a process with long `sleep()` calls (>15 seconds) inside a script when waiting for an external parameter or report date to update. Instead, use one of two persistent mechanisms:
1. **In-Script `DeferRetry` Signal:** Output `{"$netrunnerControl": "DeferRetry", "retryAfterSeconds": 300, "reason": "Parameter X not updated yet"}` (or exit with code `75`). The runner immediately frees RAM, checkpoints the node as `WaitingForConditionRetry` in `netrunner.db`, and wakes up the node after `retryAfterSeconds`.
2. **Declarative Node `retryPolicy.untilExpression`:** Configure `"untilExpression": "$.output.reportDate == $today"` with `intervalSeconds` and `maxAttempts` on the node in the workflow JSON. If the expression evaluates to `false`, the engine automatically schedules a retry without keeping any process in memory.

---

## 6. Validation & Helper Resources

* Template manifest: [`resources/netrunner.manifest.template.json`](./resources/netrunner.manifest.template.json)
* Package validation script: [`scripts/validate-netrunner-package.ps1`](./scripts/validate-netrunner-package.ps1)

To validate a Netrunner package directory and update/verify its SHA-256 hashes:
```powershell
& ".\.agents\skills\netrunner-script-package\scripts\validate-netrunner-package.ps1" -PackagePath ".\data\netrunner-packages\cbr-currency-rates-etl\1.0.0" -UpdateHashes
```
