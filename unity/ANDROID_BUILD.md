# Android Friend-Install Build Guide

Build and share the app with friends for testing (no Play Store needed).

## Prerequisites

1. **Deploy Backend First**
   - Follow `backend/DEPLOY.md`
   - Get your production URL (e.g., `https://your-app.onrender.com`)
   - Verify `/health` returns `{"ok":true}`

2. **Configure Unity**
   - Open Unity project
   - Navigate to `Assets/Resources/AppConfig.asset`
   - Set **Production URL** to your deployed backend URL
   - Set **Default Build Environment** to `Production`

## Build APK

### Option 1: Unity Editor

1. **Open Build Settings**
   - File → Build Settings (Cmd+Shift+B)
   - Select "Android" platform
   - Click "Switch Platform" if not already on Android

2. **Configure Player Settings**
   - Click "Player Settings..."
   - Company Name: Your choice
   - Product Name: "Shadowing Tutor"
   - Package Name: `com.shadowingtutor.app`
   - Minimum API Level: 24 (Android 7.0)
   - Target API Level: 34 (Android 14)

3. **Build**
   - Click "Build"
   - Choose output folder
   - Wait for build (5-10 minutes)
   - APK will be at: `YourFolder/ShadowingTutor.apk`

### Option 2: Command Line

```bash
# From project root
/Applications/Unity/Hub/Editor/6000.*/Unity.app/Contents/MacOS/Unity \
  -batchmode -quit -projectPath . \
  -executeMethod BuildScript.BuildAndroid \
  -logFile build.log
```

## Install on Friend's Phone

### Enable Unknown Sources

**Android 8.0+:**
1. Settings → Apps → Special access → Install unknown apps
2. Enable for your file manager or browser

**Older Android:**
1. Settings → Security → Unknown sources → Enable

### Transfer APK

**Option 1: Direct Transfer**
- USB cable → copy APK to phone
- Open file manager → tap APK → Install

**Option 2: Share Link**
- Upload APK to Google Drive / Dropbox
- Share download link with friend
- Download on phone → Install

**Option 3: ADB (Developer)**
```bash
adb install -r ShadowingTutor.apk
```

## First Launch Checklist

1. **Grant Permissions**
   - Microphone: Required for speech recording
   - App will prompt on first use

2. **Test Backend Connection**
   - App should connect automatically
   - If "서버 연결 실패" appears:
     - Check internet connection
     - Verify backend is deployed and running
     - Check URL in Settings panel

3. **Start Lesson**
   - Tap START button
   - Listen to tutor introduction
   - Follow prompts to practice

## Test Checklist

- [ ] App installs without errors
- [ ] App starts and shows home screen
- [ ] Backend health check passes (no error message)
- [ ] START button works
- [ ] TTS audio plays (tutor speaks)
- [ ] Microphone recording works
- [ ] STT transcription appears
- [ ] Feedback is generated

## Troubleshooting

**"설정 오류" / Config Error**
- Backend URL not configured
- Open Settings, check Production URL

**"서버 연결 실패" / Server Connection Failed**
- Backend not deployed or sleeping (free tier)
- Wait 30s and retry
- Check internet connection

**No Audio**
- Check phone volume
- Check app audio permissions

**Microphone Not Working**
- Grant microphone permission in Settings → Apps → Shadowing Tutor → Permissions

**Crash on Start**
- Check minimum Android version (7.0 required)
- Try reinstalling

## Sharing with Friends

Send them:
1. The APK file
2. This install guide
3. Note that first request may be slow (free tier cold start)

---

## Version Info

- Unity: 6000.x (Unity 6)
- Target Android: 7.0+ (API 24+)
- Backend: Node.js 18+
