# Windows acceptance

The hosted release workflow tests installation, reinstall, an increasing-version fixture upgrade and uninstall on Windows Server 2025. That is useful evidence, but it does not replace Windows 11 capture and recording acceptance.

## Clean Windows 11 installer check

On a Windows 11 Pro/Enterprise host with Windows Sandbox already enabled, prepare a private kit from a verified candidate:

```powershell
./scripts/prepare_sandbox_test.ps1 -Candidate .tools/final-candidate -Output .tools/windows-sandbox-test -VCRuntime .tools/vc_redist.x64.exe
```

Review the generated `Snipsera-test.wsb`, then open it. The generator does not enable Windows features, request administrator access, restart Windows or launch the Sandbox. Enabling Windows Sandbox may require an administrator and a Windows restart; complete that separately.

Download the [Microsoft Visual C++ x64 runtime](https://aka.ms/vs/17/release/vc_redist.x64.exe) from Microsoft first. The kit generator requires its valid Microsoft signature and records its hash. The runtime is installed only inside the disposable guest.

The kit maps only its input folder read-only and an empty results folder writable. Networking, host clipboard, microphone, camera and printer redirection are disabled. It copies reviewed packages into the disposable guest, checks their hashes, installs and reinstalls the EXE, compares every installed file, launches the synthetic WPF fixture, then uninstalls. No host account folders or capture history are mapped. The results folder receives a JSON receipt and installer logs. Do not describe a prepared kit as a passed test.

## Interactive acceptance before a public release

Use synthetic content in a fresh Windows 11 test profile. Check area/window/display capture, repeat capture, simultaneous previews, same-window Pin/Unpin, stacked pins, hide/restore and scrolling in Chrome. Open the editor and verify annotations, save/copy and editable projects. Record a moving test scene as H.264 MP4 and GIF, pause/resume, cursor and click markers, and inspect duration, resolution and frame rate in the encoded outputs.

Test microphone and computer audio only with the tester's permission. The isolated Sandbox kit deliberately cannot prove audio acceptance. Check Do not disturb's manual Windows shortcut; automatic control remains unavailable. Test the final Store-signed MSIX separately after certification, including installation, capture permissions and history preservation. Record the exact artifact hashes, OS/build, observations and limits.

Reference: [Microsoft's Windows Sandbox configuration](https://learn.microsoft.com/en-us/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-configure-using-wsb-file).
