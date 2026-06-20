\---

layout: single # splash, single

title: Moja hra

permalink: "/games/indicatrix/"

author\_profile: true

toc: true

classes: wide

\---



\## Game: Indicatrix (Working title)

The game Indicatrix (working title) is in the development stage. The programming language is C# and the game engine is Unity. I use AI tools to create the program code, but I wrote the game architecture completely myself. I don't have a specific goal for the game yet, for now it is a game that appears to be a train and vehicle transport simulator. The exact specification of the game will develop over time. I program it myself, I revise and check the entire code on my own. I am not working on the graphics side of the game yet. I will probably use some commercial purchased graphic materials from publicly available sources.



\### 📷 Development Screenshot



<div style="

&#x20; display: grid;

&#x20; grid-template-columns: repeat(2, 1fr);

&#x20; gap: 15px;

">



&#x20; <!-- First Row -->

&#x20; <img src="/games/img/Indicatrix01.png" width="100%" alt="Indicatrix 01">

&#x20; <img src="/games/img/Indicatrix02.png" width="100%" alt="Indicatrix 02">



&#x20; <!-- Second Row -->

&#x20; <img src="/games/img/Indicatrix03.png" width="100%" alt="Indicatrix 03">

&#x20; <img src="/games/img/Indicatrix04.png" width="100%" alt="Indicatrix 04">



</div>



\### 📅 Development Timeline



🎮 \*\*Development Log: Indicatrix | Tycoon Game\*\*



🗓️ 16 June 2026



➤ 🏠 Created the \*\*Main Menu\*\*

➤ 🎮 Created the \*\*Game Menu\*\*

➤ 💾 Improved the \*\*Load Game\*\* and \*\*Save Game\*\* functions

➤ 📑 Created a UI window for the \*\*Annual Financial Closing Report\*\*



\---



🗓️ 14 June 2026



➤ 📊 Created a business policy system and pricing relationship management

➤ ⛰️ Created a procedural terrain generator

➤ 💬 Added labels displayed above successfully completed business transactions



\---



🗓️ 06 June 2026



➤ 🚆 Added additional trains and vehicles beyond the basic types

➤ ⌨️ Fixed an issue with the \*\*Esc\*\* key after closing UI windows

➤ ⏰ Added in-game time and a construction cost pricing system

➤ 🏭 Added a toggle for the \*\*Factories UI\*\* and included all factory types beyond the basic ones

➤ 👥 Added a toggle for the \*\*Staff Management UI\*\*, including employee recruitment and a complete factory employee overview

➤ 🛡️ Added handling for ongoing error states during gameplay interactions

➤ 🏷️ Added percentage-based construction progress labels for factories and price labels for purchase and sale transactions



\---



🗓️ 03 June 2026



➤ 🐞 Fixed UI error-state handling for error messages

➤ 🔘 Fixed enabled/disabled states for \*\*Depot Rail\*\* and \*\*Depot Road\*\* buttons

➤ 🚉 Fixed station name display for \*\*Station Rail\*\* and \*\*Station Road\*\*



\---



🗓️ 25 May 2026



➤ 🎯 Reworked the positioning system to automatically center all UI windows

➤ 🎥 Reworked the camera system, including zoom functionality and movement constraints

➤ 🔍 Created dedicated camera zoom control buttons



\---



🗓️ 23 May 2026



➤ 💰 Implemented a business transaction system for vehicles



\---



🗓️ 22 May 2026



➤ 🖥️ Expanded the \*\*Factory Status UI\*\*

➤ 🏭 Prepared \*\*Factory\*\* and \*\*Train\*\* structures for the upcoming \*\*Station\*\* implementation

➤ 🚆💰 Implemented a business transaction system for trains



\---



🗓️ 20 May 2026



➤ 🛣️ Expanded the \*\*Road UI\*\* and \*\*Road Depot UI\*\* for the vehicle system

➤ 🏭 Added a hidden \*\*Factory UI\*\* for factory generation

➤ 🔄 Added full \*\*360° factory rotation\*\* support



\---



🗓️ 17 May 2026



➤ 🪟 Fixed the UI window system, including dragging windows via the title bar

➤ 🚉 Expanded the \*\*Rail Depot UI\*\* with additional interface elements



\---



🗓️ 12 May 2026



➤ 🚧 Fixed train jams caused by accidental track tile deletion

➤ 🏢 Created the \*\*Depot UI\*\* and connected it to the game logic

➤ ⛰️ Tested inclined railway tracks



\---



🗓️ 08 May 2026



➤ 🚆 Reworked the railway system from center-tile logic to a waypoint-based system, including railway switches

➤ ↩️ Improved track curves from rigid 90° turns to smooth 45° angles

➤ ⛰️ Improved terrain elevation transitions with more natural slope angles

➤ 🔀 Expanded intersection direction functionality to support intermediate 45° directions



\---



🗓️ 05 May 2026



➤ 🚂 Reworked the train movement system from \*\*Follow-Leader\*\* logic to a distance-based path system

➤ 🏠 Fixed trains returning to depots only after completing their routes instead of during travel

➤ 🔄 Fixed train turning behavior at stations without breaking movement logic



\---



🗓️ 02 May 2026



➤ 🚆 Added core functionality for trains, locomotives, and wagons

➤ ✨ Fixed locomotive flickering and wagon duplication issues



\---



🗓️ 20 April 2026



➤ ⚡ Optimized performance using parallel processing (\*\*TPL\*\*)

➤ 🗺️ Refactored the entire codebase

➤ 🖥️ Fixed game flow interruptions when switching maps

➤ 🚉 Optimized handling of multiple trains and railway line interruptions



\---



🗓️ 17 April 2026



➤ 🚧 Added a construction system for railway lines, tracks, stations, and depots

➤ 🗺️ Implemented shortest-route railway pathfinding using the \*\*A\*\*\* algorithm

➤ 🛠️ Added the ability to modify railway lines during gameplay

➤ 🚆 Completed railway train simulation, including train creation, deletion, departures, stops, and depot returns



\---



🗓️ 24 February 2026



➤ ⚡ Terrain Optimization III – implemented a new terrain collapse algorithm

➤ 🗺️ Adjusted terrain region functionality to 20×20 sectors

➤ 🖥️ Added additional UI elements

➤ 💾 Added a tile system, including Save/Load functionality



\---



🗓️ 04 February 2026



➤ ⚡ Terrain Optimization II

➤ 🗺️ Adjusted terrain region functionality to 20×20 sectors

➤ 🖌️ Added map edge borders

➤ 🔄 Merged \*\*Level Up\*\* and \*\*Level Down\*\* into a single function



\---



🗓️ 17 January 2026



➤ ⚡ Terrain Optimization I

➤ 🗺️ Added terrain region functionality



\---



🗓️ 19 September 2025



➤ 🧩 Implemented the terrain decomposition system



\---



🗓️ 18 July 2025



➤ 🔲 Added face snapping



\---



🗓️ 15 July 2025



➤ 🔺 Added vertex snapping



\---



🗓️ 17 March 2025



➤ 🗺️ Created base terrain coordinates aligned with the map

➤ 🔼🔽 Added terrain height adjustment for individual vertices





