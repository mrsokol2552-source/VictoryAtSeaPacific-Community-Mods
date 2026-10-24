Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$script:GameHash='7acb920771bb47779d29c9a4534c78a601837c6af8bce096ea46cb63d212aa23'
$script:OwnedPaths=@('BepInEx/plugins/VaspHotfix.dll','BepInEx/plugins/VaspScoutPatrol/VaspScoutPatrol.dll','BepInEx/plugins/VaspInterfaceTools/VaspInterfaceTools.dll','BepInEx/plugins/VaspCommunityMods/VaspCommunityMods.dll')
$script:Baseline=@('71b63f7dd4cc66353f1bf0dd3037077da7ff4b05ec3ea84a9c8609879af8def4','ceaa8776142a4d5dae536d78b319bc3b3d5a388ed7fe7088a72d9c343a0784a2','234ba06961d581f6a2796a0d89b0333c26b7162637009c795e61f4433b682a06')
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Safe-Path([string]$Root,[string]$Relative) {
 if (!$Relative -or [IO.Path]::IsPathRooted($Relative) -or $Relative.Contains(':') -or $Relative -match '(^|[\\/])\.\.([\\/]|$)') { throw "Unsafe path: $Relative" }
 $rootFull=[IO.Path]::GetFullPath($Root).TrimEnd('\'); $p=[IO.Path]::GetFullPath((Join-Path $rootFull $Relative))
 if (!$p.StartsWith($rootFull+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Path escapes root' }
 $walk=$p
 while ($walk) { if ((Test-Path -LiteralPath $walk) -and ((Get-Item -LiteralPath $walk -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Reparse point refused: $walk" }; $walk=[IO.Path]::GetDirectoryName($walk) }
 $p
}
function Assert-Closed([string]$Root) {
 $rootFull=[IO.Path]::GetFullPath($Root).TrimEnd('\')+'\'
 foreach($p in @(Get-Process)) {
  try { $exe=$p.Path } catch { $exe=$null }
  if (!$exe -and $p.ProcessName -like 'VictoryAtSeaPacific*') { throw 'Cannot determine running game path; close the game first.' }
  if ($exe -and $exe.StartsWith($rootFull,[StringComparison]::OrdinalIgnoreCase)) { throw "Process running from game folder: $($p.ProcessName)" }
 }
}
function Assert-Game([string]$Root) {
 $a=Safe-Path $Root 'VictoryAtSeaPacific_Data/Managed/Assembly-CSharp.dll'
 if (!(Test-Path -LiteralPath $a) -or (Get-Item -LiteralPath $a).Length -ne 3223040 -or (Hash $a) -ne $script:GameHash) { throw 'Unsupported game: Assembly-CSharp.dll SHA-256/size mismatch (requires VASP 1.14.2).' }
 $b=Safe-Path $Root 'BepInEx/core/BepInEx.dll'
 if (!(Test-Path -LiteralPath $b)) { throw 'Install BepInEx 5.4.23.5 x64 separately first; no loader is shipped.' }
 if ([Diagnostics.FileVersionInfo]::GetVersionInfo($b).FileVersion -ne '5.4.23.5') { throw 'BepInEx 5.4.23.5 required' }
 foreach($f in @('BepInEx/core/0Harmony.dll','BepInEx/core/BepInEx.Preloader.dll','winhttp.dll','doorstop_config.ini')) { if (!(Test-Path -LiteralPath (Safe-Path $Root $f))) { throw "Incomplete existing BepInEx installation: $f" } }
}
function Write-Json($Object,[string]$Path) { [IO.File]::WriteAllText($Path,($Object|ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($false)) }
function Read-Json([string]$Path) { [IO.File]::ReadAllText($Path)|ConvertFrom-Json }
function Check-Entries($Entries) {
 $seen=@{}; foreach($f in @($Entries)) { if ($f.path -cnotin $script:OwnedPaths -or $seen.ContainsKey($f.path) -or $f.sha256 -notmatch '^[a-f0-9]{64}$') { throw 'Invalid owned manifest entry' }; $seen[$f.path]=$true }
}
function Assert-NoDuplicatePlugins([string]$Root) {
 $plugins=Safe-Path $Root 'BepInEx/plugins'
 if(!(Test-Path -LiteralPath $plugins)) { return }
 $expected=@{}
 foreach($relative in $script:OwnedPaths) { $expected[[IO.Path]::GetFileNameWithoutExtension($relative)]=Safe-Path $Root $relative }
 $entries=@(Get-ChildItem -LiteralPath $plugins -Force -Recurse)
 if(@($entries | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { throw 'Plugin scan refused a junction/symlink; inspect it before updating.' }
 foreach($file in @($entries | Where-Object { !$_.PSIsContainer -and $_.Extension -eq '.dll' })) {
  try { $name=[Reflection.AssemblyName]::GetAssemblyName($file.FullName).Name }
  catch { if($_.Exception -is [BadImageFormatException] -or $_.Exception.InnerException -is [BadImageFormatException]) { continue }; throw }
  if($expected.ContainsKey($name) -and $file.FullName -ne $expected[$name]) { throw "Duplicate/legacy plugin outside its managed path; preserved: $($file.FullName)" }
 }
}
function Remove-PrivateTemp([string]$Path,[string]$Prefix) {
 $absolute=[IO.Path]::GetFullPath($Path).TrimEnd('\')
 $tempRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
 if([IO.Path]::GetDirectoryName($absolute) -ne $tempRoot -or [IO.Path]::GetFileName($absolute) -notmatch ('^'+[regex]::Escape($Prefix)+'[0-9a-fA-F-]{32,36}$')) { throw "Refused temporary cleanup outside owned directory: $absolute" }
 if(Test-Path -LiteralPath $absolute) {
  if((Get-Item -LiteralPath $absolute -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Temporary cleanup refused a reparse point' }
  if(@(Get-ChildItem -LiteralPath $absolute -Force -Recurse | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { throw 'Temporary cleanup refused a nested reparse point' }
  Remove-Item -LiteralPath $absolute -Recurse -Force
 }
}
