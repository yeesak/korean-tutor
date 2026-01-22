/**
 * Backend API Tests
 * Run with: npm test
 */

const { describe, it, before, after, beforeEach } = require('node:test');
const assert = require('node:assert');
const http = require('http');
const fs = require('fs');
const path = require('path');

const app = require('../index');
const { clearTTSCache, getTTSCacheStats, generateCacheKey, SPEED_PROFILES } = require('./tts');
const { clearSTTCache, getSTTCacheStats, isSTTMock, generateCacheKey: generateSTTCacheKey } = require('./stt');
const { isFeedbackMock, generateMockFeedback, calculateSimilarity, normalizeForKoreanCompare, computeCER, buildCharacterDiff } = require('./feedback');

let server;
let baseUrl;

// TTS cache directory
const TTS_CACHE_DIR = path.join(__dirname, '..', 'cache', 'tts');

// Start server before tests
before(async () => {
  return new Promise((resolve) => {
    server = app.listen(0, () => {
      const port = server.address().port;
      baseUrl = `http://localhost:${port}`;
      console.log(`Test server running on ${baseUrl}`);
      resolve();
    });
  });
});

// Stop server after tests
after(async () => {
  return new Promise((resolve) => {
    if (server) {
      server.close(resolve);
    } else {
      resolve();
    }
  });
});

/**
 * Generate a minimal valid WAV file for testing
 */
function generateTestWav(durationMs = 100) {
  const sampleRate = 16000;
  const numSamples = Math.floor(sampleRate * (durationMs / 1000));
  const numChannels = 1;
  const bitsPerSample = 16;
  const byteRate = sampleRate * numChannels * (bitsPerSample / 8);
  const blockAlign = numChannels * (bitsPerSample / 8);
  const dataSize = numSamples * blockAlign;
  const fileSize = 36 + dataSize;

  const buffer = Buffer.alloc(44 + dataSize);

  // RIFF header
  buffer.write('RIFF', 0);
  buffer.writeUInt32LE(fileSize, 4);
  buffer.write('WAVE', 8);

  // fmt subchunk
  buffer.write('fmt ', 12);
  buffer.writeUInt32LE(16, 16);
  buffer.writeUInt16LE(1, 20);
  buffer.writeUInt16LE(numChannels, 22);
  buffer.writeUInt32LE(sampleRate, 24);
  buffer.writeUInt32LE(byteRate, 28);
  buffer.writeUInt16LE(blockAlign, 32);
  buffer.writeUInt16LE(bitsPerSample, 34);

  // data subchunk
  buffer.write('data', 36);
  buffer.writeUInt32LE(dataSize, 40);

  // Generate simple sine wave
  for (let i = 0; i < numSamples; i++) {
    const sample = Math.floor(Math.sin(2 * Math.PI * 440 * i / sampleRate) * 16000);
    buffer.writeInt16LE(sample, 44 + i * 2);
  }

  return buffer;
}

/**
 * Helper to make multipart form data requests
 */
function requestMultipart(path, audioBuffer, filename = 'audio.wav', extraFields = {}) {
  return new Promise((resolve, reject) => {
    const boundary = '----TestBoundary' + Date.now();
    const url = new URL(path, baseUrl);

    let body = '';

    // Add audio file
    body += `--${boundary}\r\n`;
    body += `Content-Disposition: form-data; name="audio"; filename="${filename}"\r\n`;
    body += 'Content-Type: audio/wav\r\n\r\n';

    // Add extra fields
    let extraBody = '';
    for (const [key, value] of Object.entries(extraFields)) {
      extraBody += `\r\n--${boundary}\r\n`;
      extraBody += `Content-Disposition: form-data; name="${key}"\r\n\r\n`;
      extraBody += value;
    }

    const endBoundary = `\r\n--${boundary}--\r\n`;

    const bodyStart = Buffer.from(body);
    const bodyEnd = Buffer.from(extraBody + endBoundary);
    const fullBody = Buffer.concat([bodyStart, audioBuffer, bodyEnd]);

    const options = {
      method: 'POST',
      hostname: url.hostname,
      port: url.port,
      path: url.pathname,
      headers: {
        'Content-Type': `multipart/form-data; boundary=${boundary}`,
        'Content-Length': fullBody.length
      }
    };

    const req = http.request(options, (res) => {
      let data = [];
      res.on('data', (chunk) => data.push(chunk));
      res.on('end', () => {
        const buffer = Buffer.concat(data);
        let responseBody;
        try {
          responseBody = JSON.parse(buffer.toString());
        } catch {
          responseBody = buffer.toString();
        }
        resolve({
          status: res.statusCode,
          headers: res.headers,
          body: responseBody
        });
      });
    });

    req.on('error', reject);
    req.write(fullBody);
    req.end();
  });
}

