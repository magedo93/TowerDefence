# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

2D tower defence originally written in Unity 2018 (a university course project), now being modernized for **Unity 6 (`6000.6.8f1`, see `ProjectSettings/ProjectVersion.txt`)**. Art is Kenney's CC0 tower-defense pack; sounds from Freesound. Target platform is Windows.

`AGENTS.md` holds the binding project rules. The key ones:
- Workflow: analyze → inspect affected scripts/assets → **propose and wait for approval** → implement → validate → give Unity Editor test steps.
- Preserve asset GUIDs and `.meta` files. Never delete scenes, prefabs or assets without approval. Never edit `Library/`, `Temp/` or generated files.
- Keep changes small. Avoid large rewrites and new third-party packages. Preserve the existing object-pooling behavior.
- Do not claim Unity compilation succeeded unless it was verified. Report missing references and Inspector changes the user must make.

## Commands

All validation goes through `Tools/unity-check.ps1`. **Close the Unity Editor first**: the script refuses to run when the project is open and never removes lock files.

```powershell
# Check the installation, version and manifest only (does not launch Unity)
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\unity-check.ps1
# Batch-mode import + compile (Editor compilation only, not a player build)
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\unity-check.ps1 -Mode Compile
# Run Unity Test Framework tests
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\unity-check.ps1 -Mode PlayMode
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\unity-check.ps1 -Mode EditMode
# Windows 64-bit player build of the enabled Build Settings scenes -> Builds/Windows/TowerDefense.exe
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\unity-check.ps1 -Mode Build
```

- `-Mode Build` runs `BuildScript.BuildWindows` (`Assets/Editor/BuildScript.cs`, also under **Tools > Tower Defence > Build Windows Player**). It is the only check that catches Editor-only code (`using UnityEditor;` without `#if UNITY_EDITOR`) in runtime scripts, so run it after touching runtime code.
- The game defines a global `Path` class (`Assets/Plugins/Paths`), which hides `System.IO.Path`. Write `System.IO.Path` explicitly.

- The script finds the Editor through `-EditorPath`, then `$env:UNITY_EDITOR_PATH`, then the default Unity Hub install path. The Editor version must exactly match `ProjectVersion.txt`.
- Each run writes `automation.log`, `Unity.log` and `TestResults.xml` to `AutomationLogs/<timestamp>-<mode>-<id>/`. A run fails on compiler errors found in the log even if Unity exits with 0. It also fails when zero tests execute.
- The script has no test filter. To run a single test, use the Unity Test Runner window. Alternatively, call Unity directly with `-runTests -testPlatform PlayMode -testFilter <FullTestName>`.
- **Assemblies:** `TowerDefence.Paths` (`Assets/Plugins/Paths`), `TowerDefence.Paths.Editor`, `TowerDefence.Runtime` (`Assets/Scripts`), `TowerDefence.Editor` (`Assets/Editor`, all Editor tools). Nothing compiles into `Assembly-CSharp` any more.
- **Tests:** PlayMode tests in `Assets/Tests/PlayMode/` (assembly `TowerDefence.PlayModeTests`: `LaserTowerTests`, `CoreGameplayTests`); EditMode tests in `Assets/Tests/EditMode/` (`TowerDefence.EditModeTests`: `SceneIntegrityTests`, which checks every build scene and prefab for missing scripts, broken references, unresolvable UnityEvent hookups and unassigned key fields). Tests reference the runtime assembly directly; `internal` test hooks are exposed through `Assets/Scripts/AssemblyInfo.cs`. Run both `-Mode EditMode` and `-Mode PlayMode`.
- **Moving or renaming a script's type** (assembly or namespace) leaves stale `Type, Assembly` strings inside UnityEvent hookups. `SceneIntegrityTests` flags them; **Tools > Tower Defence > Fix UnityEvent Type Names** (`UnityEventTypeNameFixer.FixAll`, batch-capable) rewrites them, fixing prefab-owned values in the prefab rather than as scene overrides.
- **Lighting:** keep build scenes on the Unity Compute lightmapper with baked GI off (**Tools > Tower Defence > Disable Baked Lighting in Build Scenes**). The Progressive GPU baker needs an OpenCL device and makes batch builds log an error. `-Mode Build` fails on any build-report error.

## Architecture

**Scenes** (`Assets/Scenes`): `MainMenu`, then `Level01`–`Level03`. `Loading.StartGame(levelName)` loads a level by name. `GameManager.BackToMenu` loads `"MainMenu"` by name, so renaming a scene breaks these calls.

