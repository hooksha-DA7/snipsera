param([string]$Output = '', [string]$StoreIdentity = '', [switch]$DevelopmentMsix, [string]$Inno = '', [string]$MakeAppx = '')
$ErrorActionPreference = 'Stop'
if ($DevelopmentMsix -and $StoreIdentity) { throw 'Choose a Store identity or a development identity, not both.' }
if (-not $StoreIdentity -and -not $DevelopmentMsix) { $StoreIdentity = Join-Path $PSScriptRoot 'packaging/store-identity.json' }
$project = [xml](Get-Content -LiteralPath (Join-Path $PSScriptRoot 'src/Framelet/Framelet.csproj') -Raw)
if (-not $Output) { $Output = Join-Path $PSScriptRoot ('dist/Snipsera-' + $project.Project.PropertyGroup.InformationalVersion + '-candidate-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')) }
$arguments = @((Join-Path $PSScriptRoot 'scripts/package_release.py'), '--output', $Output)
if ($DevelopmentMsix) { $arguments += '--development-msix' } else { $arguments += @('--store-identity',$StoreIdentity) }
if ($Inno) { $arguments += @('--inno',$Inno) }
if ($MakeAppx) { $arguments += @('--makeappx',$MakeAppx) }
& (Join-Path $PSScriptRoot 'build.ps1') -Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
python @arguments
exit $LASTEXITCODE
