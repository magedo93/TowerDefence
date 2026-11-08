using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

// Replaces the legacy StandaloneInputModule with InputSystemUIInputModule in every
// Build Settings scene. It does the same thing as the Inspector's "Replace with
// InputSystemUIInputModule" button. Idempotent: scenes already migrated are untouched.
// Batch: Unity.exe -batchmode -quit -projectPath . -executeMethod InputSystemMigration.MigrateBuildScenes
public static class InputSystemMigration
{
    [MenuItem("Tools/Tower Defence/Migrate UI Input Modules to Input System")]
    public static void MigrateBuildScenes()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        int replaced = 0;
        foreach (var buildScene in EditorBuildSettings.scenes)
        {
            if (!buildScene.enabled) continue;
            var scene = EditorSceneManager.OpenScene(buildScene.path, OpenSceneMode.Single);
            int inScene = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var legacy in root.GetComponentsInChildren<StandaloneInputModule>(true))
                {
                    var go = legacy.gameObject;
                    UnityEngine.Object.DestroyImmediate(legacy);
                    var module = go.GetComponent<InputSystemUIInputModule>();
                    if (module == null) module = go.AddComponent<InputSystemUIInputModule>();
                    if (module.actionsAsset == null) module.AssignDefaultActions();
                    inScene++;
                }
            }

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var eventSystem in root.GetComponentsInChildren<EventSystem>(true))
                {
                    var module = eventSystem.GetComponent<InputSystemUIInputModule>();
                    if (module == null)
                        throw new InvalidOperationException($"[InputSystemMigration] EventSystem '{eventSystem.name}' in {buildScene.path} has no InputSystemUIInputModule.");
                    if (module.actionsAsset == null)
                        throw new InvalidOperationException($"[InputSystemMigration] InputSystemUIInputModule in {buildScene.path} has no actions asset.");
                }
            }

            if (inScene > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene))
                    throw new InvalidOperationException($"[InputSystemMigration] Failed to save {buildScene.path}.");
            }
            Debug.Log($"[InputSystemMigration] {buildScene.path}: replaced {inScene} module(s).");
            replaced += inScene;
        }
        Debug.Log($"[InputSystemMigration] Done. Replaced {replaced} StandaloneInputModule(s).");
    }
}
