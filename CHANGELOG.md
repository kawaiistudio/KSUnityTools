# Changelog

All notable changes to KS Unity Tools are documented here.

## [3.16.4]

**KS ANTI-RIP: "Max size added" now caps everything the protection adds.** Before, it only sized the restore shapes,
while the meshes closed by their shader -- which ship at full precision, because the shader needs their normals exactly
-- were never counted. A large avatar could come out several MB over its budget, and over VRChat's 200 MB limit, whatever
the setting said. Now the shader meshes are paid for first; when they would leave the restore shapes too little of the
budget, the one that costs the most is protected by restore shapes instead (a static mesh, which has none, is left as it
is), and the protection report says which and why. Measured on a 1-million-vertex avatar: +6.1 MB at 12 MB and +4.6 MB at
8 MB, where it added 14.8 MB before. Raise the setting if your avatar has room under 200 MB to keep every mesh closed by
its shader.

**KS ANTI-RIP: smaller restore-shape meshes.** A scrambled mesh whose source used Mesh Compression now ships at Low
compression instead of none. Measured through real bundles, its vertices come back within 0.007 mm and its restore
shapes exactly, for 30 to 40 % less size. A mesh whose source was not compressed stays that way.

## [3.16.3]

**KS ANTI-RIP: the app shows the settings your avatar really has.** When the Kawaii Studio app lists your avatars, each
protected one now comes with the settings saved in its protection (every switch and slider), and whether its scene has
changes not saved yet. Before, the app showed its own defaults, so an avatar protected with older settings looked
right in the app and uploaded with what Unity had. With app v675 the page shows what Unity has, and changes you make
there are applied to the avatar at once. The protection is saved with your scene: save the scene in Unity (Ctrl+S)
after turning it on or changing a setting, or it is lost when Unity closes. The app now says so when the scene is not
saved.

## [3.16.2]

**KS ANTI-RIP: optional neutral blend shape names.** A new switch, "Neutral blend shape names" (off by default),
gives every blend shape of the protected upload a name that says nothing, so a ripped avatar no longer tells which
shape is the smile or the blink. The avatar descriptor's visemes and jaw-flap shape and every animation that moves a
blend shape follow the new names, so visemes, blinking, expressions and face tracking keep working. Leave it off if you
dance in MMD worlds: they move the face by the standard names, so it would stay still. Your project's meshes and
animations are never changed: the renamed copies are made for the upload only. Needs the app update to show the switch.

**KS ANTI-RIP: static meshes are closed by their shader too.** Props and accessories that are not skinned (a weapon,
a hat, a chain) have no restore shapes, so up to now they shipped as they are. With "Close static meshes too" (on by
default, under the shader switch), those wearing a locked Poiyomi or a lilToon material are closed by their shader like
the skinned meshes. A static mesh the shader cannot take is left exactly as it was, and the report says why.

**KS ANTI-RIP: a long key, synced or not.** With "Long key" (on by default), a key sent to the other players is
lengthened with the synced parameter bits your avatar has left, up to a 128-bit lock on the meshes their shader closes,
so the avatar file alone is not enough to open them. It only takes bits nobody uses: your own parameters keep their room
and the upload never goes over VRChat's limit. Someone in your instance with a modified client can still read a synced
key. The laid-out textures now follow the whole key too, instead of a part of it. Needs the app update (engine 5.6).

**KS ANTI-RIP: a protection report.** Every protected upload now says, part by part, what each mesh got (closed by its
shader, restore shapes, or nothing) and why, which textures were laid out or left readable and why, and how long the key
is. It is printed in the Unity console and shown on the app's KS Anti-Rip page.

## [3.16.1]

**KS ANTI-RIP: people whose Safety blocks your animations see the Kawaii Studio poster, not a field of shards.**

- A viewer who blocks the wearer's animations (VRChat Safety) never runs the avatar's FX layer, so they saw the
  scrambled meshes exactly as saved. The protected upload is now saved in its locked look: every mesh, light and
  sound the lock hides is saved off, and the lock turns them back on once the key is here. The poster is saved on.
