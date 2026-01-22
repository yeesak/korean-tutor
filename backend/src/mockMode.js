/**
 * Mode Configuration - REAL mode by default
 *
 * Environment Variables:
 * - MODE: "REAL" (default) or "MOCK"
 * - ELEVENLABS_API_KEY: Required for TTS and STT
 * - XAI_API_KEY: Required for LLM feedback (Grok)
 *
 * In REAL mode, missing keys return HTTP 503 errors.
 * In MOCK mode (explicit), fallbacks are used.
 */

/**
 * Get current mode from env (default: REAL)
 */
function getMode() {
  const mode = (process.env.MODE || 'REAL').toUpperCase();
  return mode === 'MOCK' ? 'mock' : 'real';
}

/**
 * Check if running in REAL mode
 */
function isRealMode() {
  return getMode() === 'real';
}

/**
 * Check if ElevenLabs is configured (TTS + STT)
 */
function isElevenLabsConfigured() {
  return !!process.env.ELEVENLABS_API_KEY;
}

/**
 * Check if xAI is configured (LLM - Grok)
 */
function isXAIConfigured() {
  return !!process.env.XAI_API_KEY;
}

/**
 * Get configuration status for /api/health
 */
function getConfigStatus() {
  return {
    mode: getMode(),
    ttsConfigured: isElevenLabsConfigured(),
    sttConfigured: isElevenLabsConfigured(), // Same key for ElevenLabs
    llmConfigured: isXAIConfigured(),
    ttsProvider: 'elevenlabs',
    sttProvider: 'elevenlabs',
    llmProvider: 'xai'
  };
}

/**
 * Get list of missing API keys
 */
function getMissingKeys() {
  const missing = [];
  if (!isElevenLabsConfigured()) missing.push('ELEVENLABS_API_KEY');
  if (!isXAIConfigured()) missing.push('XAI_API_KEY');
  return missing;
}

/**
 * Log startup configuration status
 */
function logStartupStatus() {
  const mode = getMode();
  const status = getConfigStatus();
  const missing = getMissingKeys();

  console.log('');
  console.log('='.repeat(50));
  console.log(`   MODE: ${mode.toUpperCase()}`);
  console.log('='.repeat(50));
  console.log('');
  console.log('   Service Status:');
  console.log(`   - TTS (ElevenLabs):      ${status.ttsConfigured ? '✓ CONFIGURED' : '✗ MISSING KEY'}`);
  console.log(`   - STT (ElevenLabs):      ${status.sttConfigured ? '✓ CONFIGURED' : '✗ MISSING KEY'}`);
  console.log(`   - LLM (xAI Grok):        ${status.llmConfigured ? '✓ CONFIGURED' : '✗ MISSING KEY'}`);

  if (missing.length > 0 && mode === 'real') {
    console.log('');
    console.log('   ⚠️  WARNING: Some services not configured!');
    console.log('   Missing:');
    missing.forEach(key => console.log(`   - ${key}`));
    console.log('');
    console.log('   Services without keys will return HTTP 503 errors.');
  }
  console.log('');
  console.log('='.repeat(50));
  console.log('');
}

/**
 * Middleware to check if service is available
 * Returns 503 if in REAL mode and key is missing
 */
function requireElevenLabs(req, res, next) {
  if (isRealMode() && !isElevenLabsConfigured()) {
    return res.status(503).json({
      ok: false,
      error: 'Service unavailable',
      details: 'ELEVENLABS_API_KEY not configured'
    });
  }
  next();
}

function requireXAI(req, res, next) {
  if (isRealMode() && !isXAIConfigured()) {
    return res.status(503).json({
      ok: false,
      error: 'Service unavailable',
      details: 'XAI_API_KEY not configured'
    });
  }
  next();
}

module.exports = {
  getMode,
  isRealMode,
  isElevenLabsConfigured,
  isXAIConfigured,
  getConfigStatus,
  getMissingKeys,
  logStartupStatus,
  requireElevenLabs,
  requireXAI
};
