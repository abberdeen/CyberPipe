using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using CyberPipe.Contracts;

namespace CyberPipe.Security;

/// <summary>
/// Реализация <see cref="ISecretStore" /> по умолчанию на платформенном хранилище ОС:
/// Windows — Credential Manager, macOS — Keychain через security, Linux — Secret Service
/// через secret-tool. Секреты не записываются в файлы конфигурации CyberPipe.
/// </summary>
public sealed class PlatformSecretStore : ISecretStore
{
    private const string ServiceName = "CyberPipe";

    /// <inheritdoc />
    public bool IsPlatformStore => true;

    /// <inheritdoc />
    public Task SetAsync(string key, SecretValue value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        cancellationToken.ThrowIfCancellationRequested();

        if (OperatingSystem.IsWindows())
            WindowsSet(key, value.Reveal());
        else if (OperatingSystem.IsMacOS())
            RunTool("/usr/bin/security", ["add-generic-password", "-a", key, "-s", ServiceName, "-w", value.Reveal(), "-U"]);
        else if (OperatingSystem.IsLinux())
            RunTool("secret-tool", ["store", "--label=" + ServiceName, "service", "cyberpipe", "account", key], value.Reveal());
        else
            throw new PlatformNotSupportedException("Platform secret store is not supported on this OS.");

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<SecretValue?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();

        string? plain =
            OperatingSystem.IsWindows() ? WindowsGet(key)
            : OperatingSystem.IsMacOS() ? RunToolOutput("/usr/bin/security", ["find-generic-password", "-a", key, "-s", ServiceName, "-w"])
            : OperatingSystem.IsLinux() ? RunToolOutput("secret-tool", ["lookup", "service", "cyberpipe", "account", key])
            : throw new PlatformNotSupportedException("Platform secret store is not supported on this OS.");

        return Task.FromResult(plain is null ? null : SecretValue.FromPlainText(plain));
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();

        bool deleted =
            OperatingSystem.IsWindows() ? WindowsDelete(key)
            : OperatingSystem.IsMacOS() ? RunToolAllowFailure("/usr/bin/security", ["delete-generic-password", "-a", key, "-s", ServiceName])
            : OperatingSystem.IsLinux() ? RunToolAllowFailure("secret-tool", ["clear", "service", "cyberpipe", "account", key])
            : throw new PlatformNotSupportedException("Platform secret store is not supported on this OS.");

        return Task.FromResult(deleted);
    }

    private static void RunTool(string fileName, string[] args, string? stdin = null) =>
        _ = RunToolCore(fileName, args, stdin, throwOnFailure: true);

    private static string? RunToolOutput(string fileName, string[] args) =>
        RunToolCore(fileName, args, stdin: null, throwOnFailure: false)?.TrimEnd('\r', '\n');

    private static bool RunToolAllowFailure(string fileName, string[] args) =>
        RunToolCore(fileName, args, stdin: null, throwOnFailure: false) is not null;

    private static string? RunToolCore(string fileName, string[] args, string? stdin, bool throwOnFailure)
    {
        var start = new ProcessStartInfo(fileName)
        {
            RedirectStandardInput = stdin is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string arg in args)
            start.ArgumentList.Add(arg);

        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException($"Failed to start platform secret tool '{fileName}'.");
        if (stdin is not null)
        {
            process.StandardInput.Write(stdin);
            process.StandardInput.Close();
        }

        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            if (throwOnFailure)
                throw new InvalidOperationException($"Platform secret tool '{fileName}' failed with exit code {process.ExitCode}.");
            return null;
        }

        return output;
    }

    // ---- Windows Credential Manager (advapi32) ----

    private static string TargetName(string key) => ServiceName + "/" + key;

    private static void WindowsSet(string key, string plainText)
    {
        byte[] blob = Encoding.UTF8.GetBytes(plainText);
        IntPtr blobPtr = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, blobPtr, blob.Length);
            var credential = new NativeCredential
            {
                Type = 1, // CRED_TYPE_GENERIC
                TargetName = TargetName(key),
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = blobPtr,
                Persist = 2, // CRED_PERSIST_LOCAL_MACHINE
                UserName = Environment.UserName,
            };
            if (!CredWrite(ref credential, 0))
                throw new InvalidOperationException($"CredWrite failed with error {Marshal.GetLastWin32Error()}.");
        }
        finally
        {
            Marshal.FreeHGlobal(blobPtr);
        }
    }

    private static string? WindowsGet(string key)
    {
        if (!CredRead(TargetName(key), 1, 0, out IntPtr ptr))
        {
            int error = Marshal.GetLastWin32Error();
            if (error == 1168) // ERROR_NOT_FOUND
                return null;
            throw new InvalidOperationException($"CredRead failed with error {error}.");
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(ptr);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
                return string.Empty;

            byte[] blob = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, blob, 0, blob.Length);
            return Encoding.UTF8.GetString(blob);
        }
        finally
        {
            CredFree(ptr);
        }
    }

    private static bool WindowsDelete(string key)
    {
        if (CredDelete(TargetName(key), 1, 0))
            return true;

        int error = Marshal.GetLastWin32Error();
        if (error == 1168) // ERROR_NOT_FOUND
            return false;
        throw new InvalidOperationException($"CredDelete failed with error {error}.");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string? TargetName;
        public string? Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite([In] ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredFree")]
    private static extern void CredFree(IntPtr credential);
}
