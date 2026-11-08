using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TowerDefence;
using UnityEditor;
using UnityEngine;

// Every tower prefab is driven by a valid TowerDefinition, and moving balance into the
// definitions did not change any numbers.
public class TowerDefinitionTests
{
    private static IEnumerable<string> TowerPrefabs()
    {
        return AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Towers" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => AssetDatabase.LoadAssetAtPath<GameObject>(p).GetComponent<TowerScript>() != null)
            .OrderBy(p => p);
    }

    private static TowerScript Tower(string name)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Towers/{name}.prefab");
        Assert.That(prefab, Is.Not.Null, name);
        return prefab.GetComponent<TowerScript>();
    }

    [TestCaseSource(nameof(TowerPrefabs))]
    public void TowerPrefab_HasValidDefinitionOfTheRightKind(string path)
    {
        var tower = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<TowerScript>();
        Assert.That(tower.definition, Is.Not.Null, $"{path}: no definition");
        Assert.That(tower.definition.IsValid, Is.True, $"{path}: invalid definition");
        if (tower is LaserTowerScript) Assert.That(tower.definition, Is.InstanceOf<LaserTowerDefinition>(), path);
        else Assert.That(tower.definition, Is.InstanceOf<ProjectileTowerDefinition>(), path);
        Assert.That(tower.cost, Is.EqualTo(tower.definition.purchaseCost), $"{path}: legacy cost out of sync (OnValidate)");
    }

    [TestCase("BulletTower", "Bullet", 3, 0.5f, 250f, 1, 10f)]
    [TestCase("RocketTower", "Rocket", 9, 5f, 500f, 1, 10f)]
    public void ProjectileTower_BalanceUnchanged(string tower, string projectile, int cost, float cooldown, float range, int damage, float rotation)
    {
        var script = Tower(tower);
        var definition = (ProjectileTowerDefinition)script.definition;
        Assert.That(definition.purchaseCost, Is.EqualTo(cost), "cost");
        Assert.That(definition.cooldown, Is.EqualTo(cooldown), "cooldown");
        Assert.That(definition.range, Is.EqualTo(range), "range");
        Assert.That(definition.damage, Is.EqualTo(damage), "damage");
        Assert.That(definition.rotationSpeed, Is.EqualTo(rotation), "rotation speed");
        Assert.That(definition.projectilePrefab.name, Is.EqualTo(projectile), "projectile");
        Assert.That(definition.fireSound, Is.SameAs(script.gunShoot), "fire sound");

        // The prefab's own collider/legacy fields still match, so the Inspector tells no lies.
        Assert.That(script.GetComponent<CircleCollider2D>().radius, Is.EqualTo(range), "collider radius");
        Assert.That(script.cooldown, Is.EqualTo(cooldown), "legacy cooldown");
        Assert.That(script.bulletPrefab, Is.SameAs(definition.projectilePrefab), "legacy projectile");

        // Effective values used at runtime come from the definition.
        Assert.That(script.BuildCost, Is.EqualTo(cost));
        Assert.That(script.Cooldown, Is.EqualTo(cooldown));
        Assert.That(script.RotationSpeed, Is.EqualTo(rotation));
    }

    [Test]
    public void LaserTower_KeepsItsDefinitionAndPrice()
    {
        var laser = (LaserTowerScript)Tower("LaserTower");
        Assert.That(laser.LaserDefinition, Is.Not.Null);
        Assert.That(AssetDatabase.GetAssetPath(laser.LaserDefinition), Is.EqualTo("Assets/Settings/Towers/LaserTower.asset"));
        Assert.That(laser.BuildCost, Is.EqualTo(6));
        Assert.That(laser.LaserDefinition.levels.Select(l => l.upgradeCost), Is.EqualTo(new[] { 0, 6, 9 }));
    }
}
