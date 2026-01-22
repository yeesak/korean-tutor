/**
 * xAI Realtime WebSocket Client
 *
 * Connects to xAI's realtime API for voice-based pronunciation feedback.
 * Uses WebSocket to send audio and receive text analysis.
 *
 * API: wss://api.x.ai/v1/realtime
 *
 * Flow:
 * 1. Connect to WebSocket with auth
 * 2. Send session.update with instructions
 * 3. Send audio chunks via input_audio_buffer.append
 * 4. Commit and request response
 * 5. Parse JSON from response
 */

const WebSocket = require('ws');

// Configuration
const XAI_REALTIME_URL = 'wss://api.x.ai/v1/realtime';
const DEFAULT_MODEL = 'grok-2-public';
const CONNECTION_TIMEOUT_MS = 10000;
const RESPONSE_TIMEOUT_MS = 30000;
const AUDIO_CHUNK_SIZE = 4096; // bytes per chunk

/**
 * Build Korean pronunciation coach instructions
 * @param {string} targetText - The target sentence
 * @param {string} transcriptText - Optional STT transcript
 * @returns {string} System instructions
 */
function buildPronunciationInstructions(targetText, transcriptText = '') {
  return `You are a Korean pronunciation coach. Your ONLY job is to analyze the user's audio pronunciation.

TARGET SENTENCE: "${targetText}"
${transcriptText ? `STT TRANSCRIPT: "${transcriptText}"` : ''}

CRITICAL RULES:
1. Listen to the audio and compare pronunciation to the target Korean sentence.
2. Focus on: tone, rhythm, consonant/vowel clarity, intonation.
3. Output ONLY valid JSON, no markdown, no extra text.
4. All feedback in Korean language.
5. Do NOT discuss anything except pronunciation differences.

OUTPUT FORMAT (strict JSON):
{
  "weakPronunciation": [
    {"token": "발음이 약한 부분", "reason": "왜 약한지", "tip": "개선 팁"}
  ],
  "strongPronunciation": [
    {"token": "잘한 부분", "reason": "왜 좋은지"}
  ],
  "shortComment": "1-2문장 전체 피드백"
}

If pronunciation is perfect, return:
{
  "weakPronunciation": [],
  "strongPronunciation": [{"token": "전체", "reason": "발음이 정확합니다"}],
  "shortComment": "완벽한 발음입니다! 잘 하셨어요."
}

If audio is unclear or silent:
{
  "weakPronunciation": [],
  "strongPronunciation": [],
  "shortComment": "오디오가 잘 들리지 않습니다. 다시 녹음해 주세요."
}`;
}

/**
 * Convert audio buffer to base64 chunks
 * @param {Buffer} audioBuffer - PCM16 audio data
 * @returns {string[]} Array of base64 chunks
 */
function audioToBase64Chunks(audioBuffer) {
  const chunks = [];
  for (let i = 0; i < audioBuffer.length; i += AUDIO_CHUNK_SIZE) {
    const chunk = audioBuffer.slice(i, i + AUDIO_CHUNK_SIZE);
    chunks.push(chunk.toString('base64'));
  }
  return chunks;
}

/**
 * Parse JSON from xAI response, handling various formats
 * @param {string} text - Raw text from xAI
 * @returns {object|null} Parsed JSON or null
 */
