using System.Linq;
using NUnit.Framework;
using TMPro;
using TowerDefence;
using TowerDefence.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Art phase 4 (UI): the HUD is TextMeshPro (no legacy UI Text left), the money/health numbers use
// the outlined HUD material, the menu background covers the screen, and the upgrade panel uses
// the Egyptian palette.
public class UiStyleTests
{
    private static string[] Levels() { return EgyptUiStyle.Levels; }

    [TearDown]
    public void TearDown() { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); }

    [TestCaseSource(nameof(Levels))]
    public void Level_HudIsTextMeshPro(string level)
    {
        var roots = EditorSceneManager.OpenScene($"Assets/Scenes/{level}.unity", OpenSceneMode.Single).GetRootGameObjects();
        Assert.That(roots.SelectMany(r => r.GetComponentsInChildren<Text>(true)), Is.Empty, "legacy UI Text left");
        var hud = AssetDatabase.LoadAssetAtPath<Material>(EgyptUiStyle.HudMaterialPath);
        Assert.That(hud, Is.Not.Null);
        var money = roots.SelectMany(r => r.GetComponentsInChildren<MoneyScript>(true)).Single();
        var health = roots.SelectMany(r => r.GetComponentsInChildren<PlayerHealth>(true)).Single();
        Assert.That(money.moneyText, Is.Not.Null);
        Assert.That(health.healthText, Is.Not.Null);
        Assert.That(money.moneyText.fontSharedMaterial, Is.EqualTo(hud));
        Assert.That(health.healthText.fontSharedMaterial, Is.EqualTo(hud));
    }

    [Test]
    public void Menu_BackgroundCoversScreen()
    {
        var roots = EditorSceneManager.OpenScene($"Assets/Scenes/{EgyptUiStyle.MenuScene}.unity", OpenSceneMode.Single).GetRootGameObjects();
        var image = roots.SelectMany(r => r.GetComponentsInChildren<Image>(true))
            .Single(i => i.sprite != null && AssetDatabase.GetAssetPath(i.sprite) == EgyptUiStyle.BackgroundSpritePath);
        var fitter = image.GetComponent<AspectRatioFitter>();
        Assert.That(fitter, Is.Not.Null);
        Assert.That(fitter.aspectMode, Is.EqualTo(AspectRatioFitter.AspectMode.EnvelopeParent));
        Assert.That(fitter.aspectRatio, Is.EqualTo(image.sprite.rect.width / image.sprite.rect.height).Within(0.001f));
        Assert.That(image.preserveAspect, Is.False);
    }

    [Test]
    public void UpgradePanel_UsesEgyptianPalette()
    {
        var panel = AssetDatabase.LoadAssetAtPath<GameObject>(EgyptUiStyle.PanelPrefabPath);
        Assert.That(panel.GetComponent<Image>().color, Is.EqualTo(EgyptUiStyle.PanelColor));
        Assert.That(panel.GetComponent<TowerUpgradePanel>().title.color, Is.EqualTo(EgyptUiStyle.PanelTitleColor));
        foreach (var button in panel.GetComponentsInChildren<Button>(true))
            Assert.That(((Image)button.targetGraphic).color, Is.EqualTo(EgyptUiStyle.PanelButtonColor), button.name);
    }
}
