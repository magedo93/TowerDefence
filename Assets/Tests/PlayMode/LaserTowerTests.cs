using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

// Runtime code remains in the project's predefined Assembly-CSharp assembly.
// Reflection keeps test assembly boundaries from forcing a gameplay-wide rewrite.
public class LaserTowerTests
{
    private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();
    private Component _money;
    private ScriptableObject _definition;

    private static Type RuntimeType(string name)
    {
        return Type.GetType(name + ", Assembly-CSharp", true);
    }

    private static object Call(object target, string method, params object[] arguments)
    {
        return target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Invoke(target, arguments);
    }

    private static void Set(object target, string field, object value)
    {
        target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(target, value);
    }

    private static T Property<T>(object target, string name)
    {
        return (T)target.GetType().GetProperty(name).GetValue(target);
    }

    private GameObject Object(string name)
    {
        var go = new GameObject(name);
        go.SetActive(false);
        _created.Add(go);
        return go;
    }

    [SetUp]
    public void SetUp()
    {
        var display = new GameObject("TestMoneyDisplay", typeof(RectTransform), typeof(Text));
        _created.Add(display);
        var manager = Object("GameManager");
        _money = manager.AddComponent(RuntimeType("MoneyScript"));
        Set(_money, "moneyText", display.GetComponent<Text>());
        Set(_money, "startingWealth", 100);
        var gameManager = (Behaviour)manager.AddComponent(RuntimeType("GameManager"));
        gameManager.enabled = false;
        ((Behaviour)manager.GetComponent(RuntimeType("EnemyManager"))).enabled = false;
        manager.SetActive(true);
        _definition = ScriptableObject.CreateInstance(RuntimeType("LaserTowerDefinition"));
        _created.Add(_definition);
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = _created.Count - 1; i >= 0; i--)
            if (_created[i] != null) UnityEngine.Object.DestroyImmediate(_created[i]);
        _created.Clear();
    }

    private Component Tower()
    {
        var root = Object("TestLaser");
        var gun = new GameObject("Gun");
        gun.transform.SetParent(root.transform, false);
        var tower = root.AddComponent(RuntimeType("LaserTowerScript"));
        Set(tower, "definition", _definition);
        Set(tower, "enemyLayers", (LayerMask)LayerMask.GetMask("LivingEnemy", "MachineEnemy"));
        root.GetComponent<CircleCollider2D>().enabled = false;
        root.SetActive(true);
        return tower;
    }

    private Component Enemy(Vector2 position, string layer = "LivingEnemy", float progress = 0.5f)
    {
        var root = Object("TestEnemy");
        root.layer = LayerMask.NameToLayer(layer);
        root.transform.position = position;
        var enemy = root.AddComponent(RuntimeType("EnemyScript"));
        var health = root.GetComponent(RuntimeType("Health"));
        Set(health, "startingHealth", 1000);
        var walker = (Behaviour)root.AddComponent(RuntimeType("WalkerScript"));
        walker.enabled = false; // Stationary test enemy; no scene path dependency.
        root.AddComponent<CircleCollider2D>().isTrigger = true;
        root.SetActive(true);
        Set(walker, "movementProgress", progress);
        Physics2D.SyncTransforms();
        return enemy;
    }

    private Component Health()
    {
        var root = Object("TestHealth");
        var health = root.AddComponent(RuntimeType("Health"));
        Call(health, "Initialize");
        return health;
    }

    [TestCase(30)]
    [TestCase(60)]
    [TestCase(144)]
    public void FractionalDamage_IsFrameRateIndependent(int frames)
    {
        var health = Health();
        for (int i = 0; i < frames; i++) Call(health, "TakeAccumulatedDamage", 2f / frames);
        Assert.That(Call(health, "GetHealth"), Is.EqualTo(98));
    }

    [Test]
    public void FractionalDamage_CombinesSourcesAndResetsOnReuse()
    {
        var health = Health();
        Call(health, "TakeAccumulatedDamage", 0.4f);
        Call(health, "TakeAccumulatedDamage", 0.6f);
        Assert.That(Call(health, "GetHealth"), Is.EqualTo(99));
        Call(health, "TakeAccumulatedDamage", 0.75f);
        Call(health, "Initialize");
        Call(health, "TakeAccumulatedDamage", 0.25f);
        Assert.That(Call(health, "GetHealth"), Is.EqualTo(100));
    }

    [Test]
    public void IntegerProjectileDamage_RemainsUnchanged()
    {
        var health = Health();
        Call(health, "TakeDamage", 1);
        Call(health, "TakeDamage", 20);
        Assert.That(Call(health, "GetHealth"), Is.EqualTo(79));
    }

    [Test]
    public void Spending_RejectsNegativeAndUnaffordableAmounts()
    {
        Assert.That(Call(_money, "TrySpend", -1), Is.False);
        Assert.That(Call(_money, "TrySpend", 101), Is.False);
        Assert.That(Call(_money, "GetWealth"), Is.EqualTo(100));
        Assert.That(Call(_money, "TrySpend", 100), Is.True);
        Assert.That(Call(_money, "GetWealth"), Is.EqualTo(0));
    }

    [Test]
    public void Upgrades_ChargeOnceAndStopAtLevelThree()
    {
        var tower = Tower();
        Assert.That(Call(tower, "TryUpgrade", _money), Is.True);
        Assert.That(Property<int>(tower, "CurrentLevel"), Is.EqualTo(2));
        Assert.That(Call(tower, "TryUpgrade", _money), Is.True);
        Assert.That(Property<int>(tower, "CurrentLevel"), Is.EqualTo(3));
        Assert.That(Call(_money, "GetWealth"), Is.EqualTo(85));
        Assert.That(Call(tower, "TryUpgrade", _money), Is.False);
        Assert.That(Call(_money, "GetWealth"), Is.EqualTo(85));
    }

    [Test]
    public void Upgrade_WithInsufficientFundsDoesNotChangeTower()
    {
        var tower = Tower();
        Call(_money, "TrySpend", 100);
        Assert.That(Call(tower, "TryUpgrade", _money), Is.False);
        Assert.That(Property<int>(tower, "CurrentLevel"), Is.EqualTo(1));
    }

    [Test]
    public void Placement_UsesDefinitionPriceAndRejectsAnOccupiedSpot()
    {
        var template = Tower();
        Set(_definition, "purchaseCost", 7);
        Set(template, "cost", 99); // Deliberately stale legacy field.
        template.gameObject.SetActive(false);
        var manager = _money.GetComponent(RuntimeType("GameManager"));
        Call(manager, "SetTowerToBuild", template.gameObject);
        var spotObject = Object("TestBuildSpot");
        var spot = spotObject.AddComponent(RuntimeType("BuildSpotScript"));
        Set(spot, "manager", manager);
        Set(spot, "money", _money);
        Call(spot, "OnBuildSpotClicked");
        var built = (GameObject)spot.GetType().GetField("_build", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(spot);
        Assert.That(built, Is.Not.Null);
        _created.Add(built);
        _created.Add(GameObject.Find("#ObjectPooler"));
        Assert.That(built.GetComponent(RuntimeType("LaserTowerScript")), Is.Not.Null);
        Assert.That(Call(_money, "GetWealth"), Is.EqualTo(93));
        Call(spot, "OnBuildSpotClicked");
        Assert.That(Call(_money, "GetWealth"), Is.EqualTo(93));
    }

    [Test]
    public void ObjectPool_ReusesLaserAndResetsItsUpgradeLevel()
    {
        var template = Tower();
        template.gameObject.SetActive(false);
        var spawn = RuntimeType("ObjectPooler").GetMethod("InstantiatePooled", new[] { typeof(GameObject), typeof(Vector3), typeof(Quaternion) });
        var first = (GameObject)spawn.Invoke(null, new object[] { template.gameObject, Vector3.zero, Quaternion.identity });
        _created.Add(first);
        _created.Add(GameObject.Find("#ObjectPooler"));
        var tower = first.GetComponent(RuntimeType("LaserTowerScript"));
        Call(tower, "TryUpgrade", _money);
        first.SetActive(false);
        var reused = (GameObject)spawn.Invoke(null, new object[] { template.gameObject, new Vector3(50, 0), Quaternion.identity });
        Assert.That(reused, Is.SameAs(first));
        Assert.That(Property<int>(tower, "CurrentLevel"), Is.EqualTo(1));
        Assert.That(reused.transform.position, Is.EqualTo(new Vector3(50, 0)));
        Assert.That(reused.GetComponent<LineRenderer>().enabled, Is.False);
    }

    [Test]
    public void ReenabledTower_ResetsLevelAndBeam()
    {
        var tower = Tower();
        Call(tower, "TryUpgrade", _money);
        tower.gameObject.GetComponent<LineRenderer>().enabled = true;
        tower.gameObject.SetActive(false);
        Assert.That(tower.gameObject.GetComponent<LineRenderer>().enabled, Is.False);
        tower.gameObject.SetActive(true);
        Assert.That(Property<int>(tower, "CurrentLevel"), Is.EqualTo(1));
        Assert.That(tower.gameObject.GetComponent<LineRenderer>().enabled, Is.False);
    }

    [Test]
    public void Targeting_UsesGroundLayersAndFurthestProgress()
    {
        var tower = Tower();
        Enemy(new Vector2(50, 0), "LivingEnemy", 0.2f);
        var tank = Enemy(new Vector2(100, 0), "MachineEnemy", 0.8f);
        Enemy(new Vector2(20, 0), "FlyingEnemy", 0.99f);
        Assert.That(Call(tower, "SelectTarget"), Is.EqualTo(tank.gameObject));
    }

    [Test]
    public void UpgradeRange_AcquiresEnemiesAlreadyInsideExpandedRadius()
    {
        var tower = Tower();
        var enemy = Enemy(new Vector2(280, 0));
        Assert.That(Call(tower, "SelectTarget"), Is.Null);
        Call(tower, "TryUpgrade", _money);
        Assert.That(Call(tower, "SelectTarget"), Is.EqualTo(enemy.gameObject));
        enemy.gameObject.SetActive(false);
        Assert.That(Call(tower, "SelectTarget"), Is.Null);
    }

    [UnityTest]
    public IEnumerator AllLevels_SupportLaserPlacementAndUpgradeUIAlongsideExistingTowers()
    {
        foreach (var sceneName in new[] { "Level01", "Level02", "Level03" })
        {
            yield return SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            yield return null;
            var managers = UnityEngine.Object.FindObjectsByType(RuntimeType("GameManager"), FindObjectsSortMode.None);
            Assert.That(managers.Length, Is.EqualTo(1), sceneName);
            var manager = (Component)managers[0];
            var money = manager.GetComponent(RuntimeType("MoneyScript"));
            var buttons = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None);
            int selectors = 0;
            foreach (var button in buttons)
            {
                bool selectsTower = false;
                for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                    selectsTower |= button.onClick.GetPersistentMethodName(i) == "SetTowerToBuild";
                if (!selectsTower) continue;
                selectors++;
                button.onClick.Invoke();
                var prefab = (GameObject)Call(manager, "GetTowerToBuild");
                var tower = prefab.GetComponent(RuntimeType("TowerScript"));
                Assert.That(tower, Is.Not.Null);
                if (prefab.name == "BulletTower")
                {
                    Assert.That(Property<int>(tower, "BuildCost"), Is.EqualTo(3));
                    Assert.That((float)tower.GetType().GetField("cooldown").GetValue(tower), Is.EqualTo(0.5f));
                }
                else if (prefab.name == "RocketTower")
                {
                    Assert.That(Property<int>(tower, "BuildCost"), Is.EqualTo(9));
                    Assert.That((float)tower.GetType().GetField("cooldown").GetValue(tower), Is.EqualTo(5));
                }
            }
            Assert.That(selectors, Is.EqualTo(3), sceneName);
            GameObject.Find("LaserTowerButton").GetComponent<Button>().onClick.Invoke();
            var selected = (GameObject)Call(manager, "GetTowerToBuild");
            Assert.That(selected.GetComponent(RuntimeType("LaserTowerScript")), Is.Not.Null);
            var spots = UnityEngine.Object.FindObjectsByType(RuntimeType("BuildSpotScript"), FindObjectsSortMode.None);
            Assert.That(spots.Length, Is.GreaterThan(0));
            var spot = (Component)spots[0];
            int before = (int)Call(money, "GetWealth");
            Call(spot, "OnBuildSpotClicked");
            Assert.That(Call(money, "GetWealth"), Is.EqualTo(before - 6), sceneName);
            var built = (GameObject)spot.GetType().GetField("_build", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(spot);
            var laser = built.GetComponent(RuntimeType("LaserTowerScript"));
            Assert.That(laser, Is.Not.Null);
            Call(money, "AddBudget", 30);
            Call(spot, "OnBuildSpotClicked");
            var panel = (Component)spot.GetType().GetField("laserUpgradePanel").GetValue(spot);
            Assert.That(panel.gameObject.activeInHierarchy, Is.True, sceneName);
            int upgradeBudget = (int)Call(money, "GetWealth");
            Call(panel, "Upgrade");
            Call(panel, "Upgrade");
            Assert.That(Property<int>(laser, "CurrentLevel"), Is.EqualTo(3), sceneName);
            Assert.That(Call(money, "GetWealth"), Is.EqualTo(upgradeBudget - 15), sceneName);
            var upgradeButton = (Button)panel.GetType().GetField("upgradeButton").GetValue(panel);
            Assert.That(upgradeButton.interactable, Is.False, sceneName);
            Call(panel, "Close");
            Assert.That(panel.gameObject.activeSelf, Is.False, sceneName);
        }
        var lastLevel = SceneManager.GetActiveScene();
        var empty = SceneManager.CreateScene("LaserTestCleanup");
        SceneManager.SetActiveScene(empty);
        yield return SceneManager.UnloadSceneAsync(lastLevel);
    }

    [UnityTest]
    public IEnumerator ContinuousBeam_DamagesTargetAndStopsWhenItLeavesRange()
    {
        var tower = Tower();
        var enemy = Enemy(new Vector2(100, 0));
        var health = enemy.GetComponent(RuntimeType("Health"));
        yield return new WaitForSeconds(0.65f);
        Assert.That((int)Call(health, "GetHealth"), Is.LessThan(1000));
        Assert.That(tower.GetComponent<LineRenderer>().enabled, Is.True);
        enemy.transform.position = new Vector2(1000, 0);
        Physics2D.SyncTransforms();
        yield return null;
        yield return null;
        int afterLeaving = (int)Call(health, "GetHealth");
        Assert.That(tower.GetComponent<LineRenderer>().enabled, Is.False);
        yield return new WaitForSeconds(0.55f);
        Assert.That(Call(health, "GetHealth"), Is.EqualTo(afterLeaving));
    }
}
