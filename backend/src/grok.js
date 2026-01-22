/**
 * Grok Handler - Dynamic Tutor Line Generation
 *
 * Input: { model, messages: [{role, content}], temperature?, max_tokens? }
 * Output: Grok API response or error
 *
 * This endpoint proxies requests to xAI Grok for generating context-aware
 * Korean tutor speech. Unity client sends system + user prompts, this
 * forwards to Grok and returns the generated tutor line.
 */

const { isXAIConfigured } = require('./mockMode');

// Default xAI model
const DEFAULT_XAI_MODEL = 'grok-3-mini';

// API timeout in milliseconds (12s for mobile reliability)
const API_TIMEOUT_MS = 12000;

/**
 * POST /api/grok handler
 *
 * Proxies chat completion requests to xAI Grok API.
 * Used by Unity GrokClient for dynamic tutor line generation.
 */
async function grokHandler(req, res) {
  try {
    // Check if xAI is configured
    if (!isXAIConfigured()) {
      console.warn('[Grok] XAI_API_KEY not configured');
      return res.status(503).json({
        ok: false,
        error: 'XAI_API_KEY not configured',
        errorCode: 'NOT_CONFIGURED'
      });
    }

    const { model, messages, temperature, max_tokens } = req.body;

    // Validate required fields
    if (!messages || !Array.isArray(messages) || messages.length === 0) {
      return res.status(400).json({
        ok: false,
        error: 'Missing or invalid "messages" array'
      });
    }

    // Use provided model or default
    const xaiModel = model || process.env.XAI_MODEL || DEFAULT_XAI_MODEL;

    console.log(`[Grok] Calling xAI (${xaiModel}) with ${messages.length} messages...`);

    const fetch = (await import('node-fetch')).default;

    // Create abort controller for timeout
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), API_TIMEOUT_MS);

    let response;
    try {
      response = await fetch('https://api.x.ai/v1/chat/completions', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Authorization': `Bearer ${process.env.XAI_API_KEY}`
        },
        body: JSON.stringify({
          model: xaiModel,
          messages,
          temperature: temperature ?? 0.7,
          max_tokens: max_tokens ?? 150
        }),
        signal: controller.signal
      });
      clearTimeout(timeoutId);
    } catch (err) {
      clearTimeout(timeoutId);
      if (err.name === 'AbortError') {
        console.error('[Grok] Request timed out');
        return res.status(504).json({
          ok: false,
          error: 'Request timed out',
          errorCode: 'TIMEOUT'
        });
      }
      throw err;
    }

    // Handle HTTP errors
    if (!response.ok) {
      const errorText = await response.text().catch(() => 'Unknown error');
      console.error(`[Grok] xAI API error ${response.status}: ${errorText.substring(0, 200)}`);
      return res.status(response.status).json({
        ok: false,
        error: `xAI API error: ${response.status}`,
        errorCode: `HTTP_${response.status}`,
        details: errorText.substring(0, 200)
      });
    }

    // Parse and forward response
    const result = await response.json();

    const content = result.choices?.[0]?.message?.content?.trim() || '';
    console.log(`[Grok] Response: ${content?.substring(0, 50)}...`);

    // Return response with convenient 'text' field for Unity + full Grok response
    res.json({
      ...result,
      text: content,  // Convenience field for Unity
      ok: true
    });

  } catch (err) {
    console.error('[Grok] Exception:', err.message);
    res.status(500).json({
      ok: false,
      error: 'Grok request failed',
      errorCode: 'EXCEPTION',
      details: err.message
    });
  }
}

module.exports = {
  grokHandler
};
