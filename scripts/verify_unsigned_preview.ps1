param([Parameter(Mandatory)][string]$File,[Parameter(Mandatory)][string]$Version)
$ErrorActionPreference = 'Stop'
if ([IO.Path]::GetExtension($File) -ne '.exe') { throw 'Only an EXE installer can use the unsigned path.' }
$metadata = (Get-Item -LiteralPath $File).VersionInfo
if ($metadata.ProductName.Trim() -ne 'Snipsera' -or $metadata.CompanyName.Trim() -ne 'hooksha-DA7' -or $metadata.ProductVersion.Trim() -ne $Version) { throw 'Unsigned installer identity/version differs.' }
if ((Get-AuthenticodeSignature -LiteralPath $File).Status -ne 'NotSigned') { throw 'Expected a genuinely unsigned file; invalid or unexpected signatures are refused.' }
Write-Output ('Verified unsigned installer identity: ' + [IO.Path]::GetFileName($File))
