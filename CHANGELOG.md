## 1.2.0
- Three more bridges, measured against the newest versions of a 69-mod pack: `VisEquipment.SetUtilityItem(string)`
  (1.0 takes an item hash), `VisEquipment.AttachArmor(int, int)` (gained a third `quality` parameter) and
  `Inventory.AddItem(ItemData, int, int, int)` (gained `skipValidPositionCheck`). With these, **all 34 mods in
  that pack that have no 1.0 update of their own pass the static compatibility check** - 14 were already clean,
  20 need the bridges.
- `tools/apicheck.ps1` added, with a fix: calls to generic methods were reported as missing, because a
  reference's `!!0`/`!!1` never matches the game's `T`/`U` textually. It falsely flagged
  `ZRoutedRpc.Register<T>`, `ZRpc.Register<T>`, `ZNetView.Register<T,U>` and `ShuffleClass.Shuffle<T>`
  across 11 mods. Such a reference now matches on name and parameter count, but only when the game really
  has a generic method there.

## 1.1.0
- `m_elements` shadow field renamed to `m_elements_compat`. Two fields of the same name made
  `AccessTools.Field(type, "m_elements")` throw `AmbiguousMatchException`, which broke
  AzuExtendedPlayerInventory and AzuAutoStore (both inject `___m_elements` by reflection).
- Added `tools/fix-kg-marketplace.ps1`: retargets Marketplace's IL reference to the renamed field, and
  fixes its `Hud.UpdatePieceList` transpiler, which hardcoded local slot 12 for the `Piece` that Valheim 1.0
  moved to slot 13 (`InvalidProgramException`, missing quest markers in the build menu).
- Verified alongside 21 plugins on client and dedicated server.

## 1.0.0
- First release: 29 bridges/injections; Weedheim, WeedheimShip and Marketplace And Server NPCs 9.8.1 load
  on Valheim 1.0.
