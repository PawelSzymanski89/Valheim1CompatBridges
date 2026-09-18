<#
Dwie poprawki binarne do kg.Marketplace.dll (Marketplace And Server NPCs Revamped 9.8.1) pod Valheima 1.0.
Mod jest zamknietozrodlowy, wiec nie da sie zrobic PR-a - patchujemy WLASNA kopie DLL-a Cecilem.
Skrypt jest idempotentny: drugie uruchomienie nic nie zmieni.

  pwsh -File fix-kg-marketplace.ps1 -Dll "<sciezka>/BepInEx/plugins/KGvalheim-Marketplace/kg.Marketplace.dll" -Apply

1) Hud_UpdatePieceList_Patch: transpiler wstrzykuje `ldloc.s 12` licząc, ze slot 12 metody
   Hud.UpdatePieceList trzyma `Piece`. W 1.0 metoda zostala przepisana - slot 12 to `bool`,
   a `Piece` przeniosl sie na 13. Efekt: InvalidProgramException / IL Compile Error i znikajace
   znaczniki celow questow w menu budowania. Zmieniamy stala 12 -> 13.

2) InventoryGrid_UpdateGui_Patch: mod czyta `InventoryGrid.m_elements` jako `List<InventoryGrid/Element>`
   (typ z gry < 1.0). Valheim1CompatBridges dorabia to pole, ale pod nazwa `m_elements_compat` - nie moze
   nazywac sie `m_elements`, bo dwa pola o tej nazwie (obok prawdziwego `List<InventoryElement>`) sa legalne
   w IL, ale `AccessTools.Field(type,"m_elements")` rzuca wtedy AmbiguousMatchException i psuje kazdy mod
   wstrzykujacy `___m_elements` przez refleksje (AzuExtendedPlayerInventory, AzuAutoStore).
   Przekierowujemy referencje moda na `m_elements_compat`.
#>
param(
  [Parameter(Mandatory=$true)][string]$Dll,
  [switch]$Apply,
  [string]$GamePath = "$HOME/Library/Application Support/Steam/steamapps/common/Valheim"
)
$managed = Get-ChildItem -Path $GamePath -Recurse -Filter assembly_valheim.dll -ErrorAction SilentlyContinue |
           Select-Object -First 1 | ForEach-Object { $_.DirectoryName }
if (-not $managed) { throw "Nie znalazlem assembly_valheim.dll pod $GamePath - podaj -GamePath" }
Add-Type -Path (Join-Path $GamePath "BepInEx/core/Mono.Cecil.dll")

$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadWrite = $Apply.IsPresent
$res = New-Object Mono.Cecil.DefaultAssemblyResolver
$res.AddSearchDirectory($managed)
$res.AddSearchDirectory((Join-Path $GamePath "BepInEx/core"))
$res.AddSearchDirectory((Split-Path $Dll))
$rp.AssemblyResolver = $res

# slot `Piece` w Hud.UpdatePieceList czytamy z gry, nie zakladamy 13 na slepo
$vh = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $managed "assembly_valheim.dll"), (New-Object Mono.Cecil.ReaderParameters))
$upl = $vh.MainModule.GetType("Hud").Methods | Where-Object { $_.Name -eq 'UpdatePieceList' -and $_.HasBody } | Select-Object -First 1
if (-not $upl) { throw "Brak Hud.UpdatePieceList w assembly_valheim.dll" }
$pieceSlot = ($upl.Body.Variables | Where-Object { $_.VariableType.FullName -eq 'Piece' } | Select-Object -First 1).Index
if ($null -eq $pieceSlot) { throw "Nie znalazlem lokalnej `Piece` w Hud.UpdatePieceList" }
# mod wstrzykuje tez `ldloc.s 11` liczac, ze slot 11 to Hud.PieceIconData - tego nie zmieniamy, ale sprawdzamy
$v11 = $upl.Body.Variables | Where-Object { $_.Index -eq 11 } | Select-Object -First 1
Write-Output "Valheim: Hud.UpdatePieceList -> Piece = V_$pieceSlot, V_11 = $(if ($v11) { $v11.VariableType.FullName } else { '<brak>' })"
if (-not $v11 -or $v11.VariableType.FullName -ne 'Hud/PieceIconData') {
  Write-Warning "V_11 to nie Hud.PieceIconData - mod wstrzykuje tam ldloc.s 11 i nadal bedzie sypal niepoprawnym IL. Trzeba dorobic druga podmiane."
}
$vh.Dispose()

$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($Dll, $rp)
$changed = 0
foreach ($t in $asm.MainModule.GetTypes()) {
  foreach ($m in $t.Methods) {
    if (-not $m.HasBody) { continue }
    foreach ($i in $m.Body.Instructions) {
      # (1) stala slotu w transpilerze (iterator, wiec ciało siedzi w <Patch>d__N.MoveNext)
      if ($t.FullName -like '*Hud_UpdatePieceList_Patch*' -and $i.OpCode.Name -like 'ldc.i4*' -and
          "$($i.Operand)" -eq '12' -and $pieceSlot -ne 12) {
        Write-Output "  [1] $($t.Name).$($m.Name): ldloc.s 12 -> $pieceSlot"
        if ($Apply) { $i.Operand = [sbyte]$pieceSlot }
        $changed++
      }
      # (2) referencja do shadow-pola
      $f = $i.Operand
      if ($f -is [Mono.Cecil.FieldReference] -and $f.Name -eq 'm_elements' -and
          $f.DeclaringType.Name -eq 'InventoryGrid' -and $f.FieldType.FullName -like '*InventoryGrid/Element*') {
        Write-Output "  [2] $($t.Name).$($m.Name): m_elements -> m_elements_compat"
        if ($Apply) { $f.Name = 'm_elements_compat' }
        $changed++
      }
    }
  }
}
if ($changed -eq 0) { Write-Output "Nic do zmiany (DLL juz zalatany albo inna wersja moda)." }
elseif ($Apply)    { $asm.Write(); Write-Output "Zapisano $changed zmian." }
else               { Write-Output "$changed zmian do zrobienia (uruchom z -Apply)." }
$asm.Dispose()
