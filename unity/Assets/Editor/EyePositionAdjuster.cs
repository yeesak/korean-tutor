using UnityEngine;
using UnityEditor;
using System.IO;

namespace ShadowingTutor.Editor
{
    /// <summary>
    /// Adjusts eye position via bone rotation or texture offset.
    /// Frontend-only modification (prefab/material).
    /// </summary>
    public class EyePositionAdjuster : EditorWindow
    {
        private const string PREFAB_PATH = "Assets/Art/Characters/Edumeta_CharacterGirl_AAA/Prefabs/Edumeta_CharacterGirl_AAA_Stand_Talk.prefab";
        private const string MATERIALS_PATH = "Assets/Art/Characters/Edumeta_CharacterGirl_AAA/Materials_Clean";

        // Eye bone rotation adjustments
        private Vector3 _leftEyeRotation = Vector3.zero;
        private Vector3 _rightEyeRotation = Vector3.zero;

        // Texture offset adjustments
        private Vector2 _leftEyeTexOffset = Vector2.zero;
        private Vector2 _rightEyeTexOffset = Vector2.zero;

        // Loaded materials
        private Material _leftEyeMat;
        private Material _rightEyeMat;

        [MenuItem("Tools/Eye Doctor/Eye Position Adjuster Window")]
        public static void ShowWindow()
        {
            var window = GetWindow<EyePositionAdjuster>("Eye Position Adjuster");
            window.minSize = new Vector2(350, 400);
        }

        private void OnEnable()
        {
            LoadMaterials();
            LoadCurrentTextureOffsets();
        }

        private void LoadMaterials()
        {
            _leftEyeMat = AssetDatabase.LoadAssetAtPath<Material>($"{MATERIALS_PATH}/Std_Eye_L_Diffuse.mat");
            _rightEyeMat = AssetDatabase.LoadAssetAtPath<Material>($"{MATERIALS_PATH}/Std_Eye_R_Diffuse.mat");
        }

        private void LoadCurrentTextureOffsets()
        {
            if (_leftEyeMat != null && _leftEyeMat.HasProperty("_MainTex"))
            {
                _leftEyeTexOffset = _leftEyeMat.GetTextureOffset("_MainTex");
            }
            if (_rightEyeMat != null && _rightEyeMat.HasProperty("_MainTex"))
            {
                _rightEyeTexOffset = _rightEyeMat.GetTextureOffset("_MainTex");
            }
        }

