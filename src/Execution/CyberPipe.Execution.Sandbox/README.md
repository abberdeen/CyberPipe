# CyberPipe.Execution.Sandbox

`CyberPipe.Execution.Sandbox` implements `ISandboxLauncher` for Windows environments in accordance with ADR-008 and Q13 decisions.

## Architecture

The sandbox enforces process isolation and containment using three core Windows security primitives:

1. **Restricted Token (`CreateRestrictedToken`)**:
   - Strips dangerous privileges (`DISABLE_MAX_PRIVILEGE`).
   - Converts administrative/sensitive SIDs into deny-only SIDs.
   - Restricts primary token capabilities so child processes cannot acquire elevated rights.

2. **Job Object (`CreateJobObjectW` + `SetInformationJobObject`)**:
   - Limits active processes (`JOB_OBJECT_LIMIT_ACTIVE_PROCESS = 1`) to prevent fork bombs.
   - Limits commit memory per-process and per-job (`JOB_OBJECT_LIMIT_PROCESS_MEMORY`, `JOB_OBJECT_LIMIT_JOB_MEMORY`).
   - Kills all child processes when the Job Object handle is closed or terminated (`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`).
   - The process is created in a suspended state (`CREATE_SUSPENDED`), assigned to the Job Object (`AssignProcessToJobObject`), and only then resumed (`ResumeThread`). There is zero window where untrusted code can run outside the Job limits.

3. **Environment and Filesystem Isolation**:
   - Explicit environment allowlist — parent environment variables and secrets are never inherited by the child.
   - Deny-ACL / isolated working directory semantics preventing unauthorized writes.
   - Handles are strictly managed with safe RAII Win32 handle wrappers (`SafeFileHandle`, `SafeProcessHandle`, `SafeJobHandle`).

4. **Output Capture & Streaming**:
   - Stderr and Stdout are redirected via anonymous Win32 pipes.
   - Capture is bounded (e.g. default 10MB limit) to prevent memory exhaustion from runaway log output, adding truncation markers if exceeded.

## Contracts

Implements `ISandboxLauncher`:
```csharp
Task<SandboxLaunchResult> RunAsync(SandboxLaunchRequest request, CancellationToken ct);
```

Returns structured results without throwing exceptions across contract boundaries:
- `ExitCode`
- `TimedOut`
- `KilledByJobLimit`
- `Stdout`
- `Stderr`
- `Error` (`SandboxNotSupported`, `ExecutableNotFound`, `AccessDenied`, `LaunchFailed`, etc.)
