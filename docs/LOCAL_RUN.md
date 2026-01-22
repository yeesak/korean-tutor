# AI-demo Local Development Guide

This document provides exact commands to run and test the Shadowing 3x Tutor locally.

---

## 1. Prerequisites

Ensure you have completed all installations from `SETUP_DOWNLOADS.md`:
- Node.js 18+ installed
- npm installed
- Unity Hub + Unity 2022.3 LTS installed

---

## 2. Start the Backend Server

### 2.1 Navigate to Backend Directory

```bash
cd /path/to/AI-demo/backend
```

### 2.2 Install Dependencies

```bash
npm install
```

### 2.3 Create Environment File (Optional)

For production mode with real APIs:
```bash
cp .env.example .env
# Edit .env and add your API keys
```

**MOCK Mode (Default):** If you skip this step or leave keys empty, the server runs in MOCK mode automatically. No API costs will be incurred.

### 2.4 Start the Server

**Development mode (with auto-reload):**
```bash
npm run dev
```

**Production mode:**
```bash
npm start
```

Expected output:
```
🎯 AI-demo Backend Server
   Running on: http://localhost:3000
   Environment: DEVELOPMENT
   Mock Mode: ENABLED (no API costs)
   Rate Limit: 60 req/min per IP

   Endpoints:
   - GET  /api/health
   - POST /api/tts
   - POST /api/stt
   - POST /api/feedback
   - GET  /api/sentences
   - POST /api/cache/clear (dev only)
   - GET  /api/cache/stats (dev only)
```

---

## 3. Test Backend Endpoints with curl

### 3.1 Health Check

```bash
curl http://localhost:3000/api/health
```

Expected response:
```json
{
  "ok": true,
  "timestamp": "2024-01-01T00:00:00.000Z",
  "elevenlabsConfigured": false,
  "openaiConfigured": false,
  "xaiConfigured": false,
  "mockMode": true,
  "services": {
    "tts": "mock",
    "stt": "mock",
    "feedback": "mock"
  }
}
```

**Health Check Fields:**

| Field | Type | Description |
|-------|------|-------------|
| ok | boolean | Always true if server is running |
| elevenlabsConfigured | boolean | True if ELEVENLABS_API_KEY is set |
| openaiConfigured | boolean | True if OPENAI_API_KEY is set |
| xaiConfigured | boolean | True if XAI_API_KEY is set |
| mockMode | boolean | True if any service is in mock mode |
| services | object | Status of each service (mock/live) |

### 3.2 TTS (Text-to-Speech)

**Basic request:**
```bash
curl -X POST http://localhost:3000/api/tts \
  -H "Content-Type: application/json" \
  -d '{"text": "안녕하세요"}' \
  --output test_audio.wav
```

**With speed profile (1=slow, 2=normal, 3=fast):**
```bash
# Slow speed (0.9x)
curl -X POST http://localhost:3000/api/tts \
  -H "Content-Type: application/json" \
  -d '{"text": "천천히 말해 주세요", "speedProfile": 1}' \
  --output slow.wav

# Normal speed (1.0x) - default
curl -X POST http://localhost:3000/api/tts \
  -H "Content-Type: application/json" \
  -d '{"text": "보통 속도예요", "speedProfile": 2}' \
  --output normal.wav

# Fast speed (1.1x)
curl -X POST http://localhost:3000/api/tts \
  -H "Content-Type: application/json" \
  -d '{"text": "빠르게 말해요", "speedProfile": 3}' \
  --output fast.wav
```

**Check response headers (caching info):**
```bash
curl -X POST http://localhost:3000/api/tts \
  -H "Content-Type: application/json" \
  -d '{"text": "안녕하세요"}' \
  -D - --output test1.wav

# X-Cache: MISS (first request)
# X-Cache-Key: abc123def456 (truncated sha256)
```

**Second request returns cached audio:**
```bash
curl -X POST http://localhost:3000/api/tts \
  -H "Content-Type: application/json" \
  -d '{"text": "안녕하세요"}' \
  -D - --output test2.wav

# X-Cache: HIT (cached response)
```

