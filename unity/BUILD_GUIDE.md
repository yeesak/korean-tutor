# Build Guide - Shadowing 3x Tutor

## Prerequisites

### Required Software
- **Unity 6** (6000.3.x) - [Unity Hub](https://unity.com/download)
- **iOS**: Xcode 15+ (macOS only)
- **Android**: Android SDK (API 34), JDK 17+

### Unity Modules Required
Install via Unity Hub > Installs > Add Modules:
- [x] Android Build Support
- [x] Android SDK & NDK Tools
- [x] iOS Build Support (macOS only)

### Verify Installation
```bash
# Check Android SDK
echo $ANDROID_HOME
# Should output something like: /Users/<name>/Library/Android/sdk

# Check Xcode (macOS)
xcode-select -p
# Should output: /Applications/Xcode.app/Contents/Developer
```

---

## Project Configuration

### Bundle IDs (Already Configured)
| Platform | Bundle ID |
|----------|-----------|
| iOS | `com.shadowingtutor.app` |
| Android | `com.shadowingtutor.app` |

### Permissions (Already Configured)
| Permission | iOS | Android |
|------------|-----|---------|
| Microphone | NSMicrophoneUsageDescription | RECORD_AUDIO |
| Internet | Automatic | INTERNET |
| Network State | N/A | ACCESS_NETWORK_STATE |

### Backend URLs
Edit `Assets/Resources/AppConfig.asset` in Unity Inspector:

| Environment | URL | When Used |
|-------------|-----|-----------|
| Local | `http://localhost:3000` | Editor only |
| LAN | `http://{IP}:3000` | Device testing |
| Staging | `https://staging-api.example.com` | Pre-release |
| Production | `https://api.example.com` | Release builds |

**IMPORTANT**: Before production release:
1. Update `_productionUrl` to your real backend URL
2. Update `_stagingUrl` if you have a staging server

---

## Android Build

### Step 1: Configure Signing (First Time Only)
1. Open Unity > Edit > Project Settings > Player > Android
2. Under "Publishing Settings":
   - Check "Custom Keystore"
   - Create new keystore: `Project Keystore > Create New`
   - Save as `user.keystore` in project root
   - Set alias and passwords

### Step 2: Build APK (Development)
```
1. File > Build Settings
2. Select "Android" platform
3. Click "Switch Platform" if needed
4. Check "Development Build" for testing
5. Click "Build"
6. Save as: Builds/ShadowingTutor-dev.apk
```

### Step 3: Build AAB (Release)
```
1. File > Build Settings
2. Select "Android" platform
3. Uncheck "Development Build"
4. Check "Build App Bundle (Google Play)"
5. Click "Build"
6. Save as: Builds/ShadowingTutor-release.aab
```

### Install on Device
```bash
# Development APK
adb install -r Builds/ShadowingTutor-dev.apk

# If multiple devices connected
adb devices
adb -s <device-id> install -r Builds/ShadowingTutor-dev.apk
```

---

## iOS Build

### Step 1: Configure Signing
1. Open Unity > Edit > Project Settings > Player > iOS
2. Under "Identification":
   - Signing Team ID: (your Apple Developer Team ID)
   - Automatic Signing: Enable (recommended)

### Step 2: Export Xcode Project
```
1. File > Build Settings
2. Select "iOS" platform
3. Click "Switch Platform" if needed
4. Click "Build"
5. Create folder: Builds/iOS
6. Wait for export to complete
```

### Step 3: Build in Xcode
```bash
# Open the exported project
open Builds/iOS/Unity-iPhone.xcodeproj
```

In Xcode:
1. Select "Unity-iPhone" target
2. Signing & Capabilities > Select your Team
3. Select your connected device
4. Product > Run (Cmd+R)

### Archive for App Store
```
1. Product > Archive
2. Distribute App > App Store Connect
3. Follow prompts to upload
```

---

## Backend Setup (CRITICAL)

### API Keys Required for Real Functionality

The backend requires API keys to function properly. **Without API keys, the backend runs in MOCK mode**:

| Service | Mock Behavior | API Key Needed |
|---------|---------------|----------------|
| TTS (ElevenLabs) | Plays sine wave tones ("beeps") | `ELEVENLABS_API_KEY` |
| STT (OpenAI Whisper) | Returns random Korean phrases, not your speech | `OPENAI_API_KEY` |
| Feedback (xAI Grok) | Returns generic feedback | `XAI_API_KEY` |

### Setting Up API Keys

1. Copy the example environment file:
   ```bash
   cd backend
   cp .env.example .env
   ```

2. Edit `.env` and add your API keys:
   ```
   ELEVENLABS_API_KEY=your_elevenlabs_key_here
   OPENAI_API_KEY=your_openai_key_here
   XAI_API_KEY=your_xai_key_here
   ```

3. Get API keys from:
   - ElevenLabs: https://elevenlabs.io/ (for TTS)
   - OpenAI: https://platform.openai.com/api-keys (for STT)
   - xAI: https://console.x.ai/ (for feedback)

### Verify Backend Status

After starting the backend, check the health endpoint:
```bash
curl http://localhost:3000/api/health
```

**Good response (real APIs configured):**
```json
{
  "ok": true,
  "mockMode": false,
  "services": { "tts": "live", "stt": "live", "feedback": "live" }
}
```

**Mock mode response (missing API keys):**
```json
{
  "ok": true,
  "mockMode": true,
  "services": { "tts": "mock", "stt": "mock", "feedback": "mock" }
}
```

---

## Backend Deployment

### For LAN Testing
```bash
# Find your Mac's IP
ifconfig | grep "inet " | grep -v 127.0.0.1

# Start backend on LAN
cd backend
HOST=0.0.0.0 node index.js
```

In Unity AppConfig:
1. Set Environment to "LAN"
2. Set LAN IP to your Mac's IP (e.g., 192.168.1.100)

### For Production
1. Deploy backend to cloud (Railway, Render, AWS, etc.)
2. Update `_productionUrl` in AppConfig.asset
3. Ensure HTTPS is configured
4. Build release version

---

## Smoke Test Checklist

### Pre-flight
- [ ] Backend is running (check `http://<backend>/api/health`)
- [ ] Device has microphone permissions enabled
- [ ] Device has internet connection

### Core Flow
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Launch app | TutorRoom scene loads, sentence displayed |
| 2 | Tap "Start/Listen" | TTS plays Korean sentence 3x |
| 3 | After 3 listens | Record button becomes active |
| 4 | Tap "Record" | Mic recording starts, waveform shows |
| 5 | Speak Korean | Recording captures audio |
| 6 | Stop recording | Transitions to processing |
| 7 | Wait for STT | Transcript appears in Result scene |
| 8 | Check feedback | AI feedback displayed with reply |
| 9 | If needsRepeat | Next button is blocked, "Try Again" shown |
| 10 | Tap "Retry" | Returns to TutorRoom, same sentence |
| 11 | Tap "Next" | New random sentence loads |

### Error Cases
| Scenario | Expected Behavior |
|----------|-------------------|
| No microphone permission | Error message, prompt to settings |
| Backend unreachable | Error toast, retry button |
| STT fails | Error message, skip feedback option |
| TTS fails | Error message, retry option |

### Performance
- [ ] TTS audio loads within 3 seconds
- [ ] STT response within 5 seconds
- [ ] Feedback response within 3 seconds
- [ ] No audio glitches during playback
- [ ] Recording waveform is responsive

---

## Troubleshooting

### TTS plays "beeps" instead of speech
**This is the #1 issue!** It means the backend is in MOCK mode.
- **Cause**: `ELEVENLABS_API_KEY` not set in `backend/.env`
- **Fix**:
  1. Get an API key from https://elevenlabs.io/
  2. Add to `backend/.env`: `ELEVENLABS_API_KEY=your_key`
  3. Restart the backend
- **Verify**: `curl http://localhost:3000/api/health` should show `"tts": "live"`

### STT returns wrong/random Korean phrases
**The microphone IS recording correctly**, but the backend returns mock data.
- **Cause**: `OPENAI_API_KEY` not set in `backend/.env`
- **Fix**:
  1. Get an API key from https://platform.openai.com/api-keys
  2. Add to `backend/.env`: `OPENAI_API_KEY=your_key`
  3. Restart the backend
- **Verify**: `curl http://localhost:3000/api/health` should show `"stt": "live"`

### Android: "INSTALL_FAILED_NO_MATCHING_ABIS"
- Enable "ARMv7" and "ARM64" in Player Settings > Other Settings > Target Architectures

### iOS: "Code Signing Error"
- Ensure Apple Developer Team is set
- Try: Xcode > Product > Clean Build Folder

### "Backend unreachable" on device
- Check device is on same network as backend
- Verify LAN IP is correct in AppConfig
- Check firewall isn't blocking port 3000

### No audio from TTS
- Check device volume
- Verify AudioSource component exists
- Check Unity audio settings (mute audio = false)

### Microphone permission denied
- iOS: Settings > Privacy > Microphone > Enable for app
- Android: Settings > Apps > Shadowing Tutor > Permissions > Microphone
