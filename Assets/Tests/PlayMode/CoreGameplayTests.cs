using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

// Regression tests for the Phase 1 modernization fixes and the pooling contract.
// Same approach as LaserTowerTests: gameplay code lives in Assembly-CSharp, so it is
// reached through reflection instead of a direct assembly reference.
public class CoreGameplayTests
{
    private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();

    private static Type RuntimeType(string name)
    {
        return Type.GetType(name + ", Assembly-CSharp", true);
    }

    private static object Call(object target, string method, params object[] arguments)
    {
        return target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Invoke(target, arguments);
    }

    private static object CallStatic(string type, string method, params object[] arguments)
    {
        foreach (var m in RuntimeType(type).GetMethods(BindingFlags.Static | BindingFlags.Public))
        {
            if (m.Name == method && !m.IsGenericMethod && m.GetParameters().Length == arguments.Length)
                return m.Invoke(null, arguments);
        }
        throw new MissingMethodException(type, method);
    }

    private static void Set(object target, string field, object value)
    {
        FieldInfo info = null;
        for (var t = target.GetType(); t != null && info == null; t = t.BaseType)
            info = t.GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        info.SetValue(target, value);
    }

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
            if (_created[i] != null) UnityEngine.Object.DestroyImmediate(_created[i]);
        _created.Clear();
    }

    // ---------- Object pooling ----------

    [Test]
    public void ObjectPooler_ReusesInactiveInstance_AndCreatesNewWhenAllActive()
    {
        var template = Track(new GameObject("PoolTemplate"));

        var first = Track((GameObject)CallStatic("ObjectPooler", "InstantiatePooled", template, Vector3.one, Quaternion.identity));
        Assert.That(first, Is.Not.SameAs(template));
        Assert.That(first.activeSelf, Is.True);
        Assert.That(first.transform.position, Is.EqualTo(Vector3.one));

        // While the first instance is active, a second request needs a new instance.
        var second = Track((GameObject)CallStatic("ObjectPooler", "InstantiatePooled", template, Vector3.zero, Quaternion.identity));
        Assert.That(second, Is.Not.SameAs(first));

        // "Destroying" a pooled object means deactivating it; it is then reused.
        first.SetActive(false);
        var reused = (GameObject)CallStatic("ObjectPooler", "InstantiatePooled", template, Vector3.up, Quaternion.identity);
        Assert.That(reused, Is.SameAs(first));
        Assert.That(reused.activeSelf, Is.True);
        Assert.That(reused.transform.position, Is.EqualTo(Vector3.up));
    }

    // ---------- Coins ----------

    private Component Money(out GameObject coinTemplate, int coinPrice)
    {
        var display = Track(new GameObject("TestMoneyDisplay", typeof(RectTransform), typeof(Text)));
        var manager = Inactive("GameManager");
        var money = manager.AddComponent(RuntimeType("MoneyScript"));
        Set(money, "moneyText", display.GetComponent<Text>());
        Set(money, "startingWealth", 0);
        Set(money, "goldProbability", 1f);
        Set(money, "coinAudio", AudioClip.Create("TestCoin", 32, 1, 44100, false));

        coinTemplate = Inactive("TestGoldCoin");
        foreach (var child in new[] { "Coin", "Shadow" })
        {
            var go = new GameObject(child, typeof(SpriteRenderer));
            go.transform.SetParent(coinTemplate.transform, false);
        }
        var coin = coinTemplate.AddComponent(RuntimeType("CoinScript"));
        Set(coin, "price", coinPrice);
        Set(coin, "duration", 100f); // Long enough not to expire during the test.
        coinTemplate.AddComponent<EventTrigger>();
        Set(money, "goldPrefab", coinTemplate);

        // The pooler re-activates its template after cloning; keep that harmless.
        coinTemplate.SetActive(true);
        manager.SetActive(true);
        return money;
    }

    private GameObject SpawnedCoin(GameObject template, GameObject exclude = null)
    {
        foreach (var coin in UnityEngine.Object.FindObjectsByType(RuntimeType("CoinScript"), FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            var go = ((Component)coin).gameObject;
            if (go != template && go != exclude) return go;
        }
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

        Call(money, "CreateCoin", Vector3.zero);
        var coin = Track(SpawnedCoin(template));
        Assert.That(coin, Is.Not.Null, "CreateCoin should spawn a pooled coin.");
        Click(coin);
        Assert.That(Call(money, "GetWealth"), Is.EqualTo(3));

        // Pick-up deactivates the coin (the prefab does this via a persistent SetActive(false)).
        coin.SetActive(false);

        // Reuse the same pooled coin a few times; each click must pay exactly once.
        for (int use = 2; use <= 4; use++)
        {
            Call(money, "CreateCoin", Vector3.zero);
            var reused = SpawnedCoin(template);
            Assert.That(reused, Is.SameAs(coin), "The deactivated coin should be reused from the pool.");
            Click(reused);
            Assert.That(Call(money, "GetWealth"), Is.EqualTo(3 * use), $"Use #{use} paid more than once.");
            reused.SetActive(false);
        }
    }

    // ---------- Tower targeting ----------

    private Component CannonTower()
    {
        var root = Inactive("TestTower");
        var gun = new GameObject("Gun");
        gun.transform.SetParent(root.transform, false);
        var tower = root.AddComponent(RuntimeType("TowerScript"));
        Set(tower, "cooldown", 1000f); // Never fire during the test.
        root.SetActive(true);
        return tower;
    }

    private GameObject Walker(float progress)
    {
        var root = Inactive("TestWalker");
        var walker = (Behaviour)root.AddComponent(RuntimeType("WalkerScript"));
        walker.enabled = false; // Stationary; no PathCreator needed.
        root.AddComponent<CircleCollider2D>().isTrigger = true;
        root.SetActive(true);
        Set(walker, "movementProgress", progress);
        return root;
    }

    [Test]
    public void Tower_IgnoresNonEnemyColliders()
    {
        var tower = CannonTower();
        var bullet = Track(new GameObject("TestBullet", typeof(CircleCollider2D)));

        Assert.DoesNotThrow(() => Call(tower, "OnTriggerEnter2D", bullet.GetComponent<Collider2D>()));
        Assert.That(Call(tower, "GetMostDistantInRange"), Is.Null);
    }

    [Test]
    public void Tower_TargetsMostAdvancedActiveEnemy_AndPrunesInactive()
    {
        var tower = CannonTower();
        var behind = Walker(0.2f);
        var ahead = Walker(0.7f);
        Call(tower, "OnTriggerEnter2D", behind.GetComponent<Collider2D>());
        Call(tower, "OnTriggerEnter2D", ahead.GetComponent<Collider2D>());

        Assert.That(Call(tower, "GetMostDistantInRange"), Is.SameAs(ahead));

        ahead.SetActive(false); // Pooled enemy "destroyed".
        Assert.That(Call(tower, "GetMostDistantInRange"), Is.SameAs(behind));
    }
}