**Check file was created:**
```bash
ls -la test_audio.wav
# Should show non-zero file size

# Check it's a valid WAV (mock mode)
file test_audio.wav
# Should show: RIFF (little-endian) data, WAVE audio
```

**Error responses (JSON with ok:false):**
```bash
# Missing text
curl -X POST http://localhost:3000/api/tts \
  -H "Content-Type: application/json" \
  -d '{}'

# Response:
# {"ok":false,"error":"Missing or invalid \"text\" field","details":"Text must be a non-empty string"}

# Text too long (>5000 chars)
curl -X POST http://localhost:3000/api/tts \
  -H "Content-Type: application/json" \
  -d '{"text": "'"$(printf 'a%.0s' {1..6000})"'"}'

# Response:
# {"ok":false,"error":"Text too long","details":"Maximum text length is 5000 characters"}
```

**TTS API Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| text | string | required | Text to synthesize (max 5000 chars) |
| speedProfile | 1\|2\|3 | 2 | Speed: 1=slow (0.9x), 2=normal (1.0x), 3=fast (1.1x) |

**Query Parameters (optional):**
| Parameter | Default | Description |
|-----------|---------|-------------|
| output_format | mp3_44100_128 | Audio format (ElevenLabs format) |
| optimize_streaming_latency | 1 | Latency optimization level |

**Voice Settings (server-side, not configurable via API):**
- stability: 0.5
- similarity_boost: 0.75
- style: 0.0
- use_speaker_boost: true
- speed: mapped from speedProfile

**Caching:**
- Cache key = sha256(text + voice_id + voice_settings + output_format)
- Cached files stored in: `backend/cache/tts/<hash>.mp3`
- Cache is persistent across server restarts

### 3.3 STT (Speech-to-Text)

**Create a test audio file (using TTS):**
```bash
# First, generate a test audio file
curl -X POST http://localhost:3000/api/tts \
  -H "Content-Type: application/json" \
  -d '{"text": "안녕하세요"}' \
  --output test_audio.wav
```

**Basic STT request:**
```bash
curl -X POST http://localhost:3000/api/stt \
  -F "audio=@test_audio.wav"
```

**With language parameter (default: ko):**
```bash
# Korean (default)
curl -X POST http://localhost:3000/api/stt \
  -F "audio=@test_audio.wav" \
  -F "language=ko"

# English
curl -X POST http://localhost:3000/api/stt \
  -F "audio=@english_audio.wav" \
  -F "language=en"
```

**Expected response (mock mode):**
```json
{
  "ok": true,
  "text": "안녕하세요",
  "words": [
    { "word": "안녕하세요", "start": 0.0, "end": 0.35 }
  ],
  "language": "ko",
  "duration": 0.2,
  "mock": true
}
```

**Check response headers (caching info):**
```bash
curl -X POST http://localhost:3000/api/stt \
  -F "audio=@test_audio.wav" \
  -D -

# X-Cache: MISS (first request)
# X-Cache-Key: abc123def456 (truncated sha256 of audio bytes)
```

**Second request returns cached result:**
```bash
curl -X POST http://localhost:3000/api/stt \
  -F "audio=@test_audio.wav" \
  -D -

# X-Cache: HIT (cached response)
```

**Supported audio formats:**
- WAV (.wav)
- MP3 (.mp3)
- M4A (.m4a)
- WebM (.webm)
- Maximum file size: 25MB

**Error responses:**
```bash
# No audio file
curl -X POST http://localhost:3000/api/stt

# Response:
# {"ok":false,"error":"No audio file uploaded","details":"Use multipart/form-data with field name \"audio\""}
```

**STT API Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| audio | file | required | Audio file (wav/mp3/m4a/webm, max 25MB) |
| language | string | ko | Language code (e.g., "ko", "en", "ja") |

**Response Fields:**

