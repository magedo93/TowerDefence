using UnityEngine;

namespace TowerDefence
{
    // Towers that fire pooled projectiles (cannon/bullet tower, rocket tower).
    // New projectile towers: create one of these (Assets > Create > Towers > Projectile Tower),
    // a tower prefab with TowerScript + a child "Gun" + a CircleCollider2D, and assign the
    // asset to its definition. The top-level stats are level 1; `upgrades` adds levels 2, 3...
    [CreateAssetMenu(fileName = "ProjectileTower", menuName = "Towers/Projectile Tower")]
    public class ProjectileTowerDefinition : TowerDefinition
    {
        [System.Serializable]
        public class Upgrade
        {
            [Tooltip("Coins to buy this level.")]
            [Min(0)] public int upgradeCost;
            [Min(1)] public float range = 250;
            [Min(0.01f)] public float cooldown = 1;
            [Min(0)] public int damage = 1;
        }

        [Header("Level 1")]
        [Tooltip("Targeting radius in world units; applied to the tower's CircleCollider2D.")]
        [Min(1)] public float range = 250;

        [Tooltip("Seconds between shots.")]
        [Min(0.01f)] public float cooldown = 1;

        [Tooltip("Damage each projectile deals on hit (overrides the projectile prefab's value).")]
        [Min(0)] public int damage = 1;

        [Header("All levels")]
        [Tooltip("How fast the gun turns toward its target.")]
        [Min(0)] public float rotationSpeed = 10;

        [Tooltip("Pooled projectile prefab; needs a BulletScript (or RocketScript).")]
        public GameObject projectilePrefab;

        public AudioClip fireSound;

        [Header("Upgrades (level 2, 3, ...)")]
        public Upgrade[] upgrades = new Upgrade[0];

        public override int LevelCount { get { return 1 + (upgrades != null ? upgrades.Length : 0); } }

        public float RangeAt(int level) { return level <= 1 ? range : UpgradeAt(level).range; }
        public float CooldownAt(int level) { return level <= 1 ? cooldown : UpgradeAt(level).cooldown; }
        public int DamageAt(int level) { return level <= 1 ? damage : UpgradeAt(level).damage; }

        public override int UpgradeCost(int level) { return level <= 1 ? 0 : UpgradeAt(level).upgradeCost; }

        public override string DescribeLevel(int level)
        {
            return $"Damage: {DamageAt(level)} every {CooldownAt(level):0.##}s   Range: {RangeAt(level):0}";
        }

        private Upgrade UpgradeAt(int level)
        {
            return upgrades[Mathf.Clamp(level - 2, 0, upgrades.Length - 1)];
        }

        public override bool IsValid
        {
            get
            {
                if (!base.IsValid || !IsPositiveFinite(range) || !IsPositiveFinite(cooldown) || rotationSpeed < 0 || damage < 0) return false;
                if (projectilePrefab == null || projectilePrefab.GetComponent<BulletScript>() == null) return false;
                if (upgrades == null) return false;
                foreach (var upgrade in upgrades)
                {
                    if (upgrade == null || upgrade.upgradeCost < 0 || upgrade.damage < 0 ||
                        !IsPositiveFinite(upgrade.range) || !IsPositiveFinite(upgrade.cooldown)) return false;
                }
                return true;
            }
        }
    }
}
