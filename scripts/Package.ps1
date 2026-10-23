[CmdletBinding()]
param([string]$PluginPath='', [string]$OutputPath='', [string]$Version='0.2.0-preview.1')
. "$PSScriptRoot\Common.ps1"
$repo=Split-Path $PSScriptRoot -Parent
if(!$PluginPath) { $PluginPath=Join-Path $repo 'artifacts/plugins' }
if(!$OutputPath) { $OutputPath=Join-Path $repo 'artifacts' }
if($Version -notmatch '^[0-9A-Za-z.\-]+$') { throw 'Unsafe version' }
$stage=Join-Path ([IO.Path]::GetTempPath()) ('vasp-package-'+[guid]::NewGuid())
New-Item -ItemType Directory $stage | Out-Null
try {
 $files=@()
 foreach($p in $script:OwnedPaths) {
  $name=Split-Path $p -Leaf; $src=Join-Path $PluginPath $name
  # Only explicitly named build products, never recursive output copying.
  if(!(Test-Path -LiteralPath $src -PathType Leaf)) { throw "Build product missing: $name" }
  [Reflection.AssemblyName]::GetAssemblyName($src) | Out-Null
  $dst=Safe-Path $stage ('payload/'+$p); New-Item -ItemType Directory -Force (Split-Path $dst) | Out-Null
  Copy-Item -LiteralPath $src -Destination $dst
  $files+=[pscustomobject]@{path=$p;sha256=(Hash $dst)}
 }
 Write-Json ([pscustomobject]@{schema=1;name='VASPCommunityMods';version=$Version;releaseReady=$false;assemblySha256=$script:GameHash;files=$files}) (Join-Path $stage 'manifest.json')
 New-Item -ItemType Directory (Join-Path $stage 'scripts') | Out-Null
 foreach($s in @('Common.ps1','Manage.ps1')) { Copy-Item (Join-Path $PSScriptRoot $s) (Join-Path $stage 'scripts') }
 foreach($d in @('INSTALL.md','README.md','CHANGELOG.md','LICENSE','THIRD_PARTY_NOTICES.md')) { if(!(Test-Path (Join-Path $repo $d))) { throw "Required packaging documentation absent: $d" }; Copy-Item (Join-Path $repo $d) $stage }
 $allow=@('manifest.json','scripts/Common.ps1','scripts/Manage.ps1','INSTALL.md','README.md','CHANGELOG.md','LICENSE','THIRD_PARTY_NOTICES.md')+@($script:OwnedPaths | ForEach-Object { 'payload/'+$_ })
 $actual=@(Get-ChildItem $stage -File -Recurse | ForEach-Object { $_.FullName.Substring($stage.Length+1).Replace('\','/') })
 if(@(Compare-Object ($allow|Sort-Object) ($actual|Sort-Object)).Count) { throw 'Archive allowlist mismatch' }
 New-Item -ItemType Directory -Force $OutputPath | Out-Null
 $zip=Join-Path $OutputPath "VASPCommunityMods-$Version.zip"
 if(Test-Path $zip) { throw 'Output archive already exists; choose another output directory/version.' }
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 [IO.Compression.ZipFile]::CreateFromDirectory($stage,$zip)
 $archive=[IO.Compression.ZipFile]::OpenRead($zip)
 try { $entries=@($archive.Entries | ForEach-Object {$_.FullName.Replace('\','/')}); if(@(Compare-Object ($allow|Sort-Object) ($entries|Sort-Object)).Count) { throw 'Final ZIP entries mismatch' } } finally { $archive.Dispose() }
 [IO.File]::WriteAllText($zip+'.sha256',(Hash $zip)+'  '+[IO.Path]::GetFileName($zip)+"`n",[Text.Encoding]::ASCII)
 Write-Output "$zip SHA256=$(Hash $zip); plugin-only archive, releaseReady=false"
} finally { Remove-Item -LiteralPath $stage -Recurse -Force }
