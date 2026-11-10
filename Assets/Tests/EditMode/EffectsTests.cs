using System.Linq;
using NUnit.Framework;
using TowerDefence;
using TowerDefence.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Art phase 5 (effects): explosion dust layer, laser colour per level, carnelian damage flash.
public class EffectsTests
{
    [Test]
    public void Explosion_HasShortDustLayerBehindFire()
    {
        var explosion = AssetDatabase.LoadAssetAtPath<GameObject>(EgyptEffects.ExplosionPath);
        var dust = explosion.transform.Find(EgyptEffects.DustChild);
        Assert.That(dust, Is.Not.Null, "Dust child");
        var ps = dust.GetComponent<ParticleSystem>();
        Assert.That(ps.main.loop, Is.False, "dust must end so the pooled explosion deactivates");
        Assert.That(ps.main.duration + ps.main.startLifetime.constantMax, Is.LessThan(1.5f));
        var renderer = dust.GetComponent<ParticleSystemRenderer>();
        Assert.That(AssetDatabase.GetAssetPath(renderer.sharedMaterial), Is.EqualTo(EgyptEffects.DustMaterialPath));
        Assert.That(renderer.sharedMaterial.GetTexture("_BaseMap"), Is.Not.Null);
        var fire = explosion.GetComponent<ParticleSystemRenderer>();
        Assert.That(renderer.sortingLayerID, Is.EqualTo(fire.sortingLayerID));
        Assert.That(renderer.sortingOrder, Is.LessThan(fire.sortingOrder));
    }

    [Test]
    public void Laser_BeamColourChangesPerLevel()
    {
        var definition = AssetDatabase.LoadAssetAtPath<LaserTowerDefinition>(EgyptEffects.LaserDefinitionPath);
        Assert.That(definition.levels.Select(l => l.beamColor), Is.EqualTo(EgyptEffects.BeamColors));
        Assert.That(definition.levels.Select(l => l.beamColor).Distinct().Count(), Is.EqualTo(definition.levels.Length));
    }

    [TestCase("Level01")]
    [TestCase("Level02")]
    [TestCase("Level03")]
    public void Level_DamageFlashIsCarnelian(string level)
    {
        var scene = EditorSceneManager.OpenScene($"Assets/Scenes/{level}.unity", OpenSceneMode.Single);
        try
        {
            var health = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PlayerHealth>(true)).Single();
            var c = health.damageImage.color;
            Assert.That(new Vector3(c.r, c.g, c.b), Is.EqualTo(new Vector3(EgyptEffects.DamageFlash.r, EgyptEffects.DamageFlash.g, EgyptEffects.DamageFlash.b)));
        }
        finally { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); }
    }
}
