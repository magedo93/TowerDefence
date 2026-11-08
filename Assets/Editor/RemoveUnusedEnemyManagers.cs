using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TowerDefence.EditorTools
{
    // GameManager used to [RequireComponent(typeof(EnemyManager))], which silently added an
    // empty, unused EnemyManager next to it in every level (the real one lives on its own
    // object and is assigned to GameManager.enemyManager). This removes such leftovers from
    // the Build Settings scenes: only EnemyManagers that no GameManager references AND that
    // have no waves configured. Anything else is reported and left alone. Idempotent.
    // Batch: Unity.exe -batchmode -quit -projectPath . -executeMethod TowerDefence.EditorTools.RemoveUnusedEnemyManagers.Run
    public static class RemoveUnusedEnemyManagers
    {
        [MenuItem("Tools/Tower Defence/Remove Unused EnemyManagers")]
        public static void Run()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            int removed = 0;

            foreach (var buildScene in EditorBuildSettings.scenes.Where(s => s.enabled))
            {
                var scene = EditorSceneManager.OpenScene(buildScene.path, OpenSceneMode.Single);
                var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Component>(true)).Where(c => c != null).ToList();
                var used = all.OfType<GameManager>().Select(g => g.enemyManager).Where(e => e != null).ToList();
                int removedHere = 0;

                foreach (var enemyManager in all.OfType<EnemyManager>())
                {
                    if (used.Contains(enemyManager)) continue;
                    if (enemyManager.waves != null && enemyManager.waves.Length > 0)
                    {
                        Debug.LogWarning($"[RemoveUnusedEnemyManagers] {buildScene.path}: '{enemyManager.name}' is unreferenced but has waves; left alone.", enemyManager);
                        continue;
                    }
                    Debug.Log($"[RemoveUnusedEnemyManagers] {buildScene.path}: removing empty EnemyManager on '{enemyManager.name}'.");
                    UnityEngine.Object.DestroyImmediate(enemyManager, true);
                    removedHere++;
                }

                if (removedHere > 0)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException($"Failed to save {buildScene.path}");
                }
                removed += removedHere;
            }
            Debug.Log($"[RemoveUnusedEnemyManagers] Done. Removed {removed}.");
        }
    }
}
