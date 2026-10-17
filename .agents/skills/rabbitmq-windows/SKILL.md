---
name: rabbitmq-windows
description: >-
  Installs, configures, troubleshoots, and runs RabbitMQ Server on Windows without requiring full admin elevation.
  Use when the user asks to install, start, fix "ERLANG_HOME not set correctly", manage, or debug RabbitMQ on Windows.
---

# RabbitMQ on Windows (Zero-Pain / Non-Admin & Service Guide)

This skill provides procedures for setting up, repairing, starting, and diagnosing **RabbitMQ Server** on Windows, including the portable Erlang OTP runtime setup, configuration overrides, background process execution, and credential management.

## Key Facts & Architecture

1. **Erlang Dependency:** RabbitMQ runs entirely on the Erlang VM. It cannot run without a matching Erlang/OTP version:
   * **RabbitMQ 4.x** (e.g. 4.3.6) requires **Erlang/OTP 27+** (e.g. 27.3.4.x).
   * **RabbitMQ 3.13.x** requires **Erlang/OTP 26** (e.g. 26.2.5.x).
2. **Process Names in Task Manager:**
   * There is **no** `rabbitmq.exe`.
   * RabbitMQ processes appear as **`erl.exe`** (the broker node) and **`epmd.exe`** (Erlang Port Mapper Daemon).
3. **Configuration Override:**
   * Do not edit files in `C:\Program Files\RabbitMQ Server\...` if non-elevated.
   * RabbitMQ natively loads `%APPDATA%\RabbitMQ\rabbitmq-env-conf.bat` on every CLI / server call. This is the official location to set `ERLANG_HOME`.

---

## Quick Reference Scripts

The skill provides ready-to-run PowerShell helper scripts in `scripts/`:

* **[start-rabbitmq.ps1](./scripts/start-rabbitmq.ps1)**: Launches RabbitMQ in an independent background process (WMI breakaway) and verifies health.
* **[status-rabbitmq.ps1](./scripts/status-rabbitmq.ps1)**: Displays PID, memory usage, open ports (5672, 15672), and node status.
* **[stop-rabbitmq.ps1](./scripts/stop-rabbitmq.ps1)**: Gracefully shuts down the broker via `rabbitmqctl stop`.
* **[install-erlang-portable.ps1](./scripts/install-erlang-portable.ps1)**: Downloads Erlang/OTP 26, extracts via 7-Zip, initializes `Install.exe`, and wires `rabbitmq-env-conf.bat`.

---

## Step-by-Step Procedures

### 1. Diagnosing Missing or Broken Erlang

If running `rabbitmq-server.bat` or `rabbitmqctl.bat status` reports:
```text
******************************
ERLANG_HOME not set correctly.
******************************
```

**Checklist:**
1. Check if `C:\Program Files\Erlang OTP` exists and whether it contains `bin\erl.exe`.
2. Check `[Environment]::GetEnvironmentVariable("ERLANG_HOME", "Machine")` and `User`.
3. If empty or missing, deploy portable Erlang/OTP 26 to user profile (`$HOME\ErlangOTP`).

### 2. Portable Erlang/OTP Deployment (Non-Admin & Auto-Update)

Use the helper script `install-erlang-portable.ps1` which automatically queries the GitHub API for the latest compatible release within the desired major branch (defaulting to branch `26` for RabbitMQ 3.13.x):

```powershell
# Auto-discovers the latest Erlang 26.x release from GitHub, downloads and configures it:
& ".\.agents\skills\rabbitmq-windows\scripts\install-erlang-portable.ps1"

# Or specify a custom major branch or exact version:
& ".\.agents\skills\rabbitmq-windows\scripts\install-erlang-portable.ps1" -MajorVersion 26
& ".\.agents\skills\rabbitmq-windows\scripts\install-erlang-portable.ps1" -Version "26.2.5.21"
```

Manual steps (if running without script):
1. Query latest release via GitHub API:
   ```powershell
   $releases = Invoke-RestMethod -Uri "https://api.github.com/repos/erlang/otp/releases?per_page=100" -Headers @{ "User-Agent" = "PowerShell" }
   $asset = ($releases | Where-Object { $_.tag_name -like "OTP-26*" } | Select-Object -First 1).assets | Where-Object { $_.name -like "otp_win64_26*.exe" } | Select-Object -First 1
   curl.exe -L -o "$env:TEMP\$($asset.name)" $asset.browser_download_url
   ```
2. Unpack with 7-Zip:
   ```powershell
   & "C:\Program Files\7-Zip\7z.exe" x "$env:TEMP\$($asset.name)" "-o$HOME\ErlangOTP" -y
   ```
3. Initialize the Erlang environment (creates `bin\erl.ini`, `bin\erl.exe`, boot scripts):
   ```powershell
   & "$HOME\ErlangOTP\Install.exe" -s
   ```
4. Test Erlang:
   ```powershell
   & "$HOME\ErlangOTP\bin\erl.exe" -version
   ```
5. Remove the temporary installer:
   ```powershell
   Remove-Item "$env:TEMP\$($asset.name)" -Force
   ```

### 3. Binding ERLANG_HOME via `rabbitmq-env-conf.bat`

Create or update `$env:APPDATA\RabbitMQ\rabbitmq-env-conf.bat`:
```bat
@echo off
set ERLANG_HOME=%USERPROFILE%\ErlangOTP
```
This ensures all RabbitMQ scripts automatically pick up the correct Erlang installation regardless of machine environment variables.

### 4. Running RabbitMQ as Detached Background Process

To ensure RabbitMQ does not get killed when terminal sessions close, launch it via WMI `Win32_Process`:

```powershell
$sBin = "C:\Program Files\RabbitMQ Server\rabbitmq_server-3.13.2\sbin"
$cmd = "cmd.exe /c `"`"$sBin\rabbitmq-server.bat`"`""
[wmiclass]"Win32_Process".Create($cmd, $sBin, $null)
```

Wait ~15 seconds for startup, then verify:
```powershell
& "$sBin\rabbitmqctl.bat" ping
```

### 5. Management UI and Credentials

* **Web UI URL:** [http://localhost:15672/](http://localhost:15672/)
* **AMQP Port:** `5672`
* **Default Credentials:**
  * Login: `guest`
  * Password: `guest`
  *(Note: `guest` is restricted to localhost connections only).*
* **List Users:**
  ```powershell
  & "$sBin\rabbitmqctl.bat" list_users
  ```
* **Reset Password:**
  ```powershell
  & "$sBin\rabbitmqctl.bat" change_password <username> "<new_password>"
  ```
* **Enable Management Plugin (if disabled):**
  ```powershell
  & "$sBin\rabbitmq-plugins.bat" enable rabbitmq_management
  ```

### 6. Log Inspection

* Main broker log: `%APPDATA%\RabbitMQ\log\rabbit@<HOSTNAME>.log`
* Crash dumps: `%APPDATA%\RabbitMQ\erl_crash.dump`
