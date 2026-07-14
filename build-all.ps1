# ============================================================================
# TypedPond — One-shot build script (run on Windows with .NET 8 SDK installed)
# ============================================================================
# Produces:
#   windows-app\installer\Output\TypedPondSetup.exe   (the Windows installer)
#   android-app\app\build\outputs\apk\release\*.apk   (the Android companion)
#
# Usage:
#   .\build-all.ps1                            # builds Windows only
#   .\build-all.ps1 -Android                   # builds both Windows + Android
#   .\build-all.ps1 -HmacSecret "my-secret"   # bakes the real secret into both
#
# Prerequisites:
#   - .NET 8 SDK (dotnet --version should print 8.x)
#   - Inno Setup 6 (for the Windows installer; skip with -NoInstaller)
#   - JDK 17 + Android SDK (for Android builds only; set ANDROID_HOME)
# ============================================================================

param(
    [switch]$Android,
    [switch]$NoInstaller,
    [string]$HmacSecret = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "`n=== TypedPond Build ===" -ForegroundColor Cyan

# ─── Windows ──────────────────────────────────────────────────────────────────

Write-Host "`n[1/5] Building Windows solution..." -ForegroundColor Yellow
Push-Location "$root\windows-app"

dotnet build TypedPond.sln -c Release
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

Write-Host "`n[2/5] Running tests..." -ForegroundColor Yellow
dotnet test TypedPond.sln -c Release --no-build --verbosity normal
if ($LASTEXITCODE -ne 0) { throw "Tests failed" }

Write-Host "`n[3/5] Publishing binaries..." -ForegroundColor Yellow
dotnet publish src\TypedPond.Service -c Release --no-build
dotnet publish src\TypedPond.LockScreen -c Release --no-build

if (-not $NoInstaller) {
    Write-Host "`n[4/5] Building installer (Inno Setup)..." -ForegroundColor Yellow
    $iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
    if (-not (Test-Path $iscc)) {
        Write-Warning "Inno Setup not found at $iscc — skipping installer."
        Write-Warning "Install from https://jrsoftware.org/isdl.php and re-run."
    } else {
        & $iscc installer\setup.iss
        if ($LASTEXITCODE -ne 0) { throw "Installer build failed" }
        Write-Host "  -> installer\Output\TypedPondSetup.exe" -ForegroundColor Green
    }
} else {
    Write-Host "`n[4/5] Skipping installer (--NoInstaller)." -ForegroundColor DarkGray
}

Pop-Location

# ─── Android (optional) ──────────────────────────────────────────────────────

if ($Android) {
    Write-Host "`n[5/5] Building Android APK..." -ForegroundColor Yellow
    Push-Location "$root\android-app"

    if (-not (Test-Path "app\google-services.json")) {
        Write-Warning "app\google-services.json not found — using placeholder."
        Write-Warning "The APK will compile but Firebase won't connect until you replace it."
        Copy-Item "$root\android-app\app\google-services.placeholder.json" "app\google-services.json"
    }

    $gradleArgs = @("assembleRelease")
    if ($HmacSecret) {
        $gradleArgs += "-PtypedpondHmacSecret=$HmacSecret"
    }

    if (Test-Path "gradlew.bat") {
        & .\gradlew.bat @gradleArgs
    } else {
        Write-Warning "gradlew.bat not found. Run 'gradle wrapper' first or install Gradle."
    }

    if ($LASTEXITCODE -ne 0) { throw "Android build failed" }

    $apk = Get-ChildItem "app\build\outputs\apk\release\*.apk" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($apk) {
        Write-Host "  -> $($apk.FullName)" -ForegroundColor Green
    }

    Pop-Location
} else {
    Write-Host "`n[5/5] Skipping Android (pass -Android to build the APK)." -ForegroundColor DarkGray
}

# ─── Summary ─────────────────────────────────────────────────────────────────

Write-Host "`n=== Build Complete ===" -ForegroundColor Green
Write-Host "Windows installer: windows-app\installer\Output\TypedPondSetup.exe"
if ($Android) {
    Write-Host "Android APK:       android-app\app\build\outputs\apk\release\"
}
Write-Host ""
Write-Host "NEXT STEPS:" -ForegroundColor Cyan
Write-Host "  1. Edit windows-app\src\TypedPond.Service\appsettings.json with your Firebase creds + HMAC secret"
Write-Host "  2. Replace android-app\app\google-services.json with the real one from Firebase Console"
Write-Host "  3. Run the installer as Administrator on the target Windows 11 machine"
Write-Host "  4. Follow the manual lockdown steps in BUILD.md (Standard User, shell replacement, etc.)"
Write-Host ""
