using NUnit.Framework;
using TowerDefence.EditorTools;
using UnityEditor;
using UnityEngine;

// Art phase 3: the laser tower has its own (untinted) sprites; the projectile towers keep the
// shared base and their own guns.
public class TowerArtTests
{
    [Test]
    public void LaserTower_UsesDedicatedUntintedSprites()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EgyptTowerArt.LaserPrefabPath);
        Assert.That(prefab, Is.Not.Null);
        var baseRenderer = prefab.GetComponent<SpriteRenderer>();
        var gunRenderer = prefab.transform.Find("Gun").GetComponent<SpriteRenderer>();
        Assert.That(baseRenderer.sprite.name, Is.EqualTo(EgyptTowerArt.LaserBaseSprite));
        Assert.That(gunRenderer.sprite.name, Is.EqualTo(EgyptTowerArt.LaserGunSprite));
        Assert.That(baseRenderer.color, Is.EqualTo(Color.white));
        Assert.That(gunRenderer.color, Is.EqualTo(Color.white));
    }

    [TestCase("Assets/Prefabs/Towers/BulletTower.prefab", "towerDefense_tilesheet_249")]
    [TestCase("Assets/Prefabs/Towers/RocketTower.prefab", "towerDefense_tilesheet_229")]
    public void ProjectileTowers_KeepSharedBaseAndOwnGun(string path, string gunSprite)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.That(prefab.GetComponent<SpriteRenderer>().sprite.name, Is.EqualTo("towerDefense_tilesheet_180"));
        Assert.That(prefab.transform.Find("Gun").GetComponent<SpriteRenderer>().sprite.name, Is.EqualTo(gunSprite));
    }
}
