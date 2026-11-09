using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace TowerDefence.EditorTools
{
    // Web (WebGL) version settings. Idempotent; locked by WebGLSettingsTests.
    //  * Gzip + decompression fallback: loads on any static host (GitHub Pages, itch.io, any
    //    web server) even without Content-Encoding headers; slightly slower first load.
    //  * 1280x720 default canvas, browser data caching on.
    //  * HideOnWebGL on the main menu's Quit button (Application.Quit does nothing on the web).
    // Batch: -executeMethod TowerDefence.EditorTools.WebGLSetup.Run
    public static class WebGLSetup
    {
        public const int CanvasWidth = 1280, CanvasHeight = 720;

        [MenuItem("Tools/Tower Defence/Apply Web (WebGL) Settings")]
        public static void Run()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.defaultWebScreenWidth = CanvasWidth;
            PlayerSettings.defaultWebScreenHeight = CanvasHeight;
            AssetDatabase.SaveAssets();
            Debug.Log("[WebGLSetup] Player settings: Gzip + decompression fallback, data caching, 1280x720.");

            var menuPath = EditorBuildSettings.scenes.Select(s => s.path).First(p => p.EndsWith("/MainMenu.unity"));
            var scene = EditorSceneManager.OpenScene(menuPath, OpenSceneMode.Single);
            var quitButtons = scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<Button>(true))
                .Where(b => Enumerable.Range(0, b.onClick.GetPersistentEventCount()).Any(i => b.onClick.GetPersistentMethodName(i) == "Quit"))
                .ToList();
            if (quitButtons.Count == 0) throw new InvalidOperationException("[WebGLSetup] No Quit button found in MainMenu.");

            int added = 0;
            foreach (var button in quitButtons.Where(b => b.GetComponent<HideOnWebGL>() == null))
            {
                button.gameObject.AddComponent<HideOnWebGL>();
                added++;
            }
            if (added > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Failed to save " + menuPath);
            }
            Debug.Log($"[WebGLSetup] HideOnWebGL added to {added} Quit button(s) ({quitButtons.Count} found).");
        }
    }
}
