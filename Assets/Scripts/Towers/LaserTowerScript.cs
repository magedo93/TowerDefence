using System.Collections.Generic;
using UnityEngine;

namespace TowerDefence
{
    [RequireComponent(typeof(LineRenderer))]
    [RequireComponent(typeof(CircleCollider2D))]
    public class LaserTowerScript : TowerScript
    {
        public LayerMask enemyLayers;
        public float muzzleOffset = 25;
        // The inherited TowerScript.definition must be a LaserTowerDefinition.
        public LaserTowerDefinition LaserDefinition { get { return definition as LaserTowerDefinition; } }

        // Level, upgrades and LevelChanged come from TowerScript.
        public LaserTowerDefinition.Level Stats { get { return LaserDefinition.levels[CurrentLevel - 1]; } }

        private readonly List<Collider2D> _overlaps = new List<Collider2D>(64);
        private LineRenderer _beam;
        private CircleCollider2D _rangeCollider;

        private bool HasValidDefinition { get { return LaserDefinition != null && LaserDefinition.IsValid; } }

        protected override void Awake()
        {
            base.Awake();
            _beam = GetComponent<LineRenderer>();
            _rangeCollider = GetComponent<CircleCollider2D>();
        }

        protected override void OnEnable()
        {
            base.OnEnable(); // resets to level 1 and applies it
            _overlaps.Clear();
            _beam.enabled = false;
            if (!HasValidDefinition)
            {
                Debug.LogError("Laser Tower requires a valid three-level definition.", this);
                enabled = false;
            }
        }

        protected override void OnDisable()
        {
            if (_beam != null) _beam.enabled = false;
            _overlaps.Clear();
            base.OnDisable();
        }

        protected internal override GameObject SelectTarget()
        {
            // Query current overlaps rather than trusting trigger history across pooled
            // enemy reuse. This also picks up enemies already inside an upgraded range.
            var filter = new ContactFilter2D { useTriggers = true };
            filter.SetLayerMask(enemyLayers);
            Physics2D.OverlapCircle(transform.position, Stats.range, filter, _overlaps);
            WalkerScript best = null;
            float progress = float.NegativeInfinity;
            foreach (var collider in _overlaps)
            {
                var enemy = collider.GetComponentInParent<EnemyScript>();
                if (enemy == null || !enemy.isActiveAndEnabled) continue;
                var health = enemy.GetComponent<Health>();
                var walker = enemy.GetComponent<WalkerScript>();
                if (health == null || health.isDead || walker == null) continue;
                if (((Vector2)(enemy.transform.position - transform.position)).sqrMagnitude > Stats.range * Stats.range) continue;
                if (walker.GetProgress() > progress)
                {
                    progress = walker.GetProgress();
                    best = walker;
                }
            }
            return best != null ? best.gameObject : null;
        }

        protected override void UpdateAttack(GameObject target)
        {
            var health = target != null ? target.GetComponent<Health>() : null;
            if (health == null || health.isDead || !target.activeInHierarchy || Time.deltaTime <= 0)
            {
                _beam.enabled = false;
                return;
            }
            if (((Vector2)(target.transform.position - transform.position)).sqrMagnitude > Stats.range * Stats.range)
            {
                _beam.enabled = false;
                return;
            }
            _beam.enabled = true;
            _beam.SetPosition(0, GunTransform.position + GunTransform.up * muzzleOffset);
            _beam.SetPosition(1, target.transform.position);
            health.TakeAccumulatedDamage(Stats.damagePerSecond * Time.deltaTime);
            if (health.isDead) _beam.enabled = false;
        }

        // The laser never spawns bullets, including calls through the public base API.
        public override void Shoot(GameObject target) { }

        protected override void ApplyLevel()
        {
            if (!HasValidDefinition) return;
            _rangeCollider.radius = Stats.range;
            _beam.positionCount = 2;
            _beam.useWorldSpace = true;
            _beam.startWidth = Stats.beamWidth;
            _beam.endWidth = Stats.beamWidth * 0.5f;
            _beam.startColor = Stats.beamColor;
            _beam.endColor = Stats.beamColor;
        }
    }
}