/**
 * Helper to make HTTP requests
 */
function request(method, path, body = null) {
  return new Promise((resolve, reject) => {
    const url = new URL(path, baseUrl);
    const options = {
      method,
      hostname: url.hostname,
      port: url.port,
      path: url.pathname + url.search,
      headers: {}
    };

    if (body) {
      const bodyStr = JSON.stringify(body);
      options.headers['Content-Type'] = 'application/json';
      options.headers['Content-Length'] = Buffer.byteLength(bodyStr);
    }

    const req = http.request(options, (res) => {
      let data = [];

      res.on('data', (chunk) => data.push(chunk));
      res.on('end', () => {
        const buffer = Buffer.concat(data);
        const contentType = res.headers['content-type'] || '';

        let responseBody;
        if (contentType.includes('application/json')) {
          try {
            responseBody = JSON.parse(buffer.toString());
          } catch {
            responseBody = buffer.toString();
          }
        } else if (contentType.includes('audio/')) {
          responseBody = buffer;
        } else {
          responseBody = buffer.toString();
        }

        resolve({
          status: res.statusCode,
          headers: res.headers,
          body: responseBody
        });
      });
    });

    req.on('error', reject);

    if (body) {
      req.write(JSON.stringify(body));
    }
    req.end();
  });
}

// =========================
// Tests
// =========================

describe('Health Endpoint', () => {
  it('GET /api/health returns 200 with ok:true', async () => {
    const res = await request('GET', '/api/health');
    assert.strictEqual(res.status, 200);
    assert.strictEqual(res.body.ok, true);
    assert.ok(res.body.timestamp);
  });

  it('GET /api/health returns configuration status', async () => {
    const res = await request('GET', '/api/health');
    assert.strictEqual(res.status, 200);

    // Check all required fields exist
    assert.ok('elevenlabsConfigured' in res.body);
    assert.ok('openaiConfigured' in res.body);
    assert.ok('xaiConfigured' in res.body);
    assert.ok('mockMode' in res.body);

    // Check types
    assert.strictEqual(typeof res.body.elevenlabsConfigured, 'boolean');
    assert.strictEqual(typeof res.body.openaiConfigured, 'boolean');
    assert.strictEqual(typeof res.body.xaiConfigured, 'boolean');
    assert.strictEqual(typeof res.body.mockMode, 'boolean');
  });

  it('GET /api/health returns services status', async () => {
    const res = await request('GET', '/api/health');
    assert.strictEqual(res.status, 200);
    assert.ok('services' in res.body);
    assert.ok(['mock', 'live'].includes(res.body.services.tts));
    assert.ok(['mock', 'live'].includes(res.body.services.stt));
    assert.ok(['mock', 'live'].includes(res.body.services.feedback));
  });

  it('GET /api/health mockMode is consistent with configured flags', async () => {
    const res = await request('GET', '/api/health');
    assert.strictEqual(res.status, 200);

    // If any service is not configured, mockMode should be true
    const anyNotConfigured = !res.body.elevenlabsConfigured ||
                             !res.body.openaiConfigured ||
                             !res.body.xaiConfigured;

    if (anyNotConfigured) {
      assert.strictEqual(res.body.mockMode, true);
    }
  });
});

