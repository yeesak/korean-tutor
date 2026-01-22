# Build Guide: Android APK/AAB & iOS Archive

Complete steps to build release-ready apps for Android and iOS.

---

## Prerequisites

### Required Software

| Platform | Requirement |
|----------|-------------|
| **Both** | Unity 2022.3 LTS with Android/iOS Build Support modules |
| **Android** | Android SDK (API 24+), NDK, JDK 11 |
| **iOS** | macOS with Xcode 14+, Apple Developer account |

### Install Unity Modules

```
Unity Hub > Installs > [Your Unity Version] > Add Modules
> Android Build Support (includes SDK, NDK, JDK)
> iOS Build Support
```

---

## Part 1: Android Build

### 1.1 Configure Player Settings

**File > Build Settings > Android > Player Settings**

#### Other Settings

| Setting | Value | Notes |
|---------|-------|-------|
| **Package Name** | `com.shadowingtutor.app` | Unique identifier |
| **Version** | `1.0.0` | Semantic version |
| **Bundle Version Code** | `1` | Increment for each release |
| **Minimum API Level** | `24` (Android 7.0) | Required for modern audio |
| **Target API Level** | `34` (Android 14) | Google Play requirement |
| **Scripting Backend** | `IL2CPP` | Required for ARM64 |
| **Target Architectures** | `ARM64` | Modern devices only |
| **Internet Access** | `Require` | For API calls |

#### Configuration

| Setting | Value |
|---------|-------|
| **Api Compatibility Level** | `.NET Standard 2.1` |
| **Allow downloads over HTTP** | `true` (for local testing) |

### 1.2 Generate Android Keystore (One-time)

**Required for release builds.** Keep this file secure!

```bash
# Navigate to your project
cd /path/to/AI-demo/unity

# Create keystore directory
mkdir -p Keystore

# Generate keystore (remember your passwords!)
keytool -genkey -v \
  -keystore Keystore/shadowingtutor.keystore \
  -alias shadowingtutor \
  -keyalg RSA \
  -keysize 2048 \
  -validity 10000

# You'll be prompted for:
# - Keystore password (remember this!)
# - Key password (can be same as keystore)
# - Your name, organization, etc.
```

**IMPORTANT:**
- Back up your keystore file securely
- Never commit keystore to git
- Add to `.gitignore`: `*.keystore`

### 1.3 Configure Keystore in Unity

**Edit > Project Settings > Player > Android > Publishing Settings**

1. Check **Custom Keystore**
2. Browse to `Keystore/shadowingtutor.keystore`
3. Enter Keystore Password
4. Select Alias: `shadowingtutor`
5. Enter Key Password

### 1.4 Build Debug APK

For testing on device without Play Store.

```
File > Build Settings
> Platform: Android
> Build System: Gradle
> Export Project: Unchecked
> Development Build: Checked (optional, for profiling)
> Click "Build"
> Save as: builds/ShadowingTutor-debug.apk
```

**Install on device:**
```bash
# Connect device with USB debugging enabled
adb install builds/ShadowingTutor-debug.apk
```

### 1.5 Build Release AAB (for Play Store)

```
File > Build Settings
> Platform: Android
> Build App Bundle (Google Play): Checked
> Development Build: Unchecked
> Click "Build"
> Save as: builds/ShadowingTutor-release.aab
```

**Verify AAB:**
```bash
# Check AAB structure
unzip -l builds/ShadowingTutor-release.aab | head -20
```

---

## Part 2: iOS Build

### 2.1 Configure Player Settings

**File > Build Settings > iOS > Player Settings**

#### Other Settings

| Setting | Value | Notes |
|---------|-------|-------|
| **Bundle Identifier** | `com.shadowingtutor.app` | Must match App Store Connect |
| **Version** | `1.0.0` | Display version |
| **Build** | `1` | Increment for each upload |
| **Target minimum iOS Version** | `13.0` | Wide device support |
| **Target SDK** | `Device SDK` | Not Simulator |
| **Scripting Backend** | `IL2CPP` | Required for iOS |
| **Target Architectures** | `ARM64` | Modern devices |

#### Capabilities

| Setting | Value |
|---------|-------|
| **Microphone Usage Description** | `This app needs microphone access to record your voice for Korean pronunciation practice.` |
| **Camera Usage Description** | (leave empty or remove) |

### 2.2 Build Xcode Project

```
File > Build Settings
> Platform: iOS
> Run in Xcode: Release (or Debug for testing)
> Development Build: Unchecked for release
> Click "Build"
> Choose folder: builds/iOS
```

This creates an Xcode project at `builds/iOS/Unity-iPhone.xcodeproj`.

### 2.3 Configure Xcode Project

1. Open `builds/iOS/Unity-iPhone.xcodeproj` in Xcode
2. Select **Unity-iPhone** target in the left sidebar
3. Go to **Signing & Capabilities** tab

#### Signing Configuration

