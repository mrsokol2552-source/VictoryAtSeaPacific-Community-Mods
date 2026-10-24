[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$GamePath, [string]$CompilerPath='', [string]$OutputPath='')
$ErrorActionPreference='Stop'
if (!$OutputPath) { $OutputPath=Join-Path $PSScriptRoot 'artifacts\plugins' }
. "$PSScriptRoot\scripts\Common.ps1"
Assert-Game $GamePath
if (!$CompilerPath) { $CompilerPath=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe' }
if (!(Test-Path -LiteralPath $CompilerPath)) { throw 'C# compiler not found. Supply -CompilerPath (csc.exe, C# 5 or newer).' }
$managed=Join-Path $GamePath 'VictoryAtSeaPacific_Data\Managed'
$core=Join-Path $GamePath 'BepInEx\core'
# Explicit references + /nostdlib prevent accidentally compiling against .NET 4.x.
$refs=@('mscorlib','System','System.Core','System.Xml','UnityEngine','UnityEngine.UI','Assembly-CSharp','Assembly-CSharp-firstpass') | ForEach-Object { Join-Path $managed ($_.ToString()+'.dll') }
$refs+=@( (Join-Path $core 'BepInEx.dll'), (Join-Path $core '0Harmony.dll') )
foreach($r in $refs) { if (!(Test-Path -LiteralPath $r)) { throw "Missing local reference: $r" } }
if ([Reflection.AssemblyName]::GetAssemblyName($refs[0]).Version.Major -ne 2) { throw 'Expected CLR2/.NET3.5 mscorlib reference' }
$stage=Join-Path ([IO.Path]::GetTempPath()) ('vasp-build-'+[guid]::NewGuid())
New-Item -ItemType Directory $stage | Out-Null
try {
    foreach($m in @(@('Hotfix','VaspHotfix'),@('Patrol','VaspScoutPatrol'),@('Interface','VaspInterfaceTools'),@('Bridge','VaspCommunityMods'))) {
        $srcDir = Join-Path $PSScriptRoot "src\$($m[0])"
        $stageDir = Join-Path $stage $m[0]
        New-Item -ItemType Directory $stageDir | Out-Null
        Copy-Item (Join-Path $srcDir '*.cs') $stageDir
        $sources=@(Get-ChildItem -LiteralPath $stageDir -Filter '*.cs' -File | Sort-Object Name | Select-Object -ExpandProperty FullName)
        if (!$sources.Count) { throw "No sources for $($m[0])" }
        $compilerArgs=@('/nologo','/noconfig','/target:library','/optimize+','/unsafe+','/nostdlib+','/warn:4',"/out:$stage\$($m[1]).dll")
        $compilerArgs+=@($refs | ForEach-Object { '/reference:'+$_ })
        if ($m[0] -eq 'Bridge') { $compilerArgs+=@('VaspHotfix','VaspScoutPatrol','VaspInterfaceTools') | ForEach-Object { "/reference:$stage\$_.dll" } }
        if ($m[0] -eq 'Interface') { $compilerArgs+="/resource:$PSScriptRoot\src\Interface\RuOverrides.tsv,VaspInterfaceTools.RuOverrides.tsv" }
        & $CompilerPath @compilerArgs @sources
        if ($LASTEXITCODE -ne 0) { throw "Compiler failed: $($m[0])" }
    }
    if (Test-Path $OutputPath) { if (@(Get-ChildItem $OutputPath -Force | Where-Object { $_.Name -notin @('VaspHotfix.dll','VaspScoutPatrol.dll','VaspInterfaceTools.dll','VaspCommunityMods.dll') }).Count) { throw 'Output folder has unexpected files; use a dedicated empty output folder' } }
    New-Item -ItemType Directory -Force $OutputPath | Out-Null
    Copy-Item "$stage\*.dll" $OutputPath
    Write-Output "Built four plugins only: $OutputPath (all references private/CopyLocal=false)"
} finally { Remove-PrivateTemp $stage 'vasp-build-' }
