using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TowerDefence.EditorTools
{
    // The game is 2D sprites with no Light components, so baked/realtime GI do nothing
    // but trigger bakes and the "Progressive CPU will be removed" warning. This turns
    // both off in every Build Settings scene, and moves the lightmapper off Progressive CPU
    // so the warning stays gone even if baking is re-enabled later. Idempotent.
    // Batch: Unity.exe -batchmode -quit -projectPath . -executeMethod TowerDefence.EditorTools.LightingCleanup.DisableBakedLightingInBuildScenes
    public static class LightingCleanup
    {
        [MenuItem("Tools/Tower Defence/Disable Baked Lighting in Build Scenes")]
        public static void DisableBakedLightingInBuildScenes()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var lightmapper = PreferredLightmapper();
            Debug.Log($"[LightingCleanup] Lightmapper to use: {lightmapper}");

            foreach (var buildScene in EditorBuildSettings.scenes.Where(s => s.enabled))
            {
                var scene = EditorSceneManager.OpenScene(buildScene.path, OpenSceneMode.Single);

                if (!Lightmapping.TryGetLightingSettings(out var settings) || settings == null)
                {
                    // No settings object at all: create a dedicated asset next to the scene.
                    settings = new LightingSettings { name = scene.name + "Settings" };
                    var assetPath = AssetDatabase.GenerateUniqueAssetPath(
                        System.IO.Path.Combine(System.IO.Path.GetDirectoryName(buildScene.path), scene.name + "Settings.lighting"));
                    AssetDatabase.CreateAsset(settings, assetPath);
                    Lightmapping.lightingSettings = settings;
                    Debug.Log($"[LightingCleanup] {buildScene.path}: created {assetPath}");
                }

                Undo.RecordObject(settings, "Disable baked lighting");
                settings.bakedGI = false;
                settings.realtimeGI = false;
                settings.lightmapper = lightmapper;
                EditorUtility.SetDirty(settings);

                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene))
                    throw new InvalidOperationException($"[LightingCleanup] Failed to save {buildScene.path}.");
                Debug.Log($"[LightingCleanup] {buildScene.path}: bakedGI={settings.bakedGI}, realtimeGI={settings.realtimeGI}, lightmapper={settings.lightmapper}");
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[LightingCleanup] Done.");
        }

        // Pick the replacement by name so this does not depend on enum values or on
        // members that may be marked obsolete in this Unity version.
        private static LightingSettings.Lightmapper PreferredLightmapper()
        {
            var names = Enum.GetNames(typeof(LightingSettings.Lightmapper));
            var pick = names.FirstOrDefault(n => n.IndexOf("Compute", StringComparison.OrdinalIgnoreCase) >= 0)
                       ?? names.FirstOrDefault(n => n.IndexOf("GPU", StringComparison.OrdinalIgnoreCase) >= 0)
                       ?? names.FirstOrDefault(n => n.IndexOf("CPU", StringComparison.OrdinalIgnoreCase) < 0);
            if (pick == null)
                throw new InvalidOperationException("[LightingCleanup] No non-CPU lightmapper found: " + string.Join(", ", names));
            return (LightingSettings.Lightmapper)Enum.Parse(typeof(LightingSettings.Lightmapper), pick);
        }
    }
}
