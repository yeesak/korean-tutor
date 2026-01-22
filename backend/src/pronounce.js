/**
 * Pronunciation Analysis Handler
 *
 * POST /api/pronounce_grok
 * Uses xAI Realtime API for voice-based pronunciation feedback.
 *
 * Input (multipart/form-data):
 *   - targetText: string (required)
 *   - transcriptText: string (optional, from STT)
 *   - locale: string (default: ko-KR)
 *   - audio: file (WAV format)
 *
 * Output:
 *   {
 *     ok: boolean,
 *     tutor: {
 *       weakPronunciation: [{token, reason, tip}],
 *       strongPronunciation: [{token, reason}],
 *       shortComment: string
 *     } | null,
 *     error?: string,
 *     rawText?: string (for debugging)
 *   }
 */

const { analyzePronunciation, isXAIRealtimeConfigured } = require('./xaiRealtimeClient');

// Expected audio format
const EXPECTED_SAMPLE_RATE = 16000;
const EXPECTED_CHANNELS = 1;
const EXPECTED_BITS_PER_SAMPLE = 16;

/**
 * Parse WAV header to extract format info
 * @param {Buffer} wavBuffer
 * @returns {object|null} Audio format info or null if invalid
 */
function parseWavHeader(wavBuffer) {
  if (!wavBuffer || wavBuffer.length < 44) {
    return null;
  }

  // Check RIFF header
  const riff = wavBuffer.slice(0, 4).toString();
  if (riff !== 'RIFF') {
    return null;
  }

  // Check WAVE format
  const wave = wavBuffer.slice(8, 12).toString();
  if (wave !== 'WAVE') {
    return null;
  }

  // Find fmt chunk
  let offset = 12;
  while (offset < wavBuffer.length - 8) {
    const chunkId = wavBuffer.slice(offset, offset + 4).toString();
    const chunkSize = wavBuffer.readUInt32LE(offset + 4);

    if (chunkId === 'fmt ') {
      const audioFormat = wavBuffer.readUInt16LE(offset + 8);
      const numChannels = wavBuffer.readUInt16LE(offset + 10);
      const sampleRate = wavBuffer.readUInt32LE(offset + 12);
      const byteRate = wavBuffer.readUInt32LE(offset + 16);
      const blockAlign = wavBuffer.readUInt16LE(offset + 20);
      const bitsPerSample = wavBuffer.readUInt16LE(offset + 22);

      return {
        audioFormat, // 1 = PCM
        numChannels,
        sampleRate,
        byteRate,
        blockAlign,
        bitsPerSample
      };
    }

    offset += 8 + chunkSize;
  }

  return null;
}

/**
 * Extract raw PCM data from WAV file
 * @param {Buffer} wavBuffer
 * @returns {Buffer|null} PCM data or null
 */
function extractPcmData(wavBuffer) {
  if (!wavBuffer || wavBuffer.length < 44) {
    return null;
  }

  // Find data chunk
  let offset = 12;
  while (offset < wavBuffer.length - 8) {
    const chunkId = wavBuffer.slice(offset, offset + 4).toString();
    const chunkSize = wavBuffer.readUInt32LE(offset + 4);

    if (chunkId === 'data') {
      return wavBuffer.slice(offset + 8, offset + 8 + chunkSize);
    }

    offset += 8 + chunkSize;
  }

  return null;
}

/**
 * Convert stereo PCM to mono by averaging channels
 * @param {Buffer} stereoBuffer - Stereo PCM16 data
 * @returns {Buffer} Mono PCM16 data
 */
function stereoToMono(stereoBuffer) {
  const monoSamples = stereoBuffer.length / 4; // 2 channels * 2 bytes
  const monoBuffer = Buffer.alloc(monoSamples * 2);

  for (let i = 0; i < monoSamples; i++) {
    const left = stereoBuffer.readInt16LE(i * 4);
    const right = stereoBuffer.readInt16LE(i * 4 + 2);
    const mono = Math.round((left + right) / 2);
    monoBuffer.writeInt16LE(mono, i * 2);
  }

  return monoBuffer;
}

/**
 * Simple resampling (nearest neighbor - not high quality but functional)
 * @param {Buffer} pcmBuffer - Input PCM16 data
 * @param {number} fromRate - Source sample rate
 * @param {number} toRate - Target sample rate
 * @returns {Buffer} Resampled PCM16 data
 */