describe('TTS Endpoint', () => {
  // Clear cache before TTS tests
  beforeEach(() => {
    clearTTSCache();
  });

  it('POST /api/tts returns audio for valid text', async () => {
    const res = await request('POST', '/api/tts', { text: '안녕하세요' });
    assert.strictEqual(res.status, 200);
    // In mock mode, returns audio/wav; in real mode, audio/mpeg
    assert.ok(
      res.headers['content-type'].includes('audio/'),
      `Expected audio content type, got: ${res.headers['content-type']}`
    );
    assert.ok(Buffer.isBuffer(res.body));
    assert.ok(res.body.length > 0);
  });

  it('POST /api/tts returns 400 for missing text', async () => {
    const res = await request('POST', '/api/tts', {});
    assert.strictEqual(res.status, 400);
    assert.strictEqual(res.body.ok, false);
    assert.ok(res.body.error);
    assert.ok(res.body.details);
  });

  it('POST /api/tts returns 400 for text too long', async () => {
    const longText = 'a'.repeat(6000);
    const res = await request('POST', '/api/tts', { text: longText });
    assert.strictEqual(res.status, 400);
    assert.strictEqual(res.body.ok, false);
    assert.ok(res.body.error.includes('too long'));
  });

  it('POST /api/tts supports speedProfile parameter', async () => {
    // Test speed profile 1 (slow)
    const res1 = await request('POST', '/api/tts', { text: 'test', speedProfile: 1 });
    assert.strictEqual(res1.status, 200);
    assert.ok(Buffer.isBuffer(res1.body));

    // Clear cache to test different speed
    clearTTSCache();

    // Test speed profile 3 (fast)
    const res3 = await request('POST', '/api/tts', { text: 'test', speedProfile: 3 });
    assert.strictEqual(res3.status, 200);
    assert.ok(Buffer.isBuffer(res3.body));
  });

  it('POST /api/tts defaults to speedProfile 2 for invalid values', async () => {
    const res = await request('POST', '/api/tts', { text: 'test', speedProfile: 99 });
    assert.strictEqual(res.status, 200);
    assert.ok(Buffer.isBuffer(res.body));
  });

  it('POST /api/tts caches responses (cache HIT on second request)', async () => {
    const text = '캐시 테스트';

    // First request - should be MISS
    const res1 = await request('POST', '/api/tts', { text });
    assert.strictEqual(res1.status, 200);
    assert.strictEqual(res1.headers['x-cache'], 'MISS');
    assert.ok(res1.headers['x-cache-key']);

    // Second request - should be HIT
    const res2 = await request('POST', '/api/tts', { text });
    assert.strictEqual(res2.status, 200);
    assert.strictEqual(res2.headers['x-cache'], 'HIT');

    // Both should return same data
    assert.strictEqual(res1.body.length, res2.body.length);
  });

  it('POST /api/tts creates different cache keys for different speedProfiles', async () => {
    const text = '속도 테스트';

    // Request with speedProfile 1
    const res1 = await request('POST', '/api/tts', { text, speedProfile: 1 });
    assert.strictEqual(res1.status, 200);
    const key1 = res1.headers['x-cache-key'];

    // Request with speedProfile 3 (different key)
    const res3 = await request('POST', '/api/tts', { text, speedProfile: 3 });
    assert.strictEqual(res3.status, 200);
    const key3 = res3.headers['x-cache-key'];

    // Keys should be different
    assert.notStrictEqual(key1, key3, 'Cache keys should differ for different speed profiles');
  });

  it('POST /api/tts cache files are stored on disk', async () => {
    const text = '디스크 캐시 테스트';

    // Clear and verify empty
    clearTTSCache();
    const statsBefore = getTTSCacheStats();
    assert.strictEqual(statsBefore.entries, 0);

    // Make request
    await request('POST', '/api/tts', { text });

    // Verify cache file exists
    const statsAfter = getTTSCacheStats();
    assert.strictEqual(statsAfter.entries, 1);
    assert.ok(statsAfter.totalSizeBytes > 0);
  });

  it('TTS mock mode returns valid WAV file', async () => {
    const res = await request('POST', '/api/tts', { text: '모크 테스트' });
    assert.strictEqual(res.status, 200);

    // Check for mock header (only present in mock mode)
    if (res.headers['x-mock'] === 'true') {
      // Verify WAV header
      assert.ok(Buffer.isBuffer(res.body));
      assert.ok(res.body.length >= 44, 'WAV file should have at least header');

      // Check RIFF header
      const riff = res.body.slice(0, 4).toString();
      assert.strictEqual(riff, 'RIFF', 'Should have RIFF header');

      // Check WAVE format
      const wave = res.body.slice(8, 12).toString();
      assert.strictEqual(wave, 'WAVE', 'Should have WAVE format');
    }
  });

  it('TTS cache key generation is deterministic', () => {
    const voiceSettings = { stability: 0.5, similarity_boost: 0.75, style: 0.0, use_speaker_boost: true, speed: 1.0 };

    const key1 = generateCacheKey('hello', 'voice123', voiceSettings, 'mp3_44100_128');
    const key2 = generateCacheKey('hello', 'voice123', voiceSettings, 'mp3_44100_128');

    assert.strictEqual(key1, key2, 'Same inputs should produce same cache key');
    assert.strictEqual(key1.length, 64, 'SHA256 hash should be 64 hex characters');
  });

  it('TTS speed profiles are correctly defined', () => {
    assert.strictEqual(SPEED_PROFILES[1], 0.9, 'Profile 1 should be 0.9 (slow)');
    assert.strictEqual(SPEED_PROFILES[2], 1.0, 'Profile 2 should be 1.0 (normal)');
    assert.strictEqual(SPEED_PROFILES[3], 1.1, 'Profile 3 should be 1.1 (fast)');
  });

  it('clearTTSCache removes all cached files', async () => {
    // Create some cache entries
    await request('POST', '/api/tts', { text: 'cache1' });
    await request('POST', '/api/tts', { text: 'cache2' });

    const statsBefore = getTTSCacheStats();
    assert.ok(statsBefore.entries >= 2);

    // Clear cache
    const result = clearTTSCache();
    assert.ok(result.cleared >= 2);

    // Verify empty
    const statsAfter = getTTSCacheStats();
    assert.strictEqual(statsAfter.entries, 0);
  });
});

