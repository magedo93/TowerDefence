using System.Runtime.CompilerServices;

// Test assemblies may use `internal` members (test hooks such as BuildSpotScript.BuiltTower).
// Players and other game code see no difference.
[assembly: InternalsVisibleTo("TowerDefence.PlayModeTests")]
[assembly: InternalsVisibleTo("TowerDefence.EditModeTests")]
