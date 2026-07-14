# TypedPond — Getting the Installer + APK

Two ways to get the ready-to-install files. Pick whichever is easier for you.

---

## Option A: GitHub Actions (recommended — zero local setup)

1. Push this repo to a GitHub repository (public or private).
2. Go to the **Actions** tab — the "Build TypedPond" workflow runs automatically on push.
3. When it finishes (about 5 minutes), click the completed run and download the artifacts:
   - **TypedPondSetup-windows** — contains `TypedPondSetup.exe`
   - **TypedPondCompanion-debug** — contains `app-debug.apk` (unsigned, for testing)
   - **TypedPondCompanion-release** — contains the release APK (unsigned)

That's it. No SDK needed on your machine.

### Optional: bake in the real HMAC secret via CI

Go to repo Settings > Secrets and variables > Actions > New repository secret:
- Name: `HMAC_SECRET`
- Value: your chosen shared secret

The workflow will embed it into the APK's BuildConfig automatically.

---

## Option B: Build locally on a Windows machine

### Prerequisites

| Tool | Install |
|------|---------|
| .NET 8 SDK | https://dotnet.microsoft.com/download/dotnet/8.0 |
| Inno Setup 6 | https://jrsoftware.org/isdl.php |
| JDK 17 | https://adoptium.net/ (for Android APK only) |
| Android Studio | https://developer.android.com/studio (for Android APK only) |

### Steps

```powershell
# Clone the repo
git clone <your-repo-url> typed-pond
cd typed-pond

# Build everything (Windows installer only):
.\build-all.ps1

# Build everything INCLUDING the Android APK:
.\build-all.ps1 -Android -HmacSecret "your-shared-secret-here"
```

Output:
- `windows-app\installer\Output\TypedPondSetup.exe`
- `android-app\app\build\outputs\apk\release\app-release-unsigned.apk`

---

## After you have the files

### Install on the Windows laptop

1. Copy `TypedPondSetup.exe` to the brother's laptop.
2. Right-click > **Run as administrator**.
3. Follow the prompts; when it finishes, a dialog lists the manual steps.
4. Edit `C:\Program Files\TypedPond\appsettings.json`:
   - Set `FirebaseUrl`, `FirebaseApiKey`, `FirebaseUserId` from your Firebase project.
   - Set `HmacSecret` to the same value used in the Android build.
5. Lock down his account (see `windows-app/BUILD.md` section 7 for the full checklist).

### Install the APK on his phone

1. Transfer the APK to the phone (USB, Google Drive, AirDroid, etc.).
2. Enable "Install from unknown sources" for whatever app you use to open it.
3. Open the APK and install.
4. Launch TypedPond Companion, grant Health Connect permissions, sign in with Firebase.

---

## Firebase setup (one-time, 5 minutes)

1. Go to https://console.firebase.google.com/ and create a new project.
2. Enable **Authentication > Email/Password**.
3. Create two users: one for the brother (writes steps) and one for you (admin, sets goals).
4. Enable **Realtime Database** (Start in test mode, then deploy the rules from `firebase/database.rules.json`).
5. Download `google-services.json` from Project Settings > Your Apps > Android and place it at `android-app/app/google-services.json`.
6. Note the RTDB URL and API key for `appsettings.json`.
