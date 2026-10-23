# Changelog

The Kawaii Studio Link package (`com.kawaiistudio.link`). It ships with KS Unity Tools (a dependency since 3.6.0) and installs alone in a world project.

## [1.0.0]

- The Link of KS Unity Tools 3.5.0, as its own package: the `Kawaii Studio > Kawaii Studio Link` window, pairing with the Kawaii Studio app over its local bridge, Send to Unity (imports into `Assets/Kawaii Studio/KS Exported`) and Test in Kawaii Studio (the selected avatar or the open world scene).
- **Links itself on a project the app set up**: when the Kawaii Studio app installs the Link into a project it made, it leaves `UserSettings/KawaiiStudioLink.autoconnect`; the Link reads it once on the first open, turns the link on and deletes it, so no Connect click is needed. A project where you already chose Connect or Disconnect keeps your choice.
- No VRChat SDK dependency: a world project can have it, where KS Unity Tools (avatar tools, needs the avatars SDK) cannot go. The Kawaii Studio app installs it into the Unity projects it makes (GET UNITY PROJECT).
- Same type names as in 3.5.0 (`KawaiiStudio.KawaiiStudioLink`, `KawaiiStudioTestBuild`, the headless `CommandLineServe` / `CommandLineTest` entry points), now in the assembly `KawaiiStudio.Link.Editor`.
