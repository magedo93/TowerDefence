using System;
using System.Collections.Generic;
using System.Linq;
using TowerDefence.Paths;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace TowerDefence.EditorTools
{
    // Places the Egyptian landmarks (Assets/Sprites/Egypt/Landmarks, drawn from
    // Art/Egypt/landmarks.html) into the levels. Decoration only: no colliders, never on the
    // enemy path or a build spot (checked against the real PathCreator curves and BuildSpot
    // positions; the tool refuses to place otherwise), and decoration tiles under a landmark
    // are cleared. Idempotent: rebuilds each level's "Landmarks" group.
    // Batch: -executeMethod TowerDefence.EditorTools.EgyptLandmarks.Run
    public static class EgyptLandmarks
    {
        public const string SpriteFolder = "Assets/Sprites/Egypt/Landmarks";
        public const string GroupName = "Landmarks";
        public const string SortingLayer = "Background";
        public const int SortingOrder = 3;      // above ground (1) and decoration tiles (2), below enemies
        public const float Cell = 64f;
        public const float PathClearance = 40f; // world units between a landmark and the enemy path
        public const float SpotClearance = 16f;

        public struct Placement
        {
            public string sprite; public int x, y, w, h; // bottom-left cell and size in cells
            public Placement(string sprite, int x, int y, int w, int h) { this.sprite = sprite; this.x = x; this.y = y; this.w = w; this.h = h; }
            public Rect WorldRect { get { return new Rect(x * Cell, y * Cell, w * Cell, h * Cell); } }
        }

        // Chosen from the level layout dump (Tools > Tower Defence > Dump Level Layouts):
        // open sand/field areas inside the camera view (cells x -15..14, y -8..7).
        public static readonly Dictionary<string, Placement[]> Layout = new Dictionary<string, Placement[]>
        {
            { "Level01", new[] { new Placement("Pyramids", 10, 4, 5, 3), new Placement("Sphinx", 10, -8, 4, 2),
                                 new Placement("Obelisk", -14, 7, 1, 2), new Placement("Obelisk", -11, 7, 1, 2) } },
            { "Level02", new[] { new Placement("Sphinx", -11, 6, 4, 2), new Placement("RamsesSquare", 10, -2, 3, 3) } },
            { "Level03", new[] { new Placement("Pyramids", 10, 4, 5, 3), new Placement("Sphinx", 7, -8, 4, 2),
                                 new Placement("RamsesSquare", 11, -7, 3, 3) } },
        };

        [MenuItem("Tools/Tower Defence/Place Egyptian Landmarks")]
        public static void Run()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            ConfigureSprites();
            foreach (var entry in Layout)
            {
                var path = $"Assets/Scenes/{entry.Key}.unity";
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                var roots = scene.GetRootGameObjects();

                var problems = Validate(roots, entry.Value);
                if (problems.Count > 0) throw new InvalidOperationException($"[EgyptLandmarks] {entry.Key}: " + string.Join("; ", problems));

                foreach (var old in roots.Where(r => r.name == GroupName)) UnityEngine.Object.DestroyImmediate(old);
                var group = new GameObject(GroupName);
                var things = roots.SelectMany(r => r.GetComponentsInChildren<Tilemap>(true)).FirstOrDefault(t => t.name == "Things");
                int cleared = 0;

                foreach (var p in entry.Value)
                {
                    var sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{SpriteFolder}/{p.sprite}.png");
                    if (sprite == null) throw new InvalidOperationException($"[EgyptLandmarks] missing sprite {p.sprite}");
                    var go = new GameObject($"{p.sprite} ({p.x},{p.y})");
                    go.transform.SetParent(group.transform, false);
                    go.transform.position = p.WorldRect.center;
                    var renderer = go.AddComponent<SpriteRenderer>();
                    renderer.sprite = sprite;
                    renderer.sortingLayerName = SortingLayer;
                    renderer.sortingOrder = SortingOrder;

                    if (things != null)
                        for (int cx = p.x; cx < p.x + p.w; cx++)
                        for (int cy = p.y; cy < p.y + p.h; cy++)
                        {
                            var cell = new Vector3Int(cx, cy, 0);
                            var s = things.GetSprite(cell);
                            if (s == null || !IsDecoration(s)) continue;
                            things.SetTile(cell, null);
                            cleared++;
                        }
                }

                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Failed to save " + path);
                Debug.Log($"[EgyptLandmarks] {entry.Key}: placed {entry.Value.Length} landmark(s), cleared {cleared} decoration tile(s).");
            }
        }

        // Shared with LandmarkPlacementTests: what must never happen.
        public static List<string> Validate(GameObject[] roots, IEnumerable<Placement> placements)
        {
            var problems = new List<string>();
            // Only GROUND routes matter: planes fly over landmarks on their own (higher) layers.
            var managers = roots.SelectMany(r => r.GetComponentsInChildren<EnemyManager>(true)).ToList();
            var paths = managers.Count > 0
                ? managers.SelectMany(m => (m.soldierPaths ?? new PathCreator[0]).Concat(m.vehiclePaths ?? new PathCreator[0])).Where(p => p != null).Distinct().ToList()
                : roots.SelectMany(r => r.GetComponentsInChildren<PathCreator>(true)).ToList();
            var spots = roots.SelectMany(r => r.GetComponentsInChildren<BuildSpotScript>(true)).ToList();
            var placed = new List<Rect>();
            foreach (var p in placements)
            {
                var rect = p.WorldRect;
                var nearPath = Inflate(rect, PathClearance);
                foreach (var path in paths)
                    if (path.path != null && path.path.CalculateEvenlySpacedPoints(8f).Any(pt => nearPath.Contains(pt)))
                        problems.Add($"{p.sprite} at ({p.x},{p.y}) is within {PathClearance} of path '{path.name}'");
                var nearSpot = Inflate(rect, SpotClearance);
                foreach (var spot in spots)
                    if (nearSpot.Contains((Vector2)spot.transform.position))
                        problems.Add($"{p.sprite} at ({p.x},{p.y}) covers build spot '{spot.name}'");
                foreach (var other in placed)
                    if (other.Overlaps(rect)) problems.Add($"{p.sprite} at ({p.x},{p.y}) overlaps another landmark");
                placed.Add(rect);
            }
            return problems;
        }

        static Rect Inflate(Rect r, float by) { return new Rect(r.xMin - by, r.yMin - by, r.width + 2 * by, r.height + 2 * by); }

        static bool IsDecoration(Sprite s)
        {
            int i = s.name.LastIndexOf('_');
            int index;
            return i >= 0 && int.TryParse(s.name.Substring(i + 1), out index) && index >= 130 && index <= 137;
        }

        public static void ConfigureSprites()
        {
            foreach (var path in AssetDatabase.FindAssets("t:Texture2D", new[] { SpriteFolder }).Select(AssetDatabase.GUIDToAssetPath))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                bool changed = importer.textureType != TextureImporterType.Sprite || importer.spritePixelsPerUnit != 1 ||
                               importer.mipmapEnabled || importer.spriteImportMode != SpriteImportMode.Single || !importer.alphaIsTransparency;
                if (!changed) continue;
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 1;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.filterMode = FilterMode.Bilinear;
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteAlignment = (int)SpriteAlignment.Center;
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
                importer.SaveAndReimport();
                Debug.Log($"[EgyptLandmarks] import settings applied: {path}");
            }
        }
    }
}
