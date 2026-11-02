param([switch]$Release)
$ErrorActionPreference = 'Stop'
$frameletRoot = $PSScriptRoot
$frameletDotnet = Join-Path $frameletRoot '.tools/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $frameletDotnet)) { $frameletDotnet = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_HOME = Join-Path $frameletRoot '.tools/dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:NUGET_PACKAGES = Join-Path $frameletRoot '.tools/nuget'
$frameletConfiguration = if ($Release) { 'Release' } else { 'Debug' }
& $frameletDotnet restore (Join-Path $frameletRoot 'src/Framelet/Framelet.csproj') --configfile (Join-Path $frameletRoot 'NuGet.Config') --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
python (Join-Path $frameletRoot 'scripts/prepare_recording_dependencies.py')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $frameletDotnet build (Join-Path $frameletRoot 'src/Framelet/Framelet.csproj') -c $frameletConfiguration --no-restore --nologo
exit $LASTEXITCODE
