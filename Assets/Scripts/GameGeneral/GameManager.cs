using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace TowerDefence
{
    [RequireComponent(typeof(MoneyScript))]
    [RequireComponent(typeof(EnemyManager))]
    public class GameManager : MonoBehaviour {

        public TextMeshProUGUI winningMsg;
        public TextMeshProUGUI loosingMsg;

        public float MenuReturnTime;

        private GameObject towerToBuild;
        public EnemyManager enemyManager;

        private bool ended;
        private int enemiesAlive;

        public void Start()
        {
            towerToBuild = null;
            ended = false;
            Time.timeScale = 1;
            winningMsg.gameObject.SetActive(false);
            loosingMsg.gameObject.SetActive(false);
            enemiesAlive = 0;
        }

        public void GameOver()
        {
            if (ended) return;
            ended = true;
            loosingMsg.gameObject.SetActive(true);
            Invoke("BackToMenu", MenuReturnTime);
        }

        public void GameWin()
        {
            if (ended) return;
            ended = true;
            winningMsg.gameObject.SetActive(true);
            Invoke("BackToMenu", MenuReturnTime);
        }

        private void BackToMenu()
        {
            SceneManager.LoadScene("MainMenu");
        }

        public void Update()
        {
            if (enemiesAlive == 0 && enemyManager.WavesCompleted) GameWin();
            // New Input System; the legacy Input Manager is disabled in this project.
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                BackToMenu();
            }
        }

        public void SetTowerToBuild(GameObject go)
        {
            if (go.GetComponent<TowerScript>() == null) return;
            towerToBuild = go;
        }

        public GameObject GetTowerToBuild()
        {
            return towerToBuild;
        }

        public void AddEnemy()
        {
            ++enemiesAlive;
        }

        public void RemoveEnemy()
        {
            --enemiesAlive;
        }
    }
}
