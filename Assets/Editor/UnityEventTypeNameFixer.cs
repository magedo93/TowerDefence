using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// UnityEvent hookups (Button.onClick, EventTrigger, ...) store the target's type as
// "Namespace.Type, Assembly". Moving scripts into an assembly definition or a namespace
// leaves those strings stale. This rewrites stale names from each call's actual target,
// in every Build Settings scene and every prefab under Assets/Prefabs. Idempotent.
// Values inherited from a prefab are fixed in the prefab asset, never as scene overrides.
// Batch: Unity.exe -batchmode -quit -projectPath . -executeMethod UnityEventTypeNameFixer.FixAll
public static class UnityEventTypeNameFixer
{
    [MenuItem("Tools/Tower Defence/Fix UnityEvent Type Names")]
    public static void FixAll()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        int total = 0;

        foreach (var path in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }).Select(AssetDatabase.GUIDToAssetPath))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            int fixedHere = prefab.GetComponentsInChildren<Component>(true).Where(c => c != null).Sum(c => Fix(c, path));
            if (fixedHere > 0) { EditorUtility.SetDirty(prefab); Debug.Log($"[UnityEventTypeNameFixer] {path}: fixed {fixedHere}"); }
            total += fixedHere;
        }
        AssetDatabase.SaveAssets();

        foreach (var buildScene in EditorBuildSettings.scenes.Where(s => s.enabled))
        {
            var scene = EditorSceneManager.OpenScene(buildScene.path, OpenSceneMode.Single);
            int fixedHere = scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<Component>(true)).Where(c => c != null)
                .Sum(c => Fix(c, buildScene.path));
            if (fixedHere > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException($"Failed to save {buildScene.path}");
                Debug.Log($"[UnityEventTypeNameFixer] {buildScene.path}: fixed {fixedHere}");
            }
            total += fixedHere;
        }
        Debug.Log($"[UnityEventTypeNameFixer] Done. Fixed {total} stale type name(s).");
    }

    private static int Fix(Component component, string context)
    {
        bool inPrefabInstance = PrefabUtility.IsPartOfPrefabInstance(component);
        var so = new SerializedObject(component);
        int count = 0;
        var it = so.GetIterator();
        while (it.Next(true))
        {
            if (!it.isArray || it.name != "m_Calls" || !it.propertyPath.EndsWith("m_PersistentCalls.m_Calls")) continue;
            for (int i = 0; i < it.arraySize; i++)
            {
                var call = it.GetArrayElementAtIndex(i);
                var target = call.FindPropertyRelative("m_Target").objectReferenceValue;
                var typeName = call.FindPropertyRelative("m_TargetAssemblyTypeName");
                if (target == null || typeName == null || string.IsNullOrEmpty(typeName.stringValue)) continue;

                var type = target.GetType();
                var resolved = Type.GetType(typeName.stringValue, false);
                if (resolved != null && resolved.IsAssignableFrom(type)) continue;
                if (inPrefabInstance && !typeName.prefabOverride) continue; // fixed in the prefab asset

                var fresh = type.FullName + ", " + type.Assembly.GetName().Name;
                Debug.Log($"[UnityEventTypeNameFixer] {context}: {component.name}.{it.propertyPath}[{i}] '{typeName.stringValue}' -> '{fresh}'");
                typeName.stringValue = fresh;
                count++;
            }
        }
        if (count > 0) so.ApplyModifiedPropertiesWithoutUndo();
        return count;
    }
}
