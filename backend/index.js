/**
 * AI-demo Backend Server
 * Shadowing 3x Tutor - API proxy for TTS, STT, and Feedback
 *
 * Services:
 * - TTS: ElevenLabs (eleven_multilingual_v2)
 * - STT: ElevenLabs (scribe_v1)
 * - LLM: xAI Grok (for feedback generation)
 *
 * REAL mode by default. Returns HTTP 503 if API keys are missing.
 *
 * Deploy-ready for: Render, Railway, Fly.io, Docker
 */

require('dotenv').config();

const express = require('express');
const cors = require('cors');
const multer = require('multer');
const path = require('path');
const fs = require('fs');
const crypto = require('crypto');

const { ttsHandler, clearTTSCache, getTTSCacheStats } = require('./src/tts');
const { sttHandler, clearSTTCache, getSTTCacheStats } = require('./src/stt');
const { feedbackHandler } = require('./src/feedback');
const { pronounceHandler } = require('./src/pronounce');
const { evalHandler } = require('./src/eval');
const { getSentences } = require('./src/sentences');
const { grokHandler } = require('./src/grok');
const { getConfigStatus, getMode, logStartupStatus, isElevenLabsConfigured, isXAIConfigured } = require('./src/mockMode');
const { isXAIRealtimeConfigured } = require('./src/xaiRealtimeClient');
const { createRateLimiter } = require('./src/rateLimit');
const {
  stubTtsHandler,
  stubSttHandler,
  stubEvalHandler,
  stubFeedbackHandler,
  stubGrokHandler,
  stubPronounceHandler
} = require('./src/stubs');

// Helper to select real or stub handler based on mode
const isMockMode = () => getMode() === 'mock';

// =========================
// Strict Environment Validation (Production only, skip in MOCK mode)
// =========================
function validateEnvironment() {
  // Skip validation in MOCK mode - stubs will be used
  if ((process.env.MODE || '').toUpperCase() === 'MOCK') {
    console.log('\n📦 MODE=MOCK - Skipping API key validation (using stubs)\n');
    return;
  }

  const required = [];
  const warnings = [];

  // At least one of ELEVENLABS or XAI must be configured for the app to be useful
  if (!process.env.ELEVENLABS_API_KEY) {
    required.push('ELEVENLABS_API_KEY (required for TTS/STT)');
  }
  if (!process.env.XAI_API_KEY) {
    warnings.push('XAI_API_KEY (required for Grok feedback - app will work with limited features)');
  }

  if (required.length > 0) {
    console.error('\n❌ FATAL: Missing required environment variables:');
    required.forEach(v => console.error(`   - ${v}`));
    if (warnings.length > 0) {
      console.warn('\n⚠️  Also missing (optional but recommended):');
      warnings.forEach(v => console.warn(`   - ${v}`));
    }
    console.error('\nSet these in your .env file or deployment environment.\n');
    console.error('💡 TIP: Set MODE=MOCK to run with stub data for testing.\n');
    process.exit(1);
  }

  if (warnings.length > 0) {
    console.warn('\n⚠️  Missing optional environment variables:');
    warnings.forEach(v => console.warn(`   - ${v}`));
    console.warn('');
  }
}

// Run validation in production mode
if (process.env.NODE_ENV === 'production') {
  validateEnvironment();
}

const app = express();
const PORT = process.env.PORT || 3000;

// Environment
const isProd = process.env.NODE_ENV === 'production';
const isDev = process.env.NODE_ENV === 'development' || !process.env.NODE_ENV;

// =========================
// Production-ready Middleware
// =========================

// Trust proxy (required for Render/Railway/Fly.io behind load balancers)
app.set('trust proxy', 1);

// CORS - wide open for mobile app testing (restrict in production if needed)
app.use(cors({
  origin: true,
  credentials: true,
  methods: ['GET', 'POST', 'OPTIONS'],
  allowedHeaders: ['Content-Type', 'Authorization', 'X-Request-ID']
}));

// JSON body parser with size limit
app.use(express.json({ limit: '2mb' }));

// Request logging middleware with request ID for debugging
app.use((req, res, next) => {
  const timestamp = new Date().toISOString().substr(11, 12);
  const contentLength = req.headers['content-length'] || '-';

  // Generate short request ID for tracing
  const reqId = req.headers['x-request-id'] || crypto.randomBytes(4).toString('hex');
  req.reqId = reqId;
  res.setHeader('X-Request-ID', reqId);

  console.log(`[${timestamp}] ${reqId} ${req.method} ${req.path} (${contentLength} bytes)`);

  // Log response when finished
  const start = Date.now();
  res.on('finish', () => {
    const duration = Date.now() - start;
    console.log(`[${timestamp}] ${reqId} ${req.method} ${req.path} -> ${res.statusCode} (${duration}ms)`);
  });

  next();
});

