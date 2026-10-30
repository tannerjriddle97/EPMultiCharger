Electrical Progressive: Four Bay Charger 1.0.1

FEATURES
A separate craftable block with four visible bays, holding one item each.
Charges the first unfinished bay, then advances to the next automatically.
Completed items stay in their own bays. No manual swapping between charges.
Only one item charges per tick, using the original EP charger routine.
Maximum power request: 200 W total, rather than 200 W per bay.
Native charging speed, item energy costs, voltage limits, and burnout logic
are retained. Low supply slows charging; no power means no charging.
Items marked chargable and EP battery blocks are supported.
Existing charging stations and their recipes are unchanged.

INSTALL
Built for Vintage Story 1.22.7, EP Basics 3.3.1, and EP Core 3.3.0.
Place epmulticharger_1.0.1.zip in VintagestoryData/Mods, leaving it zipped.
Restart the game. For multiplayer, install on both client and server.
Search the handbook for Four Bay Charger.

CRAFT
  Iron plate | Charging Station | Iron plate
  Coil       | Plain glass      | Coil

The ingredient is the normal unburned EP Charging Station, not four of them.
It is a capacity/convenience upgrade, with no increase in total charge rate.

USE
Place on a solid surface and connect EP power underneath, as for the charger.
Right-click an empty bay with an item held to insert one item.
Right-click an occupied bay to retrieve its item. If your inventory is full,
the retrieved item drops beside the charger. Hover for all four charge levels.
Bays are numbered by world-space quadrants: NW, NE, SW, SE (1 through 4).
The lowest numbered unfinished bay has priority. Full/empty/invalid bays skip.
Hoppers may insert valid items into empty bays and extract completed items.
Breaking the block returns its contents through the game's container drops.

VALIDATION / CURRENT LIMIT
Built against the installed game and EP assemblies.
47 automated integration checks pass against real EP charging code, covering:
one-at-a-time charging; total 200 W request; partial/zero power; empty/full and
invalid bays; queue advancement; capacity limits; battery interface;
hopper selection; four-item save/load and stored charge preservation;
one-item slot limits after reload; distinct four-bay item mesh positions;
breaking drops all four stored items once; ordinary charger behavior;
Harmony patch removal. World/network services are test substitutes.
Full in-world placement, clicking, power-network behavior, and visual appearance
have not yet been playtested. This is a build ready for that in-game check.

UNINSTALL
Empty and break the new Four Bay Chargers before removing this addon, because
their block and inventory classes belong to it. Normal EP chargers are intact.

SOURCE / BUILD
The source ZIP contains source, generated assets, build/test scripts and tests.
No game or EP DLLs are redistributed. Build needs the .NET 10 runtime, a .NET
SDK with C# 11 support, and local game + extracted EP Basics/Core DLLs.
Defaults in build.ps1 point to the local installation used for this build.
Override -GamePath, -EPBasics and -EPCore to point to your own folders.
  ./build.ps1 -GamePath 'E:/Games/Vintagestory' -EPBasics <folder> -EPCore <folder>
  ./build.ps1 -Test ...same path arguments...
Package only EPMultiCharger.dll, modinfo.json, and assets/ (plus this README).
Use make-assets.ps1 to regenerate assets from your local EP charger definition.
Charging hooks intentionally reject incompatible EP IL changes rather than
silently shipping a charger with broken routing.

1.0.1 STARTUP FIX
The 1.0.0 startup failure was reproduced by starting two ModSystem instances
in one process, matching single-player client/server startup. Both instances
applied the same transpiler, causing the second application to find zero
remaining slot references. The patched version installs shared hooks once,
counts active instances, and removes hooks only after both sides finish.
Regression checks cover duplicate starts, double disposal and five rejoins.
This fixes the reproduced startup exception. In-game rendering has not been
verified, so the addon remains disabled in the user's Mods folder.
