using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Guards scene/prefab wiring while scripts move between assemblies and namespaces.
// Uses only SerializedObject data and runtime type names, so it does not depend on
// which assembly the gameplay code lives in.
public class SceneIntegrityTests
{
    // Inspector fields that must be assigned, keyed by component class name (without namespace).
    // A field listed with alternatives ("a|b") passes if whichever one exists is assigned,
    // so renames done with [FormerlySerializedAs] keep the test valid.
    private static readonly Dictionary<string, string[]> RequiredFields = new Dictionary<string, string[]>
    {
        { "GameManager", new[] { "winningMsg", "loosingMsg", "enemyManager" } },
        { "EnemyManager", new[] { "playerHealth", "manager", "waves", "soldierPaths", "vehiclePaths|vehiclekPaths", "planePaths" } },
        { "MoneyScript", new[] { "moneyText", "goldPrefab", "silverPrefab", "cooperPrefab", "coinAudio" } },
        { "PlayerHealth", new[] { "healthText", "damageImage" } },
        { "BuildSpotScript", new[] { "manager", "money", "laserUpgradePanel" } },
        { "LaserTowerScript", new[] { "definition" } },
        { "LaserUpgradePanel", new[] { "title", "statsText", "upgradeText", "upgradeButton" } },
        { "Loading", new[] { "text" } },
    };

    // Pre-existing problems inherited from the original project, kept visible here so
    // any NEW broken reference still fails. Remove an entry once it is fixed.
    private static readonly HashSet<string> KnownIssues = new HashSet<string>
    {
        // The tank engine clip (guid 09a78845...) was never committed to the original repo.
        "Tank.prefab/Tank: AudioSource.m_Resource points to a missing object",
    };