| Field | Type | Description |
|-------|------|-------------|
| ok | boolean | Success status |
| text | string | Full transcript |
| words | array | Word-level timestamps [{word, start, end}] |
| language | string | Detected/specified language |
| duration | number | Audio duration in seconds |
| mock | boolean | Present if mock mode |
| cached | boolean | Present if from cache |
| raw | object | Full API response (live mode only) |

**Caching:**
- Cache key = sha256(audio_bytes)
- Cached files stored in: `backend/cache/stt/<hash>.json`
- Same audio file always returns same cached result

### 3.4 Feedback

**Basic request (exact match):**
```bash
curl -X POST http://localhost:3000/api/feedback \
  -H "Content-Type: application/json" \
  -d '{"original": "안녕하세요", "spoken": "안녕하세요"}'
```

Expected response (mock mode - exact match):
```json
{
  "ok": true,
  "feedback": "완벽해요! 발음이 아주 정확합니다.",
  "mock": true
}
```

**Partial match (pronunciation issue):**
```bash
curl -X POST http://localhost:3000/api/feedback \
  -H "Content-Type: application/json" \
  -d '{"original": "좋은 아침이에요", "spoken": "좋은 아침"}'
```

Expected response (improvement suggestion):
```json
{
  "ok": true,
  "feedback": "'아침이에요' 부분을 조금 더 또렷하게 발음해 보세요.",
  "mock": true
}
```

**Different utterance:**
```bash
curl -X POST http://localhost:3000/api/feedback \
  -H "Content-Type: application/json" \
  -d '{"original": "감사합니다", "spoken": "고맙습니다"}'
```

Expected response (alternative suggestion):
```json
{
  "ok": true,
  "feedback": "'고맙습니다'라고 하셨는데, '감사합니다'가 더 자연스러운 표현이에요.",
  "mock": true
}
```

**With mode parameter:**
```bash
# Shadowing mode (default)
curl -X POST http://localhost:3000/api/feedback \
  -H "Content-Type: application/json" \
  -d '{"original": "안녕하세요", "spoken": "안녕하세요", "mode": "shadowing"}'

# Pronunciation mode
curl -X POST http://localhost:3000/api/feedback \
  -H "Content-Type: application/json" \
  -d '{"original": "안녕하세요", "spoken": "안녕하세요", "mode": "pronunciation"}'
```

**Error responses:**
```bash
# Missing original
curl -X POST http://localhost:3000/api/feedback \
  -H "Content-Type: application/json" \
  -d '{"spoken": "test"}'

# Response:
# {"ok":false,"error":"Missing or invalid \"original\" field","details":"Original sentence must be a non-empty string"}

# Missing spoken
curl -X POST http://localhost:3000/api/feedback \
  -H "Content-Type: application/json" \
  -d '{"original": "test"}'

# Response:
# {"ok":false,"error":"Missing or invalid \"spoken\" field","details":"Spoken transcript must be a non-empty string"}
```

**Feedback API Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| original | string | required | The Korean sentence the user should say |
| spoken | string | required | The Whisper transcript of what the user said |
| mode | string | "shadowing" | Feedback mode: "shadowing", "pronunciation", "conversation" |

**Response Fields:**

| Field | Type | Description |
|-------|------|-------------|
| ok | boolean | Success status |
| feedback | string | Korean feedback sentence (ONE sentence only) |
| mock | boolean | Present if mock mode |
| model | string | xAI model used (live mode only) |

**Feedback Behavior:**
- Mock mode: Deterministic feedback based on hash(original + spoken)
  - Exact match: Praise ("완벽해요!", "아주 잘했어요" etc.)
  - Partial match: Re-pronunciation suggestion (quotes the problematic word)
  - Different utterance: Alternative suggestion
- Live mode (with XAI_API_KEY): Uses Grok for natural, varied feedback
  - Korean only, ONE sentence
  - Praises briefly for close matches
  - Suggests alternatives for unnatural expressions
  - Asks to re-pronounce specific words for pronunciation issues

### 3.5 Sentences

