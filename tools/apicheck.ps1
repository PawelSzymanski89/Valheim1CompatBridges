# Statyczny test zgodnosci moda z assembly_valheim (Valheim 1.0): kazde odwolanie (call/ldfld/HarmonyPatch)
# do typu z gry musi istniec w obecnej wersji. Lapie to samo, co MissingMethodException w grze - ale przed startem.
# Wersja na Maca: sciezki gry z Steam, Mono.Cecil z BepInEx\core.
param([string]$dll)
$V = "$HOME/Library/Application Support/Steam/steamapps/common/Valheim"
$managed = "$V/valheim.app/Contents/Resources/Data/Managed"
Add-Type -Path "$V/BepInEx/core/Mono.Cecil.dll"
$res = New-Object Mono.Cecil.DefaultAssemblyResolver
$res.AddSearchDirectory($managed); $res.AddSearchDirectory("$V/BepInEx/core"); Get-ChildItem "$V/BepInEx/plugins" -Directory | ForEach-Object { $res.AddSearchDirectory($_.FullName) }
$rp = New-Object Mono.Cecil.ReaderParameters; $rp.AssemblyResolver = $res
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly("$managed/assembly_valheim.dll", $rp).MainModule
$mod = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($dll, $rp).MainModule
$missing = @{}; $checked = 0
function FindType($name) { $game.GetType($name) }
foreach ($t in $mod.GetTypes()) {
  foreach ($m in $t.Methods) {
    # atrybuty HarmonyPatch(typeof(X), "Y")
    foreach ($ca in ($t.CustomAttributes + $m.CustomAttributes)) {
      if ($ca.AttributeType.Name -ne "HarmonyPatch" -or $ca.ConstructorArguments.Count -lt 2) { continue }
      $a0 = $ca.ConstructorArguments[0]; $a1 = $ca.ConstructorArguments[1]
      if ($a0.Type.FullName -ne "System.Type" -or $a1.Type.FullName -ne "System.String") { continue }
      $tr = $a0.Value; if ($tr.Scope.Name -notmatch "assembly_valheim") { continue }
      $gt = FindType $tr.FullName; $checked++
      if (-not $gt -or -not ($gt.Methods | Where-Object { $_.Name -eq $a1.Value })) { $missing["HarmonyPatch $($tr.FullName).$($a1.Value)"] = 1; continue }
      # HarmonyPatch(typeof(X), "Y", typeof(A), typeof(B)...) - przypieta konkretna sygnatura musi istniec co do parametru
      if ($ca.ConstructorArguments.Count -ge 3 -and $ca.ConstructorArguments[2].Value -is [System.Array]) {
        $want = ($ca.ConstructorArguments[2].Value | ForEach-Object { $_.Value.FullName }) -join ","
        $ok = $gt.Methods | Where-Object { $_.Name -eq $a1.Value -and (($_.Parameters | ForEach-Object { $_.ParameterType.FullName }) -join ",") -eq $want }
        if (-not $ok) { $missing["HarmonyPatch $($tr.FullName).$($a1.Value)($want) - inna sygnatura w 1.0"] = 1 }
      }
    }
    if (-not $m.HasBody) { continue }
    foreach ($ins in $m.Body.Instructions) {
      $op = $ins.Operand
      if ($op -isnot [Mono.Cecil.MemberReference]) { continue }
      $dt = $op.DeclaringType; if (-not $dt -or $dt.Scope.Name -notmatch "assembly_valheim") { continue }
      $gt = FindType $dt.FullName; if ($dt.IsGenericInstance) { $gt = FindType $dt.ElementType.FullName }
      $checked++
      if (-not $gt) { $missing["typ $($dt.FullName)"] = 1; continue }
      if ($op -is [Mono.Cecil.MethodReference]) {
        $sig = ($op.Parameters | ForEach-Object { $_.ParameterType.FullName }) -join ","
        $ok = $gt.Methods | Where-Object { $_.Name -eq $op.Name -and (($_.Parameters | ForEach-Object { $_.ParameterType.FullName }) -join ",") -eq $sig }
        # Wywolanie metody generycznej: w sygnaturze siedzi !!0/!!1 (parametr metody), ktory NIGDY nie zrowna sie
        # tekstowo z T/U z gry. Wtedy porownujemy po nazwie i liczbie parametrow - ale tylko jesli gra faktycznie
        # ma tam metode generyczna, zeby nie przepuscic prawdziwego braku.
        if (-not $ok -and $sig -match '!!') {
          $ok = $gt.Methods | Where-Object { $_.Name -eq $op.Name -and $_.HasGenericParameters -and $_.Parameters.Count -eq $op.Parameters.Count }
        }
        if (-not $ok) { $missing["metoda $($dt.FullName).$($op.Name)($sig)"] = 1 }
      } elseif ($op -is [Mono.Cecil.FieldReference]) {
        $gf = $gt.Fields | Where-Object { $_.Name -eq $op.Name } | Select-Object -First 1
        if (-not $gf) { $missing["pole $($dt.FullName).$($op.Name)"] = 1 }
        # const (literal) czytany jako pole statyczne = MissingFieldException "Using static instructions with literal field"
        elseif ($gf.HasConstant -and $ins.OpCode.Name -match "sfld") { $missing["const-jako-pole $($dt.FullName).$($op.Name) (1.0 ma const; przekompilowac mod)"] = 1 }
      }
    }
  }
}
"$([IO.Path]::GetFileName($dll)): sprawdzono $checked odwolan do gry, brakujacych: $($missing.Count)"
$missing.Keys | Sort-Object
