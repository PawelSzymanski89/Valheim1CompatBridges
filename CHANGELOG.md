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
