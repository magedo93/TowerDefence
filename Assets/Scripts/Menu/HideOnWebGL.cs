using UnityEngine;

namespace TowerDefence
{
    // Hides this object in the web (WebGL) build, e.g. the main menu's Quit button:
    // Application.Quit() does nothing in a browser, so the button would look broken.
    public class HideOnWebGL : MonoBehaviour
    {
        private void Awake()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            gameObject.SetActive(false);
#endif
        }
    }
}
