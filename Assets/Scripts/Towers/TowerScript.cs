using System.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TowerDefence
{
    [RequireComponent(typeof(AudioSource))]
    public class TowerScript : MonoBehaviour {

        [Tooltip("Tower balance and setup. Projectile towers use a ProjectileTowerDefinition, the laser a LaserTowerDefinition.")]
        public TowerDefinition definition;

        [Header("Legacy (used only when no definition is assigned)")]
        public GameObject bulletPrefab;
        public int cost;
        public float rotationSpeed;
        public float cooldown;
        public AudioClip gunShoot;

        private ProjectileTowerDefinition Projectile { get { return definition as ProjectileTowerDefinition; } }

        // ---- Upgrades (all towers) ----
        // 1-based; a tower is bought at level 1 and reset to it when reused from the pool.
        public int CurrentLevel { get; private set; } = 1;
        public int MaxLevel { get { return definition != null ? definition.LevelCount : 1; } }
        public bool CanUpgrade { get { return definition != null && definition.IsValid && CurrentLevel < MaxLevel; } }
        public int NextUpgradeCost { get { return CanUpgrade ? definition.UpgradeCost(CurrentLevel + 1) : 0; } }
        public string DisplayName
        {
            get { return definition != null && !string.IsNullOrEmpty(definition.displayName) ? definition.displayName : name; }
        }
        public event Action LevelChanged;
        private bool _upgrading;

        // Effective values: the definition (at the current level) when present, otherwise the legacy fields.
        public int BuildCost { get { return definition != null ? definition.purchaseCost : cost; } }
        public float Cooldown { get { return Projectile != null ? Projectile.CooldownAt(CurrentLevel) : cooldown; } }
        public float Range { get { return Projectile != null ? Projectile.RangeAt(CurrentLevel) : (_range != null ? _range.radius : 0); } }
        // Projectile damage at the current level; 0 without a projectile definition (the
        // projectile then keeps its prefab's own damage).
        public int Damage { get { return Projectile != null ? Projectile.DamageAt(CurrentLevel) : 0; } }
        public float RotationSpeed { get { return Projectile != null ? Projectile.rotationSpeed : rotationSpeed; } }
        public GameObject ProjectilePrefab { get { return Projectile != null ? Projectile.projectilePrefab : bulletPrefab; } }
        public AudioClip FireSound { get { return Projectile != null ? Projectile.fireSound : gunShoot; } }

        private float _currentCooldown;
        private AudioSource _aus;
        private CircleCollider2D _range;

        private HashSet<GameObject> _currentTargets;
        private GameObject _gun;

        // Child lookups happen once per instance; pooled towers keep them across reuse.
        protected virtual void Awake()
        {
            var gun = transform.Find("Gun");
            if (gun == null) Debug.LogError($"Tower '{name}' needs a child named \"Gun\".", this);
            else _gun = gun.gameObject;
            _aus = GetComponent<AudioSource>();
            _range = GetComponent<CircleCollider2D>();
        }

        protected virtual void OnEnable()
        {
            CurrentLevel = 1;
            _upgrading = false;
            _currentTargets = new HashSet<GameObject>();
            ApplyLevel();
            _currentCooldown = Cooldown;
        }

        // Spends the next level's cost and raises the level. False (and no charge) if the tower
        // is maxed, inactive, has no valid definition, or the player cannot afford it.
        public bool TryUpgrade(MoneyScript money)
        {
            if (_upgrading || !isActiveAndEnabled || money == null || !CanUpgrade) return false;
            _upgrading = true;
            try
            {
                if (!money.TrySpend(NextUpgradeCost)) return false;
                CurrentLevel++;
                ApplyLevel();
                LevelChanged?.Invoke();
                return true;
            }
            finally { _upgrading = false; }
        }

        // Applies the current level's stats to components. Projectile towers resize their
        // targeting collider; cooldown and damage are read when firing.
        protected virtual void ApplyLevel()
        {
            if (Projectile != null && _range != null) _range.radius = Projectile.RangeAt(CurrentLevel);
        }

        // Keeps the legacy cost field (shown by older tools/labels) in sync with the definition.
        protected virtual void OnValidate()
        {
            if (definition != null) cost = definition.purchaseCost;
        }

        public void Destroy()
        {
            _currentTargets = null;
            gameObject.SetActive(false);
        }

        protected Transform GunTransform { get { return _gun.transform; } }

        protected internal virtual GameObject SelectTarget()
        {
            return GetMostDistantInRange();
        }

        protected virtual void OnDisable() { }

        protected virtual void Update()
        {
            GameObject target = SelectTarget();

            if (target == null)
            {
                UpdateAttack(null);
                return;
            }

            var change = target.transform.position - _gun.transform.position;
            var angle = Vector3.Angle(change, _gun.transform.up);
            if (Vector3.Cross(change, _gun.transform.up).z > 0) angle *= -1;
            _gun.transform.Rotate(0, 0, angle * Time.deltaTime * RotationSpeed);

            UpdateAttack(target);
        }

        protected virtual void UpdateAttack(GameObject target)
        {
            if (target == null) return;
            if (_currentCooldown > 0.0f) _currentCooldown -= Time.deltaTime;
            else
            {
                _currentCooldown = Cooldown;
                Shoot(target);
            }
        }

        // Cached so pruning inactive targets does not allocate every frame.
        private static readonly Predicate<GameObject> IsGone = go => go == null || !go.activeInHierarchy;

        public GameObject GetMostDistantInRange()
        {
            if (_currentTargets == null) return null;

            _currentTargets.RemoveWhere(IsGone);

            GameObject target = null;
            float maxDistance = 0;

            foreach (var enemy in _currentTargets)
            {
                var es = enemy.GetComponent<WalkerScript>();
                if (es == null) continue;
                if (maxDistance < es.GetProgress())
                {
                    maxDistance = es.GetProgress();
                    target = enemy;
                }
            }

            return target;
        }

        internal void OnTriggerEnter2D(Collider2D other)
        {
            // Track only enemies; bullets, rockets and coins also have colliders.
            if (_currentTargets == null || other.GetComponent<WalkerScript>() == null) return;
            _currentTargets.Add(other.gameObject);
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (_currentTargets == null) return;
            _currentTargets.Remove(other.gameObject);
        }

        public virtual void Shoot(GameObject target)
        {
            bool fromDefinition = Projectile != null;
            int damage = Damage;
            System.Action<BulletScript> bulletSetup = (b) =>
            {
                b.target = target;
                b.source = gameObject;
                if (fromDefinition) b.damage = damage;
            };
            if (FireSound != null) _aus.PlayOneShot(FireSound);
            ObjectPooler.InstantiatePooled<BulletScript>(ProjectilePrefab, _gun.transform.position, _gun.transform.rotation, bulletSetup);

        }
    }
}
