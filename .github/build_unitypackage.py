#!/usr/bin/env python3
"""Build KSUnityTools.unitypackage straight from the repository.

Why this exists
---------------
The .unitypackage shipped with 3.0.0 was hand-made from an OLDER source layout and was
therefore broken: it had no .asmdef files and it flattened Editor/Tools/VRC/ into
Editor/Tools/. Losing the asmdefs is fatal -- the VRC tools reference the VRChat SDK and
are only meant to compile when it is present (KawaiiStudio.VRC.Editor has
defineConstraints ["VRC_SDK_VRCSDK3"]). Dumped into Assembly-CSharp-Editor instead, they
compile unconditionally, so in a project without the SDK every editor script in the whole
project fails to compile and NOTHING in the package works.

A .unitypackage is just a gzipped tar where every asset is one directory named after its
GUID:
    <guid>/pathname     project-relative path, e.g. "Assets/Kawaii Studio/Editor/x.cs"
    <guid>/asset        the file bytes (absent for folders)
    <guid>/asset.meta   the Unity .meta, whose `guid:` MUST equal the directory name

GUID stability
--------------
This repo tracks no .meta files, so GUIDs have to be produced here. They are derived
DETERMINISTICALLY from the Unity path (md5), which matters more than it looks: a user who
re-imports a newer release keeps the same GUIDs, so their materials and scene references
survive the update. Random GUIDs would silently break every project on every release.
"""
from __future__ import annotations

import hashlib
import io
import os
import sys
import tarfile
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
UNITY_ROOT = "Assets/Kawaii Studio"

# Repo-only metadata, docs and site sources: never shipped inside Assets/.
# "build" matters more than it looks: CI writes the VCC zip there BEFORE this script runs,
# so without it the 16 MB zip gets packed inside the .unitypackage as
# Assets/Kawaii Studio/build/... (exactly what happened on the first 3.0.1 build). It never
# shows up locally, because a clean checkout has no build/ directory yet.
EXCLUDED_DIRS = {".git", ".github", "Website~", "Documentation~", "screenshots", "build", "obj", "Temp", "Library"}
EXCLUDED_FILES = {
    ".gitignore",
    # package.json is the UPM/VCC manifest. Inside Assets/ it would make Unity treat the
    # folder as an embedded package, so it must stay out of the .unitypackage.
    "package.json",
    "logo_v2.png",
    # README/landing-page art, not Unity assets. banner.png alone is 13 MB and the tools
    # treat it as optional (KawaiiStudioBranding: "Older installs shipped a 13 MB
    # banner.png; it is no longer required") -- shipping it would make the one-click
    # download 28x bigger for nothing. References/logo.png IS used and stays.
    "banner.png",
}

# Importer block per extension. Getting this right avoids a re-import dance on the user's
# machine; anything unlisted falls back to DefaultImporter, which Unity handles fine.
IMPORTERS = {
    ".cs": "MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: ",
    ".shader": "ShaderImporter:\n  externalObjects: {}\n  defaultTextures: []\n  nonModifiableTextures: []\n  preprocessorOverride: 0\n  userData: \n  assetBundleName: \n  assetBundleVariant: ",
    ".mat": "NativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 2100000\n  userData: \n  assetBundleName: \n  assetBundleVariant: ",
    ".asmdef": "AssemblyDefinitionImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: ",
    ".json": "TextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: ",
    ".md": "TextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: ",
    ".txt": "TextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: ",
}

