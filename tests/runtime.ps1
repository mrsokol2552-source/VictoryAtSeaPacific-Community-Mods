[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$GamePath,[Parameter(Mandatory=$true)][string]$ClientPath,[Parameter(Mandatory=$true)][string]$ReportPath,[string]$InputSave='', [ValidateSet('main','restart','industry-restart')][string]$Phase='main')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
. "$repo\scripts\Common.ps1"
Assert-Game $GamePath
$game=[IO.Path]::GetFullPath($GamePath).TrimEnd('\')
$client=[IO.Path]::GetFullPath($ClientPath).TrimEnd('\')
if($client -eq $game -or $client.StartsWith($game+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Use an independent private client outside the installed game.' }
Assert-Closed $client
if((Hash (Join-Path $client 'VictoryAtSeaPacific.exe')) -ne (Hash (Join-Path $game 'VictoryAtSeaPacific.exe'))) { throw 'Private client executable mismatch' }
$plugins=Join-Path $client 'BepInEx/plugins'
$allow=@('VaspHotfix.dll','VaspScoutPatrol.dll','VaspInterfaceTools.dll','VaspCommunityMods.dll','VaspRuntimeValidation.dll')
$existing=@(Get-ChildItem -LiteralPath $plugins -Recurse -Filter '*.dll' -File)
if(@($existing | Where-Object {$_.Name -notin $allow}).Count -or @($existing | Group-Object Name | Where-Object Count -gt 1).Count) { throw 'Private client contains another test/helper or duplicate plugin. Use a dedicated clean private client.' }
$report=[IO.Path]::GetFullPath($ReportPath)
if($report -eq $game -or $report.StartsWith($game+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Validation reports must be outside the installed game.' }
New-Item -ItemType Directory -Force $report | Out-Null
[IO.File]::WriteAllText((Safe-Path $report 'runtime.txt'),'PENDING: this validation process has not completed.')
foreach($name in @('modifications-top.png','modifications-bottom.png','industry-max.png','industry-confirm.png')) {
 $prior=Safe-Path $report $name
 if(Test-Path -LiteralPath $prior){Move-Item -LiteralPath $prior -Destination (Safe-Path $report ($name+'.previous-'+[guid]::NewGuid().ToString('N')))}
}
if($Phase -ne 'restart') {
 if(!$InputSave) { throw 'An explicit save is required; it will only be read and copied.' }
 Copy-Item -LiteralPath $InputSave -Destination (Join-Path $report 'input.data')
}
$products=Join-Path $repo 'artifacts/plugins'
$records=@()
foreach($name in $allow[0..3]) {
 $source=Join-Path $products $name
 $prior=@($existing | Where-Object Name -eq $name)
 $target=if($prior.Count){$prior[0].FullName}else{Join-Path $plugins $name}
 Copy-Item -LiteralPath $source -Destination $target -Force
 $records+=[pscustomobject]@{name=$name;sha256=(Hash $source)}
}
Write-Json ([pscustomobject]@{phase=$Phase;gameAssemblySha256=$script:GameHash;plugins=$records;inputSha256=if(Test-Path (Join-Path $report 'input.data')){Hash (Join-Path $report 'input.data')}else{$null}}) (Join-Path $report 'tested-build.json')
$managed=Join-Path $game 'VictoryAtSeaPacific_Data/Managed';$core=Join-Path $game 'BepInEx/core'
$refs=@('mscorlib','System','System.Core','UnityEngine','UnityEngine.UI','Assembly-CSharp','Assembly-CSharp-firstpass') | ForEach-Object {Join-Path $managed ($_.ToString()+'.dll')}
$refs+=@((Join-Path $core 'BepInEx.dll'),(Join-Path $core '0Harmony.dll'))
$refs+=@($allow[0..3] | ForEach-Object {Join-Path $products $_})
$probe=Join-Path $plugins 'VaspRuntimeValidation.dll'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$compilerArgs=@('/nologo','/noconfig','/target:library','/nostdlib+','/optimize+',"/out:$probe")+@($refs | ForEach-Object {'/reference:'+$_})+@((Join-Path $PSScriptRoot 'RuntimeValidation.cs'),(Join-Path $PSScriptRoot 'IndustryValidation.cs'))
& $compiler @compilerArgs
if($LASTEXITCODE -ne 0){throw 'Runtime helper compilation failed'}
$log=Join-Path $report 'unity.log'
$runtimeArgs=@('-nosound','-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-logFile',('"'+$log+'"'),'-vasp-community-validation','-validationRoot',('"'+$report+'"'),'-validationPhase',$Phase)
$process=Start-Process -FilePath (Join-Path $client 'VictoryAtSeaPacific.exe') -WorkingDirectory $client -ArgumentList $runtimeArgs -WindowStyle Hidden -PassThru
[pscustomobject]@{processId=$process.Id;report=$report;phase=$Phase} | ConvertTo-Json -Compress
