using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShadowingTutor
{
    /// <summary>
    /// Repository for loading and managing Korean sentences.
    /// Loads from Resources/sentences.json with random non-repeat access.
    /// </summary>
    public class SentenceRepo : MonoBehaviour
    {
        private static SentenceRepo _instance;
        public static SentenceRepo Instance => _instance;

        [Serializable]
        public class Sentence
        {
            public int id;
            public string korean;
            public string english;
            public string category;
        }

        [Serializable]
        private class SentenceList
        {
            public List<Sentence> sentences;
        }

        private List<Sentence> _allSentences = new List<Sentence>();
        private List<int> _shuffledIndices = new List<int>();
        private int _currentIndex = -1;
        private string _currentCategory = "";
        private System.Random _rng;

        public event Action OnSentencesLoaded;
        public event Action<Sentence> OnSentenceChanged;

        public Sentence CurrentSentence { get; private set; }
        public int TotalSentences => _allSentences.Count;
        public int RemainingInShuffle => _shuffledIndices.Count - _currentIndex - 1;
        public bool IsLoaded => _allSentences.Count > 0;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            InitializeRng();
            LoadSentences();
        }

        /// <summary>
        /// Initialize RNG with optional debug seed
        /// </summary>
        private void InitializeRng()
        {
            int seed = AppConfig.Instance?.DebugShuffleSeed ?? 0;
            if (seed > 0)
            {
                _rng = new System.Random(seed);
                Debug.Log($"[SentenceRepo] Using debug seed: {seed}");
            }
            else
            {
                _rng = new System.Random();
                Debug.Log("[SentenceRepo] Using random seed");
            }
        }

        /// <summary>
        /// Load sentences from Resources/sentences.json
        /// </summary>
        public void LoadSentences()
        {
            try
            {
                TextAsset jsonFile = Resources.Load<TextAsset>("sentences");
                if (jsonFile == null)
                {
                    Debug.LogError("[SentenceRepo] sentences.json not found in Resources!");
                    CreateFallbackSentences();
                    return;
                }

                SentenceList data = JsonUtility.FromJson<SentenceList>(jsonFile.text);
                if (data?.sentences == null || data.sentences.Count == 0)
                {
                    Debug.LogError("[SentenceRepo] Failed to parse sentences.json or empty!");
                    CreateFallbackSentences();
                    return;
                }

                _allSentences = data.sentences;
                Debug.Log($"[SentenceRepo] Loaded {_allSentences.Count} sentences");
                Reshuffle();
                OnSentencesLoaded?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogError($"[SentenceRepo] Error loading sentences: {e.Message}");
                CreateFallbackSentences();
            }
        }

        /// <summary>
        /// Create fallback sentences if loading fails
        /// </summary>
        private void CreateFallbackSentences()
        {
            _allSentences = new List<Sentence>
            {
                new Sentence { id = 1, korean = "안녕하세요", english = "Hello", category = "daily" },
                new Sentence { id = 2, korean = "감사합니다", english = "Thank you", category = "daily" },
                new Sentence { id = 3, korean = "죄송합니다", english = "I'm sorry", category = "daily" },
                new Sentence { id = 4, korean = "좋은 아침이에요", english = "Good morning", category = "daily" },
                new Sentence { id = 5, korean = "오늘 날씨가 좋네요", english = "The weather is nice today", category = "daily" }
            };
            Debug.LogWarning("[SentenceRepo] Using fallback sentences");
            Reshuffle();
            OnSentencesLoaded?.Invoke();
        }

        /// <summary>
        /// Filter sentences by category (empty string = all categories)
        /// </summary>
        public void SetCategory(string category)
        {
            _currentCategory = category?.ToLower() ?? "";
            Reshuffle();
        }

        /// <summary>
        /// Get all available categories
        /// </summary>
        public List<string> GetCategories()
        {
            HashSet<string> categories = new HashSet<string>();
            foreach (var s in _allSentences)
            {
                if (!string.IsNullOrEmpty(s.category))
                    categories.Add(s.category);
            }
            return new List<string>(categories);
        }

        /// <summary>
        /// Get sentences filtered by current category
        /// </summary>
        private List<Sentence> GetFilteredSentences()
        {
            if (string.IsNullOrEmpty(_currentCategory))
                return _allSentences;

            List<Sentence> filtered = new List<Sentence>();
            foreach (var s in _allSentences)
            {
                if (s.category?.ToLower() == _currentCategory)
                    filtered.Add(s);
            }
            return filtered.Count > 0 ? filtered : _allSentences;
        }

        /// <summary>
        /// Reshuffle the sentence order (Fisher-Yates)
        /// </summary>
        public void Reshuffle()
        {
            List<Sentence> filtered = GetFilteredSentences();
            _shuffledIndices.Clear();

            for (int i = 0; i < filtered.Count; i++)
                _shuffledIndices.Add(_allSentences.IndexOf(filtered[i]));

            // Fisher-Yates shuffle using persistent RNG
            int n = _shuffledIndices.Count;
            while (n > 1)
            {
                n--;
                int k = _rng.Next(n + 1);
                int temp = _shuffledIndices[k];
                _shuffledIndices[k] = _shuffledIndices[n];
                _shuffledIndices[n] = temp;
            }

            _currentIndex = -1;
            Debug.Log($"[SentenceRepo] Reshuffled {_shuffledIndices.Count} sentences");
        }

        /// <summary>
        /// Get the next sentence (reshuffles if exhausted)
        /// </summary>
        public Sentence GetNext()
        {
            if (_shuffledIndices.Count == 0)
            {
                Reshuffle();
            }

            _currentIndex++;
            if (_currentIndex >= _shuffledIndices.Count)
            {
                Debug.Log("[SentenceRepo] All sentences used, reshuffling...");
                Reshuffle();
                _currentIndex = 0;
            }

            CurrentSentence = _allSentences[_shuffledIndices[_currentIndex]];
            OnSentenceChanged?.Invoke(CurrentSentence);
            return CurrentSentence;
        }

        /// <summary>
        /// Get a random sentence (may repeat)
        /// </summary>
        public Sentence GetRandom()
        {
            if (_allSentences.Count == 0) return null;
            List<Sentence> filtered = GetFilteredSentences();
            int idx = UnityEngine.Random.Range(0, filtered.Count);
            CurrentSentence = filtered[idx];
            OnSentenceChanged?.Invoke(CurrentSentence);
            return CurrentSentence;
        }

        /// <summary>
        /// Replay current sentence (no change)
        /// </summary>
        public Sentence Replay()
        {
            if (CurrentSentence == null)
                return GetNext();
            return CurrentSentence;
        }
    }
}