PNG_IMPORTER = """TextureImporter:
  internalIDToNameTable: []
  externalObjects: {}
  serializedVersion: 12
  mipmaps:
    mipMapMode: 0
    enableMipMap: 1
    sRGBTexture: 1
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
  isReadable: 0
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  vTOnly: 0
  ignoreMasterTextureLimit: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: 0
    wrapV: 0
    wrapW: 0
  nPOTScale: 1
  lightmap: 0
  compressionQuality: 50
  spriteMode: 0
  spriteExtrude: 1
  spriteMeshType: 1
  alignment: 0
  spritePivot: {x: 0.5, y: 0.5}
  spritePixelsToUnits: 100
  spriteBorder: {x: 0, y: 0, z: 0, w: 0}
  spriteGenerateFallbackPhysicsShape: 1
  alphaUsage: 1
  alphaIsTransparency: 0
  spriteTessellationDetail: -1
  textureType: 0
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 0
  applyGammaDecoding: 0
  swizzle: 50462976
  cookieLightType: 0
  platformSettings:
  - serializedVersion: 3
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 1
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    physicsShape: []
    bones: []
    spriteID:
    internalID: 0
    vertices: []
    indices:
    edges: []
    weights: []
    secondaryTextures: []
    nameFileIdTable: {}
  spritePackingTag:
  pSDRemoveMatte: 0
  pSDShowRemoveMatteOption: 0
  userData:
  assetBundleName:
  assetBundleVariant: """


