using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BuildSpotScript : MonoBehaviour
{
    public GameManager manager;
    public MoneyScript money;
    public LaserUpgradePanel laserUpgradePanel;

    private GameObject _build;

    // The tower currently standing on this spot, or null.
    internal GameObject BuiltTower => _build;

    private void Start()
    {
        _build = null;
    }

    public void OnBuildSpotClicked()
    {
        /*if (build != null)
        {
            var tower = build.GetComponent<TowerScript>();
            var cost = tower.cost;
            tower.Destroy();
            build = null;
            money.AddBudget(cost / 2);
        }*/

        if (_build != null)
        {
            var laser = _build.GetComponent<LaserTowerScript>();
            if (laser != null && laserUpgradePanel != null) laserUpgradePanel.Show(laser, money);
            return;
        }

        var towerPrefab = manager.GetTowerToBuild();
        if (towerPrefab == null) return;

        var cost = towerPrefab.GetComponent<TowerScript>().BuildCost;
        if (!money.TrySpend(cost)) return;
        _build = ObjectPooler.InstantiatePooled(towerPrefab, transform.position, transform.rotation);
    }
}
