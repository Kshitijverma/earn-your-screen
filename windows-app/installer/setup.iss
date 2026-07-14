; ============================================================================
; TypedPond Windows Installer  --  Inno Setup 6 script
; ----------------------------------------------------------------------------
; Packages the TypedPond Windows Service (step gatekeeper + HTTP API) and the
; full-screen LockScreen shell into a single installer.
;
;   * TypedPond.Service    -> installed + registered as a Windows Service
;                             (auto-start, listens on TCP 8787 for the Android app)
;   * TypedPond.LockScreen -> installed + set as the custom Winlogon shell
;                             for the brother's (locked-down) user account
;
; This installer also lays down the anti-tamper measures: Task Manager is
; disabled via Group Policy, a firewall rule opens the LAN API port, and the
; brother's shell is replaced so he cannot reach the normal Windows desktop
; until his step goal unlocks the machine.
;
; NOTE: Inno Setup is Windows-only and cannot be compiled on Linux. Compile
; this on a Windows box with Inno Setup 6 (see ..\BUILD.md). It has been kept
; deliberately verbose so the anti-tamper wiring is auditable.
;
; IMPORTANT SECURITY / CONTEXT CAVEAT (read before shipping):
;   Several of the settings below (DisableTaskMgr, Winlogon\Shell) are PER-USER
;   settings that live in HKEY_CURRENT_USER. Inno Setup installers run elevated
;   as the ADMIN account, so "HKCU" during install = the ADMIN's hive, NOT the
;   brother's. To apply them to the brother you must EITHER:
;     (a) run the relevant step while logged in as the brother (his HKCU), or
;     (b) load the brother's hive (reg load HKU\Brother ...) and write there, or
;     (c) use the HKLM (machine-wide) equivalents, which affect ALL users.
;   This script uses the HKLM machine-wide DisableTaskMgr key by default (see
;   [Registry]) and documents the per-user Winlogon shell in [Code]. Adjust to
;   your account layout. There is no way to fully automate "the brother's
;   account" from a generic elevated installer without knowing his SID/username.
; ============================================================================

#define MyAppName "TypedPond"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "TypedPond"
#define MyServiceName "TypedPondService"
#define MyServiceExe "TypedPond.Service.exe"
#define MyLockScreenExe "TypedPond.LockScreen.exe"
#define MyApiPort "8787"

[Setup]
AppId={{5C66DBE4-2BB2-44D0-86AC-5F154D01C0CD}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName=C:\Program Files\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Anti-tamper: no per-user install location; force a fixed system path.
UsePreviousAppDir=no
; MUST be run as admin -- we register a Windows Service, write HKLM, and touch
; the firewall. Non-admin installs are rejected outright.
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
OutputBaseFilename=TypedPondSetup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; The service binaries are self-contained (SelfContained=true in the csproj),
; so no .NET runtime prerequisite is required on the target machine.

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; --- TypedPond.Service published output (self-contained, single-file, win-x64) ---
; Publish first with: dotnet publish src\TypedPond.Service -c Release
; The path below matches: <RuntimeIdentifier>win-x64</RuntimeIdentifier> +
; <TargetFramework>net8.0-windows</TargetFramework> in the csproj.
Source: "..\src\TypedPond.Service\bin\Release\net8.0-windows\win-x64\publish\*"; \
    DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; --- TypedPond.LockScreen published output (self-contained, single-file, win-x64) ---
; Publish with: dotnet publish src\TypedPond.LockScreen -c Release
Source: "..\src\TypedPond.LockScreen\bin\Release\net8.0-windows\win-x64\publish\*"; \
    DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
; Persistent data directory for the SQLite step store (see appsettings.json:
; "DataDirectory": "C:\\ProgramData\\TypedPond"). Created so the service can
; write its steps.db on first run even before ProgramData is otherwise touched.
Name: "{commonappdata}\{#MyAppName}"

[Registry]
; ----------------------------------------------------------------------------
; Disable Task Manager (anti-tamper). We use the MACHINE-WIDE (HKLM) Group
; Policy key so it applies regardless of which user is logged in. This is the
; HKLM equivalent called out in the task:
;   HKLM\Software\Microsoft\Windows\CurrentVersion\Policies\System\DisableTaskMgr = 1
;
; If you instead want to scope this to ONLY the brother's account, remove this
; entry and write the identical value under HKCU while logged in AS the brother
; (or under his loaded hive HKU\<brother-SID>). HKCU here would hit the admin.
;
; Flags: uninsdeletevalue -> the value is removed on uninstall, re-enabling
; Task Manager cleanly.
; ----------------------------------------------------------------------------
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Policies\System"; \
    ValueType: dword; ValueName: "DisableTaskMgr"; ValueData: 1; \
    Flags: uninsdeletevalue

[Run]
; ----------------------------------------------------------------------------
; 1) Register the Windows Service.
;    sc create <name> binPath= "<exe>" start= auto
;    NOTE the SC.EXE quirk: there MUST be a space AFTER each "=" and NO space
;    before it (binPath= "x", start= auto). The binPath is quoted because the
;    install path contains a space ("Program Files").
;    We wrap the whole binPath token in \" \" so cmd passes the quotes to sc.
; ----------------------------------------------------------------------------
Filename: "{sys}\sc.exe"; \
    Parameters: "create {#MyServiceName} binPath= ""\""{app}\{#MyServiceExe}\"""" start= auto DisplayName= ""TypedPond Step Gatekeeper"""; \
    Flags: runhidden waituntilterminated; \
    StatusMsg: "Registering the TypedPond Windows Service..."

; 2) Configure service recovery: on ANY failure, restart immediately (0 ms delay),
;    and never reset the failure counter (reset= 0). Keeps the gatekeeper alive
;    even if it is killed -- another anti-tamper layer.
;    sc failure <name> reset= 0 actions= restart/0
Filename: "{sys}\sc.exe"; \
    Parameters: "failure {#MyServiceName} reset= 0 actions= restart/0"; \
    Flags: runhidden waituntilterminated; \
    StatusMsg: "Setting TypedPond service recovery policy..."

