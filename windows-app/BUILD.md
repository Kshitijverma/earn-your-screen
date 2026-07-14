# TypedPond -- Windows App Build & Packaging

This document covers building the TypedPond Windows components (the background
Service and the LockScreen shell), running the tests, and producing the
`TypedPondSetup.exe` installer with Inno Setup.

TypedPond is a step-goal gatekeeper: the **Service** runs as a Windows Service,
tracks the brother's daily step count (fed by an Android companion app over the
LAN on TCP 8787 and/or Firebase), and keeps the machine locked until the goal
is met. The **LockScreen** runs as the brother's Winlogon shell (replacing
`explorer.exe`) so the normal desktop is unreachable until unlock.

---

## 1. Prerequisites

| Requirement | Notes |
|-------------|-------|
| **Windows 11** (x64) | Build and (especially) installer compilation are Windows-only. The service and lock screen target `net8.0-windows` / `win-x64`. |
| **.NET 8 SDK** | https://dotnet.microsoft.com/download/dotnet/8.0 -- verify with `dotnet --version` (should print `8.x`). |
| **Inno Setup 6** | https://jrsoftware.org/isdl.php -- provides `ISCC.exe` (the command-line compiler) and the `Inno Setup Compiler` IDE. Required only to build the installer. |
| Admin rights | Needed to *run* the installer (it registers a Windows Service, writes HKLM, and adds a firewall rule). Not needed just to build. |

The service and lock screen are published **self-contained** (`SelfContained=true`,
`PublishSingleFile=true` in their `.csproj`), so target machines do **not** need
the .NET runtime installed.

---

## 2. Solution layout

```
windows-app/
  TypedPond.sln                 <- solution referencing all four projects
  BUILD.md                      <- this file
  installer/
    setup.iss                   <- Inno Setup installer script
  src/
    TypedPond.Core/             <- shared library (config, step store, HMAC, Firebase, unlock logic)
    TypedPond.Service/          <- Windows Service + HTTP API (Kestrel on :8787)
    TypedPond.LockScreen/       <- WPF full-screen lock shell
    TypedPond.Tests/            <- xUnit tests for Core
```

---

## 3. Build

From the `windows-app/` directory.

Restore + build the whole solution:

```powershell
dotnet build TypedPond.sln -c Release
```

---

## 4. Run tests

```powershell
dotnet test TypedPond.sln -c Release
```

`TypedPond.Tests` is an xUnit project referencing `TypedPond.Core`; it is not
packable (`IsPackable=false`) and is **not** shipped in the installer.

---

## 5. Publish the shippable binaries

The installer consumes the **publish** output of the Service and LockScreen.
Publish both in `Release` (the RID and framework come from each `.csproj`, so no
extra `-r` flag is required, but it is shown for clarity):

```powershell
# Windows Service (self-contained, single-file, win-x64)
dotnet publish src\TypedPond.Service -c Release
#   -> src\TypedPond.Service\bin\Release\net8.0-windows\win-x64\publish\

# LockScreen shell (self-contained, single-file, win-x64)
dotnet publish src\TypedPond.LockScreen -c Release
#   -> src\TypedPond.LockScreen\bin\Release\net8.0-windows\win-x64\publish\
```

