# Threat Model — Scope, Assets, Trust Boundaries

## Scope

In scope: Telegram/UI/CLI ingress, 4-tier router, guards, budget governor, sanitizer,
skills, Netrunner DAG engine, sandboxes + Brainstem worker, IPC (both circuits),
SQLite estate + vault, scheduler/dashboard. Out of scope: Telegram/LLM-provider
infrastructure internals, user endpoint hygiene beyond documented procedures.

## Assets (C/I/A)

| Asset | Confidentiality | Integrity | Availability |
| :--- | :---: | :---: | :---: |
| API keys, bot tokens, DB passwords (`A-01`) | High | High | Medium |
| Personal notes, CRM, finances, meeting transcripts (`A-02`) | High | High | Medium |
| Host integrity (no wipe/ransom/persistence) (`A-03`) | — | Highest | High |
| Audio environment (mic/speakers, no eavesdrop/blast) (`A-04`) | High | Medium | Low |
| Token budget / money (`A-05`) | Medium | High | Medium |
| Task/checkpoint data, DAG artifacts (`A-06`) | Medium | High | Medium |
| Model routing decisions (no silent cloud diversion) (`A-07`) | Low | High | Medium |

## Trust boundaries

- `TB-01` Untrusted chat → host. Enforced by: whitelist `TelegramUserId` + Owner/Guest
  roles + no-raw-shell + typed skills only.
- `TB-02` Daemon → cloud LLM. Enforced by: `SanitizingChatClient` + budget governor +
  tool pruning/stripping. (Local Ollama path never leaves the PC.)
- `TB-03` Daemon → Brainstem/sandbox. Enforced by: OS isolation profiles + Job/cgroup
  limits + manifest check + PID/nonce IPC + single-use parser processes.
- `TB-04` Untrusted content → tool use. Enforced by: `TrustSource` tagging + RAG
  trust filter + hard tool stripping + Owner-confirmed writes.
- `TB-05` UI/CLI → daemon. Enforced by: first-instance pipe guard + loopback-only
  dashboard + OTT/session cookies + per-command role authorization.
- `TB-06` Sandbox → host filesystem. Enforced by: `EphemeralWorkspace` + read-only
  bindings + canonical-path checks + `IsolatedRunWorkspaceOnly` manifest scope.

## Actors and capabilities

| Actor | Capability |
| :--- | :--- |
| Outside attacker | Send arbitrary messages/files/links; cannot authenticate as Owner (assumes Telegram ID verification holds — see THREAT-001) |
| Malicious/compromised contact | Forward weaponized content through a trusted chat |
| Malicious document/site | Prompt injection, parser exploits, exfiltration attempts from inside sandbox |
| Guest | Limited commands; must never reach Tier 3, budgets, secrets |
| Compromised cloud model output | Treated as untrusted (taint + stripping apply) |
| Local malware (out of scope to stop, in scope to not assist) | Must not gain anything from CyberPipe artifacts beyond what disk ACLs already deny |