; 3) Start the service now (it will also auto-start on every boot).
;    sc start <name>
Filename: "{sys}\sc.exe"; \
    Parameters: "start {#MyServiceName}"; \
    Flags: runhidden waituntilterminated; \
    StatusMsg: "Starting the TypedPond Windows Service..."

; 4) Firewall: allow inbound TCP 8787 so the Android companion app can POST
;    step counts to the service over the LAN.
;    netsh advfirewall firewall add rule name="TypedPond" dir=in action=allow protocol=TCP localport=8787
Filename: "{sys}\netsh.exe"; \
    Parameters: "advfirewall firewall add rule name=""{#MyAppName}"" dir=in action=allow protocol=TCP localport={#MyApiPort}"; \
    Flags: runhidden waituntilterminated; \
    StatusMsg: "Adding firewall rule for the TypedPond LAN API (TCP {#MyApiPort})..."

[UninstallRun]
; Reverse everything the installer set up, in reverse order.
; RunOnceId keeps each step idempotent across repeated uninstall attempts.

; Stop the service (ignore errors if already stopped).
Filename: "{sys}\sc.exe"; Parameters: "stop {#MyServiceName}"; \
    Flags: runhidden waituntilterminated; RunOnceId: "StopTypedPondSvc"

; Delete the service registration.
Filename: "{sys}\sc.exe"; Parameters: "delete {#MyServiceName}"; \
    Flags: runhidden waituntilterminated; RunOnceId: "DelTypedPondSvc"

; Remove the firewall rule.
Filename: "{sys}\netsh.exe"; \
    Parameters: "advfirewall firewall delete rule name=""{#MyAppName}"""; \
    Flags: runhidden waituntilterminated; RunOnceId: "DelTypedPondFw"

; Restore the machine-wide shell to explorer.exe (in case the shell was
; replaced machine-wide). Restoring HKLM Winlogon\Shell is safe; per-user
; restoration is handled in the [Code] uninstall step below if applicable.
Filename: "{sys}\reg.exe"; \
    Parameters: "add ""HKLM\Software\Microsoft\Windows NT\CurrentVersion\Winlogon"" /v Shell /t REG_SZ /d explorer.exe /f"; \
    Flags: runhidden waituntilterminated; RunOnceId: "RestoreShell"

; NOTE: the machine-wide DisableTaskMgr value is removed automatically by the
; [Registry] "uninsdeletevalue" flag, so no explicit uninstall step is needed
; for it. If you scoped DisableTaskMgr to the brother's HKCU manually, remove
; it there manually or in the [Code] uninstall step.