function parseResponseJson(text) {
  if (!text) return null;

  // Try direct parse first
  try {
    return JSON.parse(text);
  } catch (e) {
    // Continue to cleanup attempts
  }

  // Remove markdown code blocks
  let cleaned = text
    .replace(/^```json\s*/i, '')
    .replace(/^```\s*/i, '')
    .replace(/\s*```$/i, '')
    .trim();

  try {
    return JSON.parse(cleaned);
  } catch (e) {
    // Continue
  }

  // Try to extract JSON object from text
  const jsonMatch = text.match(/\{[\s\S]*\}/);
  if (jsonMatch) {
    try {
      return JSON.parse(jsonMatch[0]);
    } catch (e) {
      // Give up
    }
  }

  return null;
}

/**
 * Send audio to xAI Realtime API for pronunciation analysis
 *
 * @param {Buffer} audioBuffer - PCM16 mono 16kHz or 24kHz audio
 * @param {string} targetText - The target sentence to compare against
 * @param {object} options - Optional settings
 * @param {string} options.transcriptText - STT transcript for context
 * @param {string} options.locale - Locale (default: ko-KR)
 * @param {number} options.sampleRate - Audio sample rate (default: 16000)
 * @returns {Promise<object>} Pronunciation feedback result
 */
async function analyzePronunciation(audioBuffer, targetText, options = {}) {
  const {
    transcriptText = '',
    locale = 'ko-KR',
    sampleRate = 16000
  } = options;

  const apiKey = process.env.XAI_API_KEY;
  if (!apiKey) {
    return {
      ok: false,
      error: 'XAI_API_KEY not configured',
      tutor: null
    };
  }

  return new Promise((resolve) => {
    let ws = null;
    let connectionTimeout = null;
    let responseTimeout = null;
    let responseText = '';
    let sessionReady = false;
    let responseReceived = false;

    const cleanup = () => {
      if (connectionTimeout) clearTimeout(connectionTimeout);
      if (responseTimeout) clearTimeout(responseTimeout);
      if (ws && ws.readyState === WebSocket.OPEN) {
        ws.close();
      }
    };

    const fail = (error) => {
      cleanup();
      console.error('[xaiRealtime] Error:', error);
      resolve({
        ok: false,
        error: error,
        tutor: null
      });
    };

    try {
      // Connect to xAI realtime WebSocket
      console.log('[xaiRealtime] Connecting to xAI realtime API...');

      ws = new WebSocket(XAI_REALTIME_URL, {
        headers: {
          'Authorization': `Bearer ${apiKey}`,
          'OpenAI-Beta': 'realtime=v1'
        }
      });

      // Connection timeout
      connectionTimeout = setTimeout(() => {
        fail('Connection timeout');
      }, CONNECTION_TIMEOUT_MS);

      ws.on('open', () => {
        console.log('[xaiRealtime] Connected');
        clearTimeout(connectionTimeout);

        // Send session.update with instructions
        const sessionUpdate = {
          type: 'session.update',
          session: {
            modalities: ['text', 'audio'],
            instructions: buildPronunciationInstructions(targetText, transcriptText),
            input_audio_format: 'pcm16',
            input_audio_transcription: {
              model: 'whisper-1'
            },
            turn_detection: null, // Manual turn management
            temperature: 0.6
          }
        };

        ws.send(JSON.stringify(sessionUpdate));
        console.log('[xaiRealtime] Session update sent');
      });

      ws.on('message', (data) => {
        try {
          const event = JSON.parse(data.toString());

          switch (event.type) {
            case 'session.created':
              console.log('[xaiRealtime] Session created');
              break;

            case 'session.updated':
              console.log('[xaiRealtime] Session updated, sending audio...');
              sessionReady = true;

              // Send audio chunks
              const chunks = audioToBase64Chunks(audioBuffer);
              console.log(`[xaiRealtime] Sending ${chunks.length} audio chunks (${audioBuffer.length} bytes)`);

              for (const chunk of chunks) {
                ws.send(JSON.stringify({
                  type: 'input_audio_buffer.append',
                  audio: chunk
                }));
              }

              // Commit the audio buffer
              ws.send(JSON.stringify({
                type: 'input_audio_buffer.commit'
              }));
              console.log('[xaiRealtime] Audio committed');

              // Request response (text only for pronunciation analysis)
              ws.send(JSON.stringify({
                type: 'response.create',
                response: {
                  modalities: ['text'],
                  instructions: 'Analyze the pronunciation in the audio and output JSON as instructed.'
                }
              }));
              console.log('[xaiRealtime] Response requested');

              // Start response timeout
              responseTimeout = setTimeout(() => {
                fail('Response timeout');
              }, RESPONSE_TIMEOUT_MS);
              break;

            case 'response.text.delta':
              // Accumulate text response
              if (event.delta) {
                responseText += event.delta;
              }
              break;

            case 'response.text.done':
              console.log('[xaiRealtime] Text response complete');
              break;

            case 'response.done':
              console.log('[xaiRealtime] Response done');
              responseReceived = true;
              clearTimeout(responseTimeout);

              // Try to get text from response output
              if (event.response?.output) {
                for (const item of event.response.output) {
                  if (item.type === 'message' && item.content) {
                    for (const content of item.content) {
                      if (content.type === 'text' && content.text) {
                        responseText = content.text;
                      }
                    }
                  }
                }
              }

              console.log('[xaiRealtime] Raw response:', responseText.substring(0, 200));

              // Parse the JSON response
              const parsed = parseResponseJson(responseText);

              if (parsed) {
                cleanup();
                resolve({
                  ok: true,
                  tutor: {
                    weakPronunciation: parsed.weakPronunciation || [],
                    strongPronunciation: parsed.strongPronunciation || [],
                    shortComment: parsed.shortComment || ''
                  },
                  rawText: responseText
                });
              } else {
                cleanup();
                resolve({
                  ok: false,
                  error: 'Failed to parse pronunciation feedback JSON',
                  rawText: responseText,
                  tutor: null
                });
              }
              break;

            case 'error':
              console.error('[xaiRealtime] API error:', event.error);
              fail(event.error?.message || 'Unknown API error');
              break;

            case 'rate_limit':
              fail('Rate limit exceeded');
              break;

            default:
              // Ignore other events (input_audio_buffer.committed, etc.)
              break;
          }
        } catch (parseErr) {
          console.error('[xaiRealtime] Message parse error:', parseErr);
        }
      });

      ws.on('error', (err) => {
        console.error('[xaiRealtime] WebSocket error:', err.message);
        fail(err.message);
      });

      ws.on('close', (code, reason) => {
        console.log(`[xaiRealtime] Connection closed: ${code} ${reason}`);
        if (!responseReceived) {
          fail(`Connection closed unexpectedly: ${code}`);
        }
      });

    } catch (err) {
      fail(err.message);
    }
  });
}

/**
 * Check if xAI Realtime is configured
 * @returns {boolean}
 */
function isXAIRealtimeConfigured() {
  return !!process.env.XAI_API_KEY;
}

module.exports = {
  analyzePronunciation,
  isXAIRealtimeConfigured,
  parseResponseJson,
  buildPronunciationInstructions
};
