# CyberPipe.Brainstem.Supervisor

`CyberPipe.Brainstem.Supervisor` provides process lifecycle supervision, fault detection, and resilient restart management for the isolated `CyberPipe.Brainstem` worker.

## Responsibilities

- **Worker Launching**: Starts the worker using `ISandboxLauncher` (TASK-023) with restricted tokens, Job Object memory quotas, and clean environment isolation.
- **IPC Management**: Hosts a Named Pipe server stream for framed protocol communication (`BrainstemIpc`).
- **Heartbeat Monitoring**: Actively tracks liveness via periodic heartbeats. Detects hung or unresponsive workers and triggers immediate termination and recovery.
- **Exponential Backoff Restart Policy**:
  - Configurable max attempts (`MaxRestartAttempts`), initial backoff (`InitialBackoffSeconds`), and cap (`MaxBackoffSeconds`).
  - Resets consecutive failure counter after a stable run threshold (e.g. 30 seconds).
  - Transitions to `Faulted` state upon exhausting restart attempts, publishing `BrainstemRestartExhausted` to `IMessageBus` to avoid infinite restart loops.
- **Job Dispatching**: Dispatches `BrainstemJobRequest` instances, awaiting `BrainstemJobResult` with timeout and cancellation enforcement.
- **Graceful Shutdown**: Sends a `Shutdown` frame to the worker, waits for clean exit, and terminates via Job Object if unresponsive. Zero orphaned processes.