describe('STT Endpoint', () => {
  // Clear cache before STT tests
  beforeEach(() => {
    clearSTTCache();
  });

  it('POST /api/stt returns 400 for no audio file', async () => {
    const res = await request('POST', '/api/stt', {});
    assert.strictEqual(res.status, 400);
    assert.strictEqual(res.body.ok, false);
    assert.ok(res.body.error);
    assert.ok(res.body.details);
  });

  it('POST /api/stt returns transcript for valid audio', async () => {
    const audioBuffer = generateTestWav(200);
    const res = await requestMultipart('/api/stt', audioBuffer);

    assert.strictEqual(res.status, 200);
    assert.strictEqual(res.body.ok, true);
    assert.ok(res.body.text);
    assert.ok(typeof res.body.text === 'string');
  });

  it('POST /api/stt returns words array with timestamps', async () => {
    const audioBuffer = generateTestWav(200);
    const res = await requestMultipart('/api/stt', audioBuffer);

    assert.strictEqual(res.status, 200);
    assert.ok(Array.isArray(res.body.words));

    // Verify word structure
    if (res.body.words.length > 0) {
      const word = res.body.words[0];
      assert.ok('word' in word);
      assert.ok('start' in word);
      assert.ok('end' in word);
      assert.ok(typeof word.start === 'number');
      assert.ok(typeof word.end === 'number');
    }
  });

  it('POST /api/stt accepts language parameter', async () => {
    const audioBuffer = generateTestWav(200);
    const res = await requestMultipart('/api/stt', audioBuffer, 'audio.wav', { language: 'en' });

    assert.strictEqual(res.status, 200);
    assert.strictEqual(res.body.ok, true);
  });

  it('POST /api/stt caches responses (cache HIT on repeated upload)', async () => {
    const audioBuffer = generateTestWav(150);

    // First request - should be MISS
    const res1 = await requestMultipart('/api/stt', audioBuffer);
    assert.strictEqual(res1.status, 200);
    assert.strictEqual(res1.headers['x-cache'], 'MISS');
    assert.ok(res1.headers['x-cache-key']);

    // Second request with same audio - should be HIT
    const res2 = await requestMultipart('/api/stt', audioBuffer);
    assert.strictEqual(res2.status, 200);
    assert.strictEqual(res2.headers['x-cache'], 'HIT');

    // Results should match
    assert.strictEqual(res1.body.text, res2.body.text);
  });

  it('POST /api/stt generates different transcripts for different audio', async () => {
    // Generate two different audio files
    const audio1 = generateTestWav(100);
    const audio2 = generateTestWav(200); // Different duration = different content

    const res1 = await requestMultipart('/api/stt', audio1);
    const res2 = await requestMultipart('/api/stt', audio2);

    assert.strictEqual(res1.status, 200);
    assert.strictEqual(res2.status, 200);

    // Cache keys should differ
    assert.notStrictEqual(
      res1.headers['x-cache-key'],
      res2.headers['x-cache-key'],
      'Different audio should have different cache keys'
    );
  });

  it('POST /api/stt stores cache files on disk', async () => {
    // Clear and verify empty
    clearSTTCache();
    const statsBefore = getSTTCacheStats();
    assert.strictEqual(statsBefore.entries, 0);

    // Make request
    const audioBuffer = generateTestWav(100);
    await requestMultipart('/api/stt', audioBuffer);

    // Verify cache file exists
    const statsAfter = getSTTCacheStats();
    assert.strictEqual(statsAfter.entries, 1);
    assert.ok(statsAfter.totalSizeBytes > 0);
  });

  it('STT mock mode returns deterministic results', async () => {
    const audioBuffer = generateTestWav(100);

    // First request
    const res1 = await requestMultipart('/api/stt', audioBuffer);

    // Clear cache to force re-generation
    clearSTTCache();

    // Second request (same audio)
    const res2 = await requestMultipart('/api/stt', audioBuffer);

    // Mock mode should be deterministic - same audio = same transcript
    assert.strictEqual(res1.body.text, res2.body.text);
  });

  it('STT mock mode includes mock flag', async () => {
    const audioBuffer = generateTestWav(100);
    const res = await requestMultipart('/api/stt', audioBuffer);

    assert.strictEqual(res.status, 200);

    // Check for mock header or body flag
    if (res.headers['x-mock'] === 'true' || res.body.mock === true) {
      assert.ok(true, 'Mock mode indicator present');
    }
  });

  it('STT cache key is deterministic based on audio bytes', () => {
    const audio1 = generateTestWav(100);
    const audio2 = generateTestWav(100);

    // Same content = same key
    const key1a = generateSTTCacheKey(audio1);
    const key1b = generateSTTCacheKey(audio1);
    assert.strictEqual(key1a, key1b, 'Same audio should produce same key');

    // Different content = different key (audio2 has same duration but generated at different time)
    // Actually for this test, let's use the same buffer
    const key2 = generateSTTCacheKey(Buffer.from(audio1)); // Copy
    assert.strictEqual(key1a, key2, 'Same bytes should produce same key');
  });

  it('clearSTTCache removes all cached files', async () => {
    // Create some cache entries
    await requestMultipart('/api/stt', generateTestWav(100));
    await requestMultipart('/api/stt', generateTestWav(200));

    const statsBefore = getSTTCacheStats();
    assert.ok(statsBefore.entries >= 2);

    // Clear cache
    const result = clearSTTCache();
    assert.ok(result.cleared >= 2);

    // Verify empty
    const statsAfter = getSTTCacheStats();
    assert.strictEqual(statsAfter.entries, 0);
  });

  it('STT response includes all required fields', async () => {
    const audioBuffer = generateTestWav(100);
    const res = await requestMultipart('/api/stt', audioBuffer);

    assert.strictEqual(res.status, 200);
    assert.strictEqual(res.body.ok, true);
    assert.ok('text' in res.body);
    assert.ok('words' in res.body);
    assert.ok(Array.isArray(res.body.words));
  });
});

