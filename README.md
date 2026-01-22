# Shadowing 3x Tutor

Korean language learning app with AI-powered speech practice.

## Architecture

```
┌─────────────────┐      HTTPS      ┌─────────────────┐
│   Unity App     │ ◄──────────────► │  Node.js        │
│   (Android)     │                  │  Backend        │
└─────────────────┘                  └────────┬────────┘
                                              │
                              ┌───────────────┼───────────────┐
                              ▼               ▼               ▼
                        ┌──────────┐   ┌──────────┐   ┌──────────┐
                        │ElevenLabs│   │ElevenLabs│   │ xAI Grok │
                        │   TTS    │   │   STT    │   │ Feedback │
                        └──────────┘   └──────────┘   └──────────┘
```

## Quick Start

### 1. Backend Setup

```bash
cd backend
cp .env.example .env
# Edit .env with your API keys:
# - ELEVENLABS_API_KEY
# - XAI_API_KEY

npm install
npm run dev
```

Test: `curl http://localhost:3000/health`

### 2. Unity Setup (Editor)

1. Open Unity project in `unity/` folder
2. Backend auto-starts when entering Play mode
3. Press Play to test

### 3. Deploy for Android

1. **Deploy Backend** → See `backend/DEPLOY.md`
2. **Configure Unity** → Set production URL in AppConfig
3. **Build APK** → See `unity/ANDROID_BUILD.md`

## Project Structure

```
AI-demo/
├── backend/           # Node.js API proxy
│   ├── index.js       # Express server
│   ├── src/           # API handlers
│   ├── DEPLOY.md      # Deployment guide
│   └── render.yaml    # Render.com config
│
└── unity/             # Unity 6 project
    ├── Assets/
    │   ├── Scripts/   # C# game logic
    │   └── Resources/ # AppConfig.asset
    └── ANDROID_BUILD.md
```

## API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/health` | GET | Health check |
| `/api/tts` | POST | Text-to-Speech |
| `/api/stt` | POST | Speech-to-Text |
| `/api/grok` | POST | Tutor line generation |
| `/api/eval` | POST | Combined evaluation |
| `/api/feedback` | POST | Pronunciation feedback |

## Environment Variables

| Variable | Required | Service |
|----------|----------|---------|
| `ELEVENLABS_API_KEY` | Yes | TTS/STT |
| `XAI_API_KEY` | Recommended | Grok feedback |
| `NODE_ENV` | Yes (prod) | Set to `production` |

## Test Checklist

- [ ] Backend `/health` returns OK
- [ ] Unity connects to backend
- [ ] TTS audio plays
- [ ] Microphone recording works
- [ ] STT transcription works
- [ ] Feedback generation works
- [ ] Android APK installs
- [ ] Full lesson flow completes

---

## Android Friend Install Guide

### A) Local Backend Test

```bash
cd backend
npm install
cp .env.example .env
# Edit .env: add ELEVENLABS_API_KEY and XAI_API_KEY

npm run dev

# In another terminal:
curl http://localhost:3000/health
# Expected: {"ok":true,"ts":"2025-01-22T..."}
```

### B) Deploy Backend (Render.com)

1. Push repo to GitHub
2. Go to https://render.com → New → Web Service
3. Connect your GitHub repo, select `backend` folder
4. Set environment variables:
   - `ELEVENLABS_API_KEY`: your key
   - `XAI_API_KEY`: your key
   - `NODE_ENV`: production
5. Deploy and get URL: `https://YOUR-APP.onrender.com`
6. Test: `curl https://YOUR-APP.onrender.com/health`

### C) Configure Unity

1. Open Unity project
2. Go to `Assets/Resources/AppConfig.asset`
3. Set **Production URL** to your deployed HTTPS URL
4. Set **Default Build Environment** to `Production`
5. Enter Play mode to verify logs show correct baseUrl

### D) Build Android APK

1. File → Build Settings → Android → Switch Platform
2. Player Settings:
   - Scripting Backend: IL2CPP
   - Target Architectures: ARM64
3. Click Build → Save as `ShadowingTutor.apk`
4. Share APK file with friend

### E) Friend Install Steps

1. Download APK file
2. Settings → Security → Enable "Unknown sources" (or "Install unknown apps")
3. Open APK → Install
4. Grant microphone permission when prompted
5. Start app and begin lesson

### Troubleshooting

| Problem | Solution |
|---------|----------|
| "설정 오류" on app start | AppConfig still points to localhost. Set Production URL |
| "서버 연결 실패" | Backend not running or wrong URL. Check /health |
| TTS/STT very slow | First request wakes up free-tier server (30-60s). Wait and retry |
| No audio | Check phone volume and app audio permissions |
| Microphone not working | Grant microphone permission in Android Settings |

## License

MIT
