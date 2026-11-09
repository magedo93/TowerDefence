using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace TowerDefence
{
    [RequireComponent(typeof(AudioSource))]
    public class MoneyScript : MonoBehaviour
    {
        public int startingWealth;
        protected int currentWealth;

        public float goldProbability;
        public float silverProbability;
        public float cooperProbability;

        public GameObject goldPrefab;
        public GameObject silverPrefab;
        public GameObject cooperPrefab;
        public AudioClip coinAudio;

        public TMP_Text moneyText;

        private AudioSource _aus;
        public event System.Action<int> BalanceChanged;

        public void Awake()
        {
            _aus = gameObject.GetComponent<AudioSource>();
            currentWealth = startingWealth;
            moneyText.text = currentWealth.ToString();
        }

        public void CreateCoin(Vector3 position)
        {
            var rnd = Random.Range(0.0f, 1.0f);

            GameObject coinType;
            if (rnd < goldProbability)
            {
                coinType = goldPrefab;
            }
            else if (rnd < goldProbability + silverProbability)
            {
                coinType = silverPrefab;
            }
            else if (rnd < goldProbability + silverProbability + cooperProbability)
            {
                coinType = cooperPrefab;
            }
            else return;

            var coin = coinType.GetComponent<CoinScript>();
            Debug.Assert(coin != null);
            int coinPrice = coin.price;

            System.Action<BaseEventData> callback = (c) => AddBudgetWithSound(coinPrice);
            System.Action<EventTrigger> coinSetup = (c) =>
            {
                // Coins are pooled, so this runs on every reuse. Use one dedicated runtime
                // entry and replace its listener instead of appending a new entry each time,
                // otherwise a reused coin would pay out once per previous use.
                // The prefab's own PointerClick entry (persistent SetActive(false)) is untouched.
                var entry = c.triggers.Find(e => e.eventID == EventTriggerType.PointerClick && e.callback.GetPersistentEventCount() == 0);
                if (entry == null)
                {
                    entry = new EventTrigger.Entry
                    {
                        eventID = EventTriggerType.PointerClick,
                        callback = new EventTrigger.TriggerEvent()
                    };
                    c.triggers.Add(entry);
                }
                entry.callback.RemoveAllListeners();
                entry.callback.AddListener(new UnityEngine.Events.UnityAction<BaseEventData>(callback));
            };

            ObjectPooler.InstantiatePooled(coinType, position + new Vector3(Random.Range(-25.0f, 25.0f), Random.Range(-25.0f, 25.0f), 0), Quaternion.identity, coinSetup);
        }

        public void AddBudgetWithSound(int amount)
        {
            AddBudget(amount);
            _aus.PlayOneShot(coinAudio);
        }

        public void AddBudget(int amount)
        {
            currentWealth += amount;
            moneyText.text = currentWealth.ToString();
            BalanceChanged?.Invoke(currentWealth);
        }

        public void RemoveBudget(int amount)
        {
            currentWealth -= amount;
            moneyText.text = currentWealth.ToString();
            BalanceChanged?.Invoke(currentWealth);
        }

        public bool TrySpend(int amount)
        {
            if (amount < 0 || amount > currentWealth) return false;
            RemoveBudget(amount);
            return true;
        }

        public int GetWealth()
        {
            return currentWealth;
        }
    }
}