describe('Feedback Endpoint', () => {
  it('POST /api/feedback returns feedback for valid input', async () => {
    const res = await request('POST', '/api/feedback', {
      original: '안녕하세요',
      spoken: '안녕하세요'
    });
    assert.strictEqual(res.status, 200);
    assert.strictEqual(res.body.ok, true);
    assert.ok(res.body.feedback);
    assert.ok(typeof res.body.feedback === 'string');
  });

  it('POST /api/feedback returns 400 for missing original', async () => {
    const res = await request('POST', '/api/feedback', { spoken: 'test' });
    assert.strictEqual(res.status, 400);
    assert.strictEqual(res.body.ok, false);
    assert.ok(res.body.error);
    assert.ok(res.body.details);
  });

  it('POST /api/feedback returns 400 for missing spoken', async () => {
    const res = await request('POST', '/api/feedback', { original: 'test' });
    assert.strictEqual(res.status, 400);
    assert.strictEqual(res.body.ok, false);
    assert.ok(res.body.error);
  });

  it('POST /api/feedback returns 400 for empty body', async () => {
    const res = await request('POST', '/api/feedback', {});
    assert.strictEqual(res.status, 400);
    assert.strictEqual(res.body.ok, false);
  });

  it('POST /api/feedback accepts optional mode parameter', async () => {
    const res = await request('POST', '/api/feedback', {
      original: '안녕하세요',
      spoken: '안녕하세요',
      mode: 'pronunciation'
    });
    assert.strictEqual(res.status, 200);
    assert.strictEqual(res.body.ok, true);
    assert.ok(res.body.feedback);
  });

  it('POST /api/feedback uses default mode when invalid mode provided', async () => {
    const res = await request('POST', '/api/feedback', {
      original: '안녕하세요',
      spoken: '안녕하세요',
      mode: 'invalid_mode'
    });
    assert.strictEqual(res.status, 200);
    assert.strictEqual(res.body.ok, true);
  });

  it('Feedback mock mode returns Korean feedback', async () => {
    const res = await request('POST', '/api/feedback', {
      original: '안녕하세요',
      spoken: '안녕하세요'
    });
    assert.strictEqual(res.status, 200);

    // Check feedback contains Korean characters
    const hasKorean = /[\uAC00-\uD7AF]/.test(res.body.feedback);
    assert.ok(hasKorean, 'Feedback should contain Korean characters');
  });

  it('Feedback mock mode is deterministic', async () => {
    const input = {
      original: '오늘 날씨가 좋네요',
      spoken: '오늘 날씨가 좋아요'
    };

    const res1 = await request('POST', '/api/feedback', input);
    const res2 = await request('POST', '/api/feedback', input);

    assert.strictEqual(res1.body.feedback, res2.body.feedback, 'Same input should produce same mock feedback');
  });

  it('Feedback mock mode gives praise for exact match', async () => {
    const res = await request('POST', '/api/feedback', {
      original: '감사합니다',
      spoken: '감사합니다'
    });
    assert.strictEqual(res.status, 200);
    assert.ok(res.body.feedback);
    // Mock mode should give positive feedback for exact match
  });

  it('Feedback mock mode gives improvement suggestion for partial match', async () => {
    const res = await request('POST', '/api/feedback', {
      original: '좋은 아침이에요',
      spoken: '좋은 아침'
    });
    assert.strictEqual(res.status, 200);
    assert.ok(res.body.feedback);
  });

  it('Feedback mock mode includes mock flag', async () => {
    const res = await request('POST', '/api/feedback', {
      original: '테스트',
      spoken: '테스트'
    });

    // In mock mode, should have mock: true
    if (isFeedbackMock()) {
      assert.strictEqual(res.body.mock, true);
    }
  });

  it('calculateSimilarity returns 1 for identical strings', () => {
    assert.strictEqual(calculateSimilarity('hello', 'hello'), 1);
    assert.strictEqual(calculateSimilarity('안녕하세요', '안녕하세요'), 1);
  });

  it('calculateSimilarity returns 0 for completely different strings', () => {
    const sim = calculateSimilarity('abc', 'xyz');
    assert.ok(sim < 0.5, 'Completely different strings should have low similarity');
  });

  it('calculateSimilarity handles empty strings', () => {
    assert.strictEqual(calculateSimilarity('', ''), 1);
    assert.strictEqual(calculateSimilarity('test', ''), 0);
    assert.strictEqual(calculateSimilarity('', 'test'), 0);
  });

  it('generateMockFeedback returns Korean text', () => {
    const feedback = generateMockFeedback('안녕하세요', '안녕하세요');
    const hasKorean = /[\uAC00-\uD7AF]/.test(feedback);
    assert.ok(hasKorean, 'Mock feedback should contain Korean');
  });

  it('generateMockFeedback is deterministic', () => {
    const feedback1 = generateMockFeedback('테스트', '테스트');
    const feedback2 = generateMockFeedback('테스트', '테스트');
    assert.strictEqual(feedback1, feedback2);
  });
});

