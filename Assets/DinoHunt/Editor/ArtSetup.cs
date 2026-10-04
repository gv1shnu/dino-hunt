using System.Linq;
using DinoHunt.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DinoHunt.Editor
{
    /// <summary>
    /// One-click wiring of the imported Quaternius models (CC0, via poly.pizza) into the Arena
    /// scene's Bootstrap: picks the clips by name and auto-scales each model so it occupies roughly
    /// the same footprint as the greybox capsule it replaces. Re-runnable after re-importing art.
    /// </summary>
    public static class ArtSetup
    {
        private const string ScenePath = "Assets/DinoHunt/Scenes/Arena.unity";
        private const string SoldierPath = "Assets/DinoHunt/Art/Character_Soldier.glb";
        private const string RaptorPath = "Assets/DinoHunt/Art/Velociraptor.glb";

        [MenuItem("DinoHunt/Setup Art Models")]
        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var bootstrap = Object.FindAnyObjectByType<Bootstrap>();
            if (bootstrap == null)
            {
                Debug.LogError("[DinoHunt] No Bootstrap in Arena scene; run DinoHunt/Setup Arena Scene first.");
                return;
            }

            var so = new SerializedObject(bootstrap);
            float agentScale = so.FindProperty("agentVisualScale").floatValue;
            float raptorScale = so.FindProperty("raptorVisualScale").floatValue;

            // Capsule primitive is 2 units tall at scale 1, so the agent capsule stands 2*scale high.
            Fill(so.FindProperty("soldierModel"), SoldierPath,
                 idle: "Idle", move: "Run_Gun", action: "Idle_Shoot",
                 targetHeight: agentScale * 2f, targetLength: 0f, moveClipSpeed: 20f, tintFilter: "Character_Main");

            // Raptors are long rather than tall: match their nose-to-tail length to ~2.2x the capsule scale.
            Fill(so.FindProperty("raptorModel"), RaptorPath,
                 idle: "Velociraptor_Idle", move: "Velociraptor_Run", action: "Velociraptor_Attack",
                 targetHeight: 0f, targetLength: raptorScale * 2.2f, moveClipSpeed: 30f, tintFilter: "");

            // The soldier FBX bundles every weapon in the kit on its hand bone; keep only the AK.
            SetStrings(so.FindProperty("soldierModel").FindPropertyRelative("hideNodes"),
                       "Revolver", "Sniper", "Revolver_Small", "Pistol", "SMG", "GrenadeLauncher", "ShortCannon",
                       "Shotgun", "Sniper_2", "RocketLauncher", "Shovel", "Knife_1", "Knife_2");

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[DinoHunt] Art models wired into Bootstrap.");
        }

        private static void Fill(SerializedProperty slot, string path, string idle, string move, string action,
                                 float targetHeight, float targetLength, float moveClipSpeed, string tintFilter)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogError($"[DinoHunt] Missing model at {path}");
                return;
            }

            var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().ToArray();
            slot.FindPropertyRelative("prefab").objectReferenceValue = prefab;
            slot.FindPropertyRelative("idle").objectReferenceValue = FindClip(clips, idle);
            slot.FindPropertyRelative("move").objectReferenceValue = FindClip(clips, move);
            slot.FindPropertyRelative("action").objectReferenceValue = FindClip(clips, action);
            slot.FindPropertyRelative("moveClipSpeed").floatValue = moveClipSpeed;
            slot.FindPropertyRelative("tintMaterialFilter").stringValue = tintFilter;

            var size = MeasureBounds(prefab);
            float scale = targetHeight > 0f ? targetHeight / Mathf.Max(0.001f, size.y)
                                            : targetLength / Mathf.Max(0.001f, Mathf.Max(size.x, size.z));
            slot.FindPropertyRelative("scale").floatValue = scale;
            Debug.Log($"[DinoHunt] {path}: bounds={size} scale={scale:F2} clips={clips.Length}");
        }

        private static void SetStrings(SerializedProperty array, params string[] values)
        {
            array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).stringValue = values[i];
        }

        private static AnimationClip FindClip(AnimationClip[] clips, string name)
        {
            // glTF clip names carry the armature prefix, e.g. "CharacterArmature|Idle".
            var clip = clips.FirstOrDefault(c => c.name == name || c.name.EndsWith("|" + name));
            if (clip == null) Debug.LogWarning($"[DinoHunt] Clip '{name}' not found. Available: {string.Join(", ", clips.Select(c => c.name))}");
            return clip;
        }

        private static Vector3 MeasureBounds(GameObject prefab)
        {
            var go = Object.Instantiate(prefab);
            try
            {
                // Skinned body only — bundled props (spare weapons) would inflate the size.
                Renderer[] renderers = go.GetComponentsInChildren<SkinnedMeshRenderer>();
                if (renderers.Length == 0) renderers = go.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) return Vector3.one;
                var b = renderers[0].bounds;
                foreach (var r in renderers) b.Encapsulate(r.bounds);
                return b.size;
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