    private static IEnumerable<string> BuildScenes()
    {
        return EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path);
    }

    private static IEnumerable<string> Prefabs()
    {
        return AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p);
    }

    [Test]
    public void BuildSettings_ListsMenuThenLevelsInOrder()
    {
        Assert.That(BuildScenes().Select(System.IO.Path.GetFileNameWithoutExtension).ToArray(),
            Is.EqualTo(new[] { "MainMenu", "Level01", "Level02", "Level03" }));
    }

    [TestCaseSource(nameof(BuildScenes))]
    public void Scene_HasNoBrokenWiring(string scenePath)
    {
        // Single mode: additive opening next to the Test Runner's untitled scene makes
        // Unity log EnsureUntitledSceneHasBeenSaved errors on later cases.
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        try
        {
            var problems = new List<string>();
            var components = scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<Component>(true)).Where(c => c != null).ToList();

            // Only the EnemyManager that a GameManager actually uses must be configured.
            // GameManager's [RequireComponent(EnemyManager)] also adds an unused, empty one.
            var usedEnemyManagers = new HashSet<UnityEngine.Object>(components
                .Where(c => c.GetType().Name == "GameManager")
                .Select(c => new SerializedObject(c).FindProperty("enemyManager")?.objectReferenceValue)
                .Where(o => o != null));

            foreach (var root in scene.GetRootGameObjects())
                CheckHierarchy(root, scene.name, problems, c => c.GetType().Name != "EnemyManager" || usedEnemyManagers.Contains(c));

            if (scene.name.StartsWith("Level"))
            {
                Assert.That(components.Count(c => c.GetType().Name == "GameManager"), Is.EqualTo(1), $"{scene.name}: exactly one GameManager");
                Assert.That(usedEnemyManagers.Count, Is.EqualTo(1), $"{scene.name}: GameManager.enemyManager is assigned");
                Assert.That(components.Count(c => c.GetType().Name == "BuildSpotScript"), Is.GreaterThan(0), $"{scene.name}: build spots");
            }

            Assert.That(problems.Except(KnownIssues), Is.Empty, string.Join("\n", problems));
        }
        finally
        {
            // Never save: the scene was only inspected. Leave an empty scene behind.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }

    [TestCaseSource(nameof(Prefabs))]
    public void Prefab_HasNoBrokenWiring(string prefabPath)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        Assert.That(prefab, Is.Not.Null, prefabPath);
        var problems = new List<string>();
        // Prefab fields such as BuildSpotScript.manager are filled on the scene instances,
        // so required-field checks only apply to scenes.
        CheckHierarchy(prefab, System.IO.Path.GetFileName(prefabPath), problems, _ => false);
        Assert.That(problems.Except(KnownIssues), Is.Empty, string.Join("\n", problems));
    }

    private static void CheckHierarchy(GameObject root, string context, List<string> problems, Func<Component, bool> checkRequired)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            var go = t.gameObject;
            int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
            if (missing > 0) problems.Add($"{context}/{PathOf(t)}: {missing} missing script(s)");
            foreach (var component in go.GetComponents<Component>())
            {
                if (component == null) continue;
                CheckComponent(component, $"{context}/{PathOf(t)}", problems, checkRequired(component));
            }
        }
    }

    private static void CheckComponent(Component component, string context, List<string> problems, bool checkRequired)
    {
        var typeName = component.GetType().Name;
        var so = new SerializedObject(component);
        var it = so.GetIterator();
        while (it.Next(true))
        {
            if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue == null)
            {
                var fileId = it.FindPropertyRelative("m_FileID");
                if (fileId != null && fileId.longValue != 0)
                    problems.Add($"{context}: {typeName}.{it.propertyPath} points to a missing object");
            }
            if (it.isArray && it.name == "m_Calls" && it.propertyPath.EndsWith("m_PersistentCalls.m_Calls"))
                CheckPersistentCalls(it.Copy(), $"{context}: {typeName}.{it.propertyPath}", problems);
        }

        if (checkRequired && RequiredFields.TryGetValue(typeName, out var fields))
        {
            foreach (var spec in fields)
            {
                var prop = spec.Split('|').Select(so.FindProperty).FirstOrDefault(p => p != null);
                if (prop == null) { problems.Add($"{context}: {typeName} has no field '{spec}' (update RequiredFields)"); continue; }
                if (prop.isArray && prop.propertyType == SerializedPropertyType.Generic)
                {
                    if (prop.arraySize == 0) problems.Add($"{context}: {typeName}.{prop.name} is empty");
                    for (int i = 0; i < prop.arraySize; i++)
                        if (prop.GetArrayElementAtIndex(i).objectReferenceValue == null)
                            problems.Add($"{context}: {typeName}.{prop.name}[{i}] is not assigned");
                }
                else if (prop.propertyType == SerializedPropertyType.ObjectReference && prop.objectReferenceValue == null)
                {
                    problems.Add($"{context}: {typeName}.{prop.name} is not assigned");
                }
            }
        }
    }

    private static void CheckPersistentCalls(SerializedProperty calls, string context, List<string> problems)
    {
        for (int i = 0; i < calls.arraySize; i++)
        {
            var call = calls.GetArrayElementAtIndex(i);
            var target = call.FindPropertyRelative("m_Target").objectReferenceValue;
            var method = call.FindPropertyRelative("m_MethodName").stringValue;
            var storedType = call.FindPropertyRelative("m_TargetAssemblyTypeName")?.stringValue;
            var where = $"{context}[{i}] {method}";

            if (target == null) { problems.Add($"{where}: target is missing"); continue; }
            if (string.IsNullOrEmpty(method)) { problems.Add($"{where}: no method name"); continue; }

            var targetType = target.GetType();
            bool hasMethod = targetType
                .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy)
                .Any(m => m.Name == method);
            if (!hasMethod) problems.Add($"{where}: {targetType.FullName} has no method '{method}'");

            if (!string.IsNullOrEmpty(storedType))
            {
                var resolved = Type.GetType(storedType, false);
                if (resolved == null)
                    problems.Add($"{where}: stored type '{storedType}' does not resolve (re-save needed)");
                else if (!resolved.IsAssignableFrom(targetType))
                    problems.Add($"{where}: stored type '{storedType}' does not match target {targetType.FullName}");
            }
        }
    }

    private static string PathOf(Transform t)
    {
        return t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;
    }
}
