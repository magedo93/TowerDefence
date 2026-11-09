using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Guards the URP (2D Renderer) migration: the pipeline stays assigned, nothing renders with
// Built-in-only shaders/materials, and every scene has the global light that lit sprites need.
public class RenderPipelineTests
{
    private static IEnumerable<string> BuildScenes()
    {
        return EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path);
    }

    [Test]
    public void Urp2D_IsTheActivePipelineEverywhere()
    {
        var pipeline = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
        Assert.That(pipeline, Is.Not.Null, "Graphics settings must use a URP asset");
        Assert.That(AssetDatabase.GetAssetPath(pipeline), Is.EqualTo("Assets/Settings/Rendering/URP-2D.asset"));
        Assert.That(pipeline.scriptableRenderer, Is.Not.Null);
        var rendererData = new SerializedObject(pipeline).FindProperty("m_RendererDataList").GetArrayElementAtIndex(0).objectReferenceValue;
        Assert.That(rendererData, Is.InstanceOf<Renderer2DData>(), "the 2D Renderer");

        int current = QualitySettings.GetQualityLevel();
        try
        {
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                var levelPipeline = QualitySettings.renderPipeline;
                Assert.That(levelPipeline == null || levelPipeline == pipeline, Is.True, $"quality level '{QualitySettings.names[i]}' overrides the pipeline");
            }
        }
        finally { QualitySettings.SetQualityLevel(current, false); }
    }

    [Test]
    public void ProjectMaterials_UseUrpCompatibleShaders()
    {
        var bad = new List<string>();
        foreach (var path in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath))
        {
            foreach (var material in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
            {
                var shader = material.shader != null ? material.shader.name : "<none>";
                bool ok = shader.StartsWith("Universal Render Pipeline/") || shader.StartsWith("TextMeshPro/") ||
                          shader == "GUI/Text Shader"; // auto-generated font materials, not used for rendering the game
                if (!ok) bad.Add($"{path} ({material.name}): {shader}");
            }
        }
        Assert.That(bad, Is.Empty, string.Join("\n", bad));
    }

    [Test]
    public void PrefabRenderers_DoNotUseBuiltinSpriteMaterial()
    {
        var bad = new List<string>();
        foreach (var path in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath))
            foreach (var renderer in AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<Renderer>(true))
                if (renderer.sharedMaterials.Any(IsBuiltinSpriteDefault)) bad.Add($"{path}/{renderer.name}");
        Assert.That(bad, Is.Empty, "Built-in Sprites-Default still used by:\n" + string.Join("\n", bad));
    }

    [TestCaseSource(nameof(BuildScenes))]
    public void Scene_HasOneGlobalLightAndNoBuiltinSpriteMaterial(string scenePath)
    {
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        try
        {
            var roots = scene.GetRootGameObjects();
            var globals = roots.SelectMany(r => r.GetComponentsInChildren<Light2D>(true)).Where(l => l.lightType == Light2D.LightType.Global).ToList();
            Assert.That(globals.Count, Is.EqualTo(1), "exactly one Global Light 2D");
            Assert.That(globals[0].intensity, Is.EqualTo(1f));
            Assert.That(globals[0].color, Is.EqualTo(Color.white));

            var bad = roots.SelectMany(r => r.GetComponentsInChildren<Renderer>(true))
                .Where(r => r.sharedMaterials.Any(IsBuiltinSpriteDefault)).Select(r => r.name).ToList();
            Assert.That(bad, Is.Empty, "Built-in Sprites-Default still used by: " + string.Join(", ", bad));
        }
        finally
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }

    private static bool IsBuiltinSpriteDefault(Material material)
    {
        return material != null && material.name == "Sprites-Default" &&
               AssetDatabase.GetAssetPath(material) == "Resources/unity_builtin_extra";
    }
}
