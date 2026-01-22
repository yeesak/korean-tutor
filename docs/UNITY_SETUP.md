# Unity Setup Guide - Shadowing 3x Tutor

This guide walks you through setting up and running the Unity client for the Shadowing 3x Tutor.

## Prerequisites

- **Unity Hub** installed: https://unity.com/download
- **Unity 2021.3 LTS** or newer (2022.3 LTS recommended)
- **Backend server** running (see `LOCAL_RUN.md`)

## 1. Open the Project

1. Launch **Unity Hub**
2. Click **Add** (or **Open** in newer versions)
3. Navigate to `AI-demo/unity/` folder
4. Select the folder and click **Open**
5. If prompted to upgrade Unity version, select your installed version (2021.3+ recommended)
6. Wait for Unity to import all assets (may take a few minutes on first open)

## 2. Verify Project Settings

### Force Text Serialization (Important for Git)
1. Go to **Edit > Project Settings > Editor**
2. Ensure **Asset Serialization** is set to **Force Text**
3. Ensure **Version Control Mode** is **Visible Meta Files**

### Build Settings
1. Go to **File > Build Settings**
2. Verify both scenes are listed:
   - `Assets/Scenes/TutorRoom.unity` (index 0)
   - `Assets/Scenes/Result.unity` (index 1)
3. If missing, click **Add Open Scenes** after opening each scene

## 3. Configure Backend URL

The app supports 4 environments: **Local**, **LAN**, **Staging**, **Production**.

### For Editor Testing (Local)
1. In **Project** window, navigate to `Assets/Resources/`
2. Select **AppConfig** asset
3. Environment dropdown: **Local**
4. URL: `http://localhost:3000` (default)

### For Device Testing (LAN)
1. Find your computer's LAN IP:
   ```bash
   # macOS
   ifconfig | grep "inet " | grep -v 127.0.0.1
   # Example: 192.168.1.100

   # Windows
   ipconfig
   ```

2. In Unity or in the running app:
   - Open Settings (gear icon or press Escape)
   - Select environment: **LAN**
   - Enter your IP: `192.168.1.100`
   - Click "Test Connection" to verify

3. Requirements:
   - Device must be on same WiFi network
   - Backend must be running on your computer

### For Production (Deployed Backend)
1. Deploy backend to Render, Fly.io, or your VPS (see `docs/DEPLOY_BACKEND.md`)
2. Get your HTTPS URL: `https://your-api.example.com`
3. Update AppConfig:
   - Edit `_stagingUrl` or `_productionUrl` in AppConfig asset
   - Or configure at runtime via Settings panel

**Important:** Production builds default to the Production environment.

### Switching Environments at Runtime

**In Unity Editor:**
1. Select `Assets/Resources/AppConfig`
2. Right-click > **Log Configuration** to see current settings

**In Running App:**
1. Press **Escape** key or tap gear icon
2. Select environment from dropdown
3. Enter LAN IP if using LAN
4. Test connection to verify

Settings persist across app restarts (stored in PlayerPrefs).

### Environment URLs

| Environment | URL | Use Case |
|-------------|-----|----------|
| Local | `http://localhost:3000` | Editor testing |
| LAN | `http://{IP}:3000` | Device on same WiFi |
| Staging | `https://staging-api.example.com` | Pre-release |
| Production | `https://api.example.com` | Released app |

### Verify from Phone

After deploying backend and installing app on phone:

1. Open app on phone
2. Open Settings (gear icon)
3. Select **Production** or **Staging** environment
4. Tap **Test Connection**
5. Should show "Connected" (green)

Or test manually:
```bash
# From any device with internet
curl https://your-api.example.com/api/health

# Expected response:
# {"ok":true,"mockMode":false,"services":{"tts":"live",...}}
```

## 4. Test in Editor

1. Open **TutorRoom** scene: `Assets/Scenes/TutorRoom.unity`
2. Ensure backend is running: `cd backend && npm start`
3. Press **Play** in Unity Editor
4. Click **START** to hear the sentence 3 times
5. After 3 listens, click **RECORD** and speak
6. View results on the Result screen

### Troubleshooting Editor Testing
- **No audio playing**: Check Console for errors, verify backend is running
- **Microphone not working**: Go to **Edit > Project Settings > Player > Other Settings** and enable **Microphone Usage Description**
- **Network errors**: Check backend URL in AppConfig, ensure no firewall blocking

## 5. Build for Android

### Prerequisites
1. Install **Android Build Support** via Unity Hub (Add Modules)
2. Install **Android SDK** and **NDK** (Unity Hub can install these)

### Build Steps
1. **File > Build Settings**
2. Select **Android** platform
3. Click **Switch Platform** (if not already Android)
4. Player Settings (**Edit > Project Settings > Player**):
   - **Other Settings > Identification**:
     - Package Name: `com.shadowingtutor.app`
     - Minimum API Level: 24 (Android 7.0)
     - Target API Level: 34 or higher
   - **Other Settings > Configuration**:
     - Scripting Backend: IL2CPP (recommended) or Mono
     - Target Architectures: ARMv7, ARM64
5. Click **Build** or **Build and Run**

### Android Permissions
The AndroidManifest.xml already includes:
- `RECORD_AUDIO` - For microphone access
- `INTERNET` - For API calls
- `ACCESS_NETWORK_STATE` - For network status

Runtime permission is handled by `MicRecorder.cs`.

## 6. Build for iOS