function resample(pcmBuffer, fromRate, toRate) {
  if (fromRate === toRate) return pcmBuffer;

  const ratio = fromRate / toRate;
  const inputSamples = pcmBuffer.length / 2;
  const outputSamples = Math.floor(inputSamples / ratio);
  const outputBuffer = Buffer.alloc(outputSamples * 2);

  for (let i = 0; i < outputSamples; i++) {
    const srcIndex = Math.floor(i * ratio);
    const sample = pcmBuffer.readInt16LE(srcIndex * 2);
    outputBuffer.writeInt16LE(sample, i * 2);
  }

  return outputBuffer;
}

/**
 * Validate and convert audio to expected format
 * @param {Buffer} audioBuffer - WAV file buffer
 * @returns {object} { ok, pcmBuffer, error }
 */
function validateAndConvertAudio(audioBuffer) {
  // Parse WAV header
  const format = parseWavHeader(audioBuffer);
  if (!format) {
    return {
      ok: false,
      error: 'Invalid WAV file. Please record in WAV format.'
    };
  }

  // Check if it's PCM
  if (format.audioFormat !== 1) {
    return {
      ok: false,
      error: `Unsupported audio format: ${format.audioFormat}. Please record PCM WAV.`
    };
  }

  // Check bits per sample
  if (format.bitsPerSample !== 16) {
    return {
      ok: false,
      error: `Unsupported bits per sample: ${format.bitsPerSample}. Please record 16-bit audio.`
    };
  }

  // Extract PCM data
  let pcmData = extractPcmData(audioBuffer);
  if (!pcmData) {
    return {
      ok: false,
      error: 'Could not extract audio data from WAV file.'
    };
  }

  console.log(`[Pronounce] Input audio: ${format.sampleRate}Hz, ${format.numChannels}ch, ${format.bitsPerSample}bit`);

  // Convert stereo to mono if needed
  if (format.numChannels === 2) {
    console.log('[Pronounce] Converting stereo to mono');
    pcmData = stereoToMono(pcmData);
  } else if (format.numChannels !== 1) {
    return {
      ok: false,
      error: `Unsupported channel count: ${format.numChannels}. Please record mono or stereo.`
    };
  }

  // Resample if needed (to 16kHz)
  if (format.sampleRate !== EXPECTED_SAMPLE_RATE) {
    console.log(`[Pronounce] Resampling from ${format.sampleRate}Hz to ${EXPECTED_SAMPLE_RATE}Hz`);
    pcmData = resample(pcmData, format.sampleRate, EXPECTED_SAMPLE_RATE);
  }

  console.log(`[Pronounce] Output PCM: ${pcmData.length} bytes (${pcmData.length / 2 / EXPECTED_SAMPLE_RATE}s)`);

  return {
    ok: true,
    pcmBuffer: pcmData,
    sampleRate: EXPECTED_SAMPLE_RATE
  };
}

/**
 * POST /api/pronounce_grok handler
 */
async function pronounceHandler(req, res) {
  try {
    // Check if xAI is configured
    if (!isXAIRealtimeConfigured()) {
      return res.status(503).json({
        ok: false,
        error: 'Pronunciation service unavailable',
        details: 'XAI_API_KEY not configured',
        tutor: null
      });
    }

    // Get form fields
    const targetText = req.body.targetText;
    const transcriptText = req.body.transcriptText || '';
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
    console.log(`[Pronounce] Received audio: ${audioBuffer.length} bytes`);

    // Validate and convert audio
    const audioResult = validateAndConvertAudio(audioBuffer);
    if (!audioResult.ok) {
      return res.status(400).json({
        ok: false,
        error: audioResult.error,
        hint: 'Record audio as PCM16 mono 16000Hz WAV.'
      });
    }

    // Call xAI Realtime API
    console.log(`[Pronounce] Analyzing pronunciation for: "${targetText.substring(0, 30)}..."`);

    const result = await analyzePronunciation(audioResult.pcmBuffer, targetText, {
      transcriptText,
      locale,
      sampleRate: audioResult.sampleRate
    });

    if (result.ok) {
      console.log(`[Pronounce] Success: weak=${result.tutor.weakPronunciation.length}, strong=${result.tutor.strongPronunciation.length}`);
      res.json({
        ok: true,
        tutor: result.tutor
      });
    } else {
      console.log(`[Pronounce] Failed: ${result.error}`);
      // Return ok: true but tutor: null for graceful degradation
      res.json({
        ok: true,
        tutor: null,
        warning: result.error,
        rawText: result.rawText
      });
    }

  } catch (err) {
    console.error('[Pronounce] Error:', err.message);
    // Graceful degradation - return ok but no tutor
    res.json({
      ok: true,
      tutor: null,
      warning: `Pronunciation analysis unavailable: ${err.message}`
    });
  }
}

module.exports = {
  pronounceHandler,
  validateAndConvertAudio,
  parseWavHeader,
  extractPcmData
};
