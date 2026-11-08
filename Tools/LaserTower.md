# Laser Tower

The Laser button is available in Level01, Level02, and Level03. Select it,
then click an empty build spot. Click its occupied build spot to open upgrades.
The beam attacks one ground enemy at a time, prioritizing furthest path progress.

| Level | Damage/second | Range | Cost |
| --- | ---: | ---: | ---: |
| 1 | 2 | 250 | 6 to build |
| 2 | 3.5 | 300 | 6 to upgrade |
| 3 | 5 | 350 | 9 to upgrade |

Tune these values in `Assets/Settings/Towers/LaserTower.asset`. Laser placement
reads the purchase cost directly from that definition. If you change its cost,
update the Laser selection button's label in each scene to match.

The explicit **Tools > Tower Defence > Install Laser Tower** Editor command
creates missing Laser assets and wires all three saved gameplay scenes. It can
be rerun without duplicating its button or panel. It preserves an existing Laser
definition and prefab, including manual balance changes.

## Validation

Close the Editor before running automation:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\unity-check.ps1 -Mode Compile
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\unity-check.ps1 -Mode PlayMode
```

Tests cover fractional damage at multiple frame rates, integer projectile damage,
spending, upgrades, actual pool reuse, targeting, range expansion, continuous
attack, and real selection/placement/upgrade UI in all three levels.
Play each level to check the beam's appearance, placement, upgrade panel, HUD,
coins, existing bullet/rocket towers, and win/loss behavior at different resolutions.
