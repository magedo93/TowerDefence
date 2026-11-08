using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TowerDefence
{
    // Upgrade panel for any built tower: opened by clicking the tower's build spot.
    // Shows the tower's name, level, current stats and the next level's stats, and buys
    // the next level. Text comes from the tower's definition (TowerDefinition.DescribeLevel),
    // so new tower types need no panel code.
    // (Formerly LaserUpgradePanel; same script GUID, so existing panels keep working.)
    public class TowerUpgradePanel : MonoBehaviour
    {
        public TextMeshProUGUI title;
        public TextMeshProUGUI statsText;
        public TextMeshProUGUI upgradeText;
        public Button upgradeButton;

        private TowerScript _tower;
        private MoneyScript _money;

        public TowerScript Tower { get { return _tower; } }

        public void Show(TowerScript tower, MoneyScript money)
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
            var definition = _tower.definition;
            int level = _tower.CurrentLevel;
            title.text = $"{_tower.DisplayName.ToUpperInvariant()}  -  LEVEL {level}";
            statsText.text = definition != null ? definition.DescribeLevel(level) : string.Empty;
            if (!_tower.CanUpgrade)
            {
                upgradeText.text = "MAX LEVEL";
                upgradeButton.interactable = false;
                return;
            }
            int cost = _tower.NextUpgradeCost;
            statsText.text += "\nNext: " + definition.DescribeLevel(level + 1);
            upgradeText.text = $"UPGRADE  -  {cost} COINS";
            upgradeButton.interactable = _money.GetWealth() >= cost;
        }
    }
}
