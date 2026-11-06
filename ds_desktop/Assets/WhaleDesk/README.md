# Whale Desk

Open `Assets/Scenes/WhaleDesk.unity` and select the Game tab to view the fixed frontal composition.

This scene reuses the writing desk from HQ Residential House's `DemoScene_BakedSetup_Day` and the pack's books, lamp, laptop, stationery, shelf, whiteboard and clock. The backdrop is an interior wall, with no exterior view.

The desk is approximately 3.33 × 1.68 metres; its surface is 0.755 metres above the floor. The whale girl is 0.40 metres tall and stands slightly right of centre, with both arms lowered. Her VRM source is unchanged. The scene is set as the enabled build scene; the original SampleScene remains available.

Hierarchy groups separate furniture, desktop objects, backdrop and lighting. Scene-specific materials and the post-processing profile are under `Assets/WhaleDesk`.

## Walking

Press Play, then left-click a free point on the desk in the Game view. The whale girl turns and walks there around the desktop props. A thin blue route and target ring show the current command. Click another point to redirect her; Escape cancels. Red rings indicate rejected targets, including objects and the desk edge. The floor and prop tops are not walkable.

`DesktopWalker.TryMoveTo(Vector3 worldPosition)` returns whether a complete route was accepted. X/Z are world coordinates and Y is projected to the desktop. Invalid requests preserve an existing command. `State` reports Idle, Moving, Arrived or Failed; `LastError` explains a rejected request or interrupted movement. `StateChanged` notifies callers. `CancelMove()` stops the current command. Python/JARVIS can invoke these actions through the desktop MCP integration described below.

`06 - Desktop Navigation` holds the table bounds, saved NavMesh and mouse input. `大肥鱼 - Action Root` holds the NavMeshAgent and movement component; the original VRM remains its visual child. The 0.21 m navigation radius includes clearance for the dress. Obstacles use conservative renderer footprints and Unity NavMesh carving, so moving/disabling a marked prop updates the route after a short carving delay. Flat desk mats and the loose pen are traversable. New props need a `DesktopObstacle` referencing the navigation component.

The original whale humanoid Animator now uses Quaternius UAL1 Standard's `Armature|Idle_Loop` and `Armature|Walk_Loop`, retargeted through her own Avatar. `Animation/WhaleLocomotion.controller` blends Idle/Walk over 0.15 seconds using `IsWalking`. `WalkSpeed` adjusts cadence to actual NavMeshAgent velocity; `DesktopWalker.walkAnimationReferenceSpeed` is the world speed matching playback speed 1. The old procedural bone swing and transform bob have been removed.

The FBX import follows the included Unity setup: Bake Axis Conversion on, Humanoid/Create From This Model, motion node `Armature/root`, and looping for `_Loop` takes. This is the non-RM library; Apply Root Motion stays off on the whale Animator so only the outer action root controls navigation. The source mannequin in the scene is preserved but disabled.

`Whale Desk > Install Humanoid Animations` refreshes the clip import settings, owned Animator controller and scene binding. Reinstalling the walking system or rebuilding the scene reuses an existing animation controller automatically.

`Whale Desk > Install Walking System` installs/refreshes navigation on the current WhaleDesk scene without rebuilding its composition. After changing the desk dimensions, run it again to regenerate the saved NavMesh. The scene builder also installs navigation automatically. Runtime verification commands and their results live in `Temp/WhaleDeskNavigation.*`; they are editor-only and are not included in builds.

`Whale Desk > Rebuild Study Scene` regenerates the authored layout from `Assets/Editor/WhaleDeskSceneTools.cs`; rebuilding replaces the generated scene layout. Make a copy of the scene before rebuilding if you have edited it manually.

## Local model / MCP

`07 - Local Model MCP` exchanges snapshots and actions with the loopback Python bridge at `http://127.0.0.1:18765`. The Python stdio MCP server exposes live-state filtering, coordinate movement, object approach and cancellation. Run the existing JARVIS CLI from the repository root to enable model tool calling; full startup and external MCP client configuration are in the repository's `docs/desktop-mcp.md`.

The whale has a dedicated `DesktopCharacterState` on the action root. Ordinary props use `DesktopWorldObject` with a stable ID, name, type, tags, Include In State and approach points. Object filters never remove the character state. Perception is independent of `DesktopObstacle` collision/navigation. Only implemented capabilities are advertised.

`Whale Desk > Install Desktop MCP Integration` installs or refreshes bindings and marks the initial 12 logical objects. Existing object metadata is preserved. The scene builder also installs these bindings. Runtime metadata and navigation are calculated on Unity's main thread; model inference stays outside Unity.
