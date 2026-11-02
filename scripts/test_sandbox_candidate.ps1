# Runs inside Windows Sandbox only, with Windows PowerShell 5.1 and no network.
$ErrorActionPreference = 'Stop'
if ($env:USERNAME -ne 'WDAGUtilityAccount' -or -not (Test-Path -LiteralPath 'C:\SnipseraInput\package-results.json')) { throw 'This entry point is for the generated Windows Sandbox kit only.' }
$run = 'C:\SnipseraTest'
$export = 'C:\SnipseraResults'
if (Test-Path -LiteralPath $run) { throw 'Use a fresh Sandbox session.' }
$null = New-Item -ItemType Directory -Path $run
$checks = [Collections.Generic.List[string]]::new()
$result = [ordered]@{status='failed'; platform=[Environment]::OSVersion.VersionString; checks=@(); limits=@('No microphone or host audio access; no recording, scrolling or MSIX acceptance claim')}
try {
    Copy-Item -Path 'C:\SnipseraInput\*' -Destination $run
    $vcRuntime = Join-Path $run 'vc_redist.x64.exe'
    $vcHash = (Get-Content -LiteralPath (Join-Path $run 'vc-runtime.sha256') -Raw).Trim()
    if ((Get-FileHash -LiteralPath $vcRuntime -Algorithm SHA256).Hash -ne $vcHash) { throw 'VC runtime hash mismatch.' }
    $prerequisite = Start-Process -FilePath $vcRuntime -ArgumentList '/install','/quiet','/norestart' -WindowStyle Hidden -Wait -PassThru
    if ($prerequisite.ExitCode -notin @(0,3010)) { throw "VC runtime installation failed: $($prerequisite.ExitCode)" }
    $checks.Add('Host-verified Microsoft VC runtime installed inside the disposable guest')
    $receipt = Get-Content -LiteralPath (Join-Path $run 'package-results.json') -Raw | ConvertFrom-Json
    $result['source_commit'] = $receipt.source_commit
    $result['version'] = $receipt.version
    $installerName = 'Snipsera-' + $receipt.version + '-setup-win-x64.exe'
    $portableName = 'Snipsera-' + $receipt.version + '-win-x64.zip'
    foreach ($name in @($installerName,$portableName)) {
        $expected = @($receipt.artifacts | Where-Object { $_.name -eq $name })
        if ($expected.Count -ne 1 -or (Get-FileHash -LiteralPath (Join-Path $run $name) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected[0].sha256.ToLowerInvariant()) { throw "Input hash mismatch: $name" }
    }
    $checks.Add('Input installer and portable archive match the reviewed receipt')
    $portable = Join-Path $run 'portable'
    Expand-Archive -LiteralPath (Join-Path $run $portableName) -DestinationPath $portable
    $payload = Join-Path $portable ('Snipsera-' + $receipt.version + '-win-x64')
    if (-not (Test-Path -LiteralPath (Join-Path $payload 'Snipsera.exe'))) { throw 'Unexpected portable archive layout.' }
    $installed = Join-Path $run 'installed'
    $installer = Join-Path $run $installerName
    $arguments = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS',('/DIR="' + $installed + '"'))
    for ($pass=1; $pass -le 2; $pass++) {
        $process = Start-Process -FilePath $installer -ArgumentList ($arguments + ('/LOG="' + (Join-Path $run ('install-' + $pass + '.log')) + '"')) -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode -ne 0) { throw "Installer pass $pass failed: $($process.ExitCode)" }
        $checks.Add($(if ($pass -eq 1) {'Per-user EXE installation on clean Windows'} else {'Same-version reinstall'}))
    }
    foreach ($file in Get-ChildItem -LiteralPath $payload -Recurse -File) {
        $relative = $file.FullName.Substring($payload.Length).TrimStart('\')
        $target = Join-Path $installed $relative
        if (-not (Test-Path -LiteralPath $target) -or (Get-FileHash -LiteralPath $target).Hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash) { throw "Installed file mismatch: $relative" }
    }
    $checks.Add('Every installed file matches the portable payload')
    $fixture = Start-Process -FilePath (Join-Path $installed 'Snipsera.exe') -ArgumentList '--fixture' -WindowStyle Hidden -PassThru
    try {
        $null = $fixture.WaitForInputIdle(15000)
        for ($attempt=0; $attempt -lt 50; $attempt++) {
            $fixture.Refresh()
            if ($fixture.MainWindowTitle -eq 'Snipsera Capture Test') { break }
            Start-Sleep -Milliseconds 100
        }
        if ($fixture.HasExited -or $fixture.MainWindowTitle -ne 'Snipsera Capture Test') { throw 'Installed self-contained fixture did not start.' }
        $null = $fixture.CloseMainWindow()
        if (-not $fixture.WaitForExit(10000)) { throw 'Fixture did not close normally.' }
    } finally { if (-not $fixture.HasExited) { Stop-Process -Id $fixture.Id } }
    $checks.Add('Installed WPF fixture launches without a preinstalled .NET runtime')
    $uninstaller = Start-Process -FilePath (Join-Path $installed 'unins000.exe') -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -WindowStyle Hidden -Wait -PassThru
    if ($uninstaller.ExitCode -ne 0 -or (Test-Path -LiteralPath (Join-Path $installed 'Snipsera.exe'))) { throw 'Uninstall failed.' }
    $checks.Add('Uninstall removes the application')
    $result.status = 'passed'
} catch { $result['error'] = $_.Exception.Message }
finally {
    $result.checks = $checks.ToArray()
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $export 'results.json') -Encoding utf8
    Get-ChildItem -LiteralPath $run -Filter '*.log' | Copy-Item -Destination $export
}
if ($result.status -ne 'passed') { throw $result.error }
Write-Output 'Sandbox installer checks passed. Results are in the kit results folder.'
