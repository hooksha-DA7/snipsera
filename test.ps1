param([string]$Output = '')
$ErrorActionPreference = 'Stop'
$frameletRoot = $PSScriptRoot
$frameletDotnet = Join-Path $frameletRoot '.tools/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $frameletDotnet)) { $frameletDotnet = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_HOME = Join-Path $frameletRoot '.tools/dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:NUGET_PACKAGES = Join-Path $frameletRoot '.tools/nuget'
if (-not $Output) { $Output = Join-Path $frameletRoot ('verification/runs/' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')) }
$frameletTestProject = Join-Path $frameletRoot 'tests/Framelet.Checks/Framelet.Checks.csproj'
& $frameletDotnet restore $frameletTestProject --configfile (Join-Path $frameletRoot 'NuGet.Config') --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $frameletDotnet build $frameletTestProject -c Release --no-restore --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $frameletDotnet (Join-Path $frameletRoot 'tests/Framelet.Checks/bin/Release/net10.0-windows/Framelet.Checks.dll') $Output
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $frameletDotnet (Join-Path $frameletRoot 'tests/Framelet.Checks/bin/Release/net10.0-windows/Framelet.Checks.dll') --capture-stress (Join-Path $Output 'capture-lifecycle')
exit $LASTEXITCODE
