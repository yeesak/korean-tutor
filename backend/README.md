# AI-demo Backend

Backend server for Shadowing 3x Tutor - Korean language learning app.

## Quick Start (No API Keys Needed)

For **immediate testing** without API keys, use MOCK mode:

```bash
cd backend

# Copy environment file
cp .env.example .env

# .env already has MODE=MOCK - just start!
npm install
npm start
```

The server starts with **stub responses** so the Unity app can connect and test immediately.

---

## 📱 Testing from Android/iOS (LAN)

When running the backend on your PC, mobile devices on the same Wi-Fi can connect via your LAN IP.

### Step 1: Find your PC's LAN IP

```bash
# macOS
ipconfig getifaddr en0

# Windows
ipconfig | findstr "IPv4"

# Linux
hostname -I | awk '{print $1}'
```

### Step 2: Start the server

```bash
# The server binds to 0.0.0.0 by default (all interfaces)
npm start
```

The startup log will show available LAN URLs:
```
📱 For Android/iOS testing, use one of these LAN URLs:
   http://192.168.1.50:3000  (en0)
```

### Step 3: Test from phone browser

Open this URL in your phone browser:
```
http://<YOUR_PC_IP>:3000/api/health
```

You should see:
```json
{"ok": true, "mode": "mock", ...}
```

### Step 4: Configure Unity app

In the Unity app Settings or AppConfig:
- Set **Environment** to `LAN`
- Set **LAN IP** to your PC's IP (e.g., `192.168.1.50`)

Or in `unity/Assets/Resources/AppConfig.asset`:
- Set `_defaultLanIP` to your PC's IP

### Troubleshooting LAN Connection

| Problem | Solution |
|---------|----------|
| Phone can't reach server | Ensure PC and phone are on same Wi-Fi network |
| Connection refused | Check PC firewall allows port 3000 |
| "localhost" not working | localhost = phone's own address! Use PC's LAN IP |

---

## Services

| Service | Provider | Required Env Vars |
|---------|----------|-------------------|
| TTS (Text-to-Speech) | ElevenLabs | `ELEVENLABS_API_KEY` |
| STT (Speech-to-Text) | ElevenLabs | `ELEVENLABS_API_KEY` |
| Pronunciation Feedback | xAI Realtime | `XAI_API_KEY` |
| Grammar Feedback | xAI Grok | `XAI_API_KEY` |

## Setup

```bash
# Install dependencies
npm install

# Copy and configure environment
cp .env.example .env
# Edit .env with your API keys

# Start server
npm start

# Or development mode with auto-reload
npm run dev
```

## Mode Selection

| Mode | API Keys | Behavior |
|------|----------|----------|
| `MODE=MOCK` | Not required | Returns stub data (for testing) |
| `MODE=REAL` | Required | Uses real APIs |

```bash
# Mock mode (no keys needed)
MODE=MOCK npm start

# Real mode with keys
MODE=REAL ELEVENLABS_API_KEY=xxx XAI_API_KEY=yyy npm start
```

## API Endpoints

### GET /api/health

Health check and configuration status.

```bash
curl -s http://localhost:3000/api/health | json_pp
```

Response:
```json
{
  "ok": true,
  "timestamp": "2024-01-18T12:00:00.000Z",
  "mode": "real",
  "ttsConfigured": true,
  "sttConfigured": true,
  "llmConfigured": true
}
```

---

### POST /api/eval (Recommended)

**Combined evaluation endpoint** - Single call that does everything:
- A) ElevenLabs STT -> transcriptText
- B) Punctuation-insensitive text scoring (CER-based)
- C) xAI Realtime pronunciation feedback (optional, graceful fallback)
- D) xAI Grok grammar corrections

```bash
# Test with audio file
curl -X POST http://localhost:3000/api/eval \
  -F "audio=@test.wav" \
  -F "targetText=커피 사 주세요" \
  -F "locale=ko-KR"
```

Response:
```json
{
  "ok": true,
  "targetText": "커피 사 주세요",
  "transcriptText": "커피 사 주세요",
  "rawTranscriptText": "커피 사 주세요",

  "textAccuracyPercent": 100,
  "mistakePercent": 0,
  "score": 100,

  "metrics": {
    "accuracyPercent": 100,
    "wrongPercent": 0,
    "textAccuracyPercent": 100,
    "mistakePercent": 0,
    "cer": 0
  },

  "diff": {
    "units": [
      {"unit": "커", "status": "correct"},
      {"unit": "피", "status": "correct"}
    ],
    "wrongUnits": [],
    "wrongParts": []
  },

  "pronunciation": {
    "available": true,
    "weakPronunciation": [
      {
        "token": "주세요",
        "reason": "발음이 약간 빠릅니다",
        "tip": "천천히 '주-세-요'로 발음해 보세요"
      }
    ],
    "strongPronunciation": [
      {
        "token": "커피",
        "reason": "발음이 정확합니다"
      }
    ],
    "shortComment": "좋아요! 전체적으로 잘 했어요."
  },

  "grammar": {
    "mistakes": [],
    "tutorComment": "완벽해요! 정확하게 말했습니다."
  }
}
```

