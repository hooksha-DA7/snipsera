param([string]$Version = '0.4.1')
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Use a numeric major.minor.patch version.' }
$frameletRoot = $PSScriptRoot
$frameletProject = [xml](Get-Content -LiteralPath (Join-Path $frameletRoot 'src/Framelet/Framelet.csproj') -Raw)
if ($frameletProject.Project.PropertyGroup.Version -ne $Version) { throw 'Package version must match the application project version.' }
& (Join-Path $frameletRoot 'build.ps1') -Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$frameletDist = Join-Path $frameletRoot 'dist'
$frameletPackage = Join-Path $frameletDist "Framelet-$Version-win-x64"
$frameletSource = Join-Path $frameletDist "Framelet-$Version-source"
if ((Test-Path -LiteralPath $frameletPackage) -or (Test-Path -LiteralPath $frameletSource)) {
    throw 'This version is already staged. Use a fresh version or inspect the existing output before replacing it.'
}
$null = New-Item -ItemType Directory -Path $frameletPackage,$frameletSource -Force
$frameletBinary = Join-Path $frameletRoot 'src/Framelet/bin/Release/net10.0-windows'
foreach ($file in @('Framelet.exe','Framelet.dll','Framelet.deps.json','Framelet.runtimeconfig.json')) {
    Copy-Item -LiteralPath (Join-Path $frameletBinary $file) -Destination $frameletPackage
}
Copy-Item -LiteralPath (Join-Path $frameletRoot 'README.md') -Destination $frameletPackage
Copy-Item -LiteralPath (Join-Path $frameletRoot 'CHANGELOG.md') -Destination $frameletPackage
Copy-Item -LiteralPath (Join-Path $frameletRoot 'docs') -Destination $frameletPackage -Recurse
foreach ($file in @('README.md','CHANGELOG.md','Directory.Build.props','NuGet.Config','build.ps1','test.ps1','publish.ps1','.gitignore')) {
    Copy-Item -LiteralPath (Join-Path $frameletRoot $file) -Destination $frameletSource
}
Copy-Item -LiteralPath (Join-Path $frameletRoot 'docs') -Destination $frameletSource -Recurse
$null = New-Item -ItemType Directory -Path (Join-Path $frameletSource 'verification') -Force
Copy-Item -LiteralPath (Join-Path $frameletRoot 'verification/verify_packages.py') -Destination (Join-Path $frameletSource 'verification')
foreach ($tree in @('src','tests')) {
    $treeRoot = Join-Path $frameletRoot $tree
    foreach ($file in Get-ChildItem -LiteralPath $treeRoot -Recurse -File) {
        $relative = [IO.Path]::GetRelativePath($frameletRoot, $file.FullName)
        if ($relative -match '[\\/](bin|obj)[\\/]') { continue }
        if ($file.Extension -notin @('.cs','.csproj','.manifest')) { continue }
        $destination = Join-Path $frameletSource $relative
        $null = New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force
        Copy-Item -LiteralPath $file.FullName -Destination $destination
    }
}
foreach ($folder in @($frameletPackage,$frameletSource)) {
    $manifest = @(Get-ChildItem -LiteralPath $folder -File -Recurse | Sort-Object FullName | ForEach-Object {
        [ordered]@{ path = [IO.Path]::GetRelativePath($folder,$_.FullName).Replace('\','/'); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(); bytes = $_.Length }
    })
    $manifest | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $folder 'manifest-sha256.json') -Encoding utf8
    Compress-Archive -LiteralPath $folder -DestinationPath ($folder + '.zip') -CompressionLevel Optimal
}
Get-ChildItem -LiteralPath $frameletDist -Filter '*.zip' | Get-FileHash -Algorithm SHA256 | Select-Object Path,Hash
