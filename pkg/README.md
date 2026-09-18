# Valheim1CompatBridges

BepInEx **preloader patcher** for Valheim 1.0 (Unity 6). It adds the *old* method overloads, fields and
interface defaults back into `assembly_valheim.dll` at load time, so mods compiled against pre-1.0 builds stop
crashing with `MissingMethodException`, `MissingFieldException: ZRoutedRpc.Everybody`, `TypeLoadException:
VTable setup failed` or Harmony `Undefined target method`. **No mod DLL is modified or redistributed.**

Tested with (client + dedicated server, BepInEx 5.4.2350):

| Mod | Result |
|---|---|
| MagicMike-Weedheim 2.0.4 | loads and works |
| MagicMike-WeedheimShip 1.0.2 | loads and works |
| KGvalheim-Marketplace_And_Server_NPCs_Revamped 9.8.1 | loads; NPC map **right-click** (fashion) has no equivalent in 1.0 and does nothing; transmog icons in the inventory grid are not refreshed |

## Install

Put the `patchers/Valheim1CompatBridges` folder into `BepInEx/patchers/` on the client **and** on the
dedicated server (r2modman / Thunderstore Mod Manager do this automatically). Look for
`[Valheim1CompatBridges] Gotowe: N mostkow` in `BepInEx/LogOutput.log`.

## What it bridges

- `Inventory.Changed()` → `Changed(false, false)` (original renamed to `ChangedEx`, game calls rerouted so
  Harmony postfixes on `Changed` still fire)
- `ZRoutedRpc.Everybody` const → static field
- `InventoryGrid.Element` wrapper type (`m_go`, `m_pos`, mirrored fields), `GetElement`/`GetHoveredElement`
  overloads returning it, empty `List<Element> m_elements`
- `InventoryGrid.Awake()` (called from `OnEnable`), `Minimap.OnMapRightClick()` stub
- `Hoverable.GetHoverOffset()` default implementation
- old overloads of: `Character.Message`, `MessageHud.ShowMessage`, `EffectList.Create`,
  `SEMan.AddStatusEffect` (×2), `ItemData.GetTooltip`, `Inventory.IsTeleportable`, `Humanoid.IsTeleportable`,
  `Inventory.AddItem` (12 params), `Terminal.ConsoleCommand` ctor (12 params), `Terminal.ConsoleEventArgs`
  ctor, `YesNoPopup` ctor, `Heightmap.Poke(bool)`, `TerrainComp.Save()`, `Piece.SetCreator(long)`,
  `VisEquipment.Set*Item(string, …)` (prefab name → hash)

Source: https://github.com/PawelSzymanski89/Valheim1CompatBridges