| Setting | Value |
|---------|-------|
| **Team** | Select your Apple Developer Team |
| **Bundle Identifier** | `com.shadowingtutor.app` |
| **Automatically manage signing** | Checked (recommended) |

#### Add Capabilities (if needed)

Click **+ Capability** and add:
- (No special capabilities needed for this app)

### 2.4 Build Debug (Device Testing)

1. Connect iPhone via USB
2. Select your device in the Xcode toolbar
3. Click **Run** (▶️) or press `Cmd+R`
4. Accept "Trust Developer" on device if prompted

### 2.5 Build Archive (TestFlight/App Store)

1. Select **Any iOS Device** in toolbar (not a specific device)
2. **Product > Archive**
3. Wait for build to complete (opens Organizer)
4. In Organizer, select the archive
5. Click **Distribute App**
6. Choose **App Store Connect** > **Upload**
7. Follow prompts to upload to TestFlight

---

## Part 3: Environment Configuration

### 3.1 Environment URLs

Configure in Unity via **AppConfig** asset or runtime UI:

| Environment | URL | Use Case |
|-------------|-----|----------|
| **Local** | `http://localhost:3000` | Editor testing |
| **LAN** | `http://192.168.x.x:3000` | Device testing (same WiFi) |
| **Staging** | `https://staging-api.example.com` | Pre-release testing |
| **Production** | `https://api.example.com` | Release builds |

### 3.2 Switching Environments

**In Unity Editor:**
1. Select `Assets/Resources/AppConfig`
2. Change environment in Inspector dropdown
3. Or use runtime Settings UI (gear icon)

**In Built App:**
1. Open Settings (gear icon)
2. Select environment from dropdown
3. Setting persists across sessions (PlayerPrefs)

### 3.3 Release Build Checklist

Before building for release:

- [ ] Set environment to **Production** or **Staging**
- [ ] Backend URL uses **HTTPS** (required for iOS ATS)
- [ ] Development Build is **unchecked**
- [ ] Bundle version is **incremented**
- [ ] Test on physical device first

---

## Part 4: Common Errors & Fixes

### Microphone Permission Not Working

#### Android

**Symptom:** Mic doesn't record, no permission popup.

**Fix 1:** Verify AndroidManifest.xml
```xml
<!-- Should be in Plugins/Android/AndroidManifest.xml -->
<uses-permission android:name="android.permission.RECORD_AUDIO" />
<uses-permission android:name="android.permission.INTERNET" />
```

**Fix 2:** Request permission at runtime
```csharp
// Already handled in MicRecorder.cs
if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
{
    Permission.RequestUserPermission(Permission.Microphone);
}
```

**Fix 3:** For Android 11+, add to manifest:
```xml
<queries>
    <intent>
        <action android:name="android.speech.RecognitionService" />
    </intent>
</queries>
```

#### iOS

**Symptom:** Mic doesn't work, crash on record.

**Fix:** Verify Info.plist contains:
```xml
<key>NSMicrophoneUsageDescription</key>
<string>This app needs microphone access to record your voice for Korean pronunciation practice.</string>
```

This is set in Unity under: Player Settings > iOS > Other Settings > Microphone Usage Description

---

### ATS (App Transport Security) Errors on iOS

**Symptom:** Network requests fail silently or with error.

**Error in Xcode console:**
```
App Transport Security has blocked a cleartext HTTP connection
```

**Fix 1:** Use HTTPS for production (recommended)
```
Backend URL: https://api.example.com
```

**Fix 2:** For local testing only, add ATS exception

In Unity, create/edit `Assets/Plugins/iOS/Info.plist`:
```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>NSAppTransportSecurity</key>
    <dict>
        <key>NSAllowsLocalNetworking</key>
        <true/>
        <!-- For specific domain (testing only): -->
        <key>NSExceptionDomains</key>
        <dict>
            <key>192.168.1.100</key>
            <dict>
                <key>NSExceptionAllowsInsecureHTTPLoads</key>
                <true/>
            </dict>
        </dict>
    </dict>
</dict>
</plist>
```

**Note:** App Store may reject apps with broad ATS exceptions. Only use for local IPs.

---

### Localhost Not Reachable from Phone

**Symptom:** App works in Editor but not on device.

**Cause:** `localhost` means the device itself, not your computer.

**Fix 1:** Use your computer's LAN IP

```bash
# macOS
ifconfig | grep "inet " | grep -v 127.0.0.1
# Example: 192.168.1.100

# Windows
ipconfig
# Look for IPv4 Address
```

**Fix 2:** Update AppConfig
- Set environment to **LAN**
- Enter your computer's IP: `http://192.168.1.100:3000`

**Fix 3:** Ensure same network
- Phone and computer must be on same WiFi
- Corporate networks may block local traffic

**Fix 4:** Check firewall
```bash
# macOS: Allow incoming connections
# System Preferences > Security & Privacy > Firewall > Firewall Options
# Add Node.js or allow incoming connections

# Or temporarily disable firewall for testing
sudo pfctl -d
```

