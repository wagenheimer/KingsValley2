# Level editor

Open **EDIT** from the title menu or press **F8**. The editor has a desktop workspace around a native 256 × 192 room preview. Room pixels use integer zoom; resizing the editor does not stretch the room.

The compact toolbar and grouped side palettes reserve most of the window for the stage. At 1280 × 800 the default room preview fits at 3×; at 1600 × 1000 it fits at 4×. Selection, brush and fill sit directly below the active tile palette. Zoom controls share the canvas header instead of occupying a separate bottom strip.

The workspace has bordered dock panels and distinct section bars: **Toolbox**, **Tile Palette** and **Paint Mode** on the left; **Inspector**, **Settings** and **Room Map** on the right. The selected tool appears in the Inspector. File/template/history controls are separated in the top toolbar, and the room canvas has its own frame and view controls. Keyboard hints appear below the palette when space permits.

High-resolution editor UI uses physical coordinates and physical font sizes with the Gum camera at zoom 1. Logical hit-testing coordinates still account for the UI scale. This avoids a second font-oversampling pass whenever controls are rebuilt in a maximized window; gameplay retains its existing design-resolution font behavior.

Choose **Full stage** to edit connected rooms directly beside one another in their room-map positions. Painting or placing an object activates the room under the pointer without leaving this view. Brush strokes can cross room borders and remain one Undo action; fast drags interpolate the cells between pointer samples. The wheel zooms around the pointer, and **Space + drag** or the **middle mouse button** pans the map. Click the zoom value marked **Fit** to frame the entire stage again. **Room view** isolates the active room for focused editing.

Selection, flood fill and individual brush/object footprints remain bounded to a room. Dragging the brush can continue into adjacent rooms. Empty space between rooms does not create rooms automatically; use an empty room-map cell for that. Full-stage rendering is clipped to the canvas and reuses cached previews for visible rooms. Reduced zoom levels use uniform point sampling; isolated room view retains native integer pixel scaling.

Room textures are created on demand and reused. Editing several cells in one update invalidates the affected room once; serialized document snapshots are created for editing history and saving rather than for every frame.

## Start from a game stage

Choose **From Game**, select an installed cartridge, then select a stage. Validated extractions provide **MSX1 / Original** (60 stages), **MSX2 / Original** (60 stages), and **MSX2 / Contest** (20 stages). A source appears only when its extraction is available. Loading a stage creates an editable copy and selects that cartridge's graphics. It never writes into the extracted game stages.

Use **New** for an empty room. Select an existing cell in the right-hand room map to visit it, or an empty cell to add a room. The map supports up to 64 rooms.

## Paint and configure

The left palette groups **Terrain**, **Objects**, **Enemies**, **Tools**, and **Mechanisms**. Choose **Brush** to place the selected content, **Fill** for connected terrain, or **Select** to select and copy an area. Clicking an object with Select picks its type and contextual settings.

**Inspector > Settings > Graphics** switches between extracted MSX1 and MSX2 artwork while keeping the current stage, room, edits and history. Only installed flavors in the current campaign appear. Contest shows its own **MSX2 Contest** set and cannot switch to an Original cartridge through this selector. **F11** remains available as a shortcut. Loading another campaign uses **From Game**.

**Inspector → Settings → Pyramid** selects one of the six native pyramid themes. It changes the room's mural, terrain and palette using the active cartridge's extracted graphics. A stage has one theme shared by its rooms. A loaded game template retains its original theme. MSX1 and MSX2 artwork remain separate. On narrow windows, use the **Properties** button to open the panel.

Properties also contains brush size, music, enemy level and mechanism height where applicable. Preview changes before testing the stage. **Undo** groups a paint stroke into one action; making a new edit after Undo clears Redo.

The editor and play-test advance native background animation: MSX2 palette cycles and MSX1 eye/torch color streams. Backgrounds include complete static decorations, with edited terrain drawn over them. The original theme retains the source stage's decoration placement. Switching theme uses the selected pyramid's first native stage composition, restricted to matching room numbers; unsupported decoration kinds are never transplanted across themes. New rooms without a native placement use the repeating mural only. Contest keeps its own cartridge composition.

