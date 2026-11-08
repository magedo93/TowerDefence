using UnityEngine;

namespace TowerDefence
{
    // Towers that fire pooled projectiles (cannon/bullet tower, rocket tower).
    // New projectile towers: create one of these (Assets > Create > Towers > Projectile Tower),
    // a tower prefab with TowerScript + a child "Gun", and assign the asset to its definition.
    [CreateAssetMenu(fileName = "ProjectileTower", menuName = "Towers/Projectile Tower")]
    public class ProjectileTowerDefinition : TowerDefinition
    {
        [Tooltip("Targeting radius in world units; applied to the tower's CircleCollider2D.")]
        [Min(1)] public float range = 250;

        [Tooltip("Seconds between shots.")]
        [Min(0.01f)] public float cooldown = 1;

        [Tooltip("How fast the gun turns toward its target.")]
        [Min(0)] public float rotationSpeed = 10;

        [Tooltip("Damage each projectile deals on hit (overrides the projectile prefab's value).")]
        [Min(0)] public int damage = 1;

        [Tooltip("Pooled projectile prefab; needs a BulletScript (or RocketScript).")]
        public GameObject projectilePrefab;

        public AudioClip fireSound;

        public override bool IsValid
        {
            get
            {
                return base.IsValid && IsPositiveFinite(range) && IsPositiveFinite(cooldown) && rotationSpeed >= 0 &&
                       damage >= 0 && projectilePrefab != null && projectilePrefab.GetComponent<BulletScript>() != null;
            }
        }
    }
}
