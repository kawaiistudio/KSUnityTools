# Changelog

All notable changes to KS Unity Tools are documented here.

## [1.4.1] - unreleased

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