// Rate limiting (60 requests per minute per IP)
const rateLimiter = createRateLimiter(60);
app.use('/api', rateLimiter);

// Multer for file uploads (STT audio)
const upload = multer({
  storage: multer.memoryStorage(),
  limits: { fileSize: 25 * 1024 * 1024 } // 25MB max (matches OpenAI Whisper limit)
});

// Ensure cache directory exists
const cacheDir = path.join(__dirname, 'cache');
if (!fs.existsSync(cacheDir)) {
  fs.mkdirSync(cacheDir, { recursive: true });
}

// =========================
// Routes
// =========================

/**
 * GET /health (root level for cloud health checks)
 * Minimal health check for load balancers
 */
app.get('/health', (req, res) => {
  res.json({
    ok: true,
    ts: new Date().toISOString()
  });
});

/**
 * GET /api/health
 * Detailed health check endpoint
 * Returns: { ok, mode, ttsConfigured, sttConfigured, llmConfigured, ... }
 */
app.get('/api/health', (req, res) => {
  const configStatus = getConfigStatus();

  res.json({
    ok: true,
    timestamp: new Date().toISOString(),  // Unity expects "timestamp" not "ts"
    version: '1.0.0',
    ...configStatus
  });
});

/**
 * POST /api/tts
 * Text-to-Speech via ElevenLabs (or stub in mock mode)
 * Body: { text: string, voice_id?: string }
 * Returns: audio/mpeg
 */
app.post('/api/tts', (req, res, next) => {
  if (isMockMode()) return stubTtsHandler(req, res);
  return ttsHandler(req, res, next);
});

/**
 * POST /api/stt
 * Speech-to-Text via ElevenLabs (or stub in mock mode)
 * Body: multipart/form-data with 'audio' file
 * Returns: { ok, text, language, confidence }
 */
app.post('/api/stt', upload.single('audio'), (req, res, next) => {
  if (isMockMode()) return stubSttHandler(req, res);
  return sttHandler(req, res, next);
});

/**
 * POST /api/feedback
 * Get pronunciation/shadowing feedback (or stub in mock mode)
 * Body: { targetText: string, transcriptText: string } (also accepts sttText for backward compat)
 * Returns: Structured feedback with metrics, diff, grammar corrections
 */
app.post('/api/feedback', (req, res, next) => {
  if (isMockMode()) return stubFeedbackHandler(req, res);
  return feedbackHandler(req, res, next);
});

/**
 * POST /api/grok
 * Dynamic tutor line generation via xAI Grok (or stub in mock mode)
 * Body: { model?: string, messages: [{role, content}], temperature?: number, max_tokens?: number }
 * Returns: Grok chat completion response (choices[0].message.content)
 * Used by Unity GrokClient for context-aware Korean tutor speech
 */
app.post('/api/grok', (req, res, next) => {
  if (isMockMode()) return stubGrokHandler(req, res);
  return grokHandler(req, res, next);
});

/**
 * GET /api/sentences
 * Get sentences list for shadowing practice
 * Query: ?category=daily|travel|cafe|school|work (optional)
 * Returns: { sentences: Array<{id, korean, english, category}> }
 */
app.get('/api/sentences', getSentences);

/**
 * POST /api/pronounce_grok
 * Voice-based pronunciation feedback via xAI Realtime API (or stub in mock mode)
 * Body: multipart/form-data with 'audio' file + targetText + transcriptText (optional)
 * Returns: { ok, tutor: { weakPronunciation, strongPronunciation, shortComment } }
 */
app.post('/api/pronounce_grok', upload.single('audio'), (req, res, next) => {
  if (isMockMode()) return stubPronounceHandler(req, res);
  return pronounceHandler(req, res, next);
});

/**
 * POST /api/eval
 * Combined evaluation endpoint (or stub in mock mode)
 * A) ElevenLabs STT -> transcriptText
 * B) Punctuation-insensitive text scoring
 * C) xAI Realtime pronunciation feedback (optional)
 * D) xAI Text grammar corrections
 * Body: multipart/form-data with 'audio' file + targetText + locale (optional)
 * Returns: { ok, transcriptText, textAccuracyPercent, mistakePercent, diff, pronunciation, grammar }
 */
