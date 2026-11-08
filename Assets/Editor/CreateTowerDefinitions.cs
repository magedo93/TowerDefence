using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TowerDefence.EditorTools
{
    // Moves projectile tower balance out of the prefabs into ProjectileTowerDefinition assets.
    // For every tower prefab under Assets/Prefabs/Towers that has no definition yet, creates
    // Assets/Settings/Towers/<Prefab>.asset from the prefab's CURRENT values (cost, cooldown,
    // rotation speed, projectile, fire sound, collider range, projectile damage) and assigns
    // it, so balance is unchanged. Existing definitions are never overwritten. Idempotent.
    // Batch: Unity.exe -batchmode -quit -projectPath . -executeMethod TowerDefence.EditorTools.CreateTowerDefinitions.Run
    public static class CreateTowerDefinitions
    {
        private const string PrefabFolder = "Assets/Prefabs/Towers";
        private const string DefinitionFolder = "Assets/Settings/Towers";

        [MenuItem("Tools/Tower Defence/Create Tower Definitions From Prefabs")]
        public static void Run()
        {
            int created = 0;
            foreach (var path in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var tower = prefab.GetComponent<TowerScript>();
                if (tower == null || tower is LaserTowerScript) continue;
                if (tower.definition != null)
                {
                    Debug.Log($"[CreateTowerDefinitions] {path}: already uses {AssetDatabase.GetAssetPath(tower.definition)}");
                    continue;
                }

                var bullet = tower.bulletPrefab != null ? tower.bulletPrefab.GetComponent<BulletScript>() : null;
                var range = prefab.GetComponent<CircleCollider2D>();
                if (bullet == null || range == null)
                {
                    Debug.LogError($"[CreateTowerDefinitions] {path}: needs a bulletPrefab with BulletScript and a CircleCollider2D; skipped.", prefab);
                    continue;
                }

                var definition = ScriptableObject.CreateInstance<ProjectileTowerDefinition>();
                definition.displayName = ObjectNames.NicifyVariableName(prefab.name);
                definition.purchaseCost = tower.cost;
                definition.cooldown = tower.cooldown;
                definition.rotationSpeed = tower.rotationSpeed;
                definition.projectilePrefab = tower.bulletPrefab;
                definition.fireSound = tower.gunShoot;
                definition.range = range.radius;
                definition.damage = bullet.damage;

                var assetPath = AssetDatabase.GenerateUniqueAssetPath($"{DefinitionFolder}/{prefab.name}.asset");
                AssetDatabase.CreateAsset(definition, assetPath);

                tower.definition = definition;
                EditorUtility.SetDirty(tower);
                PrefabUtility.SavePrefabAsset(prefab);
                Debug.Log($"[CreateTowerDefinitions] {path}: created {assetPath} (cost {definition.purchaseCost}, cooldown {definition.cooldown}, range {definition.range}, damage {definition.damage})");
                created++;
            }

            // Give the laser definition a display name if it has none.
            foreach (var laser in AssetDatabase.FindAssets("t:LaserTowerDefinition").Select(g => AssetDatabase.LoadAssetAtPath<LaserTowerDefinition>(AssetDatabase.GUIDToAssetPath(g))))
            {
                if (!string.IsNullOrEmpty(laser.displayName)) continue;
                laser.displayName = "Laser Tower";
                EditorUtility.SetDirty(laser);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[CreateTowerDefinitions] Done. Created {created} definition(s).");
        }
    }
}