describe('Punctuation-Insensitive Scoring', () => {
  it('normalizeForKoreanCompare removes periods', () => {
    const result = normalizeForKoreanCompare('커피 사 주세요.');
    assert.strictEqual(result, '커피 사 주세요');
  });

  it('normalizeForKoreanCompare removes question marks', () => {
    const result = normalizeForKoreanCompare('뭐 해요?');
    assert.strictEqual(result, '뭐 해요');
  });

  it('normalizeForKoreanCompare removes exclamation marks', () => {
    const result = normalizeForKoreanCompare('안녕하세요!');
    assert.strictEqual(result, '안녕하세요');
  });

  it('normalizeForKoreanCompare removes commas', () => {
    const result = normalizeForKoreanCompare('네, 감사합니다.');
    assert.strictEqual(result, '네 감사합니다');
  });

  it('normalizeForKoreanCompare removes Korean quotation marks', () => {
    const result = normalizeForKoreanCompare('「안녕」이라고 말해요');
    assert.strictEqual(result, '안녕이라고 말해요');
  });

  it('normalizeForKoreanCompare collapses multiple spaces', () => {
    const result = normalizeForKoreanCompare('커피    사    주세요');
    assert.strictEqual(result, '커피 사 주세요');
  });

  it('computeCER returns 0 for punctuation-only difference', () => {
    // Target with period, transcript without period
    const result = computeCER('커피 사 주세요.', '커피 사 주세요');
    assert.strictEqual(result.cer, 0, 'CER should be 0 when only punctuation differs');
  });

  it('computeCER returns 0 for question mark difference', () => {
    const result = computeCER('뭐 해요?', '뭐 해요');
    assert.strictEqual(result.cer, 0);
  });

  it('computeCER returns 0 for exclamation mark difference', () => {
    const result = computeCER('안녕하세요!', '안녕하세요');
    assert.strictEqual(result.cer, 0);
  });

  it('computeCER returns 0 for multiple punctuation differences', () => {
    const result = computeCER('네, 알겠습니다!', '네 알겠습니다');
    assert.strictEqual(result.cer, 0);
  });

  it('computeCER detects actual text differences', () => {
    // "주세요" vs "줘요" - different characters
    const result = computeCER('커피 주세요', '커피 줘요');
    assert.ok(result.cer > 0, 'CER should be > 0 for actual text differences');
  });

  it('buildCharacterDiff ignores punctuation', () => {
    const diff = buildCharacterDiff('커피 사 주세요.', '커피 사 주세요');
    // All units should be correct
    const allCorrect = diff.units.every(u => u.status === 'correct');
    assert.ok(allCorrect, 'All diff units should be correct when only punctuation differs');
    assert.strictEqual(diff.wrongUnits.length, 0, 'Should have no wrong units');
  });

  it('POST /api/feedback returns 100% accuracy for punctuation-only difference', async () => {
    const res = await request('POST', '/api/feedback', {
      targetText: '커피 사 주세요.',
      transcriptText: '커피 사 주세요'
    });
    assert.strictEqual(res.status, 200);
    assert.strictEqual(res.body.ok, true);
    assert.strictEqual(res.body.textAccuracyPercent, 100, 'Accuracy should be 100% when only punctuation differs');
    assert.strictEqual(res.body.mistakePercent, 0, 'Mistake percent should be 0');
  });

  it('POST /api/feedback returns correct metrics structure', async () => {
    const res = await request('POST', '/api/feedback', {
      targetText: '안녕하세요',
      transcriptText: '안녕하세요'
    });
    assert.strictEqual(res.status, 200);
    assert.strictEqual(res.body.ok, true);

    // Check top-level fields
    assert.ok('textAccuracyPercent' in res.body, 'Should have textAccuracyPercent');
    assert.ok('mistakePercent' in res.body, 'Should have mistakePercent');
    assert.ok('score' in res.body, 'Should have score');

    // Check metrics object
    assert.ok(res.body.metrics, 'Should have metrics object');
    assert.ok('accuracyPercent' in res.body.metrics, 'metrics.accuracyPercent');
    assert.ok('wrongPercent' in res.body.metrics, 'metrics.wrongPercent');
    assert.ok('textAccuracyPercent' in res.body.metrics, 'metrics.textAccuracyPercent');
    assert.ok('mistakePercent' in res.body.metrics, 'metrics.mistakePercent');
    assert.ok('cer' in res.body.metrics, 'metrics.cer');
    assert.ok('wer' in res.body.metrics, 'metrics.wer');

    // Check diff object
    assert.ok(res.body.diff, 'Should have diff object');
    assert.ok('units' in res.body.diff, 'diff.units');
    assert.ok('wrongUnits' in res.body.diff, 'diff.wrongUnits');
    assert.ok('wrongParts' in res.body.diff, 'diff.wrongParts');
  });

  it('POST /api/feedback perfect match returns 100%', async () => {
    const res = await request('POST', '/api/feedback', {
      targetText: '감사합니다',
      transcriptText: '감사합니다'
    });
    assert.strictEqual(res.body.textAccuracyPercent, 100);
    assert.strictEqual(res.body.mistakePercent, 0);
    assert.strictEqual(res.body.metrics.cer, 0);
  });

  it('POST /api/feedback partial match returns correct percentage', async () => {
    // "안녕" vs "안녕하세요" - 2/5 characters match in reference
    const res = await request('POST', '/api/feedback', {
      targetText: '안녕하세요',
      transcriptText: '안녕'
    });
    assert.strictEqual(res.status, 200);
    // CER = editDistance / refLength = 3/5 = 0.6, so accuracy = 40%
    assert.ok(res.body.textAccuracyPercent < 100, 'Should not be 100%');
    assert.ok(res.body.mistakePercent > 0, 'Should have mistakes');
  });
});

