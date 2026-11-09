using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace TowerDefence.EditorTools
{
    // Art phase 4 (UI): Egyptian palette for menu/level text and the tower upgrade panel, and
    // the level HUD moved from legacy UI Text to TextMeshPro:
    //   * green "positive" text gradients -> gold, red "negative" ones -> carnelian;
    //   * every legacy Text in the level scenes (money, health, tower prices) becomes a
    //     TextMeshProUGUI with the same rect, size and alignment; MoneyScript.moneyText and
    //     PlayerHealth.healthText are re-assigned (Canvas/MoneyDisplay/Text, Canvas/HealthDisplay/Text);
    //   * money/health numbers get an outlined material (HudMaterialPath) so they read on any terrain;
    //   * upgrade panel: lapis background, bronze buttons, gold title.
    // Idempotent. Batch: -executeMethod TowerDefence.EditorTools.EgyptUiStyle.Run
    public static class EgyptUiStyle
    {
        public static readonly string[] Levels = { "Level01", "Level02", "Level03" };
        public const string MenuScene = "MainMenu";
        public const string PanelPrefabPath = "Assets/Prefabs/UI/LaserUpgradePanel.prefab";
        public const string HudMaterialPath = "Assets/Settings/UI/HudNumbers.mat";

        // Palette (also used by LaserTowerSetup when it builds the panel and laser button).
        public static readonly Color GoldTop = new Color32(0xF6, 0xD7, 0x7A, 0xFF);
        public static readonly Color GoldBottom = new Color32(0xD9, 0xA2, 0x1E, 0xFF);
        public static readonly Color CarnelianTop = new Color32(0xF0, 0x8A, 0x6C, 0xFF);
        public static readonly Color CarnelianBottom = new Color32(0xB8, 0x39, 0x2B, 0xFF);
        public static readonly Color Ink = new Color32(0x3A, 0x24, 0x10, 0xFF);           // prices on the gold coins
        public static readonly Color HudOutline = new Color32(0x2A, 0x17, 0x0A, 0xFF);
        public static readonly Color MoneyColor = new Color32(0xF6, 0xD7, 0x7A, 0xFF);
        public static readonly Color HealthColor = new Color32(0xFF, 0xE6, 0xDA, 0xFF);
        public static readonly Color PanelColor = new Color(0.10f, 0.17f, 0.36f, 0.97f);  // lapis
        public static readonly Color PanelButtonColor = new Color32(0x8A, 0x5A, 0x1E, 0xFF); // bronze
        public static readonly Color PanelButtonHighlight = new Color32(0xFF, 0xE9, 0xB0, 0xFF);
        public static readonly Color PanelTitleColor = GoldTop;
        public static readonly Color MenuButtonTint = new Color(0.91f, 0.72f, 0.25f, 0.14f);
        public static readonly Color MenuButtonHighlight = new Color(0.91f, 0.72f, 0.25f, 0.30f);

        [MenuItem("Tools/Tower Defence/Apply Egyptian UI Style")]
        public static void Run()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var hud = EnsureHudMaterial();
            StyleScene(MenuScene, null);
            foreach (var level in Levels) StyleScene(level, hud);
            StylePanelPrefab();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Debug.Log("[EgyptUiStyle] done.");
        }

        private static void StyleScene(string name, Material hud)
        {
            var path = $"Assets/Scenes/{name}.unity";
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();

            int converted = 0;
            if (hud != null)
            {
                foreach (var legacy in roots.SelectMany(r => r.GetComponentsInChildren<Text>(true)).ToList())
                {
                    ConvertToTmp(legacy);
                    converted++;
                }
                var money = roots.SelectMany(r => r.GetComponentsInChildren<MoneyScript>(true)).Single();
                var health = roots.SelectMany(r => r.GetComponentsInChildren<PlayerHealth>(true)).Single();
                money.moneyText = FindText(roots, "MoneyDisplay");
                health.healthText = FindText(roots, "HealthDisplay");
                Hud(money.moneyText, hud, MoneyColor);
                Hud(health.healthText, hud, HealthColor);
                foreach (var price in roots.SelectMany(r => r.GetComponentsInChildren<TextMeshProUGUI>(true))
                             .Where(t => t.GetComponentInParent<Button>(true) != null && t.transform.parent.name != "Close" && IsNumber(t.text)))
                {
                    price.color = Ink;
                    price.fontStyle = FontStyles.Bold;
                    EditorUtility.SetDirty(price);
                }
                EditorUtility.SetDirty(money);
                EditorUtility.SetDirty(health);
                PrefabUtility.RecordPrefabInstancePropertyModifications(money);
                PrefabUtility.RecordPrefabInstancePropertyModifications(health);
            }

            if (name == MenuScene) CoverScreen(roots);

            int recolored = 0;
            foreach (var tmp in roots.SelectMany(r => r.GetComponentsInChildren<TextMeshProUGUI>(true)))
                if (Recolor(tmp)) recolored++;

            foreach (var button in roots.SelectMany(r => r.GetComponentsInChildren<Button>(true)))
            {
                if (button.GetComponentInParent<TowerUpgradePanel>(true) != null) continue; // prefab-owned
                var colors = button.colors;
                if (colors.normalColor.a > 0.01f && colors.normalColor.a < 0.2f) colors.normalColor = MenuButtonTint;  // level-select tiles
                if (colors.highlightedColor.a < 0.2f) colors.highlightedColor = MenuButtonHighlight;                  // transparent text buttons
                else continue;
                button.colors = colors;
                EditorUtility.SetDirty(button);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Failed to save " + path);
            Debug.Log($"[EgyptUiStyle] {name}: {converted} legacy text(s) converted, {recolored} gradient(s) recolored.");
        }

        public const string BackgroundSpritePath = "Assets/Sprites/background.png";

        // The menu background (16:9 art) covers the whole screen at any aspect ratio, cropping
        // the edges instead of letterboxing. Replaces the old preserve-aspect + 1.186 scale hack.
        private static void CoverScreen(GameObject[] roots)
        {
            var image = roots.SelectMany(r => r.GetComponentsInChildren<Image>(true))
                .Single(i => i.sprite != null && AssetDatabase.GetAssetPath(i.sprite) == BackgroundSpritePath);
            image.preserveAspect = false;
            var rect = image.rectTransform;
            rect.localScale = Vector3.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            // The fitter owns anchors and size; toggling it forces a recompute in the Editor.
            var fitter = image.GetComponent<AspectRatioFitter>() ?? image.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = image.sprite.rect.width / image.sprite.rect.height;
            fitter.enabled = false;
            fitter.enabled = true;
            Canvas.ForceUpdateCanvases();
            if (rect.rect.width < 1 || rect.rect.height < 1)
                throw new InvalidOperationException("[EgyptUiStyle] menu background has no size after fitting.");
            EditorUtility.SetDirty(image);
            EditorUtility.SetDirty(fitter);
        }

        private static TMP_Text FindText(GameObject[] roots, string display)
        {
            var parent = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == display);
            var text = parent != null ? parent.Find("Text") : null;
            var tmp = text != null ? text.GetComponent<TMP_Text>() : null;
            if (tmp == null) throw new InvalidOperationException($"[EgyptUiStyle] {display}/Text with TextMeshPro not found.");
            return tmp;
        }

        private static void Hud(TMP_Text text, Material hud, Color color)
        {
            text.fontSharedMaterial = hud;
            text.color = color;
            text.fontStyle = FontStyles.Bold;
            EditorUtility.SetDirty(text);
        }

        private static bool IsNumber(string s) { return !string.IsNullOrEmpty(s) && s.Trim().All(char.IsDigit); }

        // Legacy Text -> TextMeshProUGUI on the same GameObject (rect untouched).
        private static void ConvertToTmp(Text legacy)
        {
            var go = legacy.gameObject;
            var text = legacy.text;
            var size = legacy.fontSize;
            var color = legacy.color;
            var align = legacy.alignment;
            var raycast = legacy.raycastTarget;
            UnityEngine.Object.DestroyImmediate(legacy, true);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.font = TMP_Settings.defaultFontAsset;
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = Map(align);
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.raycastTarget = raycast;
            EditorUtility.SetDirty(go);
        }

        private static TextAlignmentOptions Map(TextAnchor anchor)
        {
            switch (anchor)
            {
                case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
                case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
                case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
                case TextAnchor.MiddleLeft: return TextAlignmentOptions.Left;
                case TextAnchor.MiddleRight: return TextAlignmentOptions.Right;
                case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
                case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
                case TextAnchor.LowerRight: return TextAlignmentOptions.BottomRight;
                default: return TextAlignmentOptions.Center;
            }
        }

        // Green-ish gradients (PLAY, level numbers, victory...) -> gold; red-ish (QUIT, BACK, game over) -> carnelian.
        private static bool Recolor(TextMeshProUGUI tmp)
        {
            if (!tmp.enableVertexGradient) return false;
            var top = tmp.colorGradient.topLeft;
            VertexGradient gradient;
            if (IsGreen(top) || IsGold(top)) gradient = new VertexGradient(GoldTop, GoldTop, GoldBottom, GoldBottom);
            else if (IsRed(top) || IsCarnelian(top)) gradient = new VertexGradient(CarnelianTop, CarnelianTop, CarnelianBottom, CarnelianBottom);
            else return false;
            tmp.colorGradient = gradient;
            EditorUtility.SetDirty(tmp);
            return true;
        }

        private static bool IsGreen(Color c) { return c.g > c.r + 0.2f && c.g > c.b + 0.2f; }
        private static bool IsRed(Color c) { return c.r > c.g + 0.4f && c.r > c.b + 0.4f; }
        private static bool IsGold(Color c) { return Same(c, GoldTop); }
        private static bool IsCarnelian(Color c) { return Same(c, CarnelianTop); }
        private static bool Same(Color a, Color b) { return Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.02f; }

        private static void StylePanelPrefab()
        {
            var root = PrefabUtility.LoadPrefabContents(PanelPrefabPath);
            try
            {
                ApplyPanelStyle(root);
                PrefabUtility.SaveAsPrefabAsset(root, PanelPrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        public static void ApplyPanelStyle(GameObject root)
        {
            var background = root.GetComponent<Image>();
            if (background != null) background.color = PanelColor;
            foreach (var button in root.GetComponentsInChildren<Button>(true)) StylePanelButton(button);
            var panel = root.GetComponent<TowerUpgradePanel>();
            if (panel != null && panel.title != null) panel.title.color = PanelTitleColor;
        }

        public static void StylePanelButton(Button button)
        {
            var image = button.targetGraphic as Image;
            if (image != null) image.color = PanelButtonColor;
            var colors = button.colors;
            colors.highlightedColor = PanelButtonHighlight;
            colors.disabledColor = new Color(0.35f, 0.35f, 0.35f);
            button.colors = colors;
        }

        // Copy of the default TMP font material with a dark outline, for the HUD numbers.
        private static Material EnsureHudMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(HudMaterialPath);
            if (material == null)
            {
                var font = TMP_Settings.defaultFontAsset;
                if (font == null) throw new InvalidOperationException("[EgyptUiStyle] no default TMP font asset.");
                var folder = System.IO.Path.GetDirectoryName(HudMaterialPath).Replace('\\', '/');
                if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Settings", "UI");
                material = new Material(font.material) { name = "HudNumbers" };
                AssetDatabase.CreateAsset(material, HudMaterialPath);
            }
            material.EnableKeyword(ShaderUtilities.Keyword_Outline);
            material.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.22f);
            material.SetColor(ShaderUtilities.ID_OutlineColor, HudOutline);
            material.SetFloat(ShaderUtilities.ID_FaceDilate, 0.1f);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            return material;
        }
    }
}
