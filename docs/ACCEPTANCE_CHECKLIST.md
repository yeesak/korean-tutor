# Acceptance Checklist - Shadowing 3x Tutor

Manual and automated verification criteria for release readiness.

## Backend Quality Gates (Automated)

Run: `cd backend && npm test`

| ID | Criterion | Test |
|----|-----------|------|
| QG1 | Health endpoint returns ok:true | `Quality Gates > QG1` |
| QG2 | TTS mock returns valid WAV (RIFF header) | `Quality Gates > QG2` |
| QG3 | STT mock returns deterministic transcript | `Quality Gates > QG3` |
| QG4 | Feedback mock returns 1-line Korean | `Quality Gates > QG4` |
| QG5 | Full workflow works without API keys | `Quality Gates > QG5` |

## Backend Smoke Test

Run: `cd backend && node tools/smoke.js`

- [ ] Health endpoint responds with ok:true
- [ ] Sentences endpoint returns 50+ sentences
- [ ] TTS returns audio bytes with RIFF header
- [ ] STT returns transcript with duration
- [ ] Feedback returns Korean string without newlines

## Unity Manual Testing

### TutorRoom Screen

- [ ] **TTS plays 3 times**: Press START, audio plays exactly 3 times
- [ ] **Dots update progressively**: Dots show 1/3 → 2/3 → 3/3 during playback
- [ ] **Record button enabled after 3 listens**: RECORD button activates only after TTS completes
- [ ] **Mic captures audio**: Press RECORD, speak, recording indicator shows
- [ ] **STT returns text**: After recording stops, transcript appears (or mock text in MOCK mode)

### Result Screen

- [ ] **Original sentence displayed**: Shows the Korean sentence that was played
- [ ] **Transcript displayed**: Shows what STT recognized from recording
- [ ] **Diff highlights wrong words**: Mismatched words appear in red
- [ ] **Feedback is 1 sentence Korean**: Single line of Korean coaching text
- [ ] **Feedback is context-aware**: Different feedback for good vs poor pronunciation
- [ ] **RETRY returns to TutorRoom**: Same sentence, listen count reset
- [ ] **NEXT loads new sentence**: Different sentence, random non-repeating

### Sentence Management

- [ ] **Random shuffle**: Sentences appear in random order
- [ ] **Non-repeating**: Same sentence doesn't appear twice until pool exhausted
- [ ] **Pool reset**: After all 50 sentences, pool reshuffles

### Error Handling & Retry UI

- [ ] **TTS Error**: "Retry TTS" button appears on failure
- [ ] **STT Error**: "Retry STT" button appears on failure
- [ ] **Feedback Error**: "Skip Feedback" button appears, allows proceeding without feedback
- [ ] Retry buttons hidden during normal operation
- [ ] Retry with stored audio works correctly

### Debug Panel (Editor Only)

Toggle: F12 or "Show Debug" button

- [ ] Shows current phase (Idle/Listening/Recording/Processing/Result)
- [ ] Shows listen count (0/3, 1/3, 2/3, 3/3)
- [ ] Shows sentence ID and Korean text
- [ ] Shows transcript length and content
- [ ] Shows feedback text

### Validation Overlay (Editor Only)

Toggle: Alt+V or "Show Valid" button

- [ ] Shows WER score (0.00-1.00)
- [ ] Score color: green (>=0.8), yellow (>=0.5), red (<0.5)
- [ ] Lists mismatched tokens in red
- [ ] Shows current feedback string

## Cost Control Verification

### Rate Limiting

- [ ] Rate limit header present: `X-RateLimit-Limit: 60`
- [ ] Remaining count decrements with each request
- [ ] Returns 429 after 60 requests in 1 minute

### Retry Logic

- [ ] TTS retries on 5xx errors (check logs)
- [ ] STT retries on 5xx errors (check logs)
- [ ] Feedback retries on 5xx errors (check logs)

### Caching

- [ ] TTS cache HIT header on repeat request
- [ ] STT cache HIT header on repeat request
- [ ] Cache stats endpoint works (dev): `GET /api/cache/stats`
- [ ] Cache clear endpoint works (dev): `POST /api/cache/clear`
- [ ] Cache endpoints return 403 in production

## MOCK Mode Verification

Run backend without API keys:

```bash
# Unset all API keys
unset ELEVENLABS_API_KEY
unset OPENAI_API_KEY
unset XAI_API_KEY

# Start backend
cd backend && npm start
```

- [ ] Health shows `mockMode: true`
- [ ] TTS returns synthetic WAV (440Hz tone)
- [ ] STT returns mock transcript based on audio duration
- [ ] Feedback returns context-aware Korean mock response

## Environment Switching

### AppConfig Settings

- [ ] **Local environment**: URL is `http://localhost:3000`
- [ ] **LAN environment**: URL template uses `{IP}` placeholder
- [ ] **Staging environment**: URL uses HTTPS
- [ ] **Production environment**: URL uses HTTPS

### Settings UI

- [ ] Settings panel opens with gear button or Escape key
- [ ] Environment dropdown shows all 4 options
- [ ] Selecting environment changes backend URL immediately
- [ ] LAN IP input field shows only when LAN selected
- [ ] Changing LAN IP updates URL immediately
- [ ] Connection test runs when environment changes
- [ ] Status indicator shows Connected (green) / Failed (red)
- [ ] Environment setting persists across app restarts (PlayerPrefs)

### Build Defaults

- [ ] Editor default: Local
- [ ] Build default: Production
- [ ] Release builds warn if using HTTP (non-secure)

## Platform-Specific

### Android

- [ ] Mic permission requested on first RECORD press
- [ ] Permission denial shows appropriate message
- [ ] Audio plays through device speaker
- [ ] Recording works after permission granted

### iOS

- [ ] Mic permission popup appears with usage description
- [ ] App functions after permission granted
- [ ] Audio plays through device speaker

## Performance

- [ ] TTS audio starts within 2 seconds of START press
- [ ] STT response returns within 3 seconds of recording stop
- [ ] Feedback returns within 2 seconds of STT completion
- [ ] No UI freezing during network calls

## Edge Cases

- [ ] Empty recording handled gracefully
- [ ] Network timeout shows error message
- [ ] Backend unavailable shows connection error
- [ ] Very long recording (>10s) handled

---

## Quick Verification Commands

```bash
# Backend tests (55 tests)
cd backend && npm test

# Smoke test
cd backend && node tools/smoke.js

# Start backend for Unity testing
cd backend && npm start

# Check mock mode
curl http://localhost:3000/api/health | jq '.mockMode'
```

## Sign-off

| Role | Name | Date | Pass |
|------|------|------|------|
| Developer | | | [ ] |
| QA | | | [ ] |
| Product | | | [ ] |
