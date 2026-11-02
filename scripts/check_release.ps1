param([string]$Output = (Join-Path $PSScriptRoot '../verification/runs/release-checks'))
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dotnet = Join-Path $root '.tools/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_HOME = Join-Path $root '.tools/dotnet-home'
$env:NUGET_PACKAGES = Join-Path $root '.tools/nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
if (Test-Path -LiteralPath $Output) { throw 'Choose a fresh check-output folder.' }
$project = Join-Path $root 'tests/Framelet.Checks/Framelet.Checks.csproj'
& $dotnet restore $project --configfile (Join-Path $root 'NuGet.Config') --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $dotnet build $project -c Release --no-restore --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$checks = Join-Path $root 'tests/Framelet.Checks/bin/Release/net10.0-windows/Framelet.Checks.dll'
foreach ($mode in @('identity-check','backend-check','overlay-style-check','recording-check')) {
    & $dotnet $checks ('--' + $mode) (Join-Path $Output $mode)
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
python -m unittest discover -s (Join-Path $root 'tests') -p 'test_*.py'
exit $LASTEXITCODE