- Nothing changes for anyone whose animations run, you included: the lock already switched all of these both ways.
- Parts your own avatar toggles itself are still left alone, so your menu toggles keep working with the key.
- Only when "Hide while locked" is on (the default).

**KS ANTI-RIP: closing meshes with their own shader is now on by default.** Meshes it cannot close (not a locked
Poiyomi or a lilToon) keep the usual method, as before.

**KS ANTI-RIP: a local key is a 128-bit key.** With "Others see it unlocked" off, the key never leaves your PC and
costs none of VRChat's synced parameters, so the Kawaii Studio app (engine 5.5) now makes it long enough that the
meshes the shader closes cannot be guessed from the avatar file. Needs the app update; with an older app the key
stays as it was.

## [3.16.0]

**KS ANTI-RIP: lilToon meshes are closed by their own shader too, like locked Poiyomi.**
Nothing changes unless "Close Poiyomi meshes with their own shader" is on in the app; that same setting now covers
lilToon as well. PC only, as before.

- **lilToon materials** (standard, cutout, transparent, outline, gem, fur, fur-only, refraction, overlay, fake shadow,
  Lite and Multi) now take the shader method: the mesh only comes back with the key, and the main texture is laid out
  like a Poiyomi one. The outline follows the laid-out texture when it wears the main texture.
- Your lilToon package and your materials are never changed: the protection works on copies made for the upload, and
  lilToon's own optimisation at build time leaves those copies alone.
- Someone who blocks your shaders sees the Kawaii Studio poster instead of those meshes, exactly like Poiyomi.
- **Not covered yet** (those meshes keep the usual restore method, safely): lilToon tessellation shaders, a material
  whose ID mask reads UV7, and lilToon's own avatar encryption.
- lilToon's `_BaseMap` / `_BaseColorMap` slots (kept for other render pipelines, never drawn here) are cleared on the
  protected copy, so they no longer ship the original texture.

## [3.15.0]

**KS ANTI-RIP: a protected upload marks the avatar's VRChat description so recovery tools skip it.**

- After VRChat accepts a protected upload, three lines are added to the avatar's description, after your own
  text: `Protected by Kawaii Studio`, `ARC BLACKLIST` (what avatar-recovery services read to leave an avatar
  alone) and `vrcdp_blacklist` (the same for VRCDataPack). A line already there is not added again, and your
  own text is never changed or cut.
- If VRChat refuses the description change, it is only reported in the app log; the upload itself is untouched.

## [3.14.2]

**KS ANTI-RIP: the developer test suites no longer ship with the package.** No change to what protection does.

- The six self-test / proof files are no longer part of the package: they do not run on anyone's avatar, and
  keeping them out leaves less of the method's design in the shipped source. The protection itself is unchanged.
- The README's KS Anti-Rip section is shorter and no longer walks through how the scramble is undone.

## [3.14.1]

**KS ANTI-RIP: a laid-out texture no longer ships readable through another slot of the same material.**
Fixes 3.14.0. Nothing changes unless "Close Poiyomi meshes with their own shader" is on.

- **A second slot holding the same texture.** 3.14.0 laid out the main texture of a protected material but left its
  other slots as they were. When one of them held that same texture, the original still shipped in the avatar, while
  the upload said the texture was laid out. Seen on a real avatar: a claws material whose matcap mask is its own base
  colour.
- **Old slots from a previous shader** (for example `_BaseColorMap` or `_BaseMap` left behind by URP or lilToon). The
  shader never reads them, but they still ship. They are now cleared on the protected copy, so the texture can be laid
  out. Your own materials are never changed.
- **A slot the shader really reads** (like that matcap mask) keeps the texture readable, and the upload now says so:
  laying it out would have shown the mosaic to the people you allow.
- Last check before the upload: a texture still held anywhere it would ship readable is put back and reported, so
  "laid out" now always means the original does not ship.

## [3.14.0]

**KS ANTI-RIP: meshes and textures closed by the avatar's own shader (optional, PC).**
Works with the Kawaii Studio app from v668 (v667 closes the meshes but not the textures). `com.kawaiistudio.link`
stays 1.2.1. Off by default: nothing changes until you turn it on in the app.