### Prerequisites
1. macOS with **Xcode** installed
2. Apple Developer account (for device testing)
3. Install **iOS Build Support** via Unity Hub

### Build Steps
1. **File > Build Settings**
2. Select **iOS** platform
3. Click **Switch Platform**
4. Player Settings (**Edit > Project Settings > Player**):
   - **Other Settings > Identification**:
     - Bundle Identifier: `com.shadowingtutor.app`
     - Target minimum iOS Version: 13.0
   - **Other Settings > Configuration**:
     - Camera Usage Description: (leave empty)
     - Microphone Usage Description: `This app needs microphone access to record your voice for Korean pronunciation practice.`
5. Click **Build**
6. Open the generated Xcode project
7. In Xcode:
   - Select your Team in **Signing & Capabilities**
   - Connect your iOS device
   - Click **Run**

### iOS Permissions
The Info.plist includes:
- `NSMicrophoneUsageDescription` - Explains why mic access is needed

## 7. Project Structure

```
unity/
├── Assets/
│   ├── Plugins/
│   │   └── Android/
│   │       └── AndroidManifest.xml    # Android permissions
│   ├── Resources/
│   │   ├── AppConfig.asset            # Backend URL config
│   │   └── sentences.json             # Korean sentences
│   ├── Scenes/
│   │   ├── TutorRoom.unity            # Main tutoring screen
│   │   └── Result.unity               # Results screen
│   └── Scripts/
│       ├── AppConfig.cs               # Backend URL management
│       ├── SentenceRepo.cs            # Sentence loading/shuffling
│       ├── ShadowingState.cs          # Session state management
│       ├── ApiClient.cs               # HTTP request wrappers
│       ├── TtsPlayer.cs               # TTS playback (3x loop)
│       ├── MicRecorder.cs             # Microphone recording
│       ├── WavEncoder.cs              # WAV encoding
│       ├── SttClient.cs               # Speech-to-text client
│       ├── DiffHighlighter.cs         # Word diff highlighting
│       ├── FeedbackClient.cs          # Grok feedback client
│       ├── LipSyncController.cs       # Avatar lip sync
│       ├── TutorRoomController.cs     # Tutor screen logic
│       └── ResultController.cs        # Result screen logic
└── ProjectSettings/
    ├── ProjectSettings.asset          # Player settings
    ├── EditorSettings.asset           # Force text mode
    └── EditorBuildSettings.asset      # Scene list
```

## 8. Customization

### Change TTS Speed
In AppConfig asset or via code:
- `speedProfile = 1` - Slow (0.9x)
- `speedProfile = 2` - Normal (1.0x)
- `speedProfile = 3` - Fast (1.1x)

### Change Recording Duration
In AppConfig asset:
- `maxRecordingDuration` - Default 10 seconds

### Change Microphone Sample Rate
In AppConfig asset:
- `micSampleRate` - Default 16000 Hz

### Filter Sentences by Category
```csharp
SentenceRepo.Instance.SetCategory("daily");  // daily, travel, cafe, school, work
SentenceRepo.Instance.SetCategory("");       // all categories
```

### Replace Avatar
1. Import your 3D avatar model
2. In TutorRoom scene, replace the Avatar capsule
3. Assign the jaw bone to LipSyncController
4. Or configure blendshape index for mouth open

## 9. Checklist: Unity Editor Steps

After opening the project for the first time:

- [ ] **Edit > Project Settings > Editor**
  - [ ] Asset Serialization: Force Text
  - [ ] Version Control: Visible Meta Files

- [ ] **Edit > Project Settings > Player > Other Settings**
  - [ ] Company Name: ShadowingTutor
  - [ ] Product Name: Shadowing 3x Tutor

- [ ] **Edit > Project Settings > Player > Other Settings (iOS)**
  - [ ] Microphone Usage Description: Set appropriate text

- [ ] **File > Build Settings**
  - [ ] Add TutorRoom scene (index 0)
  - [ ] Add Result scene (index 1)

- [ ] **Assets/Resources/AppConfig**
  - [ ] Verify Backend Base Url: http://localhost:3000
  - [ ] Set Override Url for device testing (LAN IP)

- [ ] **Test in Editor**
  - [ ] Start backend: `cd backend && npm start`
  - [ ] Open TutorRoom scene
  - [ ] Press Play
  - [ ] Test full flow: Start > Listen 3x > Record > View Results

## 10. Common Issues

### "Connection refused" error
- Backend not running. Start it with `cd backend && npm start`
- Wrong URL in AppConfig. Check `http://localhost:3000`

### Microphone permission denied
- Android: Handled at runtime by MicRecorder
- iOS: Check Info.plist has NSMicrophoneUsageDescription
- Editor: Grant permission in system settings

### No audio from TTS
- Check Unity AudioListener is in scene (on Main Camera)
- Check AudioSource volume is not 0
- Check backend TTS endpoint is working

### Korean text not displaying
- Unity's default font supports Korean
- If using custom font, ensure it has Korean character support

### Build fails on Android
- Check Android SDK/NDK paths in Unity Preferences
- Ensure minimum API level is 24+
- Check package name format (lowercase, dots only)

### Build fails on iOS
- Requires macOS with Xcode
- Check Xcode is installed and up to date
- Sign with valid development team

## Support

For issues, check:
1. Unity Console for errors
2. Backend logs for API errors
3. Device logs (adb logcat / Xcode console)
