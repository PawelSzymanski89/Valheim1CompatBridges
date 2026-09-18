param([string]$Asm, [string]$Type, [string]$Method)
$V="$HOME/Library/Application Support/Steam/steamapps/common/Valheim"
Add-Type -Path "$V/BepInEx/core/Mono.Cecil.dll"
$rp = New-Object Mono.Cecil.ReaderParameters
$res = New-Object Mono.Cecil.DefaultAssemblyResolver
$res.AddSearchDirectory("$V/valheim.app/Contents/Resources/Data/Managed")
$res.AddSearchDirectory("$V/BepInEx/core")
$rp.AssemblyResolver = $res
$m = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($Asm, $rp).MainModule
$t = $m.GetTypes() | Where-Object { $_.Name -eq $Type }
foreach ($mm in ($t.Methods | Where-Object { $_.Name -eq $Method })) {
  Write-Output "=== $($mm.FullName)"
  if (-not $mm.HasBody) { Write-Output "  (brak ciala)"; continue }
  foreach ($v in $mm.Body.Variables) { Write-Output ("  V_{0}  {1}" -f $v.Index, $v.VariableType.FullName) }
}
