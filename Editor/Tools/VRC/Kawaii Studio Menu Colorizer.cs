// Menu Colorizer -- colourful VRChat Expressions Menus: gradients, rainbow, bold, size.
//
// VRChat draws expression-menu labels with TextMeshPro, so a control's name can carry
// rich-text tags. Doing that by hand is tedious -- a gradient is one <color> tag per letter --
// so this window writes the tags: pick a theme and colour the whole menu in one click, or
// click a button in the live radial preview and fine-tune it.
//
// Only label text is ever written. Parameters, types, icons and sub-menu links are never
// touched, every change goes through Undo, and menus inside read-only packages are skipped.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace KawaiiStudio
{
    public enum KSLabelMode { Plain, Solid, Gradient, Rainbow }

    /// <summary>How one menu label is coloured.</summary>
    [Serializable]
    public class KSLabelStyle
    {
        public KSLabelMode mode = KSLabelMode.Gradient;
        public Color colorA = new Color32(0xFF, 0x6E, 0xC7, 0xFF);
        public Color colorB = new Color32(0xB2, 0x66, 0xFF, 0xFF);
        public float hueStart;            // rainbow: hue of the first letter, 0..1
        public float hueSpan = 0.8f;      // rainbow: hue travelled across the label (1 = a full rainbow)
        public float saturation = 0.72f;  // rainbow
        public bool bold = true;
        public bool italic;
        public int sizePercent = 100;

        public KSLabelStyle Clone() => (KSLabelStyle)MemberwiseClone();
    }

    /// <summary>
    /// TextMeshPro rich text for menu labels: build it from a style, read a style back out of
    /// it, strip it, and translate it for an IMGUI preview. Pure string logic.
    /// </summary>
    public static class KawaiiMenuRichText
    {
        // The tags TextMeshPro understands. Only these are stripped or interpreted, so text
        // that merely looks like a tag ("<3") stays text.
        private const string TagPattern =
            @"<(/?)(b|i|u|s|color|size|font|mark|sup|sub|noparse|alpha|cspace|mspace|voffset|" +
            @"line-height|width|indent|align|gradient|sprite|style|uppercase|lowercase|smallcaps|" +
            @"allcaps|link|nobr|pos|margin|rotate|br|font-weight|material)([= ][^<>]*)?>|<(#[0-9a-fA-F]{3,8})>";

        private static readonly Regex Tag = new Regex(TagPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex TagAt = new Regex(@"\G(?:" + TagPattern + ")", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>The visible text: every TextMeshPro tag removed, &lt;br&gt; turned into a line break.</summary>
        public static string StripTags(string rich)
        {
            if (string.IsNullOrEmpty(rich)) return rich ?? string.Empty;
            return Tag.Replace(rich, m =>
                m.Groups[2].Value.Equals("br", StringComparison.OrdinalIgnoreCase) ? "\n" : string.Empty);
        }

        public static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

        /// <summary>
        /// Blend in HSV along the short way round the colour wheel, which keeps the in-between
        /// letters as vivid as the ends (an RGB blend goes muddy grey in the middle).
        /// </summary>
        public static Color GradientAt(Color a, Color b, float t)
        {
            Color.RGBToHSV(a, out float h1, out float s1, out float v1);
            Color.RGBToHSV(b, out float h2, out float s2, out float v2);
            // A near-grey end has no meaningful hue; borrow the other end's so the blend
            // doesn't detour through unrelated colours.
            if (s1 < 0.12f) h1 = h2;
            if (s2 < 0.12f) h2 = h1;
            float dh = h2 - h1;
            if (dh > 0.5f) dh -= 1f;
            else if (dh < -0.5f) dh += 1f;
            return Color.HSVToRGB(Mathf.Repeat(h1 + dh * t, 1f), Mathf.Lerp(s1, s2, t), Mathf.Lerp(v1, v2, t));
        }

        public static Color RainbowAt(KSLabelStyle s, float t)
            => Color.HSVToRGB(Mathf.Repeat(s.hueStart + s.hueSpan * t, 1f), Mathf.Clamp01(s.saturation), 1f);

        /// <summary>The rich-text label for <paramref name="plain"/> in <paramref name="style"/>.</summary>
        public static string Build(string plain, KSLabelStyle style)
        {
            if (string.IsNullOrEmpty(plain) || style == null) return plain ?? string.Empty;

            string inner;
            switch (style.mode)
            {
                case KSLabelMode.Solid:
                    inner = "<color=#" + Hex(style.colorA) + ">" + Escape(plain) + "</color>";
                    break;
                case KSLabelMode.Gradient:
                case KSLabelMode.Rainbow:
                    inner = PerLetter(plain, style);
                    break;
                default:
                    inner = Escape(plain);
                    break;
            }

            if (style.italic) inner = "<i>" + inner + "</i>";
            if (style.bold) inner = "<b>" + inner + "</b>";
            int size = Mathf.Clamp(style.sizePercent, 10, 400);
            if (size != 100) inner = "<size=" + size.ToString(CultureInfo.InvariantCulture) + "%>" + inner + "</size>";
            return inner;
        }

        // A literal '<' typed by the user must not start a tag.
        private static string Escape(string text)
            => text.IndexOf('<') < 0 ? text : text.Replace("<", "<noparse><</noparse>");

        private static string PerLetter(string plain, KSLabelStyle s)
        {
            List<string> parts = Graphemes(plain);
            int visible = parts.Count(p => !string.IsNullOrWhiteSpace(p));
            var sb = new StringBuilder(plain.Length * 26);
            int k = 0;
            foreach (string p in parts)
            {
                if (string.IsNullOrWhiteSpace(p)) { sb.Append(p); continue; }
                float t = visible <= 1 ? 0f : k / (float)(visible - 1);
                k++;
                Color c = s.mode == KSLabelMode.Rainbow ? RainbowAt(s, t) : GradientAt(s.colorA, s.colorB, t);
                sb.Append("<color=#").Append(Hex(c)).Append('>').Append(Escape(p)).Append("</color>");
            }
            return sb.ToString();
        }

        /// <summary>
        /// What a reader sees as one character: an accented letter, an emoji with its skin tone,
        /// a flag, a joined family emoji. A colour tag inside one of those would break it.
        /// </summary>
        public static List<string> Graphemes(string text)
        {
            var parts = new List<string>();
            if (string.IsNullOrEmpty(text)) return parts;
            TextElementEnumerator e = StringInfo.GetTextElementEnumerator(text);
            while (e.MoveNext())
            {
                string element = e.GetTextElement();
                // The runtime's StringInfo already keeps combining marks with their letter;
                // these emoji joins are the ones it splits.
                if (parts.Count > 0 && JoinsPrevious(parts[parts.Count - 1], element))
                    parts[parts.Count - 1] += element;
                else
                    parts.Add(element);
            }
            return parts;
        }

        private static bool JoinsPrevious(string previous, string element)
        {
            int first = CodePoint(element, 0);
            int last = CodePoint(previous, previous.Length - (previous.Length >= 2 && char.IsLowSurrogate(previous[previous.Length - 1]) ? 2 : 1));
            if (first == 0x200D || last == 0x200D) return true;                 // zero-width joiner sequences
            if (first >= 0x1F3FB && first <= 0x1F3FF) return true;              // skin-tone modifiers
            if (first == 0xFE0F || first == 0xFE0E || first == 0x20E3) return true;  // variation selectors, keycaps
            if (first >= 0xE0020 && first <= 0xE007F) return true;              // tag sequences (subdivision flags)
            if (IsRegionalIndicator(first) && IsRegionalIndicator(last))         // flags are pairs of regional letters
            {
                int count = 0;
                for (int i = 0; i < previous.Length; i += char.IsSurrogatePair(previous, i) ? 2 : 1)
                    if (IsRegionalIndicator(CodePoint(previous, i))) count++;
                return count % 2 == 1;
            }
            return false;
        }

        private static bool IsRegionalIndicator(int codePoint) => codePoint >= 0x1F1E6 && codePoint <= 0x1F1FF;

        private static int CodePoint(string s, int index)
        {
            if (index < 0 || index >= s.Length) return 0;
            return char.IsSurrogatePair(s, index) ? char.ConvertToUtf32(s[index], s[index + 1]) : s[index];
        }

        /// <summary>
        /// Best guess of the style a label was written with, so an existing label opens in the
        /// editor the way it looks. Exact for labels this tool wrote.
        /// </summary>
        public static KSLabelStyle Guess(string rich)
        {
            var s = new KSLabelStyle { mode = KSLabelMode.Plain, bold = false };
            if (string.IsNullOrEmpty(rich)) return s;

            s.bold = Regex.IsMatch(rich, "<b>", RegexOptions.IgnoreCase);
            s.italic = Regex.IsMatch(rich, "<i>", RegexOptions.IgnoreCase);
            Match size = Regex.Match(rich, @"<size=(\d{1,3})%>", RegexOptions.IgnoreCase);
            s.sizePercent = size.Success
                ? Mathf.Clamp(int.Parse(size.Groups[1].Value, CultureInfo.InvariantCulture), 10, 400)
                : 100;

            List<Color> colors = VisibleColors(rich);
            if (colors.Count == 0) return s;

            s.colorA = colors[0];
            s.colorB = colors[colors.Count - 1];
            if (colors.All(c => Near(c, colors[0])))
            {
                s.mode = KSLabelMode.Solid;
                return s;
            }

            // A two-stop gradient never travels more than half the wheel; more than that is a rainbow.
            float travel = 0f;
            Color.RGBToHSV(colors[0], out float previous, out _, out _);
            for (int i = 1; i < colors.Count; i++)
            {
                Color.RGBToHSV(colors[i], out float h, out _, out _);
                float d = h - previous;
                if (d > 0.5f) d -= 1f;
                else if (d < -0.5f) d += 1f;
                travel += d;
                previous = h;
            }

            if (Mathf.Abs(travel) > 0.55f)
            {
                s.mode = KSLabelMode.Rainbow;
                Color.RGBToHSV(colors[0], out s.hueStart, out s.saturation, out _);
                s.hueSpan = travel;
            }
            else
            {
                s.mode = KSLabelMode.Gradient;
            }
            return s;
        }

        private static bool Near(Color a, Color b)
            => Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f;

        /// <summary>The colour of every visible letter that has one, in order.</summary>
        private static List<Color> VisibleColors(string rich)
        {
            var result = new List<Color>();
            var stack = new List<Color>();
            bool noparse = false;
            int i = 0;
            while (i < rich.Length)
            {
                Match m = rich[i] == '<' ? TagAt.Match(rich, i) : Match.Empty;
                if (m.Success)
                {
                    string name = m.Groups[4].Success ? "color" : m.Groups[2].Value.ToLowerInvariant();
                    bool closing = m.Groups[1].Value == "/";
                    if (!noparse || (closing && name == "noparse"))
                    {
                        i += m.Length;
                        if (name == "noparse") noparse = !closing;
                        else if (name == "color")
                        {
                            if (closing) { if (stack.Count > 0) stack.RemoveAt(stack.Count - 1); }
                            else stack.Add(ParseColor(m, stack.Count > 0 ? stack[stack.Count - 1] : Color.white));
                        }
                        continue;
                    }
                }

                string element = StringInfo.GetNextTextElement(rich, i);
                if (!string.IsNullOrWhiteSpace(element) && stack.Count > 0) result.Add(stack[stack.Count - 1]);
                i += Math.Max(1, element.Length);
            }
            return result;
        }

        private static Color ParseColor(Match m, Color fallback)
        {
            string value = m.Groups[4].Success
                ? m.Groups[4].Value
                : m.Groups[3].Value.TrimStart('=', ' ').Trim('"', '\'');
            return ColorUtility.TryParseHtmlString(value, out Color c) ? c : fallback;
        }

        /// <summary>
        /// The same label for an IMGUI preview. IMGUI only knows b, i, size (in pixels) and
        /// color, and prints unbalanced tags as text, so percent sizes become pixels,
        /// TextMeshPro-only tags are dropped and what remains is re-balanced.
        /// </summary>
        public static string ToImgui(string rich, int baseSize)
        {
            if (string.IsNullOrEmpty(rich)) return rich ?? string.Empty;

            var sb = new StringBuilder(rich.Length);
            var open = new List<string>();
            bool noparse = false;
            int i = 0;
            while (i < rich.Length)
            {
                Match m = rich[i] == '<' ? TagAt.Match(rich, i) : Match.Empty;
                if (m.Success)
                {
                    string name = m.Groups[4].Success ? "color" : m.Groups[2].Value.ToLowerInvariant();
                    bool closing = m.Groups[1].Value == "/";
                    if (!noparse || (closing && name == "noparse"))
                    {
                        i += m.Length;
                        if (name == "noparse") { noparse = !closing; continue; }
                        if (name == "br") { sb.Append('\n'); continue; }
                        if (name != "b" && name != "i" && name != "color" && name != "size") continue;

                        if (closing)
                        {
                            int at = open.LastIndexOf(name);
                            if (at < 0) continue;
                            for (int k = open.Count - 1; k >= at; k--) sb.Append("</").Append(open[k]).Append('>');
                            open.RemoveRange(at, open.Count - at);
                            continue;
                        }

                        sb.Append(ImguiOpenTag(name, m, baseSize));
                        open.Add(name);
                        continue;
                    }
                }

                string element = StringInfo.GetNextTextElement(rich, i);
                sb.Append(element);
                i += Math.Max(1, element.Length);
            }

            for (int k = open.Count - 1; k >= 0; k--) sb.Append("</").Append(open[k]).Append('>');
            return sb.ToString();
        }

        private static string ImguiOpenTag(string name, Match m, int baseSize)
        {
            switch (name)
            {
                case "b": return "<b>";
                case "i": return "<i>";
                case "color":
                    return "<color=#" + ColorUtility.ToHtmlStringRGBA(ParseColor(m, Color.white)) + ">";
                default:
                {
                    string value = m.Groups[3].Value.TrimStart('=', ' ').Trim('"', '\'').ToLowerInvariant();
                    float px = baseSize;
                    if (value.EndsWith("%", StringComparison.Ordinal))
                    {
                        if (float.TryParse(value.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out float pct))
                            px = baseSize * pct / 100f;
                    }
                    else if (value.EndsWith("em", StringComparison.Ordinal))
                    {
                        if (float.TryParse(value.Substring(0, value.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out float em))
                            px = baseSize * em;
                    }
                    else if (float.TryParse(value.Replace("px", string.Empty), NumberStyles.Float, CultureInfo.InvariantCulture, out float abs))
                    {
                        // Absolute TextMeshPro sizes are relative to the menu's own label size.
                        px = baseSize * abs / 36f;
                    }
                    return "<size=" + Mathf.Clamp(Mathf.RoundToInt(px), 6, 48).ToString(CultureInfo.InvariantCulture) + ">";
                }
            }
        }
    }

    /// <summary>Menu-level operations, kept out of the window so they can be scripted and tested.</summary>
    public static class KawaiiMenuColorizerOps
    {
        public sealed class Theme
        {
            public readonly string Name;
            public readonly bool Rainbow;
            public readonly string[] Pairs;   // "RRGGBB-RRGGBB" gradient ends

            public Theme(string name, bool rainbow, params string[] pairs)
            {
                Name = name; Rainbow = rainbow; Pairs = pairs;
            }
        }

        public static readonly Theme[] Themes =
        {
            new Theme("Rainbow", true),
            new Theme("Candy", false, "FF6EC7-B266FF", "B266FF-4DC3FF", "4DC3FF-5CFFB0", "5CFFB0-FFE14D", "FFE14D-FF9F43", "FF9F43-FF6EC7"),
            new Theme("Neon", false, "00F0FF-FF00E6", "FF00E6-FFF200", "FFF200-00FF85", "00FF85-00F0FF", "B000FF-00F0FF", "FF2E63-FFF200"),
            new Theme("Sunset", false, "FFE14D-FF7A2E", "FF7A2E-FF3B6B", "FF3B6B-C04DFF", "FFB13B-FF4D6D", "FF9F43-FF6EC7", "FFD86B-FF5C5C"),
            new Theme("Ocean", false, "4DE8FF-4D7BFF", "2EE6C5-4DA6FF", "4DA6FF-8C6BFF", "7DF9FF-2EE6C5", "3FA9FF-6BFFE1", "5CC8FF-B3E5FF"),
            new Theme("Sakura", false, "FFD1E8-FF6EC7", "FFB3D9-E2B8FF", "FF8FC7-FFD1E8", "FFC2E0-FF5CA8", "F9A8D4-C4B5FD", "FFE4F1-FF85C0"),
            new Theme("Fire", false, "FFF15C-FF7A1A", "FFB13B-FF3B3B", "FF7A1A-FF2E4D", "FFE14D-FF4D4D", "FF9F43-E8204E", "FFD24D-FF5C1A"),
            new Theme("Ice", false, "E6FBFF-4DE8FF", "B3E5FF-5C9DFF", "D6F0FF-8C6BFF", "A8F0FF-2EC5E6", "CFE8FF-7DB8FF", "F0FAFF-9AD9FF"),
            new Theme("Galaxy", false, "C04DFF-4D7BFF", "FF4DD2-8C6BFF", "8C6BFF-4DE8FF", "B266FF-FF6EC7", "6B5CFF-C04DFF", "4D7BFF-B266FF"),
            new Theme("Toxic", false, "C6FF4D-2EE65C", "2EE65C-2EE6C5", "F2FF4D-8CFF4D", "8CFF4D-4DFFD2", "D4FF5C-5CFF8E", "5CFF5C-E6FF4D"),
            new Theme("Gold", false, "FFF3B0-FFC83D", "FFE08A-FF9F1C", "FFD24D-FFB13B", "FFF0C2-FFCC4D", "FFC83D-FF8C1A", "FFE9A8-FFB84D"),
            new Theme("Pastel", false, "FFC3E1-C3D4FF", "C3D4FF-C3FFE8", "C3FFE8-FFF5C3", "FFF5C3-FFD6C3", "E2C3FF-FFC3E1", "C3F3FF-E2C3FF"),
        };

        public static void ParsePair(string pair, out Color a, out Color b)
        {
            string[] ends = pair.Split('-');
            if (!ColorUtility.TryParseHtmlString("#" + ends[0], out a)) a = Color.white;
            if (!ColorUtility.TryParseHtmlString("#" + ends[ends.Length - 1], out b)) b = a;
        }

        /// <summary>The style a theme gives the label at <paramref name="index"/> (counted across the whole menu).</summary>
        public static KSLabelStyle ThemeStyle(int theme, int index, bool eachDifferent, bool bold, bool italic, int sizePercent)
        {
            Theme t = Themes[Mathf.Clamp(theme, 0, Themes.Length - 1)];
            var s = new KSLabelStyle { bold = bold, italic = italic, sizePercent = sizePercent };
            int i = eachDifferent ? Mathf.Max(0, index) : 0;
            if (t.Rainbow)
            {
                s.mode = KSLabelMode.Rainbow;
                s.hueStart = Mathf.Repeat(i * 0.137f, 1f);
                s.hueSpan = 0.8f;
                s.saturation = 0.72f;
            }
            else
            {
                s.mode = KSLabelMode.Gradient;
                ParsePair(t.Pairs[i % t.Pairs.Length], out s.colorA, out s.colorB);
            }
            return s;
        }

        /// <summary>The root menu and every sub-menu reachable from it, root first, each once.</summary>
        public static List<VRCExpressionsMenu> CollectMenus(VRCExpressionsMenu root)
        {
            var result = new List<VRCExpressionsMenu>();
            if (root == null) return result;

            var seen = new HashSet<VRCExpressionsMenu> { root };
            var queue = new Queue<VRCExpressionsMenu>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                VRCExpressionsMenu menu = queue.Dequeue();
                result.Add(menu);
                if (menu.controls == null) continue;
                foreach (VRCExpressionsMenu.Control c in menu.controls)
                {
                    if (c != null && c.type == VRCExpressionsMenu.Control.ControlType.SubMenu &&
                        c.subMenu != null && seen.Add(c.subMenu))
                        queue.Enqueue(c.subMenu);
                }
            }
            return result;
        }

        /// <summary>A menu inside an immutable package (registry, git) can't be saved; skip it.</summary>
        public static bool IsReadOnly(VRCExpressionsMenu menu)
        {
            if (menu == null) return true;
            string path = AssetDatabase.GetAssetPath(menu);
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Packages/", StringComparison.Ordinal)) return false;
            UnityEditor.PackageManager.PackageInfo info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);
            return info == null ||
                   (info.source != UnityEditor.PackageManager.PackageSource.Embedded &&
                    info.source != UnityEditor.PackageManager.PackageSource.Local);
        }

        /// <summary>
        /// Rewrite every button label (and puppet label) of <paramref name="menus"/> with the
        /// style <paramref name="styleFor"/> returns for its index; null means plain text.
        /// One undo step. Returns how many labels changed.
        /// </summary>
        public static int Colorize(IEnumerable<VRCExpressionsMenu> menus, Func<int, KSLabelStyle> styleFor, string undoName)
        {
            Undo.SetCurrentGroupName(undoName);
            int group = Undo.GetCurrentGroup();
            int written = 0;
            int index = 0;

            foreach (VRCExpressionsMenu menu in menus)
            {
                if (menu == null || menu.controls == null || IsReadOnly(menu)) continue;
                Undo.RecordObject(menu, undoName);
                bool changed = false;

                foreach (VRCExpressionsMenu.Control c in menu.controls)
                {
                    if (c == null) continue;
                    int at = index++;
                    if (Rewrite(ref c.name, styleFor(at))) { written++; changed = true; }

                    if (c.labels == null) continue;
                    for (int j = 0; j < c.labels.Length; j++)
                        if (Rewrite(ref c.labels[j].name, styleFor(at + j + 1))) { written++; changed = true; }
                }

                if (changed) EditorUtility.SetDirty(menu);
            }

            Undo.CollapseUndoOperations(group);
            return written;
        }

        private static bool Rewrite(ref string label, KSLabelStyle style)
        {
            if (string.IsNullOrEmpty(label)) return false;
            string next = KawaiiMenuRichText.Build(KawaiiMenuRichText.StripTags(label), style);
            if (next == label) return false;
            label = next;
            return true;
        }
    }

    public class KawaiiMenuColorizer : EditorWindow
    {
        private const string VERSION = KawaiiStudioVersion.Current;

        [SerializeField] private VRCAvatarDescriptor _avatar;
        [SerializeField] private VRCExpressionsMenu _menuAsset;   // used when no avatar is picked
        [SerializeField] private int _theme = 1;
        [SerializeField] private bool _eachDifferent = true;
        [SerializeField] private bool _bold = true;
        [SerializeField] private bool _italic;
        [SerializeField] private int _size = 100;

        private List<VRCExpressionsMenu> _menus = new List<VRCExpressionsMenu>();
        private readonly List<VRCExpressionsMenu> _path = new List<VRCExpressionsMenu>();   // preview page: root .. current

        private int _selected = -1;
        private KSLabelStyle _edit;
        private string _editText = string.Empty;
        private string _editSource;   // the label _edit was read from; a mismatch means it changed under us

        private Vector2 _scroll;
        private bool _showRaw;
        private bool _dirty;
        private int _hover = -1;
        private string _status;
        private Action _deferred;

        // VRChat's own radial-menu colours, so the preview reads like the real thing.
        private static readonly Color RingColor = new Color(0.12f, 0.31f, 0.35f);
        private static readonly Color RingEdge = new Color(0.30f, 0.62f, 0.66f, 0.9f);
        private static readonly Color SpokeColor = new Color(1f, 1f, 1f, 0.07f);
        private static readonly Color CentreColor = new Color(0.14f, 0.15f, 0.19f);

        private GUIStyle _previewLabel, _previewBuiltin, _previewCentre, _bigPreview, _hint;
        private readonly Dictionary<bool, GUIStyle> _chipStyles = new Dictionary<bool, GUIStyle>();

        private struct Slot
        {
            public int Control;      // index into the page's controls, -1 for VRChat's own buttons
            public string Builtin;
        }

        [MenuItem("Kawaii Studio/VRC/Menu Colorizer")]
        public static void ShowWindow() => Open(null);

        [MenuItem("Assets/Kawaii Studio/Colorize VRChat Menu", true)]
        private static bool CanOpenFromAsset() => Selection.activeObject is VRCExpressionsMenu;

        [MenuItem("Assets/Kawaii Studio/Colorize VRChat Menu", false, 2000)]
        private static void OpenFromAsset() => Open(Selection.activeObject as VRCExpressionsMenu);

        private static void Open(VRCExpressionsMenu menu)
        {
            var window = GetWindow<KawaiiMenuColorizer>(L("mc_window", "Menu Colorizer"));
            window.minSize = new Vector2(460f, 620f);
            if (menu != null)
            {
                window._avatar = null;
                window._menuAsset = menu;
                window.Rebuild();
            }
            window.Show();
        }

        private static string L(string key, string english)
        {
            string value = KawaiiStudioLocalization.T(key);
            return string.IsNullOrEmpty(value) || value == key ? english : value;
        }

        private VRCExpressionsMenu RootMenu => _avatar != null ? _avatar.expressionsMenu : _menuAsset;
        private VRCExpressionsMenu Page => _path.Count > 0 ? _path[_path.Count - 1] : null;

        // ─────────────────────────────────────────────────────────────────
        //  LIFECYCLE
        // ─────────────────────────────────────────────────────────────────

        private void OnEnable()
        {
            KawaiiStudioGUI.Initialize();
            wantsMouseMove = true;
            if (_avatar == null && _menuAsset == null) _avatar = FindAvatar();
            Rebuild();
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            SaveIfDirty();
        }

        private void OnLostFocus() => SaveIfDirty();

        private void OnUndoRedo()
        {
            _editSource = null;   // re-read the selected label on the next layout pass
            Repaint();
        }

        private void OnHierarchyChange() => Repaint();

        private static VRCAvatarDescriptor FindAvatar()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected != null)
            {
                var onSelection = selected.GetComponentInParent<VRCAvatarDescriptor>();
                if (onSelection != null) return onSelection;
            }
            VRCAvatarDescriptor[] all = FindObjectsOfType<VRCAvatarDescriptor>();
            return all.Length > 0 ? all[0] : null;
        }

        /// <summary>Start over from the root menu (new avatar or menu picked).</summary>
        private void Rebuild()
        {
            _path.Clear();
            _selected = -1;
            _editSource = null;
            RefreshTree();
        }

        /// <summary>
        /// Re-collect the menu tree, keeping the open page and selection when they still exist.
        /// Runs on every layout pass, so menus edited elsewhere (descriptor, inspector) show up.
        /// </summary>
        private void RefreshTree()
        {
            VRCExpressionsMenu root = RootMenu;
            _menus = KawaiiMenuColorizerOps.CollectMenus(root);
            bool pathValid = root != null && _path.Count > 0 && _path[0] == root &&
                             _path.All(m => m != null && _menus.Contains(m));
            if (pathValid) return;

            _path.Clear();
            if (root != null) _path.Add(root);
            _selected = -1;
            _editSource = null;
        }

        private void SaveIfDirty()
        {
            if (!_dirty) return;
            _dirty = false;
            AssetDatabase.SaveAssets();
        }

        // Anything that changes what gets drawn waits for the end of the current event, so
        // the layout and the input passes always see the same controls.
        private void Defer(Action action) => _deferred += action;

        // ─────────────────────────────────────────────────────────────────
        //  GUI
        // ─────────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            KawaiiStudioGUI.DrawWindowBackground(position);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            KawaiiStudioGUI.DrawBanner(
                L("mc_title", "MENU COLORIZER"),
                L("mc_subtitle", "Colourful VRChat menus in one click"),
                VERSION,
                KawaiiStudioBranding.Logo,
                KawaiiStudioBranding.Banner);

            SyncSelection();

            KawaiiStudioGUI.DrawSection(L("mc_section_avatar", "1 · AVATAR"), DrawSource);
            if (RootMenu != null)
            {
                KawaiiStudioGUI.DrawSection(L("mc_section_quick", "2 · ONE-CLICK STYLE"), DrawQuickStyle);
                KawaiiStudioGUI.DrawSection(L("mc_section_preview", "3 · PREVIEW & FINE-TUNE"), DrawPreviewAndEditor);
            }

            KawaiiStudioGUI.DrawFooter();
            EditorGUILayout.EndScrollView();

            if (_deferred != null && Event.current.type != EventType.Layout)
            {
                Action run = _deferred;
                _deferred = null;
                run();
                Repaint();
            }
        }

        /// <summary>Re-read the selected label when it changed underneath us (undo, inspector edit).</summary>
        private void SyncSelection()
        {
            if (Event.current.type != EventType.Layout) return;
            RefreshTree();

            VRCExpressionsMenu page = Page;
            if (page == null || page.controls == null || _selected >= page.controls.Count) _selected = -1;
            if (_selected < 0) return;

            string name = page.controls[_selected].name ?? string.Empty;
            if (name == _editSource && _edit != null) return;
            _editSource = name;
            _editText = KawaiiMenuRichText.StripTags(name);
            _edit = KawaiiMenuRichText.Guess(name);
        }

        private void DrawSource()
        {
            EditorGUI.BeginChangeCheck();
            var avatar = (VRCAvatarDescriptor)EditorGUILayout.ObjectField(
                L("mc_avatar", "Avatar"), _avatar, typeof(VRCAvatarDescriptor), true);
            if (EditorGUI.EndChangeCheck())
                Defer(() => { _avatar = avatar; if (avatar != null) _menuAsset = null; Rebuild(); });

            if (_avatar == null)
            {
                EditorGUI.BeginChangeCheck();
                var menu = (VRCExpressionsMenu)EditorGUILayout.ObjectField(
                    L("mc_or_menu", "...or a menu asset"), _menuAsset, typeof(VRCExpressionsMenu), false);
                if (EditorGUI.EndChangeCheck())
                    Defer(() => { _menuAsset = menu; Rebuild(); });
            }

            if (RootMenu == null)
            {
                KawaiiStudioGUI.Banner(_avatar != null
                        ? L("mc_no_menu_avatar", "This avatar has no Expressions Menu yet. Add one in its VRC Avatar Descriptor (Expressions > Customize), then come back.")
                        : L("mc_pick_avatar", "Drop your avatar here, or press the button to use the one in the open scene."),
                    KawaiiStudioGUI.MessageKind.Info);
                if (_avatar == null && KawaiiStudioGUI.SecondaryButton(L("mc_find", "Find my avatar")))
                    Defer(() => { _avatar = FindAvatar(); Rebuild(); });
            }
            else
            {
                int buttons = _menus.Sum(m => m.controls != null ? m.controls.Count : 0);
                KawaiiStudioGUI.KeyValueRow(L("mc_found", "Found"),
                    string.Format(L("mc_found_fmt", "{0} menus · {1} buttons"), _menus.Count, buttons));

                int locked = _menus.Count(KawaiiMenuColorizerOps.IsReadOnly);
                if (locked > 0)
                    KawaiiStudioGUI.Banner(string.Format(
                        L("mc_locked", "{0} menu(s) live in a read-only package and will be skipped. Duplicate them into Assets to colour them."),
                        locked), KawaiiStudioGUI.MessageKind.Warning);
            }

            // The language is shared with every other Kawaii Studio tool.
            int current = Array.IndexOf(KawaiiStudioLocalization.AvailableLanguages, KawaiiStudioLocalization.CurrentLanguage);
            int picked = EditorGUILayout.Popup(L("mc_language", "Language"), Mathf.Max(0, current),
                KawaiiStudioLocalization.LanguageDisplayNames);
            if (picked != Mathf.Max(0, current))
                KawaiiStudioLocalization.CurrentLanguage = KawaiiStudioLocalization.AvailableLanguages[picked];
        }

        private void DrawQuickStyle()
        {
            GUILayout.Label(L("mc_pick_theme", "Pick a look, then colour everything:"), KawaiiStudioGUI.InfoLabelStyle);
            GUILayout.Space(KawaiiStudioGUI.Space1);

            KawaiiMenuColorizerOps.Theme[] themes = KawaiiMenuColorizerOps.Themes;
            int columns = Mathf.Clamp(Mathf.FloorToInt((position.width - 70f) / 112f), 2, 6);
            for (int row = 0; row * columns < themes.Length; row++)
            {
                EditorGUILayout.BeginHorizontal();
                for (int col = 0; col < columns; col++)
                {
                    int i = row * columns + col;
                    if (i < themes.Length)
                    {
                        if (ThemeChip(i)) _theme = i;
                    }
                    else
                    {
                        GUILayout.Label(GUIContent.none, GUILayout.Height(28f), GUILayout.MinWidth(90f));
                    }
                }
                EditorGUILayout.EndHorizontal();
            }

            GUILayout.Space(KawaiiStudioGUI.Space2);
            _eachDifferent = KawaiiStudioGUI.DrawToggle(L("mc_each_different", "Give every button its own colours"), _eachDifferent);
            EditorGUILayout.BeginHorizontal();
            _bold = KawaiiStudioGUI.DrawToggle(L("mc_bold", "Bold"), _bold);
            _italic = KawaiiStudioGUI.DrawToggle(L("mc_italic", "Italic"), _italic);
            EditorGUILayout.EndHorizontal();
            _size = EditorGUILayout.IntSlider(L("mc_size", "Text size %"), _size, 50, 200);

            GUILayout.Space(KawaiiStudioGUI.Space2);
            DrawThemeSample();
            GUILayout.Space(KawaiiStudioGUI.Space2);

            if (KawaiiStudioGUI.PrimaryButton("✨ " + L("mc_apply_all", "COLOUR MY WHOLE MENU"), GUILayout.Height(40f)))
                Defer(ApplyThemeToAll);
            if (KawaiiStudioGUI.SecondaryButton(L("mc_clear_all", "Remove all colours")))
                Defer(ClearAll);

            if (!string.IsNullOrEmpty(_status))
                KawaiiStudioGUI.Banner(_status, KawaiiStudioGUI.MessageKind.Success);
        }

        private bool ThemeChip(int index)
        {
            KawaiiMenuColorizerOps.Theme theme = KawaiiMenuColorizerOps.Themes[index];
            KSLabelStyle style = KawaiiMenuColorizerOps.ThemeStyle(index, 0, false, true, false, 100);
            string label = KawaiiMenuRichText.ToImgui(KawaiiMenuRichText.Build(theme.Name, style), 12);
            return GUILayout.Button(label, ChipStyle(index == _theme), GUILayout.Height(28f), GUILayout.MinWidth(90f));
        }

        private GUIStyle ChipStyle(bool selected)
        {
            if (_chipStyles.TryGetValue(selected, out GUIStyle cached) && cached != null && cached.normal.background != null)
                return cached;

            Color fill = selected ? KawaiiStudioGUI.Fade(KawaiiStudioGUI.AccentColor, 0.30f) : KawaiiStudioGUI.FieldBackground;
            Color border = selected ? KawaiiStudioGUI.AccentColor : KawaiiStudioGUI.BorderColor;
            var style = new GUIStyle(EditorStyles.label)
            {
                richText = true,
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
                margin = new RectOffset(2, 2, 2, 2),
                border = new RectOffset(KawaiiStudioGUI.RadiusMd, KawaiiStudioGUI.RadiusMd, KawaiiStudioGUI.RadiusMd, KawaiiStudioGUI.RadiusMd),
            };
            style.normal.background = KawaiiStudioGUI.GetRoundedTexture(fill, border, KawaiiStudioGUI.RadiusMd, selected ? 2 : 1);
            style.hover.background = KawaiiStudioGUI.GetRoundedTexture(KawaiiStudioGUI.Lighten(fill, 0.06f), border, KawaiiStudioGUI.RadiusMd, selected ? 2 : 1);
            style.active.background = style.hover.background;
            style.normal.textColor = style.hover.textColor = style.active.textColor = KawaiiStudioGUI.TextColor;
            _chipStyles[selected] = style;
            return style;
        }

        /// <summary>How the chosen look lands on the first few real buttons, before anything is written.</summary>
        private void DrawThemeSample()
        {
            var names = new List<string>();
            foreach (VRCExpressionsMenu menu in _menus)
            {
                if (menu.controls == null) continue;
                foreach (VRCExpressionsMenu.Control c in menu.controls)
                {
                    if (c == null) continue;
                    names.Add(c.name);
                    if (names.Count == 4) break;
                }
                if (names.Count == 4) break;
            }
            if (names.Count == 0) return;

            Rect r = GUILayoutUtility.GetRect(0f, 34f, GUILayout.ExpandWidth(true));
            if (Event.current.type != EventType.Repaint) return;

            GUI.DrawTexture(r, KawaiiStudioGUI.GetRoundedTexture(CentreColor, Color.clear, KawaiiStudioGUI.RadiusMd, 0), ScaleMode.StretchToFill, true);
            float w = r.width / names.Count;
            for (int i = 0; i < names.Count; i++)
            {
                KSLabelStyle s = KawaiiMenuColorizerOps.ThemeStyle(_theme, i, _eachDifferent, _bold, _italic, _size);
                string label = KawaiiMenuRichText.Build(KawaiiMenuRichText.StripTags(names[i]).Split('\n')[0], s);
                GUI.Label(new Rect(r.x + w * i, r.y, w, r.height), KawaiiMenuRichText.ToImgui(label, 12), PreviewCentre(12));
            }
        }

        private void DrawPreviewAndEditor()
        {
            DrawBreadcrumb();

            float size = Mathf.Clamp(position.width - 80f, 260f, 420f);
            Rect area = GUILayoutUtility.GetRect(size, size, GUILayout.ExpandWidth(true));
            DrawRadial(new Rect(area.center.x - size / 2f, area.y, size, size));

            GUILayout.Label(L("mc_preview_hint", "Click a button to edit it · double-click a sub-menu to open it"), Hint);
            GUILayout.Space(KawaiiStudioGUI.Space2);
            DrawEditor();
        }

        private void DrawBreadcrumb()
        {
            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < _path.Count; i++)
            {
                if (i > 0) GUILayout.Label("›", KawaiiStudioGUI.InfoLabelStyle, GUILayout.Width(10f));
                bool last = i == _path.Count - 1;
                string name = _path[i] != null ? _path[i].name : "?";
                if (GUILayout.Button(name, last ? EditorStyles.boldLabel : EditorStyles.linkLabel, GUILayout.ExpandWidth(false)) && !last)
                {
                    int depth = i;
                    Defer(() => NavigateTo(depth));
                }
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void NavigateTo(int depth)
        {
            if (depth >= 0 && depth < _path.Count - 1) _path.RemoveRange(depth + 1, _path.Count - depth - 1);
            _selected = -1;
            _editSource = null;
        }

        private void EnterSubMenu(VRCExpressionsMenu sub)
        {
            if (sub == null || _path.Contains(sub)) return;
            _path.Add(sub);
            _selected = -1;
            _editSource = null;
        }

        // ── Radial preview ─────────────────────────────────────────────────

        private List<Slot> BuildSlots(VRCExpressionsMenu page)
        {
            var slots = new List<Slot> { new Slot { Control = -1, Builtin = L("mc_back", "Back") } };
            if (_path.Count == 1) slots.Add(new Slot { Control = -1, Builtin = L("mc_quick_actions", "Quick Actions") });
            if (page.controls != null)
                for (int i = 0; i < page.controls.Count; i++)
                    slots.Add(new Slot { Control = i });
            return slots;
        }

        private void DrawRadial(Rect square)
        {
            VRCExpressionsMenu page = Page;
            if (page == null) return;

            List<Slot> slots = BuildSlots(page);
            int n = slots.Count;
            Vector2 c = square.center;
            float outer = square.width / 2f - 2f;
            float inner = outer * 0.40f;
            float band = outer - inner;
            float middle = (inner + outer) / 2f;
            Event e = Event.current;

            int hit = SlotAt(e.mousePosition, c, inner, outer, n);
            if (e.type == EventType.MouseMove && hit != _hover)
            {
                _hover = hit;
                Repaint();
            }
            if (e.type == EventType.MouseDown && e.button == 0 && hit >= 0)
            {
                Slot slot = slots[hit];
                if (slot.Control < 0)
                {
                    if (hit == 0 && _path.Count > 1) Defer(() => NavigateTo(_path.Count - 2));
                }
                else
                {
                    VRCExpressionsMenu.Control control = page.controls[slot.Control];
                    int index = slot.Control;
                    if (e.clickCount >= 2 && control.type == VRCExpressionsMenu.Control.ControlType.SubMenu && control.subMenu != null)
                    {
                        VRCExpressionsMenu sub = control.subMenu;
                        Defer(() => EnterSubMenu(sub));
                    }
                    else
                    {
                        Defer(() => { _selected = index; _editSource = null; });
                    }
                }
                e.Use();
            }

            if (e.type != EventType.Repaint) return;

            DrawDisc(c, outer, RingColor);
            DrawDisc(c, outer, RingEdge, 2f);
            for (int i = 0; i < n; i++) DrawSpoke(c, inner, outer, (i + 0.5f) * 360f / n);
            DrawDisc(c, inner, CentreColor);
            DrawDisc(c, inner, RingEdge, 1.5f);

            float arc = 2f * Mathf.PI * middle / n;
            float iconSize = Mathf.Min(band * 0.46f, arc * 0.5f);
            float labelWidth = Mathf.Min(arc * 0.98f, band * 1.25f);
            for (int i = 0; i < n; i++)
            {
                float angle = i * 2f * Mathf.PI / n;
                Vector2 p = c + new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle)) * middle;
                Slot slot = slots[i];

                if (slot.Control >= 0 && slot.Control == _selected)
                    DrawDisc(p, band * 0.47f, KawaiiStudioGUI.Fade(KawaiiStudioGUI.AccentColor, 0.45f));
                else if (i == _hover)
                    DrawDisc(p, band * 0.47f, new Color(1f, 1f, 1f, 0.08f));

                var iconRect = new Rect(p.x - iconSize / 2f, p.y - iconSize * 0.8f, iconSize, iconSize);
                var labelRect = new Rect(p.x - labelWidth / 2f, iconRect.yMax, labelWidth, band * 0.5f);

                if (slot.Control < 0)
                {
                    DrawDisc(iconRect.center, iconSize * 0.3f, new Color(1f, 1f, 1f, 0.18f));
                    GUI.Label(labelRect, slot.Builtin, PreviewLabel(true));
                    continue;
                }

                VRCExpressionsMenu.Control control = page.controls[slot.Control];
                if (control == null) continue;
                if (control.icon != null) GUI.DrawTexture(iconRect, control.icon, ScaleMode.ScaleToFit, true);
                else DrawDisc(iconRect.center, iconSize * 0.3f, new Color(1f, 1f, 1f, 0.25f));
                GUI.Label(labelRect, KawaiiMenuRichText.ToImgui(control.name, 10), PreviewLabel(false));
            }

            GUI.Label(new Rect(c.x - inner * 0.9f, c.y - 10f, inner * 1.8f, 20f), page.name, PreviewCentre(10));
        }

        private static int SlotAt(Vector2 mouse, Vector2 centre, float inner, float outer, int n)
        {
            Vector2 d = mouse - centre;
            float distance = d.magnitude;
            if (n == 0 || distance < inner || distance > outer) return -1;
            float angle = Mathf.Atan2(d.x, -d.y) * Mathf.Rad2Deg;   // 0 = up, clockwise
            if (angle < 0f) angle += 360f;
            return Mathf.RoundToInt(angle / (360f / n)) % n;
        }

        private static void DrawDisc(Vector2 centre, float radius, Color color, float border = 0f)
        {
            var r = new Rect(centre.x - radius, centre.y - radius, radius * 2f, radius * 2f);
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, color, border, radius);
        }

        private static void DrawSpoke(Vector2 centre, float inner, float outer, float angleDegrees)
        {
            Matrix4x4 saved = GUI.matrix;
            GUIUtility.RotateAroundPivot(angleDegrees, centre);
            EditorGUI.DrawRect(new Rect(centre.x - 0.75f, centre.y - outer + 2f, 1.5f, outer - inner - 2f), SpokeColor);
            GUI.matrix = saved;
        }

        // ── Selected button ────────────────────────────────────────────────

        private void DrawEditor()
        {
            VRCExpressionsMenu page = Page;
            if (page == null || page.controls == null || _selected < 0 || _selected >= page.controls.Count || _edit == null)
            {
                KawaiiStudioGUI.EmptyState(
                    L("mc_nothing_selected", "No button selected"),
                    L("mc_nothing_selected_hint", "Click a button in the preview above to change its text and colours."));
                return;
            }

            VRCExpressionsMenu.Control control = page.controls[_selected];
            bool locked = KawaiiMenuColorizerOps.IsReadOnly(page);

            EditorGUILayout.BeginHorizontal();
            KawaiiStudioGUI.Badge(TypeName(control.type), KawaiiStudioGUI.AccentColor);
            GUILayout.FlexibleSpace();
            if (control.type == VRCExpressionsMenu.Control.ControlType.SubMenu && control.subMenu != null &&
                KawaiiStudioGUI.SecondaryButton(L("mc_open_sub", "Open sub-menu ›"), GUILayout.Width(160f)))
            {
                VRCExpressionsMenu sub = control.subMenu;
                Defer(() => EnterSubMenu(sub));
            }
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(KawaiiStudioGUI.Space1);

            // What VRChat will show, big.
            int lines = (control.name ?? string.Empty).Count(ch => ch == '\n') + 1;
            Rect preview = GUILayoutUtility.GetRect(0f, 18f + 22f * lines, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(preview, KawaiiStudioGUI.GetRoundedTexture(RingColor, Color.clear, KawaiiStudioGUI.RadiusMd, 0), ScaleMode.StretchToFill, true);
                GUI.Label(preview, KawaiiMenuRichText.ToImgui(control.name, 16), BigPreview);
            }
            GUILayout.Space(KawaiiStudioGUI.Space2);

            if (locked)
                KawaiiStudioGUI.Banner(L("mc_locked_one", "This menu lives in a read-only package; duplicate it into Assets to edit it."),
                    KawaiiStudioGUI.MessageKind.Warning);

            using (new EditorGUI.DisabledScope(locked))
            {
                GUILayout.Label(L("mc_text", "Text (Enter = new line)"), KawaiiStudioGUI.InfoLabelStyle);
                EditorGUI.BeginChangeCheck();
                _editText = EditorGUILayout.TextArea(_editText, GUILayout.MinHeight(36f));
                bool textChanged = EditorGUI.EndChangeCheck();

                GUILayout.Space(KawaiiStudioGUI.Space2);
                string[] modes =
                {
                    L("mc_mode_plain", "Plain"), L("mc_mode_solid", "Solid"),
                    L("mc_mode_gradient", "Gradient"), L("mc_mode_rainbow", "Rainbow")
                };
                int mode = KawaiiStudioGUI.Tabs((int)_edit.mode, modes);
                if (mode != (int)_edit.mode)
                {
                    var next = (KSLabelMode)mode;
                    Defer(() => ChangeMode(next));
                }
                GUILayout.Space(KawaiiStudioGUI.Space1);

                bool boldBefore = _edit.bold, italicBefore = _edit.italic;
                EditorGUI.BeginChangeCheck();
                switch (_edit.mode)
                {
                    case KSLabelMode.Solid:
                        _edit.colorA = EditorGUILayout.ColorField(new GUIContent(L("mc_color", "Colour")), _edit.colorA, true, false, false);
                        break;
                    case KSLabelMode.Gradient:
                        EditorGUILayout.BeginHorizontal();
                        _edit.colorA = EditorGUILayout.ColorField(GUIContent.none, _edit.colorA, true, false, false);
                        if (GUILayout.Button("↔", GUILayout.Width(28f)))
                        {
                            Color swap = _edit.colorA;
                            _edit.colorA = _edit.colorB;
                            _edit.colorB = swap;
                            GUI.changed = true;
                        }
                        _edit.colorB = EditorGUILayout.ColorField(GUIContent.none, _edit.colorB, true, false, false);
                        EditorGUILayout.EndHorizontal();
                        DrawPairSwatches();
                        break;
                    case KSLabelMode.Rainbow:
                        _edit.hueStart = EditorGUILayout.Slider(L("mc_hue_start", "Start colour"), _edit.hueStart, 0f, 1f);
                        _edit.hueSpan = EditorGUILayout.Slider(L("mc_hue_span", "Rainbow length"), _edit.hueSpan, -2f, 2f);
                        _edit.saturation = EditorGUILayout.Slider(L("mc_saturation", "Vividness"), _edit.saturation, 0.1f, 1f);
                        break;
                }

                EditorGUILayout.BeginHorizontal();
                _edit.bold = KawaiiStudioGUI.DrawToggle(L("mc_bold", "Bold"), _edit.bold);
                _edit.italic = KawaiiStudioGUI.DrawToggle(L("mc_italic", "Italic"), _edit.italic);
                EditorGUILayout.EndHorizontal();
                _edit.sizePercent = EditorGUILayout.IntSlider(L("mc_size", "Text size %"), _edit.sizePercent, 50, 200);
                bool styleChanged = EditorGUI.EndChangeCheck() || boldBefore != _edit.bold || italicBefore != _edit.italic;

                if (textChanged || styleChanged) ApplySelected();

                GUILayout.Space(KawaiiStudioGUI.Space2);
                EditorGUILayout.BeginHorizontal();
                if (KawaiiStudioGUI.SecondaryButton(L("mc_style_to_page", "Same style on this whole page")))
                    Defer(ApplyStyleToPage);
                if (KawaiiStudioGUI.SecondaryButton(L("mc_reset_label", "Remove colours")))
                    Defer(ResetSelected);
                EditorGUILayout.EndHorizontal();

                if (control.labels != null && control.labels.Length > 0 &&
                    KawaiiStudioGUI.SecondaryButton(L("mc_style_to_labels", "Same style on its puppet labels")))
                    Defer(ApplyStyleToLabels);
            }

            GUILayout.Space(KawaiiStudioGUI.Space1);
            bool raw = EditorGUILayout.Foldout(_showRaw, L("mc_raw", "Raw text (what VRChat receives)"), true);
            if (raw != _showRaw) Defer(() => _showRaw = raw);
            if (_showRaw)
            {
                EditorGUILayout.SelectableLabel(control.name ?? string.Empty, EditorStyles.textArea, GUILayout.MinHeight(48f));
                KawaiiStudioGUI.KeyValueRow(L("mc_raw_length", "Characters"), (control.name ?? string.Empty).Length.ToString(CultureInfo.InvariantCulture));
            }
        }

        private void DrawPairSwatches()
        {
            KawaiiMenuColorizerOps.Theme theme = KawaiiMenuColorizerOps.Themes[_theme];
            if (theme.Rainbow) theme = KawaiiMenuColorizerOps.Themes[1];

            EditorGUILayout.BeginHorizontal();
            foreach (string pair in theme.Pairs)
            {
                KawaiiMenuColorizerOps.ParsePair(pair, out Color a, out Color b);
                Rect r = GUILayoutUtility.GetRect(34f, 18f, GUILayout.Width(34f), GUILayout.Height(18f));
                if (Event.current.type == EventType.Repaint)
                    GUI.DrawTexture(r, KawaiiStudioGUI.GetHorizontalGradient(a, b), ScaleMode.StretchToFill);
                EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none))
                {
                    _edit.colorA = a;
                    _edit.colorB = b;
                    GUI.changed = true;
                }
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private string TypeName(VRCExpressionsMenu.Control.ControlType type)
        {
            switch (type)
            {
                case VRCExpressionsMenu.Control.ControlType.Button: return L("mc_type_button", "Button");
                case VRCExpressionsMenu.Control.ControlType.Toggle: return L("mc_type_toggle", "Toggle");
                case VRCExpressionsMenu.Control.ControlType.SubMenu: return L("mc_type_submenu", "Sub-menu");
                case VRCExpressionsMenu.Control.ControlType.TwoAxisPuppet: return L("mc_type_two_axis", "Two-axis puppet");
                case VRCExpressionsMenu.Control.ControlType.FourAxisPuppet: return L("mc_type_four_axis", "Four-axis puppet");
                case VRCExpressionsMenu.Control.ControlType.RadialPuppet: return L("mc_type_radial", "Radial puppet");
                default: return type.ToString();
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  WRITES
        // ─────────────────────────────────────────────────────────────────

        private int GlobalIndex(VRCExpressionsMenu page, int control)
        {
            int index = 0;
            foreach (VRCExpressionsMenu menu in _menus)
            {
                if (menu == page) return index + control;
                if (menu.controls != null) index += menu.controls.Count;
            }
            return control;
        }

        private void ChangeMode(KSLabelMode mode)
        {
            if (_edit == null) return;
            // Coming from plain text there are no colours worth keeping: start from the theme's.
            if (_edit.mode == KSLabelMode.Plain && mode != KSLabelMode.Plain)
            {
                KSLabelStyle seed = KawaiiMenuColorizerOps.ThemeStyle(_theme, GlobalIndex(Page, _selected), true, true, false, 100);
                _edit.colorA = seed.colorA;
                _edit.colorB = seed.colorB;
                _edit.hueStart = seed.hueStart;
                _edit.bold = true;
            }
            _edit.mode = mode;
            ApplySelected();
        }

        private void ApplySelected()
        {
            VRCExpressionsMenu page = Page;
            if (page == null || page.controls == null || _selected < 0 || _selected >= page.controls.Count) return;
            if (KawaiiMenuColorizerOps.IsReadOnly(page) || _edit == null) return;

            VRCExpressionsMenu.Control control = page.controls[_selected];
            string next = KawaiiMenuRichText.Build(_editText, _edit);
            if (next == control.name) return;

            Undo.RecordObject(page, L("mc_undo_label", "Colour menu label"));
            control.name = next;
            _editSource = next;
            EditorUtility.SetDirty(page);
            _dirty = true;
        }

        private void ResetSelected()
        {
            if (_edit == null) return;
            _edit.mode = KSLabelMode.Plain;
            _edit.bold = false;
            _edit.italic = false;
            _edit.sizePercent = 100;
            ApplySelected();
        }

        private void ApplyStyleToPage()
        {
            if (_edit == null || Page == null) return;
            KSLabelStyle style = _edit.Clone();
            int n = KawaiiMenuColorizerOps.Colorize(new[] { Page }, i => style, L("mc_undo_page", "Colour menu page"));
            AfterWrite(n);
        }

        private void ApplyStyleToLabels()
        {
            VRCExpressionsMenu page = Page;
            if (_edit == null || page == null || _selected < 0 || _selected >= page.controls.Count) return;
            VRCExpressionsMenu.Control control = page.controls[_selected];
            if (control.labels == null || KawaiiMenuColorizerOps.IsReadOnly(page)) return;

            Undo.RecordObject(page, L("mc_undo_labels", "Colour puppet labels"));
            int n = 0;
            for (int j = 0; j < control.labels.Length; j++)
            {
                if (string.IsNullOrEmpty(control.labels[j].name)) continue;
                control.labels[j].name = KawaiiMenuRichText.Build(KawaiiMenuRichText.StripTags(control.labels[j].name), _edit);
                n++;
            }
            EditorUtility.SetDirty(page);
            AfterWrite(n);
        }

        private void ApplyThemeToAll()
        {
            int theme = _theme, size = _size;
            bool each = _eachDifferent, bold = _bold, italic = _italic;
            int n = KawaiiMenuColorizerOps.Colorize(_menus,
                i => KawaiiMenuColorizerOps.ThemeStyle(theme, i, each, bold, italic, size),
                L("mc_undo_all", "Colour whole menu"));
            AfterWrite(n);
        }

        private void ClearAll()
        {
            int n = KawaiiMenuColorizerOps.Colorize(_menus, i => null, L("mc_undo_clear", "Remove menu colours"));
            AfterWrite(n);
        }

        private void AfterWrite(int changed)
        {
            _dirty = true;
            SaveIfDirty();
            _editSource = null;
            _status = string.Format(L("mc_done_fmt", "{0} labels updated. Ctrl+Z undoes it."), changed);
        }

        // ─────────────────────────────────────────────────────────────────
        //  STYLES
        // ─────────────────────────────────────────────────────────────────

        private GUIStyle PreviewLabel(bool builtin)
        {
            if (_previewLabel == null)
            {
                _previewLabel = new GUIStyle(EditorStyles.label)
                {
                    richText = true,
                    wordWrap = true,
                    alignment = TextAnchor.UpperCenter,
                    fontSize = 10,
                    clipping = TextClipping.Overflow,
                };
                _previewLabel.normal.textColor = Color.white;
                _previewBuiltin = new GUIStyle(_previewLabel) { richText = false };
                _previewBuiltin.normal.textColor = new Color(1f, 1f, 1f, 0.55f);
            }
            return builtin ? _previewBuiltin : _previewLabel;
        }

        private GUIStyle PreviewCentre(int size)
        {
            if (_previewCentre == null)
            {
                _previewCentre = new GUIStyle(EditorStyles.label)
                {
                    richText = true,
                    alignment = TextAnchor.MiddleCenter,
                    clipping = TextClipping.Overflow,
                };
                _previewCentre.normal.textColor = new Color(1f, 1f, 1f, 0.6f);
            }
            _previewCentre.fontSize = size;
            return _previewCentre;
        }

        private GUIStyle BigPreview
        {
            get
            {
                if (_bigPreview == null)
                {
                    _bigPreview = new GUIStyle(EditorStyles.label)
                    {
                        richText = true,
                        wordWrap = true,
                        alignment = TextAnchor.MiddleCenter,
                        fontSize = 16,
                    };
                    _bigPreview.normal.textColor = Color.white;
                }
                return _bigPreview;
            }
        }

        private GUIStyle Hint
        {
            get
            {
                if (_hint == null)
                {
                    _hint = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { wordWrap = true };
                }
                return _hint;
            }
        }
    }
}
