using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TowerDefence
{
    [RequireComponent(typeof(DestroyScript))]
    [RequireComponent(typeof(Health))]
    public class EnemyScript : MonoBehaviour
    {
        private GameManager _manager;
        private MoneyScript _money;
        private Health _enemyHealth;

        public int strength;
        public int reward;

        // Called by EnemyManager's pool setup callback on every spawn (after OnEnable),
        // so enemies no longer look the level up by GameObject name.
        public void Init(GameManager manager, MoneyScript money)
        {
            _manager = manager;
            _money = money;
        }

        void OnEnable()
        {
            _enemyHealth = GetComponent<Health>();
            _enemyHealth.Initialize();
        }

        void OnDisable()
        {
            CancelInvoke();
        }

        private void Update()
        {
            if (_enemyHealth.isDead)
            {
                if (_money != null)
                {
                    _money.AddBudget(reward);
                    _money.CreateCoin(transform.position);
                }
                Destroy();
            }
        }

        public void Destroy(bool silent = false)
        {
            gameObject.SetActive(false);
            // Only spawned enemies were counted by GameManager.AddEnemy.
            if (_manager != null) _manager.RemoveEnemy();
            if (silent) return;
            var ds = gameObject.GetComponent<DestroyScript>();
            ds.Perform();      
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            var bullet = other.gameObject.GetComponent<BulletScript>();
            if (bullet == null) return;
            _enemyHealth.TakeDamage(bullet.damage);
            bullet.Destroy();
        }
    }
}
