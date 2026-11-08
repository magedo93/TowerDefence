using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TowerDefence
{
    public class LaserUpgradePanel : MonoBehaviour
    {
        public TextMeshProUGUI title;
        public TextMeshProUGUI statsText;
        public TextMeshProUGUI upgradeText;
        public Button upgradeButton;

        private LaserTowerScript _tower;
        private MoneyScript _money;

        public void Show(LaserTowerScript tower, MoneyScript money)
        {
            Unsubscribe();
            _tower = tower;
            _money = money;
            _tower.LevelChanged += Refresh;
            _money.BalanceChanged += OnBalanceChanged;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            Refresh();
        }

        public void Upgrade()
        {
            if (_tower != null) _tower.TryUpgrade(_money);
            Refresh();
        }

        public void Close() { gameObject.SetActive(false); }

        private void Update()
        {
            if (_tower == null || !_tower.isActiveAndEnabled) Close();
        }

        private void OnDisable() { Unsubscribe(); }
        private void OnBalanceChanged(int balance) { Refresh(); }

        private void Unsubscribe()
        {
            if (_tower != null) _tower.LevelChanged -= Refresh;
            if (_money != null) _money.BalanceChanged -= OnBalanceChanged;
            _tower = null;
            _money = null;
        }

        private void Refresh()
        {
            if (_tower == null || _money == null) return;
            var stats = _tower.Stats;
            title.text = "LASER TOWER  -  LEVEL " + _tower.CurrentLevel;
            statsText.text = $"Damage: {stats.damagePerSecond:0.#}/sec   Range: {stats.range:0}";
            if (!_tower.CanUpgrade)
            {
                upgradeText.text = "MAX LEVEL";
                upgradeButton.interactable = false;
                return;
            }
            var next = _tower.definition.levels[_tower.CurrentLevel];
            statsText.text += $"\nNext: {next.damagePerSecond:0.#}/sec   Range: {next.range:0}";
            upgradeText.text = $"UPGRADE  -  {next.upgradeCost} COINS";
            upgradeButton.interactable = _money.GetWealth() >= next.upgradeCost;
        }
    }
}
