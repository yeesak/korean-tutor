/**
 * Sentences Handler
 * Serves Korean sentences for shadowing practice
 */

const path = require('path');
const fs = require('fs');

// Load sentences from JSON file
let sentencesData = null;

function loadSentences() {
  if (sentencesData) return sentencesData;

  const filePath = path.join(__dirname, '..', '..', 'unity', 'Assets', 'Resources', 'sentences.json');

  try {
    const raw = fs.readFileSync(filePath, 'utf8');
    sentencesData = JSON.parse(raw);
    console.log(`[Sentences] Loaded ${sentencesData.sentences.length} sentences`);
    return sentencesData;
  } catch (err) {
    console.error('[Sentences] Failed to load sentences:', err.message);
    // Return minimal fallback
    return {
      sentences: [
        { id: 1, korean: "안녕하세요", english: "Hello", category: "daily" },
        { id: 2, korean: "감사합니다", english: "Thank you", category: "daily" }
      ]
    };
  }
}

/**
 * GET /api/sentences handler
 */
function getSentences(req, res) {
  try {
    const data = loadSentences();
    const { category } = req.query;

    let sentences = data.sentences;

    // Filter by category if specified
    if (category) {
      const validCategories = ['daily', 'travel', 'cafe', 'school', 'work'];
      if (!validCategories.includes(category)) {
        return res.status(400).json({
          error: 'Invalid category',
          validCategories
        });
      }
      sentences = sentences.filter(s => s.category === category);
    }

    res.json({
      total: sentences.length,
      categories: ['daily', 'travel', 'cafe', 'school', 'work'],
      sentences
    });

  } catch (err) {
    console.error('[Sentences] Error:', err.message);
    res.status(500).json({ error: 'Failed to load sentences', message: err.message });
  }
}

// Reload sentences (useful for development)
function reloadSentences() {
  sentencesData = null;
  return loadSentences();
}

module.exports = { getSentences, loadSentences, reloadSentences };
