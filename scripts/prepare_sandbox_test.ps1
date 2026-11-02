param([Parameter(Mandatory)][string]$Candidate,[Parameter(Mandatory)][string]$Output,[Parameter(Mandatory)][string]$VCRuntime)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$destination = [IO.Path]::GetFullPath($Output)
if (-not $destination.StartsWith((Join-Path $root '.tools') + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Use a fresh private .tools folder for the Sandbox kit.' }
if (Test-Path -LiteralPath $destination) { throw 'Choose a fresh kit folder.' }
$Candidate = [IO.Path]::GetFullPath($Candidate)
python (Join-Path $PSScriptRoot 'verify_candidate.py') --candidate $Candidate
if ($LASTEXITCODE -ne 0) { throw 'Candidate verification failed.' }
$VCRuntime = [IO.Path]::GetFullPath($VCRuntime)
$signature = Get-AuthenticodeSignature -LiteralPath $VCRuntime
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '^CN=Microsoft Corporation,') { throw 'Use the signed Microsoft Visual C++ x64 runtime from its official download.' }
$receipt = Get-Content -LiteralPath (Join-Path $Candidate 'package-results.json') -Raw | ConvertFrom-Json
$inputDirectory = Join-Path $destination 'input'
$outputDirectory = Join-Path $destination 'results'
$null = New-Item -ItemType Directory -Path $inputDirectory,$outputDirectory
foreach ($name in @('package-results.json',('Snipsera-' + $receipt.version + '-setup-win-x64.exe'),('Snipsera-' + $receipt.version + '-win-x64.zip'))) {
    Copy-Item -LiteralPath (Join-Path $Candidate $name) -Destination $inputDirectory
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'test_sandbox_candidate.ps1') -Destination (Join-Path $inputDirectory 'bootstrap.ps1')
Copy-Item -LiteralPath $VCRuntime -Destination (Join-Path $inputDirectory 'vc_redist.x64.exe')
(Get-FileHash -LiteralPath $VCRuntime -Algorithm SHA256).Hash | Set-Content -LiteralPath (Join-Path $inputDirectory 'vc-runtime.sha256') -Encoding ascii
$inputXml = [Security.SecurityElement]::Escape($inputDirectory)
$outputXml = [Security.SecurityElement]::Escape($outputDirectory)
@"
<Configuration>
  <Networking>Disable</Networking>
  <AudioInput>Disable</AudioInput>
  <VideoInput>Disable</VideoInput>
  <PrinterRedirection>Disable</PrinterRedirection>
  <ClipboardRedirection>Disable</ClipboardRedirection>
  <ProtectedClient>Enable</ProtectedClient>
  <MemoryInMB>8192</MemoryInMB>
  <MappedFolders>
    <MappedFolder><HostFolder>$inputXml</HostFolder><SandboxFolder>C:\SnipseraInput</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$outputXml</HostFolder><SandboxFolder>C:\SnipseraResults</SandboxFolder><ReadOnly>false</ReadOnly></MappedFolder>
  </MappedFolders>
  <LogonCommand><Command>powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\SnipseraInput\bootstrap.ps1</Command></LogonCommand>
</Configuration>
"@ | Set-Content -LiteralPath (Join-Path $destination 'Snipsera-test.wsb') -Encoding utf8
[xml]$configuration = Get-Content -LiteralPath (Join-Path $destination 'Snipsera-test.wsb') -Raw
if ($configuration.Configuration.Networking -ne 'Disable' -or $configuration.Configuration.MappedFolders.MappedFolder.Count -ne 2) { throw 'Sandbox configuration verification failed.' }
Write-Output ('Prepared, not launched: ' + (Join-Path $destination 'Snipsera-test.wsb'))
