using UnityEngine;

namespace ShadowingTutor
{
    /// <summary>
    /// Build-safe character prefab loader.
    /// Attach this to the Avatar GameObject and assign the character prefab in Inspector.
    /// This injects the prefab reference before SceneCharacterInitializer runs.
    /// </summary>
    [DefaultExecutionOrder(-100)] // Run before other scripts
    public class RuntimeCharacterLoader : MonoBehaviour
    {
        [Header("Character Prefab (Required for Builds)")]
        [Tooltip("Assign: Edumeta_CharacterGirl_AAA_Stand_Talk.prefab")]
        [SerializeField] private GameObject _characterPrefab;

        private void Awake()
        {
            // Inject prefab reference for SceneCharacterInitializer
            if (_characterPrefab != null)
            {
                SceneCharacterInitializer.InjectedPrefab = _characterPrefab;
                Debug.Log("[RuntimeCharacterLoader] Prefab injected for build-safe loading");
            }
            else
            {
                Debug.LogWarning("[RuntimeCharacterLoader] No character prefab assigned! Assign it in Inspector for builds to work.");
            }
        }

        /// <summary>
        /// Get the assigned prefab (for editor tools).
        /// </summary>
        public GameObject CharacterPrefab => _characterPrefab;

#if UNITY_EDITOR
        [ContextMenu("Auto-Assign Prefab")]
        private void AutoAssignPrefab()
        {
            string prefabPath = "Assets/Art/Characters/Edumeta_CharacterGirl_AAA/Prefabs/Edumeta_CharacterGirl_AAA_Stand_Talk.prefab";
            _characterPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (_characterPrefab != null)
            {
                UnityEditor.EditorUtility.SetDirty(this);
                Debug.Log("[RuntimeCharacterLoader] Prefab auto-assigned: " + prefabPath);
            }
            else
            {
                Debug.LogError("[RuntimeCharacterLoader] Failed to find prefab at: " + prefabPath);
            }
        }
#endif
    }
}
