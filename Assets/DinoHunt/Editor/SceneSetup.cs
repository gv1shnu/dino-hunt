using System.IO;
using DinoHunt.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DinoHunt.Editor
{
    /// <summary>
    /// One-click scene setup. Creates a fresh scene containing a single Bootstrap object,
    /// saves it to Assets/DinoHunt/Scenes/Arena.unity, and adds it to the build settings.
    /// The greybox and camera are built at runtime by Bootstrap, so the saved scene stays
    /// minimal (no hand-placed primitives to drift out of sync with the layout data).
    /// </summary>
    public static class SceneSetup
    {
        private const string ScenePath = "Assets/DinoHunt/Scenes/Arena.unity";

        [MenuItem("DinoHunt/Setup Arena Scene")]
        public static void SetupArenaScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var bootstrapGo = new GameObject("Bootstrap");
            bootstrapGo.AddComponent<Bootstrap>();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);

            AddSceneToBuildSettings(ScenePath);

            Debug.Log($"[DinoHunt] Arena scene created at {ScenePath}. Press Play to run the greybox.");
            EditorUtility.DisplayDialog("Dino Hunt",
                "Arena scene created and opened.\n\nPress Play to build the greybox and start the simulation.",
                "OK");
        }

        private static void AddSceneToBuildSettings(string path)
        {
            var scenes = EditorBuildSettings.scenes;
            foreach (var s in scenes)
                if (s.path == path) return;

            var updated = new EditorBuildSettingsScene[scenes.Length + 1];
            scenes.CopyTo(updated, 0);
            updated[scenes.Length] = new EditorBuildSettingsScene(path, true);
            EditorBuildSettings.scenes = updated;
        }
    }
}
