using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TMPro;
using TowerDefence;
using TowerDefence.Paths;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

// Pins down EnemyManager's wave schedule (order, timing, path choice, completion,
// win/lose interplay) so the spawner can be rewritten without changing behavior.
public class WaveSpawnTests
{
    // Records every enemy activation. Lives on the enemy templates, so pooled clones carry it.
    private class SpawnProbe : MonoBehaviour
    {
        public static readonly List<(float time, GameObject enemy)> Log = new List<(float, GameObject)>();
        private void OnEnable() { Log.Add((Time.time, gameObject)); }
    }

    private const float Tolerance = 0.15f; // Timers fire on frame boundaries.

    private readonly List<Object> _created = new List<Object>();
    private Transform _templates;
    private GameObject _deathEffect;

    private T Track<T>(T obj) where T : Object { _created.Add(obj); return obj; }

    private GameObject Ui<T>(string name) where T : Component
    {
        return Track(new GameObject(name, typeof(RectTransform), typeof(T)));
    }

    [SetUp]
    public void SetUp()
    {
        SpawnProbe.Log.Clear();
        // Templates sit under an inactive parent, like prefab assets: the pooler toggles a
        // template's own active flag while cloning, which must not start its scripts.
        var holder = Track(new GameObject("Templates"));
        holder.SetActive(false);
        _templates = holder.transform;
        // Stand-in for the blood/explosion prefab every real enemy spawns on death.
        _deathEffect = new GameObject("TestDeathEffect");
        _deathEffect.transform.SetParent(_templates, false);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var (_, enemy) in SpawnProbe.Log) if (enemy != null) Object.DestroyImmediate(enemy);
        SpawnProbe.Log.Clear();
        for (int i = _created.Count - 1; i >= 0; i--)
            if (_created[i] != null) Object.DestroyImmediate(_created[i]);
        _created.Clear();
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            if (t != null && t.parent == null && t.name == "TestDeathEffect") Object.DestroyImmediate(t.gameObject);
        var pooler = GameObject.Find("#ObjectPooler");
        if (pooler != null) Object.DestroyImmediate(pooler);
    }

    private PathCreator NewPath(string name, Vector2 centre)
    {
        var go = Track(new GameObject(name));
        go.transform.position = centre;
        var path = go.AddComponent<PathCreator>();
        path.CreatePath();
        return path;
    }

    private GameObject Template<TMarker>(string name) where TMarker : WalkerScript
    {
        var go = new GameObject(name);
        go.transform.SetParent(_templates, false);
        if (typeof(TMarker) == typeof(Aircraft))
        {
            new GameObject("Plane").transform.SetParent(go.transform, false);
            new GameObject("Shadow").transform.SetParent(go.transform, false);
        }
        go.AddComponent<TMarker>(); // movementSpeed 0: enemies stay at the path start.
        go.AddComponent<EnemyScript>().reward = 5;
        go.GetComponent<DestroyScript>().objectToSpawn = _deathEffect;
        go.AddComponent<SpawnProbe>();
        return go;
    }

    private static WaveProperties.Subwave Sub(GameObject enemy, int size, float enemyTime, float subwaveTime)
    {
        return new WaveProperties.Subwave { Enemy = enemy, size = size, enemyTime = enemyTime, subwaveTime = subwaveTime };
    }

    private WaveProperties Wave(float waveTime, params WaveProperties.Subwave[] subwaves)
    {
        var wave = Track(ScriptableObject.CreateInstance<WaveProperties>());
        wave.subwave = subwaves;
        wave.waveTime = waveTime;
        return wave;
    }

    private class Level
    {
        public GameObject Root;
        public GameManager Game;
        public EnemyManager Enemies;
        public PlayerHealth Player;
        public PathCreator SoldierPath, VehiclePath, PlanePath;
    }

    // Mirrors a level's GameManager object, but with test waves and paths.
    private Level BuildLevel(float startTimeOffset, params WaveProperties[] waves)
    {
        // Deliberately NOT named "GameManager": enemies must get the level injected, not look it up by name.
        var level = new Level { Root = Track(new GameObject("LevelRig")) };
        level.Root.SetActive(false);
        level.Game = level.Root.AddComponent<GameManager>();
        level.Enemies = level.Root.AddComponent<EnemyManager>();
        level.Player = level.Root.AddComponent<PlayerHealth>();
        var money = level.Root.GetComponent<MoneyScript>();

        level.Game.winningMsg = Ui<TextMeshProUGUI>("Win").GetComponent<TextMeshProUGUI>();
        level.Game.loosingMsg = Ui<TextMeshProUGUI>("Lose").GetComponent<TextMeshProUGUI>();
        level.Game.MenuReturnTime = 1000f; // Never leave the test scene.
        level.Game.enemyManager = level.Enemies;
        money.moneyText = Ui<TextMeshProUGUI>("Money").GetComponent<TextMeshProUGUI>();
        level.Player.healthText = Ui<TextMeshProUGUI>("Health").GetComponent<TextMeshProUGUI>();
        level.Player.damageImage = Ui<Image>("Damage").GetComponent<Image>();
        level.Player.startingHealth = 10;

        level.SoldierPath = NewPath("SoldierPath", new Vector2(0, 0));
        level.VehiclePath = NewPath("VehiclePath", new Vector2(0, 500));
        level.PlanePath = NewPath("PlanePath", new Vector2(0, 1000));
        level.Enemies.playerHealth = level.Player;
        level.Enemies.manager = level.Game;
        level.Enemies.startTimeOffset = startTimeOffset;
        level.Enemies.waves = waves;
        level.Enemies.soldierPaths = new[] { level.SoldierPath };
        level.Enemies.vehiclePaths = new[] { level.VehiclePath };
        level.Enemies.planePaths = new[] { level.PlanePath };
        return level;
    }

    [UnityTest]
    public IEnumerator Waves_SpawnInOrderOnScheduleAndCompleteAfterLastWaveTime()
    {
        var soldier = Template<Walker>("Soldier");
        var tank = Template<Vehicle>("Tank");
        var plane = Template<Aircraft>("Plane");
        var level = BuildLevel(0.3f,
            Wave(1.0f, Sub(soldier, 3, 0.2f, 1.0f), Sub(tank, 2, 0.2f, 99f)), // last subwave: waveTime applies
            Wave(0.5f, Sub(plane, 1, 0.2f, 99f)));

        level.Root.SetActive(true);
        float t0 = Time.time;

        // Expected schedule (seconds after start), derived from the wave data:
        // wave 1 at 0.3: soldiers at 0.3/0.5/0.7; next subwave 1.0 later -> tanks at 1.3/1.5;
        // wave 2 starts waveTime(1.0) after the last subwave -> plane at 2.3;
        // completion waveTime(0.5) after wave 2's last subwave -> 2.8.
        var expected = new[] { ("Soldier", 0.3f), ("Soldier", 0.5f), ("Soldier", 0.7f), ("Tank", 1.3f), ("Tank", 1.5f), ("Plane", 2.3f) };

        while (Time.time - t0 < 2.6f) yield return null;
        Assert.That(level.Enemies.WavesCompleted, Is.False, "Waves must not complete before the last waveTime elapses.");
        while (Time.time - t0 < 3.0f) yield return null;
        Assert.That(level.Enemies.WavesCompleted, Is.True, "Waves should complete about waveTime after the last subwave.");

        var log = SpawnProbe.Log;
        Assert.That(log.Select(e => e.enemy.name), Is.EqualTo(expected.Select(e => e.Item1)), "spawn order");
        for (int i = 0; i < expected.Length; i++)
            Assert.That(log[i].time - t0, Is.EqualTo(expected[i].Item2).Within(Tolerance), $"spawn #{i} ({expected[i].Item1}) time");

        // Each enemy type follows its own path list.
        foreach (var (_, enemy) in log)
        {
            var walker = enemy.GetComponent<WalkerScript>();
            var expectedPath = enemy.name == "Soldier" ? level.SoldierPath : enemy.name == "Tank" ? level.VehiclePath : level.PlanePath;
            Assert.That(walker.pathCreator, Is.SameAs(expectedPath), $"{enemy.name} path");
        }

        // Killing an enemy pays its reward into the level's MoneyScript.
        var money = level.Root.GetComponent<MoneyScript>();
        int before = money.GetWealth();
        log[0].enemy.GetComponent<Health>().TakeDamage(1000);
        yield return null;
        Assert.That(money.GetWealth(), Is.EqualTo(before + 5), "kill reward");
        Assert.That(log[0].enemy.activeSelf, Is.False, "killed enemy returned to the pool");

        // Not won while enemies are still alive...
        yield return null;
        Assert.That(level.Game.winningMsg.gameObject.activeSelf, Is.False, "won while enemies alive");

        // ...but won once every spawned enemy is gone.
        foreach (var (_, enemy) in log)
            if (enemy.activeSelf) enemy.GetComponent<EnemyScript>().Destroy(true);
        yield return null;
        yield return null;
        Assert.That(level.Game.winningMsg.gameObject.activeSelf, Is.True, "level should be won");
    }

    [UnityTest]
    public IEnumerator PlayerDeath_StopsFurtherSpawns()
    {
        var soldier = Template<Walker>("Soldier");
        var level = BuildLevel(0.1f, Wave(1.0f, Sub(soldier, 6, 0.3f, 99f)));

        level.Root.SetActive(true);
        while (SpawnProbe.Log.Count < 2) yield return null;
        level.Player.TakeDamage(1000);
        int spawnedAtDeath = SpawnProbe.Log.Count;
        Assert.That(level.Game.loosingMsg.gameObject.activeSelf, Is.True, "game over shown");

        yield return new WaitForSeconds(1.2f); // Four more spawn intervals.
        Assert.That(SpawnProbe.Log.Count, Is.EqualTo(spawnedAtDeath), "no spawns after the player died");
    }
}
