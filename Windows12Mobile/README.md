# Windows 12 Mobile Shell + Assets

This folder contains the source for the Windows 12 Mobile shell that matches the provided concept images (dark home screen with pinned apps, Copilot bar, bottom navigation).

## Contents
- `shell/` — C# WinForms shell project (builds for ARM via GitHub Actions)
- Icons and boot animation frames are generated/packaged by the workflow into the `win12-injector-bundle` artifact

## After GitHub Actions runs
Download the **win12-injector-bundle** artifact. It contains:
- `ffu-porter.exe` (the injector)
- `Windows12MobileShell/` (compiled ARM shell)
- `icons/` and `boot_animation/` folders

## Inject into an FFU
```batch
ffu-porter --base-ffu base.ffu --port-ffu port.ffu --ffu-file result_win12_ffu.ffu --inject Windows12MobileShell --inject-target \Windows\SystemApps\Windows12MobileShell
```

The shell is a starting point matching the concept. Extend it with real icons and a full boot animation sequence as needed.