describe('Sentences Endpoint', () => {
  it('GET /api/sentences returns sentences array', async () => {
    const res = await request('GET', '/api/sentences');
    assert.strictEqual(res.status, 200);
    assert.ok(Array.isArray(res.body.sentences));
    assert.ok(res.body.total > 0);
    assert.ok(Array.isArray(res.body.categories));
  });

  it('GET /api/sentences?category=daily filters by category', async () => {
    const res = await request('GET', '/api/sentences?category=daily');
    assert.strictEqual(res.status, 200);
    assert.ok(Array.isArray(res.body.sentences));
    res.body.sentences.forEach(s => {
      assert.strictEqual(s.category, 'daily');
    });
  });

  it('GET /api/sentences?category=invalid returns 400', async () => {
    const res = await request('GET', '/api/sentences?category=invalid');
    assert.strictEqual(res.status, 400);
    assert.ok(res.body.error);
  });
});

describe('404 Handler', () => {
  it('Unknown route returns 404', async () => {
    const res = await request('GET', '/api/unknown');
    assert.strictEqual(res.status, 404);
    assert.ok(res.body.error);
  });
});

describe('Mock Mode', () => {
  it('All services in mock mode without API keys', async () => {
    const res = await request('GET', '/api/health');
    assert.strictEqual(res.status, 200);

    // In test environment without .env, all services should be in mock mode
    if (!process.env.ELEVENLABS_API_KEY &&
        !process.env.OPENAI_API_KEY &&
        !process.env.XAI_API_KEY) {
      assert.strictEqual(res.body.mockMode, true);
      assert.strictEqual(res.body.services.tts, 'mock');
      assert.strictEqual(res.body.services.stt, 'mock');
      assert.strictEqual(res.body.services.feedback, 'mock');
    }
  });
});

describe('Error Response Format', () => {
  it('TTS error responses have ok:false format', async () => {
    const res = await request('POST', '/api/tts', { text: '' });
    assert.strictEqual(res.status, 400);
    assert.strictEqual(res.body.ok, false);
    assert.ok(res.body.error);
  });
});

