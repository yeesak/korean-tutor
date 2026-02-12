/**
 * Stub Handlers for MODE=MOCK testing
 *
 * Returns realistic dummy data so Unity app can be tested
 * without any API keys configured.
 */

const fs = require('fs');
const path = require('path');

// Minimal MP3 silence (0.5 seconds) - base64 encoded
// This is a valid MP3 file that plays silence
const STUB_AUDIO_BASE64 = `
//uQxAAAAAANIAAAAAExBTUUzLjEwMFVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVV
VVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVV
VVVVVVVVVVVVVVVVVVVVTEFNRTMuMTAwVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVV
VVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVf/7kMQA
AAADSAAAAABMQUMzLjEwMFVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVV
VVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVV
VVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVf/7kMQAAAANIAAAAABMQUMzLjEwMFZWVlZW
VlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZW
VlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZW
VlZWVlZWVlZWVlZWVlb/+5DEAAADSAUAAAAAXQGgpAAAAAVZWVlZWVlZWVlZWVlZWVlZWVlZW
VlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZW
VlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZW
`.replace(/\s+/g, '');

/**
 * Stub TTS handler - returns a short silence MP3
 */
function stubTtsHandler(req, res) {
  const { text } = req.body;

  if (!text) {
    return res.status(400).json({
      ok: false,
      error: 'Missing text parameter'
    });
  }

  console.log(`[STUB TTS] Returning silence for: "${text.substring(0, 30)}..."`);

  const audioBuffer = Buffer.from(STUB_AUDIO_BASE64, 'base64');
  res.set('Content-Type', 'audio/mpeg');
  res.set('Content-Length', audioBuffer.length);
  res.send(audioBuffer);
}

/**
 * Stub STT handler - returns a mock transcription
 */
function stubSttHandler(req, res) {
  console.log('[STUB STT] Returning mock transcription');

  res.json({
    ok: true,
    text: '안녕하세요',
    transcriptText: '안녕하세요',
    rawTranscriptText: '안녕하세요.',
    words: [
      { word: '안녕하세요', start: 0.0, end: 1.5 }
    ],
    language: 'ko',
    duration: 1.5
  });
}

/**
 * Stub Eval handler - returns mock evaluation
 */
function stubEvalHandler(req, res) {
  const targetText = req.body.targetText || '안녕하세요';

  console.log(`[STUB EVAL] Returning mock eval for: "${targetText.substring(0, 30)}..."`);

  res.json({
    ok: true,
    targetText: targetText,
    transcriptText: targetText, // Perfect match for testing
    rawTranscriptText: targetText,
    textAccuracyPercent: 100,
    mistakePercent: 0,
    score: 100,
    metrics: {
      accuracyPercent: 100,
      wrongPercent: 0,
      textAccuracyPercent: 100,
      mistakePercent: 0,
      cer: 0.0
    },
    diff: {
      units: [],
      wrongUnits: [],
      wrongParts: []
    },
    pronunciation: {
      available: true,
      weakPronunciation: [],
      strongPronunciation: [
        { token: targetText, reason: 'Clear pronunciation' }
      ],
      shortComment: 'Great job! Your pronunciation is excellent.'
    },
    grammar: {
      mistakes: [],
      tutorComment: 'Perfect! No grammar issues detected.'
    }
  });
}

/**
 * Stub Feedback handler - returns mock feedback
 */
function stubFeedbackHandler(req, res) {
  const { targetText, transcriptText, sttText } = req.body;
  const spoken = transcriptText || sttText || '';
  const target = targetText || '';

  console.log(`[STUB FEEDBACK] Mock feedback for target="${target.substring(0, 20)}..."`);

  res.json({
    ok: true,
    targetText: target,
    transcriptText: spoken || target,
    textAccuracyPercent: 95,
    mistakePercent: 5,
    score: 95,
    metrics: {
      accuracyPercent: 95,
      wrongPercent: 5,
      textAccuracyPercent: 95,
      mistakePercent: 5,
      cer: 0.05,
      wer: 0.0
    },
    diff: {
      units: [],
      wrongUnits: [],
      wrongParts: []
    },
    pronunciation: {
      available: true,
      good: [],
      weak: [],
      note: 'Mock mode - no real pronunciation analysis'
    },
    grammar: {
      corrections: [],
      comment_ko: '잘했어요! 문법 오류가 없습니다.'
    },
    tutor: {
      feedbackLevel: 'correct',
      grammarMistakes: [],
      commentKo: '완벽해요! 아주 잘하셨습니다. 계속 연습하세요!',
      pronunciationWeak: [],
      pronunciationStrong: []
    },
    tutorError: null
  });
}

/**
 * Stub Grok handler - returns mock chat response
 */
function stubGrokHandler(req, res) {
  const messages = req.body.messages || [];
  const lastMessage = messages[messages.length - 1]?.content || '';

  console.log(`[STUB GROK] Mock response for: "${lastMessage.substring(0, 30)}..."`);

  res.json({
    ok: true,
    choices: [
      {
        message: {
          role: 'assistant',
          content: '잘하셨어요! 발음이 아주 좋습니다. 계속 연습하세요!'
        }
      }
    ]
  });
}

/**
 * Stub Pronounce handler - returns mock pronunciation feedback
 */
function stubPronounceHandler(req, res) {
  const targetText = req.body.targetText || '안녕하세요';

  console.log(`[STUB PRONOUNCE] Mock pronunciation for: "${targetText.substring(0, 30)}..."`);

  res.json({
    ok: true,
    tutor: {
      weakPronunciation: [],
      strongPronunciation: [
        { token: targetText, reason: 'Excellent pronunciation!' }
      ],
      shortComment: 'Your pronunciation is very clear and natural.'
    }
  });
}

module.exports = {
  stubTtsHandler,
  stubSttHandler,
  stubEvalHandler,
  stubFeedbackHandler,
  stubGrokHandler,
  stubPronounceHandler
};
