using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace TowerDefence.EditorTools
{
    // Explicit setup command only: never modifies scenes on import or on entering Play.
    public static class LaserTowerSetup
    {
        public const string DefinitionPath = "Assets/Settings/Towers/LaserTower.asset";
        public const string PrefabPath = "Assets/Prefabs/Towers/LaserTower.prefab";
        public const string PanelPath = "Assets/Prefabs/UI/LaserUpgradePanel.prefab";
        public const string SelectionArtPath = "Assets/Sprites/Towers/LaserEmitter.png";
        private static readonly string[] Scenes = { "Level01", "Level02", "Level03" };

        [MenuItem("Tools/Tower Defence/Refresh Laser Selection Art")]
        public static void RefreshSelectionArt()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before updating selection artwork.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            AssetDatabase.ImportAsset(SelectionArtPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(SelectionArtPath) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Laser selection artwork is missing.");
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 1024;
            importer.SaveAndReimport();
            var originalScenes = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                foreach (var sceneName in Scenes)
                {
                    var scene = EditorSceneManager.OpenScene("Assets/Scenes/" + sceneName + ".unity", OpenSceneMode.Single);
                    var button = scene.GetRootGameObjects()
                        .SelectMany(root => root.GetComponentsInChildren<Button>(true))
                        .Single(candidate => candidate.name == "LaserTowerButton");
                    ApplySelectionArt(button);
                    if (sceneName == "Level01") SaveSelectionPreview(button.transform.parent);
                    EditorSceneManager.MarkSceneDirty(scene);
                    if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save " + sceneName);
                }
                AssetDatabase.SaveAssets();
                Debug.Log("LASER_SELECTION_ART_PASSED: gun icon and price updated in all three levels.");
            }
            finally
            {
                if (originalScenes.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(originalScenes);
            }
        }

        private static void ApplySelectionArt(Button button)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SelectionArtPath);
            if (sprite == null) return;
            var reference = button.transform.parent.GetComponentsInChildren<Button>(true)
                .First(candidate => candidate != button && candidate.GetComponentsInChildren<Image>(true)
                    .Any(image => image.sprite != null && AssetDatabase.GetAssetPath(image.sprite) == "Assets/Sprites/gun.png"));
            var referenceIcon = reference.GetComponentsInChildren<Image>(true)
                .First(image => image.sprite != null && AssetDatabase.GetAssetPath(image.sprite) == "Assets/Sprites/gun.png");
            var referencePrice = reference.GetComponentInChildren<Text>(true);
            var iconTransform = button.transform.Find("LaserGunIcon") as RectTransform;
            if (iconTransform == null)
                iconTransform = Rect("LaserGunIcon", button.transform, referenceIcon.rectTransform.sizeDelta, Vector2.zero);
            CopyRect(referenceIcon.rectTransform, iconTransform);
            var icon = iconTransform.GetComponent<Image>();
            if (icon == null) icon = iconTransform.gameObject.AddComponent<Image>();
            icon.sprite = sprite;
            icon.color = Color.white;
            icon.preserveAspect = referenceIcon.preserveAspect;
            icon.raycastTarget = true;
            iconTransform.SetAsFirstSibling();
            // Match the existing transparent, edge-to-edge weapon icons. The image
            // itself is the button graphic; there is no colored rectangular backdrop.
            var background = button.GetComponent<Image>();
            if (background != null)
            {
                background.enabled = false;
                background.raycastTarget = false;
            }
            button.targetGraphic = icon;
            button.colors = reference.colors;
            button.transition = reference.transition;
            var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null) label.gameObject.SetActive(false);
            var priceTransform = button.transform.Find("LaserPrice") as RectTransform;
            if (priceTransform == null)
                priceTransform = Rect("LaserPrice", button.transform, referencePrice.rectTransform.sizeDelta, Vector2.zero);
            CopyRect(referencePrice.rectTransform, priceTransform);
            var price = priceTransform.GetComponent<Text>();
            if (price == null) price = priceTransform.gameObject.AddComponent<Text>();
            var definition = AssetDatabase.LoadAssetAtPath<LaserTowerDefinition>(DefinitionPath);
            price.text = definition.purchaseCost.ToString();
            price.font = referencePrice.font;
            price.fontSize = referencePrice.fontSize;
            price.fontStyle = referencePrice.fontStyle;
            price.alignment = referencePrice.alignment;
            price.color = referencePrice.color;
            price.resizeTextForBestFit = referencePrice.resizeTextForBestFit;
            price.resizeTextMinSize = referencePrice.resizeTextMinSize;
            price.resizeTextMaxSize = referencePrice.resizeTextMaxSize;
            price.raycastTarget = false;
            priceTransform.SetAsLastSibling();
        }

        private static void CopyRect(RectTransform source, RectTransform destination)
        {
            destination.anchorMin = source.anchorMin;
            destination.anchorMax = source.anchorMax;
            destination.pivot = source.pivot;
            destination.sizeDelta = source.sizeDelta;
            destination.anchoredPosition = source.anchoredPosition;
            destination.localScale = source.localScale;
            destination.localRotation = source.localRotation;
        }

        private static void SaveSelectionPreview(Transform selectionGroup)
        {
            var previewScene = EditorSceneManager.NewPreviewScene();
            var renderTarget = new RenderTexture(512, 180, 24);
            Texture2D screenshot = null;
            var previousTarget = RenderTexture.active;
            try
            {
                var cameraObject = new GameObject("SelectionPreviewCamera", typeof(Camera));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, previewScene);
                var camera = cameraObject.GetComponent<Camera>();
                camera.scene = previewScene;
                camera.transform.position = new Vector3(0, 0, -10);
                camera.orthographic = true;
                camera.orthographicSize = 90;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.14f, 0.18f, 0.2f);
                camera.targetTexture = renderTarget;
                var canvasObject = new GameObject("SelectionPreviewCanvas", typeof(RectTransform), typeof(Canvas));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(canvasObject, previewScene);
                var canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
                var strip = UnityEngine.Object.Instantiate(selectionGroup.gameObject, canvas.transform).GetComponent<RectTransform>();
                strip.anchorMin = strip.anchorMax = new Vector2(0.5f, 0.5f);
                strip.anchoredPosition = new Vector2(83, 0);
                strip.localScale = Vector3.one;
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = renderTarget;
                screenshot = new Texture2D(512, 180, TextureFormat.RGBA32, false);
                screenshot.ReadPixels(new Rect(0, 0, 512, 180), 0, 0);
                screenshot.Apply();
                System.IO.Directory.CreateDirectory("AutomationLogs");
                System.IO.File.WriteAllBytes("AutomationLogs/LaserSelectionPreview.png", screenshot.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previousTarget;
                if (screenshot != null) UnityEngine.Object.DestroyImmediate(screenshot);
                renderTarget.Release();
                UnityEngine.Object.DestroyImmediate(renderTarget);
                EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        [MenuItem("Tools/Tower Defence/Install Laser Tower")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before installing Laser Tower assets.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var originalScenes = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                EnsureFolder("Assets/Settings/Towers");
                EnsureFolder("Assets/Prefabs/UI");
                EnsureFolder("Assets/Materials");
                var definition = AssetDatabase.LoadAssetAtPath<LaserTowerDefinition>(DefinitionPath);
                if (definition == null)
                {
                    definition = ScriptableObject.CreateInstance<LaserTowerDefinition>();
                    AssetDatabase.CreateAsset(definition, DefinitionPath);
                }
                if (!definition.IsValid) throw new InvalidOperationException("Invalid Laser Tower definition.");
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                if (prefab == null) prefab = CreateTower(definition);
                var panel = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPath);
                if (panel == null) panel = CreatePanel();
                foreach (var sceneName in Scenes) InstallScene(sceneName, prefab, panel);
                AssetDatabase.SaveAssets();
                Debug.Log("LASER_SETUP_PASSED: Laser prefab, three-level definition, selection buttons and upgrade panels installed in all three levels.");
            }
            finally
            {
                if (originalScenes.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(originalScenes);
            }
        }

        private static GameObject CreateTower(LaserTowerDefinition definition)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Towers/BulletTower.prefab");
            if (source == null) throw new InvalidOperationException("Bullet Tower template is missing.");
            var root = UnityEngine.Object.Instantiate(source);
            if (PrefabUtility.IsPartOfPrefabInstance(root))
                PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            root.name = "LaserTower";
            root.SetActive(false);
            try
            {
                UnityEngine.Object.DestroyImmediate(root.GetComponent<TowerScript>());
                foreach (var pool in root.GetComponents<ObjectPooler>()) UnityEngine.Object.DestroyImmediate(pool);
                var laser = root.AddComponent<LaserTowerScript>();
                laser.definition = definition;
                laser.cost = definition.purchaseCost;
                laser.rotationSpeed = 10;
                laser.enemyLayers = LayerMask.GetMask("LivingEnemy", "MachineEnemy");
                if (laser.enemyLayers.value == 0) throw new InvalidOperationException("Ground enemy layers are missing.");
                var range = root.GetComponent<CircleCollider2D>();
                range.radius = definition.levels[0].range;
                // Laser targeting uses current overlap queries, not collision messages.
                range.enabled = false;
                var audio = root.GetComponent<AudioSource>();
                audio.playOnAwake = false;
                EgyptTowerArt.ApplyLaserSprites(root);
                const string materialPath = "Assets/Materials/LaserBeam.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    var shader = Shader.Find("Sprites/Default");
                    if (shader == null) throw new InvalidOperationException("Sprites/Default shader is missing.");
                    material = new Material(shader) { name = "LaserBeam" };
                    AssetDatabase.CreateAsset(material, materialPath);
                }
                var beam = root.GetComponent<LineRenderer>();
                beam.sharedMaterial = material;
                beam.positionCount = 2;
                beam.useWorldSpace = true;
                beam.startWidth = definition.levels[0].beamWidth;
                beam.endWidth = definition.levels[0].beamWidth * 0.5f;
                beam.startColor = beam.endColor = definition.levels[0].beamColor;
                beam.sortingLayerName = "Foreground";
                beam.sortingOrder = 10;
                beam.numCapVertices = 4;
                beam.enabled = false;
                root.SetActive(true);
                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static GameObject CreatePanel()
        {
            var root = Rect("LaserUpgradePanel", null, new Vector2(480, 230), new Vector2(-270, 300));
            try
            {
                root.anchorMin = root.anchorMax = new Vector2(1, 0);
                var image = root.gameObject.AddComponent<Image>();
                image.color = new Color(0.035f, 0.09f, 0.12f, 0.97f);
                var controller = root.gameObject.AddComponent<TowerUpgradePanel>();
                controller.title = Label("Title", root, new Vector2(400, 38), new Vector2(-15, 80), "LASER TOWER", 25);
                controller.title.color = new Color(0.25f, 1, 0.85f);
                controller.statsText = Label("Stats", root, new Vector2(440, 70), new Vector2(0, 15), "", 21);
                controller.upgradeButton = MakeButton("Upgrade", root, new Vector2(420, 48), new Vector2(0, -70), "UPGRADE", out var upgradeText);
                controller.upgradeText = upgradeText;
                UnityEventTools.AddPersistentListener(controller.upgradeButton.onClick, controller.Upgrade);
                var close = MakeButton("Close", root, new Vector2(38, 36), new Vector2(212, 85), "X", out _);
                UnityEventTools.AddPersistentListener(close.onClick, controller.Close);
                root.gameObject.SetActive(false);
                return PrefabUtility.SaveAsPrefabAsset(root.gameObject, PanelPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(root.gameObject); }
        }

        private static void InstallScene(string sceneName, GameObject towerPrefab, GameObject panelPrefab)
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/" + sceneName + ".unity", OpenSceneMode.Single);
            var all = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<Transform>(true)).ToArray();
            var manager = all.Select(t => t.GetComponent<GameManager>()).FirstOrDefault(c => c != null);
            if (manager == null) throw new InvalidOperationException(sceneName + " has no GameManager.");
            var selectionButton = all.Select(t => t.GetComponent<Button>()).FirstOrDefault(button =>
                button != null && Enumerable.Range(0, button.onClick.GetPersistentEventCount())
                    .Any(i => button.onClick.GetPersistentMethodName(i) == "SetTowerToBuild"));
            if (selectionButton == null) throw new InvalidOperationException(sceneName + " has no tower selector.");
            var panel = all.Select(t => t.GetComponent<TowerUpgradePanel>()).FirstOrDefault(c => c != null);
            if (panel == null)
            {
                var canvas = selectionButton.GetComponentInParent<Canvas>().rootCanvas;
                var panelObject = (GameObject)PrefabUtility.InstantiatePrefab(panelPrefab, canvas.transform);
                panel = panelObject.GetComponent<TowerUpgradePanel>();
            }
            panel.gameObject.SetActive(false);
            if (!all.Any(t => t.name == "LaserTowerButton"))
            {
                var button = MakeButton("LaserTowerButton", selectionButton.transform.parent,
                    new Vector2(165, 140), new Vector2(-249, 0),
                    "LASER\n" + towerPrefab.GetComponent<LaserTowerScript>().BuildCost + " COINS\nGROUND", out var label);
                label.fontSize = 23;
                UnityEventTools.AddObjectPersistentListener<GameObject>(button.onClick, manager.SetTowerToBuild, towerPrefab);
                ApplySelectionArt(button);
            }
            foreach (var spot in all.Select(t => t.GetComponent<BuildSpotScript>()).Where(c => c != null))
            {
                spot.upgradePanel = panel;
                PrefabUtility.RecordPrefabInstancePropertyModifications(spot);
                EditorUtility.SetDirty(spot);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save " + sceneName);
            Debug.Log("Laser Tower integrated: " + sceneName);
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 position)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.gameObject.layer = LayerMask.NameToLayer("UI");
            if (parent != null) rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        private static TextMeshProUGUI Label(string name, Transform parent, Vector2 size, Vector2 position, string text, float fontSize)
        {
            var rect = Rect(name, parent, size, position);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = TMP_Settings.defaultFontAsset;
            label.text = text;
            label.fontSize = fontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.raycastTarget = false;
            return label;
        }

        private static Button MakeButton(string name, Transform parent, Vector2 size, Vector2 position, string text, out TextMeshProUGUI label)
        {
            var rect = Rect(name, parent, size, position);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.06f, 0.35f, 0.32f, 1);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.highlightedColor = new Color(0.7f, 1, 0.9f);
            colors.disabledColor = new Color(0.35f, 0.35f, 0.35f);
            button.colors = colors;
            label = Label("Label", rect, size - new Vector2(12, 8), Vector2.zero, text, 22);
            return button;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var slash = path.LastIndexOf('/');
            var parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
    }
}
