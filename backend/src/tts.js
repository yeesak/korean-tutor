/**
 * TTS Handler - ElevenLabs Text-to-Speech
 *
 * REAL mode only - no mock fallback.
 * Returns HTTP 503 if ELEVENLABS_API_KEY not configured.
 */

const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { retryFetch } = require('./retry');
const { isElevenLabsConfigured } = require('./mockMode');

// Cache directory for TTS files
const TTS_CACHE_DIR = path.join(__dirname, '..', 'cache', 'tts');

// Ensure cache directory exists
if (!fs.existsSync(TTS_CACHE_DIR)) {
  fs.mkdirSync(TTS_CACHE_DIR, { recursive: true });
}

// Default ElevenLabs parameters
const DEFAULT_MODEL_ID = 'eleven_multilingual_v2';
const DEFAULT_OUTPUT_FORMAT = 'mp3_44100_128';
const DEFAULT_VOICE_ID = '21m00Tcm4TlvDq8ikWAM'; // Rachel voice

// Default voice settings
const DEFAULT_VOICE_SETTINGS = {
  stability: 0.5,
  similarity_boost: 0.75,
  style: 0.0,
  use_speaker_boost: true
};

// API timeout in milliseconds
const API_TIMEOUT_MS = 30000;

/**
 * Generate cache key using sha256
 */
function generateCacheKey(text, voiceId) {
  const data = JSON.stringify({ text, voiceId });
  return crypto.createHash('sha256').update(data).digest('hex');
}

function getCachePath(cacheKey) {
  return path.join(TTS_CACHE_DIR, `${cacheKey}.mp3`);
}

function isCached(cacheKey) {
  return fs.existsSync(getCachePath(cacheKey));
}

function readCache(cacheKey) {
  return fs.readFileSync(getCachePath(cacheKey));
}

function writeCache(cacheKey, data) {
  fs.writeFileSync(getCachePath(cacheKey), data);
}

/**
 * POST /api/tts handler
 *
 * Request body: { text: string }
 * Response: audio/mpeg
 */
async function ttsHandler(req, res) {
  try {
    // Check if configured
    if (!isElevenLabsConfigured()) {
      return res.status(503).json({
        ok: false,
        error: 'Service unavailable',
        details: 'ELEVENLABS_API_KEY not configured'
      });
    }

    const { text } = req.body;

    // Validate text
    if (!text || typeof text !== 'string') {
      return res.status(400).json({
        ok: false,
        error: 'Missing or invalid "text" field'
      });
    }

    if (text.length > 5000) {
      return res.status(400).json({
        ok: false,
        error: 'Text too long (max 5000 chars)'
      });
    }

    // Get voice ID from env or use default
    const voiceId = process.env.ELEVENLABS_VOICE_ID || DEFAULT_VOICE_ID;

    // Check cache
    const cacheKey = generateCacheKey(text, voiceId);
    if (isCached(cacheKey)) {
      const cachedAudio = readCache(cacheKey);
      if (cachedAudio && cachedAudio.length > 100) {
        console.log(`[TTS] Cache HIT for "${text.substring(0, 30)}..." (${cachedAudio.length} bytes)`);
        res.set('Content-Type', 'audio/mpeg');
        res.set('X-Cache', 'HIT');
        return res.send(cachedAudio);
      } else {
        console.log(`[TTS] Cache invalid for "${text.substring(0, 30)}...", refetching`);
      }
    }

    // Call ElevenLabs API
    console.log(`[TTS] Calling ElevenLabs for "${text.substring(0, 30)}..."`);

    const fetch = (await import('node-fetch')).default;
    const url = `https://api.elevenlabs.io/v1/text-to-speech/${voiceId}?output_format=${DEFAULT_OUTPUT_FORMAT}`;

    const response = await retryFetch(async () => {
      const controller = new AbortController();
      const timeoutId = setTimeout(() => controller.abort(), API_TIMEOUT_MS);

      try {
        const res = await fetch(url, {
          method: 'POST',
          headers: {
            'Accept': 'audio/mpeg',
            'Content-Type': 'application/json',
            'xi-api-key': process.env.ELEVENLABS_API_KEY
          },
          body: JSON.stringify({
            text,
            model_id: DEFAULT_MODEL_ID,
            voice_settings: DEFAULT_VOICE_SETTINGS
          }),
          signal: controller.signal
        });
        clearTimeout(timeoutId);
        return res;
      } catch (err) {
        clearTimeout(timeoutId);
        throw err;
      }
    });

    // Log response details
    console.log(`[TTS] ElevenLabs response: status=${response.status}, content-type=${response.headers.get('content-type')}`);

    if (!response.ok) {
      const errorText = await response.text();
      console.error(`[TTS] ElevenLabs error (${response.status}):`, errorText);
      return res.status(502).json({
        ok: false,
        error: 'ElevenLabs API error',
        details: errorText
      });
    }

    const audioBuffer = Buffer.from(await response.arrayBuffer());
    console.log(`[TTS] Received MP3: ${audioBuffer.length} bytes`);

    // Validate audio data
    if (!audioBuffer || audioBuffer.length === 0) {
      console.error('[TTS] ElevenLabs returned empty audio');
      return res.status(502).json({
        ok: false,
        error: 'ElevenLabs returned empty audio',
        details: 'Audio buffer is empty'
      });
    }

    // Cache the result
    writeCache(cacheKey, audioBuffer);
    console.log(`[TTS] Cached ${audioBuffer.length} bytes`);

    res.set('Content-Type', 'audio/mpeg');
    res.set('X-Cache', 'MISS');
    res.send(audioBuffer);

  } catch (err) {
    console.error('[TTS] Error:', err.message);
    res.status(500).json({
      ok: false,
      error: 'TTS processing failed',
      details: err.message
    });
  }
}

/**
 * Get TTS cache statistics
 */
function getTTSCacheStats() {
  try {
    if (!fs.existsSync(TTS_CACHE_DIR)) {
      return { entries: 0, totalSizeBytes: 0 };
    }
    const files = fs.readdirSync(TTS_CACHE_DIR).filter(f => f.endsWith('.wav') || f.endsWith('.mp3'));
    let totalSize = 0;
    for (const file of files) {
      const stat = fs.statSync(path.join(TTS_CACHE_DIR, file));
      totalSize += stat.size;
    }
    return { entries: files.length, totalSizeBytes: totalSize };
  } catch (err) {
    return { error: err.message };
  }
}

/**
 * Clear TTS cache
 */
function clearTTSCache() {
  try {
    if (!fs.existsSync(TTS_CACHE_DIR)) return { cleared: 0 };
    const files = fs.readdirSync(TTS_CACHE_DIR).filter(f => f.endsWith('.wav') || f.endsWith('.mp3'));
    for (const file of files) {
      fs.unlinkSync(path.join(TTS_CACHE_DIR, file));
    }
    return { cleared: files.length };
  } catch (err) {
    return { error: err.message };
  }
}

module.exports = {
  ttsHandler,
  getTTSCacheStats,
  clearTTSCache
};
