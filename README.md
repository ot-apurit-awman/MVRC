# MVRC

## What this mod is

MVRC (My Winter Car VR) is a GPL-3.0 VR mod authored by ot-apurit-awman. It
adds VR head tracking and controller interaction to My Winter Car. The mod is
still under development, so some features are incomplete and bugs are expected.

## Requirements

- My Winter Car for PC, installed legally through Steam
- MSC Mod Loader
- SteamVR or another compatible OpenVR runtime
- A compatible VR headset and VR controllers
- A release package of this mod

The primary player-facing controls and troubleshooting notes remain in
[README.txt](README.txt).

## Installation for players

1. Install [MSC Mod Loader](https://github.com/piotrulos/MSCModLoader).
2. Start My Winter Car once after installing the loader, then close the game.
3. Extract the MVRC ZIP file.
4. Copy `MVRC.dll` into the game's `Mods` folder.
5. Copy `MVRC_OpenVR.dll` into `mywintercar_Data\Plugins`.
6. Copy `openvr_api.dll` into the main game folder, next to `mywintercar.exe`.
7. Start SteamVR.
8. Launch My Winter Car.
9. Press F10 in game to toggle VR mode, or use the autostart setting.
10. If your view is off center, press R to recenter.

The release workflow provides `MVRC_Full.zip` with all runtime files,
`MVRC_Mod.zip` for the managed mod when OpenVR is already installed, and
`MVRC_OpenVR_Runtime.zip` for the native/OpenVR runtime files.

## Building

The only build tool required is the [.NET SDK](https://dotnet.microsoft.com/download/dotnet),
which includes the `dotnet` CLI. Visual Studio and the .NET Framework 3.5
Developer Pack are not required. The project remains a .NET Framework 3.5
project; its reference assemblies are restored from NuGet.

The public source does not contain proprietary game assemblies. Set
`MVRC_GAME_REFS` to a local directory containing these DLLs:

```
Assembly-CSharp.dll
Assembly-CSharp-firstpass.dll
UnityEngine.dll
UnityEngine.UI.dll
PlayMaker.dll
MSCLoader.dll
cInput.dll
0Harmony.dll
```

For example, in PowerShell:

```powershell
$env:MVRC_GAME_REFS = 'C:\path\to\mywintercar_Data\Managed'
```

You may instead copy `Local.props.example` to the untracked `Local.props`
and set `MVRC_GAME_REFS` there. For deployment, also set
`MVRC_MODS_DIR` to an existing game `Mods` folder:

```powershell
$env:MVRC_MODS_DIR = 'C:\path\to\My Winter Car\Mods'
```

The required references are the following private DLLs, copied from the user's
own game and loader installation:

```
Assembly-CSharp.dll
Assembly-CSharp-firstpass.dll
UnityEngine.dll
UnityEngine.UI.dll
PlayMaker.dll
MSCLoader.dll
cInput.dll
0Harmony.dll
```

Run **Build MVRC (Release)** or **Build MVRC (Debug)** from the VS Code
Command Palette. These are the canonical VS Code tasks. The equivalent SDK
script commands are:

```powershell
& .\.vscode\build-mvrc.ps1 -Configuration Release
& .\.vscode\build-mvrc.ps1 -Configuration Debug -Deploy
```

The `-Deploy` form copies `MVRC.dll` and the tracked `openvr_api.dll` to
`MVRC_MODS_DIR`; without it, nothing is copied. A successful build writes
the managed mod to `bin\Release\MVRC.dll` or `bin\Debug\MVRC.dll`.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for the development constraints and
pull request requirements.

## License

MVRC is licensed under the GPL-3.0-or-later license. This repository includes
no game files or proprietary game assemblies. Users must supply their own legal
copy of My Winter Car and the assemblies required to build or run the mod.

## Credit and attribution

The original publication is on [Nexus Mods](https://www.nexusmods.com/mywintercar/mods/1913).
Copyright notices and the [NOTICE](NOTICE) file must be preserved in copies
and derivatives, as required by GPL-3.0.
