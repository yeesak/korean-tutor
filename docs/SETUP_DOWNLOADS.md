# AI-demo Setup: Downloads and Installation Guide

This document contains **exact downloads and installation steps** for the Shadowing 3x Tutor project.

---

## 1. Node.js LTS + npm

Node.js is required to run the backend server.

**Download URL:**
```
https://nodejs.org/en/download/
```

**Recommended Version:** Node.js 20.x LTS (or latest LTS)

**Installation:**
- **macOS/Windows:** Download the installer and run it
- **macOS (Homebrew):**
  ```bash
  brew install node@20
  ```
- **Ubuntu/Debian:**
  ```bash
  curl -fsSL https://deb.nodesource.com/setup_20.x | sudo -E bash -
  sudo apt-get install -y nodejs
  ```

**Verify Installation:**
```bash
node -v
npm -v
```

---

## 2. Git

Git is required for version control.

**Download URL:**
```
https://git-scm.com/downloads
```

**Installation:**
- **macOS:**
  ```bash
  brew install git
  ```
  Or install Xcode Command Line Tools: `xcode-select --install`
- **Windows:** Download installer from URL above
- **Ubuntu/Debian:**
  ```bash
  sudo apt-get install git
  ```

**Verify Installation:**
```bash
git --version
```

---

## 3. Unity Hub + Unity Editor

Unity is required to build the mobile app.

### 3.1 Unity Hub

**Download URL:**
```
https://unity.com/download
```

Direct download links:
```
macOS: https://public-cdn.cloud.unity3d.com/hub/prod/UnityHubSetup.dmg
Windows: https://public-cdn.cloud.unity3d.com/hub/prod/UnityHubSetup.exe
```

### 3.2 Unity Editor

**Recommended Version:** Unity 2022.3 LTS (Long Term Support)

**Installation via Unity Hub:**
1. Open Unity Hub
2. Go to "Installs" tab
3. Click "Install Editor"
4. Select **Unity 2022.3.x LTS** (latest patch)
5. Add the following modules during installation:
   - **Android Build Support**
     - Android SDK & NDK Tools
     - OpenJDK
   - **iOS Build Support** (macOS only)

### 3.3 Required Unity Packages

These packages should be enabled in your Unity project (via Package Manager):

| Package | Purpose |
|---------|---------|
| TextMeshPro | UI text rendering |
| Input System | Modern input handling (optional) |

TextMeshPro is usually included by default. To verify:
1. Window > Package Manager
2. Search for "TextMeshPro"
3. If not installed, click "Install"

---

## 4. Android Build Toolchain

### 4.1 Android Studio (Optional but Recommended)

**Download URL:**
```
https://developer.android.com/studio
```

Direct download:
```
https://redirector.gvt1.com/edgedl/android/studio/install/2023.2.1.23/android-studio-2023.2.1.23-mac.dmg
```

**Note:** If you installed Android SDK & NDK via Unity Hub, Android Studio is optional. However, it provides useful debugging tools.

### 4.2 Android SDK (via Unity Hub)

When installing Unity Editor, ensure you check:
- **Android SDK & NDK Tools**
- **OpenJDK**

Unity Hub will install these automatically to:
```
macOS: /Applications/Unity/Hub/Editor/2022.3.x/PlaybackEngines/AndroidPlayer/
Windows: C:\Program Files\Unity\Hub\Editor\2022.3.x\PlaybackEngines\AndroidPlayer\
```

### 4.3 Manual SDK Installation (if needed)

**Android SDK Command Line Tools:**
```
https://developer.android.com/studio#command-tools
```

**Required SDK Components:**
```bash
sdkmanager "platform-tools"
sdkmanager "platforms;android-33"
sdkmanager "build-tools;33.0.2"
sdkmanager "ndk;25.2.9519653"
```

### 4.4 OpenJDK

Unity bundles OpenJDK. If you need standalone:

**Download URL:**
```
https://adoptium.net/temurin/releases/
```