---

### CORS Errors

**Symptom in browser/WebGL:**
```
Access to fetch blocked by CORS policy
```

**Note:** Native mobile apps (Android/iOS) are NOT affected by CORS. This is a browser-only issue.

**For WebGL builds or browser testing:**

Backend already includes CORS middleware:
```javascript
// In backend/index.js
app.use(cors());
```

For specific origins:
```javascript
app.use(cors({
  origin: ['http://localhost:3000', 'https://yourdomain.com']
}));
```

---

### Mixed Content Errors

**Symptom:** HTTPS app can't call HTTP backend.

**Cause:** Browsers/apps block "mixed content" (HTTPS page calling HTTP API).

**Fix:** Use HTTPS for backend in production

```bash
# Option 1: Use a reverse proxy (nginx) with SSL
# Option 2: Deploy to cloud with HTTPS (Heroku, Railway, etc.)
# Option 3: Use ngrok for testing
ngrok http 3000
# Gives you: https://abc123.ngrok.io
```

---

### SSL Certificate Errors

**Symptom:** `SSL certificate problem: unable to get local issuer certificate`

**Cause:** Self-signed or invalid SSL certificate.

**Fix 1:** Use valid SSL certificate (Let's Encrypt)

**Fix 2:** For testing only, disable certificate validation (NOT for production)

In Unity, you can create a custom certificate handler, but this is not recommended for production apps.

---

### Build Fails: Gradle Error (Android)

**Symptom:**
```
Gradle build failed
CommandInvokationFailure: Gradle build failed
```

**Fix 1:** Update Gradle
```
Edit > Preferences > External Tools > Android
> Gradle Installed with Unity (checked)
> Or specify custom Gradle path
```

**Fix 2:** Clear Gradle cache
```bash
# macOS/Linux
rm -rf ~/.gradle/caches

# Windows
rmdir /s /q %USERPROFILE%\.gradle\caches
```

**Fix 3:** Check Android SDK
- Unity Hub > Installs > Add Modules > Android SDK & NDK Tools
- Or: Edit > Preferences > External Tools > Android SDK path

---

### Build Fails: Xcode Signing Error (iOS)

**Symptom:**
```
Signing for "Unity-iPhone" requires a development team
```

**Fix:**
1. Open Xcode project
2. Select Unity-iPhone target
3. Signing & Capabilities > Team > Select your team
4. Ensure Automatically manage signing is checked

**Symptom:**
```
No profiles for 'com.shadowingtutor.app' were found
```

**Fix:**
1. Log into developer.apple.com
2. Certificates, IDs & Profiles > Identifiers
3. Create App ID if not exists
4. Bundle ID must match exactly

---

### App Crashes on Launch (iOS)

**Symptom:** App immediately closes after splash screen.

**Fix 1:** Check minimum iOS version
- Player Settings > iOS > Target minimum iOS Version: 13.0

**Fix 2:** Check Xcode console for crash logs
- Window > Devices and Simulators > View Device Logs

**Fix 3:** Missing permission descriptions
- Ensure all required `NS*UsageDescription` keys are set

---

## Part 5: Quick Reference

### Android Build Commands

```bash
# Build APK from command line
/path/to/Unity -quit -batchmode -projectPath /path/to/AI-demo/unity \
  -executeMethod BuildScript.BuildAndroidAPK \
  -logFile build.log

# Install APK
adb install -r builds/ShadowingTutor.apk

# View logs
adb logcat -s Unity
```

### iOS Build Commands

```bash
# Build Xcode project from command line
/path/to/Unity -quit -batchmode -projectPath /path/to/AI-demo/unity \
  -executeMethod BuildScript.BuildiOS \
  -logFile build.log

# Build from Xcode command line
xcodebuild -project builds/iOS/Unity-iPhone.xcodeproj \
  -scheme Unity-iPhone \
  -configuration Release \
  -archivePath builds/ShadowingTutor.xcarchive \
  archive

# Export for App Store
xcodebuild -exportArchive \
  -archivePath builds/ShadowingTutor.xcarchive \
  -exportOptionsPlist ExportOptions.plist \
  -exportPath builds/AppStore
```

### Version Checklist

Before each release:

| Platform | Setting | Location |
|----------|---------|----------|
| **Android** | Version | Player Settings > Android > Other Settings |
| **Android** | Bundle Version Code | Player Settings > Android > Other Settings |
| **iOS** | Version | Player Settings > iOS > Other Settings |
| **iOS** | Build | Player Settings > iOS > Other Settings |

---

## Summary

| Build Type | Steps |
|------------|-------|
| **Android Debug APK** | Build Settings > Android > Build |
| **Android Release AAB** | Build Settings > Android > Build App Bundle ✓ > Build |
| **iOS Debug** | Build Settings > iOS > Build > Xcode > Run |
| **iOS TestFlight** | Build Settings > iOS > Build > Xcode > Archive > Distribute |
