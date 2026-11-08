using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using TowerDefence.Paths;

namespace TowerDefence
{
    // Spawns the level's waves and acts as the base: an enemy entering its trigger
    // damages the player.
    //
    // Schedule (unchanged from the original Invoke chain, pinned by WaveSpawnTests):
    //   startTimeOffset, then for each wave, for each subwave:
    //     the subwave starts spawning `size` enemies, `enemyTime` apart, on one random path
    //     for its enemy type; the next subwave starts `subwaveTime` after this one STARTED
    //     (for a wave's last subwave, the next wave starts `waveTime` after it started).
    //   WavesCompleted becomes true `waveTime` after the last wave's last subwave started.
    // Differences only in cases the shipped waves never hit: a subwave still spawning when
    // the next one starts keeps its own enemy/path (the old code mixed them), and
    // completion waits for every subwave to finish spawning (the old code dropped the rest).
    public class EnemyManager : MonoBehaviour
    {
        public PlayerHealth playerHealth;
        public GameManager manager;

        public PathCreator[] soldierPaths;
        [FormerlySerializedAs("vehiclekPaths")]
        public PathCreator[] vehiclePaths;
        public PathCreator[] planePaths;

        public WaveProperties[] waves;
        public float startTimeOffset;

        public bool WavesCompleted { get; private set; }

        private MoneyScript _money;
        private int _activeSubwaves;

        private void Start()
        {
            // GameManager requires a MoneyScript on the same object.
            _money = manager != null ? manager.GetComponent<MoneyScript>() : null;
            WavesCompleted = false;
            StartCoroutine(RunWaves());
        }

        private IEnumerator RunWaves()
        {
            yield return new WaitForSeconds(startTimeOffset);

            foreach (var wave in waves ?? new WaveProperties[0])
            {
                var subwaves = wave != null ? wave.subwave : null;
                if (subwaves == null || subwaves.Length == 0)
                {
                    Debug.LogWarning($"[EnemyManager] Wave '{(wave != null ? wave.name : "null")}' has no subwaves.", this);
                    if (wave != null) yield return new WaitForSeconds(wave.waveTime);
                    continue;
                }

                for (int i = 0; i < subwaves.Length; i++)
                {
                    var subwave = subwaves[i];
                    var path = PathFor(subwave.Enemy);
                    if (path != null) StartCoroutine(SpawnSubwave(subwave, path));

                    bool lastInWave = i == subwaves.Length - 1;
                    yield return new WaitForSeconds(lastInWave ? wave.waveTime : subwave.subwaveTime);
                }
            }

            while (_activeSubwaves > 0) yield return null;
            WavesCompleted = true;
        }

        private IEnumerator SpawnSubwave(WaveProperties.Subwave subwave, PathCreator path)
        {
            _activeSubwaves++;
            try
            {
                for (int n = 0; n < subwave.size; n++)
                {
                    if (n > 0) yield return new WaitForSeconds(subwave.enemyTime);
                    if (playerHealth.isDead) yield break;
                    Spawn(subwave.Enemy, path);
                }
            }
            finally
            {
                _activeSubwaves--;
            }
        }

        private void Spawn(GameObject enemyPrefab, PathCreator path)
        {
            manager.AddEnemy();
            ObjectPooler.InstantiatePooled<WalkerScript>(enemyPrefab, path.GetStartPoint(), path.GetStartRotation(), walker =>
            {
                walker.pathCreator = path;
                var enemy = walker.GetComponent<EnemyScript>();
                if (enemy != null) enemy.Init(manager, _money);
            });
        }

        // The enemy's movement component decides which path list it uses.
        private PathCreator PathFor(GameObject enemyPrefab)
        {
            PathCreator[] candidates = null;
            if (enemyPrefab == null) { }
            else if (enemyPrefab.GetComponent<Walker>() != null) candidates = soldierPaths;
            else if (enemyPrefab.GetComponent<Vehicle>() != null) candidates = vehiclePaths;
            else if (enemyPrefab.GetComponent<Aircraft>() != null) candidates = planePaths;

            if (candidates == null || candidates.Length == 0)
            {
                Debug.LogError($"[EnemyManager] No path for enemy '{(enemyPrefab != null ? enemyPrefab.name : "null")}'; subwave skipped.", this);
                return null;
            }
            return candidates[Random.Range(0, candidates.Length)];
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            // Only enemies damage the base; ignore bullets, rockets and anything else.
            var enemy = other.gameObject.GetComponent<EnemyScript>();
            if (enemy == null || !enemy.isActiveAndEnabled) return;
            playerHealth.TakeDamage(enemy.strength);
            enemy.Destroy(true);
        }
    }
}
