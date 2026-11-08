using UnityEngine;

namespace TowerDefence
{
    // Three upgrade levels of a beam tower. purchaseCost/displayName come from TowerDefinition
    // (same serialized field name as before, so existing assets keep their values).
    [CreateAssetMenu(fileName = "LaserTower", menuName = "Towers/Laser Tower")]
    public class LaserTowerDefinition : TowerDefinition
    {
        [System.Serializable]
        public class Level
        {
            [Min(0.01f)] public float damagePerSecond = 2;
            [Min(1)] public float range = 250;
            [Min(0)] public int upgradeCost;
            [Min(0.1f)] public float beamWidth = 5;
            public Color beamColor = new Color(0.15f, 1, 0.85f);
        }

        public Level[] levels =
        {
            new Level { damagePerSecond = 2, range = 250, upgradeCost = 0 },
            new Level { damagePerSecond = 3.5f, range = 300, upgradeCost = 6, beamWidth = 7 },
            new Level { damagePerSecond = 5, range = 350, upgradeCost = 9, beamWidth = 9 }
        };

        public LaserTowerDefinition()
        {
            purchaseCost = 6; // Default for new assets; serialized values override it.
        }

        public override bool IsValid
        {
            get
            {
                if (!base.IsValid || levels == null || levels.Length != 3) return false;
                foreach (var level in levels)
                {
                    if (level == null || level.upgradeCost < 0 ||
                        !IsPositiveFinite(level.damagePerSecond) || !IsPositiveFinite(level.range) ||
                        !IsPositiveFinite(level.beamWidth)) return false;
                }
                return true;
            }
        }
    }
}
