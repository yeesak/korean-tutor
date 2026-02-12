using UnityEngine;
using UnityEditor;

namespace ShadowingTutor.Editor
{
    /// <summary>
    /// Adjusts Main Camera framing for character portrait view.
    /// Frontend-only modification (camera settings).
    /// </summary>
    public class CameraFramingAdjuster : EditorWindow
    {
        private Camera _mainCamera;
        private float _fov = 60f;
        private Vector3 _cameraPosition = Vector3.zero;
        private Vector3 _cameraRotation = Vector3.zero;

        [MenuItem("Tools/Eye Doctor/Camera Framing Adjuster")]
        public static void ShowWindow()
        {
            var window = GetWindow<CameraFramingAdjuster>("Camera Framing");
            window.minSize = new Vector2(350, 350);
        }

        private void OnEnable()
        {
            FindMainCamera();
        }

        private void FindMainCamera()
        {
            _mainCamera = Camera.main;
            if (_mainCamera != null)
            {
                _fov = _mainCamera.fieldOfView;
                _cameraPosition = _mainCamera.transform.position;
                _cameraRotation = _mainCamera.transform.eulerAngles;
            }
        }

        private void OnGUI()
        {
            GUILayout.Label("Camera Framing Adjuster", EditorStyles.boldLabel);
            GUILayout.Label("Frontend-only: Modifies Main Camera settings", EditorStyles.miniLabel);
            EditorGUILayout.Space(10);

            if (_mainCamera == null)
            {
                EditorGUILayout.HelpBox("Main Camera not found! Click Refresh.", MessageType.Warning);
                if (GUILayout.Button("Refresh"))
                {
                    FindMainCamera();
                }
                return;
            }

            EditorGUILayout.LabelField($"Camera: {_mainCamera.name}", EditorStyles.boldLabel);
            EditorGUILayout.Space(10);

            // FOV
            EditorGUILayout.LabelField("Field of View", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Lower FOV = more zoom (closer view)\n" +
                "Higher FOV = less zoom (wider view)\n" +
                "Portrait typically uses 30-45 FOV",
                MessageType.Info);

            _fov = EditorGUILayout.Slider("FOV", _fov, 10f, 90f);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("30 (Close)"))
            {
                _fov = 30f;
                ApplyFOV();
            }
            if (GUILayout.Button("45 (Medium)"))
            {
                _fov = 45f;
                ApplyFOV();
            }
            if (GUILayout.Button("60 (Default)"))
            {
                _fov = 60f;
                ApplyFOV();
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Apply FOV"))
            {
                ApplyFOV();
            }

            EditorGUILayout.Space(20);

            // Position
            EditorGUILayout.LabelField("Camera Position", EditorStyles.boldLabel);
            _cameraPosition = EditorGUILayout.Vector3Field("Position", _cameraPosition);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Move Closer (+Z)"))
            {
                _cameraPosition.z += 0.1f;
                ApplyPosition();
            }
            if (GUILayout.Button("Move Away (-Z)"))
            {
                _cameraPosition.z -= 0.1f;
                ApplyPosition();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Move Up (+Y)"))
            {
                _cameraPosition.y += 0.05f;
                ApplyPosition();
            }
            if (GUILayout.Button("Move Down (-Y)"))
            {
                _cameraPosition.y -= 0.05f;
                ApplyPosition();
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Apply Position"))
            {
                ApplyPosition();
            }

            EditorGUILayout.Space(20);

            // Rotation
            EditorGUILayout.LabelField("Camera Rotation", EditorStyles.boldLabel);
            _cameraRotation = EditorGUILayout.Vector3Field("Rotation", _cameraRotation);

            if (GUILayout.Button("Apply Rotation"))
            {
                ApplyRotation();
            }

            EditorGUILayout.Space(20);

            // Presets
            EditorGUILayout.LabelField("Quick Presets", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Portrait Close-up"))
            {
                _fov = 35f;
                _cameraPosition = new Vector3(0f, 1.5f, -1.5f);
                _cameraRotation = new Vector3(0f, 0f, 0f);
                ApplyAll();
            }
            if (GUILayout.Button("Half Body"))
            {
                _fov = 45f;
                _cameraPosition = new Vector3(0f, 1.2f, -2.0f);
                _cameraRotation = new Vector3(0f, 0f, 0f);
                ApplyAll();
            }
            if (GUILayout.Button("Full Body"))
            {
                _fov = 60f;
                _cameraPosition = new Vector3(0f, 1.0f, -3.0f);
                _cameraRotation = new Vector3(0f, 0f, 0f);
                ApplyAll();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            if (GUILayout.Button("Refresh from Camera"))
            {
                FindMainCamera();
            }
        }

        private void ApplyFOV()
        {
            if (_mainCamera != null)
            {
                Undo.RecordObject(_mainCamera, "Change Camera FOV");
                _mainCamera.fieldOfView = _fov;
                EditorUtility.SetDirty(_mainCamera);
                Debug.Log($"Camera FOV set to: {_fov}");
            }
        }

        private void ApplyPosition()
        {
            if (_mainCamera != null)
            {
                Undo.RecordObject(_mainCamera.transform, "Change Camera Position");
                _mainCamera.transform.position = _cameraPosition;
                EditorUtility.SetDirty(_mainCamera.transform);
                Debug.Log($"Camera position set to: {_cameraPosition}");
            }
        }

        private void ApplyRotation()
        {
            if (_mainCamera != null)
            {
                Undo.RecordObject(_mainCamera.transform, "Change Camera Rotation");
                _mainCamera.transform.eulerAngles = _cameraRotation;
                EditorUtility.SetDirty(_mainCamera.transform);
                Debug.Log($"Camera rotation set to: {_cameraRotation}");
            }
        }

        private void ApplyAll()
        {
            ApplyFOV();
            ApplyPosition();
            ApplyRotation();
        }
    }
}
