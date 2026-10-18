# ADR-0005: Dual-Contour Named Pipe IPC Security and Loopback `Hangfire` Dashboard `OTT` Authentication

- **Status:** `Proposed`
- **Date:** 2026-10-18
- **Deciders:** CyberPipe Architecture Team
- **Source Plan Evidence:** [`02-architecture-diagram.md`](../../plans/02-architecture-diagram.md), [`05-security-agent.md`](../../plans/05-security-agent.md), [`12-assistant-capabilities-and-skills.md`](../../plans/12-assistant-capabilities-and-skills.md), [`15-cli-and-execution-modes.md`](../../plans/15-cli-and-execution-modes.md), [`17-adr-mvp.md`](../../plans/17-adr-mvp.md) (`ADR-009`)
- **Related Requirements:** `REQ-002`, `REQ-011`, `REQ-016`
- **Related Quality Scenarios (ATAM):** [`QAS-004`](../03-atam/quality-attribute-scenarios.md#qas-004), [`QAS-005`](../03-atam/quality-attribute-scenarios.md#qas-005)
- **Related Risks & Threats:** [`RISK-009`](../03-atam/risk-register.md#risk-009), [`THREAT-004`](../04-threat-model/threat-register.md#threat-004), [`THREAT-005`](../04-threat-model/threat-register.md#threat-005), [`THREAT-008`](../04-threat-model/threat-register.md#threat-008)
- **Verification Gate:** [`TEST-006`](../06-validation/assumptions-and-open-questions.md#test-006), [`TEST-011`](../06-validation/assumptions-and-open-questions.md#test-011)

---

## 1. Context and Problem Statement

CyberPipe exposes three local inter-process communication (IPC) and diagnostic endpoints on the host workstation:
1. **Contour 1 (External Local CLI IPC):** Connects the Native AOT CLI (`cyberpipe.exe`) and local coding agents to `CyberPipe.exe` via `\\.\pipe\CyberPipe.Ipc.{Username}`.
2. **Contour 2 (Internal Worker IPC):** Connects `BrainstemSupervisor` in `CyberPipe.exe` to child `CyberPipe.Brainstem.exe` processes via `\\.\pipe\CyberPipe.Brainstem.{InstanceGuid}`.
3. **Loopback Web UI (`127.0.0.1:5080/hangfire`):** Serves the embedded `Hangfire` scheduling dashboard to the Owner's local browser.

Our threat modeling and technical audit uncovered three specific security and API issues (`RISK-009`, `THREAT-004`, `THREAT-005`, `THREAT-008`):
- **Win32 `FILE_FLAG_FIRST_PIPE_INSTANCE` Pool Conflict (`RISK-009`):** `15-cli-and-execution-modes.md` specifies `PipeOptions.FirstPipeInstance` with an 8-connection pool on Contour 1. In Win32, passing `FILE_FLAG_FIRST_PIPE_INSTANCE` to pipe instances `2..8` causes `CreateNamedPipe` to fail with `ERROR_ACCESS_DENIED`.
- **Contour 2 Protobuf Frame Bombing (`THREAT-008`):** A compromised `Brainstem` worker sending a 4-byte length prefix of `0x7FFFFFFF` (`2 GB`) over Contour 2 will crash `CyberPipe.exe` if the buffer is allocated before validating the frame length.
- **Localhost Browser CSRF / DNS Rebinding (`THREAT-005`):** An unauthenticated `127.0.0.1:5080` dashboard can be triggered by malicious JavaScript in any open browser tab via cross-site requests or DNS rebinding.

---

## 2. Decision Drivers

- **`QAS-004`:** Achieve **`< 15 ms`** Native AOT CLI startup and **`p95 < 25 ms`** IPC round-trip latency across 8 concurrent CLI clients.
- **`THREAT-004` & `RISK-009`:** Prevent Named Pipe squatting while supporting multi-instance pipe pooling cleanly.
- **`THREAT-005`:** Protect `127.0.0.1:5080/hangfire` against browser CSRF, SSRF, and DNS rebinding without forcing the user to manage static passwords.
- **`THREAT-008`:** Bound Contour 2 IPC memory allocation strictly to **`<= 8 MB`** per frame.

---

## 3. Considered Options

1. **Option 1:** Localhost TCP/HTTP Sockets for All CLI and Worker IPC
2. **Option 2 (Chosen):** Dual-Contour OS Named Pipes / Unix Domain Sockets with Hardened DACLs + `8 MB` Protobuf Framing + Single-Use `60s OTT` Cookie Exchange for Loopback `127.0.0.1:5080`
3. **Option 3:** Mutual TLS (mTLS) over Localhost gRPC for All Contours

### Option Comparison Matrix

| Criterion | Option 1 (Localhost TCP) | Option 2 (Dual-Contour Named Pipes + Loopback `OTT`) | Option 3 (Localhost mTLS gRPC) |
| :--- | :--- | :--- | :--- |
| **OS Kernel Identity Enforcement (SID/UID)** | None (Any local process can connect to TCP port) | **Native OS Kernel DACL (`CurrentUserOnly` + Sandbox `Deny`)** | Requires local cert provisioning |
| **IPC Latency (`p95`)** | `~5–15 ms` | **`~1–3 ms` (Zero TCP stack overhead)** | `~5–20 ms` (TLS handshake) |
| **Resistance to Localhost SSRF** | Weak | **Strong (Named Pipes unreachable from browser; `OTT` + `Host` check on `5080`)** | Strong |

---

## 4. Decision Outcome

**Chosen Option:** **Option 2 — Dual-Contour Named Pipes (`CurrentUserOnly` DACLs + First-Handle `FirstPipeInstance` + `<= 8 MB` Frame Cap) and Single-Use `60s OTT` Authentication for `127.0.0.1:5080`.**

### Architectural Specification

#### 4.1 Contour 1: External CLI IPC (`\\.\pipe\CyberPipe.Ipc.{Username}`)
- **Protocol:** JSON-RPC 2.0 over Named Pipe (Windows) or `0600` Unix Domain Socket (`$XDG_RUNTIME_DIR/cyberpipe.sock` on Linux/macOS), using source-generated `System.Text.Json` (`JsonSerializerContext`) for Native AOT compatibility.
- **Anti-Squatting & Pool Semantics (`RISK-009` Fix):**
  - On daemon startup, `CyberPipe.Host` creates the **initial anchor pipe instance** with `PipeOptions.FirstPipeInstance | PipeOptions.Asynchronous` and `MaxNumberOfServerInstances = 8`. If this first handle creation throws `UnauthorizedAccessException` or `IOException`, a squatter or duplicate daemon already holds the pipe and startup aborts immediately.
  - Subsequent pooled server instances (`2..8`) are created with `PipeOptions.Asynchronous` (omitting `FirstPipeInstance`) sharing the exact same `PipeSecurity` descriptor.
- **DACL:** Grant `PipeAccessRights.ReadWrite` strictly to the interactive Owner's Windows SID (`WindowsIdentity.GetCurrent().User`); explicitly add an `AccessControlType.Deny` rule for `S-1-15-2-1` (`AppContainer`) and the sandbox Restricted SID so child sandboxes cannot call the CLI pipe.

#### 4.2 Contour 2: Internal Worker IPC (`\\.\pipe\CyberPipe.Brainstem.{InstanceGuid}`)
- **Protocol:** 4-byte little-endian `uint32` length-prefixed `Google.Protobuf` binary frames.
- **Pre-Allocation Frame Size Guard (`THREAT-008` Fix):**
  ```csharp
  const uint MaxFrameBytes = 8 * 1024 * 1024; // 8 MB hard ceiling
  uint frameLength = BinaryPrimitives.ReadUInt32LittleEndian(headerSpan);
  if (frameLength == 0 || frameLength > MaxFrameBytes)
  {
      await KillWorkerImmediatelyAsync("Protocol violation: oversized Protobuf frame");
      throw new InvalidDataException($"Frame length {frameLength} exceeds 8 MB ceiling.");
  }
  ```
- **DACL:** Scoped exclusively to the parent `CyberPipe.exe` Owner SID and the specific child worker's `AppContainer`/Restricted SID (`ADR-0004`).

#### 4.3 Loopback Hangfire Dashboard (`http://127.0.0.1:5080/hangfire`) (`THREAT-005` Fix)
1. Kestrel binds **exclusively** to `IPAddress.Loopback` (`127.0.0.1:5080`).
2. **DNS Rebinding Guard Middleware:** Rejects any HTTP request whose `Host` header is not exactly `127.0.0.1:5080` or `localhost:5080` with `400 Bad Request`.
3. **Single-Use `OTT` Exchange:** Running `cyberpipe dashboard open` requests a 256-bit cryptographically random token (`32 bytes` from `RandomNumberGenerator.Fill`, Base64Url-encoded, `60 s` TTL) over Contour 1 IPC. The first `GET /hangfire?ott=<token>` atomically invalidates the token (`Interlocked.Exchange`) and sets an `HttpOnly; SameSite=Strict; Path=/hangfire` session cookie signed with an ephemeral in-memory HMAC key.

---

## 5. Consequences and Trade-Offs

### Positive Consequences (`+`)
- **`+` Sub-25ms Native AOT CLI Round-Trip (`QAS-004`):** Named Pipes + source-generated JSON-RPC achieve near-instantaneous CLI responsiveness.
- **`+` Eliminates Pipe Squatting & Frame Bombs (`THREAT-004`, `THREAT-008`):** First-handle `FirstPipeInstance` and the `8 MB` header check protect both IPC contours.

### Negative Consequences and Trade-Offs (`-`)
- **`-` OS-Specific Pipe ACL Code:** Requires conditional compilation / platform strategy (`PipeSecurity` on Windows vs `chmod 0600` on Unix Domain Sockets).

---

## 6. Validation and Compliance Checks

- **`TEST-006` (Contour 1 Concurrency & Anti-Squatting):**
  1. Pre-open a rogue pipe with the same name and assert `CyberPipe.Host` fails closed on startup.
  2. Run 8 concurrent `cyberpipe status --json` clients and assert `100%` success with `p95 < 25 ms`.
- **`TEST-011` (Hangfire `OTT` & DNS Rebinding Security):**
  Assert `GET /hangfire` without `OTT` returns `401`, replay of a consumed `OTT` returns `401`, and `Host: evil.com:5080` returns `400`.