# GUIDs already published in the 3.0.0 .unitypackage. They MUST be reused: a GUID is an
# asset's identity in Unity, so changing one turns an update into "delete + add" -- every
# material, prefab and scene reference in the user's project silently breaks. Verified case:
# "Video Screen Overlay.mat" hard-references 94aa4309... for KSScreenShader.shader.
# The five VRC tools moved from Editor/Tools/ into Editor/Tools/VRC/; they are pinned under
# their NEW path to their OLD GUID, so Unity MOVES the file instead of duplicating it.
PINNED_GUIDS = {
    "Assets/Kawaii Studio": "3483d9c8c552e8640b33f5e863a4cf8d",
    "Assets/Kawaii Studio/CHANGELOG.md": "417cc23efcf49f58acae7da72fc180e1",
    "Assets/Kawaii Studio/Editor": "b1e2260984fb81745838d862d84f2485",
    "Assets/Kawaii Studio/Editor/Core": "ab2f53367f634d36b68557655c5d805d",
    "Assets/Kawaii Studio/Editor/Core/KawaiiStudioBranding.cs": "20b1da25e1d9cc246bf170b1b0946dda",
    "Assets/Kawaii Studio/Editor/Core/KawaiiStudioGUI.cs": "dd3d9773d0b62284a95c8ccd705f01ab",
    "Assets/Kawaii Studio/Editor/Core/KawaiiStudioLocalization.cs": "d7774fbc8a36bd3dcb705162df800bc7",
    "Assets/Kawaii Studio/Editor/Core/KawaiiStudioPaths.cs": "546fbc2c3dca5eea481024aa2100a251",
    "Assets/Kawaii Studio/Editor/Core/KawaiiStudioUtil.cs": "c22f90c21757e02d93ce65ffde50ef46",
    "Assets/Kawaii Studio/Editor/Core/KawaiiStudioVersion.cs": "310fa8fddafc0d8223319aab4586b42c",
    "Assets/Kawaii Studio/Editor/ShaderGUI": "56f4b869f35442c2baee9ccfb2e48769",
    "Assets/Kawaii Studio/Editor/ShaderGUI/KSHairRealisticGUI.cs": "d4e4ea53802817d4386af20eca5b60eb",
    "Assets/Kawaii Studio/Editor/ShaderGUI/KS_BloodKillerGUI.cs": "380ee016e4b19fe458a27ddb9895a2a0",
    "Assets/Kawaii Studio/Editor/ShaderGUI/KS_ScreenLineGUI.cs": "f943ac9238c084b46aa69a3b5933a96f",
    "Assets/Kawaii Studio/Editor/ShaderGUI/KS_VampireEyeGUI.cs": "053ec83cc7bb74846a936461b412b7ac",
    "Assets/Kawaii Studio/Editor/Tools": "ce5bdd729359496a892fd3691fc3cdc4",
    "Assets/Kawaii Studio/Editor/Tools/Kawaii Studio Exporter.cs": "7107dedd34104fe40bcf6ed8571c6911",
    "Assets/Kawaii Studio/Editor/Tools/Kawaii Studio GLB to FBX.cs": "afa00cb1c3e3af54cbd5d1f02f6fc6c5",
    "Assets/Kawaii Studio/Editor/Tools/Kawaii Studio Manager.cs": "00dd6b7fe142f1246950c9811041c6f7",
    "Assets/Kawaii Studio/Editor/Tools/Kawaii Studio Prefab Optimizer.cs": "d40c0a56204ebcb449ae0262878deca1",
    "Assets/Kawaii Studio/Editor/Tools/Kawaii Studio Video Animator.cs": "33e2de0c18e29bb408e9e756e15753a1",
    # moved into Editor/Tools/VRC/ -- keep the 3.0.0 GUIDs so updates move, not duplicate
    "Assets/Kawaii Studio/Editor/Tools/VRC/ContactScannerMenu.cs": "40fca6c6e1d93d249bbb8c2808306f03",
    "Assets/Kawaii Studio/Editor/Tools/VRC/Kawaii Studio NSFW Detector.cs": "637b8d0b307905446b5a70dabbf0f6ef",
    "Assets/Kawaii Studio/Editor/Tools/VRC/Kawaii Studio Obfuscator.cs": "cfff56e6e5a2e6b47b82ac6394b561ea",
    "Assets/Kawaii Studio/Editor/Tools/VRC/Kawaii Studio Tail Animator to PhysBones.cs": "641e3b81cf957374780fa4cae271ac8c",
    "Assets/Kawaii Studio/Editor/Tools/VRC/UltimateConstraintTool.cs": "09307a6cf6f93e44eb7de6b94592d972",
    "Assets/Kawaii Studio/Languages": "4c3d2069f80ebde48b7b60fbd34c7276",
    "Assets/Kawaii Studio/Languages/de.json": "7172c2e997b73604d8974a3bae97da25",
    "Assets/Kawaii Studio/Languages/en.json": "844dd9870a10caa458cdc69abf192e14",
    "Assets/Kawaii Studio/Languages/es.json": "8439ec8be41724145b79e2815dc83be2",
    "Assets/Kawaii Studio/Languages/fr.json": "56e1fcf0e656fc74bb31860df2279da9",
    "Assets/Kawaii Studio/Languages/ja.json": "a492a17ba66b3c540b56803426df90aa",
    "Assets/Kawaii Studio/Languages/ru.json": "e2b6cfcc0cf372547b4f6fbdd0c2ab8e",
    "Assets/Kawaii Studio/Languages/zh.json": "0dfe3049daf36a4439e0100e32eb28b5",
    "Assets/Kawaii Studio/Materials": "d4fabfec64cb467eba94b9305ffe6041",
    "Assets/Kawaii Studio/Materials/EYE SHADER.mat": "cdda1aeb6c26910469f96da586f2e802",
    "Assets/Kawaii Studio/Materials/Video Screen Overlay.mat": "e2cf59e5ca454204daa436f64f36242c",
    "Assets/Kawaii Studio/README.md": "76912952e4f7e0549adadb302aca633a",
    "Assets/Kawaii Studio/References": "33d9a364f4140bc4b94f7a3752adda73",
    "Assets/Kawaii Studio/References/logo.png": "c2e85003fad45b04d928e08ccbd5ed54",
    "Assets/Kawaii Studio/Shaders": "27299da12eac4964f9a1c905135b84c2",
    "Assets/Kawaii Studio/Shaders/BloodKiller.shader": "c29018bcd7f0a474c92364b038083648",
    "Assets/Kawaii Studio/Shaders/KSHairRealistic.shader": "8071bb636c64936429029f3b2236b59f",
    "Assets/Kawaii Studio/Shaders/KSScreenShader.shader": "94aa43094a17ef740981aaa847b57956",
    "Assets/Kawaii Studio/Shaders/KSVRStereoDisplay.shader": "c9a0cc17377dc8b4995ad99a11cf6f90",
    "Assets/Kawaii Studio/Shaders/KSVampireCrimsonEye.shader": "7e16ff4ba0833cb43b154f946a6eadb2",
    "Assets/Kawaii Studio/Shaders/KSVideoDecoder.shader": "c364119957a951549a9089de4efd4635",
    "Assets/Kawaii Studio/Shaders/KS_ScreenLine.shader": "23297783061f05b4a9eabde2f599967e",
    "Assets/Kawaii Studio/Shaders/screen_overlay.shader": "9d452ca05b653b5429147d9644b3ae51",
    "Assets/Kawaii Studio/VERSION.md": "5797272e8ed7a4341bf5f888753feb70",
}