MSX1 graphics profile revision 4 reloads the cartridge's theme/common pattern tables before capture. It also stores native SCREEN 2 VRAM/register snapshots and clean room name tables for all six editor themes. Runtime changes use these extracted tables without executing an emulator. Older MSX1 generations require extraction again; incomplete attempts never replace a validated generation. Both source-stage mural parity and native 256 by 192 dimensions are preserved.

## Save and share

**Save As** opens a desktop file picker. The portable `.kv2level` file stores a versioned level document, its name and cartridge variant. It contains no ROM, artwork or audio. The recipient needs the corresponding cartridge extraction to open it. **Open** also accepts legacy stage JSON.

**Slots** retains the ten local stage slots. They live in the application's user data directory under `CustomLevels`; existing legacy executable-folder slots are copied when no replacement already exists. External documents may be saved in a folder of your choice. Extracted cartridge content is protected from overwriting.

The editor asks before discarding unsaved changes when replacing a document or leaving through Back. A recovery document is written every 15 seconds while changes are pending; the next editor session offers to restore it. Closing the operating-system window relies on this periodic recovery, so use Save before closing.

## Test and return

Choose **Test** or press **F5** to play the current edited map. Press **Escape** or **F5** during the test to return with the document, undo history, view mode, zoom and camera position retained. The preview and test assemble rooms from native assets, including newly added rooms.

| Shortcut | Action |
| --- | --- |
| Ctrl+N / Ctrl+O | New / Open |
| Ctrl+S / Ctrl+Shift+S | Save / Save As |
| Ctrl+Z / Ctrl+Y or Ctrl+Shift+Z | Undo / Redo |
| Ctrl+C / Ctrl+V | Copy / Paste selection |
| B / F / V / E | Brush / Fill / Select / Eraser |
| G | Toggle grid |
| Mouse wheel | Zoom around the pointer in Full stage |
| Space + drag / middle-button drag | Pan in Full stage |
| Zoom value / Fit | Frame the entire stage |
| F5 or P | Test |
| Escape | Back, close the current dialog, or return from test |

## Development diagnostics

Opening the editor automatically starts a timestamped `Diagnostics/editor-performance-*.jsonl` file in the application user data directory. Its full path is also printed in `kingsvalley2.log`. No environment variable is needed. Reproduce slow clicks, room changes and play-tests, then close the game normally to flush the last events.

The log records each toolbar/palette/room-map action, CPU input-to-draw completion, UI rebuilds and text/icon creation, room assembly/uploads, document snapshots, scene updates, Gum updates/draws and actual update intervals. Every two seconds it reports average/maximum times, slow-frame counts, managed allocations and GC counts. Times include nested scopes and graphics-driver waits; they are not pure GPU execution times or monitor latency. File-dialog waiting is identified separately because it includes time spent choosing a file. Disk writes run on a background timer with a bounded queue and a 5 MiB session-file cap.

For a controlled warm-cache check, `KINGSVALLEY_EDITOR_REBUILD_PROBE=1` rebuilds the same workspace once after 180 editor updates and emits a `probe` event. It does not change the document or simulate a user click. Normal sessions leave this probe disabled.

`KINGSVALLEY_EDITOR_WORLD_EDIT_PROBE=1`, with full-stage view and a two-room-or-larger template, checks unsaved-change protection during an active stroke, a cross-room stroke, one-step Undo/Redo, anchored zoom, panning and a Test/return cycle. It restores the original room data and history after its edits. Use an isolated `KINGSVALLEY_USER_DATA` directory for this integration check, which also exercises recovery saving. Successful checks emit `full_stage_edit_pass`, `full_stage_camera_pass` and `full_stage_return_pass` events. It is disabled in normal sessions.

`KINGSVALLEY_START_SCENE=editor` opens the workspace directly. `KINGSVALLEY_EDITOR_DIALOG=templates`, `slots`, or `properties` opens a specific panel. `KINGSVALLEY_EDITOR_TEMPLATE_VARIANT=msx1`, `msx2`, or `msx2-contest` selects a source; combine it with `KINGSVALLEY_EDITOR_TEMPLATE_STAGE` to load a stage through the same template-loading path as the UI. `KINGSVALLEY_USER_DATA` redirects application preferences and custom levels for isolated tests. These overrides do not modify the normal user data directory.