If xAI is unavailable (no API key or error), pronunciation returns:
```json
{
  "pronunciation": {
    "available": false,
    "weakPronunciation": [],
    "strongPronunciation": [],
    "shortComment": ""
  }
}
```

---

### POST /api/pronounce_grok

Voice-based pronunciation feedback via xAI Realtime API (standalone endpoint).

```bash
curl -X POST http://localhost:3000/api/pronounce_grok \
  -F "audio=@test.wav" \
  -F "targetText=안녕하세요" \
  -F "transcriptText=안녕하세요" \
  -F "locale=ko-KR"
```

Response:
```json
{
  "ok": true,
  "tutor": {
    "weakPronunciation": [
      {"token": "하세요", "reason": "억양이 약간 부자연스럽습니다", "tip": "끝을 올려서 발음해 보세요"}
    ],
    "strongPronunciation": [
      {"token": "안녕", "reason": "발음이 정확합니다"}
    ],
    "shortComment": "좋아요! 계속 연습하세요."
  }
}
```

If xAI fails or is unavailable:
```json
{
  "ok": true,
  "tutor": null,
  "warning": "xAI Realtime connection failed"
}
```

---

### POST /api/feedback

Text-based feedback only (no audio analysis). Uses CER for scoring, **ignores punctuation**.

```bash
# Punctuation should NOT affect score (both return 100%)
curl -X POST http://localhost:3000/api/feedback \
  -H "Content-Type: application/json" \
  -d '{"targetText":"커피 사 주세요.","transcriptText":"커피 사 주세요"}'
```

Response:
```json
{
  "ok": true,
  "targetText": "커피 사 주세요.",
  "transcriptText": "커피 사 주세요",

  "textAccuracyPercent": 100,
  "mistakePercent": 0,
  "score": 100,

  "metrics": {
    "accuracyPercent": 100,
    "wrongPercent": 0,
    "cer": 0,
    "wer": 0
  },

  "diff": {
    "units": [...],
    "wrongUnits": [],
    "wrongParts": []
  },

  "grammar": {
    "corrections": [],
    "comment_ko": "완벽해요!"
  }
}
```

---

### POST /api/stt

Speech-to-Text only (ElevenLabs).

```bash
curl -X POST http://localhost:3000/api/stt \
  -F "audio=@test.wav"
```

Response:
```json
{
  "ok": true,
  "text": "안녕하세요",
  "transcriptText": "안녕하세요",
  "rawTranscriptText": "안녕하세요",
  "language": "ko",
  "confidence": 0.95
}
```

---

### POST /api/tts

Text-to-Speech (ElevenLabs). Returns audio/mpeg.

```bash
# Basic usage
curl -X POST http://localhost:3000/api/tts \
  -H "Content-Type: application/json" \
  -d '{"text":"안녕하세요"}' \
  --output hello.mp3

# With speed profile (0=slow, 1=normal, 2=fast)
curl -X POST http://localhost:3000/api/tts \
  -H "Content-Type: application/json" \
  -d '{"text":"안녕하세요","speedProfile":0}' \
  --output hello_slow.mp3
```

---

### GET /api/sentences

Get sentences list for shadowing practice.

```bash
# All sentences
curl -s http://localhost:3000/api/sentences | json_pp

# Filter by category
curl -s "http://localhost:3000/api/sentences?category=daily" | json_pp
```

---

## Audio Requirements

For best results:
- **Format**: WAV (PCM)
- **Sample Rate**: 16kHz (preferred), or will be resampled
- **Channels**: Mono (stereo will be converted)
- **Bit Depth**: 16-bit

---

## Testing

```bash
# Test health endpoint
curl -s http://localhost:3000/api/health | json_pp

# Test feedback (punctuation should not affect score)
curl -s -X POST http://localhost:3000/api/feedback \
  -H "Content-Type: application/json" \
  -d '{"targetText":"밥 먹었어요?","transcriptText":"밥 먹었어요"}'

# Test combined eval with audio
curl -X POST http://localhost:3000/api/eval \
  -F "audio=@test.wav" \
  -F "targetText=안녕하세요"

# Test pronunciation only
curl -X POST http://localhost:3000/api/pronounce_grok \
  -F "audio=@test.wav" \
  -F "targetText=안녕하세요"
```

---

## Graceful Degradation

The app is designed to work even when xAI services are unavailable:

| Service | Unavailable Behavior |
|---------|---------------------|
| xAI Realtime (pronunciation) | `pronunciation.available = false`, empty arrays |
| xAI Grok (grammar) | `grammar.mistakes = []`, empty tutor comment |
| ElevenLabs STT | Returns HTTP 503 (required service) |
| ElevenLabs TTS | Returns HTTP 503 (required service) |

---

## Run Tests

```bash
npm test
```

Includes tests for:
- Punctuation-insensitive scoring
- API response formats
- Cache behavior
- Error handling
