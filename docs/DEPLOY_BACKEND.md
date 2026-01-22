# Backend Deployment Guide

Deploy the Shadowing 3x Tutor backend to production. Two options:
1. **PaaS** (Render/Fly.io) - Easiest, managed infrastructure
2. **VPS + Docker** - Full control, self-managed

---

## Prerequisites

Before deploying, you need:

| Item | Purpose | Where to get |
|------|---------|--------------|
| **ELEVENLABS_API_KEY** | Text-to-Speech | https://elevenlabs.io/api |
| **OPENAI_API_KEY** | Speech-to-Text (Whisper) | https://platform.openai.com/api-keys |
| **XAI_API_KEY** | Feedback (Grok) | https://x.ai/api |

**Without API keys:** The backend runs in MOCK mode (no costs, deterministic responses).

---

## Environment Variables Reference

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `PORT` | No | `3000` | Server port |
| `HOST` | No | `0.0.0.0` | Bind address |
| `NODE_ENV` | No | - | `production` or `development` |
| `ELEVENLABS_API_KEY` | No | - | ElevenLabs API key |
| `ELEVENLABS_VOICE_ID` | No | Rachel | Voice ID for TTS |
| `OPENAI_API_KEY` | No | - | OpenAI API key for Whisper |
| `XAI_API_KEY` | No | - | xAI API key for Grok |
| `XAI_MODEL` | No | `grok-3` | xAI model name |

---

## Option 1: Deploy to Render (Recommended PaaS)

Render offers free tier with HTTPS, easy setup.

### Step 1: Create Render Account

1. Go to https://render.com
2. Sign up with GitHub

### Step 2: Create New Web Service

1. Dashboard > **New** > **Web Service**
2. Connect your GitHub repository
3. Select the `AI-demo` repository

### Step 3: Configure Service

| Setting | Value |
|---------|-------|
| **Name** | `shadowing-tutor-api` |
| **Region** | Choose closest to users |
| **Branch** | `main` |
| **Root Directory** | `backend` |
| **Runtime** | `Node` |
| **Build Command** | `npm install` |
| **Start Command** | `npm start` |
| **Instance Type** | Free (or Starter for production) |

### Step 4: Set Environment Variables

In Render dashboard > Environment:

```
NODE_ENV=production
ELEVENLABS_API_KEY=your_key_here
OPENAI_API_KEY=your_key_here
XAI_API_KEY=your_key_here
```

### Step 5: Deploy

Click **Create Web Service**. Render will:
- Build your app
- Deploy with HTTPS automatically
- Provide URL like: `https://shadowing-tutor-api.onrender.com`

### Step 6: Verify Deployment

```bash
curl https://shadowing-tutor-api.onrender.com/api/health
```

Expected response:
```json
{
  "ok": true,
  "mockMode": false,
  "services": { "tts": "live", "stt": "live", "feedback": "live" }
}
```

### Render Health Check

Configure in Render dashboard:
- **Health Check Path**: `/api/health`
- **Health Check Timeout**: 10s

---

## Option 1b: Deploy to Fly.io (Alternative PaaS)

Fly.io offers global edge deployment, generous free tier.

### Step 1: Install Fly CLI

```bash
# macOS
brew install flyctl

# Linux
curl -L https://fly.io/install.sh | sh

# Windows
powershell -Command "iwr https://fly.io/install.ps1 -useb | iex"
```

### Step 2: Login and Initialize

```bash
cd backend
fly auth login
fly launch
```

