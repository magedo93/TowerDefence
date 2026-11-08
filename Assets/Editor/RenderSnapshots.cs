using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace TowerDefence.EditorTools
{
    // Renders deterministic screenshots for render-pipeline regression checks:
    //   * every Build Settings scene through its main camera (edit-time state), and
    //   * an "Effects" scene with the material-sensitive prefabs (particles, laser beam,
    //     towers, enemies, coins), particles simulated with fixed seeds and times.
    // Compare() diffs two snapshot folders and writes amplified diff images.
    // Works under the Built-in pipeline and URP (URP-only bits are looked up by reflection).
    // Needs a graphics device: run batch mode WITHOUT -nographics.
    // Batch: -executeMethod TowerDefence.EditorTools.RenderSnapshots.CaptureFromCommandLine -snapshotDir <dir>
    //        -executeMethod TowerDefence.EditorTools.RenderSnapshots.CompareFromCommandLine -baseline <dir> -current <dir> -diffDir <dir>
    public static class RenderSnapshots
    {
        private const int Width = 1280, Height = 720;
        private const int Seed = 12345;

        [MenuItem("Tools/Tower Defence/Render Snapshots")]
        public static void CaptureMenu() { Capture("AutomationLogs/Snapshots/manual"); }

        public static void CaptureFromCommandLine() { Capture(Arg("-snapshotDir") ?? "AutomationLogs/Snapshots/latest"); }

        public static void CompareFromCommandLine()
        {
            Compare(Arg("-baseline"), Arg("-current"), Arg("-diffDir") ?? "AutomationLogs/Snapshots/diff");
        }

        public static void Capture(string outDir)
        {
            Directory.CreateDirectory(outDir);
            var pipeline = GraphicsSettings.currentRenderPipeline;
            Debug.Log($"[RenderSnapshots] Pipeline: {(pipeline != null ? pipeline.GetType().Name + " (" + pipeline.name + ")" : "Built-in")}");

            foreach (var buildScene in EditorBuildSettings.scenes.Where(s => s.enabled))
            {
                var scene = EditorSceneManager.OpenScene(buildScene.path, OpenSceneMode.Single);
                var camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>(true))
                    .OrderByDescending(c => c.CompareTag("MainCamera")).FirstOrDefault();
                if (camera == null) { Debug.LogWarning($"[RenderSnapshots] {scene.name}: no camera"); continue; }
                Save(camera, System.IO.Path.Combine(outDir, scene.name + ".png"));
            }

            CaptureEffects(outDir);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Debug.Log($"[RenderSnapshots] Captured to {System.IO.Path.GetFullPath(outDir)}");
        }

        private static void CaptureEffects(string outDir)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("SnapshotCamera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 400;
            camera.transform.position = new Vector3(0, 0, -100);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.3f, 0.35f, 0.3f, 1);
            AddGlobalLightIfUrp();

            var items = new[]
            {
                ("Assets/Prefabs/Etc/Explosion.prefab", new Vector2(-500, 200), 0.12f),
                ("Assets/Prefabs/Towers/Rocket.prefab", new Vector2(-250, 200), 0.6f),
                ("Assets/Prefabs/Enemies/Tank.prefab", new Vector2(0, 200), 0.8f),
                ("Assets/Prefabs/Enemies/Plane.prefab", new Vector2(300, 200), 0.8f),
                ("Assets/Prefabs/Towers/BulletTower.prefab", new Vector2(-500, -150), 0f),
                ("Assets/Prefabs/Towers/RocketTower.prefab", new Vector2(-300, -150), 0f),
                ("Assets/Prefabs/Towers/LaserTower.prefab", new Vector2(-100, -150), 0f),
                ("Assets/Prefabs/Enemies/Soldier.prefab", new Vector2(150, -150), 0f),
                ("Assets/Prefabs/Etc/GoldCoin.prefab", new Vector2(350, -150), 0f),
                ("Assets/Prefabs/Etc/Blood.prefab", new Vector2(500, -150), 0.3f),
            };

            foreach (var (path, position, simulate) in items)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { Debug.LogWarning($"[RenderSnapshots] missing {path}"); continue; }
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instance.transform.position = position;
                // Show damage fire on vehicles; scripts don't run in edit mode.
                foreach (var fire in new[] { "Fire", "LeftFire", "RightFire" })
                {
                    var child = instance.transform.Find(fire);
                    if (child != null) child.gameObject.SetActive(true);
                }
                var beam = instance.GetComponent<LineRenderer>();
                if (beam != null)
                {
                    beam.enabled = true;
                    beam.useWorldSpace = true;
                    beam.positionCount = 2;
                    beam.SetPosition(0, (Vector3)position + new Vector3(0, 25, 0));
                    beam.SetPosition(1, (Vector3)position + new Vector3(220, 120, 0));
                    beam.startWidth = 5; beam.endWidth = 2.5f;
                    beam.startColor = beam.endColor = new Color(0.15f, 1, 0.85f);
                }
                foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true))
                {
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    ps.useAutoRandomSeed = false;
                    ps.randomSeed = Seed;
                }
                if (simulate > 0)
                    foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true)
                                 .Where(p => p.transform.parent == null || p.transform.parent.GetComponentInParent<ParticleSystem>() == null))
                        ps.Simulate(simulate, true, true, true);
            }
            Save(camera, System.IO.Path.Combine(outDir, "Effects.png"));
            // Close-ups of the particle row (explosion + rocket, burning tank + plane).
            camera.orthographicSize = 160;
            camera.transform.position = new Vector3(-375, 200, -100);
            Save(camera, System.IO.Path.Combine(outDir, "EffectsZoomA.png"));
            camera.transform.position = new Vector3(150, 200, -100);
            Save(camera, System.IO.Path.Combine(outDir, "EffectsZoomB.png"));
        }

        // Global white 2D light so URP's lit sprites look like the Built-in unlit ones.
        private static void AddGlobalLightIfUrp()
        {
            var lightType = Type.GetType("UnityEngine.Rendering.Universal.Light2D, Unity.RenderPipelines.Universal.2D.Runtime");
            if (lightType == null || GraphicsSettings.currentRenderPipeline == null) return;
            var go = new GameObject("Global Light 2D");
            var light = go.AddComponent(lightType);
            var so = new SerializedObject(light);
            so.FindProperty("m_LightType").intValue = 4; // Global
            so.FindProperty("m_Intensity").floatValue = 1f;
            so.FindProperty("m_Color").colorValue = Color.white;
            var layers = so.FindProperty("m_ApplyToSortingLayers");
            layers.arraySize = SortingLayer.layers.Length;
            for (int i = 0; i < SortingLayer.layers.Length; i++) layers.GetArrayElementAtIndex(i).intValue = SortingLayer.layers[i].id;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Save(Camera camera, string file)
        {
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            try
            {
                if (GraphicsSettings.currentRenderPipeline == null)
                {
                    camera.targetTexture = rt;
                    camera.Render();
                }
                else
                {
                    var request = new RenderPipeline.StandardRequest { destination = rt };
                    if (!RenderPipeline.SupportsRenderRequest(camera, request))
                        throw new InvalidOperationException("Render pipeline does not support render requests.");
                    RenderPipeline.SubmitRenderRequest(camera, request);
                }
                RenderTexture.active = rt;
                var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                tex.Apply();
                File.WriteAllBytes(file, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                Debug.Log($"[RenderSnapshots] Saved {file}");
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
        }

        public static void Compare(string baselineDir, string currentDir, string diffDir)
        {
            Directory.CreateDirectory(diffDir);
            foreach (var baselineFile in Directory.GetFiles(baselineDir, "*.png").OrderBy(f => f))
            {
                var name = System.IO.Path.GetFileName(baselineFile);
                var currentFile = System.IO.Path.Combine(currentDir, name);
                if (!File.Exists(currentFile)) { Debug.LogWarning($"[RenderSnapshots] Compare {name}: missing in current"); continue; }
                var a = new Texture2D(2, 2); a.LoadImage(File.ReadAllBytes(baselineFile));
                var b = new Texture2D(2, 2); b.LoadImage(File.ReadAllBytes(currentFile));
                if (a.width != b.width || a.height != b.height) { Debug.LogWarning($"[RenderSnapshots] Compare {name}: size differs"); continue; }
                var pa = a.GetPixels32(); var pb = b.GetPixels32();
                var diff = new Color32[pa.Length];
                long sum = 0; int changed = 0, maxDelta = 0;
                for (int i = 0; i < pa.Length; i++)
                {
                    int dr = Math.Abs(pa[i].r - pb[i].r), dg = Math.Abs(pa[i].g - pb[i].g), db = Math.Abs(pa[i].b - pb[i].b);
                    int d = Math.Max(dr, Math.Max(dg, db));
                    sum += dr + dg + db;
                    if (d > 16) changed++;
                    maxDelta = Math.Max(maxDelta, d);
                    byte v = (byte)Math.Min(255, d * 4);
                    diff[i] = new Color32(v, v, v, 255);
                }
                var diffTex = new Texture2D(a.width, a.height, TextureFormat.RGB24, false);
                diffTex.SetPixels32(diff); diffTex.Apply();
                File.WriteAllBytes(System.IO.Path.Combine(diffDir, name), diffTex.EncodeToPNG());
                Debug.Log($"[RenderSnapshots] Compare {name}: mean channel diff {sum / (3.0 * pa.Length):0.00}/255, " +
                          $"pixels changed (>16) {100.0 * changed / pa.Length:0.00}%, max {maxDelta}");
                UnityEngine.Object.DestroyImmediate(a); UnityEngine.Object.DestroyImmediate(b); UnityEngine.Object.DestroyImmediate(diffTex);
            }
        }

        private static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
