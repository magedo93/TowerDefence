using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace TowerDefence.EditorTools
{
    // Prints each level as a character map (one char per tilemap cell) to help place
    // decorations/landmarks: R road, s sand, g grass, d dirt, * decoration tile,
    // B build spot, ' ' empty. Also lists tilemaps, sorting layers and the camera view.
    // Uses the ORIGINAL Kenney sheet (Art/Source) to classify each tile by its dominant material.
    // Batch: -executeMethod TowerDefence.EditorTools.LevelLayoutDump.Run  (writes AutomationLogs/LevelLayout.txt)
    public static class LevelLayoutDump
    {
        static readonly Color32[] Bases = { new Color32(0xEC, 0xDC, 0xB8, 255), new Color32(0xBB, 0x80, 0x44, 255), new Color32(0x2E, 0xCC, 0x71, 255), new Color32(0x89, 0xA4, 0xA6, 255) };
        const string Codes = "sdgR";

        [MenuItem("Tools/Tower Defence/Dump Level Layouts")]
        public static void Run()
        {
            var sheet = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave }; // survives scene loads
            sheet.LoadImage(File.ReadAllBytes("Art/Source/towerDefense_tilesheet_kenney.png"));
            var sb = new StringBuilder();
            foreach (var path in EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).Where(p => p.Contains("/Level")))
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                var roots = scene.GetRootGameObjects();
                var maps = roots.SelectMany(r => r.GetComponentsInChildren<Tilemap>(true)).ToList();
                var cam = roots.SelectMany(r => r.GetComponentsInChildren<Camera>(true)).First();
                sb.AppendLine($"=== {scene.name}  camera pos {cam.transform.position} ortho {cam.orthographicSize}");
                foreach (var m in maps)
                {
                    var r = m.GetComponent<TilemapRenderer>();
                    sb.AppendLine($"  tilemap '{m.name}' layer={r.sortingLayerName}/{r.sortingOrder} cellSize={m.layoutGrid.cellSize} origin={m.transform.position} bounds={m.cellBounds} tiles={m.GetUsedTilesCount()}");
                }
                var renderers = roots.SelectMany(r => r.GetComponentsInChildren<SpriteRenderer>(true));
                sb.AppendLine("  sprite sorting layers: " + string.Join(", ", renderers.GroupBy(r => r.sortingLayerName + "/" + r.sortingOrder).Select(g => g.Key + " x" + g.Count())));
                var spots = roots.SelectMany(r => r.GetComponentsInChildren<BuildSpotScript>(true)).ToList();

                var bounds = maps.Select(m => m.cellBounds).Aggregate((a, b) => { var min = Vector3Int.Min(a.min, b.min); var max = Vector3Int.Max(a.max, b.max); return new BoundsInt(min, max - min); });
                var grid = maps[0].layoutGrid;
                sb.AppendLine($"  cells x {bounds.xMin}..{bounds.xMax - 1}, y {bounds.yMin}..{bounds.yMax - 1}  (rows printed top to bottom)");
                for (int y = bounds.yMax - 1; y >= bounds.yMin; y--)
                {
                    var line = new StringBuilder($"  {y,4} ");
                    for (int x = bounds.xMin; x < bounds.xMax; x++)
                    {
                        var cell = new Vector3Int(x, y, 0);
                        var center = grid.GetCellCenterWorld(cell);
                        char c = ' ';
                        foreach (var m in maps)
                        {
                            var sprite = m.GetSprite(cell);
                            if (sprite == null) continue;
                            int index = int.Parse(sprite.name.Substring(sprite.name.LastIndexOf('_') + 1));
                            if (index >= 130 && index <= 137) { c = '*'; continue; }
                            if (c == '*') continue;
                            c = Classify(sheet, sprite);
                        }
                        if (spots.Any(s => Vector2.Distance(s.transform.position, center) < grid.cellSize.x * 0.5f)) c = 'B';
                        line.Append(c);
                    }
                    sb.AppendLine(line.ToString());
                }
                sb.AppendLine("  x ref: " + string.Concat(Enumerable.Range(bounds.xMin, bounds.size.x).Select(x => (char)('0' + ((x % 10) + 10) % 10))));
            }
            Directory.CreateDirectory("AutomationLogs");
            File.WriteAllText("AutomationLogs/LevelLayout.txt", sb.ToString());
            Debug.Log("[LevelLayoutDump] wrote AutomationLogs/LevelLayout.txt");
        }

        static char Classify(Texture2D sheet, Sprite sprite)
        {
            var rect = sprite.rect;
            var counts = new int[4];
            for (int y = 0; y < (int)rect.height; y += 4)
            for (int x = 0; x < (int)rect.width; x += 4)
            {
                var p = (Color32)sheet.GetPixel((int)rect.x + x, (int)rect.y + y);
                int best = 0, bestD = int.MaxValue;
                for (int i = 0; i < 4; i++)
                {
                    int dr = p.r - Bases[i].r, dg = p.g - Bases[i].g, db = p.b - Bases[i].b, d = dr * dr + dg * dg + db * db;
                    if (d < bestD) { bestD = d; best = i; }
                }
                counts[best]++;
            }
            // Any substantial stone share means the cell carries road.
            if (counts[3] > counts.Sum() / 3) return 'R';
            int max = 0; for (int i = 1; i < 4; i++) if (counts[i] > counts[max]) max = i;
            return Codes[max];
        }
    }
}