**Recommended Version:** OpenJDK 11 or 17 LTS

---

## 5. iOS Build Toolchain (macOS Only)

### 5.1 Xcode

**Download URL (Mac App Store):**
```
https://apps.apple.com/us/app/xcode/id497799835
```

**Alternative (Apple Developer):**
```
https://developer.apple.com/download/all/
```

**Recommended Version:** Xcode 15.x (latest stable)

**Post-Installation:**
```bash
# Accept license
sudo xcodebuild -license accept

# Install command line tools
xcode-select --install
```

### 5.2 CocoaPods

CocoaPods is required for iOS dependency management.

**Installation:**
```bash
sudo gem install cocoapods
pod setup
```

**Alternative (Homebrew):**
```bash
brew install cocoapods
```

**Verify Installation:**
```bash
pod --version
```

---

## 6. Microphone Permissions Setup

### 6.1 Android Microphone Permissions

**File:** `unity/Assets/Plugins/Android/AndroidManifest.xml`

Add the following permission:
```xml
<uses-permission android:name="android.permission.RECORD_AUDIO" />
<uses-permission android:name="android.permission.INTERNET" />
```

**Unity Player Settings:**
1. Edit > Project Settings > Player
2. Android tab > Other Settings
3. Ensure "Internet Access" is set to "Require"

**Runtime Permission Request (Android 6.0+):**
The app will request microphone permission at runtime. This is handled in the Unity C# code.

### 6.2 iOS Microphone Permissions

**Info.plist Entry Required:**

Unity automatically adds this when you use `Microphone` class, but verify:

**Key:** `NSMicrophoneUsageDescription`
**Value:** `This app requires microphone access for speech recognition in shadowing exercises.`

**Unity Player Settings:**
1. Edit > Project Settings > Player
2. iOS tab > Other Settings
3. Add "Microphone Usage Description" in the Configuration section

**Manual plist edit (if needed):**
```xml
<key>NSMicrophoneUsageDescription</key>
<string>This app requires microphone access for speech recognition in shadowing exercises.</string>
```

---

## 7. Environment Variables Setup

Create a `.env` file in the `backend/` directory:

```bash
cp backend/.env.example backend/.env
```

**Required API Keys (for production mode):**

| Variable | Service | Get Key URL |
|----------|---------|-------------|
| ELEVENLABS_API_KEY | ElevenLabs TTS | `https://elevenlabs.io/` |
| OPENAI_API_KEY | OpenAI Whisper STT | `https://platform.openai.com/api-keys` |
| XAI_API_KEY | xAI Grok | `https://console.x.ai/` |

**MOCK Mode:**
If any API key is missing or empty, the backend automatically runs in MOCK mode, returning deterministic fake responses. This prevents any API costs during development.

---

## 8. Quick Verification Checklist

Run these commands to verify your setup:

```bash
# Node.js
node -v          # Should show v20.x.x or higher

# npm
npm -v           # Should show 10.x.x or higher

# Git
git --version    # Should show git version 2.x.x

# Unity (check Unity Hub is installed)
# Open Unity Hub and verify Editor 2022.3.x LTS is installed

# CocoaPods (macOS only)
pod --version    # Should show 1.x.x

# Xcode (macOS only)
xcodebuild -version  # Should show Xcode 15.x
```

---

## 9. Troubleshooting

### Unity can't find Android SDK
1. Edit > Preferences > External Tools
2. Uncheck "Android SDK Tools Installed with Unity"
3. Set custom path to your Android SDK

### CocoaPods install fails
```bash
sudo gem install -n /usr/local/bin cocoapods
```

### Xcode command line tools missing
```bash
sudo xcode-select --reset
xcode-select --install
```

### Node permission errors
```bash
sudo chown -R $(whoami) ~/.npm
```

---

## Next Steps

After completing all installations:
1. Clone or navigate to the project directory
2. Follow `docs/LOCAL_RUN.md` for running the project locally
