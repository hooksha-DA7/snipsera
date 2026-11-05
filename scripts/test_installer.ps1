param([Parameter(Mandatory)][string]$Candidate,[Parameter(Mandatory)][string]$Output,[switch]$ExerciseUpgrade,[switch]$ExerciseStartupCleanup)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$run = [IO.Path]::GetFullPath($Output)
$installation = Join-Path $run 'installed'
if (-not $run.StartsWith($root + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Installer tests must use a fresh folder inside this checkout.' }
if (Test-Path -LiteralPath $run) { throw 'Choose a fresh installer-test folder.' }
$registration = 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/{AE5C6F25-7A71-4A81-AFC2-7442D18AE306}_is1'
if (Test-Path -LiteralPath $registration) { throw 'Preserve the existing Snipsera installation; use an isolated test profile.' }
$startupKey = 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Run'
$startupName = 'SnipseraStartup'
if ($ExerciseStartupCleanup -and (Get-ItemProperty -LiteralPath $startupKey -Name $startupName -ErrorAction SilentlyContinue)) { throw 'Preserve the existing startup entry; use an isolated test profile.' }
$Candidate = [IO.Path]::GetFullPath($Candidate)
$receipt = Get-Content -LiteralPath (Join-Path $Candidate 'package-results.json') -Raw | ConvertFrom-Json
$payload = Join-Path $Candidate ('Snipsera-' + $receipt.version + '-win-x64')
$installer = Join-Path $Candidate ('Snipsera-' + $receipt.version + '-setup-win-x64.exe')
$null = New-Item -ItemType Directory -Path $run,(Join-Path $run 'history')
$marker = Join-Path $run 'history/keep.txt'
[IO.File]::WriteAllText($marker,'Synthetic saved-data marker. Preserve through install, upgrade and uninstall.')
$markerHash = (Get-FileHash -LiteralPath $marker).Hash
$checks = [Collections.Generic.List[string]]::new()
function Install-Candidate([string]$Path,[string]$LogName) {
    $process = Start-Process -FilePath $Path -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS',('/DIR="' + $installation + '"'),('/LOG="' + (Join-Path $run $LogName) + '"')) -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "Installer exit $($process.ExitCode)" }
}
Install-Candidate $installer 'install.log'
$app = Join-Path $installation 'Snipsera.exe'
if (-not (Test-Path -LiteralPath $app) -or -not (Test-Path -LiteralPath $registration)) { throw 'Expected installation was not created.' }
$checks.Add('Per-user installation and uninstall registration')
if ($ExerciseStartupCleanup) {
    if (Get-ItemProperty -LiteralPath $startupKey -Name $startupName -ErrorAction SilentlyContinue) { throw 'Installation opted into Windows startup.' }
    $checks.Add('Installation leaves Windows startup off')
}
foreach ($file in Get-ChildItem -LiteralPath $payload -Recurse -File) {
    $installed = Join-Path $installation ([IO.Path]::GetRelativePath($payload,$file.FullName))
    if (-not (Test-Path -LiteralPath $installed) -or (Get-FileHash -LiteralPath $installed).Hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash) { throw "Installed payload differs: $($file.Name)" }
}
$checks.Add('Installed payload matches every reviewed file')
$fixture = Start-Process -FilePath $app -ArgumentList '--fixture' -PassThru -WindowStyle Hidden
try {
    $null = $fixture.WaitForInputIdle(10000)
    for ($attempt=0; $attempt -lt 30; $attempt++) { $fixture.Refresh(); if ($fixture.MainWindowTitle -eq 'Snipsera Capture Test') { break }; Start-Sleep -Milliseconds 100 }
    if ($fixture.HasExited -or $fixture.MainWindowTitle -ne 'Snipsera Capture Test') { throw 'The installed synthetic fixture did not launch.' }
    $null = $fixture.CloseMainWindow()
    if (-not $fixture.WaitForExit(10000)) { throw 'Synthetic fixture did not close normally.' }
} finally { if (-not $fixture.HasExited) { Stop-Process -Id $fixture.Id } }
$checks.Add('Installed self-contained WPF fixture launches and closes')
Install-Candidate $installer 'reinstall.log'
$checks.Add('Same-version reinstall')
if ($ExerciseUpgrade) {
    $currentVersion = [Version](Get-Item -LiteralPath $app).VersionInfo.FileVersion
    $nextVersion = '{0}.{1}.{2}' -f $currentVersion.Major,$currentVersion.Minor,($currentVersion.Build + 1)
    $nextLabel = $nextVersion + '-upgrade-fixture'
    $upgradePayload = Join-Path $run 'upgrade-payload'
    Copy-Item -LiteralPath $payload -Destination $upgradePayload -Recurse
    $dotnet = Join-Path $root '.tools/dotnet/dotnet.exe'
    if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
    $env:DOTNET_CLI_HOME = Join-Path $root '.tools/dotnet-home'
    $env:NUGET_PACKAGES = Join-Path $root '.tools/nuget'
    & $dotnet publish (Join-Path $root 'src/Framelet/Framelet.csproj') -c Release -r win-x64 --self-contained true ('-p:Version=' + $nextVersion) ('-p:InformationalVersion=' + $nextLabel) -p:DebugType=None -p:DebugSymbols=false --output $upgradePayload --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Upgrade fixture build failed.' }
    & (Join-Path $root '.tools/packaging/inno/ISCC.exe') ('/DAppDir=' + $upgradePayload) ('/DReleaseVersion=' + $nextLabel) ('/DOutputDir=' + $run) (Join-Path $root 'packaging/Snipsera.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Upgrade fixture installer failed.' }
    Install-Candidate (Join-Path $run ('Snipsera-' + $nextLabel + '-setup-win-x64.exe')) 'upgrade.log'
    $newVersion = [Version](Get-Item -LiteralPath $app).VersionInfo.FileVersion
    if ($newVersion -le $currentVersion -or (Get-ItemProperty -LiteralPath $registration).DisplayVersion -ne $nextLabel) { throw 'The installer did not upgrade the executable and registration.' }
    $checks.Add('Increasing-version installer fixture upgrades executable and registration')
}
$uninstaller = Join-Path $installation 'unins000.exe'
if ($ExerciseStartupCleanup) {
    $null = New-Item -Path $startupKey -Force
    $null = New-ItemProperty -LiteralPath $startupKey -Name $startupName -PropertyType String -Value ('"' + $app + '" --startup') -Force
}
$process = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + (Join-Path $run 'uninstall.log') + '"')) -WindowStyle Hidden -PassThru -Wait
if ($process.ExitCode -ne 0 -or (Test-Path -LiteralPath $app) -or (Test-Path -LiteralPath $registration)) { throw 'Uninstall did not remove the test app and registration.' }
if ($ExerciseStartupCleanup) {
    if (Get-ItemProperty -LiteralPath $startupKey -Name $startupName -ErrorAction SilentlyContinue) { throw 'Uninstall left the owned startup entry behind.' }
    $checks.Add('Uninstall removes the exact owned startup command')
    Install-Candidate $installer 'startup-reinstall.log'
    $otherCopy = '"C:\Synthetic-other-copy\Snipsera.exe" --startup'
    $null = New-ItemProperty -LiteralPath $startupKey -Name $startupName -PropertyType String -Value $otherCopy -Force
    try {
        $process = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -WindowStyle Hidden -PassThru -Wait
        if ($process.ExitCode -ne 0 -or (Get-ItemPropertyValue -LiteralPath $startupKey -Name $startupName) -ne $otherCopy) { throw 'Uninstall changed another copy startup command.' }
        $checks.Add('Uninstall preserves another copy startup command')
    } finally { Remove-ItemProperty -LiteralPath $startupKey -Name $startupName -ErrorAction SilentlyContinue }
}
if ((Get-FileHash -LiteralPath $marker).Hash -ne $markerHash) { throw 'Saved-data marker changed.' }
$checks.Add('Uninstall and synthetic saved-data preservation')
[ordered]@{status='passed'; checks=$checks.ToArray(); platform=[Environment]::OSVersion.VersionString; limits=@('Synthetic fixture tests; not a replacement for interactive capture/recording acceptance','An upgrade fixture uses the same source with an increased version; it is never a published release')} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $run 'results.json') -Encoding utf8
Write-Output 'Installer acceptance checks passed.'
