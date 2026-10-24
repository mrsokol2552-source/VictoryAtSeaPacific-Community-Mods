[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$GamePath,[Parameter(Mandatory=$true)][string]$ClientPath,[Parameter(Mandatory=$true)][string]$InputSave,[Parameter(Mandatory=$true)][string]$ReportPath)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
. "$repo/scripts/Common.ps1"
Assert-Game $GamePath
$game=[IO.Path]::GetFullPath($GamePath).TrimEnd('\');$client=[IO.Path]::GetFullPath($ClientPath).TrimEnd('\');$report=[IO.Path]::GetFullPath($ReportPath)
if($client -eq $game -or $client.StartsWith($game+'\',[StringComparison]::OrdinalIgnoreCase) -or $report -eq $game -or $report.StartsWith($game+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Use private client and reports outside the installed game'}
Assert-Closed $client
if((Hash (Join-Path $client 'VictoryAtSeaPacific.exe')) -ne (Hash (Join-Path $game 'VictoryAtSeaPacific.exe'))){throw 'Private EXE mismatch'}
$plugins=Safe-Path $client 'BepInEx/plugins'
$names=@('VaspHotfix.dll','VaspScoutPatrol.dll','VaspInterfaceTools.dll','VaspCommunityMods.dll','VaspRuntimeValidation.dll')
$files=@(Get-ChildItem -LiteralPath $plugins -Recurse -Filter '*.dll' -File)
if(@($files | Where-Object Name -notin $names).Count -or @($files | Group-Object Name | Where-Object Count -gt 1).Count){throw 'Unknown or duplicate active private plugin'}
New-Item -ItemType Directory -Force $report | Out-Null
[IO.File]::WriteAllText((Safe-Path $report 'runtime.txt'),'PENDING: this vanilla validation process has not completed.')
Copy-Item -LiteralPath $InputSave -Destination (Join-Path $report 'input.data')
$held=@();$process=$null;$probe=Safe-Path $client 'BepInEx/plugins/VaspRuntimeValidation.dll';$probeHash=$null
try {
 foreach($file in $files){
  $relative=$file.FullName.Substring($client.Length+1);$verified=Safe-Path $client $relative;$parked=Safe-Path $client ($relative+'.vanilla-disabled')
  if(Test-Path -LiteralPath $parked){throw 'Previous private test file remains; inspect before retrying'}
  Move-Item -LiteralPath $verified -Destination $parked
  $held+= [pscustomobject]@{original=$verified;parked=$parked}
 }
 $managed=Join-Path $game 'VictoryAtSeaPacific_Data/Managed';$core=Join-Path $game 'BepInEx/core'
 $refs=@('mscorlib','System','System.Core','UnityEngine','Assembly-CSharp','Assembly-CSharp-firstpass') | ForEach-Object {Join-Path $managed ($_.ToString()+'.dll')}
 $refs+=@((Join-Path $core 'BepInEx.dll'),(Join-Path $core '0Harmony.dll'))
 $compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
 $compilerArgs=@('/nologo','/noconfig','/target:library','/nostdlib+','/optimize+',"/out:$probe")+@($refs|ForEach-Object{'/reference:'+$_})+@(Join-Path $PSScriptRoot 'VanillaIndustryValidation.cs')
 & $compiler @compilerArgs
 if(Test-Path -LiteralPath $probe){$probeHash=Hash $probe}
 if($LASTEXITCODE -ne 0){throw 'Vanilla helper compilation failed'}
 $runtimeArgs=@('-nosound','-batchmode','-logFile',('"'+(Join-Path $report 'unity.log')+'"'),'-vanillaIndustryReport',('"'+$report+'"'))
 $process=Start-Process -FilePath (Join-Path $client 'VictoryAtSeaPacific.exe') -WorkingDirectory $client -ArgumentList $runtimeArgs -WindowStyle Hidden -PassThru
 Write-Output "Private vanilla validation PID=$($process.Id)"
 $deadline=[DateTime]::UtcNow.AddSeconds(180)
 while(!$process.WaitForExit(1000)){if([DateTime]::UtcNow -gt $deadline){Stop-Process -Id $process.Id;throw 'Private vanilla validation timeout'}}
 $result=[IO.File]::ReadAllText((Join-Path $report 'runtime.txt'))
 if($result -notmatch 'ALL PASS vanilla-industry'){throw $result}
 Write-Output $result
} finally {
 Assert-Closed $client
 if($probeHash -and (Test-Path -LiteralPath $probe)){
  if((Hash $probe) -ne $probeHash){throw 'Changed private helper preserved for inspection'}
  Move-Item -LiteralPath $probe -Destination ($probe+'.vanilla-result-'+[guid]::NewGuid().ToString('N')+'.disabled')
 }
 foreach($file in $held){if(Test-Path -LiteralPath $file.original){throw 'Private restoration destination occupied'};Move-Item -LiteralPath $file.parked -Destination $file.original}
}
