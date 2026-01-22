# Backend Deployment Guide

Deploy the Shadowing Tutor backend to make it accessible from Android devices.

## Quick Start (Render.com - Recommended)

1. **Push to GitHub**
   ```bash
   git add .
   git commit -m "Deploy backend"
   git push origin main
   ```

2. **Create Render Account**
   - Go to https://render.com
   - Sign up with GitHub

3. **Deploy**
   - Click "New" → "Web Service"
   - Connect your GitHub repo
   - Select the `backend` folder as root directory
   - Render will auto-detect `render.yaml`

4. **Set Environment Variables**
   In Render dashboard → Environment:
   ```
   ELEVENLABS_API_KEY=your_key_here
   XAI_API_KEY=your_key_here
   NODE_ENV=production
   ```

5. **Get Your URL**
   After deploy: `https://your-app-name.onrender.com`

6. **Test**
   ```bash
   curl https://your-app-name.onrender.com/health
   # Should return: {"ok":true,"ts":"..."}
   ```

## Alternative: Railway.app

1. Install Railway CLI: `npm i -g @railway/cli`
2. Login: `railway login`
3. Deploy:
   ```bash
   cd backend
   railway init
   railway up
   ```
4. Set env vars: `railway variables set ELEVENLABS_API_KEY=xxx`

## Alternative: Fly.io

1. Install flyctl: https://fly.io/docs/hands-on/install-flyctl/
2. Deploy:
   ```bash
   cd backend
   fly launch
   fly secrets set ELEVENLABS_API_KEY=xxx XAI_API_KEY=xxx
   fly deploy
   ```

## Environment Variables Required

| Variable | Required | Description |
|----------|----------|-------------|
| `ELEVENLABS_API_KEY` | Yes | ElevenLabs API key for TTS/STT |
| `XAI_API_KEY` | Recommended | xAI Grok API key for feedback |
| `NODE_ENV` | Yes | Set to `production` |
| `PORT` | Auto | Set by hosting platform |

## Verify Deployment

```bash
# Health check
curl https://YOUR-URL/health

# Test TTS (should return audio)
curl -X POST https://YOUR-URL/api/tts \
  -H "Content-Type: application/json" \
  -d '{"text":"안녕하세요"}'

# Test Grok
curl -X POST https://YOUR-URL/api/grok \
  -H "Content-Type: application/json" \
  -d '{"messages":[{"role":"user","content":"say hello in Korean"}]}'
```

## Update Unity App

After deploying, update Unity:

1. Open Unity project
2. Find `Assets/Resources/AppConfig.asset`
3. Set Production URL to your deployed URL
4. Set Default Build Environment to "Production"
5. Build APK

---

## Troubleshooting

**502 Bad Gateway**
- Check Render logs for startup errors
- Verify all env vars are set

**503 Service Unavailable**
- Missing API keys - check ELEVENLABS_API_KEY

**Request Timeout**
- Free tier may sleep after inactivity
- First request takes 30-60s to wake up
- Upgrade to paid tier for always-on

**CORS Errors**
- Backend already configured for wide-open CORS
- Check if URL is correct in Unity
