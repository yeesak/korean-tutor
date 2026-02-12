using UnityEngine;

namespace ShadowingTutor
{
    /// <summary>
    /// Locks the camera FOV to a specific value every frame.
    /// Prevents any other scripts from overriding the FOV.
    /// This is a FRONTEND-ONLY component for camera rendering control.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [ExecuteAlways] // Works in both Edit and Play mode
    public class CameraFOVLock : MonoBehaviour
    {
        [Header("FOV Lock Settings")]
        [Tooltip("The exact FOV value to maintain")]
        public float targetFOV = 14.69837f;

        [Tooltip("Enable/disable the FOV lock")]
        public bool lockEnabled = true;

        private Camera _camera;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        private void OnEnable()
        {
            if (_camera == null)
                _camera = GetComponent<Camera>();

            // Apply immediately on enable
            if (lockEnabled && _camera != null)
            {
                _camera.fieldOfView = targetFOV;
            }
        }

        private void LateUpdate()
        {
            // LateUpdate ensures we override any other scripts that modify FOV in Update
            if (lockEnabled && _camera != null)
            {
                if (_camera.fieldOfView != targetFOV)
                {
                    _camera.fieldOfView = targetFOV;
                }
            }
        }

        // Also lock in OnPreRender for extra safety
        private void OnPreRender()
        {
            if (lockEnabled && _camera != null)
            {
                if (_camera.fieldOfView != targetFOV)
                {
                    _camera.fieldOfView = targetFOV;
                }
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Apply in editor when values change
            if (_camera == null)
                _camera = GetComponent<Camera>();

            if (lockEnabled && _camera != null)
            {
                _camera.fieldOfView = targetFOV;
            }
        }

        private void Reset()
        {
            // Default values when component is first added
            targetFOV = 14.69837f;
            lockEnabled = true;
        }
#endif
    }
}
