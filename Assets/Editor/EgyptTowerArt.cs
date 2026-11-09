using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TowerDefence.EditorTools
{
    // Art phase 3: the laser tower gets its own sprites (sheet cells 182 base / 250 emitter, drawn
    // from Art/Egypt/towers.html) instead of the cannon sprites tinted cyan, which turned the
    // sandstone art muddy green. Idempotent.
    // Batch: -executeMethod TowerDefence.EditorTools.EgyptTowerArt.Run
    public static class EgyptTowerArt
    {
        public const string SheetPath = "Assets/Sprites/Sheets/towerDefense_tilesheet.png";
        public const string LaserPrefabPath = "Assets/Prefabs/Towers/LaserTower.prefab";
        public const string LaserBaseSprite = "towerDefense_tilesheet_182";
        public const string LaserGunSprite = "towerDefense_tilesheet_250";

        [MenuItem("Tools/Tower Defence/Apply Egyptian Laser Tower Sprites")]
        public static void Run()
        {
            var root = PrefabUtility.LoadPrefabContents(LaserPrefabPath);
            try
            {
                ApplyLaserSprites(root);
                PrefabUtility.SaveAsPrefabAsset(root, LaserPrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            Debug.Log("[EgyptTowerArt] LaserTower uses its own sprites now.");
        }

        // Base = the root SpriteRenderer, gun = the child named "Gun" (what TowerScript rotates).
        public static void ApplyLaserSprites(GameObject root)
        {
            var sprites = AssetDatabase.LoadAllAssetsAtPath(SheetPath).OfType<Sprite>().ToList();
            Func<string, Sprite> find = name => sprites.FirstOrDefault(s => s.name == name)
                ?? throw new InvalidOperationException($"[EgyptTowerArt] sprite {name} not found in {SheetPath}");
            var gun = root.transform.Find("Gun");
            if (gun == null) throw new InvalidOperationException("[EgyptTowerArt] LaserTower has no child \"Gun\".");
            var baseRenderer = root.GetComponent<SpriteRenderer>();
            var gunRenderer = gun.GetComponent<SpriteRenderer>();
            if (baseRenderer == null || gunRenderer == null) throw new InvalidOperationException("[EgyptTowerArt] LaserTower sprite renderers missing.");
            baseRenderer.sprite = find(LaserBaseSprite);
            gunRenderer.sprite = find(LaserGunSprite);
            baseRenderer.color = gunRenderer.color = Color.white;
        }
    }
}
