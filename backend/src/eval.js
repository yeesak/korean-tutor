/**
 * Combined Evaluation Handler
 *
 * POST /api/eval
 * Single endpoint that performs:
 * A) ElevenLabs STT -> transcriptText
 * B) Punctuation-insensitive text scoring (accuracy/mistake)
 * C) xAI Realtime pronunciation feedback (optional, graceful fallback)
 * D) xAI Text model grammar corrections
 *
 * Input (multipart/form-data):
 *   - targetText: string (required)
 *   - locale: string (default: ko-KR)
 *   - audio: file (WAV format)
 *
 * Output:
 *   {
 *     ok: boolean,
 *     transcriptText: string,
 *     textAccuracyPercent: number,
 *     mistakePercent: number,
 *     diff: { units, wrongUnits, wrongParts },
 *     pronunciation: {
 *       available: boolean,
 *       weakPronunciation: [{token, reason, tip}],
 *       strongPronunciation: [{token, reason}],
 *       shortComment: string
 *     },
 *     grammar: {
 *       mistakes: [{youSaid, correct, why}],
 *       tutorComment: string
 *     },
 *     error?: string
 *   }
 */

const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { retryFetch } = require('./retry');
const { isElevenLabsConfigured, isXAIConfigured } = require('./mockMode');
const { analyzePronunciation, isXAIRealtimeConfigured } = require('./xaiRealtimeClient');
const { normalizeForKoreanCompare, computeCER, buildCharacterDiff } = require('./feedback');
const { validateAndConvertAudio } = require('./pronounce');

// API timeout in milliseconds
const STT_TIMEOUT_MS = 60000;
const GRAMMAR_TIMEOUT_MS = 30000;

// Maximum file size: 25MB
const MAX_FILE_SIZE = 25 * 1024 * 1024;

/**
 * Sanitize transcript text for UI display
 */
function sanitizeTranscript(text) {
  if (!text || typeof text !== 'string') return '';

  return text
    .replace(/\[[^\]]*\]/g, '')
    .replace(/\((?:music|noise|background|applause|laughter|silence|inaudible|unclear|crosstalk|foreign|speaking\s+\w+)[^)]*\)/gi, '')
    .replace(/<[^>]*>/g, '')
    .replace(/\*[^*]+\*/g, '')
    .replace(/\s+/g, ' ')
    .trim();
}

/**
 * Call ElevenLabs STT API directly
 * @param {Buffer} audioBuffer - WAV audio buffer
 * @returns {Promise<object>} STT result
 */
async function callElevenLabsSTT(audioBuffer) {
  if (!isElevenLabsConfigured()) {
    return {
      ok: false,
      error: 'ELEVENLABS_API_KEY not configured'
    };
  }

  const fetch = (await import('node-fetch')).default;
  const { FormData, Blob } = await import('node-fetch');

  const response = await retryFetch(async () => {
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), STT_TIMEOUT_MS);

    try {
      const formData = new FormData();
      const audioBlob = new Blob([audioBuffer], { type: 'audio/wav' });
      formData.append('file', audioBlob, 'audio.wav');
      formData.append('model_id', 'scribe_v1');

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
    return {
      ok: false,
      error: `ElevenLabs STT error (${response.status}): ${errorText}`
    };
  }

  const apiResult = await response.json();
  const rawText = apiResult.text || '';
  const cleanText = sanitizeTranscript(rawText);

  return {
    ok: true,
    text: cleanText,
    rawText: rawText,
    language: apiResult.language_code || 'ko',
    confidence: apiResult.language_probability || null
  };
}

/**
 * Call xAI Grok for grammar corrections (text-based, not voice)
 * @param {string} targetText
 * @param {string} transcriptText
 * @returns {Promise<object>}
 */
