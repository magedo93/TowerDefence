using UnityEngine;

namespace TowerDefence
{
    // Data shared by every tower type. A tower prefab's TowerScript.definition points to
    // one of the concrete subclasses (ProjectileTowerDefinition, LaserTowerDefinition).
    public abstract class TowerDefinition : ScriptableObject
    {
        [Tooltip("Name shown to the player, e.g. in build buttons.")]
        public string displayName;

        [Min(0)] public int purchaseCost;

        public virtual bool IsValid { get { return purchaseCost >= 0; } }

        protected static bool IsPositiveFinite(float value)
        {
            return value > 0 && !float.IsInfinity(value) && !float.IsNaN(value);
        }
    }
}
