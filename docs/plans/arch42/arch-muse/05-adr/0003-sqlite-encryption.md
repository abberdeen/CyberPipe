# ADR-0003 — SQLite at-rest encryption only via a SQLCipher provider (hypothesis until PoC)

- **Status:** Proposed — explicitly a **hypothesis**, not a guarantee
- **Date:** 2026-10-17
- **Sources:** `docs/plans/03`, `09`, `17-adr-mvp.md`; audit AUD-04, L3

## Context and constraints

Plans assert per-DB 256-bit keys passed as `Password=` (`C-007`, `C-008`). Stock
`Microsoft.Data.Sqlite` implements no page encryption: without a SQLCipher-capable
SQLitePCLRaw provider the "encrypted" databases are plaintext to anyone reading the
files. Every confidentiality claim about data at rest depends on this decision.

## Decision drivers

1. Real page-level encryption on all 9 DBs. 2. Per-DB unique keys from `SecretsVault`.
3. Rotation (`rekey`) without data loss. 4. Fail-closed on master-key loss.

## Alternatives considered

| Alternative | Fit | Fatal flaw |
| :--- | :--- | :--- |
| Stock `Microsoft.Data.Sqlite` + `Password=` | Zero new deps | **Encrypts nothing** — rejected belief, documented as false |
| SQLCipher provider (`SQLitePCLRaw.bundle_e_sqlcipher` or equivalent) | Real encryption, per-DB keys | Native binaries per OS/arch; licensing/distribution review needed; **unproven here** |
| File-level/OS-level encryption only (BitLocker/FileVault) | Transparent | Protects lost-disk, not running-host file reads; no per-DB keys; out of app control |
| Column-level AES in app code | Works with any provider | Schema/query complexity; does not cover indexes/WAL artifacts; partial story |

## Evidence and comparative analysis

- Verified: repo has no code/provider reference to test against.
- Vendor-documented (not tested here): stock provider has no encryption; SQLCipher
  providers add `Password=`-driven page encryption with `rekey` support.
- Unverified: exact provider package, version, .NET 10 + per-OS native support,
  license terms — see compatibility matrix.

## Decision and rationale

Encryption at rest is claimed **only** through a SQLCipher provider with unique
per-DB keys in `SecretsVault`. Until the PoC passes, the working assumption is that
database files are **readable by any local process with file access** — OS ACLs are
the only operative control.

## Consequences

+ Honest security posture; real encryption path. − Native-binary matrix to own;
− rotation/recovery procedures mandatory; − startup cost of key handling.

## Risks and mitigations

RISK-001. Mitigation is the PoC itself, plus documenting ACL-only posture meanwhile.

## Validation and acceptance criteria

PoC: open with correct key succeeds; wrong key fails; `rekey` rotation preserves data;
cold read of raw file shows no plaintext. Green → Accepted (and RISK-001 retired).

## Revisit triggers

PoC failure; provider abandonment/license conflict; measurable startup/durability cost.

## Related ADRs and source documents

ADR-0002 (which files); `03-atam/risk-register.md` RISK-001; `02-arc42/08` (vault/rotation).
