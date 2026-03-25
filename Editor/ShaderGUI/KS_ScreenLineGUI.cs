using UnityEngine;
using UnityEditor;

public class KS_ScreenLineGUI : ShaderGUI
{
    private const string DISCORD_URL = "https://discord.gg/xAeJrSAgqG";
    private const string TELEGRAM_URL = "https://t.me/kawaiistudio";
    private const string VRCHAT_URL = "https://vrchat.com/home/group/grp_7bf987ee-2f4a-4eae-b9b5-c060b97250ab";
    private const string LOGO_PATH = "Assets/Kawaii Studio/References/logo.png";

    private static Texture2D logoTexture;
    private static bool isDownloading = false;

    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
    {
        base.OnGUI(materialEditor, properties);

        EditorGUILayout.Space(15);
        DrawFooter();
    }

    private void DrawFooter()
    {
        if (logoTexture == null && !isDownloading)
            DownloadLogo();

        GUILayout.BeginVertical(EditorStyles.helpBox);
        GUILayout.Space(5);

        if (logoTexture != null)
        {
            float aspectRatio = (float)logoTexture.height / logoTexture.width;
            float width = Mathf.Min(EditorGUIUtility.currentViewWidth - 40, 200);
            float height = width * aspectRatio;
            
            Rect rect = GUILayoutUtility.GetRect(width, height);
            rect.width = width;
            rect.height = height;
            rect.x = (EditorGUIUtility.currentViewWidth - width) / 2;
            
            GUI.DrawTexture(rect, logoTexture, ScaleMode.ScaleToFit);
        }
        else
        {
            GUILayout.Label("KAWAII STUDIO", new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter });
        }

        GUILayout.Space(5);
        GUILayout.Label("Join the Community", EditorStyles.centeredGreyMiniLabel);
        GUILayout.Space(5);

        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        
        if (GUILayout.Button("Discord", GUILayout.Width(80))) Application.OpenURL(DISCORD_URL);
        GUILayout.Space(5);
        if (GUILayout.Button("Telegram", GUILayout.Width(80))) Application.OpenURL(TELEGRAM_URL);
        
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.Space(2);

        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("VRChat Group", GUILayout.Width(120))) Application.OpenURL(VRCHAT_URL);
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.Space(5);
        GUILayout.Label("This shader is made for AKCESSIVE <3", EditorStyles.centeredGreyMiniLabel);
        GUILayout.EndVertical();
    }

    private void DownloadLogo()
    {
        // Branding is loaded locally from Assets/Kawaii Studio/References (no network).
        isDownloading = true;
        logoTexture = KawaiiStudio.KawaiiStudioBranding.Logo ?? AssetDatabase.LoadAssetAtPath<Texture2D>(LOGO_PATH);
        isDownloading = false;
    }
}

