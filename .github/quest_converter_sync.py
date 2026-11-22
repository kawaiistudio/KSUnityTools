#!/usr/bin/env python3
"""Regenerate QuestConverter/Engine from an upstream VRCQuestTools package.

KS Quest Converter's conversion engine is VRCQuestTools by kurotu (MIT), carried here under our own names so it
installs with KS Unity Tools and never collides with a VRCQuestTools the user already has:

  - namespace KRT.VRCQuestTools            -> KawaiiStudio.QuestConverter
  - assemblies VRCQuestTools[-Editor...]   -> KawaiiStudio.QuestConverter[.Editor...]
  - defines VQT_*                          -> KSQ_*
  - shaders Hidden/VRCQuestTools/*         -> Hidden/KawaiiStudio/QuestConverter/*
  - every .meta GUID                       -> md5("com.kawaiistudio.questconverter::" + upstream GUID), so the same
                                              upstream asset always gets the same GUID here (scenes that use our
                                              components keep working across syncs) and never the upstream one
  - menus, component menus, settings file, output folder, temp folder, NDMF plugin id, Harmony id, logger prefix
  - the upstream update check is off (KS Unity Tools is updated by the Kawaii Studio app)
  - our fixes (see KS_PATCHES) are applied on top, each one must match exactly once or the sync stops

The upstream LICENSE.txt (MIT, kurotu) and NOTICE.txt (lilToon, MIT) are kept in Engine/. astcenc binaries are not
carried (the engine then uses Unity's own ASTC compressor).

Usage:
    python .github/quest_converter_sync.py <upstream package dir> [--commit <upstream commit>]
    e.g. python .github/quest_converter_sync.py C:/umlab/vrcqt_lab/src/Packages/com.github.kurotu.vrc-quest-tools
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "QuestConverter", "Engine")
NS_OLD, NS_NEW = "KRT.VRCQuestTools", "KawaiiStudio.QuestConverter"
OUR_EDITOR_ASM = "KawaiiStudio.QuestConverter.KS.Editor"   # our own layer (QuestConverter/Editor), sees the internals

ASM_MAP = {
    "VRCQuestTools": "KawaiiStudio.QuestConverter",
    "VRCQuestTools-Editor": "KawaiiStudio.QuestConverter.Editor",
    "VRCQuestTools-Editor-Ndmf": "KawaiiStudio.QuestConverter.Editor.Ndmf",
    "VRCQuestTools-Editor-Ndmf-Unsupported": "KawaiiStudio.QuestConverter.Editor.Ndmf.Unsupported",
    "VRCQuestTools-Editor-NonDestructive": "KawaiiStudio.QuestConverter.Editor.NonDestructive",
}
DROP_REFS = {"VRCQuestTools-Analyzers"}
SKIP_TOP = {"package.json", "package.json.meta", "README.md", "README.md.meta"}
SKIP_DIRS = {os.path.join("Editor", "Tools")}          # astcenc (binaries are not in the upstream repo either)
TEXT_EXT = {".cs", ".asmdef", ".shader", ".cginc", ".compute", ".po", ".json", ".txt", ".md", ".meta", ".hlsl"}

# (file relative to Engine/, regex, replacement, expected count). Applied AFTER the generic renames.
# Bump whenever one of our patches changes what the engine bakes: the texture cache key holds the engine version
# (texture_{Version}_...), so a bump makes every avatar re-bake instead of reusing an image made by the old code.
KS_ENGINE_REV = 1

KS_PATCHES = [
    ("Editor/VRCQuestTools.cs", r'public const string Version = "(\d+\.\d+\.\d+)";',
     lambda m: f'public const string Version = "{m.group(1)}-ks.{KS_ENGINE_REV}";', 1),
    ("Editor/VRCQuestTools.cs", r'public const string Name = "VRCQuestTools";',
     'public const string Name = "KS Quest Converter";', 1),
    ("Editor/VRCQuestTools.cs", r'internal const string GitHubRepository = "kurotu/VRCQuestTools";',
     'internal const string GitHubRepository = "kawaiistudio/KSUnityTools";', 1),
    ("Editor/VRCQuestTools.cs", r'internal const string BoothURL = "https://kurotu\.booth\.pm/items/2436054";',
     'internal const string BoothURL = "https://github.com/kawaiistudio/KSUnityTools";', 1),
    ("Editor/VRCQuestTools.cs", r'internal static string VPMRepositoryURL = "https://kurotu\.github\.io/vpm-repos/vpm\.json";',
     'internal static string VPMRepositoryURL = "https://kawaiistudio.github.io/KSUnityTools/index.json";', 1),
    ("Editor/VRCQuestTools.cs", r'internal static string DocsURL = "https://kurotu\.github\.io/VRCQuestTools/";',
     'internal static string DocsURL = "https://github.com/kawaiistudio/KSUnityTools";', 1),
    ("Editor/VRCQuestTools.cs", r'internal static string PackageName = "com\.github\.kurotu\.vrc-quest-tools";',
     'internal static string PackageName = "com.kawaiistudio.ksunitytools";', 1),
    # AssetRoot = the folder of Engine/LICENSE.txt (upstream: the folder of package.json, which is not carried)
    ("Editor/VRCQuestTools.cs", r'private const string PackageJsonGUID = "[0-9a-f]{32}";',
     'private const string PackageJsonGUID = "@LICENSE_GUID@";', 1),
    ("Editor/Menus/VRCQuestToolsMenus.cs", r'internal const string RootMenu = "Tools/" \+ VRCQuestTools\.Name \+ "/";',
     'internal const string RootMenu = "Kawaii Studio/VRC/Quest Converter Tools/";', 1),
    ("Editor/Menus/VRCQuestToolsMenus.cs", r'private const string MenuPrefix = "GameObject/VRCQuestTools/";',
     'private const string MenuPrefix = "GameObject/Kawaii Studio/Quest Converter/";', 1),
    ("Runtime/Logger.cs", r'private const string Prefix = "\[VRCQuestTools\] ";',
     'private const string Prefix = "[KS Quest Converter] ";', 1),
    ("Editor/NDMF/VRCQuestToolsNdmfPlugin.cs", r'public override string DisplayName => "VRCQuestTools";',
     'public override string DisplayName => "KS Quest Converter";', 1),
    ("Editor/NDMF/VRCQuestToolsNdmfPlugin.cs", r'public override string QualifiedName => "com\.github\.kurotu\.vrc-quest-tools";',
     'public override string QualifiedName => "com.kawaiistudio.questconverter";', 1),
    ("Editor/NDMF/Passes/RemoveVRCQuestToolsComponentsPass.cs", r'"Remove VRCQuestTools components"',
     '"Remove KS Quest Converter components"', 1),
    ("Editor/NDMF/Compat/VRCFuryMaterialRemovalPatch.cs", r'"com\.github\.kurotu\.vrc-quest-tools\.vrcfury-compat"',
     '"com.kawaiistudio.questconverter.vrcfury-compat"', 1),
    ("Runtime/Components/VRCQuestToolsEditorOnly.cs",
     r'\[Icon\("Packages/com\.github\.kurotu\.vrc-quest-tools/Runtime/Images/VRCQuestTools-Icon\.png"\)\]',
     '[Icon("Packages/com.kawaiistudio.ksunitytools/References/logo.png")]', 1),
    ("Editor/Models/VRCQuestToolsSettings.cs", r'Path\.Combine\("Library", "VRCQuestTools", "Cache", "TextureCache"\)',
     'Path.Combine("Library", "KawaiiStudioQuestConverter", "Cache", "TextureCache")', 1),
    ("Editor/NonDestructive/VRCQuestToolsAvatarProcessor.cs", r'EditorUtility\.DisplayDialog\("VRCQuestTools", ',
     'EditorUtility.DisplayDialog(VRCQuestTools.Name, ', 1),
    ("Editor/Views/VRCQuestToolsSettingsProvider.cs", r'label = "VRCQuestTools",', 'label = "Quest Converter",', 1),
    # no update check against the upstream project
    ("Editor/Automators/UpdateCheckerAutomator.cs",
     r'#if !KSQ_HAS_NEWTONSOFT_JSON\r?\n(\s*)Logger\.LogWarning\(\$"Newtonsoft Json package is not installed\. Update checker is not working\."\);\r?\n',
     '#if true // KS: no update check against the upstream project (KS Unity Tools is updated by the Kawaii Studio app)\n', 1),
    # KS fix: on a shader the engine does not know, emission ON without an _EmissionMap (masked emission under another
    # name, e.g. KS Hair Realistic's _EmissionMask) added the emission colour to the WHOLE surface (the bake's default map
    # is white): measured 11-22 on LAYLA WENDIGO, deep blue hair -> pale cyan. Now: the shader's own emission mask is
    # used, and no mask / emission switched off by the shader = no emission baked.
    ("Editor/Models/Unity/MaterialBase.cs",
     r'(\r?\n)(\s*)// Check platform override settings from source textures used in baking',
     lambda m: (m.group(1) + m.group(2) + "// KS: masked emission on unknown shaders (see .github/quest_converter_sync.py)"
                + m.group(1) + m.group(2) + "if (GetType() == typeof(StandardMaterial)) KsBakeEmission(baker.Object, disposables);"
                + m.group(1) + m.group(1) + m.group(2) + "// Check platform override settings from source textures used in baking"), 1),
    ("Editor/Models/Unity/MaterialBase.cs",
     r'(\r?\n)(\s*)internal virtual AsyncCallbackRequest GenerateToonLitImage\(',
     lambda m: (m.group(1) + m.group(2) + "// KS: what the unknown shader really emits, for the Toon Lit bake (vqt_standard adds _EmissionMap x _EmissionColor)"
                + m.group(1) + m.group(2) + "private void KsBakeEmission(Material baker, CompositeDisposable disposables)"
                + m.group(1) + m.group(2) + "{"
                + m.group(1) + m.group(2) + "    if (!baker.IsKeywordEnabled(\"_EMISSION\"))"
                + m.group(1) + m.group(2) + "    {"
                + m.group(1) + m.group(2) + "        // a shader with no _EMISSION keyword at all but an emission map + colour emits all the time"
                + m.group(1) + m.group(2) + "        // (measured: RaivoVFX/Standard V2, the green Undead Scythe)"
                + m.group(1) + m.group(2) + "        bool keywordShader = Material.shader != null && Material.shader.keywordSpace.keywordNames.Contains(\"_EMISSION\");"
                + m.group(1) + m.group(2) + "        if (!keywordShader && Material.HasProperty(\"_EmissionMap\") && Material.GetTexture(\"_EmissionMap\") != null"
                + m.group(1) + m.group(2) + "            && Material.HasProperty(\"_EmissionColor\") && Material.GetColor(\"_EmissionColor\").maxColorComponent > 0.01f)"
                + m.group(1) + m.group(2) + "            baker.EnableKeyword(\"_EMISSION\");"
                + m.group(1) + m.group(2) + "        return;"
                + m.group(1) + m.group(2) + "    }"
                + m.group(1) + m.group(2) + "    if (Material.HasProperty(\"_EmissionMap\") && Material.GetTexture(\"_EmissionMap\") != null) return;"
                + m.group(1) + m.group(2) + "    bool off = Material.HasProperty(\"_UseEmission\") && Material.GetFloat(\"_UseEmission\") < 0.5f;"
                + m.group(1) + m.group(2) + "    bool maskOff = Material.HasProperty(\"_UseEmissionMask\") && Material.GetFloat(\"_UseEmissionMask\") < 0.5f;"
                + m.group(1) + m.group(2) + "    string maskName = off || maskOff ? null : Material.GetTexturePropertyNames().FirstOrDefault(n => n != \"_EmissionMap\""
                + m.group(1) + m.group(2) + "        && n.IndexOf(\"emissi\", System.StringComparison.OrdinalIgnoreCase) >= 0 && Material.GetTexture(n) is Texture2D t && !TextureUtility.IsNormalMapAsset(t));"
                + m.group(1) + m.group(2) + "    if (off) { baker.DisableKeyword(\"_EMISSION\"); return; }"
                + m.group(1) + m.group(2) + "    if (maskOff) return; // the shader itself emits on the whole surface"
                + m.group(1) + m.group(2) + "    if (maskName == null) { baker.DisableKeyword(\"_EMISSION\"); return; }"
                + m.group(1) + m.group(2) + "    var src = Material.GetTexture(maskName);"
                + m.group(1) + m.group(2) + "    var mask = TextureUtility.LoadUncompressedTexture(src);"
                + m.group(1) + m.group(2) + "    if (mask != src) disposables.Add(DisposableObject.New(mask));"
                + m.group(1) + m.group(2) + "    baker.SetTexture(\"_EmissionMap\", mask);"
                + m.group(1) + m.group(2) + "    baker.SetTextureScale(\"_EmissionMap\", Material.GetTextureScale(maskName));"
                + m.group(1) + m.group(2) + "    baker.SetTextureOffset(\"_EmissionMap\", Material.GetTextureOffset(maskName));"
                + m.group(1) + m.group(2) + "}"
                + m.group(1)
                + m.group(1) + m.group(2) + "internal virtual AsyncCallbackRequest GenerateToonLitImage("), 1),
    # KS fix: on a shader the engine does not know, the colour / albedo under another name than _Color / _MainTex
    # (RaivoVFX "_MainColor", URP-style "_BaseColor" / "_BaseMap"...) was lost: white. Measured 11-22 on LAYLA WENDIGO:
    # the green Undead Scythe (RaivoVFX/Standard V2, _MainColor green) came out white.
    ("Editor/Models/Unity/MaterialBase.cs", r'var mainTexture = Material\.mainTexture \?\? Texture2D\.whiteTexture;',
     'var mainTexture = KsMainTexture() ?? Texture2D.whiteTexture;', 1),
    ("Editor/Models/Unity/MaterialBase.cs",
     r'(\r?\n)(\s*)if \(GetType\(\) == typeof\(StandardMaterial\)\) KsBakeEmission\(baker\.Object, disposables\);',
     lambda m: (m.group(1) + m.group(2) + "if (GetType() == typeof(StandardMaterial)) KsBakeEmission(baker.Object, disposables);"
                + m.group(1) + m.group(2) + "if (GetType() == typeof(StandardMaterial)) KsBakeColor(baker.Object, bakeMainTexture);"), 1),
    ("Editor/Models/Unity/MaterialBase.cs",
     r'(\r?\n)(\s*)// KS: what the unknown shader really emits',
     lambda m: (m.group(1) + m.group(2) + "private static readonly string[] KsColorNames = { \"_MainColor\", \"_BaseColor\", \"_Tint\", \"_TintColor\", \"_AlbedoColor\", \"_MainTint\", \"_DiffuseColor\" };"
                + m.group(1) + m.group(2) + "private static readonly string[] KsTextureNames = { \"_BaseMap\", \"_BaseColorMap\", \"_MainTexture\", \"_AlbedoMap\", \"_Albedo\", \"_AlbedoTex\", \"_BaseTexture\", \"_DiffuseMap\", \"_Diffuse\", \"_DiffuseTex\", \"_ColorMap\" };"
                + m.group(1)
                + m.group(1) + m.group(2) + "// KS: the albedo of an unknown shader that keeps it under another name than _MainTex"
                + m.group(1) + m.group(2) + "private Texture KsMainTexture()"
                + m.group(1) + m.group(2) + "{"
                + m.group(1) + m.group(2) + "    if (Material.mainTexture != null || GetType() != typeof(StandardMaterial)) return Material.mainTexture;"
                + m.group(1) + m.group(2) + "    foreach (var n in KsTextureNames) if (Material.HasProperty(n) && Material.GetTexture(n) is Texture2D t) return t;"
                + m.group(1) + m.group(2) + "    return null;"
                + m.group(1) + m.group(2) + "}"
                + m.group(1)
                + m.group(1) + m.group(2) + "// KS: the colour (and albedo) of an unknown shader that keeps them under other names, for the Toon Lit bake"
                + m.group(1) + m.group(2) + "private void KsBakeColor(Material baker, Texture bakeMainTexture)"
                + m.group(1) + m.group(2) + "{"
                + m.group(1) + m.group(2) + "    if (!Material.HasProperty(\"_MainTex\") || Material.GetTexture(\"_MainTex\") == null)"
                + m.group(1) + m.group(2) + "    {"
                + m.group(1) + m.group(2) + "        if (bakeMainTexture != null && bakeMainTexture != Texture2D.whiteTexture) baker.SetTexture(\"_MainTex\", bakeMainTexture);"
                + m.group(1) + m.group(2) + "    }"
                + m.group(1) + m.group(2) + "    if (Material.HasProperty(\"_Color\")) return;"
                + m.group(1) + m.group(2) + "    foreach (var n in KsColorNames)"
                + m.group(1) + m.group(2) + "    {"
                + m.group(1) + m.group(2) + "        if (!Material.HasProperty(n)) continue;"
                + m.group(1) + m.group(2) + "        Color c;"
                + m.group(1) + m.group(2) + "        try { c = Material.GetColor(n); } catch { continue; }"
                + m.group(1) + m.group(2) + "        if (c.a <= 0.001f) c.a = 1f; // an opaque shader that leaves the alpha of its colour at 0"
                + m.group(1) + m.group(2) + "        baker.SetColor(\"_Color\", c);"
                + m.group(1) + m.group(2) + "        return;"
                + m.group(1) + m.group(2) + "    }"
                + m.group(1) + m.group(2) + "}"
                + m.group(1)
                + m.group(1) + m.group(2) + "// KS: what the unknown shader really emits"), 1),
    # KS hook: our own layer (QuestConverter/Editor) converts the Kawaii Studio shaders it knows (KS Hair Realistic...)
    ("Editor/Models/Unity/MaterialWrapperBuilder.cs",
     r'(\r?\n)(\s*)private MaterialBase BuildForCategory\(Material material, ShaderCategory category\)(\r?\n)(\s*)\{',
     lambda m: (m.group(1) + m.group(2) + "/// <summary>KS: set by QuestConverter/Editor; returns a wrapper for the Kawaii Studio shaders it knows, else null.</summary>"
                + m.group(1) + m.group(2) + "internal static System.Func<Material, MaterialBase> KsWrapperHook;"
                + m.group(1)
                + m.group(1) + m.group(2) + "private MaterialBase BuildForCategory(Material material, ShaderCategory category)"
                + m.group(3) + m.group(4) + "{"
                + m.group(3) + m.group(4) + "    var ksOwn = KsWrapperHook?.Invoke(material);"
                + m.group(3) + m.group(4) + "    if (ksOwn != null) return ksOwn;"), 1),
    # KS fix: an old lilToon (before 1.10) no longer stops the whole avatar: that material is read as a generic one
    ("Editor/Models/Unity/MaterialWrapperBuilder.cs", r'return new LilToonMaterial\(material\);',
     'try { return new LilToonMaterial(material); }\n'
     '                    catch (KawaiiStudio.QuestConverter.Utils.LegacyPackageException)\n'
     '                    {\n'
     '                        // KS: an old lilToon (before 1.10) no longer stops the whole avatar\n'
     '                        Logger.LogWarning($"{material.name}: lilToon is older than 1.10, converted as a generic material.");\n'
     '                        return new StandardMaterial(material);\n'
     '                    }', 1),
]


def new_guid(old: str) -> str:
    return hashlib.md5(f"com.kawaiistudio.questconverter::{old}".encode()).hexdigest()


def path_guid(unity_rel: str) -> str:
    return hashlib.md5(f"com.kawaiistudio.ksunitytools::{unity_rel}".encode()).hexdigest()


def folder_meta(guid: str) -> str:
    return ("fileFormatVersion: 2\nguid: " + guid + "\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n"
            "  userData: \n  assetBundleName: \n  assetBundleVariant: \n")


def text_meta(guid: str) -> str:
    return ("fileFormatVersion: 2\nguid: " + guid + "\nTextScriptImporter:\n  externalObjects: {}\n"
            "  userData: \n  assetBundleName: \n  assetBundleVariant: \n")


def map_asm(name: str) -> str:
    if name in ASM_MAP:
        return ASM_MAP[name]
    if name.startswith("VRCQuestTools"):
        return "KawaiiStudio.QuestConverter" + name[len("VRCQuestTools"):].replace("-", ".")
    return name


def rewrite_asmdef(text: str) -> str:
    data = json.loads(text.lstrip("\ufeff"))
    data["name"] = map_asm(data["name"])
    if data.get("rootNamespace", "").startswith(NS_OLD):
        data["rootNamespace"] = NS_NEW + data["rootNamespace"][len(NS_OLD):]
    data["references"] = [map_asm(r) for r in data.get("references", []) if r not in DROP_REFS]
    vds = data.get("versionDefines", [])
    for vd in vds:
        vd["define"] = re.sub(r"\bVQT_", "KSQ_", vd["define"])
    # only in Unity 2022.3+ with the VRChat Avatars SDK 3.9+: KS Unity Tools itself still installs on 2019.4 / worlds
    vds.append({"name": "com.vrchat.avatars", "expression": "3.9.0", "define": "KSQ_VRCSDK_AVATARS"})
    data["versionDefines"] = vds
    dc = [re.sub(r"\bVQT_", "KSQ_", c) for c in data.get("defineConstraints", [])]
    for c in ("UNITY_2022_3_OR_NEWER", "KSQ_VRCSDK_AVATARS"):
        if c not in dc:
            dc.append(c)
    data["defineConstraints"] = dc
    return json.dumps(data, indent=4, ensure_ascii=False) + "\n"


def rewrite_text(rel: str, text: str, guid_map: dict[str, str]) -> str:
    if guid_map:
        text = re.sub(r"\b[0-9a-f]{32}\b", lambda m: guid_map.get(m.group(0), m.group(0)), text)
    if rel.endswith(".asmdef"):
        return rewrite_asmdef(text)
    if rel.endswith(".meta"):
        return text
    text = re.sub(r'InternalsVisibleTo\("(VRCQuestTools[A-Za-z-]*)"\)',
                  lambda m: f'InternalsVisibleTo("{map_asm(m.group(1))}")', text)
    text = text.replace(NS_OLD, NS_NEW)
    text = re.sub(r"\bVQT_", "KSQ_", text)
    text = text.replace("Hidden/VRCQuestTools/", "Hidden/KawaiiStudio/QuestConverter/")
    text = re.sub(r'AddComponentMenu\("VRCQuestTools/VQT ([^"]+)"\)', r'AddComponentMenu("Kawaii Studio/Quest Converter/\1")', text)
    text = text.replace('"Assets/VRCQuestToolsOutput/', '"Assets/Kawaii Studio/Quest Converter/')
    text = text.replace('"ProjectSettings/VRCQuestToolsSettings.json"', '"ProjectSettings/KawaiiStudioQuestConverter.json"')
    text = text.replace('"Temp/VRCQuestTools/', '"Temp/KawaiiStudioQuestConverter/')
    text = text.replace('"Project/VRCQuestTools"', '"Project/Kawaii Studio/Quest Converter"')
    # the engine's own mesh file type: one extension = one importer in the whole project, an installed VRCQuestTools
    # would take ours (measured 11-22: "Importer at path is not MeshModifierImporter")
    text = text.replace('"vqtmesh"', '"ksqmesh"').replace(".vqtmesh", ".ksqmesh")
    if rel.endswith(".po"):
        text = "\n".join(l.replace("VRCQuestTools", "KS Quest Converter") if l.startswith("msgstr") else l
                         for l in text.split("\n"))
    return text


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("upstream")
    ap.add_argument("--commit", default="")
    a = ap.parse_args()
    src = os.path.abspath(a.upstream)
    with open(os.path.join(src, "package.json"), encoding="utf-8-sig") as fh:
        up = json.load(fh)
    if up.get("name") != "com.github.kurotu.vrc-quest-tools":
        print("not the VRCQuestTools package:", up.get("name"))
        return 1

    files = []
    for dp, dns, fns in os.walk(src):
        rel_dir = os.path.relpath(dp, src)
        dns[:] = sorted(d for d in dns if os.path.normpath(os.path.join(rel_dir, d)) not in SKIP_DIRS)
        for fn in sorted(fns):
            rel = os.path.normpath(os.path.join(rel_dir, fn))
            if rel in SKIP_TOP:
                continue
            if any(rel.startswith(d + os.sep) or rel == d + ".meta" for d in SKIP_DIRS):
                continue
            files.append(rel)

    guid_map: dict[str, str] = {}
    for rel in files:
        if rel.endswith(".meta"):
            with open(os.path.join(src, rel), encoding="utf-8-sig") as fh:
                m = re.search(r"^guid: ([0-9a-f]{32})", fh.read(), re.M)
            if m:
                guid_map[m.group(1)] = new_guid(m.group(1))
    lic_old = None
    with open(os.path.join(src, "LICENSE.txt.meta"), encoding="utf-8-sig") as fh:
        lic_old = re.search(r"^guid: ([0-9a-f]{32})", fh.read(), re.M).group(1)

    if os.path.isdir(OUT):
        shutil.rmtree(OUT)
    os.makedirs(OUT)
    changed_text = 0
    for rel in files:
        s, d = os.path.join(src, rel), os.path.join(OUT, rel)
        # assembly definition FILES named like their assembly (no "VRCQuestTools-*.asmdef" file in the user's project)
        base = os.path.basename(rel)
        for ext in (".asmdef.meta", ".asmdef"):
            if base.endswith(ext) and base[: -len(ext)] in ASM_MAP:
                d = os.path.join(os.path.dirname(d), ASM_MAP[base[: -len(ext)]] + ext)
                break
        os.makedirs(os.path.dirname(d), exist_ok=True)
        if os.path.splitext(rel)[1].lower() in TEXT_EXT:
            with open(s, "rb") as fh:
                raw = fh.read()
            text = raw.decode("utf-8", "surrogateescape")
            out = rewrite_text(rel.replace(os.sep, "/"), text, guid_map)
            changed_text += out != text
            with open(d, "wb") as fh:
                fh.write(out.encode("utf-8", "surrogateescape"))
        else:
            shutil.copyfile(s, d)

    applied = []
    for rel, pat, rep, want in KS_PATCHES:
        p = os.path.join(OUT, rel)
        with open(p, "rb") as fh:
            text = fh.read().decode("utf-8", "surrogateescape")
        if callable(rep):
            new, n = re.subn(pat, rep, text)
        else:
            rep = rep.replace("@LICENSE_GUID@", new_guid(lic_old))
            new, n = re.subn(pat, lambda m, r=rep: r, text)
        if n != want:
            print(f"PATCH FAILED ({n} matches, want {want}): {rel}: {pat}")
            return 2
        with open(p, "wb") as fh:
            fh.write(new.encode("utf-8", "surrogateescape"))
        applied.append(rel)

    # our layer sees the engine's internals
    ai = os.path.join(OUT, "Editor", "AssemblyInfo.cs")
    with open(ai, "rb") as fh:
        text = fh.read().decode("utf-8", "surrogateescape")
    line = f'[assembly: InternalsVisibleTo("{OUR_EDITOR_ASM}")]'
    text, n = re.subn(r'(\[assembly: InternalsVisibleTo\("KawaiiStudio\.QuestConverter\.Editor\.Ndmf"\)\])',
                      lambda m: m.group(1) + "\n" + line, text, count=1)
    if n != 1:
        print("PATCH FAILED: AssemblyInfo InternalsVisibleTo")
        return 2
    with open(ai, "wb") as fh:
        fh.write(text.encode("utf-8", "surrogateescape"))
    rt_ai = os.path.join(OUT, "Runtime", "AssemblyInfo.KS.cs")
    with open(rt_ai, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("// Generated by .github/quest_converter_sync.py: our layer (QuestConverter/Editor) sees the engine's internals.\n"
                 "using System.Runtime.CompilerServices;\n\n"
                 f'[assembly: InternalsVisibleTo("{OUR_EDITOR_ASM}")]\n'
                 '[assembly: InternalsVisibleTo("KawaiiStudio.QuestConverter.Editor")]\n'
                 '[assembly: InternalsVisibleTo("KawaiiStudio.QuestConverter.Editor.Ndmf")]\n')
    with open(rt_ai + ".meta", "w", encoding="utf-8", newline="\n") as fh:
        fh.write("fileFormatVersion: 2\nguid: " + path_guid("QuestConverter/Engine/Runtime/AssemblyInfo.KS.cs") +
                 "\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n"
                 "  executionOrder: 0\n  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n")

    with open(os.path.join(OUT, "UPSTREAM.txt"), "w", encoding="utf-8", newline="\n") as fh:
        fh.write("KS Quest Converter engine = VRCQuestTools by kurotu (MIT, see LICENSE.txt; lilToon parts: NOTICE.txt)\n"
                 f"upstream version: {up.get('version')}\n"
                 f"upstream commit: {a.commit or 'unknown'}\n"
                 "https://github.com/kurotu/VRCQuestTools\n\n"
                 "GENERATED by .github/quest_converter_sync.py -- do not edit by hand: change the script and run it again.\n")
    with open(os.path.join(OUT, "UPSTREAM.txt.meta"), "w", encoding="utf-8", newline="\n") as fh:
        fh.write(text_meta(path_guid("QuestConverter/Engine/UPSTREAM.txt")))
    for folder in ("QuestConverter", "QuestConverter/Engine"):
        mp = os.path.join(ROOT, *folder.split("/")) + ".meta"
        if not os.path.exists(mp):
            with open(mp, "w", encoding="utf-8", newline="\n") as fh:
                fh.write(folder_meta(path_guid(folder)))

    # leftovers that would mean a collision with an installed VRCQuestTools
    bad = []
    for dp, _, fns in os.walk(OUT):
        for fn in fns:
            p = os.path.join(dp, fn)
            if os.path.splitext(fn)[1].lower() not in TEXT_EXT:
                continue
            with open(p, encoding="utf-8", errors="surrogateescape") as fh:
                t = fh.read()
            # (the material properties _VQT_* of our own hidden bake shaders stay: they live on our materials only;
            # the Crowdin header of the .po files is a comment)
            t2 = "\n".join(l for l in t.split("\n") if "X-Crowdin-File" not in l)
            for needle in ("KRT.VRCQuestTools", "Hidden/VRCQuestTools", "com.github.kurotu.vrc-quest-tools",
                           '"GameObject/VRCQuestTools', '"Tools/" + VRCQuestTools', "vqtmesh"):
                if needle in t2:
                    bad.append((os.path.relpath(p, OUT), needle))
            if re.search(r"\bVQT_", t2):
                bad.append((os.path.relpath(p, OUT), "VQT_ define"))
            for g in re.findall(r"\b[0-9a-f]{32}\b", t):
                if g in guid_map:
                    bad.append((os.path.relpath(p, OUT), "old guid " + g))
    for b in bad:
        print("LEFTOVER:", *b)
    print(f"files {len(files)}, text rewritten {changed_text}, guids {len(guid_map)}, patches {len(applied)}, leftovers {len(bad)}")
    return 3 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
