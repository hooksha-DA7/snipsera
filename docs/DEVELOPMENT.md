# Building Framelet

Commands below run from the repository root.

## Build and verify

Requires a .NET 10 SDK on Windows x64. Scripts prefer a project-local SDK at `.tools/dotnet/dotnet.exe`, otherwise use `dotnet` from PATH. No third-party NuGet packages are needed. `NuGet.Config` clears external sources.

```powershell
./build.ps1 -Release
./test.ps1
./publish.ps1
```

Close running development copies, including the synthetic fixture, before rebuilding to avoid locking the EXE. `Framelet.exe --fixture` opens a blue/yellow test card without hotkeys or history. The verification harness briefly shows this card and exercises the clipboard, attempting to restore its previous contents afterward. Run tests on an unlocked desktop.

See [on-image controls](ON-IMAGE-CONTROLS.md), [floating image sizing and pin fixes](FLOATING-IMAGE-FIXES.md), [annotation and capture refinement](ANNOTATION-REFINEMENT.md), [Windows 11 design and verification](WINDOWS11-DESIGN.md), [UI redesign verification](UI-REDESIGN-VERIFICATION.md), [visual references](UI-REFERENCES.md), [Phase 2 verification](PHASE2-VERIFICATION.md), [Phase 1 verification](VERIFICATION.md), [architecture](ARCHITECTURE.md) and [changes](../CHANGELOG.md). Release ZIPs also have per-file SHA256 manifests. After publishing, the optional standard-library Python verifier is `python verification/verify_packages.py`; it checks packaged file contents, CRC, paths, hashes, source parity and documentation links.

Microsoft runtime download: https://dotnet.microsoft.com/en-us/download/dotnet/10.0 (choose Windows Desktop Runtime x64).
