/**
 * STT Handler - ElevenLabs Speech-to-Text
 *
 * REAL mode only - no mock fallback.
 * Returns HTTP 503 if ELEVENLABS_API_KEY not configured.
 *
 * ElevenLabs STT API: https://elevenlabs.io/docs/api-reference/speech-to-text
 */

const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { retryFetch } = require('./retry');
const { isElevenLabsConfigured } = require('./mockMode');

// Cache directory for STT results
const STT_CACHE_DIR = path.join(__dirname, '..', 'cache', 'stt');

// Ensure cache directory exists
if (!fs.existsSync(STT_CACHE_DIR)) {
  fs.mkdirSync(STT_CACHE_DIR, { recursive: true });
}

// API timeout in milliseconds
const API_TIMEOUT_MS = 60000;

// Maximum file size: 25MB
const MAX_FILE_SIZE = 25 * 1024 * 1024;

/**
 * Sanitize transcript text for UI display
 * - Remove bracketed/annotated noise like [music], (music), <noise>, [background], etc.
 * - Collapse whitespace
 * - Do NOT add any parentheses or per-character wrapping
 */
function sanitizeTranscript(text) {
  if (!text || typeof text !== 'string') return '';

  let cleaned = text
    // Remove square brackets content: [music], [background], [applause], etc.
    .replace(/\[[^\]]*\]/g, '')
    // Remove parentheses content that look like annotations: (music), (background noise), etc.
    .replace(/\((?:music|noise|background|applause|laughter|silence|inaudible|unclear|crosstalk|foreign|speaking\s+\w+)[^)]*\)/gi, '')
    // Remove angle bracket content: <noise>, <unk>, etc.
    .replace(/<[^>]*>/g, '')
    // Remove asterisk annotations: *music*, *inaudible*, etc.
    .replace(/\*[^*]+\*/g, '')
    // Collapse multiple whitespace into single space
    .replace(/\s+/g, ' ')
    // Trim leading/trailing whitespace
    .trim();

  return cleaned;
}

/**
 * Generate cache key from audio buffer
 */
function generateCacheKey(audioBuffer) {
  return crypto.createHash('sha256').update(audioBuffer).digest('hex');
}

function getCachePath(cacheKey) {
  return path.join(STT_CACHE_DIR, `${cacheKey}.json`);
}

function isCached(cacheKey) {
  return fs.existsSync(getCachePath(cacheKey));
}

function readCache(cacheKey) {
  const data = fs.readFileSync(getCachePath(cacheKey), 'utf8');
  return JSON.parse(data);
}

function writeCache(cacheKey, result) {
  fs.writeFileSync(getCachePath(cacheKey), JSON.stringify(result, null, 2), 'utf8');
}

/**
 * POST /api/stt handler
 *
 * Request: multipart/form-data with 'audio' file
 * Response: { ok, text, confidence? }
 */
