using System.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class TowerScript : MonoBehaviour {
    
    public GameObject bulletPrefab;
    public int cost;
    public virtual int BuildCost { get { return cost; } }
    public float rotationSpeed;

    public float cooldown;
    private float _currentCooldown;

    public AudioClip gunShoot;
    private AudioSource _aus;

    private HashSet<GameObject> _currentTargets;
    private GameObject _gun;

    protected virtual void OnEnable()
    {
        _currentCooldown = cooldown;
        _currentTargets = new HashSet<GameObject>();
        _gun = transform.Find("Gun").gameObject;
        Debug.Assert(_gun != null);
        _aus = gameObject.GetComponent<AudioSource>();
    }

    public void Destroy()
    {
        _currentTargets = null;
        _gun = null;
        gameObject.SetActive(false); 
    }

    protected Transform GunTransform { get { return _gun.transform; } }

    protected virtual GameObject SelectTarget()
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
        _gun.transform.Rotate(0, 0, angle * Time.deltaTime * rotationSpeed);

        UpdateAttack(target);
    }

    protected virtual void UpdateAttack(GameObject target)
    {
        if (target == null) return;
        if (_currentCooldown > 0.0f) _currentCooldown -= Time.deltaTime;
        else
        {
            _currentCooldown = cooldown;
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

    void OnTriggerEnter2D(Collider2D other)
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
        System.Action<BulletScript> bulletSetup = (b) =>
        {
            b.target = target;
            b.source = gameObject;
        };
        _aus.PlayOneShot(gunShoot);
        ObjectPooler.InstantiatePooled<BulletScript>(bulletPrefab, _gun.transform.position, _gun.transform.rotation, bulletSetup);

    }
}
