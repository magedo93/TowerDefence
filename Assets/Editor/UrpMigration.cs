using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TowerDefence.EditorTools
{
    // Moves the project from the Built-in Render Pipeline to URP with the 2D Renderer,
    // keeping the look identical (checked with RenderSnapshots before/after). Idempotent:
    //  1. Creates Assets/Settings/Rendering/URP-2D-Renderer.asset + URP-2D.asset (as Unity's
    //     own "Create > Rendering > URP 2D Renderer" menu does) and assigns the pipeline to
    //     Graphics settings and every Quality level.
    //  2. Converts "Legacy Shaders/Particles/*" materials to URP Particles/Unlit with the same
    //     texture, blend mode and tint (legacy shaders double the tint; URP does not).
    //  3. Converts materials on the built-in "Sprites/Default" shader (laser beam) to URP's
    //     unlit sprite shader.
    //  4. Points every renderer using the built-in Sprites-Default material (sprites,
    //     tilemaps) at URP's Sprite-Lit-Default, in prefabs and Build Settings scenes.
    //     Prefab-owned values are changed in the prefab, never as scene overrides.
    //  5. Adds one white Global Light 2D per Build Settings scene, so lit sprites look as before.
    // Batch: -executeMethod TowerDefence.EditorTools.UrpMigration.Run
    public static class UrpMigration
    {
        public const string Folder = "Assets/Settings/Rendering";
        public const string RendererPath = Folder + "/URP-2D-Renderer.asset";
        public const string PipelinePath = Folder + "/URP-2D.asset";
        private const string UrpPackage = "Packages/com.unity.render-pipelines.universal";
        public const string SpriteLitMaterialPath = UrpPackage + "/Runtime/Materials/Sprite-Lit-Default.mat";
        private const string ParticlesUnlitShader = "Universal Render Pipeline/Particles/Unlit";
        private const string SpriteUnlitShader = "Universal Render Pipeline/2D/Sprite-Unlit-Default";

        [MenuItem("Tools/Tower Defence/Migrate to URP (2D Renderer)")]
        public static void Run()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var pipeline = CreatePipeline();
            AssignPipeline(pipeline);
            ConvertMaterials();
            var spriteLit = AssetDatabase.LoadAssetAtPath<Material>(SpriteLitMaterialPath);
            if (spriteLit == null) throw new InvalidOperationException("URP Sprite-Lit-Default material not found at " + SpriteLitMaterialPath);
            ConvertPrefabRenderers(spriteLit);
            ConvertScenes(spriteLit);
            AssetDatabase.SaveAssets();
            Debug.Log("[UrpMigration] Done.");
        }

        private static UniversalRenderPipelineAsset CreatePipeline()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Settings", "Rendering");

            var rendererData = AssetDatabase.LoadAssetAtPath<Renderer2DData>(RendererPath);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<Renderer2DData>();
                AssetDatabase.CreateAsset(rendererData, RendererPath);
                // Same defaults as Unity's own menu; postProcessData is internal, so set its field.
                var so = new SerializedObject(rendererData);
                so.FindProperty("m_PostProcessData").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<PostProcessData>(UrpPackage + "/Runtime/Data/PostProcessData.asset");
                so.ApplyModifiedPropertiesWithoutUndo();
                ResourceReloader.ReloadAllNullIn(rendererData, UrpPackage);
                EditorUtility.SetDirty(rendererData);
                Debug.Log($"[UrpMigration] Created {RendererPath}");
            }

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
                Debug.Log($"[UrpMigration] Created {PipelinePath}");
            }
            return pipeline;
        }

        private static void AssignPipeline(UniversalRenderPipelineAsset pipeline)
        {
            GraphicsSettings.defaultRenderPipeline = pipeline;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(current, false);
            Debug.Log($"[UrpMigration] Pipeline assigned to Graphics and {QualitySettings.names.Length} quality level(s).");
        }

        private static void ConvertMaterials()
        {
            var particles = Shader.Find(ParticlesUnlitShader);
            var spriteUnlit = Shader.Find(SpriteUnlitShader);
            if (particles == null || spriteUnlit == null) throw new InvalidOperationException("URP shaders not found.");

            foreach (var path in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                var oldShader = material.shader != null ? material.shader.name : "";
                bool builtinParticles = oldShader.Contains("Particles/") && !oldShader.StartsWith("Universal Render Pipeline");
                if (builtinParticles)
                {
                    var texture = material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
                    var scale = material.HasProperty("_MainTex") ? material.GetTextureScale("_MainTex") : Vector2.one;
                    var offset = material.HasProperty("_MainTex") ? material.GetTextureOffset("_MainTex") : Vector2.zero;
                    // "Legacy Shaders/Particles/*": color = 2 x _TintColor x vertex color x texture.
                    // "Mobile/Particles/*": color = vertex color x texture (no tint).
                    bool legacy = oldShader.StartsWith("Legacy Shaders/");
                    var tint = legacy && material.HasProperty("_TintColor") ? material.GetColor("_TintColor") * 2f : Color.white;
                    float blend = oldShader.Contains("Additive") ? 2 : oldShader.Contains("Multiply") ? 3 : oldShader.Contains("Premultiply") ? 1 : 0;

                    material.shader = particles;
                    material.SetTexture("_BaseMap", texture);
                    material.SetTextureScale("_BaseMap", scale);
                    material.SetTextureOffset("_BaseMap", offset);
                    material.SetColor("_BaseColor", tint);
                    material.SetFloat("_Surface", 1);  // transparent
                    material.SetFloat("_Blend", blend);
                    material.SetFloat("_Cull", 0);     // legacy particles render both sides
                    material.SetFloat("_ColorMode", 0); // multiply by particle vertex color
                    BaseShaderGUI.SetMaterialKeywords(material, null, ParticleGUI.SetMaterialKeywords);
                    EditorUtility.SetDirty(material);
                    Debug.Log($"[UrpMigration] {path}: '{oldShader}' -> Particles/Unlit (blend {blend}, base color {tint})");
                }
                else if (oldShader == "Sprites/Default")
                {
                    var texture = material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
                    var color = material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
                    material.shader = spriteUnlit;
                    if (texture != null) material.SetTexture("_MainTex", texture);
                    if (material.HasProperty("_Color")) material.SetColor("_Color", color);
                    EditorUtility.SetDirty(material);
                    Debug.Log($"[UrpMigration] {path}: 'Sprites/Default' -> {SpriteUnlitShader}");
                }
                else if (!oldShader.StartsWith("Universal Render Pipeline") && !oldShader.StartsWith("TextMeshPro/") && oldShader != "GUI/Text Shader")
                {
                    Debug.LogWarning($"[UrpMigration] {path}: shader '{oldShader}' left unconverted.");
                }
            }
        }

        // The built-in Sprites-Default material lives in "Resources/unity_builtin_extra".
        private static bool IsBuiltinSpriteDefault(Material material)
        {
            return material != null && material.name == "Sprites-Default" &&
                   AssetDatabase.GetAssetPath(material) == "Resources/unity_builtin_extra";
        }

        private static int ReplaceBuiltinSpriteMaterials(Renderer renderer, Material spriteLit, bool skipInherited)
        {
            var so = new SerializedObject(renderer);
            var materials = so.FindProperty("m_Materials");
            if (materials == null) return 0;
            if (skipInherited && PrefabUtility.IsPartOfPrefabInstance(renderer) && !materials.prefabOverride) return 0;
            int count = 0;
            for (int i = 0; i < materials.arraySize; i++)
            {
                var element = materials.GetArrayElementAtIndex(i);
                if (!IsBuiltinSpriteDefault(element.objectReferenceValue as Material)) continue;
                element.objectReferenceValue = spriteLit;
                count++;
            }
            if (count > 0) so.ApplyModifiedPropertiesWithoutUndo();
            return count;
        }

        private static void ConvertPrefabRenderers(Material spriteLit)
        {
            foreach (var path in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                // Renderers inside nested prefab instances are fixed in their own prefab asset.
                int changed = prefab.GetComponentsInChildren<Renderer>(true).Sum(r => ReplaceBuiltinSpriteMaterials(r, spriteLit, true));
                if (changed == 0) continue;
                EditorUtility.SetDirty(prefab);
                PrefabUtility.SavePrefabAsset(prefab);
                Debug.Log($"[UrpMigration] {path}: {changed} renderer material(s) -> Sprite-Lit-Default");
            }
        }

        private static void ConvertScenes(Material spriteLit)
        {
            foreach (var buildScene in EditorBuildSettings.scenes.Where(s => s.enabled))
            {
                var scene = EditorSceneManager.OpenScene(buildScene.path, OpenSceneMode.Single);
                var roots = scene.GetRootGameObjects();
                int changed = roots.SelectMany(r => r.GetComponentsInChildren<Renderer>(true)).Sum(r => ReplaceBuiltinSpriteMaterials(r, spriteLit, true));

                bool hasGlobalLight = roots.SelectMany(r => r.GetComponentsInChildren<Light2D>(true)).Any(l => l.lightType == Light2D.LightType.Global);
                if (!hasGlobalLight)
                {
                    var go = new GameObject("Global Light 2D");
                    var light = go.AddComponent<Light2D>();
                    var so = new SerializedObject(light);
                    so.FindProperty("m_LightType").intValue = (int)Light2D.LightType.Global;
                    so.FindProperty("m_Intensity").floatValue = 1f;
                    so.FindProperty("m_Color").colorValue = Color.white;
                    var layers = so.FindProperty("m_ApplyToSortingLayers");
                    layers.arraySize = SortingLayer.layers.Length;
                    for (int i = 0; i < SortingLayer.layers.Length; i++) layers.GetArrayElementAtIndex(i).intValue = SortingLayer.layers[i].id;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    changed++;
                }

                if (changed > 0)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Failed to save " + buildScene.path);
                }
                Debug.Log($"[UrpMigration] {buildScene.path}: {changed} change(s){(hasGlobalLight ? "" : " incl. Global Light 2D")}");
            }
        }
    }
}