def guid_for(unity_path: str) -> str:
    """GUID for a Unity path: the published one when it exists, otherwise a deterministic
    hash of the path so it stays identical across every future build."""
    pinned = PINNED_GUIDS.get(unity_path)
    if pinned:
        return pinned
    return hashlib.md5(f"com.kawaiistudio.ksunitytools::{unity_path}".encode("utf-8")).hexdigest()


def meta_for(unity_path: str, is_dir: bool) -> str:
    guid = guid_for(unity_path)
    if is_dir:
        body = "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: "
    else:
        ext = os.path.splitext(unity_path)[1].lower()
        if ext == ".png":
            body = PNG_IMPORTER
        else:
            body = IMPORTERS.get(
                ext,
                "DefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: ",
            )
    return f"fileFormatVersion: 2\nguid: {guid}\n{body}\n"


def collect():
    """(unity_path, disk_path_or_None) for every folder and file to ship, parents first."""
    entries: list[tuple[str, str | None]] = []
    seen_dirs: set[str] = set()

    def add_dirs(rel: str):
        parts = rel.split("/")
        for i in range(1, len(parts) + 1):
            d = "/".join(parts[:i])
            if d and d not in seen_dirs:
                seen_dirs.add(d)
                entries.append((f"{UNITY_ROOT}/{d}", None))

    entries.append((UNITY_ROOT, None))
    for dirpath, dirnames, filenames in os.walk(ROOT):
        dirnames[:] = sorted(d for d in dirnames if d not in EXCLUDED_DIRS)
        rel_dir = os.path.relpath(dirpath, ROOT).replace("\\", "/")
        if rel_dir == ".":
            rel_dir = ""
        elif rel_dir.split("/")[0] in EXCLUDED_DIRS:
            continue
        if rel_dir:
            add_dirs(rel_dir)
        for fn in sorted(filenames):
            if fn in EXCLUDED_FILES or fn.endswith(".meta"):
                continue
            rel = f"{rel_dir}/{fn}" if rel_dir else fn
            entries.append((f"{UNITY_ROOT}/{rel}", os.path.join(dirpath, fn)))
    return entries


def main() -> int:
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "build", "KSUnityTools.unitypackage")
    os.makedirs(os.path.dirname(out) or ".", exist_ok=True)
    entries = collect()

    mtime = int(os.environ.get("SOURCE_DATE_EPOCH", time.time()))

    def ti(name: str, data: bytes) -> tuple[tarfile.TarInfo, io.BytesIO]:
        t = tarfile.TarInfo(name)
        t.size = len(data)
        t.mtime = mtime
        t.mode = 0o644
        return t, io.BytesIO(data)

    n_files = n_dirs = 0
    with tarfile.open(out, "w:gz") as tar:
        for unity_path, disk in entries:
            guid = guid_for(unity_path)
            tar.addfile(*ti(f"{guid}/pathname", unity_path.encode("utf-8")))
            tar.addfile(*ti(f"{guid}/asset.meta", meta_for(unity_path, disk is None).encode("utf-8")))
            if disk is None:
                n_dirs += 1
                continue
            with open(disk, "rb") as f:
                tar.addfile(*ti(f"{guid}/asset", f.read()))
            n_files += 1

    size = os.path.getsize(out)
    print(f"{out}: {n_files} file(s), {n_dirs} folder(s), {size:,} bytes")

    # Fail the build rather than ship another package missing its assembly definitions --
    # that is the exact defect this script was written to stop repeating.
    shipped = {p for p, d in entries if d is not None}
    required = [p for p in shipped if p.endswith(".asmdef")]
    if len(required) < 2:
        print(f"::error::expected 2 .asmdef files in the package, found {len(required)}")
        return 1
    print("asmdef check: " + ", ".join(sorted(required)))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
