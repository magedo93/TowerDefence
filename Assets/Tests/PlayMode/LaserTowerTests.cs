using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using TowerDefence;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

public class LaserTowerTests
{
    private readonly List<Object> _created = new List<Object>();
    private MoneyScript _money;
    private LaserTowerDefinition _definition;

    private GameObject Inactive(string name)
    {
        var go = new GameObject(name);
        go.SetActive(false);
        _created.Add(go);
        return go;
    }

    [SetUp]
    public void SetUp()
    {
        var display = new GameObject("TestMoneyDisplay", typeof(RectTransform), typeof(TextMeshProUGUI));
        _created.Add(display);
        var manager = Inactive("GameManager");
        _money = manager.AddComponent<MoneyScript>();
        _money.moneyText = display.GetComponent<TextMeshProUGUI>();
        _money.startingWealth = 100;
        var gameManager = manager.AddComponent<GameManager>();
        gameManager.enabled = false;
        manager.SetActive(true);
        _definition = ScriptableObject.CreateInstance<LaserTowerDefinition>();
        _created.Add(_definition);
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = _created.Count - 1; i >= 0; i--)
            if (_created[i] != null) Object.DestroyImmediate(_created[i]);
        _created.Clear();
    }

    private LaserTowerScript Tower()
    {
        var root = Inactive("TestLaser");
        var gun = new GameObject("Gun");
        gun.transform.SetParent(root.transform, false);
        var tower = root.AddComponent<LaserTowerScript>();
        tower.definition = _definition;
        tower.enemyLayers = LayerMask.GetMask("LivingEnemy", "MachineEnemy");
        root.GetComponent<CircleCollider2D>().enabled = false;
        root.SetActive(true);
        return tower;
    }

    private EnemyScript Enemy(Vector2 position, string layer = "LivingEnemy", float progress = 0.5f)
    {
        var root = Inactive("TestEnemy");
        root.layer = LayerMask.NameToLayer(layer);
        root.transform.position = position;
        var enemy = root.AddComponent<EnemyScript>();
        root.GetComponent<Health>().startingHealth = 1000;
        var walker = root.AddComponent<WalkerScript>();
        walker.enabled = false; // Stationary test enemy; no scene path dependency.
        root.AddComponent<CircleCollider2D>().isTrigger = true;
        root.SetActive(true);
        walker.movementProgress = progress;
        Physics2D.SyncTransforms();
        return enemy;
    }

    private Health Health()
    {
        var health = Inactive("TestHealth").AddComponent<Health>();
        health.Initialize();
        return health;
    }

    [TestCase(30)]
    [TestCase(60)]
    [TestCase(144)]
    public void FractionalDamage_IsFrameRateIndependent(int frames)
    {
        var health = Health();
        for (int i = 0; i < frames; i++) health.TakeAccumulatedDamage(2f / frames);
        Assert.That(health.GetHealth(), Is.EqualTo(98));
    }

    [Test]
    public void FractionalDamage_CombinesSourcesAndResetsOnReuse()
    {
        var health = Health();
        health.TakeAccumulatedDamage(0.4f);
        health.TakeAccumulatedDamage(0.6f);
        Assert.That(health.GetHealth(), Is.EqualTo(99));
        health.TakeAccumulatedDamage(0.75f);
        health.Initialize();
        health.TakeAccumulatedDamage(0.25f);
        Assert.That(health.GetHealth(), Is.EqualTo(100));
    }

    [Test]
    public void IntegerProjectileDamage_RemainsUnchanged()
    {
        var health = Health();
        health.TakeDamage(1);
        health.TakeDamage(20);
        Assert.That(health.GetHealth(), Is.EqualTo(79));
    }

    [Test]
    public void Spending_RejectsNegativeAndUnaffordableAmounts()
    {
        Assert.That(_money.TrySpend(-1), Is.False);
        Assert.That(_money.TrySpend(101), Is.False);
        Assert.That(_money.GetWealth(), Is.EqualTo(100));
        Assert.That(_money.TrySpend(100), Is.True);
        Assert.That(_money.GetWealth(), Is.EqualTo(0));
    }

    [Test]
    public void Upgrades_ChargeOnceAndStopAtLevelThree()
    {
        var tower = Tower();
        Assert.That(tower.TryUpgrade(_money), Is.True);
        Assert.That(tower.CurrentLevel, Is.EqualTo(2));
        Assert.That(tower.TryUpgrade(_money), Is.True);
        Assert.That(tower.CurrentLevel, Is.EqualTo(3));
        Assert.That(_money.GetWealth(), Is.EqualTo(85));
        Assert.That(tower.TryUpgrade(_money), Is.False);
        Assert.That(_money.GetWealth(), Is.EqualTo(85));
    }

    [Test]
    public void Upgrade_WithInsufficientFundsDoesNotChangeTower()
    {
        var tower = Tower();
        _money.TrySpend(100);
        Assert.That(tower.TryUpgrade(_money), Is.False);
        Assert.That(tower.CurrentLevel, Is.EqualTo(1));
    }

    [Test]
    public void Placement_UsesDefinitionPriceAndRejectsAnOccupiedSpot()
    {
        var template = Tower();
        _definition.purchaseCost = 7;
        template.cost = 99; // Deliberately stale legacy field.
        template.gameObject.SetActive(false);
        var manager = _money.GetComponent<GameManager>();
        manager.SetTowerToBuild(template.gameObject);
        var spot = Inactive("TestBuildSpot").AddComponent<BuildSpotScript>();
        spot.manager = manager;
        spot.money = _money;
        spot.OnBuildSpotClicked();
        var built = spot.BuiltTower;
        Assert.That(built, Is.Not.Null);
        _created.Add(built);
        _created.Add(GameObject.Find("#ObjectPooler"));
        Assert.That(built.GetComponent<LaserTowerScript>(), Is.Not.Null);
        Assert.That(_money.GetWealth(), Is.EqualTo(93));
        spot.OnBuildSpotClicked();
        Assert.That(_money.GetWealth(), Is.EqualTo(93));
    }

    [Test]
    public void ObjectPool_ReusesLaserAndResetsItsUpgradeLevel()
    {
        var template = Tower();
        template.gameObject.SetActive(false);
        var first = ObjectPooler.InstantiatePooled(template.gameObject, Vector3.zero, Quaternion.identity);
        _created.Add(first);
        _created.Add(GameObject.Find("#ObjectPooler"));
        var tower = first.GetComponent<LaserTowerScript>();
        tower.TryUpgrade(_money);
        first.SetActive(false);
        var reused = ObjectPooler.InstantiatePooled(template.gameObject, new Vector3(50, 0), Quaternion.identity);
        Assert.That(reused, Is.SameAs(first));
        Assert.That(tower.CurrentLevel, Is.EqualTo(1));
        Assert.That(reused.transform.position, Is.EqualTo(new Vector3(50, 0)));
        Assert.That(reused.GetComponent<LineRenderer>().enabled, Is.False);
    }

    [Test]
    public void ReenabledTower_ResetsLevelAndBeam()
    {
        var tower = Tower();
        var beam = tower.GetComponent<LineRenderer>();
        tower.TryUpgrade(_money);
        beam.enabled = true;
        tower.gameObject.SetActive(false);
        Assert.That(beam.enabled, Is.False);
        tower.gameObject.SetActive(true);
        Assert.That(tower.CurrentLevel, Is.EqualTo(1));
        Assert.That(beam.enabled, Is.False);
    }

    [Test]
    public void Targeting_UsesGroundLayersAndFurthestProgress()
    {
        var tower = Tower();
        Enemy(new Vector2(50, 0), "LivingEnemy", 0.2f);
        var tank = Enemy(new Vector2(100, 0), "MachineEnemy", 0.8f);
        Enemy(new Vector2(20, 0), "FlyingEnemy", 0.99f);
        Assert.That(tower.SelectTarget(), Is.EqualTo(tank.gameObject));
    }

    [Test]
    public void UpgradeRange_AcquiresEnemiesAlreadyInsideExpandedRadius()
    {
        var tower = Tower();
        var enemy = Enemy(new Vector2(280, 0));
        Assert.That(tower.SelectTarget(), Is.Null);
        tower.TryUpgrade(_money);
        Assert.That(tower.SelectTarget(), Is.EqualTo(enemy.gameObject));
        enemy.gameObject.SetActive(false);
        Assert.That(tower.SelectTarget(), Is.Null);
    }

    [UnityTest]
    public IEnumerator AllLevels_SupportLaserPlacementAndUpgradeUIAlongsideExistingTowers()
    {
        foreach (var sceneName in new[] { "Level01", "Level02", "Level03" })
        {
            yield return SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            yield return null;
            var managers = Object.FindObjectsByType<GameManager>();
            Assert.That(managers.Length, Is.EqualTo(1), sceneName);
            var manager = managers[0];
            var money = manager.GetComponent<MoneyScript>();
            int selectors = 0;
            var projectileTowers = new Dictionary<string, GameObject>();
            foreach (var button in Object.FindObjectsByType<Button>())
            {
                bool selectsTower = false;
                for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                    selectsTower |= button.onClick.GetPersistentMethodName(i) == "SetTowerToBuild";
                if (!selectsTower) continue;
                selectors++;
                button.onClick.Invoke();
                var prefab = manager.GetTowerToBuild();
                var tower = prefab.GetComponent<TowerScript>();
                Assert.That(tower, Is.Not.Null);
                if (!(tower is LaserTowerScript)) projectileTowers[prefab.name] = prefab;
                if (prefab.name == "BulletTower")
                {
                    Assert.That(tower.BuildCost, Is.EqualTo(3));
                    Assert.That(tower.Cooldown, Is.EqualTo(0.5f));
                }
                else if (prefab.name == "RocketTower")
                {
                    Assert.That(tower.BuildCost, Is.EqualTo(9));
                    Assert.That(tower.Cooldown, Is.EqualTo(5));
                }
            }
            Assert.That(selectors, Is.EqualTo(3), sceneName);
            GameObject.Find("LaserTowerButton").GetComponent<Button>().onClick.Invoke();
            Assert.That(manager.GetTowerToBuild().GetComponent<LaserTowerScript>(), Is.Not.Null);
            var spots = Object.FindObjectsByType<BuildSpotScript>();
            Assert.That(spots.Length, Is.GreaterThan(0));
            var spot = spots[0];
            int before = money.GetWealth();
            spot.OnBuildSpotClicked();
            Assert.That(money.GetWealth(), Is.EqualTo(before - 6), sceneName);
            var laser = spot.BuiltTower.GetComponent<LaserTowerScript>();
            Assert.That(laser, Is.Not.Null);
            money.AddBudget(30);
            spot.OnBuildSpotClicked();
            var panel = spot.upgradePanel;
            Assert.That(panel.gameObject.activeInHierarchy, Is.True, sceneName);
            Assert.That(panel.title.text, Is.EqualTo("LASER TOWER  -  LEVEL 1"), sceneName);
            int upgradeBudget = money.GetWealth();
            panel.Upgrade();
            panel.Upgrade();
            Assert.That(laser.CurrentLevel, Is.EqualTo(3), sceneName);
            Assert.That(money.GetWealth(), Is.EqualTo(upgradeBudget - 15), sceneName);
            Assert.That(panel.upgradeButton.interactable, Is.False, sceneName);
            panel.Close();
            Assert.That(panel.gameObject.activeSelf, Is.False, sceneName);

            // Every other tower type upgrades through the same panel.
            Assert.That(projectileTowers.Keys, Is.EquivalentTo(new[] { "BulletTower", "RocketTower" }), sceneName);
            Assert.That(spots.Length, Is.GreaterThanOrEqualTo(3), sceneName);
            var expected = new Dictionary<string, (string title, int upgrades)>
            {
                { "BulletTower", ("BULLET TOWER", 3 + 5) },
                { "RocketTower", ("ROCKET TOWER", 9 + 14) },
            };
            int spotIndex = 1;
            foreach (var entry in expected)
            {
                var projectileSpot = spots[spotIndex++];
                money.AddBudget(50);
                manager.SetTowerToBuild(projectileTowers[entry.Key]);
                projectileSpot.OnBuildSpotClicked();                 // build
                var built = projectileSpot.BuiltTower.GetComponent<TowerScript>();
                Assert.That(built.CurrentLevel, Is.EqualTo(1), $"{sceneName} {entry.Key}");
                projectileSpot.OnBuildSpotClicked();                 // open its upgrade panel
                Assert.That(projectileSpot.upgradePanel.Tower, Is.SameAs(built), $"{sceneName} {entry.Key}");
                Assert.That(panel.title.text, Is.EqualTo(entry.Value.title + "  -  LEVEL 1"), $"{sceneName} {entry.Key}");
                Assert.That(panel.upgradeButton.interactable, Is.True, $"{sceneName} {entry.Key}");
                int beforeUpgrades = money.GetWealth();
                panel.Upgrade();
                panel.Upgrade();
                Assert.That(built.CurrentLevel, Is.EqualTo(3), $"{sceneName} {entry.Key}");
                Assert.That(money.GetWealth(), Is.EqualTo(beforeUpgrades - entry.Value.upgrades), $"{sceneName} {entry.Key} cost");
                Assert.That(panel.title.text, Is.EqualTo(entry.Value.title + "  -  LEVEL 3"), $"{sceneName} {entry.Key}");
                Assert.That(panel.upgradeText.text, Is.EqualTo("MAX LEVEL"), $"{sceneName} {entry.Key}");
                Assert.That(panel.upgradeButton.interactable, Is.False, $"{sceneName} {entry.Key}");
                panel.Close();
            }
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
        var health = enemy.GetComponent<Health>();
        yield return new WaitForSeconds(0.65f);
        Assert.That(health.GetHealth(), Is.LessThan(1000));
        Assert.That(tower.GetComponent<LineRenderer>().enabled, Is.True);
        enemy.transform.position = new Vector2(1000, 0);
        Physics2D.SyncTransforms();
        yield return null;
        yield return null;
        int afterLeaving = health.GetHealth();
        Assert.That(tower.GetComponent<LineRenderer>().enabled, Is.False);
        yield return new WaitForSeconds(0.55f);
        Assert.That(health.GetHealth(), Is.EqualTo(afterLeaving));
    }
}
