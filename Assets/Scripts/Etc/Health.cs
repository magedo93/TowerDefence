using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class Health : MonoBehaviour
{
    public int startingHealth = 100;
    protected int currentHealth;

    public bool isDead { get; protected set; }
    protected bool damaged;
    private double _fractionalDamage;

    void Awake()
    {
        Initialize();
    }

    void Update()
    {
        damaged = false;
    }

    public void Initialize()
    {
        currentHealth = startingHealth;
        damaged = false;
        isDead = false;
        _fractionalDamage = 0;
    }

    public int GetHealth()
    {
        return currentHealth;
    }

    public virtual void TakeDamage(int amount)
    {
        damaged = true;
        currentHealth -= amount;
        if (currentHealth <= 0 && !isDead) Death();
    }

    // Keep fractional damage on the enemy so multiple beams combine correctly.
    // Existing projectile damage continues to use TakeDamage(int) unchanged.
    public void TakeAccumulatedDamage(float amount)
    {
        if (isDead || amount <= 0 || float.IsNaN(amount) || float.IsInfinity(amount)) return;
        _fractionalDamage += amount;
        int wholeDamage = (int)System.Math.Min(currentHealth, System.Math.Floor(_fractionalDamage + 0.000001));
        if (wholeDamage <= 0) return;
        _fractionalDamage = System.Math.Max(0, _fractionalDamage - wholeDamage);
        TakeDamage(wholeDamage);
    }

    protected virtual void Death()
    {
        isDead = true;
    }
}