When prompted:
- App name: `shadowing-tutor-api`
- Region: Choose closest
- PostgreSQL: No
- Redis: No
- Deploy now: No (we'll set secrets first)

### Step 3: Set Secrets

```bash
fly secrets set NODE_ENV=production
fly secrets set ELEVENLABS_API_KEY=your_key_here
fly secrets set OPENAI_API_KEY=your_key_here
fly secrets set XAI_API_KEY=your_key_here
```

### Step 4: Create fly.toml

Create `backend/fly.toml`:

```toml
app = "shadowing-tutor-api"
primary_region = "sjc"

[build]
  dockerfile = "Dockerfile"

[http_service]
  internal_port = 3000
  force_https = true
  auto_stop_machines = true
  auto_start_machines = true
  min_machines_running = 0

[[http_service.checks]]
  grace_period = "10s"
  interval = "30s"
  method = "GET"
  timeout = "5s"
  path = "/api/health"
```

### Step 5: Deploy

```bash
fly deploy
```

Your app will be at: `https://shadowing-tutor-api.fly.dev`

### Step 6: Verify

```bash
curl https://shadowing-tutor-api.fly.dev/api/health
```

---

## Option 2: Deploy to VPS with Docker

For full control on your own server (DigitalOcean, Linode, AWS EC2, etc.).

### Prerequisites

- VPS with Docker installed
- Domain name (for HTTPS)
- Basic Linux knowledge

### Step 1: Prepare Server

```bash
# SSH into your server
ssh user@your-server.com

# Install Docker (if not installed)
curl -fsSL https://get.docker.com | sh
sudo usermod -aG docker $USER

# Install Docker Compose
sudo curl -L "https://github.com/docker/compose/releases/latest/download/docker-compose-$(uname -s)-$(uname -m)" -o /usr/local/bin/docker-compose
sudo chmod +x /usr/local/bin/docker-compose

# Log out and back in for group changes
exit
ssh user@your-server.com
```

### Step 2: Clone Repository

```bash
git clone https://github.com/your-username/AI-demo.git
cd AI-demo/backend
```

### Step 3: Configure Environment

```bash
# Copy example env file
cp .env.example .env

# Edit with your API keys
nano .env
```

Add your keys:
```env
NODE_ENV=production
ELEVENLABS_API_KEY=your_key_here
OPENAI_API_KEY=your_key_here
XAI_API_KEY=your_key_here
```

### Step 4: Build and Run

```bash
# Build and start in detached mode
docker-compose up -d --build

# Check logs
docker-compose logs -f

# Verify it's running
curl http://localhost:3000/api/health
```

### Step 5: Set Up Reverse Proxy (Nginx + HTTPS)

For HTTPS (required for mobile apps), use Nginx with Let's Encrypt.

#### Install Nginx and Certbot

```bash
sudo apt update
sudo apt install nginx certbot python3-certbot-nginx
```

#### Configure Nginx

Create `/etc/nginx/sites-available/shadowing-api`:

```nginx
server {
    listen 80;
    server_name api.yourdomain.com;

    location / {
        proxy_pass http://localhost:3000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection 'upgrade';
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_cache_bypass $http_upgrade;

        # Timeout settings for long requests (TTS/STT)
        proxy_connect_timeout 60s;
        proxy_send_timeout 60s;
        proxy_read_timeout 60s;
    }
}
```

#### Enable Site and Get SSL

```bash
# Enable site
sudo ln -s /etc/nginx/sites-available/shadowing-api /etc/nginx/sites-enabled/

# Test config
sudo nginx -t

# Reload Nginx
sudo systemctl reload nginx

# Get SSL certificate
sudo certbot --nginx -d api.yourdomain.com
```

Certbot will automatically:
- Obtain certificate from Let's Encrypt
- Configure Nginx for HTTPS
- Set up auto-renewal

### Step 6: Verify HTTPS

```bash
curl https://api.yourdomain.com/api/health
```

---

## CORS Configuration

The backend allows all origins by default (`cors()`). For production, restrict to your domain:

### Update backend/index.js

```javascript
// Current (allows all)
app.use(cors());

// Production (restrict to specific origins)
app.use(cors({
  origin: [
    'https://yourdomain.com',
    'https://www.yourdomain.com',
    // Mobile apps don't need CORS (native HTTP clients)
  ],
  methods: ['GET', 'POST'],
  allowedHeaders: ['Content-Type', 'Authorization']
}));
```

**Note:** Native mobile apps (iOS/Android) are NOT subject to CORS restrictions. CORS only applies to browser-based requests.

---

## HTTPS Requirement for Mobile

### Why HTTPS is Required

| Platform | Requirement |
|----------|-------------|
| **iOS** | ATS (App Transport Security) blocks HTTP by default |
| **Android** | Android 9+ blocks cleartext HTTP by default |

### Solutions

1. **Use HTTPS** (recommended)
   - PaaS (Render/Fly.io) provides free HTTPS
   - VPS: Use Let's Encrypt with Nginx

2. **Development Exception** (testing only)
   - iOS: Add ATS exception in Info.plist
   - Android: Add `android:usesCleartextTraffic="true"` in manifest

---

## Health Check Configuration

The health endpoint is designed for monitoring:

| Property | Value |
|----------|-------|
| **URL** | `/api/health` |
| **Method** | GET |
| **Expected Status** | 200 |
| **Response** | `{"ok": true, ...}` |

### Example Health Check Response

```json
{
  "ok": true,
  "timestamp": "2024-01-01T12:00:00.000Z",
  "elevenlabsConfigured": true,
  "openaiConfigured": true,
  "xaiConfigured": true,
  "mockMode": false,
  "services": {
    "tts": "live",
    "stt": "live",
    "feedback": "live"
  }
}
```

### Monitoring Services

Configure your monitoring service (UptimeRobot, Pingdom, etc.):
- URL: `https://your-api.com/api/health`
- Interval: 60 seconds
- Alert if: `ok` is not `true` or status is not 200

---

## Docker Commands Reference

```bash
# Start services
docker-compose up -d

# Stop services
docker-compose down

# View logs
docker-compose logs -f backend

# Rebuild after code changes
docker-compose up -d --build

# Check status
docker-compose ps

# Enter container shell
docker-compose exec backend sh

# Clear cache (restart to recreate volume)
docker-compose down -v
docker-compose up -d

# View resource usage
docker stats
```

---

## Troubleshooting

### Connection Refused

```bash
# Check if container is running
docker-compose ps

# Check container logs
docker-compose logs backend

# Verify port binding
netstat -tlnp | grep 3000
```

### API Keys Not Working

```bash
# Check environment variables in container
docker-compose exec backend env | grep API

# Verify health endpoint shows services as "live"
curl http://localhost:3000/api/health
```

### HTTPS Not Working

```bash
# Check Nginx status
sudo systemctl status nginx

# Check Nginx error log
sudo tail -f /var/log/nginx/error.log

# Verify certificate
sudo certbot certificates
```

### Memory Issues

```bash
# Check container memory
docker stats

# Increase limits in docker-compose.yml
# Under deploy.resources.limits, increase memory
```

---

## Production Checklist

Before going live:

- [ ] All three API keys configured
- [ ] `NODE_ENV=production` set
- [ ] HTTPS enabled
- [ ] Health check verified
- [ ] Monitoring configured
- [ ] Logs accessible
- [ ] Backup strategy for cache (optional)
- [ ] Rate limiting in place (built-in: 60 req/min)

---

## Cost Estimates

### PaaS Hosting

| Service | Free Tier | Paid Tier |
|---------|-----------|-----------|
| **Render** | 750 hours/month | $7/month (Starter) |
| **Fly.io** | 3 shared VMs | $1.94/month (basic) |

### API Costs (per 1000 uses)

| API | Cost | Notes |
|-----|------|-------|
| **ElevenLabs TTS** | ~$0.30 | Caching reduces repeated calls |
| **OpenAI Whisper** | ~$0.60 | $0.006/minute of audio |
| **xAI Grok** | ~$0.15 | Per 1M tokens input |

**With caching enabled**, repeated sentences cost $0 for TTS/STT.