**GameManager object (one per level scene).** Several scripts find it with `GameObject.Find("GameManager")`, so the object's name is load-bearing. It carries:
- `GameManager`: win/lose state, the enemies-alive counter, and the tower type currently selected in the UI (`SetTowerToBuild`). The level is won when `enemiesAlive == 0` and `EnemyManager.WavesCompleted`.
- `EnemyManager`: spawns waves through chained `Invoke` calls. Each enemy is assigned a path based on which marker component its prefab has: `Walker` → `soldierPaths`, `Vehicle` → `vehiclekPaths`, `Aircraft` → `planePaths`. Its trigger collider is the base: an enemy that enters it damages `PlayerHealth`.
- `MoneyScript`: holds the budget (`TrySpend`, `AddBudget`) and spawns coins.

**Waves** are `WaveProperties` ScriptableObjects in `Assets/Waves` (menu: Level/Wave). Each wave is a list of subwaves, and each subwave has an enemy prefab, a count and timings. They are assigned to `EnemyManager.waves` in each scene.

**Enemies.** `EnemyScript` handles reward and death and requires `Health` and `DestroyScript`. Movement comes from a `WalkerScript` subclass (`TankScript`, `PlaneScript`) that follows a `PathCreator` path (`Assets/Plugins/Paths`). Spawning and despawning must keep `GameManager.AddEnemy`/`RemoveEnemy` balanced, or the level will never be won. `EnemyScript.Destroy(silent)` calls `RemoveEnemy`.

**Pooling.** Use `ObjectPooler.InstantiatePooled(prefab, pos, rot[, Action<T> setup])` for enemies, towers, bullets and coins. It is a lazily created static singleton keyed by prefab. Pooled objects are "destroyed" with `SetActive(false)` and reused. Put reset logic in `OnEnable`/`OnDisable`, not `Start`/`Awake`. The setup callback runs after `SetActive(true)`, which means it runs after `OnEnable`.

**Towers.**
- `TowerScript` is the base class. It tracks targets from trigger enter/exit events and picks the enemy with the highest `WalkerScript.GetProgress()`. It rotates the child named `"Gun"` toward the target and fires pooled `BulletScript`/`RocketScript` projectiles.
- `BuildSpotScript.OnBuildSpotClicked` places the tower selected in `GameManager` and pays its `BuildCost`. Clicking an occupied spot opens `LaserUpgradePanel` if the tower there is a laser.
- **Laser tower** is the recent addition and the model for new configurable towers. It consists of `LaserTowerScript` plus a `LaserTowerDefinition` ScriptableObject (`Assets/Settings/Towers/LaserTower.asset`), which defines purchase cost and three levels of DPS, range and beam settings. Laser damage uses `Health.TakeAccumulatedDamage(float)`. Projectiles keep the integer `TakeDamage`.
- The Editor menu **Tools > Tower Defence > Install Laser Tower** (`Assets/Editor/LaserTowerSetup.cs`) idempotently creates the laser assets and wires them into all three level scenes. If you change the laser cost, also update the button label in each scene.
- `Tools/LaserTower.md` documents balance and validation. `Tools/LaserTowerArt.md` records how the icon was made.

**Third-party / sample code.** `Standard Assets`, the TMP "Examples & Extras" and the old 2d-extras `Plugins/Tilemap` were removed in the modernization. The two particle materials the prefabs use (`ParticleFirecloud`, `ParticleSmokeBlack`, built-in legacy particle shaders) now live in `Assets/Materials/Particles`. The only kept tilemap tool is `Assets/Editor/Tilemap/PrefabBrush.cs`, which is used by the `Assets/Tilemaps/Brushes` build-spot brushes; its GUID must stay. `Assets/Plugins/Paths` is the in-house Bezier path tool and is still runtime code. Packages are kept minimal: no Ads, Analytics, GDK, Timeline or AI Navigation. The game uses TMP for menu/win/lose text and legacy `UnityEngine.UI.Text` for the money HUD.

**Input.** The project uses only the new Input System (`activeInputHandler: 1`); the legacy `Input` class throws at runtime, so do not use it. UI input goes through `InputSystemUIInputModule` on each scene's EventSystem, with the package's `DefaultInputActions`. World clicks (build spots, coins) reach objects through `Physics2DRaycaster` plus `EventTrigger`. Keyboard checks use `Keyboard.current` (for example, Escape in `GameManager`). New scenes need an `InputSystemUIInputModule`; **Tools > Tower Defence > Migrate UI Input Modules to Input System** converts any leftover `StandaloneInputModule`.