Get all sentences:
```bash
curl http://localhost:3000/api/sentences
```

Get sentences by category:
```bash
curl "http://localhost:3000/api/sentences?category=daily"
curl "http://localhost:3000/api/sentences?category=travel"
curl "http://localhost:3000/api/sentences?category=cafe"
curl "http://localhost:3000/api/sentences?category=school"
curl "http://localhost:3000/api/sentences?category=work"
```

---

## 4. Run Backend Tests

```bash
cd /path/to/AI-demo/backend
npm test
```

Expected output:
```
✔ Health Endpoint > GET /api/health returns 200 with status ok
✔ TTS Endpoint > POST /api/tts returns audio for valid text
✔ TTS Endpoint > POST /api/tts returns 400 for missing text
✔ TTS Endpoint > POST /api/tts returns 400 for text too long
✔ STT Endpoint > POST /api/stt returns 400 for no audio file
✔ Feedback Endpoint > POST /api/feedback returns feedback for valid input
✔ Feedback Endpoint > POST /api/feedback returns 400 for missing original
✔ Feedback Endpoint > POST /api/feedback returns 400 for missing transcript
✔ Sentences Endpoint > GET /api/sentences returns sentences array
✔ Sentences Endpoint > GET /api/sentences?category=daily filters by category
✔ Sentences Endpoint > GET /api/sentences?category=invalid returns 400
✔ 404 Handler > Unknown route returns 404
✔ Mock Mode > Health endpoint shows mock status
```

---

## 5. Run Unity in Editor

### 5.1 Open Project in Unity

1. Open Unity Hub
2. Click "Add" or "Open"
3. Navigate to `/path/to/AI-demo/unity`
4. Select the folder and open

### 5.2 Import Required Packages

On first open, Unity may prompt for:
- **TextMeshPro Essentials** - Click "Import TMP Essentials"

### 5.3 Create a Test Scene

1. Create new scene: File > New Scene
2. Add Empty GameObject, name it "GameManager"
3. Add script: `AIDemo.GameManager`
4. Add Empty GameObject, name it "ApiClient"
5. Add script: `AIDemo.ApiClient`
6. Add Empty GameObject, name it "MicrophoneRecorder"
7. Add script: `AIDemo.MicrophoneRecorder`
8. Add Empty GameObject, name it "ShadowingManager"
9. Add script: `AIDemo.ShadowingManager`
10. Add UI Canvas with buttons/text for testing

### 5.4 Configure Backend URL

On the ApiClient component:
- Set `Base Url` to `http://localhost:3000`

### 5.5 Play and Test

1. Ensure backend is running (`npm run dev`)
2. Press Play in Unity Editor
3. Check Console for health check result
4. Use the UIManager to test shadowing flow

---

## 6. Test on Physical Device (LAN)

### 6.1 Find Your Local IP

**macOS/Linux:**
```bash
ifconfig | grep "inet " | grep -v 127.0.0.1
# Example output: inet 192.168.1.100
```

**Windows:**
```bash
ipconfig
# Look for IPv4 Address under your network adapter
```

### 6.2 Start Backend on All Interfaces

Modify the start command or ensure your firewall allows port 3000.

The backend already listens on all interfaces by default.

### 6.3 Configure Unity for Device Testing

In the Unity ApiClient component or UIManager input field:
- Change `http://localhost:3000` to `http://192.168.1.100:3000`
  (Replace with your actual LAN IP)

### 6.4 Build and Run on Device

**Android:**
1. Connect device via USB with USB debugging enabled
2. File > Build Settings > Android
3. Switch Platform if needed
4. Click "Build and Run"

**iOS (macOS only):**
1. File > Build Settings > iOS
2. Switch Platform if needed
3. Click "Build"
4. Open generated Xcode project
5. Configure signing team
6. Build and run on device

### 6.5 Verify Connection

On the device, the app should show:
- "Backend healthy" message on successful connection
- "Mock mode: true" if no API keys configured

---

## 7. Testing Workflow Summary

### Quick Start Commands

