# Changelog

The Kawaii Studio Link package (`com.kawaiistudio.link`). It ships with KS Unity Tools (a dependency since 3.6.0) and installs alone in a world project.

## [1.2.1]

- **Fixed a GUILayout error in the Link window** (`Getting control N's position in a group with only N controls`).
  The window reads state the background link thread changes — the log, the history, the connection — and that could
  change between IMGUI's Layout and Repaint passes, so a section built a different number of controls on each. The
  window now freezes everything it reads once per repaint, on the Layout pass, so both passes draw the same controls.

## [1.2.0]

**Other Kawaii Studio packages can answer the app through the Link, without the Link knowing about them.**

- Any loaded Kawaii Studio assembly may expose a `public static class KSLinkTools` with `Knows(string)` and `Run(string, string)`; the Link finds it by reflection and hands it the questions it knows. Before this, every question the app could ask was a hard-coded case in the Link, so a new feature in KS Unity Tools meant re-releasing the Link too.
- KS Unity Tools 3.9.0 uses it for KS ANTI-RIP: the app lists the avatars in your open scenes, checks a project before protecting (account, editor, avatar, parameters) and repairs what it can, through the Link.
- A provider that throws is reported with its real reason and never takes the Link down. No new dependency; still works in world projects.
- **Fixed: "Kawaii Studio is not running" while it was.** When the app ran as administrator and Unity did not, Windows refused the Link the right to ask whether the app's process had exited, and the Link took the refusal for an answer. Now only "no such process" means gone.
- **The window wears the app's galaxy.** The banner behind the logo is the Kawaii Studio app's own backdrop -- its galaxy shader computed once into a 229 KB image -- instead of a flat gradient.

## [1.1.0]

**An AI linked through the Kawaii Studio app works on the projects open in Unity: it looks, and it edits.**

- **Every project links by itself now.** A project opened in the Unity editor links with Kawaii Studio on its own whenever the app runs (it waits quietly while the app is closed); no Connect click. **Disconnect** still keeps a project apart until you click Connect. A headless batch-mode Unity (a build, a CI job) links only when told.
- **Several projects at once**: the app knows every project open with its Link, and the AI is told about all of them. You tell the AI in your own words which one you want to work on ("I want to work on MyAvatar"); it stays that one until you name another. Nothing is picked anywhere.

- The Kawaii Studio app (v638 and later) has a local MCP server that an AI uses through it, such as Capri when Claude Code is her AI. Its requests reach this project over the same link as Send to Unity, and the Link answers them on Unity's main thread.
- **Looking** (always, while the app's switch "Her AI sees my Unity project" is on): the project (Unity version, render pipeline, VRChat SDK packages, open scenes, compiling or playing, Console counts), the hierarchy (a branch, a depth, a maximum), one object's components and serialized fields as the Inspector shows them, the selection, the Console's errors / warnings / logs with their first stack lines, an asset search (`t:Material Skin`), and one text file of the project with numbered lines, read part by part when it is big. The Console is read whatever its Log / Warning / Error buttons hide, and those buttons are put back exactly as they were right after.
- **Editing** (while the app's second switch "...and may change it" is on): write a new script, shader or text file, or change part of one; create GameObjects (primitives, under a parent, with components); rename, move, turn, scale, re-parent, (de)activate, tag, duplicate or delete them; add or remove components; set any serialized field the Inspector shows (numbers, true/false, enums, colours, vectors, references to assets or scene objects, material slots) and a material's shader properties; create folders and materials, move, copy and delete assets; save the scenes, refresh, Play / Stop, select, undo / redo, run a menu item.
- **Everything can be taken back**: each change to a scene is ONE Undo step in Unity ("Capri: ..."). A text file it overwrites or edits is copied first into `Assets/Kawaii Studio/Backups~/AI` (Unity does not import a folder ending in `~`); only text files are copied, the same content is never copied twice in a row, and the folder keeps the 30 newest copies within 10 MB. A deleted asset goes to the Windows Recycle Bin. Nothing is saved by itself: scene changes stay unsaved until the AI saves when asked (or you press Ctrl+S).
- Never written: files outside the project, outside `Assets/` and `Packages/`, `.meta` files, Unity's own folders, the backups themselves. Never read: files outside the project, `Library`, `Temp`, `Logs`, `obj`. Each answer is capped so a huge scene cannot stall the editor.
- A script it writes makes Unity recompile; the link comes back by itself after the reload, and the AI then reads the Console for compile errors.
- Tested end to end with real Unity 2022.3 editors, two of them open at once (92 checks: every question and every edit, the recompile and the link coming back, undo and redo, saving, the refusals, the backups staying at 30, the editing switch off, not one byte of the project changed by looking, the right project answering and an edit going only to the project named) and compiled for Unity 2019.4 (C# 7.3) and Unity 6.6.
- An app older than v638 never asks; an older Link answers that it does not know the request and must be updated.

## [1.0.1]

- The new Kawaii Studio logo in the Link window. No code change.

## [1.0.0]

- The Link of KS Unity Tools 3.5.0, as its own package: the `Kawaii Studio > Kawaii Studio Link` window, pairing with the Kawaii Studio app over its local bridge, Send to Unity (imports into `Assets/Kawaii Studio/KS Exported`) and Test in Kawaii Studio (the selected avatar or the open world scene).
- **Links itself on a project the app set up**: when the Kawaii Studio app installs the Link into a project it made, it leaves `UserSettings/KawaiiStudioLink.autoconnect`; the Link reads it once on the first open, turns the link on and deletes it, so no Connect click is needed. A project where you already chose Connect or Disconnect keeps your choice.
- No VRChat SDK dependency: a world project can have it, where KS Unity Tools (avatar tools, needs the avatars SDK) cannot go. The Kawaii Studio app installs it into the Unity projects it makes (GET UNITY PROJECT).
- Same type names as in 3.5.0 (`KawaiiStudio.KawaiiStudioLink`, `KawaiiStudioTestBuild`, the headless `CommandLineServe` / `CommandLineTest` entry points), now in the assembly `KawaiiStudio.Link.Editor`.
