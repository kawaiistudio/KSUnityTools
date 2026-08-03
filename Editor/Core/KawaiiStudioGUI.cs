using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KawaiiStudio
{
    /// <summary>
    /// The Kawaii Studio design system.
    ///
    /// Every window in the toolset draws through this class so they share one
    /// identity: one spacing scale, one type ramp, one accent, one set of
    /// containers and controls.
    ///
    /// Everything is theme-aware. Unity ships a dark and a light editor skin, and
    /// a UI that only reads well in one of them is a bug, so all colours resolve
    /// through EditorGUIUtility.isProSkin and every cached style and texture is
    /// rebuilt when the user switches skin.
    ///
    /// All textures are generated procedurally and flagged HideAndDontSave, so the
    /// look costs nothing on disk and leaks nothing across assembly reloads.
    /// </summary>
    public static class KawaiiStudioGUI
    {
        // ─────────────────────────────────────────────────────────────────
        //  SPACING SCALE — one 4pt grid instead of scattered magic numbers
        // ─────────────────────────────────────────────────────────────────
        public const float Space1 = 4f;
        public const float Space2 = 8f;
        public const float Space3 = 12f;
        public const float Space4 = 16f;
        public const float Space5 = 24f;
        public const float Space6 = 32f;

        public const int RadiusSm = 4;
        public const int RadiusMd = 8;
        public const int RadiusLg = 12;

        public const float ButtonHeight = 32f;
        public const float BannerHeight = 96f;

        private static bool Pro => EditorGUIUtility.isProSkin;

        private static Color Hex(int rgb, float a = 1f)
        {
            return new Color(
                ((rgb >> 16) & 0xFF) / 255f,
                ((rgb >> 8) & 0xFF) / 255f,
                (rgb & 0xFF) / 255f,
                a);
        }

        // ─────────────────────────────────────────────────────────────────
        //  COLOUR TOKENS
        //  These used to be `static readonly` fields. They are properties now
        //  so they can follow the editor skin; read access is unchanged for
        //  every existing caller.
        // ─────────────────────────────────────────────────────────────────
        public static Color BaseBackground => Pro ? Hex(0x17141E) : Hex(0xF2F0F6);
        public static Color BaseBackgroundTop => Pro ? Hex(0x1E1928) : Hex(0xFAF8FD);
        public static Color BaseBackgroundBottom => Pro ? Hex(0x121016) : Hex(0xE9E6F0);

        /// <summary>Raised container surface (cards, sections).</summary>
        public static Color BoxBackground => Pro ? Hex(0x241E2F) : Hex(0xFFFFFF);

        /// <summary>Recessed surface (inputs, logs, wells).</summary>
        public static Color FieldBackground => Pro ? Hex(0x141119) : Hex(0xF4F2F8);

        public static Color BorderColor => Pro ? new Color(1f, 1f, 1f, 0.09f) : new Color(0f, 0f, 0f, 0.11f);
        public static Color BorderStrong => Pro ? new Color(1f, 1f, 1f, 0.16f) : new Color(0f, 0f, 0f, 0.18f);

        // Light skin needs darker, more saturated values to hold contrast on white.
        public static Color AccentColor => Pro ? Hex(0xA855F7) : Hex(0x7C3AED);
        public static Color AccentSoft => Pro ? Hex(0xC79BFB) : Hex(0x9061F9);
        public static Color SuccessColor => Pro ? Hex(0x34D399) : Hex(0x059669);
        public static Color WarningColor => Pro ? Hex(0xFBBF24) : Hex(0xB45309);
        public static Color ErrorColor => Pro ? Hex(0xF87171) : Hex(0xDC2626);

        public static Color TextColor => Pro ? Hex(0xEDE9F5) : Hex(0x1E1A26);
        public static Color SubTextColor => Pro ? Hex(0x9C93AE) : Hex(0x6B6478);
        public static Color MutedTextColor => Pro ? Hex(0x6F6880) : Hex(0x938C9F);

        /// <summary>Readable foreground on top of the accent colour.</summary>
        public static Color OnAccent => Color.white;

        public static Color Lighten(Color c, float amount) => Color.Lerp(c, Color.white, Mathf.Clamp01(amount));
        public static Color Darken(Color c, float amount) => Color.Lerp(c, Color.black, Mathf.Clamp01(amount));
        public static Color Fade(Color c, float alpha) => new Color(c.r, c.g, c.b, alpha);

        // ─────────────────────────────────────────────────────────────────
        //  STYLE CACHE
        // ─────────────────────────────────────────────────────────────────
        private static bool _stylesBuilt;
        private static bool _builtForPro;

        private static GUIStyle _section, _title, _subTitle, _button, _linkButton;
        private static GUIStyle _label, _infoLabel, _box, _h1, _h2, _h3, _body, _mono, _pill;

        private static bool NeedsRebuild =>
            !_stylesBuilt || _builtForPro != Pro || _section == null || _section.normal.background == null;

        public static GUIStyle SectionStyle { get { Initialize(); return _section; } }
        public static GUIStyle TitleStyle { get { Initialize(); return _title; } }
        public static GUIStyle SubTitleStyle { get { Initialize(); return _subTitle; } }
        public static GUIStyle ButtonStyle { get { Initialize(); return _button; } }
        public static GUIStyle LinkButtonStyle { get { Initialize(); return _linkButton; } }
        public static GUIStyle LabelStyle { get { Initialize(); return _label; } }
        public static GUIStyle InfoLabelStyle { get { Initialize(); return _infoLabel; } }
        public static GUIStyle BoxStyle { get { Initialize(); return _box; } }

        public static GUIStyle H1 { get { Initialize(); return _h1; } }
        public static GUIStyle H2 { get { Initialize(); return _h2; } }
        public static GUIStyle H3 { get { Initialize(); return _h3; } }
        public static GUIStyle Body { get { Initialize(); return _body; } }
        public static GUIStyle Mono { get { Initialize(); return _mono; } }
        public static GUIStyle PillStyle { get { Initialize(); return _pill; } }

        /// <summary>
        /// Builds the style cache. Safe to call every frame; it rebuilds only on
        /// first use, after a skin change, or when Unity has destroyed the
        /// generated textures behind our back.
        /// </summary>
        public static void Initialize()
        {
            if (!NeedsRebuild) return;

            // GUI.skin is only valid inside a GUI context. Bail quietly and retry
            // on the next OnGUI pass rather than throwing.
            if (Event.current == null || GUI.skin == null) return;

            // Skin flipped: every cached texture is now the wrong palette.
            if (_builtForPro != Pro)
            {
                TextureCache.Clear();
                TintedButtons.Clear();
            }

            _section = new GUIStyle
            {
                padding = new RectOffset((int)Space4, (int)Space4, (int)Space4, (int)Space4),
                margin = new RectOffset(0, 0, (int)Space2, (int)Space2),
                border = new RectOffset(RadiusLg, RadiusLg, RadiusLg, RadiusLg)
            };
            _section.normal.background = GetRoundedTexture(BoxBackground, BorderColor, RadiusLg, 1);

            _box = new GUIStyle
            {
                padding = new RectOffset((int)Space3, (int)Space3, (int)Space3, (int)Space3),
                margin = new RectOffset(0, 0, (int)Space1, (int)Space1),
                border = new RectOffset(RadiusMd, RadiusMd, RadiusMd, RadiusMd)
            };
            _box.normal.background = GetRoundedTexture(FieldBackground, BorderColor, RadiusMd, 1);

            _h1 = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 19,
                normal = { textColor = TextColor },
                margin = new RectOffset(0, 0, (int)Space1, (int)Space2)
            };

            _h2 = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                normal = { textColor = TextColor },
                margin = new RectOffset(0, 0, (int)Space1, (int)Space1)
            };

            _h3 = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 10,
                normal = { textColor = SubTextColor },
                margin = new RectOffset(0, 0, (int)Space1, (int)Space1)
            };

            _body = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                wordWrap = true,
                richText = true,
                normal = { textColor = TextColor }
            };

            _mono = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 10,
                wordWrap = false,
                normal = { textColor = SubTextColor }
            };

            _title = _h1;

            _subTitle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                normal = { textColor = SubTextColor },
                wordWrap = true
            };

            _label = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = AccentColor },
                fontStyle = FontStyle.Bold
            };

            _infoLabel = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = SubTextColor },
                fontSize = 11,
                wordWrap = true
            };

            _button = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                fixedHeight = ButtonHeight,
                padding = new RectOffset((int)Space4, (int)Space4, 0, 0),
                margin = new RectOffset(0, 0, (int)Space1, (int)Space1),
                border = new RectOffset(RadiusMd, RadiusMd, RadiusMd, RadiusMd),
                alignment = TextAnchor.MiddleCenter
            };
            _button.normal.background = GetRoundedTexture(AccentColor, Color.clear, RadiusMd, 0);
            _button.normal.textColor = OnAccent;
            _button.hover.background = GetRoundedTexture(Lighten(AccentColor, 0.10f), Color.clear, RadiusMd, 0);
            _button.hover.textColor = OnAccent;
            _button.active.background = GetRoundedTexture(Darken(AccentColor, 0.12f), Color.clear, RadiusMd, 0);
            _button.active.textColor = OnAccent;
            _button.focused.background = _button.normal.background;
            _button.focused.textColor = OnAccent;

            _linkButton = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = AccentColor },
                hover = { textColor = AccentSoft },
                fontSize = 11,
                fontStyle = FontStyle.Bold
            };

            _pill = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 9,
                padding = new RectOffset((int)Space2, (int)Space2, 1, 1)
            };

            _builtForPro = Pro;
            _stylesBuilt = true;
        }

        // ─────────────────────────────────────────────────────────────────
        //  WINDOW CHROME
        // ─────────────────────────────────────────────────────────────────

        /// <summary>Paints the window background gradient. Repaint-only.</summary>
        public static void DrawWindowBackground(Rect position)
        {
            if (Event.current == null || Event.current.type != EventType.Repaint) return;

            Rect full = new Rect(0, 0, position.width, position.height);

            // One cached 1x64 gradient stretched over the window, replacing the 28
            // EditorGUI.DrawRect calls this issued on every single repaint.
            GUI.DrawTexture(full, GetVerticalGradient(BaseBackgroundTop, BaseBackgroundBottom), ScaleMode.StretchToFill);

            // Faint accent hairline along the top edge for depth.
            EditorGUI.DrawRect(new Rect(full.x, full.y, full.width, 1f), Fade(AccentColor, 0.35f));
        }

        /// <summary>
        /// Product banner: logo, title, subtitle and a version pill.
        /// bannerBg is optional; without it a generated accent gradient is used,
        /// which is what ships now (the 13 MB banner PNG is no longer bundled).
        /// </summary>
        public static void DrawBanner(string title, string subtitle, string version, Texture2D logo = null, Texture2D bannerBg = null)
        {
            Initialize();
            GUILayout.Space(Space2);

            Rect banner = GUILayoutUtility.GetRect(0, BannerHeight, GUILayout.ExpandWidth(true));
            banner.x += Space2;
            banner.width -= Space2 * 2f;

            if (Event.current != null && Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(banner, GetRoundedTexture(BoxBackground, BorderColor, RadiusLg, 1), ScaleMode.StretchToFill, true);

                if (bannerBg != null)
                {
                    GUI.DrawTexture(banner, bannerBg, ScaleMode.ScaleAndCrop);
                    EditorGUI.DrawRect(banner, Fade(Pro ? Color.black : Color.white, 0.55f));
                }
                else
                {
                    Rect inner = new Rect(banner.x + 1f, banner.y + 1f, banner.width - 2f, banner.height - 2f);
                    GUI.DrawTexture(inner,
                        GetHorizontalGradient(Fade(AccentColor, Pro ? 0.30f : 0.16f), Fade(AccentColor, 0f)),
                        ScaleMode.StretchToFill, true);
                }

                // Accent rail down the leading edge.
                EditorGUI.DrawRect(new Rect(banner.x, banner.y + RadiusLg, 3f, banner.height - RadiusLg * 2f), AccentColor);
            }

            float textX = banner.x + Space5;

            if (logo != null)
            {
                Rect logoRect = new Rect(banner.x + Space4, banner.y + (BannerHeight - 56f) * 0.5f, 56f, 56f);
                GUI.DrawTexture(logoRect, logo, ScaleMode.ScaleToFit);
                textX = logoRect.xMax + Space4;
            }

            float textW = Mathf.Max(40f, banner.xMax - textX - Space4);

            GUI.Label(new Rect(textX, banner.y + 24f, textW, 26f), title ?? string.Empty, H1);
            if (!string.IsNullOrEmpty(subtitle))
                GUI.Label(new Rect(textX, banner.y + 50f, textW, 20f), subtitle, SubTitleStyle);

            if (!string.IsNullOrEmpty(version))
            {
                var content = new GUIContent("v" + version);
                Vector2 size = PillStyle.CalcSize(content);
                Rect pill = new Rect(banner.xMax - size.x - Space4, banner.y + Space3, size.x + Space2, 16f);
                DrawPill(pill, content.text, AccentColor);
            }

            GUILayout.Space(Space3);
        }

        /// <summary>
        /// A titled container. This is the workhorse layout primitive; the tools
        /// call it 21 times, so the signature is preserved exactly.
        /// </summary>
        public static void DrawSection(string title, Action content, Texture2D icon = null)
        {
            Initialize();
            EditorGUILayout.BeginVertical(SectionStyle);

            if (!string.IsNullOrEmpty(title))
            {
                EditorGUILayout.BeginHorizontal();

                Rect bar = GUILayoutUtility.GetRect(3f, 16f, GUILayout.Width(3f));
                if (Event.current != null && Event.current.type == EventType.Repaint)
                    EditorGUI.DrawRect(new Rect(bar.x, bar.y + 2f, 3f, 12f), AccentColor);

                GUILayout.Space(Space2);

                if (icon != null)
                {
                    Rect ic = GUILayoutUtility.GetRect(14f, 14f, GUILayout.Width(14f));
                    GUI.DrawTexture(new Rect(ic.x, ic.y + 1f, 14f, 14f), icon, ScaleMode.ScaleToFit);
                    GUILayout.Space(Space1);
                }

                GUILayout.Label(title, H2);
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();

                GUILayout.Space(Space3);
            }

            content?.Invoke();

            EditorGUILayout.EndVertical();
            GUILayout.Space(Space1);
        }

        /// <summary>Labelled checkbox; the label is clickable too.</summary>
        public static bool DrawToggle(string label, bool value)
        {
            Initialize();
            EditorGUILayout.BeginHorizontal();

            bool newValue = EditorGUILayout.Toggle(value, GUILayout.Width(16f));
            GUILayout.Space(Space1);

            var style = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = value ? TextColor : SubTextColor },
                fontSize = 11
            };

            Rect labelRect = GUILayoutUtility.GetRect(new GUIContent(label), style);
            GUI.Label(labelRect, label, style);
            EditorGUIUtility.AddCursorRect(labelRect, MouseCursor.Link);
            if (Event.current != null && Event.current.type == EventType.MouseDown &&
                labelRect.Contains(Event.current.mousePosition))
            {
                newValue = !value;
                Event.current.Use();
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            return newValue;
        }

        public static void DrawFooter()
        {
            Initialize();
            GUILayout.FlexibleSpace();
            GUILayout.Space(Space4);

            Separator();

            GUILayout.Space(Space2);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            DrawSocialLink("Discord", "https://discord.gg/xAeJrSAgqG");
            GUILayout.Label("·", new GUIStyle(EditorStyles.label) { normal = { textColor = MutedTextColor } });
            DrawSocialLink("GitHub", "https://github.com/kawaiistudio/KSUnityTools");

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(Space1);
            GUILayout.Label("Powered by Kawaii Studio",
                new GUIStyle(EditorStyles.centeredGreyMiniLabel) { normal = { textColor = MutedTextColor } });
            GUILayout.Space(Space2);
        }

        private static void DrawSocialLink(string name, string url)
        {
            if (GUILayout.Button(name, LinkButtonStyle, GUILayout.ExpandWidth(false)))
                Application.OpenURL(url);
            EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);
        }

        // ─────────────────────────────────────────────────────────────────
        //  CONTROLS
        // ─────────────────────────────────────────────────────────────────

        /// <summary>Filled accent button. One main action per view.</summary>
        public static bool PrimaryButton(string label, params GUILayoutOption[] options)
        {
            Initialize();
            return GUILayout.Button(label, ButtonStyle, options);
        }

        /// <summary>Outlined button for secondary actions.</summary>
        public static bool SecondaryButton(string label, params GUILayoutOption[] options)
        {
            Initialize();
            return GUILayout.Button(label, TintedButton(FieldBackground, TextColor, BorderStrong), options);
        }

        /// <summary>Destructive action; reserve for things the user cannot undo.</summary>
        public static bool DangerButton(string label, params GUILayoutOption[] options)
        {
            Initialize();
            return GUILayout.Button(label, TintedButton(ErrorColor, Color.white, Color.clear), options);
        }

        private static readonly Dictionary<string, GUIStyle> TintedButtons = new Dictionary<string, GUIStyle>();

        private static GUIStyle TintedButton(Color fill, Color text, Color border)
        {
            string key = $"{Key(fill)}|{Key(text)}|{Key(border)}|{(Pro ? 1 : 0)}";
            if (TintedButtons.TryGetValue(key, out GUIStyle cached) &&
                cached != null && cached.normal.background != null)
                return cached;

            int bt = border.a > 0f ? 1 : 0;
            var style = new GUIStyle(ButtonStyle);
            style.normal.background = GetRoundedTexture(fill, border, RadiusMd, bt);
            style.normal.textColor = text;
            style.hover.background = GetRoundedTexture(Lighten(fill, 0.08f), border, RadiusMd, bt);
            style.hover.textColor = text;
            style.active.background = GetRoundedTexture(Darken(fill, 0.10f), border, RadiusMd, bt);
            style.active.textColor = text;
            style.focused.background = style.normal.background;
            style.focused.textColor = text;

            TintedButtons[key] = style;
            return style;
        }

        public static void Separator()
        {
            Rect r = GUILayoutUtility.GetRect(0, 1f, GUILayout.ExpandWidth(true));
            if (Event.current != null && Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(r, BorderColor);
        }

        /// <summary>Small rounded label drawn into an explicit rect.</summary>
        public static void DrawPill(Rect rect, string text, Color color)
        {
            Initialize();
            if (Event.current != null && Event.current.type == EventType.Repaint)
                GUI.DrawTexture(rect, GetRoundedTexture(Fade(color, 0.20f), Fade(color, 0.55f), 6, 1), ScaleMode.StretchToFill, true);

            GUI.Label(rect, text, new GUIStyle(PillStyle) { normal = { textColor = color } });
        }

        /// <summary>Inline pill inside a layout flow.</summary>
        public static void Badge(string text, Color color)
        {
            Initialize();
            Vector2 size = PillStyle.CalcSize(new GUIContent(text));
            Rect r = GUILayoutUtility.GetRect(size.x + Space2, 16f, GUILayout.Width(size.x + Space2));
            DrawPill(r, text, color);
        }

        public enum MessageKind { Info, Success, Warning, Error }

        public static Color ColorFor(MessageKind kind)
        {
            switch (kind)
            {
                case MessageKind.Success: return SuccessColor;
                case MessageKind.Warning: return WarningColor;
                case MessageKind.Error: return ErrorColor;
                default: return AccentColor;
            }
        }

        private static string GlyphFor(MessageKind kind)
        {
            switch (kind)
            {
                case MessageKind.Success: return "✓";
                case MessageKind.Warning: return "!";
                case MessageKind.Error: return "✕";
                default: return "i";
            }
        }

        /// <summary>
        /// Inline status banner. Prefer this over EditorUtility.DisplayDialog for
        /// validation: it does not steal focus and stays visible while the user
        /// fixes the problem.
        /// </summary>
        public static void Banner(string message, MessageKind kind = MessageKind.Info)
        {
            Initialize();
            Color c = ColorFor(kind);

            Rect r = EditorGUILayout.BeginVertical();
            if (Event.current != null && Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(r, GetRoundedTexture(Fade(c, Pro ? 0.12f : 0.10f), Fade(c, 0.40f), RadiusMd, 1), ScaleMode.StretchToFill, true);
                EditorGUI.DrawRect(new Rect(r.x + 1f, r.y + RadiusSm, 3f, Mathf.Max(0f, r.height - RadiusSm * 2f)), c);
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(Space4);
            GUILayout.Label(GlyphFor(kind), new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = c },
                fontStyle = FontStyle.Bold,
                fixedWidth = 14f
            });
            GUILayout.Label(message, new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = TextColor },
                fontSize = 11,
                wordWrap = true
            });
            GUILayout.FlexibleSpace();
            GUILayout.Space(Space2);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
            GUILayout.Space(Space2);
        }

        /// <summary>Big number over a caption, for scan results and totals.</summary>
        public static void StatTile(string value, string caption, Color accent, float width = 0f)
        {
            Initialize();
            GUILayoutOption[] opts = width > 0f
                ? new[] { GUILayout.Width(width) }
                : new[] { GUILayout.ExpandWidth(true) };

            Rect r = EditorGUILayout.BeginVertical(opts);
            if (Event.current != null && Event.current.type == EventType.Repaint)
                GUI.DrawTexture(r, GetRoundedTexture(FieldBackground, BorderColor, RadiusMd, 1), ScaleMode.StretchToFill, true);

            GUILayout.Space(Space2);
            GUILayout.Label(value, new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = accent }
            });
            GUILayout.Label(caption, new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = SubTextColor }
            });
            GUILayout.Space(Space2);

            EditorGUILayout.EndVertical();
        }

        /// <summary>Slim progress track; t is 0..1.</summary>
        public static void ProgressBar(float t, string label = null, Color? color = null)
        {
            Initialize();
            Color c = color ?? AccentColor;
            t = Mathf.Clamp01(t);

            Rect r = GUILayoutUtility.GetRect(0, 6f, GUILayout.ExpandWidth(true));
            if (Event.current != null && Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(r, GetRoundedTexture(FieldBackground, BorderColor, 3, 1), ScaleMode.StretchToFill, true);
                if (t > 0f)
                {
                    Rect fill = new Rect(r.x, r.y, Mathf.Max(6f, r.width * t), r.height);
                    GUI.DrawTexture(fill, GetRoundedTexture(c, Color.clear, 3, 0), ScaleMode.StretchToFill, true);
                }
            }

            if (!string.IsNullOrEmpty(label))
            {
                GUILayout.Space(Space1);
                GUILayout.Label(label, new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = SubTextColor } });
            }
        }

        /// <summary>Centred placeholder for "nothing here yet" states.</summary>
        public static void EmptyState(string title, string hint, string actionLabel = null, Action action = null)
        {
            Initialize();
            GUILayout.Space(Space5);

            GUILayout.Label(title, new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = SubTextColor }
            });

            if (!string.IsNullOrEmpty(hint))
            {
                GUILayout.Label(hint, new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = true,
                    normal = { textColor = MutedTextColor }
                });
            }

            if (!string.IsNullOrEmpty(actionLabel) && action != null)
            {
                GUILayout.Space(Space3);
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (SecondaryButton(actionLabel, GUILayout.Width(200f))) action.Invoke();
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }

            GUILayout.Space(Space5);
        }

        /// <summary>Label left, value right, on one baseline.</summary>
        public static void KeyValueRow(string key, string value, Color? valueColor = null)
        {
            Initialize();
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(key, new GUIStyle(EditorStyles.label) { normal = { textColor = SubTextColor }, fontSize = 11 });
            GUILayout.FlexibleSpace();
            GUILayout.Label(value, new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = valueColor ?? TextColor },
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight
            });
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>Segmented control; returns the selected index.</summary>
        public static int Tabs(int selected, string[] labels)
        {
            Initialize();
            if (labels == null || labels.Length == 0) return selected;

            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < labels.Length; i++)
            {
                bool active = i == selected;
                var style = new GUIStyle(EditorStyles.miniButtonMid)
                {
                    fontSize = 11,
                    fontStyle = active ? FontStyle.Bold : FontStyle.Normal,
                    fixedHeight = 22f,
                    border = new RectOffset(RadiusSm, RadiusSm, RadiusSm, RadiusSm),
                    normal = { textColor = active ? OnAccent : SubTextColor }
                };
                style.normal.background = GetRoundedTexture(
                    active ? AccentColor : FieldBackground,
                    active ? Color.clear : BorderColor,
                    RadiusSm, active ? 0 : 1);

                if (GUILayout.Button(labels[i], style)) selected = i;
            }
            EditorGUILayout.EndHorizontal();
            return selected;
        }

        /// <summary>Recessed well, e.g. behind a log console.</summary>
        public static void BeginWell()
        {
            Initialize();
            EditorGUILayout.BeginVertical(BoxStyle);
        }

        public static void EndWell() => EditorGUILayout.EndVertical();

        // ─────────────────────────────────────────────────────────────────
        //  PROCEDURAL TEXTURES
        //  Generated, HideAndDontSave, cached: the theme costs nothing on disk
        //  and survives assembly reloads without leaking.
        // ─────────────────────────────────────────────────────────────────
        private static readonly Dictionary<string, Texture2D> TextureCache = new Dictionary<string, Texture2D>();

        private static Texture2D Cached(string key, Func<Texture2D> factory)
        {
            if (TextureCache.TryGetValue(key, out Texture2D tex) && tex != null) return tex;
            tex = factory();
            tex.hideFlags = HideFlags.HideAndDontSave;
            TextureCache[key] = tex;
            return tex;
        }

        private static string Key(Color c) => $"{c.r:F3},{c.g:F3},{c.b:F3},{c.a:F3}";

        public static Texture2D GetVerticalGradient(Color top, Color bottom)
        {
            return Cached($"vgrad|{Key(top)}|{Key(bottom)}", () =>
            {
                const int h = 64;
                var t = new Texture2D(1, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < h; y++)
                    t.SetPixel(0, y, Color.Lerp(bottom, top, y / (float)(h - 1)));
                t.Apply(false, false);
                return t;
            });
        }

        public static Texture2D GetHorizontalGradient(Color left, Color right)
        {
            return Cached($"hgrad|{Key(left)}|{Key(right)}", () =>
            {
                const int w = 64;
                var t = new Texture2D(w, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int x = 0; x < w; x++)
                    t.SetPixel(x, 0, Color.Lerp(left, right, x / (float)(w - 1)));
                t.Apply(false, false);
                return t;
            });
        }

        /// <summary>
        /// Antialiased rounded rectangle, 9-slice friendly: set style.border to the radius.
        /// </summary>
        public static Texture2D GetRoundedTexture(Color color, Color borderColor, int radius, int borderThickness)
        {
            string key = $"round|{Key(color)}|{Key(borderColor)}|r{radius}|b{borderThickness}";
            return Cached(key, () =>
            {
                const int size = 48;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };

                int rad = Mathf.Clamp(radius, 0, size / 2);
                int bt = Mathf.Clamp(borderThickness, 0, Mathf.Max(1, rad));

                float half = size * 0.5f;
                float innerHalf = half - rad;

                var pixels = new Color[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float px = (x + 0.5f) - half;
                        float py = (y + 0.5f) - half;

                        float dx = Mathf.Max(Mathf.Abs(px) - innerHalf, 0f);
                        float dy = Mathf.Max(Mathf.Abs(py) - innerHalf, 0f);
                        float dist = Mathf.Sqrt(dx * dx + dy * dy);

                        float alpha = Mathf.Clamp01(rad - dist + 0.5f);
                        if (alpha <= 0.0001f)
                        {
                            pixels[y * size + x] = Color.clear;
                            continue;
                        }

                        Color c = color;
                        c.a *= alpha;

                        if (bt > 0 && borderColor.a > 0f)
                        {
                            float innerAlpha = Mathf.Clamp01((rad - bt) - dist + 0.5f);
                            float borderA = Mathf.Clamp01(alpha - innerAlpha);
                            if (borderA > 0.0001f)
                            {
                                Color bc = borderColor;
                                bc.a *= borderA;
                                float outA = bc.a + c.a * (1f - bc.a);
                                if (outA > 0.0001f)
                                {
                                    c = new Color(
                                        (bc.r * bc.a + c.r * c.a * (1f - bc.a)) / outA,
                                        (bc.g * bc.a + c.g * c.a * (1f - bc.a)) / outA,
                                        (bc.b * bc.a + c.b * c.a * (1f - bc.a)) / outA,
                                        outA);
                                }
                            }
                        }

                        pixels[y * size + x] = c;
                    }
                }

                tex.SetPixels(pixels);
                tex.Apply(false, false);
                return tex;
            });
        }
    }
}