app.post('/api/eval', upload.single('audio'), (req, res, next) => {
  if (isMockMode()) return stubEvalHandler(req, res);
  return evalHandler(req, res, next);
});

/**
 * POST /api/cache/clear
 * Clear TTS and STT caches (development only)
 * Returns: { ok, cleared: { tts, stt } }
 */
app.post('/api/cache/clear', (req, res) => {
  // Only allow in development mode
  if (isProd) {
    return res.status(403).json({
      ok: false,
      error: 'Cache clear disabled in production'
    });
  }

  const ttsResult = clearTTSCache();
  const sttResult = clearSTTCache();

  console.log(`[Cache] Cleared: TTS=${ttsResult.cleared}, STT=${sttResult.cleared}`);

  res.json({
    ok: true,
    cleared: {
      tts: ttsResult.cleared || 0,
      stt: sttResult.cleared || 0
    }
  });
});

/**
 * GET /api/cache/stats
 * Get cache statistics (development only)
 * Returns: { ok, stats: { tts, stt } }
 */
app.get('/api/cache/stats', (req, res) => {
  // Only allow in development mode
  if (isProd) {
    return res.status(403).json({
      ok: false,
      error: 'Cache stats disabled in production'
    });
  }

  res.json({
    ok: true,
    stats: {
      tts: getTTSCacheStats(),
      stt: getSTTCacheStats()
    }
  });
});

// Error handling middleware
app.use((err, req, res, next) => {
  console.error('Error:', err.message);
  res.status(500).json({
    error: 'Internal server error',
    message: err.message
  });
});

// 404 handler
app.use((req, res) => {
  res.status(404).json({ error: 'Not found' });
});

// Start server
if (require.main === module) {
  const HOST = process.env.HOST || '0.0.0.0';
  const mode = getMode();

  app.listen(PORT, HOST, () => {
    console.log(`\n🎯 AI-demo Backend Server`);
    console.log(`   Running on: http://${HOST}:${PORT}`);
    console.log(`   Environment: ${isProd ? 'PRODUCTION' : 'DEVELOPMENT'}`);
    if (mode === 'mock') {
      console.log(`   🧪 MODE: MOCK (using stub responses - no API keys needed)`);
    }
    console.log(`   Rate Limit: 60 req/min per IP`);

    // Log detailed startup status (mode + missing keys)
    logStartupStatus();

    console.log(`   Endpoints:`);
    console.log(`   - GET  /health (root health check)`);
    console.log(`   - GET  /api/health (detailed)`);
    console.log(`   - POST /api/tts${mode === 'mock' ? ' [STUB]' : ''}`);
    console.log(`   - POST /api/stt${mode === 'mock' ? ' [STUB]' : ''}`);
    console.log(`   - POST /api/feedback${mode === 'mock' ? ' [STUB]' : ''}`);
    console.log(`   - POST /api/grok${mode === 'mock' ? ' [STUB]' : ''}`);
    console.log(`   - POST /api/pronounce_grok${mode === 'mock' ? ' [STUB]' : ''}`);
    console.log(`   - POST /api/eval${mode === 'mock' ? ' [STUB]' : ''}`);
    console.log(`   - GET  /api/sentences`);
    if (isDev) {
      console.log(`   - POST /api/cache/clear (dev only)`);
      console.log(`   - GET  /api/cache/stats (dev only)`);
    }
    if (mode !== 'mock') {
      console.log(`   xAI Realtime: ${isXAIRealtimeConfigured() ? 'ENABLED' : 'DISABLED (no XAI_API_KEY)'}`)
    }

    // Print LAN IP helper for mobile testing
    printLanIpHelp();

    console.log('');
  });
}

/**
 * Print LAN IP addresses for mobile testing
 */
function printLanIpHelp() {
  try {
    const os = require('os');
    const interfaces = os.networkInterfaces();
    const lanIps = [];

    for (const name of Object.keys(interfaces)) {
      for (const iface of interfaces[name]) {
        // Skip internal and non-IPv4
        if (iface.internal || iface.family !== 'IPv4') continue;
        lanIps.push({ name, address: iface.address });
      }
    }

    if (lanIps.length > 0) {
      console.log('');
      console.log('   📱 For Android/iOS testing, use one of these LAN URLs:');
      lanIps.forEach(({ name, address }) => {
        console.log(`      http://${address}:${PORT}  (${name})`);
      });
      console.log('');
      console.log('   Test from phone browser: http://<IP>:' + PORT + '/api/health');
    }
  } catch (e) {
    // Ignore network interface errors
  }
}

module.exports = app;
