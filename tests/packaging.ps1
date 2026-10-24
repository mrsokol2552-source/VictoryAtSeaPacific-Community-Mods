[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$GamePath)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
. "$repo\scripts\Common.ps1"
Assert-Game $GamePath
$temp=Join-Path ([IO.Path]::GetTempPath()) ('vasp-fixture-'+[guid]::NewGuid())
New-Item -ItemType Directory $temp | Out-Null
$script:passed=0
function Expect-Fail([scriptblock]$Body,[string]$Name) { $failed=$false; try { & $Body } catch { $failed=$true }; if(!$failed) {throw "Expected refusal: $Name"}; $script:passed++; Write-Output "PASS refusal: $Name" }
function Check([bool]$Value,[string]$Name) {if(!$Value){throw "FAIL $Name"};$script:passed++;Write-Output "PASS $Name"}
function Make-Package([string]$Root,[string]$Marker) {
 $entries=@(); foreach($p in $script:OwnedPaths) {$dst=Safe-Path $Root ('payload/'+$p);New-Item -ItemType Directory -Force (Split-Path $dst)|Out-Null;[IO.File]::WriteAllText($dst,$Marker+$p);$entries+=[pscustomobject]@{path=$p;sha256=(Hash $dst)}}
 Write-Json ([pscustomobject]@{schema=1;assemblySha256=$script:GameHash;releaseReady=$false;files=$entries}) (Join-Path $Root 'manifest.json')
}
try {
 $fixture=Join-Path $temp 'game';New-Item -ItemType Directory $fixture|Out-Null
 # Copy only local references needed for real compatibility guard, never distribute fixtures.
 foreach($p in @('VictoryAtSeaPacific_Data/Managed/Assembly-CSharp.dll','BepInEx/core/BepInEx.dll','BepInEx/core/BepInEx.Preloader.dll','BepInEx/core/0Harmony.dll','winhttp.dll','doorstop_config.ini')) {$dst=Safe-Path $fixture $p;New-Item -ItemType Directory -Force (Split-Path $dst)|Out-Null;Copy-Item -LiteralPath (Safe-Path $GamePath $p) $dst}
 Assert-Closed $fixture
 $keep=Safe-Path $fixture 'BepInEx/config/user.cfg';New-Item -ItemType Directory -Force (Split-Path $keep)|Out-Null;[IO.File]::WriteAllText($keep,'preserve config')
 $foreign=Safe-Path $fixture 'BepInEx/plugins/foreign.dll';New-Item -ItemType Directory -Force (Split-Path $foreign)|Out-Null;[IO.File]::WriteAllText($foreign,'foreign')
 $loaderHash=Hash (Join-Path $fixture 'winhttp.dll')
 $p1=Join-Path $temp 'p1';$p2=Join-Path $temp 'p2';Make-Package $p1 'old';Make-Package $p2 'new'
 $manage=Join-Path $repo 'scripts/Manage.ps1'
 Expect-Fail { & $manage -Action Install -GamePath $fixture -PackagePath $p1 } 'experimental opt-in'
 & $manage -Action Install -GamePath $fixture -PackagePath $p1 -AllowExperimental
 $owned=Read-Json (Join-Path $fixture '.vasp-community-owned.json');Check (@($owned.files).Count -eq 4) 'clean install'
 & $manage -Action Install -GamePath $fixture -PackagePath $p2 -AllowExperimental
 Check ((Hash (Safe-Path $fixture $script:OwnedPaths[0])) -eq (Hash (Safe-Path $p2 ('payload/'+$script:OwnedPaths[0])))) 'update'
 & $manage -Action Rollback -GamePath $fixture
 Check ((Hash (Safe-Path $fixture $script:OwnedPaths[0])) -eq (Hash (Safe-Path $p1 ('payload/'+$script:OwnedPaths[0])))) 'rollback update'
 $ownedFile=Safe-Path $fixture $script:OwnedPaths[0];[IO.File]::AppendAllText($ownedFile,'modified')
 Expect-Fail { & $manage -Action Remove -GamePath $fixture } 'modified owned DLL preserved'
 Copy-Item (Safe-Path $p1 ('payload/'+$script:OwnedPaths[0])) $ownedFile -Force
 & $manage -Action Remove -GamePath $fixture
 Check (!(Test-Path $ownedFile)) 'remove owned only'
 Check (([IO.File]::ReadAllText($keep) -eq 'preserve config') -and ([IO.File]::ReadAllText($foreign) -eq 'foreign') -and (Hash (Join-Path $fixture 'winhttp.dll')) -eq $loaderHash) 'loader config foreign preservation'
 [IO.File]::WriteAllText($ownedFile,'foreign conflict')
 Expect-Fail { & $manage -Action Install -GamePath $fixture -PackagePath $p1 -AllowExperimental } 'foreign destination'
 [IO.File]::Delete($ownedFile)
 $duplicate=Join-Path $fixture 'BepInEx/plugins/renamed-legacy.dll'
 Copy-Item -LiteralPath (Join-Path $repo 'artifacts/plugins/VaspHotfix.dll') -Destination $duplicate
 Expect-Fail { & $manage -Action Install -GamePath $fixture -PackagePath $p1 -AllowExperimental } 'renamed duplicate plugin assembly'
 Check ((Hash $duplicate) -eq (Hash (Join-Path $repo 'artifacts/plugins/VaspHotfix.dll'))) 'duplicate preserved on refusal'
 [IO.File]::Delete($duplicate)
 Expect-Fail { Safe-Path $fixture '../escape' } 'path traversal'
 $a=Safe-Path $fixture 'VictoryAtSeaPacific_Data/Managed/Assembly-CSharp.dll';[IO.File]::AppendAllText($a,'bad')
 Expect-Fail { & $manage -Action Install -GamePath $fixture -PackagePath $p1 -AllowExperimental } 'unsupported game'
 Write-Output "Packaging fixture tests: $script:passed passed. No installed-game writes."
} finally {Remove-PrivateTemp $temp 'vasp-fixture-'}