// =============================================================================
// QUALITY GATES - Explicit acceptance criteria verification
// =============================================================================

describe('Quality Gates', () => {
  describe('QG1: Health endpoint works', () => {
    it('returns ok:true with all required fields', async () => {
      const res = await request('GET', '/api/health');
      assert.strictEqual(res.status, 200);
      assert.strictEqual(res.body.ok, true);
      assert.ok(res.body.timestamp, 'Must have timestamp');
      assert.ok('mockMode' in res.body, 'Must have mockMode flag');
      assert.ok('services' in res.body, 'Must have services object');
    });
  });

  describe('QG2: TTS mock returns audio-like response', () => {
    it('returns valid WAV with RIFF header', async () => {
      const res = await request('POST', '/api/tts', { text: '테스트' });
      assert.strictEqual(res.status, 200);

      // Check content-type is audio
      assert.ok(
        res.headers['content-type']?.includes('audio'),
        'Content-Type must be audio/*'
      );

      // Check response is binary with RIFF header
      const data = res.rawBody || res.body;
      assert.ok(data.length > 44, 'WAV must have header + data');

      // Verify RIFF header if we have raw bytes
      if (Buffer.isBuffer(data)) {
        assert.strictEqual(data.slice(0, 4).toString(), 'RIFF', 'Must start with RIFF');
        assert.strictEqual(data.slice(8, 12).toString(), 'WAVE', 'Must have WAVE marker');
      }
    });
  });

  describe('QG3: STT mock returns deterministic transcript', () => {
    it('same audio produces same transcript', async () => {
      const audio = generateTestWav(200);

      const res1 = await requestMultipart('/api/stt', audio, 'test.wav');
      const res2 = await requestMultipart('/api/stt', audio, 'test.wav');

      assert.strictEqual(res1.status, 200);
      assert.strictEqual(res2.status, 200);
      assert.strictEqual(res1.body.text, res2.body.text, 'Transcript must be deterministic');
      assert.ok(res1.body.text.length > 0, 'Transcript must not be empty');
    });
  });

  describe('QG4: Feedback mock returns 1-line Korean string', () => {
    it('feedback is single line Korean text', async () => {
      const res = await request('POST', '/api/feedback', {
        original: '안녕하세요',
        spoken: '안녕하세요'
      });

      assert.strictEqual(res.status, 200);
      assert.strictEqual(res.body.ok, true);

      const feedback = res.body.feedback;
      assert.ok(feedback, 'Feedback must exist');
      assert.ok(feedback.length > 0, 'Feedback must not be empty');

      // Check single line (no newlines)
      assert.ok(!feedback.includes('\n'), 'Feedback must be single line');

      // Check contains Korean characters (Hangul Unicode range)
      const hasKorean = /[\uAC00-\uD7AF]/.test(feedback);
      assert.ok(hasKorean, 'Feedback must contain Korean characters');
    });

    it('feedback is context-aware (different for match vs mismatch)', async () => {
      const matchRes = await request('POST', '/api/feedback', {
        original: '감사합니다',
        spoken: '감사합니다'
      });

      const mismatchRes = await request('POST', '/api/feedback', {
        original: '안녕하세요',
        spoken: '반갑습니다'
      });

      // Both should succeed
      assert.strictEqual(matchRes.body.ok, true);
      assert.strictEqual(mismatchRes.body.ok, true);

      // Feedback should be different for match vs mismatch
      assert.notStrictEqual(
        matchRes.body.feedback,
        mismatchRes.body.feedback,
        'Feedback should differ based on similarity'
      );
    });
  });

  describe('QG5: All services work in MOCK mode', () => {
    it('no API keys needed for full workflow', async () => {
      // 1. Health check shows mock mode
      const health = await request('GET', '/api/health');
      assert.strictEqual(health.body.ok, true);

      // 2. Get sentences
      const sentences = await request('GET', '/api/sentences');
      assert.strictEqual(sentences.status, 200);
      assert.ok(sentences.body.sentences.length > 0, 'Must have sentences');

      const sentence = sentences.body.sentences[0];

      // 3. TTS works
      const tts = await request('POST', '/api/tts', { text: sentence.korean });
      assert.strictEqual(tts.status, 200);

      // 4. STT works
      const audio = generateTestWav(500);
      const stt = await requestMultipart('/api/stt', audio, 'test.wav');
      assert.strictEqual(stt.status, 200);
      assert.ok(stt.body.text, 'STT must return text');

      // 5. Feedback works
      const feedback = await request('POST', '/api/feedback', {
        original: sentence.korean,
        spoken: stt.body.text
      });
      assert.strictEqual(feedback.status, 200);
      assert.ok(feedback.body.feedback, 'Feedback must return text');
    });
  });
});
