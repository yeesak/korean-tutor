using UnityEngine;

namespace ShadowingTutor
{
    /// <summary>
    /// Simple lip sync controller using RMS audio envelope.
    /// Drives a jaw bone transform or blendshape based on audio amplitude.
    /// </summary>
    public class LipSyncController : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("The jaw transform to animate (will rotate around X axis)")]
        [SerializeField] private Transform _jawBone;

        [Tooltip("Optional SkinnedMeshRenderer for blendshape-based lip sync")]
        [SerializeField] private SkinnedMeshRenderer _skinnedMesh;

        [Tooltip("Index of the mouth open blendshape")]
        [SerializeField] private int _mouthOpenBlendshapeIndex = 0;

        [Header("Jaw Rotation Settings")]
        [Tooltip("Rotation axis for jaw bone")]
        [SerializeField] private Vector3 _rotationAxis = Vector3.right;

        [Tooltip("Maximum jaw rotation angle in degrees")]
        [SerializeField] private float _maxJawAngle = 15f;

        [Header("Blendshape Settings")]
        [Tooltip("Maximum blendshape weight (0-100)")]
        [SerializeField] private float _maxBlendshapeWeight = 100f;

        [Header("Animation Settings")]
        [Tooltip("How quickly the mouth responds to audio changes")]
        [SerializeField] private float _responsiveness = 15f;

        [Tooltip("Minimum RMS threshold to trigger animation")]
        [SerializeField] private float _threshold = 0.01f;

        [Tooltip("Multiplier for RMS value")]
        [SerializeField] private float _sensitivity = 5f;

        private Quaternion _jawBaseRotation;
        private float _currentOpenAmount = 0f;
        private float _targetOpenAmount = 0f;

        private void Start()
        {
            // Store base rotation if jaw bone is assigned
            if (_jawBone != null)
            {
                _jawBaseRotation = _jawBone.localRotation;
            }

            // Subscribe to TTS audio events
            if (TtsPlayer.Instance != null)
            {
                TtsPlayer.Instance.OnAudioData += OnAudioData;
                TtsPlayer.Instance.OnPlaybackComplete += OnPlaybackComplete;
            }
        }

        private void OnDestroy()
        {
            // Unsubscribe
            if (TtsPlayer.Instance != null)
            {
                TtsPlayer.Instance.OnAudioData -= OnAudioData;
                TtsPlayer.Instance.OnPlaybackComplete -= OnPlaybackComplete;
            }
        }

        private void LateUpdate()
        {
            // Skip if TtsLipSyncRuntime is handling lip sync
            if (TtsLipSyncRuntime.Instance != null && TtsLipSyncRuntime.Instance.ttsSource != null &&
                TtsLipSyncRuntime.Instance.ttsSource.isPlaying)
            {
                return;
            }

            // Smooth interpolation to target
            _currentOpenAmount = Mathf.Lerp(_currentOpenAmount, _targetOpenAmount, Time.deltaTime * _responsiveness);

            // Apply to jaw bone
            if (_jawBone != null)
            {
                float angle = _currentOpenAmount * _maxJawAngle;
                _jawBone.localRotation = _jawBaseRotation * Quaternion.AngleAxis(angle, _rotationAxis);
            }

            // Apply to blendshape
            if (_skinnedMesh != null && _mouthOpenBlendshapeIndex >= 0)
            {
                float weight = _currentOpenAmount * _maxBlendshapeWeight;
                _skinnedMesh.SetBlendShapeWeight(_mouthOpenBlendshapeIndex, weight);
            }
        }

        /// <summary>
        /// Called when audio data is received from TtsPlayer
        /// </summary>
        private void OnAudioData(float rms)
        {
            // Apply threshold and sensitivity
            if (rms < _threshold)
            {
                _targetOpenAmount = 0f;
            }
            else
            {
                // Normalize and clamp
                _targetOpenAmount = Mathf.Clamp01((rms - _threshold) * _sensitivity);
            }
        }

        /// <summary>
        /// Called when playback completes
        /// </summary>
        private void OnPlaybackComplete()
        {
            _targetOpenAmount = 0f;
        }

        /// <summary>
        /// Manually set mouth open amount (0-1)
        /// </summary>
        public void SetOpenAmount(float amount)
        {
            _targetOpenAmount = Mathf.Clamp01(amount);
        }

        /// <summary>
        /// Set jaw bone at runtime
        /// </summary>
        public void SetJawBone(Transform jaw)
        {
            _jawBone = jaw;
            if (_jawBone != null)
            {
                _jawBaseRotation = _jawBone.localRotation;
            }
        }

        /// <summary>
        /// Set skinned mesh renderer and blendshape index at runtime
        /// </summary>
        public void SetBlendshapeTarget(SkinnedMeshRenderer mesh, int blendshapeIndex)
        {
            _skinnedMesh = mesh;
            _mouthOpenBlendshapeIndex = blendshapeIndex;
        }

        /// <summary>
        /// Find and auto-configure jaw bone from hierarchy
        /// </summary>
        public bool AutoConfigureJaw()
        {
            // Try common jaw bone names
            string[] jawNames = { "Jaw", "jaw", "Jaw_Bone", "jaw_bone", "Bip01_Head_Jaw", "Head_Jaw", "Chin" };

            foreach (string name in jawNames)
            {
                Transform found = FindChildRecursive(transform, name);
                if (found != null)
                {
                    SetJawBone(found);
                    Debug.Log($"[LipSyncController] Auto-configured jaw bone: {found.name}");
                    return true;
                }
            }

            Debug.LogWarning("[LipSyncController] Could not find jaw bone automatically");
            return false;
        }

        private Transform FindChildRecursive(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name.ToLower().Contains(name.ToLower()))
                    return child;

                Transform found = FindChildRecursive(child, name);
                if (found != null)
                    return found;
            }
            return null;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Test animation in editor
        /// </summary>
        [ContextMenu("Test Open Mouth")]
        private void TestOpenMouth()
        {
            _targetOpenAmount = 1f;
        }

        [ContextMenu("Test Close Mouth")]
        private void TestCloseMouth()
        {
            _targetOpenAmount = 0f;
        }
#endif
    }
}
