using System.Linq;
using NUnit.Framework;
using TowerDefence.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Egyptian landmarks are decoration only: present as configured, on the right sorting layer,
// without colliders, and never on the enemy path or a build spot.
public class LandmarkPlacementTests
{
    private static string[] Levels() { return EgyptLandmarks.Layout.Keys.ToArray(); }

    [TestCaseSource(nameof(Levels))]
    public void Landmarks_ArePlacedSafely(string level)
    {
        var scene = EditorSceneManager.OpenScene($"Assets/Scenes/{level}.unity", OpenSceneMode.Single);
        try
        {
            var roots = scene.GetRootGameObjects();
            var group = roots.SingleOrDefault(r => r.name == EgyptLandmarks.GroupName);
            Assert.That(group, Is.Not.Null, "Landmarks group");
            var renderers = group.GetComponentsInChildren<SpriteRenderer>(true);
            var expected = EgyptLandmarks.Layout[level];
            Assert.That(renderers.Length, Is.EqualTo(expected.Length), "landmark count");
            foreach (var r in renderers)
            {
                Assert.That(r.sprite, Is.Not.Null, r.name);
                Assert.That(AssetDatabase.GetAssetPath(r.sprite), Does.StartWith(EgyptLandmarks.SpriteFolder), r.name);
                Assert.That(r.sortingLayerName, Is.EqualTo(EgyptLandmarks.SortingLayer), r.name);
                Assert.That(r.sortingOrder, Is.EqualTo(EgyptLandmarks.SortingOrder), r.name);
            }
            Assert.That(group.GetComponentsInChildren<Collider2D>(true), Is.Empty, "landmarks must not block anything");

            var problems = EgyptLandmarks.Validate(roots, expected);
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }
        finally
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }
}
