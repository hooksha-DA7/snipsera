$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$tools = Join-Path $root '.tools/packaging'
$null = New-Item -ItemType Directory -Path $tools -Force
$lock = Get-Content -LiteralPath (Join-Path $root 'packaging/toolchain.json') -Raw | ConvertFrom-Json
foreach ($item in $lock) {
    if ([IO.Path]::GetFileName($item.name) -ne $item.name) { throw 'Invalid tool filename' }
    $target = Join-Path $tools $item.name
    if (-not (Test-Path -LiteralPath $target)) { Invoke-WebRequest -Uri $item.url -OutFile $target }
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() -ne $item.sha256) { throw "Tool archive hash differs: $($item.name)" }
}
$sdk = Join-Path $tools 'windows-sdk-buildtools'
$makeAppx = Join-Path $sdk 'bin/10.0.28000.0/x64/makeappx.exe'
if (-not (Test-Path -LiteralPath $makeAppx)) {
    if (Test-Path -LiteralPath $sdk) { throw 'Incomplete SDK cache; inspect it before replacing it.' }
    $archive = $lock | Where-Object { $_.name -like '*.nupkg' }
    [IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $tools $archive.name),$sdk)
}
$inno = Join-Path $tools 'inno'
$compiler = Join-Path $inno 'ISCC.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $setup = $lock | Where-Object { $_.name -like 'innosetup-*.exe' }
    $installer = Join-Path $tools $setup.name
    $signature = Get-AuthenticodeSignature -LiteralPath $installer
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notlike '*Pyrsys B.V.*') { throw 'Unexpected Inno Setup publisher' }
    $process = Start-Process -FilePath $installer -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/CURRENTUSER','/NOICONS',('/DIR="' + $inno + '"'),'/TASKS=""') -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "Inno Setup installation failed: $($process.ExitCode)" }
}
foreach ($tool in @(@($compiler,'Pyrsys B.V.'),@($makeAppx,'Microsoft Corporation'))) {
    $signature = Get-AuthenticodeSignature -LiteralPath $tool[0]
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notlike ('*' + $tool[1] + '*')) { throw "Unexpected tool signature: $($tool[0])" }
}
Write-Output 'Pinned packaging tools verified.'
