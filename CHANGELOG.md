# Changelog

All notable changes to KS Unity Tools are documented here.

## [3.6.1]

- The new Kawaii Studio logo, in the tools' windows and in the Kawaii Studio Link (`com.kawaiistudio.link` 1.0.1). No code change.

## [3.6.0]

**The Kawaii Studio Link is its own package now (`com.kawaiistudio.link` 1.0.0), so world projects can have it.**

- KS Unity Tools depends on it: VCC installs both, and nothing changes for an avatar project (same window, same menu, same pairing).
- The Link package needs no VRChat SDK. KS Unity Tools stays an avatar toolset (it needs the avatars SDK), so a world project gets the Link alone. The Kawaii Studio app installs the right one into the Unity projects it makes (GET UNITY PROJECT): KS Unity Tools + the Link for an avatar, the Link for a world.
- **Links itself on a project the app set up**: when the Kawaii Studio app installs the Link into a project it made, it leaves `UserSettings/KawaiiStudioLink.autoconnect`; the Link reads it once on the first open, turns the link on and deletes it, so no Connect click is needed. A project where you already chose Connect or Disconnect keeps your choice.
- **Fix: the `.unitypackage` imports again.** Unity only reads a package whose gzip header names the inner file `archtemp.tar` (what its own exporter writes); the build script stored the package's own file name there, and Unity then imported nothing from it, without an error. The build now writes the header Unity expects.
- The `.unitypackage` carries the Link under `Assets/Kawaii Studio/Link`. Imported over 3.5.0, it adds the new Link and KS Unity Tools then deletes the 3.5.0 copy of the three Link scripts from `Editor/Link` on its first load, so two Links never run side by side.

## [3.5.0]

**Kawaii Studio Link: this Unity and the Kawaii Studio app, linked.**

