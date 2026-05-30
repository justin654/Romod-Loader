# Contributing

This repository is **experimental**: APIs, install steps, and file layouts can change without notice. Issues and small pull requests are welcome; for larger changes, open an issue first so we can align on direction.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- A legal install of **Romestead** (client and/or dedicated server) for full builds — project references point at game assemblies under your install path.

## First-time setup

1. Copy `Workspace.local.props.example` to `Workspace.local.props` and set `RomesteadGameRoot` / `RomesteadServerRoot` to your installs **or** set `ROMESTEAD_GAME_ROOT` and `ROMESTEAD_SERVER_ROOT` before building.

2. Default paths in `Directory.Build.props` match a typical Steam Windows layout; override them if your install differs.

3. The `workspace/ripped/` tree (ripped game content for map tooling) is **not** committed — it is listed in `workspace/.gitignore`. Generate it locally if you use that workflow.

## Build and test

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

CI runs the subset that does **not** require game DLLs (`Romestead.RomodFormat` + its tests). A full loader build still needs the game paths above.

Focused documentation: [docs/README.md](docs/README.md).

## Scope note

**Map Workshop** is a separate WinForms tool (map / XNB workflows). This repository used to contain it under `MapWorkshop/`; it now lives **alongside** the modding workspace (same parent directory), for example:

`Romestead/romestead_modding/` (this repo) and `Romestead/MapWorkshop/` (the app).

Open `MapWorkshop.csproj` from that folder; it is intentionally not part of `romestead_modding.sln`.

## Game assets and redistribution

Do not commit copyrighted game binaries or ripped `Content` from the game. This repo should only contain your own code, docs, and sample mod assets you own or have rights to ship.