async function callGrokForGrammar(targetText, transcriptText) {
  if (!isXAIConfigured()) {
    return {
      ok: false,
      error: 'XAI_API_KEY not configured'
    };
  }

  const model = process.env.XAI_MODEL || 'grok-3';
  const fetch = (await import('node-fetch')).default;

  const systemPrompt = `You are a Korean language tutor analyzing a student's spoken text.

CRITICAL RULES:
1. Compare TARGET text with what the STUDENT said (transcript)
2. Find grammar/vocabulary mistakes
3. All feedback in Korean
4. Output ONLY valid JSON, no markdown

OUTPUT FORMAT:
{
  "mistakes": [
    {
      "youSaid": "학생이 말한 부분",
      "correct": "올바른 표현",
      "why": "간단한 설명 (max 50자)"
    }
  ],
  "tutorComment": "격려하는 피드백 1-2문장"
}

If perfect match, return:
{
  "mistakes": [],
  "tutorComment": "완벽해요! 정확하게 말했습니다."
}`;

  const userPrompt = `TARGET: "${targetText}"
STUDENT SAID: "${transcriptText}"

Find grammar/vocabulary differences. Output JSON only.`;

  try {
    const response = await retryFetch(async () => {
      const controller = new AbortController();
      const timeoutId = setTimeout(() => controller.abort(), GRAMMAR_TIMEOUT_MS);

      try {
        const res = await fetch('https://api.x.ai/v1/chat/completions', {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            'Authorization': `Bearer ${process.env.XAI_API_KEY}`
          },
          body: JSON.stringify({
            model,
            messages: [
              { role: 'system', content: systemPrompt },
              { role: 'user', content: userPrompt }
            ],
            max_tokens: 500,
            temperature: 0.5
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

    if (!response.ok) {
      const errorText = await response.text();
      return {
        ok: false,
        error: `xAI API error (${response.status}): ${errorText}`
      };
    }

    const result = await response.json();
    const content = result.choices?.[0]?.message?.content?.trim();

    if (!content) {
      return {
        ok: false,
        error: 'Empty response from xAI'
      };
    }

    // Parse JSON
    let cleaned = content
      .replace(/^```json\s*/i, '')
      .replace(/^```\s*/i, '')
      .replace(/\s*```$/i, '')
      .trim();

    try {
      const parsed = JSON.parse(cleaned);
      return {
        ok: true,
        mistakes: parsed.mistakes || [],
        tutorComment: parsed.tutorComment || ''
      };
    } catch (parseErr) {
      // Try to extract JSON
      const jsonMatch = content.match(/\{[\s\S]*\}/);
      if (jsonMatch) {
        try {
          const parsed = JSON.parse(jsonMatch[0]);
          return {
            ok: true,
            mistakes: parsed.mistakes || [],
            tutorComment: parsed.tutorComment || ''
          };
        } catch (e) {
          // Fall through
        }
      }
      return {
        ok: false,
        error: 'Failed to parse grammar response',
        rawText: content
      };
    }

  } catch (err) {
    return {
      ok: false,
      error: err.message
    };
  }
}

/**
 * POST /api/eval handler
 */
async function evalHandler(req, res) {
  try {
    // Get form fields
    const targetText = req.body.targetText;
    const locale = req.body.locale || 'ko-KR';

    // Validate targetText
    if (!targetText || typeof targetText !== 'string') {
      return res.status(400).json({
        ok: false,
        error: 'Missing required field: targetText'
      });
    }

    // Validate audio file
    if (!req.file) {
      return res.status(400).json({
        ok: false,
        error: 'Missing audio file. Please upload a WAV file as "audio".'
      });
    }

    const audioBuffer = req.file.buffer;
    console.log(`[Eval] Received: targetText="${targetText.substring(0, 30)}...", audio=${audioBuffer.length} bytes`);

    // Validate file size
    if (audioBuffer.length > MAX_FILE_SIZE) {
      return res.status(400).json({
        ok: false,
        error: `File too large. Maximum: ${MAX_FILE_SIZE / (1024 * 1024)}MB`
      });
    }

    // =========================================================
    // A) STT: ElevenLabs speech-to-text
    // =========================================================
    console.log('[Eval] Step A: STT...');
    const sttResult = await callElevenLabsSTT(audioBuffer);

    if (!sttResult.ok) {
      return res.status(503).json({
        ok: false,
        error: 'STT failed',
        details: sttResult.error
      });
    }

    const transcriptText = sttResult.text;
    console.log(`[Eval] STT result: "${transcriptText.substring(0, 50)}..."`);

    // =========================================================
    // B) Text scoring: punctuation-insensitive CER
    // =========================================================
    console.log('[Eval] Step B: Text scoring...');
    const { cer } = computeCER(targetText, transcriptText);
    const wrongPercent = Math.round(cer * 100);
    const accuracyPercent = Math.max(0, 100 - wrongPercent);
    const diff = buildCharacterDiff(targetText, transcriptText);

    console.log(`[Eval] Score: accuracy=${accuracyPercent}%, cer=${cer}`);

    // =========================================================
    // C) Pronunciation: xAI Realtime (optional, graceful fallback)
    // =========================================================
    console.log('[Eval] Step C: Pronunciation analysis...');
    let pronunciationResult = {
      available: false,
      weakPronunciation: [],
      strongPronunciation: [],
      shortComment: ''
    };

    if (isXAIRealtimeConfigured()) {
      try {
        // Convert audio for xAI
        const audioConversion = validateAndConvertAudio(audioBuffer);

        if (audioConversion.ok) {
          const pronResult = await analyzePronunciation(
            audioConversion.pcmBuffer,
            targetText,
            { transcriptText, locale, sampleRate: audioConversion.sampleRate }
          );

          if (pronResult.ok && pronResult.tutor) {
            pronunciationResult = {
              available: true,
              weakPronunciation: pronResult.tutor.weakPronunciation || [],
              strongPronunciation: pronResult.tutor.strongPronunciation || [],
              shortComment: pronResult.tutor.shortComment || ''
            };
            console.log(`[Eval] Pronunciation: weak=${pronunciationResult.weakPronunciation.length}, strong=${pronunciationResult.strongPronunciation.length}`);
          } else {
            console.log(`[Eval] Pronunciation unavailable: ${pronResult.error || 'unknown'}`);
          }
        } else {
          console.log(`[Eval] Audio conversion failed: ${audioConversion.error}`);
        }
      } catch (pronErr) {
        console.log(`[Eval] Pronunciation error (continuing): ${pronErr.message}`);
      }
    } else {
      console.log('[Eval] xAI Realtime not configured, skipping pronunciation');
    }

    // =========================================================
    // D) Grammar: xAI Text model
    // =========================================================
    console.log('[Eval] Step D: Grammar analysis...');
    let grammarResult = {
      mistakes: [],
      tutorComment: ''
    };

    if (isXAIConfigured()) {
      try {
        const gramResult = await callGrokForGrammar(targetText, transcriptText);

        if (gramResult.ok) {
          grammarResult = {
            mistakes: gramResult.mistakes || [],
            tutorComment: gramResult.tutorComment || ''
          };
          console.log(`[Eval] Grammar: ${grammarResult.mistakes.length} mistakes`);
        } else {
          console.log(`[Eval] Grammar unavailable: ${gramResult.error}`);
        }
      } catch (gramErr) {
        console.log(`[Eval] Grammar error (continuing): ${gramErr.message}`);
      }
    } else {
      console.log('[Eval] xAI not configured, skipping grammar');
    }

    // =========================================================
    // Build and return response
    // =========================================================
    const response = {
      ok: true,
      targetText,
      transcriptText,
      rawTranscriptText: sttResult.rawText,

      // Top-level scores
      textAccuracyPercent: accuracyPercent,
      mistakePercent: wrongPercent,
      score: accuracyPercent,

      // Detailed metrics
      metrics: {
        accuracyPercent,
        wrongPercent,
        textAccuracyPercent: accuracyPercent,
        mistakePercent: wrongPercent,
        cer: Math.round(cer * 1000) / 1000
      },

      // Diff for highlighting
      diff: {
        units: diff.units,
        wrongUnits: diff.wrongUnits,
        wrongParts: diff.wrongUnits
      },

      // Pronunciation (from xAI Realtime)
      pronunciation: pronunciationResult,

      // Grammar (from xAI Text)
      grammar: grammarResult
    };

    console.log(`[Eval] Complete: accuracy=${accuracyPercent}%, pron=${pronunciationResult.available ? 'yes' : 'no'}, grammar=${grammarResult.mistakes.length} mistakes`);

    res.json(response);

  } catch (err) {
    console.error('[Eval] Error:', err.message);
    res.status(500).json({
      ok: false,
      error: 'Evaluation failed',
      details: err.message
    });
  }
}

module.exports = {
  evalHandler,
  callElevenLabsSTT,
  callGrokForGrammar
};
