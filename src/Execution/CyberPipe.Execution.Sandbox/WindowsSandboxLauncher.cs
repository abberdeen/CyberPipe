using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using CyberPipe.Contracts;
using CyberPipe.Execution.Sandbox.Native;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace CyberPipe.Execution.Sandbox;

/// <summary>
/// Реализация <see cref="ISandboxLauncher"/> с использованием Windows Restricted Token,
/// Job Object с жестким лимитом памяти и ограничением окружения (TASK-023).
/// </summary>
public sealed class WindowsSandboxLauncher : ISandboxLauncher
{
    private const int DefaultMemoryLimitMb = 512;
    private const int MaxOutputSizeBytes = 10 * 1024 * 1024; // 10 MB stdout/stderr limit

    private readonly CyberPipeOptions _options;
    private readonly ILogger<WindowsSandboxLauncher>? _logger;

    public WindowsSandboxLauncher(CyberPipeOptions options, ILogger<WindowsSandboxLauncher>? logger = null)
    {
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SandboxLaunchResult> RunAsync(SandboxLaunchRequest request, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new SandboxLaunchResult(
                ExitCode: null,
                TimedOut: false,
                KilledByJobLimit: false,
                Stdout: string.Empty,
                Stderr: string.Empty,
                Error: "SandboxNotSupported: Windows Sandbox is only supported on Windows.");
        }

        // 1. Validation
        if (string.IsNullOrWhiteSpace(request.ExecutablePath))
        {
            return new SandboxLaunchResult(null, false, false, "", "", "ExecutableNotFound: ExecutablePath must not be empty.");
        }

        if (!Path.IsPathRooted(request.ExecutablePath))
        {
            return new SandboxLaunchResult(null, false, false, "", "", $"ExecutableNotFound: Path must be absolute: {request.ExecutablePath}");
        }

        if (!File.Exists(request.ExecutablePath))
        {
            return new SandboxLaunchResult(null, false, false, "", "", $"ExecutableNotFound: File does not exist: {request.ExecutablePath}");
        }

        string workingDir = string.IsNullOrWhiteSpace(request.WorkingDirectory)
            ? Path.GetDirectoryName(request.ExecutablePath) ?? Directory.GetCurrentDirectory()
            : request.WorkingDirectory;

        if (!Directory.Exists(workingDir))
        {
            try
            {
                Directory.CreateDirectory(workingDir);
            }
            catch (Exception ex)
            {
                return new SandboxLaunchResult(null, false, false, "", "", $"AccessDenied: Failed to create working directory: {ex.Message}");
            }
        }

        long memoryLimitMb = request.MaxMemoryMb
            ?? _options.Sandbox.MemoryLimitMb
            ?? DefaultMemoryLimitMb;

        long memoryLimitBytes = memoryLimitMb * 1024 * 1024;
        int timeoutMs = request.TimeoutMs > 0 ? request.TimeoutMs : _options.Sandbox.TimeoutSeconds * 1000;

        _logger?.LogInformation(
            "Launching sandboxed process: {Executable}, MemoryLimit: {Memory}MB, Timeout: {Timeout}ms",
            request.ExecutablePath,
            memoryLimitMb,
            timeoutMs);

        return await ExecuteSandboxedProcessAsync(
            request,
            workingDir,
            memoryLimitBytes,
            timeoutMs,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<SandboxLaunchResult> ExecuteSandboxedProcessAsync(
        SandboxLaunchRequest request,
        string workingDir,
        long memoryLimitBytes,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        // 2. Create Job Object with memory and process limits
        using SafeJobHandle jobHandle = WindowsNative.CreateJobObject(IntPtr.Zero, null);
        if (jobHandle.IsInvalid)
        {
            int err = Marshal.GetLastWin32Error();
            return new SandboxLaunchResult(null, false, false, "", "", $"LaunchFailed: CreateJobObject failed with error {err}.");
        }

        var limitInfo = new WindowsNative.JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new WindowsNative.JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = WindowsNative.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                    | WindowsNative.JOB_OBJECT_LIMIT_ACTIVE_PROCESS
                    | WindowsNative.JOB_OBJECT_LIMIT_PROCESS_MEMORY
                    | WindowsNative.JOB_OBJECT_LIMIT_JOB_MEMORY,
                ActiveProcessLimit = 1
            },
            ProcessMemoryLimit = (UIntPtr)memoryLimitBytes,
            JobMemoryLimit = (UIntPtr)memoryLimitBytes
        };

        if (!WindowsNative.SetInformationJobObject(
            jobHandle,
            WindowsNative.JobObjectExtendedLimitInformation,
            ref limitInfo,
            (uint)Marshal.SizeOf<WindowsNative.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()))
        {
            int err = Marshal.GetLastWin32Error();
            return new SandboxLaunchResult(null, false, false, "", "", $"LaunchFailed: SetInformationJobObject failed with error {err}.");
        }

        // 3. Create Restricted Token. Trusted workers opt out: DISABLE_MAX_PRIVILEGE
        // removes traverse checking and the .NET host cannot load.
        SafeProcessTokenHandle? restrictedToken = null;
        if (request.RestrictToken && WindowsNative.OpenProcessToken(
            WindowsNative.GetCurrentProcess(),
            WindowsNative.TOKEN_DUPLICATE | WindowsNative.TOKEN_ASSIGN_PRIMARY | WindowsNative.TOKEN_QUERY | WindowsNative.TOKEN_ADJUST_DEFAULT,
            out SafeProcessTokenHandle primaryToken))
        {
            using (primaryToken)
            {
                if (WindowsNative.CreateRestrictedToken(
                    primaryToken,
                    WindowsNative.DISABLE_MAX_PRIVILEGE,
                    DisableSidCount: 0,
                    SidsToDisable: IntPtr.Zero,
                    DeletePrivilegeCount: 0,
                    PrivilegesToDelete: IntPtr.Zero,
                    RestrictedSidCount: 0,
                    SidsToRestrict: IntPtr.Zero,
                    out SafeProcessTokenHandle token))
                {
                    restrictedToken = token;
                }
            }
        }

        // 4. Create anonymous pipes for Stdout and Stderr
        var saPipe = new WindowsNative.SECURITY_ATTRIBUTES
        {
            nLength = Marshal.SizeOf<WindowsNative.SECURITY_ATTRIBUTES>(),
            bInheritHandle = true,
            lpSecurityDescriptor = IntPtr.Zero
        };

        if (!WindowsNative.CreatePipe(out IntPtr hStdOutRead, out IntPtr hStdOutWrite, ref saPipe, 0) ||
            !WindowsNative.CreatePipe(out IntPtr hStdErrRead, out IntPtr hStdErrWrite, ref saPipe, 0))
        {
            restrictedToken?.Dispose();
            int err = Marshal.GetLastWin32Error();
            return new SandboxLaunchResult(null, false, false, "", "", $"LaunchFailed: CreatePipe failed with error {err}.");
        }

        // Prevent child from inheriting read ends
        WindowsNative.SetHandleInformation(hStdOutRead, WindowsNative.HANDLE_FLAG_INHERIT, 0);
        WindowsNative.SetHandleInformation(hStdErrRead, WindowsNative.HANDLE_FLAG_INHERIT, 0);

        // 5. Build environment block (allowlist only, no secrets from parent process)
        IntPtr envBlock = CreateEnvironmentBlock(request.Environment);

        // 6. Format command line
        string commandLine = BuildCommandLine(request.ExecutablePath, request.Arguments);

        var startupInfo = new WindowsNative.STARTUPINFO
        {
            cb = Marshal.SizeOf<WindowsNative.STARTUPINFO>(),
            dwFlags = WindowsNative.STARTF_USESTDHANDLES,
            hStdOutput = hStdOutWrite,
            hStdError = hStdErrWrite,
            hStdInput = IntPtr.Zero
        };

        var processAttributes = new WindowsNative.SECURITY_ATTRIBUTES
        {
            nLength = Marshal.SizeOf<WindowsNative.SECURITY_ATTRIBUTES>(),
            bInheritHandle = true
        };

        var threadAttributes = new WindowsNative.SECURITY_ATTRIBUTES
        {
            nLength = Marshal.SizeOf<WindowsNative.SECURITY_ATTRIBUTES>(),
            bInheritHandle = true
        };

        uint creationFlags = WindowsNative.CREATE_SUSPENDED
            | WindowsNative.CREATE_UNICODE_ENVIRONMENT
            | WindowsNative.CREATE_NO_WINDOW;

        bool processCreated = false;
        WindowsNative.PROCESS_INFORMATION pi = default;

        try
        {
            if (restrictedToken != null && !restrictedToken.IsInvalid)
            {
                processCreated = WindowsNative.CreateProcessAsUserW(
                    restrictedToken,
                    lpApplicationName: null,
                    lpCommandLine: commandLine,
                    ref processAttributes,
                    ref threadAttributes,
                    bInheritHandles: true,
                    dwCreationFlags: creationFlags,
                    lpEnvironment: envBlock,
                    lpCurrentDirectory: workingDir,
                    ref startupInfo,
                    out pi);
            }

            // Fallback to CreateProcessW with suspended thread if CreateProcessAsUser fails due to privileges
            if (!processCreated)
            {
                processCreated = WindowsNative.CreateProcessW(
                    lpApplicationName: null,
                    lpCommandLine: commandLine,
                    ref processAttributes,
                    ref threadAttributes,
                    bInheritHandles: true,
                    dwCreationFlags: creationFlags,
                    lpEnvironment: envBlock,
                    lpCurrentDirectory: workingDir,
                    ref startupInfo,
                    out pi);
            }

            if (!processCreated)
            {
                int err = Marshal.GetLastWin32Error();
                return new SandboxLaunchResult(null, false, false, "", "", $"LaunchFailed: Process creation failed with error {err}.");
            }

            // 7. Assign process to Job Object BEFORE resuming thread
            if (!WindowsNative.AssignProcessToJobObject(jobHandle, pi.hProcess))
            {
                int err = Marshal.GetLastWin32Error();
                WindowsNative.TerminateJobObject(jobHandle, 1);
                return new SandboxLaunchResult(null, false, false, "", "", $"LaunchFailed: AssignProcessToJobObject failed with error {err}.");
            }

            // Close write ends in parent process so EOF can be read on pipe
            WindowsNative.CloseHandle(hStdOutWrite);
            hStdOutWrite = IntPtr.Zero;
            WindowsNative.CloseHandle(hStdErrWrite);
            hStdErrWrite = IntPtr.Zero;

            // 8. Resume the suspended thread to begin execution in Job
            WindowsNative.ResumeThread(pi.hThread);

            // 9. Read stdout and stderr asynchronously
            using var stdoutStream = new FileStream(new SafeFileHandle(hStdOutRead, ownsHandle: true), FileAccess.Read);
            using var stderrStream = new FileStream(new SafeFileHandle(hStdErrRead, ownsHandle: true), FileAccess.Read);
            hStdOutRead = IntPtr.Zero; // Ownership transferred to FileStream
            hStdErrRead = IntPtr.Zero;

            Task<string> stdoutTask = ReadStreamWithLimitAsync(stdoutStream, MaxOutputSizeBytes);
            Task<string> stderrTask = ReadStreamWithLimitAsync(stderrStream, MaxOutputSizeBytes);

            // 10. Wait for process exit or timeout
            bool timedOut = false;
            bool killedByJob = false;

            using var timeoutCts = new CancellationTokenSource(timeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var processWaitTask = Task.Run(() =>
            {
                uint waitResult = WindowsNative.WaitForSingleObject(pi.hProcess, WindowsNative.INFINITE);
                return waitResult == WindowsNative.WAIT_OBJECT_0;
            }, linkedCts.Token);

            Task completedTask = await Task.WhenAny(processWaitTask, Task.Delay(timeoutMs, cancellationToken)).ConfigureAwait(false);

            if (completedTask != processWaitTask)
            {
                timedOut = true;
                WindowsNative.TerminateJobObject(jobHandle, 1067); // Terminate entire job
            }

            // Await stdout and stderr reading
            string stdout = string.Empty;
            string stderr = string.Empty;
            try
            {
                stdout = await stdoutTask.ConfigureAwait(false);
                stderr = await stderrTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Exception while reading process streams");
            }

            // Query exit code
            int? exitCode = null;
            if (WindowsNative.GetExitCodeProcess(pi.hProcess, out uint code))
            {
                exitCode = (int)code;
                // Check if killed by Job Object quota violation (0xC0000017 / 0xC0000409 / exit code 713 / STATUS_QUOTA_EXCEEDED)
                if (code is 0xC0000017 or 0xC0000409 or 713 or 0x0000042B)
                {
                    killedByJob = true;
                }
            }

            // Check Job accounting info
            var accounting = new WindowsNative.JOBOBJECT_BASIC_ACCOUNTING_INFORMATION();
            if (WindowsNative.QueryInformationJobObject(
                jobHandle,
                WindowsNative.JobObjectBasicAccountingInformation,
                ref accounting,
                (uint)Marshal.SizeOf<WindowsNative.JOBOBJECT_BASIC_ACCOUNTING_INFORMATION>(),
                out _))
            {
                if (accounting.TotalTerminatedProcesses > 0 && !timedOut && exitCode != 0)
                {
                    killedByJob = true;
                }
            }

            return new SandboxLaunchResult(
                ExitCode: exitCode,
                TimedOut: timedOut,
                KilledByJobLimit: killedByJob,
                Stdout: stdout,
                Stderr: stderr,
                Error: null);
        }
        finally
        {
            if (hStdOutRead != IntPtr.Zero) WindowsNative.CloseHandle(hStdOutRead);
            if (hStdOutWrite != IntPtr.Zero) WindowsNative.CloseHandle(hStdOutWrite);
            if (hStdErrRead != IntPtr.Zero) WindowsNative.CloseHandle(hStdErrRead);
            if (hStdErrWrite != IntPtr.Zero) WindowsNative.CloseHandle(hStdErrWrite);

            if (pi.hProcess != IntPtr.Zero) WindowsNative.CloseHandle(pi.hProcess);
            if (pi.hThread != IntPtr.Zero) WindowsNative.CloseHandle(pi.hThread);

            restrictedToken?.Dispose();
            if (envBlock != IntPtr.Zero) Marshal.FreeHGlobal(envBlock);
        }
    }

    private static IntPtr CreateEnvironmentBlock(IReadOnlyDictionary<string, string> environment)
    {
        if (environment.Count == 0)
        {
            // Empty environment block: "\0\0"
            byte[] emptyBytes = [0, 0, 0, 0];
            IntPtr ptr = Marshal.AllocHGlobal(emptyBytes.Length);
            Marshal.Copy(emptyBytes, 0, ptr, emptyBytes.Length);
            return ptr;
        }

        var sb = new StringBuilder();
        foreach (KeyValuePair<string, string> pair in environment.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append(pair.Key).Append('=').Append(pair.Value).Append('\0');
        }
        sb.Append('\0');

        byte[] bytes = Encoding.Unicode.GetBytes(sb.ToString());
        IntPtr block = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, block, bytes.Length);
        return block;
    }

    private static string BuildCommandLine(string executable, IReadOnlyList<string> arguments)
    {
        var sb = new StringBuilder();
        sb.Append('"').Append(executable).Append('"');

        foreach (string arg in arguments)
        {
            sb.Append(' ');
            if (arg.Contains(' ') || arg.Contains('"') || arg.Length == 0)
            {
                sb.Append('"').Append(arg.Replace("\"", "\\\"")).Append('"');
            }
            else
            {
                sb.Append(arg);
            }
        }

        return sb.ToString();
    }

    private static async Task<string> ReadStreamWithLimitAsync(Stream stream, int maxBytes)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var buffer = new char[4096];
        var sb = new StringBuilder();
        int totalChars = 0;
        int read;

        while ((read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(false)) > 0)
        {
            if (totalChars + read > maxBytes)
            {
                int remaining = maxBytes - totalChars;
                if (remaining > 0)
                {
                    sb.Append(buffer, 0, remaining);
                }
                sb.Append("\n[TRUNCATED: Exceeded maximum output limit]");
                break;
            }

            sb.Append(buffer, 0, read);
            totalChars += read;
        }

        return sb.ToString();
    }
}