```bash
# Terminal 1: Start backend
cd AI-demo/backend
npm install
npm run dev

# Terminal 2: Test endpoints
curl http://localhost:3000/api/health
curl http://localhost:3000/api/sentences | head -20

# Terminal 3 (optional): Run tests
cd AI-demo/backend
npm test
```

### Unity Testing Checklist

- [ ] Backend running at localhost:3000
- [ ] Health check returns status: ok
- [ ] TTS returns audio file
- [ ] STT accepts audio upload
- [ ] Feedback returns Korean feedback
- [ ] Sentences loads 310 items
- [ ] Unity Editor connects successfully
- [ ] Microphone permission works (device only)
- [ ] Audio plays correctly

---

## 8. Troubleshooting

### Backend won't start
```bash
# Check if port 3000 is in use
lsof -i :3000

# Kill existing process
kill -9 <PID>
```

### Unity can't connect to backend
- Ensure backend is running
- Check firewall settings
- Try disabling VPN
- Verify URL doesn't have trailing slash

### Microphone not working on device
- Verify manifest permissions (Android)
- Check Info.plist entry (iOS)
- Grant permission when prompted
- Restart app after granting permission

### TTS audio doesn't play in Unity
- Check AudioSource component exists
- Verify audio format is supported
- Check Unity console for errors

---

## 9. Mock Mode Details

The backend has three mock states (one per service):

| Service | Mock Trigger | Mock Behavior |
|---------|--------------|---------------|
| TTS | `ELEVENLABS_API_KEY` missing | Returns minimal MP3 (silent) |
| STT | `OPENAI_API_KEY` missing | Returns random Korean phrase |
| Feedback | `XAI_API_KEY` missing | Returns encouraging feedback |

Mock mode is **deterministic**: the same input always produces the same output.

To exit mock mode for a service:
1. Get API key from respective provider
2. Add to `.env` file
3. Restart backend

---

## 10. Cost Control Features

The backend includes built-in protections against excessive API costs:

### Rate Limiting

- **60 requests per minute per IP** (in-memory)
- Returns `429 Too Many Requests` when exceeded
- Rate limit headers included in all responses:
  - `X-RateLimit-Limit`: Maximum requests per window
  - `X-RateLimit-Remaining`: Remaining requests in window
  - `X-RateLimit-Reset`: Seconds until window resets

**Test rate limiting:**
```bash
# Make 61 rapid requests to trigger limit
for i in {1..61}; do curl -s http://localhost:3000/api/health > /dev/null && echo "Request $i"; done
```

### Retry with Backoff

All upstream API calls (ElevenLabs, OpenAI, xAI) include:
- **3 retries** with exponential backoff
- Initial delay: 1 second, max: 10 seconds
- Automatic retry on: 429, 500, 502, 503, 504 status codes
- Automatic retry on network errors (timeout, connection reset)

### Aggressive Caching

- **TTS Cache**: SHA256 hash of text + voice settings
  - Same text always returns cached audio (zero API cost on repeat)
  - Cache stored on disk: `backend/cache/tts/`
- **STT Cache**: SHA256 hash of audio bytes
  - Same audio always returns cached transcript
  - Cache stored on disk: `backend/cache/stt/`

**View cache stats (dev only):**
```bash
curl http://localhost:3000/api/cache/stats
```

**Clear cache (dev only):**
```bash
curl -X POST http://localhost:3000/api/cache/clear
```

### Minimal Logging

- Production mode (`NODE_ENV=production`): No user audio/text logged
- Development mode: Detailed logs for debugging

---

## 11. Production Deployment Notes

For production:
1. Set `NODE_ENV=production`
2. Use a process manager like PM2
3. Put behind a reverse proxy (nginx)
4. Use HTTPS
5. Set proper CORS origins
6. Add rate limiting
7. Use a proper database for caching (Redis)

Example PM2 start:
```bash
npm install -g pm2
pm2 start index.js --name "ai-demo-backend"
pm2 save
pm2 startup
```
