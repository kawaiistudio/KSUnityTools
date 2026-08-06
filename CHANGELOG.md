# Changelog

All notable changes to KS Unity Tools are documented here.

## [3.0.2]

Ships the package 3.0.1 was supposed to ship. The 3.0.1 build worked, but CI writes the
VCC zip into `build/` before generating the `.unitypackage`, and that directory was not
excluded — so the 16 MB zip ended up packed inside the package as
`Assets/Kawaii Studio/build/…`, turning a 0.5 MB download into 17 MB. It never showed up
in local testing because a fresh checkout has no `build/` directory.

### Fixed
- `build/` (and `obj`, `Temp`, `Library`) are excluded from the `.unitypackage`, so no
  build output can leak into it. Download is back to ~0.5 MB.

## [3.0.1]

Fixes the `.unitypackage`. The one attached to 3.0.0 was built by hand from an older
source layout and could not work: it contained neither assembly definition and it
flattened `Editor/Tools/VRC/` into `Editor/Tools/`.

Losing the assembly definitions is what actually broke it. The VRChat tools are meant to
compile only when the VRChat SDK is installed — `KawaiiStudio.VRC.Editor` is constrained
on `VRC_SDK_VRCSDK3`. Dropped into `Assembly-CSharp-Editor` instead, they compiled
unconditionally, so in a project without the SDK every editor script failed to compile
and nothing in the package worked.

### Fixed
- The `.unitypackage` is now generated from the repository by CI, so it can never drift
  from the source tree again. The build fails outright if fewer than two `.asmdef` files
  make it in.
- Both assembly definitions and the real `Editor/Tools/VRC/` folder are shipped, so the
  VRChat tools stay gated behind the SDK and the rest of the toolset compiles on its own.
- Asset GUIDs are pinned to the ones published in 3.0.0. A GUID is an asset's identity in
  Unity, so regenerating one turns an update into delete-and-add and silently breaks every
  material, prefab and scene reference in your project. The five tools that moved into
  `Editor/Tools/VRC/` keep their 3.0.0 GUIDs, so Unity moves them instead of duplicating.
- The 13 MB `banner.png` is no longer inside the package (the tools already treat it as
  optional), bringing the one-click download back down to ~0.5 MB.

### Known issue
- `Materials/EYE SHADER.mat` references a third-party eye shader that has never been part
  of this toolset, so it imports without a shader. It was equally broken in 3.0.0.

## [3.0.0]

Merge of the two lines that had drifted apart: the published .unitypackage (12 tools,
product version 1.4) and the git repository (4 tools, versioned 2.0/2.2). The result is
a superset of both, so it takes 3.0.0 — above every number previously in circulation.

### Merged
- Imported the 8 tools that existed only in the shipped package: Exporter, NSFW Detector,
  Obfuscator, Tail Animator to PhysBones, Contact Scanner, Ultimate Constraint Tool,
  plus the 4 shader GUIs and the 8 shaders.
- Kept the repository's Video Animator (its ffprobe metadata and loop work was never in
  the package) and its per-tool localization for the Prefab Optimizer and GLB to FBX.
- New `Editor/Core` layer: `KawaiiStudioGUI` (design system), `KawaiiStudioBranding`,
  `KawaiiStudioPaths`, `KawaiiStudioUtil`, `KawaiiStudioLocalization`, `KawaiiStudioVersion`.
- One version constant for the whole toolset; every tool reads `KawaiiStudioVersion.Current`.
- VRChat-dependent tools moved into their own assembly, constrained on `VRC_SDK_VRCSDK3`,
  so the rest of the toolset still compiles without the VRChat SDK.

### User interface
- Rebuilt `KawaiiStudioGUI` as a real design system: 4pt spacing scale, type ramp, cards,
  primary/secondary/danger buttons with hover and active states, badges, stat tiles,
  progress bars, empty states and inline validation banners.
- Everything is theme-aware and follows Unity's dark **and** light editor skin; styles and
  generated textures rebuild automatically when the skin changes.
- All chrome is generated procedurally, so the 13 MB banner PNG is no longer shipped and
  FFmpeg is no longer bundled (the Video Animator locates an existing or system install).
- Prefab Optimizer reorganised: scan summary tiles, then Textures / Meshes / Audio as tabs
  instead of six stacked sections; the log is selectable with copy/clear.

### Fixed in the merged tools
- Shader GUIs pointed at `Editor/Cache/logo.png`, a folder renamed to `References` in v1.4,
  so the logo and banner silently never loaded.
- Obfuscator: created its output folder without telling the AssetDatabase (so every
  `CreateAsset` failed), reported "Encryption Complete!" even when the prefab was never
  written, leaked the instantiated avatar clone into the scene, left the modal progress bar
  up on any exception, and broke on avatar names containing path characters.
- Exporter: reported success and revealed a nonexistent file when the user cancelled
  Unity's export dialog.
- `MakeTex` was duplicated in three tools, each allocating textures without
  `HideAndDontSave` and never destroying them.
- Hardcoded `Assets/Kawaii Studio/Languages` broke any install under `Packages/`.

## [1.4.1] - folded into 3.0.0

### Distribution
- The repository is now a Unity package (`com.kawaiistudio.ksunitytools`) installable
  through the VRChat Creator Companion (VPM) or Unity's Package Manager (git URL).
- Editor scripts moved to `Editor/` behind the `KawaiiStudio.Editor` assembly definition,
  so the tools no longer compile into `Assembly-CSharp-Editor`.

### Fixed - Prefab Optimizer
- Audio sample rate was compared against the wrong value: the chosen rate was never
  applied to clips set to "Preserve", and every later run re-reported the clip as modified.
- A per-mesh compression of `Off` could never be applied; it silently fell back to the
  global setting. Added an explicit per-mesh "Global" toggle.
- "Memory saved" counted already-optimal textures as 0 bytes, hugely overstating the total.
- Optimized texture memory was measured on the stale pre-reimport texture instance.
- Estimated audio size was read back from the source file, which importer settings never
  rewrite, so the reported reduction was always ~0.
- A material with a missing/failed shader aborted the whole scan.
- Missing files no longer throw while computing sizes.
- The modal progress bar is now cleared in a `finally` block; an error used to leave it
  on screen and lock the editor.
- Generated GUI textures are tagged `HideAndDontSave` and destroyed on close (no more
  "Texture2D has been leaked"); two unused textures are no longer allocated at all.

### Fixed - Manager
- `StartCoroutine` ignored nested `yield return <IEnumerator>`, so "Check all for updates"
  and "Update all" performed no work at all yet reported success.
- An exception inside a coroutine left it subscribed to `EditorApplication.update` and
  re-threw on every editor tick.

### Fixed - GLB to FBX
- A Blender crash or Python error was reported to the user as a successful conversion.
- A failed Blender launch left the window permanently stuck in "converting".
- Output folder creation failures now surface as an error instead of an unhandled exception.
- The Blender process handle is disposed, and closing the window no longer throws when
  the process was never started.
- The project is refreshed when the output folder is inside `Assets/`.

### Fixed - Video Animator
- `GCD` never terminated when given 0, hard-freezing the editor; atlas slice counts are
  clamped so this input can no longer occur.
- Divide-by-zero when a frame was larger than the atlas size limit.
- ffprobe could deadlock the editor indefinitely when it wrote more than one pipe buffer
  of stderr; stderr is now drained asynchronously.
- Frame-mode encoding called main-thread-only APIs from the process exit thread.