- **New window `Kawaii Studio > Kawaii Studio Link`**: click Connect once and the project pairs with the running Kawaii Studio app (VRChat Archive) over its local bridge on `127.0.0.1:8791`, then links itself again each time it opens. The window shows the link, the app's version, the signed-in account and its level, and which features that level includes (Scene Editor: VIP, Bundle Patcher: Legendary). The server stays the one that decides; Unity only shows what the app reports.
- **Send to Unity**: in the app's Scene Editor, Send to Unity runs Export all (the Unity 2022 project layout, shaders rebuilt on the server included) straight into this project. The files arrive in `Temp/`, are moved into `Assets/Kawaii Studio/KS Exported/<name>`, imported, and the avatar's prefab (or the world's scene) is selected and pinged. Sending the same name again keeps the previous copy once in `Backups~/KS Exported/<name>`; the GUIDs are the same, so anything that used the old files follows the new ones. The window lists the recent transfers (Select, Open scene).
- **Security**: the app's bridge is reachable by any web page, so pairing needs a secret only a local process can read (`%LOCALAPPDATA%\KawaiiStudio\link\bridge.json`, new on every app start), traded for a token for this project; the app refuses anything that looks like a browser request. Unity opens no port: it long-polls the bridge from a background thread, which also survives every domain reload.
- **Test in Kawaii Studio** (`Kawaii Studio > Test in Kawaii Studio`, the hierarchy's right-click menu, or the Link window): builds the selected avatar, or the open world scene, and opens it in the app's Scene Editor, playing right away. An avatar starts in Play mode; a world starts on the World toolkit's Photon sim. The avatar is prepared the way the VRChat SDK prepares a build: a clone goes through the SDK's preprocess callbacks (Modular Avatar, VRCFury and NDMF hook in there), then Unity's BuildPipeline writes a PC bundle into `Library/KawaiiStudio/TestBuilds`. The app copies it before opening it, and the server still decides whether the Scene Editor opens (VIP).
- Headless entry points `KawaiiStudio.KawaiiStudioLink.CommandLineServe` and `KawaiiStudio.KawaiiStudioTestBuild.CommandLineTest` (`KS_LINK_TEST` = the prefab or scene) (batchmode without `-quit`; `KS_LINK_EXIT_AFTER`, `KS_LINK_TIMEOUT`, `KS_LINK_DIR`).
- Needs Kawaii Studio with the link (the app release that ships Send to Unity).

## [3.4.3]

- **Video Animator**: a per-atlas material that already exists ("… Mat N.mat", reused so its GUID holds) is rebuilt from the custom material -- shader and properties -- before its atlas is set. It used to keep whatever shader an older conversion had given it (the pre-3.4 tool's copies pointed at `rireal/screen overlay`), so a re-run over an old folder played on the wrong shader.

## [3.4.2]

- **Video Animator**: the default output is a quad with the **KSVideoDecoder** material again. 3.4.1 switched "Use custom material" on by itself with the workspace's Video Screen Overlay, so every new video came out as a full-screen overlay (a white screen in the Scene view until the animation runs). The overlay is now only prefilled in the "Material" field: tick the box when you want the screen overlay.

## [3.4.1]

**Every shader gets its Kawaii Studio inspector, and the shaders catch up with the author's latest versions.**

- **KSHairRealistic**: the cutout build (`TransparentCutout`, `AlphaTest+50`): a real alpha clip with a "Transparency Threshold" (`_ClampCutoff`), clipped shadows so transparent tips no longer cast square shadows, and a depth pre-pass that fixes the self-sorting artifacts of overlapping semi-transparent strands. The inspector now exposes the threshold and, in a new **Advanced** section, the energy-conservation / specular-colour / tangent-texture toggles and the specular / reflection switches that were in the shader but never shown.
- **KSVampireCrimsonEye**: stabilised ring noise -- the rings no longer "blink".
- **Prefab Optimizer, rebuilt around one rule: nothing breaks, nothing the eye can see changes.**
  - Textures are classed by ROLE (main colour, normal map, emission, secondary map) from the shader properties they feed. **Balanced keeps main and normal maps at 2048** (the size avatars ship with) and brings secondary maps to 1024; crunch is 90, never on a normal map (crunch makes those shade wrong). The old blanket "50 %" halved every albedo to 1024 and crunched at 50-75.
  - Polygon reduction: the error ceiling is 0.15 % of the mesh size at 75 % (was 0.5 % -- 8 mm on an avatar, visible on a face). Meshes under Cloth are left alone. **Every reduced mesh is verified** against its source (positions, normals, UVs, bone weights, blendshape deltas, bind poses) before it is used; a mesh that fails is left untouched. The triangle counts on screen are now the real ones (the simplifier is run for the estimate), not a guess.
  - Audio: Balanced is "High" (Vorbis 0.7, channels and rate kept); mono / 22 kHz is Maximum only.
  - Unused blendshapes: every controller and clip referenced by ANY component on the avatar is read (Modular Avatar, VRCFury, Gesture Manager…), not only the descriptor's layers.
  - New UI: preset cards, a **Visual impact** badge (none / slight / noticeable, with the reasons), four live tiles (texture memory, download, triangles, audio), role badges per texture, search and "changed only" in the item list, per-texture size override (keep / 2048 / 1024 / 512 / 256), "Select the copy" after optimizing. Import settings are project-wide: the banner says so.
- **Video Animator**: the number of atlases is now bounded ("Max atlases", default 8; frames are scaled down, aspect kept, to fit) -- a 100 s video used to come out as 800+ atlases and 400+ materials, which nothing could play; a conversion the KSVideoDecoder cannot play (> 64 atlases without a custom material) is refused up front. An existing controller with no layer or state (left by a conversion that died) is repaired instead of being left empty. Atlas clearing no longer needs a graphics device. FFMPEG is only accepted from a folder whose `ffprobe -version` actually answers (a copy without its DLLs starts and exits silently, and every video then read as 0x0). The output name is cut down only when the full path would pass Windows' 260-character limit. Headless entry point `KawaiiStudio.VideoAnimatorWindow.CommandLineConvert`.
- **Video Animator**: loading a video now sets the **frame size to the video's resolution** (it stayed at 512x512; the atlas packer still scales frames down when they do not fit the atlas limit) and, when no custom material is chosen, picks the workspace's **Video Screen Overlay** material (KSScreenShader, `_MainTex`) and switches "Use custom material" on -- the video plays on the overlay screen out of the box. The log shows the exact frame rate (23.976, not 24.0).
- **Inspectors**: BloodKiller's `CustomEditor` named its class without the namespace, so Unity silently fell back to the default inspector -- fixed. `screen_overlay` referenced Shader Forge's inspector (not installed). **KSScreenShader, VR Stereo Display and KSVideoDecoder had none.** All four now use `KawaiiStudioShaderGUI`: the branded banner, coloured sections built from the shader's own `[Header]` blocks, a hint on what the shader is for, the Video Decoder's 64 atlas slots shown only where used (the Video Animator fills them), the Stereo Display mode as a popup, render queue / instancing / double-sided GI, and the community footer -- the same look as the hand-written inspectors.

## [3.4.0]

**The workspace.** The package now creates and maintains `Assets/Kawaii Studio` in your project and every tool talks to it.

### Why
Installed through VCC, the toolset lives under `Packages/` -- read-only and invisible in the Assets tree. 3.3.0 still kept its shaders and materials there and its tools still wrote to hard-coded `Assets/Kawaii Studio/...` paths that nothing created: the Video Animator could not find its own decoder shader, materials pointed at shaders with random GUIDs, outputs landed in stray folders (`Assets/Kawaii Studio Optimized`, `Assets/KS_Protected_*`).

### What changes
- **Workspace installer** (`Kawaii Studio > Workspace`): on load, the package creates `Assets/Kawaii Studio` with `Shaders`, `Materials`, `Videos`, `Animations`, `Prefabs`, `Models`, `KS Exported`, `Optimized`, `Protected`, `ThirdParty/FFMPEG`, and installs the 8 shaders and 2 materials there **with their published GUIDs** -- a material that already uses a Kawaii Studio shader keeps working. Shaders follow package updates (the previous file goes to `Backups~/<version>/`); materials are yours and are never replaced. `workspace.json` records the synced version. "Install or Repair" re-copies everything.
- **Project scan and repair** (`Kawaii Studio > Workspace > Scan and Repair Project`, also offered right after an install or an update): the package looks INSIDE the project for what older installs left behind and puts it right -- tool scripts of a pre-package install under `Assets/` (they compile next to the package and double every menu item: parked in `Backups~`); byte-identical copies of a Kawaii Studio shader outside the workspace (every material on the copy is repointed to the workspace shader, the copy is parked -- a modified copy is yours and is only reported); materials whose shader is missing (the pink `Hidden/InternalErrorShader`) when a known pre-3.4 GUID or their saved properties identify one of our shaders beyond doubt (repointed; otherwise reported); outputs of older versions in `Assets/Kawaii Studio Optimized` and `Assets/KS_Protected_*` (moved into the workspace through the AssetDatabase, so references hold); FFMPEG in the pre-3.4 `Assets/ThirdParty/FFMPEG` (moved to the workspace). Nothing is ever deleted.
- **Your edits are safe**: the installer records the hash of every shader it installs; on an update it only replaces shaders that are still exactly what it installed. A shader you edited, or one that was already in the project before the package, is kept and listed -- "Repair / Update" in the Studio Manager replaces everything, with the previous files kept in `Backups~`.
- **Video Animator**: decoder shader and `Video Screen Overlay` material come from the workspace; the converted video's size is written into the overlay material (`_VideoWidth` / `_VideoHeight`, KSScreenShader) so a screen shows it undistorted; FFMPEG is looked up in `Assets/Kawaii Studio/ThirdParty/FFMPEG` first (then the old `Assets/ThirdParty/FFMPEG`, then any `ffmpeg` folder), `bin/ffmpeg.exe` layouts included; outputs in `Videos/<name>/`.
- **Controller To Anim** joins the package (`Kawaii Studio > Universal Tools > Controller To Anim`): bake a chain of a controller layer's states into one clip. Output in `Animations/Controller To Anim`.
- **Prefab Optimizer** writes to `Optimized/` (was `Assets/Kawaii Studio Optimized`), **KS Obfuscator** to `Protected/` (was `Assets/KS_Protected_*`), **Kawaii Exporter** to `KS Exported/`, **Tail to PhysBones** to `Animations/` and `Prefabs/`, **GLB to FBX** defaults to `Models/`. Every folder is created through the AssetDatabase, so Unity sees it at once.
- **Studio Manager** shows the workspace (folder, synced version, package location) with Install / Repair and Open Folder, and lists Controller To Anim.
- `EYE SHADER.mat` now references the published `KSVampireCrimsonEye` GUID (it pointed at a GUID that only existed in the author's project).
- Package: shaders and materials ship as `Shaders~` / `Materials~` sources with tracked `.meta` files (Unity ignores `~` folders, so the only imported copy is the workspace's). The `.unitypackage` build maps them back to `Shaders` / `Materials`, where the package is the workspace. `legacyFolders` removed from `package.json`.

## [3.3.0]

**Prefab Optimizer, rebuilt**: real compression and real polygon reduction, without breaking your avatar.

### Simple
- Presets: **Light** (no visible change), **Balanced** (recommended), **Maximum** (smallest).
- Three sliders in %: texture size, audio quality, polygons. Plus "remove unused blendshapes".
- Live numbers: texture memory, triangles and audio, before → after.
- Every texture, mesh and sound listed with its own estimated before → after and its own setting (Auto, or forced).

### Faithful
- Polygon reduction only folds edges onto existing vertices: every vertex left keeps its exact position, normals, UVs, skin weights and blendshape deltas. Faces and everything a blendshape moves, UV seams, open borders and material edges are never touched, and a mesh stops before its shape would change.
- Textures: size is a % of the source image (never halved twice), uncompressed ones get compressed, crunch makes the download smaller. Ramps, lookup tables and small textures are left alone.
- Audio: Vorbis at the chosen quality, mono 22 / 11 kHz for Medium / Low. Only ever smaller.
- Nothing of yours is overwritten: the lighter meshes are new files in `Assets/Kawaii Studio Optimized/`, used by a copy of your avatar named "… (Optimized)". "Restore settings" puts texture and sound settings back.

Tested on a 170k-triangle avatar: Balanced gives 128k triangles and halved textures, and it looks the same in Unity.

Translated into all seven languages.

## [3.2.0]

New tool: **Menu Colorizer** (`Kawaii Studio > VRC > Menu Colorizer`).

VRChat draws expression-menu labels with TextMeshPro, so a button name can carry rich-text
tags. A gradient, though, is one `<color>` tag per letter, which nobody wants to type by
hand. The Menu Colorizer writes the tags for you.

### Added
- **One-click looks**: Rainbow, Candy, Neon, Sunset, Ocean, Sakura, Fire, Ice, Galaxy, Toxic,
  Gold and Pastel. One button colours every label of the root menu and all of its
  sub-menus, each button in its own colours, with bold, italic and size options.
- **Live radial preview** drawn like VRChat's own menu. Click a button to edit it,
  double-click a sub-menu to open it.
- **Per-button editor**: text (multi-line), solid colour, two-colour gradient, rainbow, bold,
  italic and size, plus "same style on this whole page". Gradients blend in HSV, so the
  middle letters stay as vivid as the ends instead of going grey.
- Puppet labels (two- and four-axis) are coloured too.
- An existing label opens in the editor with the style it was written with. "Remove all
  colours" brings back the plain names.
- Right-click any Expressions Menu asset: `Kawaii Studio > Colorize VRChat Menu`.
- Translated into all seven languages.

### Safe by design
- Only label text is written. Parameters, control types, icons and sub-menu links are never
  touched.
- Every operation is a single undo step.
- Menus inside read-only packages are detected and skipped, with a warning, instead of
  silently failing to save.
- Emoji with skin tones, flags and accented letters are coloured as one character and never
  split.

### Colour-coded tools
The design system gains per-section accents, so a window reads as a set of distinct,
labelled panels instead of one flat purple wall.
- New shared building blocks: vivid section accents (pink, cyan, green, orange, blue,
  purple), sections with a tinted header and an accent rail, a big `ActionButton` for the
  main action and a `StatusBanner` for verdicts.
- The Studio Manager, GLB to FBX Converter, Video Animator and Menu Colorizer use them.
  The GLB and Video Animator windows also get the Kawaii Studio banner. Only the layout
  changed; every field and the conversion pipelines are untouched.
- Empty consoles now say "No output yet." instead of showing a blank box.

## [3.1.0]

Visual pass. The tools worked but looked flat, and the Studio Manager was doing a job it
shouldn't. This restores the branding and turns the Manager into a proper hub.

### Banner is back
- Every tool's header draws the Kawaii Studio artwork again. It had two problems: the
  `.unitypackage` didn't ship `banner.png` at all (so those installs fell back to a plain
  gradient), and even where it was present the header cropped it to a 96px sliver and
  dimmed it under a flat 55% veil. The header is now 140px, the art shows at full strength
  on the right, and a left-to-right shade keeps the logo/title/subtitle crisp on the left.
- `banner.png` ships in **both** the `.unitypackage` and the VCC zip, so the two installs
  look identical. It was resized from 4096px/13 MB to 1024px/~0.7 MB, so shipping it
  everywhere is cheap.

### Studio Manager is a hub, not an installer
- It used to download individual `.cs` files from raw.githubusercontent and overwrite them
  in place to "update" tools -- fragile, and redundant now that VCC handles updates. All of
  that is gone. The Manager is a launcher: it lists every tool, opens it, shows the shared
  version, and links out.
- It now uses the shared design system (the same cards, buttons and header as the tools),
  so it stops looking like a different, older app. VRChat tools are detected and greyed out
  with a note when the SDK isn't installed, instead of failing silently.

## [3.0.4]

Unity still refused the 3.0.3 package. The archive layout was wrong in two ways that only
became visible by comparing against a `.unitypackage` Unity had written itself, rather
than against the previous release (which turned out to be malformed the same way).

Unity writes each asset as a real tar **directory entry** (type 5, mode 0777) followed by
its files in alphabetical order — `asset`, `asset.meta`, `pathname`. The generator wrote
no directory entries at all and led with `pathname`, so the importer rejected the archive
even though every byte of content was correct.

### Fixed
- Each asset now gets its tar directory entry, files follow in Unity's own order, and
  every entry uses mode 0777 — matching a Unity-authored package entry for entry.

## [3.0.3]

Unity refused to import the 3.0.2 package. Every entry was present and well-formed, but
they were written in the wrong order inside the archive.

A `.unitypackage` is a tar in which each asset is a GUID-named directory holding
`pathname`, `asset` and `asset.meta`. Unity reads that tar as a stream and expects the
order it writes itself — `pathname`, `asset`, `asset.meta`. The generator emitted
`asset.meta` before `asset`, and the importer rejected the package outright rather than
reordering.

### Fixed
- Entries are now written in Unity's own order, matching a known-good package
  byte-pattern for byte-pattern (folders: `pathname` + `asset.meta`; files: `pathname` +
  `asset` + `asset.meta`).

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
