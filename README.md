# Cursor Beacon

Lost your mouse pointer across multiple monitors? **Shake the mouse** or **double-tap Ctrl** and a glowing beacon locks onto the cursor while the rest of your screens dim.

- Tiny: one ~80 KB exe, no runtime to install (uses the .NET Framework built into Windows), ~5 MB RAM idle
- Works across monitors with different scaling (per-monitor DPI aware)
- Tray menu: toggle shake / Ctrl / dimming, 5 colours, shake sensitivity, start with Windows
- Updates itself automatically from this repo's GitHub Releases

## Install

1. Download `CursorBeacon.exe` from the [latest release](../../releases/latest).
2. Run it. It installs to `%LOCALAPPDATA%\Programs\CursorBeacon`, starts with Windows, and lives in the system tray.

The exe is unsigned, so Windows SmartScreen may warn the first time: click **More info → Run anyway**.

To remove it: tray icon → **Uninstall**.

## How updates work

Every push to `main` triggers [the release workflow](.github/workflows/release.yml): it builds the exe, runs a self-test and publishes release `v1.0.<run number>`. Installed copies check the latest release 30 seconds after starting and then every 3 hours, swap in the new exe and restart. The repo must be **public** for this to work.

## Build locally

```powershell
./build.ps1
```

Local builds have version `0.0.0` and no repo, so auto-update is off for them. Useful flags:

| Command | What it does |
| --- | --- |
| `CursorBeacon.exe --portable` | Run without installing |
| `CursorBeacon.exe --preview out.png` | Render a preview sheet of the animation |
| `CursorBeacon.exe --selftest log.txt` | Install hooks, show the beacon once, write OK/FAIL |
