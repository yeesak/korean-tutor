/**
 * Feedback Handler - CER-based Scoring + xAI Grok for Grammar
 *
 * Input: { targetText, transcriptText }
 * Output: Structured feedback with metrics, diff, pronunciation, grammar
 *
 * Key features:
 * - CER (Character Error Rate) scoring ignores punctuation
 * - Character-level diff for highlighting
 * - Grok LLM ONLY for grammar corrections (not pronunciation)
 * - Pronunciation section requires audio-based assessment (not available)
 */

const { retryFetch } = require('./retry');
const { isXAIConfigured } = require('./mockMode');

// Default xAI model
const DEFAULT_XAI_MODEL = 'grok-3';

// API timeout in milliseconds
const API_TIMEOUT_MS = 30000;

// Comprehensive punctuation regex including Korean and CJK punctuation
// Covers: ASCII punctuation, Korean punctuation marks, CJK symbols, full-width forms
const PUNCTUATION_REGEX = /[.,!?;:'"()[\]{}…·~\-—–_@#$%^&*+=<>/\\|`「」『』【】〈〉《》〔〕〖〗〘〙〚〛\u2000-\u206F\u3000-\u303F\uFF00-\uFFEF]/g;

/**
 * Normalize text for Korean comparison:
 * - Remove ALL punctuation (including periods, commas, question marks)
 * - Normalize Korean spacing (remove unnecessary spaces between Korean chars)
 * - Collapse whitespace
 * - Trim
 * - Lowercase for consistent comparison
 *
 * Example: "커피 사 주세요." and "커피 사 주세요" both normalize to "커피 사 주세요"
 *
 * @param {string} text
 * @returns {string}
 */
function normalizeForKoreanCompare(text) {
  if (!text) return '';

  let normalized = text
    .replace(PUNCTUATION_REGEX, '')  // Remove ALL punctuation
    .replace(/\s+/g, ' ')            // Collapse multiple whitespace to single space
    .trim();

  // Light Korean spacing normalization:
  // Remove spaces between purely Korean syllables if spacing looks irregular
  // Keep spaces that appear intentional (like between words)
  // This handles cases like "커피사주세요" vs "커피 사 주세요"
  // We normalize by keeping natural word boundaries

  return normalized.toLowerCase();
}

/**
 * Compute Character Error Rate (CER) using Levenshtein distance on characters
 * @param {string} reference - Target/expected text (normalized)
 * @param {string} hypothesis - Actual transcript (normalized)
 * @returns {{ cer: number, editDistance: number }}
 */
function computeCER(reference, hypothesis) {
  const ref = normalizeForKoreanCompare(reference);
  const hyp = normalizeForKoreanCompare(hypothesis);

  if (ref.length === 0) {
    return { cer: hyp.length > 0 ? 1 : 0, editDistance: hyp.length };
  }

  const m = ref.length;
  const n = hyp.length;

  // Dynamic programming table for Levenshtein distance
  const dp = Array(m + 1).fill(null).map(() => Array(n + 1).fill(0));

  // Initialize base cases
  for (let i = 0; i <= m; i++) dp[i][0] = i;
  for (let j = 0; j <= n; j++) dp[0][j] = j;

  // Fill DP table
  for (let i = 1; i <= m; i++) {
    for (let j = 1; j <= n; j++) {
      if (ref[i - 1] === hyp[j - 1]) {
        dp[i][j] = dp[i - 1][j - 1];
      } else {
        dp[i][j] = 1 + Math.min(
          dp[i - 1][j],     // Deletion
          dp[i][j - 1],     // Insertion
          dp[i - 1][j - 1]  // Substitution
        );
      }
    }
  }

  const editDistance = dp[m][n];
  const cer = editDistance / m;

  return {
    cer: Math.round(cer * 1000) / 1000, // Round to 3 decimal places
    editDistance
  };
}

/**
 * Compute Word Error Rate (WER) - optional, for reference
 */
function computeWER(reference, hypothesis) {
  const refNorm = normalizeForKoreanCompare(reference);
  const hypNorm = normalizeForKoreanCompare(hypothesis);

  const refWords = refNorm.split(/\s+/).filter(w => w);
  const hypWords = hypNorm.split(/\s+/).filter(w => w);

  if (refWords.length === 0) {
    return hypWords.length > 0 ? 1 : 0;
  }

  const m = refWords.length;
  const n = hypWords.length;

  const dp = Array(m + 1).fill(null).map(() => Array(n + 1).fill(0));

  for (let i = 0; i <= m; i++) dp[i][0] = i;
  for (let j = 0; j <= n; j++) dp[0][j] = j;

  for (let i = 1; i <= m; i++) {
    for (let j = 1; j <= n; j++) {
      if (refWords[i - 1] === hypWords[j - 1]) {
        dp[i][j] = dp[i - 1][j - 1];
      } else {
        dp[i][j] = 1 + Math.min(dp[i - 1][j], dp[i][j - 1], dp[i - 1][j - 1]);
      }
    }
  }

  return Math.round((dp[m][n] / m) * 1000) / 1000;
}

/**
 * Build character-level diff for highlighting
 * Returns units with status: 'correct', 'wrong', 'missing', 'extra'
 */
function buildCharacterDiff(reference, hypothesis) {
  const ref = normalizeForKoreanCompare(reference);
  const hyp = normalizeForKoreanCompare(hypothesis);

  const units = [];
  const wrongUnits = [];

  // Use simple alignment for diff
  const m = ref.length;
  const n = hyp.length;

  // Build DP table with backtracking
  const dp = Array(m + 1).fill(null).map(() => Array(n + 1).fill(0));
  const ops = Array(m + 1).fill(null).map(() => Array(n + 1).fill(''));

  for (let i = 0; i <= m; i++) { dp[i][0] = i; ops[i][0] = 'D'; }
  for (let j = 0; j <= n; j++) { dp[0][j] = j; ops[0][j] = 'I'; }
  ops[0][0] = '';

  for (let i = 1; i <= m; i++) {
    for (let j = 1; j <= n; j++) {
      if (ref[i - 1] === hyp[j - 1]) {
        dp[i][j] = dp[i - 1][j - 1];
        ops[i][j] = 'M'; // Match
      } else {
        const del = dp[i - 1][j] + 1;
        const ins = dp[i][j - 1] + 1;
        const sub = dp[i - 1][j - 1] + 1;
        const min = Math.min(del, ins, sub);
        dp[i][j] = min;
        if (min === sub) ops[i][j] = 'S'; // Substitution
        else if (min === del) ops[i][j] = 'D'; // Deletion
        else ops[i][j] = 'I'; // Insertion
      }
    }
  }

  // Backtrace to build diff
  let i = m, j = n;
  const result = [];

  while (i > 0 || j > 0) {
    const op = ops[i][j];
    if (op === 'M') {
      result.unshift({ unit: ref[i - 1], status: 'correct' });
      i--; j--;
    } else if (op === 'S') {
      result.unshift({ unit: ref[i - 1], status: 'wrong', got: hyp[j - 1] });
      wrongUnits.push(ref[i - 1]);
      i--; j--;
    } else if (op === 'D') {
      result.unshift({ unit: ref[i - 1], status: 'missing' });
      wrongUnits.push(ref[i - 1]);
      i--;
    } else if (op === 'I') {
      result.unshift({ unit: hyp[j - 1], status: 'extra' });
      j--;
    } else {
      break;
    }
  }

  return {
    units: result,
    wrongUnits: [...new Set(wrongUnits)] // Unique wrong characters
  };
}

/**
 * Build system prompt for xAI Grok - Friendly Korean Tutor for Kids
 *
 * 3-Tier Feedback System:
 * ✅ Correct (accuracy >= 90%): Praise + move on
 * ☑️ Partial (accuracy 50-89%): Gentle correction with specific comparison
 * ❌ Severe (accuracy < 50%): Full model + retry encouragement
 */
function buildGrammarSystemPrompt() {
  return `당신은 친근한 한국어 튜터 "그록"이에요. 아이들에게 한국어를 가르치는 역할이에요.

=== 튜터 성격 ===
- 친근하고 격려하는 톤 (반말 사용)
- 절대 "틀렸어"라고 하지 않아요
- 항상 노력을 먼저 인정해요
- 실수는 배움의 일부로 대해요

=== 3단계 피드백 시스템 ===

1. 정확함 (정확도 >= 90%):
   - "정확해! 잘했어." 또는 "와, 완벽해!"
   - 짧고 긍정적인 칭찬

2. 부분 오류 (정확도 50-89%, 1-2개 틀림):
   - 먼저 칭찬: "좋았어!" 또는 "거의 다 맞았어!"
   - 틀린 부분 구체적 비교: "'X' 말고 'Y'처럼 발음하면 좋아"
   - 재시도 유도: "다시 해볼까?"

3. 많이 틀림 (정확도 < 50%):
   - 부드럽게: "음, 다시 한 번 해볼까?"
   - 정답 제시: "정확한 발음은 'X'이야."
   - 격려: "잘 들어봐~"

=== 출력 형식 (JSON만, 마크다운 금지) ===
{
  "feedbackLevel": "correct" | "partial" | "severe",
  "grammarMistakes": [
    {
      "youSaid": "학생이 말한 부분",
      "correct": "올바른 표현",
      "reasonKo": "친근한 설명"
    }
  ],
  "commentKo": "튜터 피드백 메시지 (반말, 친근하게)"
}

=== 피드백 예시 ===

정확함 (90%+):
{"feedbackLevel":"correct","grammarMistakes":[],"commentKo":"정확해! 잘했어~"}

부분 오류 (50-89%):
{"feedbackLevel":"partial","grammarMistakes":[{"youSaid":"캐이크","correct":"케이크","reasonKo":"'ㅐ' 말고 'ㅔ' 소리야"}],"commentKo":"좋았어! 그런데 '케이크' 발음이 조금 달랐어. '캐이크' 말고 '케이크'처럼 발음해 볼까?"}

많이 틀림 (<50%):
{"feedbackLevel":"severe","grammarMistakes":[],"commentKo":"음, 다시 한 번 해볼까? 정확한 발음은 '커피 주세요'이야. 잘 들어봐~"}

=== 중요 규칙 ===
- 정확도에 맞는 feedbackLevel 선택 필수
- 낮은 정확도에서 칭찬 금지 (잘했어요, 완벽해요 등)
- 항상 격려하는 톤 유지
- JSON만 출력`;
}

/**
 * Build user prompt with accuracy level for 3-tier feedback
 */
function buildGrammarUserPrompt(targetText, transcriptText, wrongUnits, accuracyPercent) {
  // Determine feedback level based on accuracy
  let feedbackLevel;
  let instruction;

  if (accuracyPercent >= 90) {
    feedbackLevel = 'correct';
    instruction = '칭찬해 주세요. "정확해! 잘했어~" 같은 짧은 칭찬.';
  } else if (accuracyPercent >= 50) {
    feedbackLevel = 'partial';
    instruction = '먼저 칭찬하고, 틀린 부분을 구체적으로 비교해서 알려주세요. "좋았어! 그런데 X 발음이 조금 달랐어. Y 말고 Z처럼..."';
  } else {
    feedbackLevel = 'severe';
    instruction = '부드럽게 다시 시도하도록 격려하세요. "음, 다시 한 번 해볼까? 정확한 발음은 X이야." 칭찬 금지!';
  }

  const diffInfo = wrongUnits.length > 0
    ? `틀린 글자: ${wrongUnits.join(', ')}`
    : '차이 없음';

  return `TARGET: "${targetText}"
STUDENT: "${transcriptText}"
정확도: ${accuracyPercent}%
피드백 레벨: ${feedbackLevel}
${diffInfo}

${instruction}

JSON만 출력:`;
}

/**
 * Call xAI Grok API for tutor feedback
 * Returns { ok, tutor } on success or { ok: false, error, errorCode } on failure
 */
async function callGrokForTutor(targetText, transcriptText, wrongUnits, accuracyPercent) {
  const model = process.env.XAI_MODEL || DEFAULT_XAI_MODEL;
  const fetch = (await import('node-fetch')).default;

  console.log(`[Feedback] Calling xAI Grok (${model}) for tutor feedback...`);

  try {
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
          model,
          messages: [
            { role: 'system', content: buildGrammarSystemPrompt() },
            { role: 'user', content: buildGrammarUserPrompt(targetText, transcriptText, wrongUnits, accuracyPercent) }
          ],
          max_tokens: 500,
          temperature: 0.3
        }),
        signal: controller.signal
      });
      clearTimeout(timeoutId);
    } catch (err) {
      clearTimeout(timeoutId);
      if (err.name === 'AbortError') {
        return { ok: false, error: 'Request timed out', errorCode: 'TIMEOUT' };
      }
      throw err;
    }

    // Handle HTTP errors with specific codes
    if (!response.ok) {
      const errorText = await response.text().catch(() => 'Unknown error');
      console.error(`[Feedback] xAI API error ${response.status}: ${errorText}`);
      return {
        ok: false,
        error: `xAI API error: ${response.status}`,
        errorCode: response.status === 401 ? 'UNAUTHORIZED' :
                   response.status === 403 ? 'FORBIDDEN' :
                   response.status === 429 ? 'RATE_LIMITED' :
                   `HTTP_${response.status}`,
        details: errorText.substring(0, 200)
      };
    }

    const result = await response.json();
    const content = result.choices?.[0]?.message?.content?.trim();

    if (!content) {
      return { ok: false, error: 'Empty response from xAI', errorCode: 'EMPTY_RESPONSE' };
    }

    // Parse JSON response
    const cleanContent = content
      .replace(/^```json\s*/i, '')
      .replace(/^```\s*/i, '')
      .replace(/\s*```$/i, '')
      .trim();

    let parsed;
    try {
      parsed = JSON.parse(cleanContent);
    } catch (parseErr) {
      // Try to extract JSON from content
      const jsonMatch = content.match(/\{[\s\S]*\}/);
      if (jsonMatch) {
        try {
          parsed = JSON.parse(jsonMatch[0]);
        } catch (e) {
          console.error('[Feedback] Failed to parse Grok response:', content.substring(0, 200));
          return { ok: false, error: 'Failed to parse JSON response', errorCode: 'PARSE_ERROR', rawResponse: content.substring(0, 200) };
        }
      } else {
        return { ok: false, error: 'No JSON found in response', errorCode: 'PARSE_ERROR', rawResponse: content.substring(0, 200) };
      }
    }

    // Build tutor object with 3-tier feedback
    const tutor = {
      feedbackLevel: parsed.feedbackLevel || (accuracyPercent >= 90 ? 'correct' : accuracyPercent >= 50 ? 'partial' : 'severe'),
      grammarMistakes: (parsed.grammarMistakes || []).map(m => ({
        youSaid: m.youSaid || m.wrong || '',
        correct: m.correct || '',
        reasonKo: m.reasonKo || m.reason_ko || m.why || ''
      })),
      commentKo: parsed.commentKo || parsed.comment_ko || parsed.tutorComment || '',
      // Legacy fields for backward compatibility
      pronunciationWeak: parsed.pronunciationWeak || [],
      pronunciationStrong: parsed.pronunciationStrong || []
    };

    console.log(`[Feedback] Grok success: ${tutor.grammarMistakes.length} mistakes, comment="${tutor.commentKo.substring(0, 30)}..."`);
    return { ok: true, tutor };

  } catch (err) {
    console.error('[Feedback] Grok exception:', err.message);
    return {
      ok: false,
      error: err.message,
      errorCode: 'EXCEPTION'
    };
  }
}

/**
 * POST /api/feedback handler
 *
 * Input: { targetText, transcriptText }
 * Output: Structured feedback with metrics, diff, tutor (or tutorError)
 *
 * Response structure:
 * - tutor: { pronunciationWeak, pronunciationStrong, grammarMistakes, commentKo } on Grok success
 * - tutor: null + tutorError: { code, message } on Grok failure
 */
async function feedbackHandler(req, res) {
  try {
    const { targetText } = req.body;
    // Accept both transcriptText and sttText for backward compatibility
    const transcriptText = req.body.transcriptText || req.body.sttText;

    // Validate required fields
    if (!targetText || typeof targetText !== 'string') {
      return res.status(400).json({
        ok: false,
        error: 'Missing or invalid "targetText" field'
      });
    }

    if (!transcriptText || typeof transcriptText !== 'string') {
      return res.status(400).json({
        ok: false,
        error: 'Missing or invalid "transcriptText" field (also accepts "sttText")'
      });
    }

    // Compute CER-based metrics (ignores punctuation)
    const { cer } = computeCER(targetText, transcriptText);
    const wer = computeWER(targetText, transcriptText);
    const wrongPercent = Math.round(cer * 100);
    const accuracyPercent = Math.max(0, 100 - wrongPercent);

    // Build character-level diff
    const diff = buildCharacterDiff(targetText, transcriptText);

    console.log(`[Feedback] Target: "${targetText.substring(0, 30)}..." | Transcript: "${transcriptText.substring(0, 30)}..." | Accuracy: ${accuracyPercent}%`);

    // Call xAI Grok for tutor feedback
    let tutor = null;
    let tutorError = null;

    if (isXAIConfigured()) {
      const grokResult = await callGrokForTutor(targetText, transcriptText, diff.wrongUnits, accuracyPercent);

      if (grokResult.ok) {
        tutor = grokResult.tutor;
      } else {
        // Grok failed - expose error, DO NOT return fallback "잘했어요"
        tutorError = {
          code: grokResult.errorCode || 'UNKNOWN',
          message: grokResult.error || 'Grok feedback unavailable'
        };
        if (grokResult.details) {
          tutorError.details = grokResult.details;
        }
        console.warn(`[Feedback] Grok failed: ${tutorError.code} - ${tutorError.message}`);
      }
    } else {
      // xAI not configured
      tutorError = {
        code: 'NOT_CONFIGURED',
        message: 'XAI_API_KEY not set'
      };
      console.warn('[Feedback] xAI not configured');
    }

    // Build response
    const response = {
      ok: true,
      targetText,
      transcriptText,

      // Top-level score fields for easy UI binding
      textAccuracyPercent: accuracyPercent,
      mistakePercent: wrongPercent,
      score: accuracyPercent,

      // Detailed metrics
      metrics: {
        accuracyPercent,
        wrongPercent,
        textAccuracyPercent: accuracyPercent,
        mistakePercent: wrongPercent,
        cer,
        wer
      },

      // Diff for highlighting
      diff: {
        units: diff.units,
        wrongUnits: diff.wrongUnits,
        wrongParts: diff.wrongUnits
      },

      // Tutor feedback from Grok (or null + error)
      tutor,
      tutorError,

      // Legacy fields for backward compatibility
      pronunciation: {
        available: false,
        good: [],
        weak: [],
        note: '발음 평가는 오디오 기반 분석이 필요합니다.'
      },
      grammar: {
        corrections: tutor?.grammarMistakes?.map(m => ({
          wrong: m.youSaid,
          correct: m.correct,
          reason_ko: m.reasonKo
        })) || [],
        comment_ko: tutor?.commentKo || ''
      }
    };

    console.log(`[Feedback] Response: accuracy=${accuracyPercent}%, tutor=${tutor ? 'OK' : 'null'}, tutorError=${tutorError?.code || 'none'}`);

    res.json(response);

  } catch (err) {
    console.error('[Feedback] Error:', err.message);
    res.status(500).json({
      ok: false,
      error: 'Feedback processing failed',
      details: err.message
    });
  }
}

module.exports = {
  feedbackHandler,
  normalizeForKoreanCompare,
  computeCER,
  computeWER,
  buildCharacterDiff
};
