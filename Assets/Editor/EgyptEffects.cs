using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace TowerDefence.EditorTools
{
    // Art phase 5 (effects). Idempotent.
    //   * Explosion.prefab gets a child "Dust" particle system: a short puff of sand-coloured
    //     clouds behind the (unchanged) fire, using DustCloud.png (Art/Egypt/effects.html) with an
    //     alpha-blended URP Particles/Unlit material. Lifetime stays short so the pooled explosion
    //     still deactivates quickly (ParticleSystemAutoDestroyScript waits for children too).
    //   * Laser beam colour per level: turquoise -> white-hot -> gold "sunbeam of Ra".
    //   * Damage flash (PlayerHealth.damageImage) is carnelian instead of pure red.
    //   * The soldier death mark is sheet cell 21, redrawn by the art build (no prefab change).
    // Batch: -executeMethod TowerDefence.EditorTools.EgyptEffects.Run
    public static class EgyptEffects
    {
        public const string ExplosionPath = "Assets/Prefabs/Etc/Explosion.prefab";
        public const string DustTexturePath = "Assets/Materials/Particles/Textures/DustCloud.png";
        public const string DustMaterialPath = "Assets/Materials/Particles/ParticleDust.mat";
        public const string LaserDefinitionPath = "Assets/Settings/Towers/LaserTower.asset";
        public const string DustChild = "Dust";

        public static readonly Color DustColor = new Color(0.80f, 0.66f, 0.45f, 0.6f);
        public static readonly Color DamageFlash = new Color(0.72f, 0.22f, 0.17f, 0f); // alpha is animated by PlayerHealth
        public static readonly Color[] BeamColors =
        {
            new Color(0.15f, 1f, 0.85f, 1f),   // level 1: turquoise
            new Color(0.78f, 1f, 0.96f, 1f),   // level 2: white-hot
            new Color(1f, 0.80f, 0.27f, 1f),   // level 3: gold sunbeam of Ra
        };

        [MenuItem("Tools/Tower Defence/Apply Egyptian Effects")]
        public static void Run()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var material = EnsureDustMaterial();
            AddDust(material);
            SetBeamColors();
            foreach (var level in EgyptUiStyle.Levels) SetDamageFlash(level);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Debug.Log("[EgyptEffects] done.");
        }

        private static Material EnsureDustMaterial()
        {
            var importer = AssetImporter.GetAtPath(DustTexturePath) as TextureImporter;
            if (importer == null) throw new InvalidOperationException($"[EgyptEffects] {DustTexturePath} missing; run Tools/Art/build-egypt-art.ps1.");
            if (!importer.alphaIsTransparency || importer.wrapMode != TextureWrapMode.Clamp || importer.textureType != TextureImporterType.Default)
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(DustTexturePath);
            var material = AssetDatabase.LoadAssetAtPath<Material>(DustMaterialPath);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader == null) throw new InvalidOperationException("[EgyptEffects] URP Particles/Unlit shader missing.");
                material = new Material(shader) { name = "ParticleDust" };
                AssetDatabase.CreateAsset(material, DustMaterialPath);
            }
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Surface", 1);   // transparent
            material.SetFloat("_Blend", 0);     // alpha blend
            material.SetFloat("_Cull", 0);
            material.SetFloat("_ColorMode", 0); // multiply by particle colour
            BaseShaderGUI.SetMaterialKeywords(material, null, ParticleGUI.SetMaterialKeywords);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            return material;
        }

        private static void AddDust(Material material)
        {
            var root = PrefabUtility.LoadPrefabContents(ExplosionPath);
            try
            {
                var fire = root.GetComponent<ParticleSystemRenderer>();
                var child = root.transform.Find(DustChild);
                if (child == null)
                {
                    child = new GameObject(DustChild).transform;
                    child.SetParent(root.transform, false);
                    child.gameObject.layer = root.layer;
                    child.gameObject.AddComponent<ParticleSystem>();
                }
                var ps = child.GetComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                var main = ps.main;
                main.duration = 0.3f;
                main.loop = false;
                main.playOnAwake = true;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.75f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(15f, 40f);
                main.startSize = new ParticleSystem.MinMaxCurve(20f, 36f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.startColor = DustColor;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.scalingMode = ParticleSystemScalingMode.Local;
                main.maxParticles = 20;

                var emission = ps.emission;
                emission.rateOverTime = 0;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 9) });

                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = 8f;
                shape.rotation = Vector3.zero;

                var size = ps.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 1.7f));

                var color = ps.colorOverLifetime;
                color.enabled = true;
                var fade = new Gradient();
                fade.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                             new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0.7f, 0.4f), new GradientAlphaKey(0, 1) });
                color.color = fade;

                var renderer = child.GetComponent<ParticleSystemRenderer>();
                renderer.sharedMaterial = material;
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.maxParticleSize = fire != null ? fire.maxParticleSize : 0.5f;
                renderer.sortingLayerID = fire != null ? fire.sortingLayerID : 0;
                renderer.sortingOrder = (fire != null ? fire.sortingOrder : 0) - 1; // behind the fire

                PrefabUtility.SaveAsPrefabAsset(root, ExplosionPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void SetBeamColors()
        {
            var definition = AssetDatabase.LoadAssetAtPath<LaserTowerDefinition>(LaserDefinitionPath);
            if (definition == null || definition.levels == null || definition.levels.Length != BeamColors.Length)
                throw new InvalidOperationException("[EgyptEffects] LaserTower.asset must have 3 levels.");
            for (int i = 0; i < BeamColors.Length; i++) definition.levels[i].beamColor = BeamColors[i];
            EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssets();
        }

        private static void SetDamageFlash(string level)
        {
            var path = $"Assets/Scenes/{level}.unity";
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var health = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PlayerHealth>(true)).Single();
            if (health.damageImage == null) throw new InvalidOperationException($"[EgyptEffects] {level}: no damage image.");
            var c = DamageFlash;
            c.a = health.damageImage.color.a;
            health.damageImage.color = c;
            EditorUtility.SetDirty(health.damageImage);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Failed to save " + path);
        }
    }
}