- **A new switch, "Close Poiyomi meshes with their own shader".** The parts of a mesh whose every material is a locked
  Poiyomi shader are closed at upload by a copy of that shader, patched for the purpose. What puts them back is not in
  the avatar file at all: the shader computes it from your key while the avatar is worn. The other parts of the same
  mesh keep the usual restore shapes, so a body with a Poiyomi skin and lilToon clothes is protected all over.
- **Their main textures too.** The colour texture of those materials is cut into blocks that change place, turn and
  swap colour channels, all from the key; the shader puts it back as it reads it. A ripped copy shows a mosaic. A texture
  that another, unprotected part of the avatar also uses stays as it is (it would ship readable anyway).
- **Whoever blocks your shaders** does not see those parts (VRChat's fallback is set to Hidden), and sees a Kawaii
  Studio poster on your chest instead. With shaders shown nobody sees that poster.
- **PC only.** An Android or iOS upload keeps the usual method whatever the switch says.
- A renderer whose materials your own animations swap keeps the usual method (the swapped-in material is not a patched
  one).
- Every pass of a patched shader is compiled before the upload goes on; a shader that will not take the change keeps
  the usual method for its mesh, and one that will not take the texture change keeps its texture readable. Neither
  stops the upload.

Known limits: lilToon materials are not closed this way (they keep restore shapes); Poiyomi 7.x keeps its texture
readable; the laid-out texture loses a little sharpness at a distance (box-filtered mip maps, no anisotropic filtering),
not visible at normal size.

## [3.13.1]

**KS ANTI-RIP: the poster is in front of the wearer's eyes.**
Works with the Kawaii Studio app from v657; v665 makes the key arrive after the app updates itself.
`com.kawaiistudio.link` stays 1.2.1.

- The poster now hangs on the head, about 30 cm in front of the eyes (sized to the avatar): someone wearing the avatar
  without its key reads "ask me if you want to use this avatar" straight in front of them, wherever they look, and
  everyone else reads it where the face would be. A VRC Head Chop keeps it at full size in first person (VRChat
  otherwise shrinks everything on the head for the wearer). Up close it turns fully to the camera, so it reads
  straight on; from further away it stays upright as before. Needs VRChat SDK 3.5.2 or newer for the head; with an
  older SDK, or an avatar without a humanoid head, it stays at the same place without following the head.

## [3.13.0]

**KS ANTI-RIP: a locked avatar is a poster and a statue.**
Works with the Kawaii Studio app from v657 (v664 for the upload log). `com.kawaiistudio.link` stays 1.2.1.

- **The poster alone.** The 3D heart is gone: it stood at eye level in front of the wearer and filled their own view.
  The poster with the words and the logo stays, centred in front of the chest, and now reads the right way in a
  mirror too (it came out back to front there).
- **Everything hidden while locked.** With "Hide it until it is unlocked" on, up to 3.12 only the scrambled skinned
  meshes were hidden: static meshes, particle effects, trails, lights and sounds stayed on and kept playing around an
  invisible body. Now every renderer, light and sound of the avatar is off until the key arrives, and back exactly as
  it was afterwards.
- **A real statue when frozen.** On top of the position lock and the menu, the freeze now stops hands, breathing and
  emotes (the Gesture, Additive and Action layers), every Animator inside the avatar, and every particle emitter.
  Everything comes back when the key arrives.
- **Toggles are never broken.** Anything the avatar's own animations switch (a jacket toggle, a particle toggle) is
  left to the avatar, so it keeps working for the people who have the key. The app's log says how many such parts
  stay running while locked.
- **New test: the upload hook, on and off.** With protection off (no mark, or the mark switched off in the app) the
  upload copy comes out exactly as it went in and nothing is written to the project; with it on, the copy is
  protected and the build files are gone afterwards. `Kawaii Studio > VRC > Debug > KS Anti-Rip upload hook test`.
- The avatar's name reaches the app without the SDK's "(Clone)".

## [3.12.2]

**KS ANTI-RIP: a protected upload with the locked badge is no longer refused by the VRChat SDK.**
Works with the Kawaii Studio app from v657; v664 shows Unity's side of the upload in its KS Anti-Rip log.
`com.kawaiistudio.link` stays 1.2.1.

- Since 3.12.0, every protected upload with the badge on stopped with "Avatar validation failed". The VRChat SDK
  refuses any texture with mip maps but without "Streaming Mip Maps", and the badge's picture came into each project
  with Unity's default import settings, which are exactly that. Its "Auto Fix" could not help: only the uploaded copy
  has the badge, not the avatar in the scene. Each upload now uses its own copy of the picture, imported the way the
  SDK accepts it, and deleted with the rest of the build's files. If that copy cannot be made, the badge is left out
  with a note and the avatar is still protected.
- Unity now tells the Kawaii Studio app how a protected upload went: protected (with any notes), then accepted by
  VRChat, or stopped by the VRChat SDK with the SDK's own reasons. Until now those reasons were only in Unity's console,
  printed after the "Avatar validation failed" line.

## [3.12.1]

**KS ANTI-RIP no longer stops an upload because the Kawaii Studio app was restarted.**
Same Kawaii Studio app as 3.12.0; `com.kawaiistudio.link` stays 1.2.1.

- Every update of the Kawaii Studio app restarts it, and the first protected upload after that stopped with
  "Kawaii Studio restarted. Try again.": Unity still held its connection to the app that had closed. Unity now connects
  again by itself at the start of the protection and carries on. The same when the app was closed and reopened.
- A restart of the app in the middle of a protection still stops that upload: the plan it was following is gone.

## [3.12.0]

**KS ANTI-RIP: a locked avatar shows a badge, cannot walk, and its menu does nothing.**
Works with the Kawaii Studio app from v657; v660 adds the two switches under Settings. `com.kawaiistudio.link` stays 1.2.1.

- **The locked badge.** Someone wearing a protected avatar without its key used to be an empty space. Now one object
  stands in its place: a 3D pink heart that turns, beats and glows, and in front of it a panel, "ANTI-RIP PROTECTION —
  ask me if you want to use this avatar — powered by Kawaii Studio". The panel always faces whoever looks at it, the
  heart has its own halo and shines more in worlds with bloom, and the whole thing is switched off the moment the key
  arrives. It costs one mesh, two materials and about 1,900 triangles, and is added to the build only.
- **Frozen while locked.** Worn without its key for more than 5 seconds, the avatar stops its wearer walking (the way
  GoGo Loco's stationary option does) and its own FX layers weigh nothing, so its menu toggles do nothing. When the
  key arrives later, everything is put back.
- **People you allow never notice.** Their key arrives within a second or two, well inside the 5 seconds, and in that
  case nothing at all is applied to the avatar — no layer weight, no locomotion setting is touched.
- Both are on by default and are settings of the avatar's protection (`lockedBadge`, `freezeWhileLocked`).
- Honest limit: the freeze, like the hiding, reacts to "a key is present", not "the key is right" — testing for the
  right value would write it into the bundle. Someone who sets the key parameters to anything over OSC walks again, in
  an avatar that is still scrambled. Layer 0 of the FX controller cannot be blocked (Unity always plays it in full).

## [3.11.1]

**KS ANTI-RIP no longer leaves its build files in your project.**
Same Kawaii Studio app as 3.11.0; `com.kawaiistudio.link` stays 1.2.1.

- Every protected upload left a folder `Assets/Kawaii Studio/Protected/_build_…` behind, holding the scrambled meshes
  and clips made for that build — 812 MB on a real avatar, and one more folder per upload. They are only needed while
  the VRChat SDK packs the bundle. They are now deleted as soon as the SDK has finished building (the same moment
  Modular Avatar and NDMF delete their own temporary assets), and anything an earlier or failed build left there is
  cleared at the start of the next protected build.
- Only the `_build_…` folders this tool makes are removed. Nothing else under `Protected` is touched, and a project
  that does not use KS Anti-Rip gets no folder created.

## [3.11.0]

**KS ANTI-RIP: the last readable names are gone — materials, textures, meshes and sounds ship under random names.**
Same Kawaii Studio app as 3.10.0 (v653 or later); `com.kawaiistudio.link` stays 1.2.1.

- A ripped protected avatar used to read like a parts list: its hierarchy and clips were already random, but its
  materials, textures, meshes and sounds kept their names ("Body_Skin", "Lingerie_Lace", "Scream"…). With name
  obfuscation on (the default), they now ship under random names too. Measured on a real avatar: 311 materials,
  501 textures, 104 meshes and 34 sounds renamed; what is left readable is Unity's own built-in assets (Cube, Sphere,
  Default-Particle…) and baked `.asset` meshes of your project, which are never touched.
- **Your project is never modified.** Materials ship as renamed copies made for that build; textures, meshes and
  sounds are renamed only in memory while the SDK builds, and get their names back as soon as it finishes. Verified:
  every file the avatar uses is byte-identical after a protected build, and every asset has its own name back.
- Nothing changes in game: the same 220 menu toggles, radials, gestures, contacts and PhysBones were checked plain
  against protected, and every material slot samples the same textures.

## [3.10.2]

**KS ANTI-RIP: a protected avatar now closes exactly in game, and GoGo Loco keeps its states.**
Same Kawaii Studio app as 3.10.0 (v653 or later); `com.kawaiistudio.link` stays 1.2.1.

- **Fixes meshes that came back slightly crumpled even with the key.** A protected mesh kept the Mesh Compression of
  the model it came from (Medium or High on many FBX imports), and Unity rounds a compressed mesh's vertex positions
  when it builds the bundle — so the scrambled vertices, spread over metres, shipped rounded to the centimetre.
  Measured on a real 30-mesh avatar: 3 meshes came back up to 1.5 cm off with the right key. Scrambled meshes now
  ship uncompressed, and every mesh restored from the uploaded bundle itself is within 0.01 mm of the original.
- **Fixes GoGo Loco (and any controller whose sub-state machine starts in a nested one) entering the wrong state.**
  The animator copy lost a sub-state machine's default state when that state lived one level deeper, so two GoGo Loco
  sub-state machines started at the wrong state once protected.
- Size, measured on that 387,000-vertex avatar: +6 MB download, +27 MB uncompressed (the restore shapes plus the
  scrambled meshes stored at full precision). An avatar already within ~30 MB of VRChat's 500 MB uncompressed limit
  should lower its size budget before protecting.
- Verified end to end on that avatar (VRCFury + Modular Avatar + GoGo Loco), plain build against protected build:
  the same objects, components and animator graphs; 220 menu toggles, radials, gestures, contacts and PhysBones give
  the same result protected as plain at every moment sampled; and the protected avatar loads hidden, then appears
  exactly as the plain one once KS OSC Key sends the key.

## [3.10.1]

**KS ANTI-RIP no longer breaks the animations of props that have their own Animator.**
Same Kawaii Studio app as 3.10.0 (v653 or later); `com.kawaiistudio.link` stays 1.2.1.

- **VRCFury effects keep working once protected.** VRCFury gives many props, particles and effects their own
  Animator, and their clips still pointed at the old object names after the hierarchy was randomized — so those
  effects did nothing on the protected avatar (about 30 on a real avatar: auras, waves, pillars, a scythe, a tail…).
  Each of them now gets its own rewritten copy. Measured on that avatar: exactly as many unresolved animation paths
  protected as unprotected.
- **The original FX controller no longer ships inside a protected avatar.** The avatar's own Animator still pointed
  at the unprotected FX, so the bundle carried every original clip and path next to the randomized ones. It now
  points at the protected copy.
- **Fixes upload errors on avatars merged by NDMF / Modular Avatar** ("does not exist in the same StateMachine", a
  broken Gesture mask): controllers are now copied object by object instead of as a file.
- An object driven by an override controller or a legacy Animation keeps its names, instead of being renamed into
  something its animations can no longer find.
- The Unity window's **Open Kawaii Studio Link** button works right after a script reload too.

## [3.10.0]

**KS ANTI-RIP protects your avatar AT UPLOAD now, without making a copy — non-destructive.**
Needs the Kawaii Studio app v653 or later. Comes with `com.kawaiistudio.link` 1.2.1 (a Link-window layout fix).

- **Turn it on, then upload normally.** On the app's **KS Anti-Rip** page each avatar has a **Protect on upload**
  switch. With it on, the avatar is protected automatically every time you upload it in the VRChat SDK — the mesh
  ships scrambled, the lock is built and the key is written at that moment. No protected prefab is made, and the
  avatar in your scene is never changed. Keep Kawaii Studio running during the upload.
- **It now reaches VRCFury and Modular Avatar toggles.** The protection runs at build time, *after* VRCFury and
  Modular Avatar merge their toggles into the avatar, so the clothing and prop animations they add are scrambled
  too. Before, a protected avatar built with those tools still shipped readable clips; it does not any more. The
  hierarchy is randomized and every animation path rewritten, while the skeleton, the viseme/eye meshes and a mesh
  named "Body" keep their names so MMD and dynamic bones still work.
- **It fits under VRChat's size limit.** The scramble is spread over only as many vertices as a size budget allows
  (default +12 MB, set per avatar), so an avatar already close to the 200 MB limit stays legal instead of gaining
  tens of MB of restore data. The restoration is still exact.
- **PC only.** KS Anti-Rip is for PC avatars — the key is sent by KS OSC Key on your PC — so an Android/Quest build
  target is now a clear error with a one-click **switch to Windows**, instead of a note that let you protect an
  avatar that could never be unlocked.
- Everything is still driven from the app; the Unity window has no controls. The old "make a protected copy"
  command remains for scripts but the app no longer uses it.

## [3.9.1]

**KS ANTI-RIP is driven from the Kawaii Studio app only.**

- The Unity window `Kawaii Studio > VRC > KS Anti-Rip` no longer has an avatar field, settings or a PROTECT
  button: every one of them is on the app's **KS Anti-Rip** page (UNITY TOOLS), which lists your linked projects
  and the avatars in their open scenes by itself. The window now says where to go.
- The app can show the protected prefab in Unity (selected and highlighted in the Project window), ready for the
  VRChat SDK's upload.
- The "save scenes" repair no longer opens Unity's Save As dialog for a scene that was never saved: it goes to
  `Assets/Kawaii Studio/Scenes` under a dated name. Scenes that belong to packages (NDMF's preview scene) are left alone.

## [3.9.0]

**KS ANTI-RIP replaces KS Obfuscator: your avatar ships scrambled, and nothing inside the bundle says how to put it back.**

Comes with `com.kawaiistudio.link` 1.2.0, and needs the Kawaii Studio app v650 or later: the protection itself is
computed in the app, and is a Premium feature (Premium, VIP and Legendary).

- **Protect from the app.** The Kawaii Studio app's new **KS ANTI-RIP** section lists your linked Unity projects and
  the avatars in their open scenes; pick one and press PROTECT. The same is available in Unity under
  `Kawaii Studio > VRC > KS Anti-Rip`. Either way the app does the thinking, and KS Unity Tools -- which you can read
  -- only applies what it is given, so the method is not published with the source.
- **The key is not in the bundle.** Each restore blend shape follows one key value, and the animator only says
  "this shape follows this parameter". The values live in a per-avatar `.key` on your PC and **KS OSC KEY** in the
  app sends them into VRChat while you wear the avatar. Opening the bundle and reading its animator gives the
  values away no more than reading the mesh does.
- **Others see you normally**, by default: the key values are synced, and they are chosen on VRChat's own sync
  precision, so what other players receive is exactly what was sent. Or keep the key **on your PC only**: nobody
  can read it, and nobody but you sees the avatar whole. Your choice, per protection.
- **The repair cannot be undone one slider at a time.** Every restore shape moves every vertex, and only the whole
  set at the right values cancels out. Measured on a real 64k-vertex avatar: shipped 2.5 m out of place on average,
  every slider at 100 still 3-4 m out, the best single value for all of them still 1.3 m out, solving the shapes one
  by one still 1.4 m out -- and the key puts it back to 0.0003 mm, including after VRChat's network rounding.
- **Checks before it starts, and repairs what it can.** Your VRChat account signed in inside Unity (one-click
  reconnect when your credentials are saved), upload permission, VRChat confirming the avatar is yours, play mode,
  unsaved scenes, the Blueprint ID, an Animator, a zero scale, a nested avatar, free synced parameters, a missing
  expressions menu, an avatar that is already protected, very heavy meshes, and Force Text projects whose avatar
  is too heavy for Unity to save as text. Every repair is one Ctrl+Z.
- **No shader is touched.** The repair is carried by blend shapes, so Poiyomi, lilToon and the Quest mobile shaders
  all work. (1.x regex-patched shader source and broke most of them.)
- **Non-destructive, and tested as such**: everything happens on a copy; the source avatar's files are hashed
  before and after and must be byte-for-byte identical.
- **Not archived by VRChat Archive.** A protected avatar carries an empty, inactive `Kawaii Studio Bundle` object,
  and the archive refuses those, auto-archiver and manual upload alike.
- Keys live in Kawaii Studio's own Keys folder (the app's KS Anti-Rip page opens it), outside your Unity project,
  so sharing a project cannot leak them.
- Honest about its limits: a downloaded bundle is scrambled geometry, and its animator holds no answer. It is not
  unbreakable -- the repair is linear, so a solver written against this scheme can still be pointed at a bundle --
  and with the key synced, someone in your instance with a modified client can read it. No client-side protection
  avoids that. The old "Advanced Military Grade" banner is gone.
- Requires an avatar that has been uploaded once (the key is named after its Blueprint ID).
- **Kawaii Studio Link 1.2.0**: other Kawaii Studio packages can answer the app's questions without the Link having
  to know about them (`KSLinkTools`), which is how KS ANTI-RIP talks to the app.

## [3.8.0]

**The Kawaii Studio Link 1.1.0: every open project links by itself, and an AI linked through the Kawaii Studio app works on them -- it looks, and it edits.**

- Comes with `com.kawaiistudio.link` 1.1.0 (KS Unity Tools depends on it). With the Kawaii Studio app v638 or later, an AI that uses the app's local MCP server (such as Capri, when Claude Code is her AI) sees the open scenes, the hierarchy, an object's components and their values, the selection, the Console's errors and warnings, finds assets and reads the project's scripts and shaders -- and, with the app's editing switch on, writes and fixes scripts, builds and sets up objects, components, materials and assets, saves, plays and stops.
- No more Connect click: a project opened in Unity links by itself whenever Kawaii Studio runs (Disconnect still keeps one apart). With several projects open, you tell the AI which one you want to work on, in your own words.
- Each change is one Ctrl+Z in Unity, rewritten files are backed up first (30 copies, 10 MB at most), deleted assets go to the Recycle Bin. See the Link's changelog for the details.

## [3.7.0]

**Texture Asset To PNG: the exact textures of a repaired project, editable again.**

- A VRChat Archive full repair keeps compressed and HDR textures as `.asset` files that hold the original bundle's data, so they look exactly like the original. They cannot be opened in an image editor.
- **New window `Kawaii Studio > Universal Tools > Texture Asset To PNG`** (also in the Studio Manager, and on the right-click menu of a texture: `Kawaii Studio > Convert Texture Asset to PNG`): lists the project's `.asset` textures and swaps the ones you pick for an editable image.
- The image comes from the project's `Editable Textures` folder (the repair's ripped images), with their original import settings. Without that folder, the tool decodes the texture itself: PNG, or EXR for an HDR texture; a normal map is rebuilt as a regular RGB normal map.
- **Materials stay linked**: the image takes the texture's GUID and the references are rewritten for an imported image. Nothing to reassign.
- **Back to exact** (same window, or right-click `Kawaii Studio > Texture Back to Exact`) puts the original `.asset` back. Nothing is deleted: the `.asset` waits in `Assets/Kawaii Studio/Backups~/Texture Asset To PNG`, and your edited image goes there when you go back.
- Unity re-compresses an image when it imports it, so a converted texture is no longer byte-identical to the original. Measured on two repaired avatars (32 textures): 0 to 20 % of the pixels move by one step (out of 255) or more, 0.66 step on average for the worst texture.
- Tested on two repaired avatar projects (DXT1, DXT5, crunched, RGBAHalf, normal maps, a cubemap): every material slot stays linked, the console stays clean, and after Back to exact the project is byte-identical to before.

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
