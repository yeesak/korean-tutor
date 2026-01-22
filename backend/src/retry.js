/**
 * Retry utility with exponential backoff
 * For upstream API calls (ElevenLabs, OpenAI, xAI)
 */

// Default retry configuration
const DEFAULT_OPTIONS = {
  maxRetries: 3,
  initialDelayMs: 1000,
  maxDelayMs: 10000,
  backoffMultiplier: 2,
  retryableStatusCodes: [429, 500, 502, 503, 504],
  retryableErrors: ['ECONNRESET', 'ETIMEDOUT', 'ENOTFOUND', 'EAI_AGAIN']
};

/**
 * Sleep for specified milliseconds
 * @param {number} ms - Milliseconds to sleep
 */
function sleep(ms) {
  return new Promise(resolve => setTimeout(resolve, ms));
}

/**
 * Check if error is retryable
 * @param {Error|Response} errorOrResponse - Error or fetch response
 * @param {Object} options - Retry options
 */
function isRetryable(errorOrResponse, options) {
  // Check if it's a fetch Response object with status
  if (errorOrResponse?.status) {
    return options.retryableStatusCodes.includes(errorOrResponse.status);
  }

  // Check if it's an Error with code
  if (errorOrResponse?.code) {
    return options.retryableErrors.includes(errorOrResponse.code);
  }

  // Check for AbortError (timeout) - retryable
  if (errorOrResponse?.name === 'AbortError') {
    return true;
  }

  // Network errors are generally retryable
  if (errorOrResponse?.message) {
    const msg = errorOrResponse.message.toLowerCase();
    return msg.includes('network') ||
           msg.includes('timeout') ||
           msg.includes('socket') ||
           msg.includes('econnrefused');
  }

  return false;
}

/**
 * Calculate delay with jitter
 * @param {number} attempt - Current attempt (0-indexed)
 * @param {Object} options - Retry options
 */
function calculateDelay(attempt, options) {
  const baseDelay = options.initialDelayMs * Math.pow(options.backoffMultiplier, attempt);
  const delay = Math.min(baseDelay, options.maxDelayMs);
  // Add jitter (up to 20%)
  const jitter = delay * 0.2 * Math.random();
  return Math.floor(delay + jitter);
}

/**
 * Retry a fetch call with exponential backoff
 * @param {Function} fetchFn - Async function that performs fetch and returns Response
 * @param {Object} opts - Override default options
 * @returns {Promise<Response>} - Successful response
 * @throws {Error} - If all retries exhausted
 */
async function retryFetch(fetchFn, opts = {}) {
  const options = { ...DEFAULT_OPTIONS, ...opts };
  let lastError;
  let lastResponse;

  for (let attempt = 0; attempt <= options.maxRetries; attempt++) {
    try {
      const response = await fetchFn();

      // If response is ok (2xx), return it
      if (response.ok) {
        if (attempt > 0) {
          console.log(`[Retry] Succeeded on attempt ${attempt + 1}`);
        }
        return response;
      }

      // Check if status is retryable
      if (isRetryable(response, options) && attempt < options.maxRetries) {
        lastResponse = response;
        const delay = calculateDelay(attempt, options);

        // Check for Retry-After header
        const retryAfter = response.headers?.get?.('retry-after');
        const actualDelay = retryAfter ? Math.min(parseInt(retryAfter) * 1000, options.maxDelayMs) : delay;

        console.log(`[Retry] Attempt ${attempt + 1} failed (${response.status}), retrying in ${actualDelay}ms...`);
        await sleep(actualDelay);
        continue;
      }

      // Non-retryable error status
      return response;

    } catch (error) {
      lastError = error;

      // Check if error is retryable
      if (isRetryable(error, options) && attempt < options.maxRetries) {
        const delay = calculateDelay(attempt, options);
        console.log(`[Retry] Attempt ${attempt + 1} error (${error.message || error.code}), retrying in ${delay}ms...`);
        await sleep(delay);
        continue;
      }

      // Non-retryable error or max retries reached
      throw error;
    }
  }

  // Max retries exhausted
  if (lastError) {
    throw lastError;
  }

  // Return last response if we have one
  if (lastResponse) {
    return lastResponse;
  }

  throw new Error('Max retries exhausted');
}

/**
 * Create a fetch wrapper with retry
 * @param {Function} fetchImpl - The fetch implementation to use
 * @param {Object} opts - Retry options
 */
function createRetryableFetch(fetchImpl, opts = {}) {
  const options = { ...DEFAULT_OPTIONS, ...opts };

  return async (url, fetchOptions = {}) => {
    return retryFetch(
      () => fetchImpl(url, fetchOptions),
      options
    );
  };
}

module.exports = {
  retryFetch,
  createRetryableFetch,
  isRetryable,
  calculateDelay,
  sleep,
  DEFAULT_OPTIONS
};
