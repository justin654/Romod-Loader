# Root PowerShell scripts

All scripts assume the working directory is the **workspace root** (the folder that contains `build.ps1` and `romestead_modding.sln`). They dot-source [`workspace-paths.ps1`](../workspace-paths.ps1) when they need resolved game/server install paths.

| Script | Role |
|--------|------|
| [`build.ps1`](../build.ps1) | Full build: StartupHook, Installer, ClientCore, Romod tool, sample mods, pack `romods/*` → `artifacts/`. Flags: `-Configuration`, `-ServerOnly`. |
| [`dev-install.ps1`](../dev-install.ps1) | Fast loop: build + deploy to configured client and/or server. Flags: `-Target`, `-Configuration`, `-Launch`. |
| [`install.ps1`](../install.ps1) | One-shot **client** patch: backup entry DLL, inject hook, copy loader DLLs, deploy `artifacts/mods` + `mods.json`. |
| [`install-server.ps1`](../install-server.ps1) | Same for **dedicated server** (uses `-ServerOnly` build path). |
| [`uninstall.ps1`](../uninstall.ps1) | Restore client entry DLL + `deps.json` from backups. |
| [`uninstall-server.ps1`](../uninstall-server.ps1) | Restore server entry DLL + `deps.json`. |
| [`Launch-RomesteadModded.ps1`](../Launch-RomesteadModded.ps1) | Convenience: runs `Romestead.exe` from the configured game root. |
| [`diag-launch.ps1`](../diag-launch.ps1) | Launches the game with `COREHOST_TRACE` and logs to `artifacts/diag/`. |

There are no legacy alternate installers in this folder; if you add new automation, extend `build.ps1` / `dev-install.ps1` or document a new script here.