These two `publish\` folders are exactly the paths referenced by the `[Files]`
section of `installer/setup.iss`. Publish before compiling the installer.

---

## 6. Build the installer

With Inno Setup 6 installed, from `windows-app/`:

```powershell
# Command-line compiler (ISCC.exe is typically under
#   C:\Program Files (x86)\Inno Setup 6\ISCC.exe)
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\setup.iss
```

or open `installer\setup.iss` in the Inno Setup Compiler IDE and press
**Build > Compile**.

Output: `installer\Output\TypedPondSetup.exe` (unless you change
`OutputBaseFilename` / output dir in the script).

Run `TypedPondSetup.exe` **as administrator** on the target Windows 11 machine.
It will:

1. Install both apps to `C:\Program Files\TypedPond\`.
2. Register the `TypedPondService` Windows Service (auto-start) and start it.
3. Set the service recovery policy to restart on any failure.
4. Add a firewall rule allowing inbound TCP 8787 (LAN API for the Android app).
5. Disable Task Manager machine-wide (Group Policy `DisableTaskMgr`).
6. Show a reminder dialog listing the manual steps below.

The uninstaller stops & deletes the service, removes the firewall rule, restores
the machine shell to `explorer.exe`, and re-enables Task Manager.

---

## 7. Manual configuration (REQUIRED -- installer cannot do these)

These steps are **mandatory** and must be done by the admin after install.
The installer prints them in a dialog and they are documented in `setup.iss`.

### 7.1 Firebase credentials & HMAC secret

Edit `C:\Program Files\TypedPond\appsettings.json`:

```jsonc
{
  "TypedPond": {
    "StepGoal": 10000,
    "FirebaseUrl": "https://YOUR-PROJECT.firebaseio.com",  // <- fill in
    "FirebaseApiKey": "YOUR-API-KEY",                       // <- fill in
    "FirebaseUserId": "BROTHER-UID",                        // <- fill in
    "HmacSecret": "CHANGE-THIS-SECRET",                     // <- set a strong secret
    "LocalHttpPort": 8787,
    ...
  }
}
```

> **The `HmacSecret` MUST be byte-for-byte identical** in this
> `appsettings.json` **and** in the Android companion app. The Service uses it
> to verify (HMAC) the step submissions coming from the phone; a mismatch means
> every submission is rejected and the machine can never unlock.

Restart the service after editing:

```powershell
sc stop TypedPondService
sc start TypedPondService
```

### 7.2 Lock down the brother's account

The following are **per-user** and cannot be reliably automated from the generic
elevated installer (it runs as the admin, so `HKCU` = admin's hive). Do them
against the brother's account:

1. **Make the brother a Standard User** (not administrator):
   `Settings > Accounts > Family & other users >` select his account `> Change
   account type > Standard User`. This prevents him from stopping the service or
   editing `Program Files`.

2. **Set the brother's Winlogon shell** to the LockScreen exe so he gets the
   lock screen instead of the desktop. In **his** user hive:
   ```powershell
   # With the brother logged OUT, from an admin prompt:
   reg load HKU\Brother "C:\Users\<brother>\NTUSER.DAT"
   reg add "HKU\Brother\Software\Microsoft\Windows NT\CurrentVersion\Winlogon" /v Shell /t REG_SZ /d "C:\Program Files\TypedPond\TypedPond.LockScreen.exe" /f
   reg add "HKU\Brother\Software\Microsoft\Windows\CurrentVersion\Policies\System" /v DisableTaskMgr /t REG_DWORD /d 1 /f
   reg unload HKU\Brother
   ```
   (The installer already sets `DisableTaskMgr` machine-wide via HKLM; the
   per-hive line above is only needed if you prefer per-user scoping.)

3. **Set a Safe Mode boot password** so he cannot boot into Safe Mode to bypass
   the custom shell / service:
   ```powershell
   # Force the default boot entry to require Safe Mode config protection, and
   # set BIOS/UEFI + boot-menu passwords so boot options cannot be changed.
   bcdedit /set {default} safeboot minimal
   ```
   Also set a **BIOS/UEFI supervisor password** and disable booting from
   USB/optical media so the lockdown cannot be circumvented at boot time.

---

## 8. Anti-tamper summary

| Layer | Mechanism | Set by |
|-------|-----------|--------|
| Service always running | Auto-start + `sc failure ... restart/0` | Installer |
| Can't kill via Task Manager | `DisableTaskMgr=1` (HKLM Group Policy) | Installer |
| No normal desktop | Winlogon `Shell` = LockScreen exe | Manual (brother's hive) |
| Can't edit files / stop service | Brother is a Standard User | Manual |
| Can't boot around it | Safe Mode + BIOS/UEFI password | Manual |
| Phone can reach service | Firewall allow inbound TCP 8787 | Installer |
| Submissions authenticated | Shared `HmacSecret` (Windows + Android) | Manual |