        private void OnGUI()
        {
            GUILayout.Label("Eye Position Adjuster", EditorStyles.boldLabel);
            GUILayout.Label("Frontend-only: Modifies prefab bones or material texture offsets", EditorStyles.miniLabel);
            EditorGUILayout.Space(10);

            // ===== TEXTURE OFFSET SECTION =====
            EditorGUILayout.LabelField("Texture Offset (UV)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Use this if the iris/pupil is off-center due to UV mapping.\n" +
                "Small values like 0.01-0.05 are typical.",
                MessageType.Info);

            EditorGUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Left Eye Offset", GUILayout.Width(100));
            _leftEyeTexOffset = EditorGUILayout.Vector2Field("", _leftEyeTexOffset);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Right Eye Offset", GUILayout.Width(100));
            _rightEyeTexOffset = EditorGUILayout.Vector2Field("", _rightEyeTexOffset);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            if (GUILayout.Button("Apply Texture Offsets"))
            {
                ApplyTextureOffsets();
            }

            if (GUILayout.Button("Reset Texture Offsets to Zero"))
            {
                _leftEyeTexOffset = Vector2.zero;
                _rightEyeTexOffset = Vector2.zero;
                ApplyTextureOffsets();
            }

            EditorGUILayout.Space(20);

            // ===== EYE BONE ROTATION SECTION =====
            EditorGUILayout.LabelField("Eye Bone Rotation (Prefab)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Use this if the eye is looking in wrong direction.\n" +
                "Modifies L_Eye and R_Eye bone local rotation in prefab.",
                MessageType.Info);

            EditorGUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Left Eye Rot", GUILayout.Width(100));
            _leftEyeRotation = EditorGUILayout.Vector3Field("", _leftEyeRotation);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Right Eye Rot", GUILayout.Width(100));
            _rightEyeRotation = EditorGUILayout.Vector3Field("", _rightEyeRotation);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            if (GUILayout.Button("Apply Eye Bone Rotations to Prefab"))
            {
                ApplyEyeBoneRotations();
            }

            if (GUILayout.Button("Reset Eye Bone Rotations to Zero"))
            {
                _leftEyeRotation = Vector3.zero;
                _rightEyeRotation = Vector3.zero;
                ApplyEyeBoneRotations();
            }

            EditorGUILayout.Space(20);

            // ===== QUICK PRESETS =====
            EditorGUILayout.LabelField("Quick Presets", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Center Eyes\n(Reset All)"))
            {
                _leftEyeTexOffset = Vector2.zero;
                _rightEyeTexOffset = Vector2.zero;
                _leftEyeRotation = Vector3.zero;
                _rightEyeRotation = Vector3.zero;
                ApplyTextureOffsets();
                ApplyEyeBoneRotations();
            }

            if (GUILayout.Button("Look Slightly Up"))
            {
                _leftEyeRotation = new Vector3(-5, 0, 0);
                _rightEyeRotation = new Vector3(-5, 0, 0);
                ApplyEyeBoneRotations();
            }

            if (GUILayout.Button("Look Slightly Down"))
            {
                _leftEyeRotation = new Vector3(5, 0, 0);
                _rightEyeRotation = new Vector3(5, 0, 0);
                ApplyEyeBoneRotations();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void ApplyTextureOffsets()
        {
            if (_leftEyeMat == null || _rightEyeMat == null)
            {
                LoadMaterials();
            }

            if (_leftEyeMat != null && _leftEyeMat.HasProperty("_MainTex"))
            {
                _leftEyeMat.SetTextureOffset("_MainTex", _leftEyeTexOffset);
                EditorUtility.SetDirty(_leftEyeMat);
                Debug.Log($"Left eye texture offset set to: {_leftEyeTexOffset}");
            }

            if (_rightEyeMat != null && _rightEyeMat.HasProperty("_MainTex"))
            {
                _rightEyeMat.SetTextureOffset("_MainTex", _rightEyeTexOffset);
                EditorUtility.SetDirty(_rightEyeMat);
                Debug.Log($"Right eye texture offset set to: {_rightEyeTexOffset}");
            }

            AssetDatabase.SaveAssets();
            Debug.Log("Texture offsets applied to materials.");
        }

        private void ApplyEyeBoneRotations()
        {
            if (!File.Exists(PREFAB_PATH))
            {
                Debug.LogError($"Prefab not found: {PREFAB_PATH}");
                return;
            }

            GameObject prefabContents = PrefabUtility.LoadPrefabContents(PREFAB_PATH);
            if (prefabContents == null)
            {
                Debug.LogError("Failed to load prefab!");
                return;
            }

            // Find eye bones
            Transform leftEye = FindBoneRecursive(prefabContents.transform, "L_Eye");
            Transform rightEye = FindBoneRecursive(prefabContents.transform, "R_Eye");

            if (leftEye != null)
            {
                leftEye.localEulerAngles = _leftEyeRotation;
                Debug.Log($"Left eye bone rotation set to: {_leftEyeRotation}");
            }
            else
            {
                Debug.LogWarning("L_Eye bone not found in prefab!");
            }

            if (rightEye != null)
            {
                rightEye.localEulerAngles = _rightEyeRotation;
                Debug.Log($"Right eye bone rotation set to: {_rightEyeRotation}");
            }
            else
            {
                Debug.LogWarning("R_Eye bone not found in prefab!");
            }

            PrefabUtility.SaveAsPrefabAsset(prefabContents, PREFAB_PATH);
            PrefabUtility.UnloadPrefabContents(prefabContents);

            AssetDatabase.SaveAssets();
            Debug.Log("Eye bone rotations applied to prefab.");
        }

        private Transform FindBoneRecursive(Transform parent, string boneName)
        {
            if (parent.name == boneName || parent.name.Contains(boneName))
                return parent;

            foreach (Transform child in parent)
            {
                Transform found = FindBoneRecursive(child, boneName);
                if (found != null)
                    return found;
            }

            return null;
        }
    }
}
