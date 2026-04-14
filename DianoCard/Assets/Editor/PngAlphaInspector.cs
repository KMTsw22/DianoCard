#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 선택한 PNG의 코너와 가운데 지점 픽셀을 샘플링해 알파/컬러를 Console에 출력.
/// 용도: "이 이미지에 실제 투명이 있나? 아니면 체커 패턴이 픽셀로 구워진 건가?" 확인용.
///
/// Project 창에서 PNG 우클릭 → "Inspect PNG Alpha" 실행.
/// </summary>
public static class PngAlphaInspector
{
    private const string MenuPath = "Assets/Inspect PNG Alpha";

    [MenuItem(MenuPath, true)]
    private static bool Validate()
    {
        foreach (var obj in Selection.objects)
        {
            if (obj == null) continue;
            string path = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(path) &&
                path.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    [MenuItem(MenuPath, false, 22)]
    private static void Run()
    {
        foreach (var obj in Selection.objects)
        {
            if (obj == null) continue;
            string path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path)) continue;
            if (!path.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase)) continue;

            Inspect(path);
        }
    }

    private static void Inspect(string path)
    {
        byte[] bytes;
        try { bytes = File.ReadAllBytes(path); }
        catch { Debug.LogError($"[PngAlphaInspector] Read failed: {path}"); return; }

        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!ImageConversion.LoadImage(tex, bytes))
            {
                Debug.LogError($"[PngAlphaInspector] Decode failed: {path}");
                return;
            }

            int w = tex.width;
            int h = tex.height;
            var px = tex.GetPixels32();

            // 몇 개 위치 샘플
            var samples = new (string name, int x, int y)[]
            {
                ("top-left      ", 0, 0),
                ("top-right     ", w - 1, 0),
                ("bottom-left   ", 0, h - 1),
                ("bottom-right  ", w - 1, h - 1),
                ("center        ", w / 2, h / 2),
                ("center-left   ", w / 4, h / 2),
                ("center-right  ", 3 * w / 4, h / 2),
                ("above-center  ", w / 2, h / 4),
                ("below-center  ", w / 2, 3 * h / 4),
            };

            // 전체 알파 분포 요약
            int fullyOpaque = 0, fullyTransparent = 0, partial = 0;
            for (int i = 0; i < px.Length; i++)
            {
                byte a = px[i].a;
                if (a == 0) fullyTransparent++;
                else if (a == 255) fullyOpaque++;
                else partial++;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"=== {Path.GetFileName(path)} ({w}x{h}) ===");
            sb.AppendLine($"Alpha distribution: opaque={fullyOpaque} ({100f * fullyOpaque / px.Length:F1}%), " +
                          $"transparent={fullyTransparent} ({100f * fullyTransparent / px.Length:F1}%), " +
                          $"partial={partial} ({100f * partial / px.Length:F1}%)");
            sb.AppendLine();
            sb.AppendLine("Samples:");
            foreach (var (name, x, y) in samples)
            {
                var c = px[y * w + x];
                sb.AppendLine($"  {name} ({x,4},{y,4}): RGBA=({c.r,3},{c.g,3},{c.b,3},{c.a,3})  {AlphaTag(c.a)}");
            }

            Debug.Log(sb.ToString());
        }
        finally
        {
            Object.DestroyImmediate(tex);
        }
    }

    private static string AlphaTag(byte a)
    {
        if (a == 0) return "[TRANSPARENT]";
        if (a == 255) return "[OPAQUE]";
        return "[PARTIAL]";
    }
}
#endif
