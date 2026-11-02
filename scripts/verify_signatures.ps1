param([string[]]$Files,[string]$Directory,[Parameter(Mandatory)][string]$ExpectedPublisher)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ExpectedPublisher)) { throw 'Set the exact certificate subject after SignPath approval.' }
if ($Directory) { $Files = @((Join-Path $Directory 'Snipsera.exe'),(Join-Path $Directory 'Snipsera.dll')) }
if (-not $Files) { throw 'Provide files or the signed application directory.' }
foreach ($file in $Files) {
    $signature = Get-AuthenticodeSignature -LiteralPath $file
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -ne $ExpectedPublisher -or $null -eq $signature.TimeStamperCertificate) {
        throw "Signature, timestamp or publisher check failed: $([IO.Path]::GetFileName($file))"
    }
    Write-Output ('Verified signature: ' + [IO.Path]::GetFileName($file))
}
