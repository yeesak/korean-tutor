/**
 * Simple in-memory rate limiter
 * Per-IP, configurable requests per minute
 */

// Store: { ip: { count: number, resetTime: number } }
const ipStore = new Map();

// Default: 60 requests per minute
const DEFAULT_LIMIT = 60;
const WINDOW_MS = 60 * 1000; // 1 minute

/**
 * Clean up expired entries (called periodically)
 */
function cleanupExpired() {
  const now = Date.now();
  for (const [ip, data] of ipStore.entries()) {
    if (now > data.resetTime) {
      ipStore.delete(ip);
    }
  }
}

// Cleanup every 5 minutes
setInterval(cleanupExpired, 5 * 60 * 1000);

/**
 * Rate limit middleware factory
 * @param {number} limit - Max requests per window (default: 60)
 * @returns {Function} Express middleware
 */
function createRateLimiter(limit = DEFAULT_LIMIT) {
  return (req, res, next) => {
    // Get client IP (support proxies)
    const ip = req.ip ||
               req.headers['x-forwarded-for']?.split(',')[0]?.trim() ||
               req.connection?.remoteAddress ||
               'unknown';

    const now = Date.now();
    let data = ipStore.get(ip);

    // Initialize or reset if window expired
    if (!data || now > data.resetTime) {
      data = {
        count: 0,
        resetTime: now + WINDOW_MS
      };
      ipStore.set(ip, data);
    }

    // Increment count
    data.count++;

    // Set rate limit headers
    const remaining = Math.max(0, limit - data.count);
    const resetSeconds = Math.ceil((data.resetTime - now) / 1000);

    res.set('X-RateLimit-Limit', String(limit));
    res.set('X-RateLimit-Remaining', String(remaining));
    res.set('X-RateLimit-Reset', String(resetSeconds));

    // Check if over limit
    if (data.count > limit) {
      console.log(`[RateLimit] Blocked: ${ip} (${data.count}/${limit} requests)`);
      return res.status(429).json({
        ok: false,
        error: 'Too many requests',
        details: `Rate limit exceeded. Try again in ${resetSeconds} seconds.`,
        retryAfter: resetSeconds
      });
    }

    next();
  };
}

/**
 * Get current rate limit stats (for debugging)
 */
function getStats() {
  const stats = [];
  for (const [ip, data] of ipStore.entries()) {
    stats.push({
      ip: ip.substring(0, 10) + '...', // Truncate for privacy
      count: data.count,
      resetsIn: Math.ceil((data.resetTime - Date.now()) / 1000)
    });
  }
  return stats;
}

/**
 * Clear all rate limit data (for testing)
 */
function clearAll() {
  ipStore.clear();
}

module.exports = {
  createRateLimiter,
  getStats,
  clearAll,
  DEFAULT_LIMIT,
  WINDOW_MS
};