[Code]
{ ==========================================================================
  POST-INSTALL: apply the per-user settings that CANNOT be done from the
  generic [Registry]/[Run] sections because they must land in the BROTHER'S
  user hive, not the admin's.

  ------------------------------------------------------------------------
  !!! MUST RUN IN THE BROTHER'S USER CONTEXT !!!
  ------------------------------------------------------------------------
  The two settings below are PER-USER (HKEY_CURRENT_USER). This installer runs
  elevated as the ADMIN, so HKCU here is the admin's hive. To apply them to the
  brother you must do ONE of:
    (a) Log in as the brother and re-run only these registry writes, OR
    (b) Load his hive from the admin session:
          reg load HKU\Brother "C:\Users\<brother>\NTUSER.DAT"
          reg add "HKU\Brother\Software\Microsoft\Windows NT\CurrentVersion\Winlogon" ^
              /v Shell /t REG_SZ /d "C:\Program Files\TypedPond\TypedPond.LockScreen.exe" /f
          reg add "HKU\Brother\Software\Microsoft\Windows\CurrentVersion\Policies\System" ^
              /v DisableTaskMgr /t REG_DWORD /d 1 /f
          reg unload HKU\Brother
        (the brother must be logged OUT for his hive to be unloadable).

  The RegisterBrotherShell() helper below writes to the CURRENT user's HKCU. It
  is only correct if the installer is being run WHILE LOGGED IN AS THE BROTHER.
  By default it is NOT auto-called (see the commented call in
  CurStepChanged) precisely because the normal flow is an elevated admin
  install. Uncomment / adapt to your deployment reality.
  ========================================================================== }

const
  ShellKeyPath   = 'Software\Microsoft\Windows NT\CurrentVersion\Winlogon';
  PoliciesKey    = 'Software\Microsoft\Windows\CurrentVersion\Policies\System';
  LockScreenPath = 'C:\Program Files\TypedPond\TypedPond.LockScreen.exe';

{ Writes the brother's Winlogon\Shell (per-user) so the LockScreen exe becomes
  his shell instead of explorer.exe, AND disables Task Manager for him.
  Precondition: this code is executing under the BROTHER'S user account. }
procedure RegisterBrotherShell();
begin
  { HKCU here == the currently-logged-in user's hive. See the big caveat above. }
  RegWriteStringValue(HKEY_CURRENT_USER, ShellKeyPath, 'Shell', LockScreenPath);
  RegWriteDWordValue(HKEY_CURRENT_USER, PoliciesKey, 'DisableTaskMgr', 1);
end;

{ Restores the brother's shell back to explorer.exe and re-enables Task Manager.
  Precondition: executing under the brother's user account. }
procedure RestoreBrotherShell();
begin
  RegWriteStringValue(HKEY_CURRENT_USER, ShellKeyPath, 'Shell', 'explorer.exe');
  RegDeleteValue(HKEY_CURRENT_USER, PoliciesKey, 'DisableTaskMgr');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    { --------------------------------------------------------------------
      Uncomment the next line ONLY when this installer is being executed
      while logged in as the brother, so his HKCU is the target hive. In the
      normal elevated-admin flow, leave it commented and instead perform the
      manual "reg load HKU\Brother ..." steps documented above.
      -------------------------------------------------------------------- }
    // RegisterBrotherShell();

    { Show the admin the mandatory manual follow-up steps. }
    MsgBox(
      'TypedPond core install complete.' + #13#10 + #13#10 +
      'MANUAL STEPS THE ADMIN MUST STILL DO:' + #13#10 +
      '1. Make the brother''s account a STANDARD USER (not administrator):' + #13#10 +
      '     Settings > Accounts > Family & other users > change to Standard.' + #13#10 +
      '2. Set a SAFE MODE boot password so he cannot boot around the lock:' + #13#10 +
      '     bcdedit /set {default} safeboot minimal   (and set a BIOS/UEFI' + #13#10 +
      '     password; require a password to change boot options).' + #13#10 +
      '3. Set the brother''s Winlogon Shell to the LockScreen exe IN HIS HIVE' + #13#10 +
      '     (reg load HKU\Brother ...  -- see setup.iss [Code] comments), and' + #13#10 +
      '     set DisableTaskMgr=1 in his hive too.' + #13#10 +
      '4. Edit C:\Program Files\TypedPond\appsettings.json:' + #13#10 +
      '     - Fill in Firebase credentials (FirebaseUrl / ApiKey / UserId).' + #13#10 +
      '     - Set a strong HmacSecret.' + #13#10 +
      '5. The HmacSecret MUST be IDENTICAL in appsettings.json AND the Android' + #13#10 +
      '     companion app, or step submissions will be rejected.' + #13#10 + #13#10 +
      'See BUILD.md for the full checklist.',
      mbInformation, MB_OK);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    { If the shell/DisableTaskMgr were applied in the CURRENT user's HKCU
      (i.e. this uninstaller is running as the brother), undo them here.
      For hive-loaded (HKU\Brother) writes, undo them manually / via reg. }
    // RestoreBrotherShell();
  end;
end;
