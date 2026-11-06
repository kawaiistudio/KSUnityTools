// Texture Asset To PNG -- make the exact textures of a repaired project editable again (3.7.0).
//
// A VRChat Archive full repair keeps every compressed / HDR texture as a NATIVE Texture2D asset (.asset) that holds
// the bundle's own bytes: byte-identical in Unity, where a PNG would be re-encoded by the importer. The ripped
// images it set aside are in "<project>/Editable Textures/", each with its original import settings in
// "<image>.meta.txt" (same GUID as the native asset).
//
// Convert: the native asset is swapped for that image (or, when there is none, for an image decoded from the asset
// itself), under the SAME GUID, and the references are written back as an imported asset's (type: 3), so every
// material stays linked. The native asset is parked in Assets/Kawaii Studio/Backups~/Texture Asset To PNG/.
// Back to exact: the image is parked (edits included, nothing is deleted) and the native asset comes back, with its
// references as a native asset's (type: 2) -- the project is then exactly what it was.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KawaiiStudio
{
    public static class KawaiiStudioTextureAssets
    {
        public const string BackupFolderName = "Texture Asset To PNG";
        public const string EditableFolderName = "Editable Textures";

        /// <summary>Asset-relative folder (ends with Backups~, so Unity never imports it).</summary>
        public static string BackupRoot => KawaiiStudioPaths.Backups + "/" + BackupFolderName;
        private static string BackupRootAbs => KawaiiStudioPaths.ToAbsolute(BackupRoot);
        private static string ManifestAbs => Path.Combine(BackupRootAbs, "converted.json");
        private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

        public enum Source { EditableTextures, Decoded, None }

        /// <summary>One native Texture2D / Cubemap asset of the project.</summary>
        public sealed class NativeTexture
        {
            public string Guid;
            public string AssetPath;          // Assets/...
            public int Width, Height, Format, MipCount, ColorSpace;
            public int FilterMode, Aniso, WrapU, WrapV, WrapW, Streaming;
            public float MipBias;
            public bool Cube;
            public string EditableImage;      // absolute: the ripped image in Editable Textures (null: none)
            public string EditableMeta;       // absolute: its "<image>.meta.txt"

            public string Name => Path.GetFileNameWithoutExtension(AssetPath);
            public Source Source => EditableImage != null ? Source.EditableTextures : (Cube ? Source.None : Source.Decoded);
            public bool Hdr => IsHdr(Format);
            public string FormatName => FormatLabel(Format);
            public override string ToString() => $"{AssetPath} ({Width}x{Height} {FormatName}, {Source})";
        }

        [Serializable]
        public sealed class Converted
        {
            public string guid;
            public string assetPath;      // where the native asset was
            public string imagePath;      // the image written in its place
            public string backupAsset;    // absolute path of the parked native asset
            public string source;         // EditableTextures / Decoded
            public string when;
        }

        [Serializable]
        private sealed class Manifest { public List<Converted> items = new List<Converted>(); }

        public sealed class Result
        {
            public readonly List<string> Done = new List<string>();
            public readonly List<string> Skipped = new List<string>();
            public readonly List<string> Errors = new List<string>();
            public string Summary(string verb) =>
                $"{Done.Count} texture(s) {verb}" + (Skipped.Count > 0 ? $", {Skipped.Count} skipped" : "") +
                (Errors.Count > 0 ? $", {Errors.Count} error(s)" : "") + ".";
        }

        // ------------------------------------------------------------------------------------------------ scan

        private static readonly Regex GuidLine = new Regex(@"^guid: ([0-9a-f]{32})", RegexOptions.Multiline);

        /// <summary>Every native Texture2D / Cubemap asset under Assets/ (not in a ~ folder), with its editable copy when
        /// the repair left one. Read-only.</summary>
        public static List<NativeTexture> Scan()
        {
            var list = new List<NativeTexture>();
            var editable = IndexEditable();
            foreach (string abs in EnumerateProjectFiles(Application.dataPath, "*.asset"))
            {
                NativeTexture t = ReadHeader(abs);
                if (t == null) continue;
                string meta = abs + ".meta";
                if (!File.Exists(meta)) continue;
                string metaText = File.ReadAllText(meta);
                if (!metaText.Contains("NativeFormatImporter:")) continue;
                var g = GuidLine.Match(metaText);
                if (!g.Success) continue;
                t.Guid = g.Groups[1].Value;
                t.AssetPath = ToAssetPath(abs);
                string img;
                if (editable.TryGetValue(t.Guid, out img)) { t.EditableImage = img; t.EditableMeta = img + ".meta.txt"; }
                list.Add(t);
            }
            list.Sort((a, b) => string.Compare(a.AssetPath, b.AssetPath, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        /// <summary>The textures converted by this tool that can go back to their exact asset.</summary>
        public static List<Converted> ConvertedTextures()
        {
            var m = LoadManifest();
            return m.items.Where(c => File.Exists(c.backupAsset)).ToList();
        }

        /// <summary>guid -> absolute image path, from every "*.meta.txt" under Editable Textures.</summary>
        private static Dictionary<string, string> IndexEditable()
        {
            var map = new Dictionary<string, string>();
            string root = Path.Combine(ProjectRoot, EditableFolderName);
            if (!Directory.Exists(root)) return map;
            foreach (string mt in Directory.EnumerateFiles(root, "*.meta.txt", SearchOption.AllDirectories))
            {
                string img = mt.Substring(0, mt.Length - ".meta.txt".Length);
                if (!File.Exists(img)) continue;
                string head;
                try { head = ReadStart(mt, 512); } catch { continue; }
                var g = GuidLine.Match(head);
                if (g.Success && !map.ContainsKey(g.Groups[1].Value)) map[g.Groups[1].Value] = img;
            }
            return map;
        }

        private static readonly Regex HeaderField = new Regex(@"^  (m_\w+): (-?[0-9.eE+-]+)\s*$");
        private static readonly Regex SettingsField = new Regex(@"^    (m_\w+): (-?[0-9.eE+-]+)\s*$");

        /// <summary>The fields of a native texture asset, read from its first lines only (the pixel hex comes after
        /// "image data:" and can be tens of MB). Null when the file is not a Texture2D / Cubemap asset.</summary>
        private static NativeTexture ReadHeader(string abs)
        {
            try
            {
                using (var r = new StreamReader(abs, Encoding.UTF8, false, 4096))
                {
                    string l1 = r.ReadLine(), l2 = r.ReadLine(), l3 = r.ReadLine(), l4 = r.ReadLine();
                    if (l1 == null || !l1.StartsWith("%YAML") || l3 == null || l4 == null) return null;
                    bool cube;
                    if (l3.StartsWith("--- !u!28 &2800000") && l4.StartsWith("Texture2D:")) cube = false;
                    else if (l3.StartsWith("--- !u!89 &8900000") && l4.StartsWith("Cubemap:")) cube = true;
                    else return null;
                    var t = new NativeTexture { Cube = cube, ColorSpace = 1, Aniso = 1, FilterMode = 1, MipCount = 1 };
                    string line;
                    int n = 0;
                    while ((line = r.ReadLine()) != null && n++ < 400)
                    {
                        if (line.StartsWith("  image data:") || line.StartsWith("  _typelessdata:") || line.StartsWith("--- ")) break;
                        var m = HeaderField.Match(line);
                        var s = m.Success ? null : SettingsField.Match(line);
                        string key = m.Success ? m.Groups[1].Value : (s.Success ? s.Groups[1].Value : null);
                        string val = m.Success ? m.Groups[2].Value : (s.Success ? s.Groups[2].Value : null);
                        if (key == null) continue;
                        switch (key)
                        {
                            case "m_Width": t.Width = ToInt(val); break;
                            case "m_Height": t.Height = ToInt(val); break;
                            case "m_TextureFormat": t.Format = ToInt(val); break;
                            case "m_MipCount": t.MipCount = ToInt(val); break;
                            case "m_ColorSpace": t.ColorSpace = ToInt(val); break;
                            case "m_StreamingMipmaps": t.Streaming = ToInt(val); break;
                            case "m_FilterMode": t.FilterMode = ToInt(val); break;
                            case "m_Aniso": t.Aniso = ToInt(val); break;
                            case "m_WrapU": t.WrapU = ToInt(val); break;
                            case "m_WrapV": t.WrapV = ToInt(val); break;
                            case "m_WrapW": t.WrapW = ToInt(val); break;
                            case "m_MipBias": float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out t.MipBias); break;
                        }
                    }
                    return t.Width > 0 && t.Height > 0 ? t : null;
                }
            }
            catch { return null; }
        }

        // ------------------------------------------------------------------------------------------------ convert

        /// <summary>Swap native texture assets for editable images (same GUID, materials stay linked). The native assets
        /// are parked in Backups~. <paramref name="forceDecode"/> ignores Editable Textures (tests, or a copy that is gone).</summary>
        public static Result ConvertToImage(IEnumerable<NativeTexture> textures, bool forceDecode = false)
        {
            var result = new Result();
            var todo = textures.Where(t => t != null).ToList();
            if (todo.Count == 0) return result;
            var manifest = LoadManifest();
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string parkDir = Path.Combine(BackupRootAbs, stamp + " exact");
            var guids = new HashSet<string>(todo.Select(t => t.Guid));
            var refs = FindReferences(guids);
            var decoded = new List<KeyValuePair<string, NativeTexture>>();     // image path -> texture: importer set after the refresh
            var converted = new HashSet<string>();
            var pending = new List<Pending>();
            var planned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // images not written yet

            try
            {
                EditorUtility.DisplayProgressBar("Texture Asset To PNG", "Converting...", 0f);
                for (int i = 0; i < todo.Count; i++)
                {
                    var t = todo[i];
                    EditorUtility.DisplayProgressBar("Texture Asset To PNG", t.Name, (float)i / todo.Count);
                    string assetAbs = KawaiiStudioPaths.ToAbsolute(t.AssetPath);
                    bool useEditable = !forceDecode && t.EditableImage != null && File.Exists(t.EditableImage) && File.Exists(t.EditableMeta);
                    if (!useEditable && t.Cube) { result.Skipped.Add($"{t.AssetPath}: a cubemap needs its copy in Editable Textures"); continue; }
                    if (manifest.items.Any(c => c.guid == t.Guid && File.Exists(c.backupAsset)))
                    {
                        result.Skipped.Add($"{t.AssetPath}: already converted once (use Back to exact first)");
                        continue;
                    }

                    byte[] image;
                    string ext, metaText;
                    if (useEditable)
                    {
                        image = File.ReadAllBytes(t.EditableImage);
                        ext = Path.GetExtension(t.EditableImage);
                        metaText = File.ReadAllText(t.EditableMeta);
                        var mg = GuidLine.Match(metaText);
                        if (!mg.Success || mg.Groups[1].Value != t.Guid) { result.Errors.Add($"{t.AssetPath}: the editable copy's .meta.txt has another GUID"); continue; }
                    }
                    else
                    {
                        bool normal = refs.NormalSlots.Contains(t.Guid);
                        string err;
                        image = Decode(t, normal, out ext, out err);
                        if (image == null) { result.Errors.Add($"{t.AssetPath}: {err}"); continue; }
                        // a .meta with only the GUID is read as a broken importer ("Unknown error occurred while loading")
                        // and the image comes in as a CUBEMAP (textureShape 2, measured in the lab): name the importer and the
                        // shape; every other setting is applied through the API after the refresh
                        metaText = "fileFormatVersion: 2\nguid: " + t.Guid + "\nTextureImporter:\n  textureShape: 1\n" +
                                   "  userData: \n  assetBundleName: \n  assetBundleVariant: \n";
                    }

                    string dir = Path.GetDirectoryName(assetAbs);
                    string baseName = useEditable ? Path.GetFileNameWithoutExtension(t.EditableImage) : t.Name;
                    baseName = CleanName(baseName, t.Guid);
                    string imageAbs = UniquePath(dir, baseName, ext, planned);
                    planned.Add(imageAbs);
                    string parked = UniquePark(Path.Combine(parkDir, RelToAssets(assetAbs)));

                    Directory.CreateDirectory(Path.GetDirectoryName(parked));
                    File.Move(assetAbs, parked);
                    File.Move(assetAbs + ".meta", parked + ".meta");
                    pending.Add(new Pending { T = t, AssetAbs = assetAbs, Parked = parked, ImageAbs = imageAbs, Image = image, Meta = metaText, Editable = useEditable });
                }
            }
            catch (Exception e)
            {
                result.Errors.Add("stopped: " + e.Message);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            // TWO refreshes. In one, the GUID would change from a native asset to an imported image while Unity still holds
            // the native texture in memory: it then reads the image as a native file and logs "Unknown error occurred while
            // loading" once per texture (lab, 14 of 14). First the native assets leave and Unity unloads them...
            AssetDatabase.Refresh();

            // ...then the images come in under the same GUIDs
            foreach (var p in pending)
            {
                try
                {
                    File.WriteAllBytes(p.ImageAbs, p.Image);
                    File.WriteAllText(p.ImageAbs + ".meta", p.Meta, new UTF8Encoding(false));
                }
                catch (Exception e)
                {
                    // put the native asset back: never leave a GUID with no file
                    try
                    {
                        if (File.Exists(p.ImageAbs)) File.Delete(p.ImageAbs);
                        if (File.Exists(p.ImageAbs + ".meta")) File.Delete(p.ImageAbs + ".meta");
                        File.Move(p.Parked, p.AssetAbs);
                        File.Move(p.Parked + ".meta", p.AssetAbs + ".meta");
                    }
                    catch { }
                    result.Errors.Add($"{p.T.AssetPath}: image not written, exact asset kept ({e.Message})");
                    continue;
                }
                string imagePath = ToAssetPath(p.ImageAbs);
                manifest.items.RemoveAll(c => c.guid == p.T.Guid);
                manifest.items.Add(new Converted
                {
                    guid = p.T.Guid, assetPath = p.T.AssetPath, imagePath = imagePath, backupAsset = p.Parked,
                    source = p.Editable ? "EditableTextures" : "Decoded", when = DateTime.Now.ToString("s"),
                });
                if (!p.Editable) decoded.Add(new KeyValuePair<string, NativeTexture>(imagePath, p.T));
                converted.Add(p.T.Guid);
                result.Done.Add($"{p.T.AssetPath} -> {imagePath} ({(p.Editable ? "Editable Textures" : "decoded")})");
            }
            // an imported image is referenced as "type: 3" (a native asset as "type: 2"): with the wrong type the slot
            // resolves to None
            RewriteReferenceType(refs, converted, "2", "3", result);
            SaveManifest(manifest);
            AssetDatabase.Refresh();
            foreach (var kv in decoded)
            {
                try { ApplyImporter(kv.Key, kv.Value, refs.NormalSlots.Contains(kv.Value.Guid)); }
                catch (Exception e) { result.Errors.Add($"{kv.Key}: import settings not set ({e.Message})"); }
            }
            foreach (string g in converted)
            {
                string now = AssetDatabase.GUIDToAssetPath(g);
                var c = manifest.items.First(x => x.guid == g);
                if (!string.Equals(now, c.imagePath, StringComparison.OrdinalIgnoreCase))
                    result.Errors.Add($"{c.imagePath}: Unity resolves its GUID to '{now}'");
            }
            return result;
        }

        private sealed class Pending
        {
            public NativeTexture T;
            public string AssetAbs, Parked, ImageAbs, Meta;
            public byte[] Image;
            public bool Editable;
        }

        // ------------------------------------------------------------------------------------------------ back to exact

        /// <summary>Put the exact native assets back. The images (with any edit made to them) are parked in Backups~.</summary>
        public static Result BackToExact(IEnumerable<string> guids)
        {
            var result = new Result();
            var want = new HashSet<string>(guids);
            var manifest = LoadManifest();
            var todo = manifest.items.Where(c => want.Contains(c.guid)).ToList();
            if (todo.Count == 0) return result;
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string parkDir = Path.Combine(BackupRootAbs, stamp + " images");
            var refs = FindReferences(new HashSet<string>(todo.Select(c => c.guid)));
            var restored = new HashSet<string>();
            try
            {
                foreach (var c in todo)
                {
                    if (!File.Exists(c.backupAsset) || !File.Exists(c.backupAsset + ".meta"))
                    {
                        result.Errors.Add($"{c.assetPath}: the parked exact asset is gone ({c.backupAsset})");
                        continue;
                    }
                    // the image may have been moved or renamed in Unity: its GUID knows where it is
                    string imagePath = AssetDatabase.GUIDToAssetPath(c.guid);
                    if (string.IsNullOrEmpty(imagePath)) imagePath = c.imagePath;
                    string imageAbs = KawaiiStudioPaths.ToAbsolute(imagePath);
                    string target = KawaiiStudioPaths.ToAbsolute(c.assetPath);
                    if (File.Exists(target) || File.Exists(target + ".meta"))
                        target = UniquePath(Path.GetDirectoryName(imageAbs), Path.GetFileNameWithoutExtension(c.assetPath), ".asset");
                    if (File.Exists(imageAbs))
                    {
                        string parked = UniquePark(Path.Combine(parkDir, RelToAssets(imageAbs)));
                        Directory.CreateDirectory(Path.GetDirectoryName(parked));
                        File.Move(imageAbs, parked);
                        if (File.Exists(imageAbs + ".meta")) File.Move(imageAbs + ".meta", parked + ".meta");
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Move(c.backupAsset, target);
                    File.Move(c.backupAsset + ".meta", target + ".meta");
                    manifest.items.Remove(c);
                    restored.Add(c.guid);
                    result.Done.Add($"{imagePath} -> {ToAssetPath(target)}");
                }
                RewriteReferenceType(refs, restored, "3", "2", result);
                SaveManifest(manifest);
            }
            catch (Exception e)
            {
                result.Errors.Add("stopped: " + e.Message);
                SaveManifest(manifest);
            }
            AssetDatabase.Refresh();
            return result;
        }

        // ------------------------------------------------------------------------------------------------ references

        private sealed class References
        {
            public readonly Dictionary<string, List<string>> Files = new Dictionary<string, List<string>>();   // guid -> files
            public readonly HashSet<string> NormalSlots = new HashSet<string>();                                 // guids used as a normal map
        }

        private static readonly HashSet<string> YamlExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mat", ".prefab", ".asset", ".anim", ".controller", ".unity", ".overrideController", ".mask", ".playable",
            ".renderTexture", ".flare", ".cubemap", ".physicMaterial", ".lighting", ".spriteatlas", ".terrainlayer",
        };
        private static readonly Regex TextureOrMeshHeader = new Regex(@"^--- !u!(28|43|89|117|187) &", RegexOptions.Multiline);
        private static readonly Regex RefRe = new Regex(@"\{fileID: (-?\d+), guid: ([0-9a-f]{32}), type: (\d)\}");
        // "- _BumpMap:" (Unity 2017.3+) or "_BumpMap:" without the dash (older materials: missed at first, lab 2026-11-06)
        private static readonly Regex SlotRe = new Regex(@"^[ \t]*(?:- )?(\w+):[ \t]*\r?\n[ \t]*m_Texture: \{fileID: -?\d+, guid: ([0-9a-f]{32}), type: \d\}", RegexOptions.Multiline);

        /// <summary>Every YAML file (and importer .meta: a shader's default textures) under Assets/ that names one of
        /// <paramref name="guids"/>, plus the guids a material uses in a normal-map slot.</summary>
        private static References FindReferences(HashSet<string> guids)
        {
            var r = new References();
            if (guids.Count == 0) return r;
            foreach (string f in EnumerateProjectFiles(Application.dataPath, "*"))
            {
                string ext = Path.GetExtension(f);
                bool meta = ext.Equals(".meta", StringComparison.OrdinalIgnoreCase);
                if (!meta && !YamlExt.Contains(ext)) continue;
                long len;
                try { len = new FileInfo(f).Length; } catch { continue; }
                if (len > (meta ? 1L << 20 : 1L << 30)) continue;
                string text;
                try
                {
                    // a texture or mesh asset holds no texture reference, and its pixel hex can be tens of MB: header only
                    if (!meta && TextureOrMeshHeader.IsMatch(ReadStart(f, 400))) continue;
                    text = File.ReadAllText(f);
                }
                catch { continue; }
                if (text.IndexOf("guid: ", StringComparison.Ordinal) < 0) continue;
                foreach (Match m in RefRe.Matches(text))
                {
                    string g = m.Groups[2].Value;
                    if (!guids.Contains(g)) continue;
                    List<string> l;
                    if (!r.Files.TryGetValue(g, out l)) r.Files[g] = l = new List<string>();
                    if (!l.Contains(f)) l.Add(f);
                }
                if (ext.Equals(".mat", StringComparison.OrdinalIgnoreCase))
                    foreach (Match m in SlotRe.Matches(text))
                        if (guids.Contains(m.Groups[2].Value) && Regex.IsMatch(m.Groups[1].Value, "bump|normal", RegexOptions.IgnoreCase))
                            r.NormalSlots.Add(m.Groups[2].Value);
            }
            return r;
        }

        private static void RewriteReferenceType(References refs, HashSet<string> guids, string from, string to, Result result)
        {
            if (guids.Count == 0) return;
            var files = guids.SelectMany(g => refs.Files.ContainsKey(g) ? refs.Files[g] : new List<string>()).Distinct();
            foreach (string f in files)
            {
                try
                {
                    string t = File.ReadAllText(f);
                    string n = RefRe.Replace(t, m => guids.Contains(m.Groups[2].Value) && m.Groups[3].Value == from
                        ? "{fileID: " + m.Groups[1].Value + ", guid: " + m.Groups[2].Value + ", type: " + to + "}" : m.Value);
                    if (n != t) File.WriteAllText(f, n, new UTF8Encoding(false));
                }
                catch (Exception e) { result.Errors.Add($"{ToAssetPath(f)}: references not updated ({e.Message})"); }
            }
        }

        // ------------------------------------------------------------------------------------------------ decode

        /// <summary>The asset's top mip as an image, read back from the GPU: PNG, or EXR for an HDR format. A texture
        /// used as a normal map is rebuilt as a plain RGB normal (what UnpackNormal reads: x = r * a, y = g).</summary>
        private static byte[] Decode(NativeTexture t, bool normal, out string ext, out string error)
        {
            ext = t.Hdr ? ".exr" : ".png";
            error = null;
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(t.AssetPath);
            if (tex == null) { error = "Unity cannot load the texture"; return null; }
            bool srgb = t.ColorSpace == 1 && !t.Hdr && !normal;
            var rt = RenderTexture.GetTemporary(t.Width, t.Height, 0,
                t.Hdr ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32,
                srgb ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
            var prev = RenderTexture.active;
            Texture2D read = null;
            try
            {
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                read = new Texture2D(t.Width, t.Height, t.Hdr ? TextureFormat.RGBAHalf : TextureFormat.RGBA32, false, !srgb);
                read.ReadPixels(new Rect(0, 0, t.Width, t.Height), 0, 0, false);
                read.Apply(false, false);
                if (t.Hdr) return read.EncodeToEXR(Texture2D.EXRFlags.CompressZIP);
                if (normal)
                {
                    var px = read.GetPixels32();
                    for (int i = 0; i < px.Length; i++)
                    {
                        float x = (px[i].r / 255f) * (px[i].a / 255f) * 2f - 1f;
                        float y = px[i].g / 255f * 2f - 1f;
                        float z = Mathf.Sqrt(Mathf.Max(0f, 1f - x * x - y * y));
                        px[i] = new Color32(To8(x), To8(y), To8(z), 255);
                    }
                    read.SetPixels32(px);
                    read.Apply(false, false);
                }
                return read.EncodeToPNG();
            }
            catch (Exception e) { error = "decode failed: " + e.Message; return null; }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                if (read != null) Object.DestroyImmediate(read);
            }
        }

        private static byte To8(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt((v * 0.5f + 0.5f) * 255f), 0, 255);

        /// <summary>Import settings of a decoded image, from what the native asset says about itself.</summary>
        private static void ApplyImporter(string imagePath, NativeTexture t, bool normal)
        {
            var imp = AssetImporter.GetAtPath(imagePath) as TextureImporter;
            if (imp == null) throw new Exception("no texture importer");
            imp.textureShape = TextureImporterShape.Texture2D;
            imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            imp.sRGBTexture = t.ColorSpace == 1 && !t.Hdr;
            imp.mipmapEnabled = t.MipCount > 1;
            imp.streamingMipmaps = t.Streaming == 1;
            imp.npotScale = TextureImporterNPOTScale.None;
            imp.alphaIsTransparency = false;
            imp.isReadable = false;
            imp.wrapModeU = (TextureWrapMode)t.WrapU;
            imp.wrapModeV = (TextureWrapMode)t.WrapV;
            imp.wrapModeW = (TextureWrapMode)t.WrapW;
            imp.filterMode = (FilterMode)Mathf.Clamp(t.FilterMode, 0, 2);
            imp.anisoLevel = t.Aniso;
            imp.mipMapBias = t.MipBias;
#if UNITY_2021_2_OR_NEWER
            const int maxSize = 16384;
#else
            const int maxSize = 8192;
#endif
            imp.maxTextureSize = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(t.Width, t.Height)), 32, maxSize);
            if (IsUncompressed(t.Format)) imp.textureCompression = TextureImporterCompression.Uncompressed;
            else if (t.Format == 24 || t.Format == 25 || t.Format == 26 || t.Format == 27) imp.textureCompression = TextureImporterCompression.CompressedHQ;
            else imp.textureCompression = TextureImporterCompression.Compressed;
            imp.crunchedCompression = t.Format == 28 || t.Format == 29;
            if (imp.crunchedCompression) imp.compressionQuality = 100;
            imp.SaveAndReimport();
        }

        // ------------------------------------------------------------------------------------------------ formats

        public static bool IsHdr(int f) => f == 15 || f == 16 || f == 17 || f == 18 || f == 19 || f == 20 || f == 22 || f == 24;
        // HDR included: an uncompressed RGBAHalf brought back as BC6H was 99.8 % off (lab, synthetic RGBAHalf 0..4)
        private static bool IsUncompressed(int f) => f == 1 || f == 2 || f == 3 || f == 4 || f == 5 || f == 7 || f == 9 || f == 13 || f == 14 || f == 62 || f == 63
                                                     || f == 15 || f == 16 || f == 17 || f == 18 || f == 19 || f == 20 || f == 22;

        public static string FormatLabel(int f)
        {
            switch (f)
            {
                case 1: return "Alpha8"; case 3: return "RGB24"; case 4: return "RGBA32"; case 5: return "ARGB32";
                case 7: return "RGB565"; case 10: return "DXT1"; case 12: return "DXT5"; case 13: return "RGBA4444";
                case 14: return "BGRA32"; case 15: return "RHalf"; case 16: return "RGHalf"; case 17: return "RGBAHalf";
                case 18: return "RFloat"; case 19: return "RGFloat"; case 20: return "RGBAFloat"; case 22: return "RGB9e5";
                case 24: return "BC6H"; case 25: return "BC7"; case 26: return "BC4"; case 27: return "BC5";
                case 28: return "DXT1 Crunched"; case 29: return "DXT5 Crunched"; case 62: return "RG16"; case 63: return "R8";
                default: return "format " + f;
            }
        }

        // ------------------------------------------------------------------------------------------------ files

        private static IEnumerable<string> EnumerateProjectFiles(string root, string pattern)
        {
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                string d = stack.Pop();
                string[] files, dirs;
                try { files = Directory.GetFiles(d, pattern); dirs = Directory.GetDirectories(d); } catch { continue; }
                foreach (string f in files) yield return f;
                foreach (string sub in dirs)
                {
                    string n = Path.GetFileName(sub);
                    if (n.EndsWith("~") || n.StartsWith(".")) continue;      // never imported by Unity (Backups~ included)
                    stack.Push(sub);
                }
            }
        }

        private static string ReadStart(string path, int chars)
        {
            using (var r = new StreamReader(path))
            {
                var buf = new char[chars];
                int n = r.Read(buf, 0, chars);
                return new string(buf, 0, n);
            }
        }

        private static int ToInt(string v)
        {
            int i;
            return int.TryParse(v, out i) ? i : 0;
        }

        private static string ToAssetPath(string abs)
        {
            string root = ProjectRoot.Replace('\\', '/').TrimEnd('/') + "/";
            string p = abs.Replace('\\', '/');
            return p.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? p.Substring(root.Length) : p;
        }

        private static string RelToAssets(string abs)
        {
            string a = Application.dataPath.Replace('/', Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string p = abs.Replace('/', Path.DirectorySeparatorChar);
            return p.StartsWith(a, StringComparison.OrdinalIgnoreCase) ? p.Substring(a.Length) : Path.GetFileName(p);
        }

        /// <summary>The repair's collision names: "X (texture)" for the asset, "X (1a2b3c4d)" (the GUID's start) for the
        /// editable image. The image gets the texture's own name back when that name is free.</summary>
        private static string CleanName(string name, string guid)
        {
            if (name.EndsWith(" (texture)")) name = name.Substring(0, name.Length - " (texture)".Length);
            string tag = " (" + guid.Substring(0, 8) + ")";
            if (name.EndsWith(tag)) name = name.Substring(0, name.Length - tag.Length);
            return name;
        }

        private static string UniquePath(string dir, string baseName, string ext, HashSet<string> planned = null)
        {
            string p = Path.Combine(dir, baseName + ext);
            int i = 1;
            while (File.Exists(p) || File.Exists(p + ".meta") || Directory.Exists(p) || (planned != null && planned.Contains(p)))
                p = Path.Combine(dir, baseName + (i++ == 1 ? " (png)" : $" (png {i - 1})") + ext);
            return p;
        }

        private static string UniquePark(string path)
        {
            string p = path;
            int i = 1;
            while (File.Exists(p) || File.Exists(p + ".meta"))
                p = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + $" ({i++})" + Path.GetExtension(path));
            return p;
        }

        private static Manifest LoadManifest()
        {
            try
            {
                if (File.Exists(ManifestAbs))
                    return JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestAbs)) ?? new Manifest();
            }
            catch { }
            return new Manifest();
        }

        private static void SaveManifest(Manifest m)
        {
            Directory.CreateDirectory(BackupRootAbs);
            File.WriteAllText(ManifestAbs, JsonUtility.ToJson(m, true), new UTF8Encoding(false));
        }

        // ------------------------------------------------------------------------------------------------ menus

        private static List<NativeTexture> SelectedNative()
        {
            var sel = new HashSet<string>(Selection.assetGUIDs);
            if (sel.Count == 0) return new List<NativeTexture>();
            return Scan().Where(t => sel.Contains(t.Guid)).ToList();
        }

        [MenuItem("Assets/Kawaii Studio/Convert Texture Asset to PNG", true)]
        private static bool ConvertMenuValidate() =>
            Selection.assetGUIDs.Any(g => AssetDatabase.GUIDToAssetPath(g).EndsWith(".asset", StringComparison.OrdinalIgnoreCase)
                                          && AssetDatabase.GetMainAssetTypeAtPath(AssetDatabase.GUIDToAssetPath(g)) is Type ty
                                          && (ty == typeof(Texture2D) || ty == typeof(Cubemap)));

        [MenuItem("Assets/Kawaii Studio/Convert Texture Asset to PNG", false, 2010)]
        private static void ConvertMenu()
        {
            var list = SelectedNative();
            if (list.Count == 0) { EditorUtility.DisplayDialog("Texture Asset To PNG", "No native texture asset in the selection.", "OK"); return; }
            if (!EditorUtility.DisplayDialog("Texture Asset To PNG",
                    $"Convert {list.Count} texture(s) to editable images?\n\nUnity re-compresses an image when it imports it, so the " +
                    "texture is no longer byte-identical to the original. The exact asset is kept in Backups~ and Back to exact restores it.",
                    "Convert", "Cancel")) return;
            var r = ConvertToImage(list);
            ReportDialog(r, "converted");
        }

        [MenuItem("Assets/Kawaii Studio/Texture Back to Exact", true)]
        private static bool BackMenuValidate()
        {
            var conv = new HashSet<string>(ConvertedTextures().Select(c => c.guid));
            return Selection.assetGUIDs.Any(conv.Contains);
        }

        [MenuItem("Assets/Kawaii Studio/Texture Back to Exact", false, 2011)]
        private static void BackMenu()
        {
            var conv = new HashSet<string>(ConvertedTextures().Select(c => c.guid));
            var guids = Selection.assetGUIDs.Where(conv.Contains).ToList();
            if (guids.Count == 0) return;
            if (!EditorUtility.DisplayDialog("Texture Asset To PNG",
                    $"Put the exact asset back for {guids.Count} texture(s)?\n\nThe images, with any edit made to them, are moved to Backups~.",
                    "Back to exact", "Cancel")) return;
            ReportDialog(BackToExact(guids), "back to exact");
        }

        internal static void ReportDialog(Result r, string verb)
        {
            var sb = new StringBuilder(r.Summary(verb));
            foreach (string e in r.Errors.Take(12)) sb.Append("\n\nError: ").Append(e);
            foreach (string s in r.Skipped.Take(8)) sb.Append("\n\nSkipped: ").Append(s);
            Debug.Log("[Texture Asset To PNG] " + r.Summary(verb) + "\n" + string.Join("\n", r.Done.Concat(r.Errors).Concat(r.Skipped)));
            EditorUtility.DisplayDialog("Texture Asset To PNG", sb.ToString(), "OK");
        }
    }

    public class KawaiiStudioTextureAssetToPngWindow : EditorWindow
    {
        private const string VERSION = KawaiiStudioVersion.Current;

        private List<KawaiiStudioTextureAssets.NativeTexture> _native = new List<KawaiiStudioTextureAssets.NativeTexture>();
        private List<KawaiiStudioTextureAssets.Converted> _converted = new List<KawaiiStudioTextureAssets.Converted>();
        private readonly HashSet<string> _pickNative = new HashSet<string>();
        private readonly HashSet<string> _pickConverted = new HashSet<string>();
        private Vector2 _scroll;
        private string _status = "";
        private KawaiiStudioGUI.MessageKind _statusKind = KawaiiStudioGUI.MessageKind.Info;

        [MenuItem("Kawaii Studio/Universal Tools/Texture Asset To PNG")]
        public static void ShowWindow()
        {
            var w = GetWindow<KawaiiStudioTextureAssetToPngWindow>("Texture Asset To PNG");
            w.minSize = new Vector2(640, 560);
            w.Show();
        }

        private void OnEnable()
        {
            KawaiiStudioGUI.Initialize();
            Rescan();
        }

        private void OnFocus() => Rescan();

        private void Rescan()
        {
            _native = KawaiiStudioTextureAssets.Scan();
            _converted = KawaiiStudioTextureAssets.ConvertedTextures();
            _pickNative.IntersectWith(_native.Select(t => t.Guid));
            _pickConverted.IntersectWith(_converted.Select(c => c.guid));
        }

        private void OnGUI()
        {
            KawaiiStudioGUI.Initialize();
            KawaiiStudioGUI.DrawWindowBackground(position);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.BeginVertical(new GUIStyle { padding = new RectOffset(12, 12, 12, 12) });

            KawaiiStudioGUI.DrawBanner("TEXTURE ASSET TO PNG", "Make the exact textures of a repaired project editable",
                VERSION, KawaiiStudioBranding.Logo, KawaiiStudioBranding.Banner);
            GUILayout.Space(10);

            KawaiiStudioGUI.DrawSection("HOW THIS TOOL WORKS", () =>
            {
                EditorGUILayout.LabelField("A full repair keeps compressed textures as .asset files that hold the original data, so they look exactly like the original.", KawaiiStudioGUI.InfoLabelStyle);
                EditorGUILayout.LabelField("Convert puts an editable image in their place. Materials stay linked, nothing to reassign.", KawaiiStudioGUI.InfoLabelStyle);
                EditorGUILayout.LabelField("The image comes from the project's Editable Textures folder when it has one, with its original import settings.", KawaiiStudioGUI.InfoLabelStyle);
                EditorGUILayout.LabelField("Back to exact restores the original .asset. Nothing is deleted: what moves goes to Backups~.", KawaiiStudioGUI.InfoLabelStyle);
                GUILayout.Space(6);
                EditorGUILayout.HelpBox("Unity re-compresses an image when it imports it. A converted texture is no longer byte-identical " +
                                        "to the original: the difference is small, and it is the price of an editable texture.", MessageType.Warning);
            });

            DrawNativeSection();
            DrawConvertedSection();

            if (!string.IsNullOrEmpty(_status)) KawaiiStudioGUI.Banner(_status, _statusKind);
            GUILayout.Space(16);
            KawaiiStudioGUI.DrawFooter();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndScrollView();
        }

        private void DrawNativeSection()
        {
            KawaiiStudioGUI.DrawSection($"EXACT TEXTURES ({_native.Count})", () =>
            {
                if (_native.Count == 0)
                {
                    EditorGUILayout.LabelField("No .asset texture in this project.", KawaiiStudioGUI.InfoLabelStyle);
                    return;
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (KawaiiStudioGUI.SecondaryButton("SELECT ALL", GUILayout.Width(110))) foreach (var t in _native) _pickNative.Add(t.Guid);
                    if (KawaiiStudioGUI.SecondaryButton("SELECT NONE", GUILayout.Width(110))) _pickNative.Clear();
                    GUILayout.FlexibleSpace();
                    if (KawaiiStudioGUI.SecondaryButton("RESCAN", GUILayout.Width(90))) Rescan();
                }
                GUILayout.Space(4);
                foreach (var t in _native)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        bool on = _pickNative.Contains(t.Guid);
                        bool now = EditorGUILayout.Toggle(on, GUILayout.Width(18));
                        if (now != on) { if (now) _pickNative.Add(t.Guid); else _pickNative.Remove(t.Guid); }
                        if (GUILayout.Button(t.Name, KawaiiStudioGUI.LabelStyle, GUILayout.MinWidth(160)))
                            EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(t.AssetPath));
                        GUILayout.FlexibleSpace();
                        GUILayout.Label($"{t.Width}x{t.Height}  {t.FormatName}", KawaiiStudioGUI.InfoLabelStyle, GUILayout.Width(150));
                        string src = t.Source == KawaiiStudioTextureAssets.Source.EditableTextures ? "Editable Textures"
                                   : t.Source == KawaiiStudioTextureAssets.Source.Decoded ? "decoded" : "no source";
                        GUILayout.Label(src, KawaiiStudioGUI.InfoLabelStyle, GUILayout.Width(110));
                    }
                }
                GUILayout.Space(6);
                using (new EditorGUI.DisabledScope(_pickNative.Count == 0))
                {
                    if (KawaiiStudioGUI.PrimaryButton($"CONVERT {_pickNative.Count} TO PNG", GUILayout.Height(30)))
                    {
                        var list = _native.Where(t => _pickNative.Contains(t.Guid)).ToList();
                        if (EditorUtility.DisplayDialog("Texture Asset To PNG",
                                $"Convert {list.Count} texture(s) to editable images?\n\nThe exact assets are kept in Backups~ and Back to exact restores them.",
                                "Convert", "Cancel"))
                        {
                            var r = KawaiiStudioTextureAssets.ConvertToImage(list);
                            SetStatus(r, "converted to editable images");
                            _pickNative.Clear();
                            Rescan();
                        }
                    }
                }
            });
        }

        private void DrawConvertedSection()
        {
            if (_converted.Count == 0) return;
            KawaiiStudioGUI.DrawSection($"CONVERTED ({_converted.Count})", () =>
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (KawaiiStudioGUI.SecondaryButton("SELECT ALL", GUILayout.Width(110))) foreach (var c in _converted) _pickConverted.Add(c.guid);
                    if (KawaiiStudioGUI.SecondaryButton("SELECT NONE", GUILayout.Width(110))) _pickConverted.Clear();
                }
                GUILayout.Space(4);
                foreach (var c in _converted)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        bool on = _pickConverted.Contains(c.guid);
                        bool now = EditorGUILayout.Toggle(on, GUILayout.Width(18));
                        if (now != on) { if (now) _pickConverted.Add(c.guid); else _pickConverted.Remove(c.guid); }
                        string path = AssetDatabase.GUIDToAssetPath(c.guid);
                        if (string.IsNullOrEmpty(path)) path = c.imagePath;
                        if (GUILayout.Button(Path.GetFileName(path), KawaiiStudioGUI.LabelStyle, GUILayout.MinWidth(160)))
                            EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(path));
                        GUILayout.FlexibleSpace();
                        GUILayout.Label(c.source == "Decoded" ? "decoded" : "Editable Textures", KawaiiStudioGUI.InfoLabelStyle, GUILayout.Width(110));
                    }
                }
                GUILayout.Space(6);
                using (new EditorGUI.DisabledScope(_pickConverted.Count == 0))
                {
                    if (KawaiiStudioGUI.SecondaryButton($"BACK TO EXACT ({_pickConverted.Count})", GUILayout.Height(28)))
                    {
                        if (EditorUtility.DisplayDialog("Texture Asset To PNG",
                                $"Put the exact asset back for {_pickConverted.Count} texture(s)?\n\nThe images, with any edit made to them, are moved to Backups~.",
                                "Back to exact", "Cancel"))
                        {
                            var r = KawaiiStudioTextureAssets.BackToExact(_pickConverted.ToList());
                            SetStatus(r, "back to their exact asset");
                            _pickConverted.Clear();
                            Rescan();
                        }
                    }
                }
            });
        }

        private void SetStatus(KawaiiStudioTextureAssets.Result r, string verb)
        {
            _status = r.Summary(verb) + (r.Errors.Count > 0 ? " First error: " + r.Errors[0] : "");
            _statusKind = r.Errors.Count > 0 ? KawaiiStudioGUI.MessageKind.Error
                        : r.Skipped.Count > 0 ? KawaiiStudioGUI.MessageKind.Warning : KawaiiStudioGUI.MessageKind.Success;
            Debug.Log("[Texture Asset To PNG] " + r.Summary(verb) + "\n" + string.Join("\n", r.Done.Concat(r.Errors).Concat(r.Skipped)));
        }
    }
}
