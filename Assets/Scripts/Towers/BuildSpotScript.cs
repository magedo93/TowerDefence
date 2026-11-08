using UnityEngine;
using UnityEngine.Serialization;

namespace TowerDefence
{
    // A place where the player can build. Clicking an empty spot builds the tower selected
    // in GameManager (if affordable); clicking an occupied spot opens its upgrade panel.
    public class BuildSpotScript : MonoBehaviour
    {
        public GameManager manager;
        public MoneyScript money;
        [FormerlySerializedAs("laserUpgradePanel")]
        public TowerUpgradePanel upgradePanel;

        private GameObject _build;

        // The tower currently standing on this spot, or null.
        internal GameObject BuiltTower => _build;

        private void Start()
        {
            _build = null;
        }

        public void OnBuildSpotClicked()
        {
            if (_build != null)
            {
                var tower = _build.GetComponent<TowerScript>();
                if (tower != null && upgradePanel != null) upgradePanel.Show(tower, money);
                return;
            }

            var towerPrefab = manager.GetTowerToBuild();
            if (towerPrefab == null) return;

            var cost = towerPrefab.GetComponent<TowerScript>().BuildCost;
            if (!money.TrySpend(cost)) return;
            _build = ObjectPooler.InstantiatePooled(towerPrefab, transform.position, transform.rotation);
        }
    }
}
