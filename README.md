# Valheim1CompatBridges

Pre-1.0 Valheim mods stopped loading when Valheim 1.0 (Unity 6) changed method signatures, turned
`InventoryGrid.Element` into a top-level component and made `ZRoutedRpc.Everybody` a `const`. This repo
brings three of them back on 1.0, and documents exactly how.

Two separate things live here:

1. **`Valheim1CompatBridges`** — a BepInEx *preloader patcher* that re-adds the old API to
   `assembly_valheim.dll` at load time (29 bridges/injections). Touches only the game assembly.
2. **`tools/fix-kg-marketplace.ps1`** — two binary fixes applied to *your own copy* of
   `kg.Marketplace.dll`, for breakage the preloader cannot reach (the mod's own hardcoded IL).

## Why a new repo instead of pull requests

Weedheim, WeedheimShip and Marketplace And Server NPCs Revamped have **no published sources**
(Weedheim is Discord-only with no license, KG's Marketplace is closed-source; `Tristan-dvr/KG_Marketplace`
is a 2023 BSD copy far behind 9.8.1). There is nowhere to send a patch, so the fixes are published
here as a preloader plus a script that patches the DLL you already own. No mod DLL is redistributed.

Where sources do exist, the fix goes upstream instead — see
[AlmanacClasses PR #31](https://github.com/RustyMods/AlmanacClasses/pull/31).

## Tested

Client + dedicated Linux server, BepInEx 5.4.2350, Valheim 1.0:

| Mod | Result |
|---|---|
| MagicMike-Weedheim 2.0.4 | loads and works |
| MagicMike-WeedheimShip 1.0.2 | loads and works |
| KGvalheim-Marketplace_And_Server_NPCs_Revamped 9.8.1 | loads and works with `fix-kg-marketplace.ps1` applied |

Verified to coexist with AzuExtendedPlayerInventory 2.4.14, AzuAutoStore 3.1.4, AzuCraftyBoxes 1.8.19,
CurrencyPocket 1.0.13, Unshamed 1.0.5, MyDirtyHoe 2.0.3, MassFarming 1.13.0, Jotunn 2.30.0 and eight
older community mods — 21 plugins loading together.

### Coverage, measured

Checked against the **newest** versions of a 69-mod pack. 35 of those mods have had a 1.0 update of their own -
install those, no bridge needed. Of the remaining **34 with no 1.0 update: 14 are already clean and 20 load
thanks to these bridges. None are left with a gap.**

`tools/apicheck.ps1 <mod.dll>` is the tool that produced those numbers: it statically resolves every
call, field read and `[HarmonyPatch]` in a mod against your `assembly_valheim.dll`, so it reports what would
throw `MissingMethodException` before the game starts.

**A clean scan is not proof the mod works.** It only covers that one class of breakage. It cannot see a mod's
own hardcoded IL - Marketplace scanned clean and still threw `InvalidProgramException`, because its transpiler
had a local variable slot number baked in (see below). Nothing replaces launching the game.

Known gaps, both cosmetic and both in Marketplace: right-clicking an NPC on the map ("fashion") has no
1.0 equivalent and does nothing, and transmog icons in the inventory grid are not refreshed.

## Install

Copy `patchers/Valheim1CompatBridges` from the release zip into `BepInEx/patchers/` on the client **and**
the dedicated server. Confirm in `BepInEx/LogOutput.log`:

```
[Valheim1CompatBridges] Gotowe: 29 mostkow/wstrzykniec w assembly_valheim.dll.
```

Using Marketplace as well? Also run, once, per machine:

```
pwsh -File tools/fix-kg-marketplace.ps1 -Dll "<game>/BepInEx/plugins/KGvalheim-Marketplace/kg.Marketplace.dll" -Apply
```

It reads the real local-variable slots out of your `assembly_valheim.dll` rather than trusting a hardcoded
number, is idempotent, and refuses to guess if the layout it expects is gone. Keep a copy of the original
DLL; re-run it after every Marketplace update.

## What the preloader bridges

- `Inventory.Changed()` → `Changed(false, false)`. The original is renamed to `ChangedEx` and the game's 19
  call sites are rerouted, so Harmony postfixes on `Changed` still fire.
- `ZRoutedRpc.Everybody`: `const` → static field (a `const` read is inlined at compile time, so mods
  compiled earlier look for a field that no longer exists).
- `InventoryGrid.Element`: wrapper type with the old class's fields (`m_go`, `m_pos`, 18 mirrored), plus
  `GetElement`/`GetHoveredElement` overloads returning it — the CLR allows overloads differing only in
  return type, which is what lets old and new signatures coexist.
- An empty `List<Element> m_elements_compat`, so old loops over the grid no-op instead of throwing.
  **It is deliberately not called `m_elements`:** two same-named fields are legal in IL (a field reference
  carries its type) but `AccessTools.Field(type, "m_elements")` then throws `AmbiguousMatchException`,
  which breaks every mod injecting `___m_elements` by reflection — AzuExtendedPlayerInventory and
  AzuAutoStore both do. Mods reading the old field from IL get retargeted to the new name instead.
- `InventoryGrid.Awake()` (invoked from `OnEnable`), `Minimap.OnMapRightClick()` stub,
  `Hoverable.GetHoverOffset()` default interface implementation (without it, classes in old mods fail
  `TypeLoadException: VTable setup failed`).
- `VisEquipment.SetUtilityItem(string)`, `VisEquipment.AttachArmor(int, int)` and
  `Inventory.AddItem(ItemData, int, int, int)` - the first takes an item hash in 1.0, the other two gained a
  parameter (`quality`, `skipValidPositionCheck`).
- Old overloads of `Character.Message`, `MessageHud.ShowMessage`, `EffectList.Create`,
  `SEMan.AddStatusEffect` (×2), `ItemData.GetTooltip`, `Inventory.IsTeleportable`, `Humanoid.IsTeleportable`,
  `Inventory.AddItem` (12 params), `Terminal.ConsoleCommand` ctor (12 params), `Terminal.ConsoleEventArgs`
  ctor, `YesNoPopup` ctor, `Heightmap.Poke(bool)`, `TerrainComp.Save()`, `Piece.SetCreator(long)` and
  `VisEquipment.Set*Item(string, …)` (prefab name → stable hash).

## What the Marketplace script fixes

**1. `Hud.UpdatePieceList` — `InvalidProgramException: Invalid IL code`.** Marketplace's transpiler injects
`ldloc.s 12`, assuming local slot 12 holds the `Piece`. Valheim 1.0 rewrote the method — slot 12 is now a
`bool` and `Piece` moved to 13 — so the emitted `call` gets the wrong types on the stack and the whole
patch fails to compile. Symptom in game: quest-target markers missing from the build menu. The script
changes the constant to whatever slot actually holds `Piece` in your build.

**2. `InventoryGrid.UpdateGui` — `AmbiguousMatchException`.** Marketplace reads
`InventoryGrid.m_elements` as `List<InventoryGrid.Element>` from IL. The script retargets that reference to
`m_elements_compat` (see above), which is what lets Marketplace and the Azu mods run side by side.

## Building

```
dotnet build -c Release
```

The game path defaults to the usual Steam location; override it with
`-p:BepInExCore="<game>/BepInEx/core"` if yours differs.

`Patcher.cs` is a single file; `tools/il-locals.ps1` dumps a method's IL locals with Mono.Cecil, which is how
the slot numbers above were found.
