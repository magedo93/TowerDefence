using UnityEngine;

namespace TowerDefence
{
    // Data shared by every tower type. A tower prefab's TowerScript.definition points to
    // one of the concrete subclasses (ProjectileTowerDefinition, LaserTowerDefinition).
    // Levels are 1-based: level 1 is the tower as bought, upgrades raise it to LevelCount.
    public abstract class TowerDefinition : ScriptableObject
    {
        [Tooltip("Name shown to the player, e.g. in the upgrade panel.")]
        public string displayName;

        [Min(0)] public int purchaseCost;

        public virtual bool IsValid { get { return purchaseCost >= 0; } }

        // Number of levels, including level 1.
        public virtual int LevelCount { get { return 1; } }

        // Coins needed to go from level-1 to `level` (2..LevelCount).
        public virtual int UpgradeCost(int level) { return 0; }

        // One line of player-facing stats for `level`, shown by TowerUpgradePanel.
        public virtual string DescribeLevel(int level) { return string.Empty; }

        protected static bool IsPositiveFinite(float value)
        {
            return value > 0 && !float.IsInfinity(value) && !float.IsNaN(value);
        }
    }
}
