using UnityEngine;
using System.Collections;

namespace ShadowingTutor
{
    /// <summary>
    /// Frames the camera for a portrait view of the character.
    /// Calculates position based on renderer bounds, not hardcoded values.
    /// Reserves space for UI elements (Start button at bottom).
    /// </summary>
    public class PortraitCameraFramer : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private string _avatarName = "Avatar";
        [SerializeField] private string _characterName = "CharacterModel";

        [Header("Framing")]
        [Tooltip("How much of vertical screen the character should fill (0.5 = half screen)")]
        [SerializeField] private float _verticalFill = 0.6f;

        [Tooltip("Vertical offset from bounds center (positive = look higher, toward face)")]
        [SerializeField] private float _verticalBias = 0.3f;

        [Tooltip("Reserved space at bottom for UI (0.15 = 15% of screen height)")]
        [SerializeField] private float _bottomUIReserve = 0.2f;

        [Tooltip("Field of view for portrait shot")]
        [SerializeField] private float _portraitFOV = 30f;

        [Tooltip("Minimum distance from character")]
        [SerializeField] private float _minDistance = 0.5f;

        [Tooltip("Maximum distance from character")]
        [SerializeField] private float _maxDistance = 3f;

        private Camera _camera;
        private Transform _character;
        private bool _framed = false;
        private bool _searching = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSetup()
        {
            Camera mainCam = Camera.main;
            if (mainCam == null)
            {
                Debug.LogWarning("[PortraitFramer] No Main Camera found");
                return;
            }

            // Add this framer if not present
            PortraitCameraFramer framer = mainCam.GetComponent<PortraitCameraFramer>();
            if (framer == null)
            {
                framer = mainCam.gameObject.AddComponent<PortraitCameraFramer>();
                Debug.Log("[PortraitFramer] Auto-attached to Main Camera");
            }
        }

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            if (_camera == null)
            {
                Debug.LogError("[PortraitFramer] No Camera component!");
                enabled = false;
            }
        }

        private void Start()
        {
            StartCoroutine(WaitForCharacterAndFrame());
        }

        private IEnumerator WaitForCharacterAndFrame()
        {
            float timeout = 5f;
            float elapsed = 0f;

            while (_searching && elapsed < timeout)
            {
                GameObject avatar = GameObject.Find(_avatarName);
                if (avatar != null)
                {
                    Transform character = avatar.transform.Find(_characterName);
                    if (character != null)
                    {
                        // Wait one frame for renderers to initialize
                        yield return null;
                        _character = character;
                        FrameCharacter();
                        _searching = false;
                        yield break;
                    }
                }

                elapsed += 0.1f;
                yield return new WaitForSeconds(0.1f);
            }

            if (_searching)
            {
                Debug.LogWarning($"[PortraitFramer] Could not find {_avatarName}/{_characterName}");
            }
        }

        /// <summary>
        /// Calculate and apply camera framing based on character bounds.
        /// </summary>
        public void FrameCharacter()
        {
            if (_character == null || _camera == null) return;

            // Get bounds of all renderers
            Bounds bounds = CalculateBounds(_character);
            if (bounds.size == Vector3.zero)
            {
                Debug.LogWarning("[PortraitFramer] Character has no renderer bounds");
                return;
            }

            // Set FOV
            _camera.fieldOfView = _portraitFOV;

            // Calculate target point (upper portion of character for portrait)
            Vector3 targetPoint = bounds.center;
            float characterHeight = bounds.size.y;

            // Bias toward upper body/face
            targetPoint.y = bounds.center.y + (characterHeight * _verticalBias);

            // Calculate how much vertical space we want the character to occupy
            // Account for UI reserve at bottom
            float effectiveScreenHeight = 1f - _bottomUIReserve;
            float desiredVisibleHeight = characterHeight * _verticalFill;

            // Calculate distance using FOV geometry
            float halfFOVRad = (_camera.fieldOfView * 0.5f) * Mathf.Deg2Rad;
            float distance = (desiredVisibleHeight * 0.5f) / Mathf.Tan(halfFOVRad);

            // Clamp distance
            distance = Mathf.Clamp(distance, _minDistance, _maxDistance);

            // Position camera in front of character (character faces -Z after 180° rotation)
            Vector3 cameraPos = targetPoint + Vector3.back * distance;

            // Adjust vertical position to account for UI reserve
            // Move target point up slightly so character appears in upper portion
            float verticalOffset = (bounds.size.y * _bottomUIReserve * 0.5f);
            targetPoint.y += verticalOffset;
            cameraPos.y = targetPoint.y;

            // Apply
            transform.position = cameraPos;
            transform.LookAt(targetPoint);

            _framed = true;
            Debug.Log($"[PortraitFramer] Framed character - Distance: {distance:F2}m, FOV: {_portraitFOV}°, Bounds: {bounds.size}");
        }

        private Bounds CalculateBounds(Transform root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                return new Bounds(root.position, Vector3.zero);
            }

            // Start with first valid renderer
            Bounds bounds = default;
            bool initialized = false;

            foreach (Renderer r in renderers)
            {
                if (r.bounds.size.magnitude < 0.001f) continue; // Skip zero-size

                if (!initialized)
                {
                    bounds = r.bounds;
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }

            return bounds;
        }

        /// <summary>
        /// Re-frame (call after character changes).
        /// </summary>
        public void Reframe()
        {
            if (_character != null)
            {
                FrameCharacter();
            }
            else
            {
                StartCoroutine(WaitForCharacterAndFrame());
            }
        }

#if UNITY_EDITOR
        [ContextMenu("Frame Now (Editor)")]
        private void FrameNowEditor()
        {
            _camera = GetComponent<Camera>();
            GameObject avatar = GameObject.Find(_avatarName);
            if (avatar != null)
            {
                _character = avatar.transform.Find(_characterName);
                if (_character != null)
                {
                    FrameCharacter();
                    UnityEditor.EditorUtility.SetDirty(transform);
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (_character != null)
            {
                Bounds bounds = CalculateBounds(_character);
                Gizmos.color = Color.green;
                Gizmos.DrawWireCube(bounds.center, bounds.size);

                // Draw target point
                Vector3 target = bounds.center;
                target.y = bounds.center.y + (bounds.size.y * _verticalBias);
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(target, 0.05f);
            }
        }
#endif
    }
}
