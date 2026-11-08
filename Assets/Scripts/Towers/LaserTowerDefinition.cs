using UnityEngine;

namespace TowerDefence
{
    [CreateAssetMenu(fileName = "LaserTower", menuName = "Towers/Laser Tower")]
    public class LaserTowerDefinition : ScriptableObject
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

        [Min(0)] public int purchaseCost = 6;
        public Level[] levels =
        {
            new Level { damagePerSecond = 2, range = 250, upgradeCost = 0 },
            new Level { damagePerSecond = 3.5f, range = 300, upgradeCost = 6, beamWidth = 7 },
            new Level { damagePerSecond = 5, range = 350, upgradeCost = 9, beamWidth = 9 }
        };

        public bool IsValid
        {
            get
            {
                if (purchaseCost < 0 || levels == null || levels.Length != 3) return false;
                foreach (var level in levels)
                {
                    if (level == null || level.upgradeCost < 0 ||
                        !IsPositiveFinite(level.damagePerSecond) || !IsPositiveFinite(level.range) ||
                        !IsPositiveFinite(level.beamWidth)) return false;
                }
                return true;
            }
        }

        private static bool IsPositiveFinite(float value)
        {
            return value > 0 && !float.IsInfinity(value) && !float.IsNaN(value);
        }
    }
}
