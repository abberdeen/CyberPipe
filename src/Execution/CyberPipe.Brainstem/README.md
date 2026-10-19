# CyberPipe.Brainstem

`CyberPipe.Brainstem` is an isolated worker process designed to execute heavy, untrusted, or resource-intensive tasks (such as audio transcription with Whisper, document parsing, and script runners) outside the main Windows Service process.

## Features

- **Isolated Process**: Runs in a dedicated process managed by `BrainstemSupervisor` and sandboxed via `ISandboxLauncher` (TASK-023).
- **Framed IPC**: Communicates with the supervisor via Named Pipe IPC using the framed protocol defined in `BrainstemIpc`.
- **Periodic Heartbeat**: Transmits periodic heartbeat frames to signal liveness.
- **Built-in Handlers**:
  - `Ping`: Responds with "PONG" payload.
  - `Echo`: Echoes back input payload.
  - `Hang`: Test handler that suppresses heartbeat and sleeps to verify supervisor hang detection.
  - `Allocate`: Allocates memory chunks to verify Job Object memory quotas.
  - `Crash`: Simulates abnormal exit.
- **Graceful Shutdown**: Shuts down cleanly when receiving `Shutdown` frame or when the IPC pipe closes.