async function sttHandler(req, res) {
  try {
    // Check if configured
    if (!isElevenLabsConfigured()) {
      return res.status(503).json({
        ok: false,
        error: 'Service unavailable',
        details: 'ELEVENLABS_API_KEY not configured'
      });
    }

    // Check if file was uploaded
    if (!req.file) {
      return res.status(400).json({
        ok: false,
        error: 'No audio file uploaded',
        details: 'Use multipart/form-data with field name "audio"'
      });
    }

    const audioBuffer = req.file.buffer;

    // Validate file size
    if (audioBuffer.length > MAX_FILE_SIZE) {
      return res.status(400).json({
        ok: false,
        error: 'File too large',
        details: `Maximum file size is ${MAX_FILE_SIZE / (1024 * 1024)}MB`
      });
    }

    // Check cache
    const cacheKey = generateCacheKey(audioBuffer);
    if (isCached(cacheKey)) {
      console.log(`[STT] Cache HIT for ${cacheKey.substring(0, 12)}...`);
      const cachedResult = readCache(cacheKey);
      cachedResult.cached = true;
      res.set('X-Cache', 'HIT');
      return res.json(cachedResult);
    }

    // Call ElevenLabs STT API
    console.log(`[STT] Calling ElevenLabs STT for ${cacheKey.substring(0, 12)}... (${audioBuffer.length} bytes)`);

    const fetch = (await import('node-fetch')).default;
    const { FormData, Blob } = await import('node-fetch');

    const response = await retryFetch(async () => {
      const controller = new AbortController();
      const timeoutId = setTimeout(() => controller.abort(), API_TIMEOUT_MS);

      try {
        const formData = new FormData();
        const audioBlob = new Blob([audioBuffer], { type: 'audio/wav' });
        formData.append('file', audioBlob, 'audio.wav');
        formData.append('model_id', 'scribe_v1'); // ElevenLabs Scribe model
        // CRITICAL: Force Korean language to prevent Chinese/other language detection
        // Get language from request body (default to Korean)
        const requestedLanguage = req.body?.language || 'ko';
        formData.append('language_code', requestedLanguage);
        console.log(`[STT] Using language_code: ${requestedLanguage}`);

        const res = await fetch('https://api.elevenlabs.io/v1/speech-to-text', {
          method: 'POST',
          headers: {
            'xi-api-key': process.env.ELEVENLABS_API_KEY
          },
          body: formData,
          signal: controller.signal
        });
        clearTimeout(timeoutId);
        return res;
      } catch (err) {
        clearTimeout(timeoutId);
        throw err;
      }
    });

    if (!response.ok) {
      const errorText = await response.text();
      console.error(`[STT] ElevenLabs error (${response.status}):`, errorText);
      return res.status(response.status).json({
        ok: false,
        error: 'ElevenLabs STT API error',
        details: errorText
      });
    }

    const apiResult = await response.json();

    // Get raw text and sanitize for UI
    const rawText = apiResult.text || '';
    const cleanText = sanitizeTranscript(rawText);

    // Build standardized response
    const result = {
      ok: true,
      text: cleanText,                   // Clean text for UI display
      transcriptText: cleanText,         // Alias for UI (preferred field name)
      rawTranscriptText: rawText,        // Original text for debugging
      language: apiResult.language_code || 'ko',
      confidence: apiResult.language_probability || null
    };

    // Cache the result
    writeCache(cacheKey, result);
    console.log(`[STT] Result: "${cleanText.substring(0, 50)}${cleanText.length > 50 ? '...' : ''}"`);
    if (rawText !== cleanText) {
      console.log(`[STT] Raw (pre-sanitize): "${rawText.substring(0, 50)}${rawText.length > 50 ? '...' : ''}"`);
    }

    res.set('X-Cache', 'MISS');
    res.json(result);

  } catch (err) {
    console.error('[STT] Error:', err.message);
    res.status(500).json({
      ok: false,
      error: 'STT processing failed',
      details: err.message
    });
  }
}

/**
 * Get STT cache statistics
 */
function getSTTCacheStats() {
  try {
    if (!fs.existsSync(STT_CACHE_DIR)) {
      return { entries: 0, totalSizeBytes: 0 };
    }
    const files = fs.readdirSync(STT_CACHE_DIR).filter(f => f.endsWith('.json'));
    let totalSize = 0;
    for (const file of files) {
      const stat = fs.statSync(path.join(STT_CACHE_DIR, file));
      totalSize += stat.size;
    }
    return { entries: files.length, totalSizeBytes: totalSize };
  } catch (err) {
    return { error: err.message };
  }
}

/**
 * Clear STT cache
 */
function clearSTTCache() {
  try {
    if (!fs.existsSync(STT_CACHE_DIR)) return { cleared: 0 };
    const files = fs.readdirSync(STT_CACHE_DIR).filter(f => f.endsWith('.json'));
    for (const file of files) {
      fs.unlinkSync(path.join(STT_CACHE_DIR, file));
    }
    return { cleared: files.length };
  } catch (err) {
    return { error: err.message };
  }
}

module.exports = {
  sttHandler,
  getSTTCacheStats,
  clearSTTCache
};
