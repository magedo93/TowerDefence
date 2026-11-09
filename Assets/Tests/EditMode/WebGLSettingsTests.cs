using System.Linq;
using NUnit.Framework;
using TowerDefence;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.UI;

// Web (WebGL) version settings (see Tools > Tower Defence > Apply Web (WebGL) Settings).
public class WebGLSettingsTests
{
    [Test]
    public void WebGL_LoadsOnAnyStaticHost()
    {
        // Gzip + decompression fallback works even when the server sends no Content-Encoding
        // header (e.g. GitHub Pages); Brotli without fallback would fail to load there.
        Assert.That(PlayerSettings.WebGL.compressionFormat, Is.EqualTo(WebGLCompressionFormat.Gzip));
        Assert.That(PlayerSettings.WebGL.decompressionFallback, Is.True);
        Assert.That(PlayerSettings.WebGL.dataCaching, Is.True);
        Assert.That(PlayerSettings.defaultWebScreenWidth, Is.EqualTo(1280));
        Assert.That(PlayerSettings.defaultWebScreenHeight, Is.EqualTo(720));
    }

    [Test]
    public void MainMenu_QuitButtonIsHiddenOnWeb()
    {
        var path = EditorBuildSettings.scenes.Select(s => s.path).First(p => p.EndsWith("/MainMenu.unity"));
        var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        try
        {
            var quitButtons = scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<Button>(true))
                .Where(b => Enumerable.Range(0, b.onClick.GetPersistentEventCount()).Any(i => b.onClick.GetPersistentMethodName(i) == "Quit"))
                .ToList();
            Assert.That(quitButtons, Is.Not.Empty, "Quit button");
            Assert.That(quitButtons.All(b => b.GetComponent<HideOnWebGL>() != null), Is.True, "every Quit button hides itself on the web");
        }
        finally
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }
}
