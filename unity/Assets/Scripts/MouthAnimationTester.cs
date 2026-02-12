using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ShadowingTutor
{
    /// <summary>
    /// Test script for verifying mouth animation without TTS.
    /// Uses new Input System to avoid exceptions.
    ///
    /// Usage:
    /// - M key: Toggle mouth open/close
    /// - Hold SPACE: Simulate speaking (mouth opens based on sine wave)
    /// - L key: Log current wiring status
    /// </summary>
    public class MouthAnimationTester : MonoBehaviour
    {
        [Header("Test Settings")]
        [SerializeField] private float _testOpenAmount = 0.8f;
        [SerializeField] private float _speakFrequency = 8f;

        private TtsMouthController _mouthController;
        private bool _mouthOpen = false;
        private bool _wasHoldingSpace = false;
        private bool _inputAvailable = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            // Only in editor/development
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            GameObject go = new GameObject("_MouthAnimationTester");
            go.AddComponent<MouthAnimationTester>();
            Debug.Log("[MouthTester] Created. Keys: M=toggle, SPACE=simulate, L=status");
            #endif
        }

        private void Start()
        {
            // Check if input is available
            #if ENABLE_INPUT_SYSTEM
            _inputAvailable = Keyboard.current != null;
            #elif ENABLE_LEGACY_INPUT_MANAGER
            _inputAvailable = true;
            #else
            _inputAvailable = false;
            #endif

            if (!_inputAvailable)
            {
                Debug.Log("[MouthTester] Input not available - test keys disabled");
            }
        }

        private void Update()
        {
            // Find controller if not cached
            if (_mouthController == null)
            {
                _mouthController = TtsMouthController.Instance;
            }

            // Skip input polling if not available
            if (!_inputAvailable) return;

            #if ENABLE_INPUT_SYSTEM
            // New Input System
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            // M key: Toggle mouth
            if (keyboard.mKey.wasPressedThisFrame)
            {
                ToggleMouth();
            }

            // SPACE key: Simulate speaking
            bool holdingSpace = keyboard.spaceKey.isPressed;
            HandleSpaceKey(holdingSpace);

            // L key: Log status
            if (keyboard.lKey.wasPressedThisFrame)
            {
                LogWiringStatus();
            }

            #elif ENABLE_LEGACY_INPUT_MANAGER
            // Legacy Input Manager
            if (Input.GetKeyDown(KeyCode.M))
            {
                ToggleMouth();
            }

            bool holdingSpace = Input.GetKey(KeyCode.Space);
            HandleSpaceKey(holdingSpace);

            if (Input.GetKeyDown(KeyCode.L))
            {
                LogWiringStatus();
            }
            #endif
        }

        private void ToggleMouth()
        {
            _mouthOpen = !_mouthOpen;

            if (_mouthController != null)
            {
                var field = typeof(TtsMouthController).GetField("_faceRenderer",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var indexField = typeof(TtsMouthController).GetField("_mouthBlendshapeIndex",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var maxWeightField = typeof(TtsMouthController).GetField("_maxBlendshapeWeight",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (field != null && indexField != null && maxWeightField != null)
                {
                    var renderer = field.GetValue(_mouthController) as SkinnedMeshRenderer;
                    int index = (int)indexField.GetValue(_mouthController);
                    float maxWeight = (float)maxWeightField.GetValue(_mouthController);

                    if (renderer != null && index >= 0)
                    {
                        float weight = _mouthOpen ? maxWeight * _testOpenAmount : 0f;
                        renderer.SetBlendShapeWeight(index, weight);
                        Debug.Log($"[MouthTester] Mouth {(_mouthOpen ? "OPEN" : "CLOSED")} weight={weight:F1}");
                    }
                    else
                    {
                        Debug.LogWarning("[MouthTester] Face renderer or blendshape not configured");
                    }
                }
            }
            else
            {
                Debug.LogWarning("[MouthTester] TtsMouthController not found");
            }
        }

        private void HandleSpaceKey(bool holdingSpace)
        {
            if (holdingSpace)
            {
                if (_mouthController != null)
                {
                    var field = typeof(TtsMouthController).GetField("_faceRenderer",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var indexField = typeof(TtsMouthController).GetField("_mouthBlendshapeIndex",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var maxWeightField = typeof(TtsMouthController).GetField("_maxBlendshapeWeight",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    if (field != null && indexField != null && maxWeightField != null)
                    {
                        var renderer = field.GetValue(_mouthController) as SkinnedMeshRenderer;
                        int index = (int)indexField.GetValue(_mouthController);
                        float maxWeight = (float)maxWeightField.GetValue(_mouthController);

                        if (renderer != null && index >= 0)
                        {
                            float simRms = (Mathf.Sin(Time.time * _speakFrequency) + 1f) * 0.5f;
                            float weight = simRms * maxWeight * _testOpenAmount;
                            renderer.SetBlendShapeWeight(index, weight);
                        }
                    }
                }

                if (!_wasHoldingSpace)
                {
                    Debug.Log("[MouthTester] Simulating speech...");
                }
            }
            else if (_wasHoldingSpace)
            {
                if (_mouthController != null)
                {
                    var field = typeof(TtsMouthController).GetField("_faceRenderer",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var indexField = typeof(TtsMouthController).GetField("_mouthBlendshapeIndex",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    if (field != null && indexField != null)
                    {
                        var renderer = field.GetValue(_mouthController) as SkinnedMeshRenderer;
                        int index = (int)indexField.GetValue(_mouthController);

                        if (renderer != null && index >= 0)
                        {
                            renderer.SetBlendShapeWeight(index, 0f);
                        }
                    }
                }
                Debug.Log("[MouthTester] Speech stopped");
            }

            _wasHoldingSpace = holdingSpace;
        }

        /// <summary>
        /// Public method to log status (can be called from UI button)
        /// </summary>
        public void LogWiringStatus()
        {
            Debug.Log("=== MOUTH WIRING STATUS ===");

            if (_mouthController == null)
            {
                _mouthController = TtsMouthController.Instance;
            }

            if (_mouthController == null)
            {
                Debug.LogError("TtsMouthController: NOT FOUND");
                return;
            }

            Debug.Log($"TtsMouthController: FOUND on {_mouthController.gameObject.name}");

            var faceField = typeof(TtsMouthController).GetField("_faceRenderer",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var indexField = typeof(TtsMouthController).GetField("_mouthBlendshapeIndex",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var nameField = typeof(TtsMouthController).GetField("_selectedBlendshapeName",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var audioField = typeof(TtsMouthController).GetField("_ttsAudioSource",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var jawField = typeof(TtsMouthController).GetField("_jawBone",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (faceField != null)
            {
                var renderer = faceField.GetValue(_mouthController) as SkinnedMeshRenderer;
                if (renderer != null)
                {
                    int blendCount = renderer.sharedMesh != null ? renderer.sharedMesh.blendShapeCount : 0;
                    Debug.Log($"Face Renderer: {renderer.name} (blendshapes: {blendCount})");
                }
                else
                {
                    Debug.LogWarning("Face Renderer: NOT SET");
                }
            }

            if (indexField != null && nameField != null)
            {
                int index = (int)indexField.GetValue(_mouthController);
                string name = nameField.GetValue(_mouthController) as string;
                if (index >= 0)
                {
                    Debug.Log($"Blendshape: {name} (index {index})");
                }
                else
                {
                    Debug.LogWarning("Blendshape: NOT SET");
                }
            }

            if (audioField != null)
            {
                var audio = audioField.GetValue(_mouthController) as AudioSource;
                if (audio != null)
                {
                    string clipName = audio.clip != null ? audio.clip.name : "none";
                    Debug.Log($"TTS AudioSource: {audio.gameObject.name} (clip: {clipName}, playing: {audio.isPlaying})");
                }
                else
                {
                    Debug.LogWarning("TTS AudioSource: NOT SET");
                }
            }

            if (jawField != null)
            {
                var jaw = jawField.GetValue(_mouthController) as Transform;
                if (jaw != null)
                {
                    Debug.Log($"Jaw Bone: {jaw.name}");
                }
                else
                {
                    Debug.Log("Jaw Bone: NOT SET");
                }
            }

            Debug.Log("=== END STATUS ===");
        }

        /// <summary>
        /// Public method to open mouth (can be called from UI)
        /// </summary>
        public void TestOpenMouth()
        {
            _mouthOpen = true;
            ToggleMouth();
            _mouthOpen = true; // Keep open state
        }

        /// <summary>
        /// Public method to close mouth (can be called from UI)
        /// </summary>
        public void TestCloseMouth()
        {
            _mouthOpen = false;
            if (_mouthController != null)
            {
                var field = typeof(TtsMouthController).GetField("_faceRenderer",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var indexField = typeof(TtsMouthController).GetField("_mouthBlendshapeIndex",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (field != null && indexField != null)
                {
                    var renderer = field.GetValue(_mouthController) as SkinnedMeshRenderer;
                    int index = (int)indexField.GetValue(_mouthController);

                    if (renderer != null && index >= 0)
                    {
                        renderer.SetBlendShapeWeight(index, 0f);
                        Debug.Log("[MouthTester] Mouth CLOSED");
                    }
                }
            }
        }
    }
}
