#!/usr/bin/env node
/**
 * Smoke Test Script - Quick endpoint validation
 * Run: node tools/smoke.js [baseUrl]
 * Default: http://localhost:3000
 */

const BASE_URL = process.argv[2] || 'http://localhost:3000';

const colors = {
  green: '\x1b[32m',
  red: '\x1b[31m',
  yellow: '\x1b[33m',
  cyan: '\x1b[36m',
  reset: '\x1b[0m'
};

function log(color, symbol, msg) {
  console.log(`${colors[color]}${symbol}${colors.reset} ${msg}`);
}

function pass(msg) { log('green', '✓', msg); }
function fail(msg) { log('red', '✗', msg); }
function info(msg) { log('cyan', '→', msg); }

/**
 * Generate minimal WAV for STT test
 */
function generateTestWav() {
  const sampleRate = 16000;
  const numSamples = 8000; // 0.5 seconds
  const buffer = Buffer.alloc(44 + numSamples * 2);

  buffer.write('RIFF', 0);
  buffer.writeUInt32LE(36 + numSamples * 2, 4);
  buffer.write('WAVE', 8);
  buffer.write('fmt ', 12);
  buffer.writeUInt32LE(16, 16);
  buffer.writeUInt16LE(1, 20);
  buffer.writeUInt16LE(1, 22);
  buffer.writeUInt32LE(sampleRate, 24);
  buffer.writeUInt32LE(sampleRate * 2, 28);
  buffer.writeUInt16LE(2, 32);
  buffer.writeUInt16LE(16, 34);
  buffer.write('data', 36);
  buffer.writeUInt32LE(numSamples * 2, 40);

  for (let i = 0; i < numSamples; i++) {
    const sample = Math.floor(Math.sin(2 * Math.PI * 440 * i / sampleRate) * 16000);
    buffer.writeInt16LE(sample, 44 + i * 2);
  }

  return buffer;
}

async function testHealth() {
  info('Testing /api/health...');
  try {
    const res = await fetch(`${BASE_URL}/api/health`);
    const data = await res.json();

    if (res.ok && data.ok === true) {
      pass(`Health OK - mockMode: ${data.mockMode}`);
      pass(`  Services: TTS=${data.services.tts}, STT=${data.services.stt}, Feedback=${data.services.feedback}`);
      return true;
    } else {
      fail(`Health failed: ${JSON.stringify(data)}`);
      return false;
    }
  } catch (e) {
    fail(`Health error: ${e.message}`);
    return false;
  }
}

async function testSentences() {
  info('Testing /api/sentences...');
  try {
    const res = await fetch(`${BASE_URL}/api/sentences`);
    const data = await res.json();

    if (res.ok && data.sentences && data.sentences.length > 0) {
      pass(`Sentences OK - ${data.sentences.length} sentences loaded`);
      pass(`  Sample: "${data.sentences[0].korean}" (${data.sentences[0].category})`);
      return data.sentences[0];
    } else {
      fail(`Sentences failed: ${JSON.stringify(data)}`);
      return null;
    }
  } catch (e) {
    fail(`Sentences error: ${e.message}`);
    return null;
  }
}

async function testTts(text) {
  info(`Testing /api/tts with "${text.substring(0, 20)}..."...`);
  try {
    const res = await fetch(`${BASE_URL}/api/tts`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ text, speedProfile: 2 })
    });

    if (res.ok) {
      const contentType = res.headers.get('content-type');
      const buffer = await res.arrayBuffer();
      const bytes = new Uint8Array(buffer);

      // Check RIFF header
      const header = String.fromCharCode(...bytes.slice(0, 4));
      if (header === 'RIFF' && bytes.length > 44) {
        pass(`TTS OK - ${bytes.length} bytes, content-type: ${contentType}`);
        return true;
      } else {
        fail(`TTS returned invalid WAV (header: ${header})`);
        return false;
      }
    } else {
      const data = await res.json();
      fail(`TTS failed: ${JSON.stringify(data)}`);
      return false;
    }
  } catch (e) {
    fail(`TTS error: ${e.message}`);
    return false;
  }
}

async function testStt() {
  info('Testing /api/stt...');
  try {
    const wavBuffer = generateTestWav();
    const formData = new FormData();
    formData.append('audio', new Blob([wavBuffer], { type: 'audio/wav' }), 'test.wav');

    const res = await fetch(`${BASE_URL}/api/stt`, {
      method: 'POST',
      body: formData
    });

    const data = await res.json();

    if (res.ok && data.ok && data.text) {
      pass(`STT OK - transcript: "${data.text}"`);
      pass(`  Duration: ${data.duration}s, Language: ${data.language}`);
      return data.text;
    } else {
      fail(`STT failed: ${JSON.stringify(data)}`);
      return null;
    }
  } catch (e) {
    fail(`STT error: ${e.message}`);
    return null;
  }
}

async function testFeedback(original, spoken) {
  info(`Testing /api/feedback...`);
  try {
    const res = await fetch(`${BASE_URL}/api/feedback`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ original, spoken, mode: 'shadowing' })
    });

    const data = await res.json();

    if (res.ok && data.ok && data.feedback) {
      // Verify single line Korean
      const hasKorean = /[\uAC00-\uD7AF]/.test(data.feedback);
      const isSingleLine = !data.feedback.includes('\n');

      if (hasKorean && isSingleLine) {
        pass(`Feedback OK - "${data.feedback}"`);
        return true;
      } else {
        fail(`Feedback format invalid - Korean: ${hasKorean}, SingleLine: ${isSingleLine}`);
        return false;
      }
    } else {
      fail(`Feedback failed: ${JSON.stringify(data)}`);
      return false;
    }
  } catch (e) {
    fail(`Feedback error: ${e.message}`);
    return false;
  }
}

async function runSmoke() {
  console.log(`\n${colors.cyan}═══════════════════════════════════════════${colors.reset}`);
  console.log(`${colors.cyan}  Smoke Test - ${BASE_URL}${colors.reset}`);
  console.log(`${colors.cyan}═══════════════════════════════════════════${colors.reset}\n`);

  const results = { passed: 0, failed: 0 };

  // Test 1: Health
  if (await testHealth()) results.passed++; else results.failed++;
  console.log();

  // Test 2: Sentences
  const sentence = await testSentences();
  if (sentence) results.passed++; else results.failed++;
  console.log();

  // Test 3: TTS
  const text = sentence?.korean || '안녕하세요';
  if (await testTts(text)) results.passed++; else results.failed++;
  console.log();

  // Test 4: STT
  const transcript = await testStt();
  if (transcript) results.passed++; else results.failed++;
  console.log();

  // Test 5: Feedback
  const original = sentence?.korean || '안녕하세요';
  const spoken = transcript || '안녕하세요';
  if (await testFeedback(original, spoken)) results.passed++; else results.failed++;
  console.log();

  // Summary
  console.log(`${colors.cyan}═══════════════════════════════════════════${colors.reset}`);
  const total = results.passed + results.failed;
  const allPassed = results.failed === 0;

  if (allPassed) {
    console.log(`${colors.green}  ALL TESTS PASSED: ${results.passed}/${total}${colors.reset}`);
  } else {
    console.log(`${colors.red}  TESTS FAILED: ${results.failed}/${total}${colors.reset}`);
  }
  console.log(`${colors.cyan}═══════════════════════════════════════════${colors.reset}\n`);

  process.exit(allPassed ? 0 : 1);
}

runSmoke();
