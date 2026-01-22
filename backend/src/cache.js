/**
 * Disk-based caching for TTS and STT results
 * Minimizes paid API calls by caching responses
 */

const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const CACHE_DIR = path.join(__dirname, '..', 'cache');

// Ensure cache directory exists
if (!fs.existsSync(CACHE_DIR)) {
  fs.mkdirSync(CACHE_DIR, { recursive: true });
}

/**
 * Generate a hash key for cache lookup
 */
function generateCacheKey(prefix, data) {
  const hash = crypto.createHash('md5').update(JSON.stringify(data)).digest('hex');
  return `${prefix}_${hash}`;
}

/**
 * Get cached item if it exists and is not expired
 * @param {string} key - Cache key
 * @param {number} ttlSeconds - Time to live in seconds
 * @returns {Buffer|string|null} - Cached data or null
 */
function getCache(key, ttlSeconds = 86400) {
  const metaPath = path.join(CACHE_DIR, `${key}.meta.json`);
  const dataPath = path.join(CACHE_DIR, `${key}.data`);

  if (!fs.existsSync(metaPath) || !fs.existsSync(dataPath)) {
    return null;
  }

  try {
    const meta = JSON.parse(fs.readFileSync(metaPath, 'utf8'));
    const now = Date.now();
    const age = (now - meta.timestamp) / 1000;

    if (age > ttlSeconds) {
      // Cache expired, clean up
      fs.unlinkSync(metaPath);
      fs.unlinkSync(dataPath);
      return null;
    }

    // Return cached data
    if (meta.type === 'binary') {
      return fs.readFileSync(dataPath);
    } else {
      return fs.readFileSync(dataPath, 'utf8');
    }
  } catch (err) {
    console.error('Cache read error:', err.message);
    return null;
  }
}

/**
 * Store item in cache
 * @param {string} key - Cache key
 * @param {Buffer|string} data - Data to cache
 * @param {string} type - 'binary' or 'text'
 */
function setCache(key, data, type = 'text') {
  const metaPath = path.join(CACHE_DIR, `${key}.meta.json`);
  const dataPath = path.join(CACHE_DIR, `${key}.data`);

  try {
    const meta = {
      timestamp: Date.now(),
      type: type,
      size: Buffer.isBuffer(data) ? data.length : Buffer.byteLength(data)
    };

    fs.writeFileSync(metaPath, JSON.stringify(meta));

    if (type === 'binary') {
      fs.writeFileSync(dataPath, data);
    } else {
      fs.writeFileSync(dataPath, data, 'utf8');
    }
  } catch (err) {
    console.error('Cache write error:', err.message);
  }
}

/**
 * Clear all cache or specific prefix
 */
function clearCache(prefix = null) {
  try {
    const files = fs.readdirSync(CACHE_DIR);
    for (const file of files) {
      if (!prefix || file.startsWith(prefix)) {
        fs.unlinkSync(path.join(CACHE_DIR, file));
      }
    }
  } catch (err) {
    console.error('Cache clear error:', err.message);
  }
}

/**
 * Get cache statistics
 */
function getCacheStats() {
  try {
    const files = fs.readdirSync(CACHE_DIR);
    const metaFiles = files.filter(f => f.endsWith('.meta.json'));

    let totalSize = 0;
    let ttsCount = 0;
    let sttCount = 0;

    for (const file of files) {
      if (file.endsWith('.data')) {
        const stat = fs.statSync(path.join(CACHE_DIR, file));
        totalSize += stat.size;
      }
      if (file.startsWith('tts_')) ttsCount++;
      if (file.startsWith('stt_')) sttCount++;
    }

    return {
      totalEntries: metaFiles.length,
      totalSizeBytes: totalSize,
      totalSizeMB: (totalSize / (1024 * 1024)).toFixed(2),
      ttsEntries: ttsCount / 2, // divide by 2 for meta+data
      sttEntries: sttCount / 2
    };
  } catch (err) {
    return { error: err.message };
  }
}

module.exports = {
  generateCacheKey,
  getCache,
  setCache,
  clearCache,
  getCacheStats
};
