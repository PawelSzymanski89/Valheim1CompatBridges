# Valheim1CompatBridges

BepInEx **preloader patcher** for Valheim 1.0 (Unity 6). It adds the *old* method overloads, fields and
interface defaults back into `assembly_valheim.dll` at load time, so mods compiled against pre-1.0 builds stop
crashing with `MissingMethodException`, `MissingFieldException: ZRoutedRpc.Everybody`, `TypeLoadException:
VTable setup failed` or Harmony `Undefined target method`. **No mod DLL is redistributed.**

Tested with (client + dedicated server, BepInEx 5.4.2350):

| Mod | Result |
|---|---|
| MagicMike-Weedheim 2.0.4 | loads and works |
| MagicMike-WeedheimShip 1.0.2 | loads and works |
| KGvalheim-Marketplace_And_Server_NPCs_Revamped 9.8.1 | loads and works, with `fix-kg-marketplace.ps1` applied once (see below); NPC map **right-click** (fashion) has no equivalent in 1.0 and does nothing; transmog icons in the inventory grid are not refreshed |

## Coverage, measured

Checked against the newest versions of a 69-mod pack: 35 of them have a 1.0 update of their own (install those
instead), and of the 34 with no update, 14 were already clean while **20 load thanks to these bridges, with none
left failing** the static check. That check (`tools/apicheck.ps1` in the repo) only covers missing/renamed API,
though - it cannot see a mod's own hardcoded IL, so it is not a substitute for launching the game.

## Install

Put the `patchers/Valheim1CompatBridges` folder into `BepInEx/patchers/` on the client **and** on the
dedicated server (r2modman / Thunderstore Mod Manager do this automatically). Look for
`[Valheim1CompatBridges] Gotowe: N mostkow` in `BepInEx/LogOutput.log`.

## What it bridges

- `Inventory.Changed()` → `Changed(false, false)` (original renamed to `ChangedEx`, game calls rerouted so
  Harmony postfixes on `Changed` still fire)
- `ZRoutedRpc.Everybody` const → static field
- `InventoryGrid.Element` wrapper type (`m_go`, `m_pos`, mirrored fields), `GetElement`/`GetHoveredElement`
  overloads returning it, empty `List<Element> m_elements_compat` (deliberately *not* named `m_elements`:
  two same-named fields are legal in IL but make `AccessTools.Field(type, "m_elements")` throw
  `AmbiguousMatchException`, breaking mods that inject `___m_elements` by reflection, such as
  AzuExtendedPlayerInventory and AzuAutoStore)
- `InventoryGrid.Awake()` (called from `OnEnable`), `Minimap.OnMapRightClick()` stub
- `Hoverable.GetHoverOffset()` default implementation
- old overloads of: `Character.Message`, `MessageHud.ShowMessage`, `EffectList.Create`,
  `SEMan.AddStatusEffect` (×2), `ItemData.GetTooltip`, `Inventory.IsTeleportable`, `Humanoid.IsTeleportable`,
  `Inventory.AddItem` (12 params), `Terminal.ConsoleCommand` ctor (12 params), `Terminal.ConsoleEventArgs`
  ctor, `YesNoPopup` ctor, `Heightmap.Poke(bool)`, `TerrainComp.Save()`, `Piece.SetCreator(long)`,
  `VisEquipment.Set*Item(string, …)` (prefab name → hash)

## Using Marketplace as well?

Marketplace has two breakages the preloader cannot reach, because they live in the mod's own IL: its
`Hud.UpdatePieceList` transpiler hardcodes local slot 12 for a `Piece` that Valheim 1.0 moved to 13
(`InvalidProgramException`, quest markers missing from the build menu), and it reads the old
`InventoryGrid.m_elements` field from IL. The mod is closed-source, so there is no upstream fix to wait for.
Run this once per machine, against your own copy of the DLL:

```
pwsh -File tools/fix-kg-marketplace.ps1 -Dll "<game>/BepInEx/plugins/KGvalheim-Marketplace/kg.Marketplace.dll" -Apply
```

It reads the real slot numbers out of your `assembly_valheim.dll` instead of trusting a hardcoded one, is
idempotent, and refuses to guess if the layout it expects is gone. Keep the original DLL, and re-run it after
every Marketplace update. The script lives in the repo, not in this package.

Source, and why this is a new project rather than pull requests:
https://github.com/PawelSzymanski89/Valheim1CompatBridges
