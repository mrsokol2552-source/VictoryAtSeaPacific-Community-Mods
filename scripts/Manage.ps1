[CmdletBinding()]
param([Parameter(Mandatory=$true)][ValidateSet('Install','Remove','Rollback')][string]$Action,[Parameter(Mandatory=$true)][string]$GamePath,[string]$PackagePath='', [switch]$AllowExperimental,[switch]$MigrateBaseline)
. "$PSScriptRoot\Common.ps1"
$root=[IO.Path]::GetFullPath($GamePath).TrimEnd('\')
Assert-Closed $root
Assert-Game $root
Assert-NoDuplicatePlugins $root
$statePath=Safe-Path $root '.vasp-community-owned.json'
$journalPath=Safe-Path $root '.vasp-community-journal.json'
$lockPath=Safe-Path $root '.vasp-community.lock'
$lock=[IO.File]::Open($lockPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
try {
 if (Test-Path $journalPath) { throw 'Unfinished transaction. Preserve .vasp-community-journal.json and backup; inspect/recover before retry.' }
 $old=$null; if(Test-Path $statePath) { $old=Read-Json $statePath; if($old.schema -ne 1) { throw 'Unknown ownership schema' }; Check-Entries $old.files }
 $current=@(); if($old) { $current=@($old.files) }
 foreach($f in $current) { $p=Safe-Path $root $f.path; if(!(Test-Path $p) -or (Hash $p) -ne $f.sha256) { throw "Owned file modified/missing; preserved: $($f.path)" } }
 $next=@(); $sources=@{}; $restore=$null
 if($Action -eq 'Install') {
  if(!$PackagePath) { $PackagePath=Split-Path $PSScriptRoot -Parent }
  $pkg=[IO.Path]::GetFullPath($PackagePath); $m=Read-Json (Safe-Path $pkg 'manifest.json')
  if($m.schema -ne 1 -or $m.assemblySha256 -ne $script:GameHash) { throw 'Unsupported package' }
  if(!$m.releaseReady -and !$AllowExperimental) { throw 'Experimental package: explicit -AllowExperimental required' }
  Check-Entries $m.files; if(@($m.files).Count -ne 4) { throw 'Expected exactly four plugin files' }
  $next=@($m.files)
  foreach($f in $next) { $s=Safe-Path $pkg ('payload/'+$f.path); if((Hash $s) -ne $f.sha256) { throw 'Payload checksum mismatch' }; $sources[$f.path]=$s }
  if(!$old) {
   for($i=0;$i -lt 4;$i++) { $p=Safe-Path $root $script:OwnedPaths[$i]; if(Test-Path $p) { if(!$MigrateBaseline -or $i -ge 3 -or (Hash $p) -ne $script:Baseline[$i]) { throw "Foreign file conflict: $p" }; $current+= [pscustomobject]@{path=$script:OwnedPaths[$i];sha256=$script:Baseline[$i]} } }
   # Legacy ownership records remain untouched; loader/config entries are NEVER adopted.
   if($MigrateBaseline -and $current.Count -ne 3) { throw 'Migration requires all three exact baseline DLLs' }
  }
 } elseif($Action -eq 'Rollback') {
  if(!$old -or !$old.backup) { throw 'No previous transaction available' }
  $restore=Safe-Path $root $old.backup; $snapshot=Read-Json (Safe-Path $restore 'snapshot.json')
  Check-Entries $snapshot.files; $next=@($snapshot.files)
  foreach($f in $next) { $s=Safe-Path $restore ('files/'+$f.path); if((Hash $s) -ne $f.sha256) { throw 'Backup checksum mismatch' }; $sources[$f.path]=$s }
 } elseif(!$old) { throw 'No owned installation; nothing removed' }
 # No overwrite even for an identical file unless owned or explicitly baseline-adopted.
 foreach($f in $next) { $p=Safe-Path $root $f.path; if((Test-Path $p) -and !@($current | Where-Object path -eq $f.path).Count) { throw "Unowned destination: $p" } }
 $backupRel='.vasp-community-backups/'+[guid]::NewGuid().ToString('N'); $backup=Safe-Path $root $backupRel
 New-Item -ItemType Directory $backup | Out-Null
 $snap=[pscustomobject]@{files=@($current);state=$old}
 Write-Json $snap (Join-Path $backup 'snapshot.json')
 foreach($f in $current) { $b=Safe-Path $backup ('files/'+$f.path); New-Item -ItemType Directory -Force (Split-Path $b) | Out-Null; Copy-Item -LiteralPath (Safe-Path $root $f.path) -Destination $b }
 $journal=[pscustomobject]@{schema=1;backup=$backupRel;before=@($current);after=@($next)}
 Write-Json $journal $journalPath
 $changed=@()
 try {
  foreach($f in $current) { Assert-Closed $root; $p=Safe-Path $root $f.path; if((Hash $p) -ne $f.sha256) { throw 'Concurrent file change' }; [IO.File]::Delete($p); $changed+=$f.path }
  foreach($f in $next) { Assert-Closed $root; $p=Safe-Path $root $f.path; New-Item -ItemType Directory -Force (Split-Path $p) | Out-Null; $stream=[IO.File]::Open($p,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None); $changed+=$f.path; try { $bytes=[IO.File]::ReadAllBytes($sources[$f.path]); $stream.Write($bytes,0,$bytes.Length) } finally { $stream.Dispose() }; if((Hash $p) -ne $f.sha256) { throw 'Write verification failed' } }
  Assert-Closed $root
  if($Action -eq 'Rollback') { if($snapshot.state) { Write-Json $snapshot.state $statePath } else { [IO.File]::Delete($statePath) } }
  elseif($Action -eq 'Remove') { [IO.File]::Delete($statePath) }
  else { Write-Json ([pscustomobject]@{schema=1;files=@($next);backup=$backupRel;releaseReady=$false}) $statePath }
  [IO.File]::Delete($journalPath)
  Write-Output "$Action complete. Backup retained: $backupRel; config, loader and foreign files untouched."
 } catch {
  $failure=$_
  try {
   Assert-Closed $root
   foreach($path in @($changed | Select-Object -Unique)) { $p=Safe-Path $root $path; if(Test-Path $p) { $expected=@($next | Where-Object path -eq $path); if(!$expected.Count -or (Hash $p) -ne $expected[0].sha256) { throw "Changed file preserved, manual recovery needed: $path" }; [IO.File]::Delete($p) } }
   foreach($f in $current) { $p=Safe-Path $root $f.path; if(!(Test-Path $p)) { Copy-Item -LiteralPath (Safe-Path $backup ('files/'+$f.path)) -Destination $p } }
   if($old) { Write-Json $old $statePath } elseif(Test-Path $statePath) { [IO.File]::Delete($statePath) }
   [IO.File]::Delete($journalPath)
  } catch { Write-Warning "Recovery stopped safely; retain journal and backup: $_" }
  throw $failure
 }
} finally { $lock.Dispose(); [IO.File]::Delete($lockPath) }
