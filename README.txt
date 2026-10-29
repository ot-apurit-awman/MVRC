MVRC - README
======================

Mod name: MVRC (My Winter Car VR)
Author: ot-apurit-awman

What this mod does
------------------
MVRC lets you play My Winter Car in VR. You can look around and use VR controllers to interact with many objects. The mod is still under heavy development, so some features are incomplete and bugs are expected.

What you need
-------------
- My Winter Car (PC)
- A PC VR headset that works with SteamVR
- SteamVR installed
- MSC Mod Loader
- The full release package contains MVRC.dll, MVRC_OpenVR.dll, and openvr_api.dll.
  The release workflow also provides a managed-only package and a runtime-only
  package; see the installation steps below.

Building from source
--------------------
The public repository does not include proprietary game assemblies. Set the
MVRC_GAME_REFS environment variable to a local folder containing the following
files copied from your own My Winter Car installation:

	Assembly-CSharp.dll
	Assembly-CSharp-firstpass.dll
	UnityEngine.dll
	UnityEngine.UI.dll
	PlayMaker.dll
	MSCLoader.dll
	cInput.dll
	0Harmony.dll

The expected layout is:

	<MVRC_GAME_REFS>\Assembly-CSharp.dll
	<MVRC_GAME_REFS>\Assembly-CSharp-firstpass.dll
	<MVRC_GAME_REFS>\UnityEngine.dll
	<MVRC_GAME_REFS>\UnityEngine.UI.dll
	<MVRC_GAME_REFS>\PlayMaker.dll
	<MVRC_GAME_REFS>\MSCLoader.dll
	<MVRC_GAME_REFS>\cInput.dll
	<MVRC_GAME_REFS>\0Harmony.dll

For a local build, use the .NET SDK and set MVRC_GAME_REFS to this folder.
For deployment, set MVRC_MODS_DIR to the game's Mods folder. Run the VS Code
task Build MVRC (Release) or Build MVRC (Debug), or run:

	.\.vscode\build-mvrc.ps1 -Configuration Release
	.\.vscode\build-mvrc.ps1 -Configuration Debug -Deploy

The build produces bin\Release\MVRC.dll or bin\Debug\MVRC.dll. Do not
commit these files or download the private references from third parties.

First-time install guide
------------------------
1. Install MSC Mod Loader:
	https://github.com/piotrulos/MSCModLoader
2. Start My Winter Car once after installing the loader, then close the game.
3. Extract this MVRC ZIP file.
4. Copy MVRC.dll into the game's Mods folder.
5. Copy MVRC_OpenVR.dll into:
	mywintercar_Data\Plugins
6. Copy openvr_api.dll into the main game folder, next to mywintercar.exe.
7. Start SteamVR.
8. Launch My Winter Car.
9. Press F10 in game to toggle VR mode (or use the autostart setting).
10. If your view is off center, press R to recenter.

Control setup
-------------
The mod creates and updates SteamVR control files automatically.
You do not need to copy control files by hand.
You can remap button bindings in SteamVR settings.
You can view the default bindings in SteamVR bindings menu or on the mod page.

Default controls
----------------

- Toggle VR mode: F10
- Recenter view (keyboard): R

Where to report bugs
--------------------
Please report bugs to https://www.nexusmods.com/mywintercar/mods/1913?tab=bugs.
Include this info if you can:
- What happened
- Exact steps before it happened
- Your headset and controllers
- output_log.txt from the game folder (very helpful)

Current known issues
--------------------
- The mod is still under heavy development.
- Some interactions can still be inconsistent.
- Some hand models might be a bit off.
- Cant pickup items with both hands at the same time (only 1 item can be held)

CREDITS
-------
MVRC was originally published on [Nexus Mods](https://www.nexusmods.com/mywintercar/mods/1913).
