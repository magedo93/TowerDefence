using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TowerDefence;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

// Regression tests for the Phase 1 modernization fixes and the pooling contract.
public class CoreGameplayTests
{
    private readonly List<Object> _created = new List<Object>();

    private GameObject Track(GameObject go)
    {
        _created.Add(go);
        return go;
    }

    private GameObject Inactive(string name)
    {
        var go = new GameObject(name);
        go.SetActive(false);
        return Track(go);
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = _created.Count - 1; i >= 0; i--)
            if (_created[i] != null) Object.DestroyImmediate(_created[i]);
        _created.Clear();
    }

    // ---------- Object pooling ----------

    [Test]
    public void ObjectPooler_ReusesInactiveInstance_AndCreatesNewWhenAllActive()
    {
        var template = Track(new GameObject("PoolTemplate"));

        var first = Track(ObjectPooler.InstantiatePooled(template, Vector3.one, Quaternion.identity));
        Assert.That(first, Is.Not.SameAs(template));
        Assert.That(first.activeSelf, Is.True);
        Assert.That(first.transform.position, Is.EqualTo(Vector3.one));

        // While the first instance is active, a second request needs a new instance.
        var second = Track(ObjectPooler.InstantiatePooled(template, Vector3.zero, Quaternion.identity));
        Assert.That(second, Is.Not.SameAs(first));

        // "Destroying" a pooled object means deactivating it; it is then reused.
        first.SetActive(false);
        var reused = ObjectPooler.InstantiatePooled(template, Vector3.up, Quaternion.identity);
        Assert.That(reused, Is.SameAs(first));
        Assert.That(reused.activeSelf, Is.True);
        Assert.That(reused.transform.position, Is.EqualTo(Vector3.up));
    }

    // ---------- Coins ----------

    private MoneyScript Money(out GameObject coinTemplate, int coinPrice)
    {
        var display = Track(new GameObject("TestMoneyDisplay", typeof(RectTransform), typeof(Text)));
        var manager = Inactive("GameManager");
        var money = manager.AddComponent<MoneyScript>();
        money.moneyText = display.GetComponent<Text>();
        money.startingWealth = 0;
        money.goldProbability = 1f;
        money.coinAudio = AudioClip.Create("TestCoin", 32, 1, 44100, false);

        coinTemplate = Inactive("TestGoldCoin");
        foreach (var child in new[] { "Coin", "Shadow" })
        {
            var go = new GameObject(child, typeof(SpriteRenderer));
            go.transform.SetParent(coinTemplate.transform, false);
        }
        var coin = coinTemplate.AddComponent<CoinScript>();
        coin.price = coinPrice;
        coin.duration = 100f; // Long enough not to expire during the test.
        coinTemplate.AddComponent<EventTrigger>();
        money.goldPrefab = coinTemplate;

        // The pooler re-activates its template after cloning; keep that harmless.
        coinTemplate.SetActive(true);
        manager.SetActive(true);
        return money;
    }

    private static GameObject SpawnedCoin(GameObject template)
    {
        foreach (var coin in Object.FindObjectsByType<CoinScript>(FindObjectsInactive.Exclude))
            if (coin.gameObject != template) return coin.gameObject;
        return null;
    }

    private static void Click(GameObject go)
    {
        go.GetComponent<EventTrigger>().OnPointerClick(new PointerEventData(null));
    }

    [UnityTest]
    public IEnumerator Coin_ReusedFromPool_PaysOutOnlyOncePerClick()
    {
        var money = Money(out var template, coinPrice: 3);
        yield return null;

        money.CreateCoin(Vector3.zero);
        var coin = Track(SpawnedCoin(template));
        Assert.That(coin, Is.Not.Null, "CreateCoin should spawn a pooled coin.");
        Click(coin);
        Assert.That(money.GetWealth(), Is.EqualTo(3));

        // Pick-up deactivates the coin (the prefab does this via a persistent SetActive(false)).
        coin.SetActive(false);

        // Reuse the same pooled coin a few times; each click must pay exactly once.
        for (int use = 2; use <= 4; use++)
        {
            money.CreateCoin(Vector3.zero);
            var reused = SpawnedCoin(template);
            Assert.That(reused, Is.SameAs(coin), "The deactivated coin should be reused from the pool.");
            Click(reused);
            Assert.That(money.GetWealth(), Is.EqualTo(3 * use), $"Use #{use} paid more than once.");
            reused.SetActive(false);
        }
    }

    // ---------- Tower targeting ----------

    private TowerScript CannonTower()
    {
        var root = Inactive("TestTower");
        var gun = new GameObject("Gun");
        gun.transform.SetParent(root.transform, false);
        var tower = root.AddComponent<TowerScript>();
        tower.cooldown = 1000f; // Never fire during the test.
        root.SetActive(true);
        return tower;
    }

    private GameObject Walker(float progress)
    {
        var root = Inactive("TestWalker");
        var walker = root.AddComponent<WalkerScript>();
        walker.enabled = false; // Stationary; no PathCreator needed.
        root.AddComponent<CircleCollider2D>().isTrigger = true;
        root.SetActive(true);
        walker.movementProgress = progress;
        return root;
    }

    [UnityTest]
    public IEnumerator Tower_ReusedAfterDestroy_StillAimsItsGun()
    {
        var tower = CannonTower();
        tower.rotationSpeed = 1000f;
        var gun = tower.transform.Find("Gun");

        tower.Destroy();                 // pooled "destroy"
        tower.gameObject.SetActive(true); // pooled reuse

        var enemy = Walker(0.5f);
        enemy.transform.position = tower.transform.position + new Vector3(100, 0, 0);
        tower.OnTriggerEnter2D(enemy.GetComponent<Collider2D>());
        var before = gun.rotation;
        yield return null;
        yield return null;
        Assert.That(Quaternion.Angle(before, gun.rotation), Is.GreaterThan(1f), "gun should turn toward the target after reuse");
    }

    // A projectile tower driven by a fresh definition: level 1 = range 123, cooldown 0.25s,
    // damage 7, price 4; one upgrade (cost 2) to range 200, cooldown 0.1s, damage 9.
    private TowerScript DefinedProjectileTower(out GameObject bulletTemplate)
    {
        // Projectile template under an inactive parent, like a prefab asset.
        var holder = Inactive("Templates");
        bulletTemplate = new GameObject("TestProjectile");
        bulletTemplate.transform.SetParent(holder.transform, false);
        bulletTemplate.AddComponent<BulletScript>().damage = 1;

        var definition = ScriptableObject.CreateInstance<ProjectileTowerDefinition>();
        _created.Add(definition);
        definition.displayName = "Test Cannon";
        definition.purchaseCost = 4;
        definition.range = 123f;
        definition.cooldown = 0.25f;
        definition.damage = 7;
        definition.projectilePrefab = bulletTemplate;
        definition.upgrades = new[]
        {
            new ProjectileTowerDefinition.Upgrade { upgradeCost = 2, range = 200f, cooldown = 0.1f, damage = 9 }
        };

        var root = Inactive("TestTower");
        new GameObject("Gun").transform.SetParent(root.transform, false);
        root.AddComponent<CircleCollider2D>().radius = 999f;
        var tower = root.AddComponent<TowerScript>();
        tower.cost = 99;      // legacy values must be ignored
        tower.cooldown = 99f;
        tower.definition = definition;
        root.SetActive(true);
        return tower;
    }

    private readonly HashSet<BulletScript> _seenProjectiles = new HashSet<BulletScript>();

    private BulletScript Fire(TowerScript tower, GameObject template)
    {
        tower.Shoot(Track(new GameObject("Target")));
        foreach (var bullet in Object.FindObjectsByType<BulletScript>(FindObjectsInactive.Exclude))
        {
            if (bullet.gameObject == template || !_seenProjectiles.Add(bullet)) continue;
            Track(bullet.gameObject);
            return bullet;
        }
        return null;
    }

    private MoneyScript Wallet(int coins)
    {
        var display = Track(new GameObject("TestMoneyDisplay", typeof(RectTransform), typeof(Text)));
        var go = Inactive("TestWallet");
        var money = go.AddComponent<MoneyScript>();
        money.moneyText = display.GetComponent<Text>();
        money.startingWealth = coins;
        go.SetActive(true);
        return money;
    }

    [Test]
    public void ProjectileTower_TakesRangeCooldownAndDamageFromItsDefinition()
    {
        var tower = DefinedProjectileTower(out var template);

        Assert.That(tower.BuildCost, Is.EqualTo(4), "cost from definition");
        Assert.That(tower.Cooldown, Is.EqualTo(0.25f), "cooldown from definition");
        Assert.That(tower.GetComponent<CircleCollider2D>().radius, Is.EqualTo(123f), "range applied to the collider");
        Assert.That(tower.DisplayName, Is.EqualTo("Test Cannon"));

        var fired = Fire(tower, template);
        Assert.That(fired, Is.Not.Null, "a projectile was fired");
        Assert.That(fired.damage, Is.EqualTo(7), "damage from definition");
        Assert.That(fired.source, Is.SameAs(tower.gameObject));
    }

    [Test]
    public void ProjectileTower_UpgradeAppliesNextLevel_StopsAtMax_ResetsOnReuse()
    {
        var tower = DefinedProjectileTower(out var template);
        var money = Wallet(10);
        var range = tower.GetComponent<CircleCollider2D>();
        int levelChanges = 0;
        tower.LevelChanged += () => levelChanges++;

        Assert.That(tower.MaxLevel, Is.EqualTo(2));
        Assert.That(tower.CanUpgrade, Is.True);
        Assert.That(tower.NextUpgradeCost, Is.EqualTo(2));

        Assert.That(tower.TryUpgrade(money), Is.True);
        Assert.That(tower.CurrentLevel, Is.EqualTo(2));
        Assert.That(money.GetWealth(), Is.EqualTo(8), "charged once");
        Assert.That(levelChanges, Is.EqualTo(1));
        Assert.That(range.radius, Is.EqualTo(200f), "upgraded range");
        Assert.That(tower.Cooldown, Is.EqualTo(0.1f), "upgraded cooldown");
        Assert.That(Fire(tower, template).damage, Is.EqualTo(9), "upgraded damage");

        Assert.That(tower.CanUpgrade, Is.False, "max level");
        Assert.That(tower.TryUpgrade(money), Is.False);
        Assert.That(money.GetWealth(), Is.EqualTo(8), "no charge at max level");

        // Pooled reuse starts again at level 1.
        tower.gameObject.SetActive(false);
        tower.gameObject.SetActive(true);
        Assert.That(tower.CurrentLevel, Is.EqualTo(1));
        Assert.That(range.radius, Is.EqualTo(123f));
        Assert.That(tower.Cooldown, Is.EqualTo(0.25f));
    }

    [Test]
    public void ProjectileTower_UpgradeNeedsEnoughCoins()
    {
        var tower = DefinedProjectileTower(out _);
        var money = Wallet(1);
        Assert.That(tower.TryUpgrade(money), Is.False);
        Assert.That(tower.CurrentLevel, Is.EqualTo(1));
        Assert.That(money.GetWealth(), Is.EqualTo(1));
    }

    [Test]
    public void Tower_IgnoresNonEnemyColliders()
    {
        var tower = CannonTower();
        var bullet = Track(new GameObject("TestBullet", typeof(CircleCollider2D)));

        Assert.DoesNotThrow(() => tower.OnTriggerEnter2D(bullet.GetComponent<Collider2D>()));
        Assert.That(tower.GetMostDistantInRange(), Is.Null);
    }

    [Test]
    public void Tower_TargetsMostAdvancedActiveEnemy_AndPrunesInactive()
    {
        var tower = CannonTower();
        var behind = Walker(0.2f);
        var ahead = Walker(0.7f);
        tower.OnTriggerEnter2D(behind.GetComponent<Collider2D>());
        tower.OnTriggerEnter2D(ahead.GetComponent<Collider2D>());

        Assert.That(tower.GetMostDistantInRange(), Is.SameAs(ahead));

        ahead.SetActive(false); // Pooled enemy "destroyed".
        Assert.That(tower.GetMostDistantInRange(), Is.SameAs(behind));
    }
}
