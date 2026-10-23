# Third-party notices

This distribution contains only the community plugin binaries and their own source, documentation and scripts. It does not include BepInEx, Harmony, Unity, game libraries, decompiled game classes, original assets or saves.

- Victory At Sea Pacific and its assets/code belong to their respective rights holders, including Evil Twin Artworks. A legitimate local game installation is required. References to game APIs do not grant any rights to distribute the game.
- Unity is a trademark of Unity Technologies. Unity libraries are local compile/runtime dependencies, not redistributed.
- BepInEx: https://github.com/BepInEx/BepInEx — LGPL-2.1 licensed upstream project. Install the official Windows x64 5.4.23.5 release separately: https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5 . Follow the upstream archive licenses and instructions. Do not overwrite an unknown existing loader.
- Harmony/HarmonyX: supplied by the selected BepInEx distribution, upstream projects https://github.com/pardeike/Harmony and https://github.com/BepInEx/HarmonyX (MIT). The local 0Harmony.dll is referenced, not included here. No third-party binary licenses are replaced by the license of this repository.

Existing-solutions preflight: the established BepInEx/Harmony stack is reused. No external manager is built: a narrow Unity UI bridge is necessary because the native single-active data-mod loader cannot independently manage C# plugin lifecycles. The game API remains the authority for launches, returns and sea pathfinding.
