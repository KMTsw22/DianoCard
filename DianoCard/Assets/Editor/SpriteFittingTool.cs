#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Project 창에서 PNG 우클릭 → "SpriteFitting" 메뉴 2종.
///
/// 두 모드:
/// 1) "SpriteFitting"            — 배경 제거 + 콘텐츠 영역으로 크롭 (크기 작아짐)
/// 2) "SpriteFitting (Keep Size)" — 배경만 투명화, 크롭 안 함 (원본 크기 유지)
///
/// 배경 제거 전략 (두 모드 공통):
/// - 이미지에 투명 픽셀이 있으면 → 알파 기준 트림 (알파 낮은 영역 제거)
/// - 이미지가 완전 불투명(Gemini가 흰 배경으로 만든 PNG 등)이면
///   → 좌상단 코너 픽셀을 배경색으로 샘플링
///   → **4 코너에서 flood fill**로 배경색과 연결된 픽셀만 투명화
///   → 내부의 흰 공간(예: 빈 카드 프레임 안쪽)은 보존됨
///
/// Flood fill을 쓰는 이유: 단순히 "배경색과 매칭되는 픽셀 모두" 투명화하면
/// 카드 프레임 내부의 흰색도 같이 사라져서 프레임만 고리 모양으로 남음.
/// 가장자리에서 연결된 픽셀만 제거해야 프레임 구조가 유지됨.
/// </summary>
public static class SpriteFittingTool
{
    private const string MenuPath         = "Assets/SpriteFitting";
    private const string MenuPathKeepSize = "Assets/SpriteFitting (Keep Size)";

    // 콘텐츠로 간주할 최소 알파.
    // remove.bg 같은 배경 제거 도구가 가장자리에 알파 1~10 정도의 희미한 잔여물을
    // 남기는 경우가 있어서, 20 정도로 올려야 실제 콘텐츠 바운딩 박스를 찾을 수 있음.
    // 이 값보다 낮은 알파는 "사실상 투명"으로 처리되어 트림 대상이 됨.
    // 안티에일리어싱된 가장자리는 보통 알파 100+ 이므로 안전하게 유지됨.
    private const byte AlphaThreshold = 20;

    private const int  BackgroundColorTolerance = 20;
    private const byte HasTransparencyAlphaCutoff = 250;

    // ---------------------------------------------------------
    // Menu entries
    // ---------------------------------------------------------

    [MenuItem(MenuPath, true)]
    private static bool ValidateSelection() => CollectPngPaths().Count > 0;

    [MenuItem(MenuPath, false, 20)]
    private static void Run() => RunInternal(crop: true);

    [MenuItem(MenuPathKeepSize, true)]
    private static bool ValidateSelectionKeepSize() => CollectPngPaths().Count > 0;

    [MenuItem(MenuPathKeepSize, false, 21)]
    private static void RunKeepSize() => RunInternal(crop: false);

    private static void RunInternal(bool crop)
    {
        var paths = CollectPngPaths();
        if (paths.Count == 0)
        {
            EditorUtility.DisplayDialog("SpriteFitting",
                "선택된 PNG가 없습니다.\nProject 창에서 PNG를 선택한 뒤 다시 시도하세요.", "OK");
            return;
        }

        string title = crop ? "SpriteFitting" : "SpriteFitting (Keep Size)";
        string body = crop
            ? $"{paths.Count}개 PNG에서 배경을 제거하고 콘텐츠 영역으로 잘라냅니다.\n원본 파일이 덮어써집니다. 계속할까요?"
            : $"{paths.Count}개 PNG에서 배경만 투명화합니다 (크기 유지).\n원본 파일이 덮어써집니다. 계속할까요?";

        if (!EditorUtility.DisplayDialog(title, body, "Process", "Cancel")) return;

        int processed = 0, unchanged = 0, failed = 0;

        try
        {
            AssetDatabase.StartAssetEditing();
            for (int i = 0; i < paths.Count; i++)
            {
                EditorUtility.DisplayProgressBar(title, Path.GetFileName(paths[i]), (float)i / paths.Count);
                var result = ProcessPng(paths[i], crop);
                switch (result)
                {
                    case ProcessResult.Done: processed++; break;
                    case ProcessResult.NoChange: unchanged++; break;
                    default: failed++; break;
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            AssetDatabase.StopAssetEditing();
            AssetDatabase.Refresh();
        }

        Debug.Log($"[SpriteFitting] {(crop ? "Crop" : "KeepSize")} — done:{processed} unchanged:{unchanged} failed:{failed}");
        EditorUtility.DisplayDialog(
            $"{title} 완료",
            $"처리됨: {processed}\n변경 없음: {unchanged}\n실패: {failed}",
            "OK");
    }

    // ---------------------------------------------------------
    // Selection collection
    // ---------------------------------------------------------

    private static List<string> CollectPngPaths()
    {
        var set = new HashSet<string>();
        var selection = Selection.GetFiltered<Object>(SelectionMode.Assets | SelectionMode.DeepAssets);

        foreach (var obj in selection)
        {
            if (obj == null) continue;
            string path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path)) continue;
            if (!path.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase)) continue;
            set.Add(path);
        }

        var list = new List<string>(set);
        list.Sort();
        return list;
    }

    // ---------------------------------------------------------
    // Core processing
    // ---------------------------------------------------------

    private enum ProcessResult { Done, NoChange, Failed }

    private static ProcessResult ProcessPng(string assetPath, bool crop)
    {
        if (!File.Exists(assetPath))
        {
            Debug.LogWarning($"[SpriteFitting] File not found: {assetPath}");
            return ProcessResult.Failed;
        }

        byte[] originalBytes;
        try { originalBytes = File.ReadAllBytes(assetPath); }
        catch (System.Exception e)
        {
            Debug.LogError($"[SpriteFitting] Read failed: {assetPath}\n{e.Message}");
            return ProcessResult.Failed;
        }

        var srcTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!ImageConversion.LoadImage(srcTex, originalBytes))
            {
                Debug.LogWarning($"[SpriteFitting] PNG decode failed: {assetPath}");
                return ProcessResult.Failed;
            }

            int w = srcTex.width;
            int h = srcTex.height;
            var pixels = srcTex.GetPixels32();

            // 1) 배경 처리 모드 자동 선택:
            //    체커 감지를 가장 먼저 시도 (이미 일부 투명 영역이 있어도 체커가 베이크된 경우 처리해야 함).
            //    체커 발견 시 해당 회색 색상들을 전역 제거.
            //    체커 없으면, 투명도 있으면 알파 모드, 없으면 flood fill.
            Color32[] processed;
            string mode;
            bool hasTransparency = HasAnyTransparentPixel(pixels);

            if (TryDetectCheckerBackground(pixels, w, h, out var checkerColors))
            {
                processed = RemoveColorsGlobal(pixels, checkerColors, BackgroundColorTolerance);
                mode = $"checker ({checkerColors.Count} colors: " +
                       string.Join(",", checkerColors.ConvertAll(c => $"#{c.r:X2}{c.g:X2}{c.b:X2}")) + ")";
                // 체커 제거 후 투명 영역이 더 늘어났으므로 hasTransparency 갱신
                hasTransparency = true;
            }
            else if (hasTransparency)
            {
                processed = pixels;
                mode = "alpha";
            }
            else
            {
                var bgColor = SampleBackgroundColor(pixels, w, h);
                processed = FloodFillKnockout(pixels, w, h, bgColor, BackgroundColorTolerance);
                mode = $"flood (bg=#{bgColor.r:X2}{bgColor.g:X2}{bgColor.b:X2})";
                hasTransparency = true;
            }

            // 2) Keep Size 모드: 투명화만 하고 원본 크기 그대로 저장
            if (!crop)
            {
                byte[] ksBytes = EncodeFullSize(processed, w, h);
                if (ksBytes == null) return ProcessResult.Failed;

                try { File.WriteAllBytes(assetPath, ksBytes); }
                catch (System.Exception e)
                {
                    Debug.LogError($"[SpriteFitting] Write failed: {assetPath}\n{e.Message}");
                    return ProcessResult.Failed;
                }

                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                Debug.Log($"[SpriteFitting] {assetPath}: {w}x{h} (kept size) [{mode}]");
                return ProcessResult.Done;
            }

            // 3) Crop 모드: 콘텐츠 영역으로 바운딩 박스 계산 후 잘라냄
            if (!FindOpaqueBounds(processed, w, h, out int minX, out int minY, out int maxX, out int maxY))
            {
                Debug.LogWarning($"[SpriteFitting] 잘라낼 콘텐츠가 없음 (전체가 배경/투명): {assetPath}");
                return ProcessResult.Failed;
            }

            int newW = maxX - minX + 1;
            int newH = maxY - minY + 1;

            if (hasTransparency && newW == w && newH == h)
            {
                Debug.Log($"[SpriteFitting] 이미 트리밍 상태: {assetPath} ({w}x{h})");
                return ProcessResult.NoChange;
            }

            byte[] pngBytes = EncodeCropped(processed, w, minX, minY, newW, newH);
            if (pngBytes == null) return ProcessResult.Failed;

            try { File.WriteAllBytes(assetPath, pngBytes); }
            catch (System.Exception e)
            {
                Debug.LogError($"[SpriteFitting] Write failed: {assetPath}\n{e.Message}");
                return ProcessResult.Failed;
            }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            Debug.Log($"[SpriteFitting] {assetPath}: {w}x{h} → {newW}x{newH} [{mode}]");
            return ProcessResult.Done;
        }
        finally
        {
            Object.DestroyImmediate(srcTex);
        }
    }

    // ---------------------------------------------------------
    // Background detection & flood fill knockout
    // ---------------------------------------------------------

    private static bool HasAnyTransparentPixel(Color32[] pixels)
    {
        for (int i = 0; i < pixels.Length; i++)
            if (pixels[i].a < HasTransparencyAlphaCutoff) return true;
        return false;
    }

    private static Color32 SampleBackgroundColor(Color32[] pixels, int w, int h)
    {
        // 좌상단 코너를 배경색으로 채택 (대부분의 생성 이미지에서 충분)
        return pixels[0];
    }

    /// <summary>
    /// 체커 패턴(Gemini가 "투명"을 표현한답시고 회색 격자를 픽셀로 그려 넣은 경우) 감지.
    /// 이미지 전체에 걸친 그리드의 여러 위치에서 픽셀을 샘플링해서
    /// 2~3개의 비슷한 회색 색상이 발견되고 모두 회색 계열이면 체커로 판정.
    /// 투명한 픽셀은 샘플링에서 제외 (가장자리가 이미 진짜 투명일 수 있으므로).
    /// </summary>
    private static bool TryDetectCheckerBackground(Color32[] pixels, int w, int h, out List<Color32> colors)
    {
        colors = new List<Color32>();

        // 가장자리 전체에 걸친 다양한 위치 샘플링.
        // 체커 격자 양쪽 색을 모두 잡기 위해 인접 픽셀도 포함.
        var samplePositions = new List<int>
        {
            // 좌상단 영역
            0, 1, w, w + 1, 2, w + 2, 2 * w, 2 * w + 1,
            // 우상단 영역
            w - 1, w - 2, 2 * w - 1, 2 * w - 2,
            // 좌하단 영역
            (h - 1) * w, (h - 1) * w + 1, (h - 2) * w,
            // 우하단 영역
            h * w - 1, h * w - 2, (h - 1) * w - 1,
            // 가장자리 중앙들
            w / 2, h / 2 * w, (h - 1) * w + w / 2, (h / 2 + 1) * w - 1,
        };

        int opaqueSampleCount = 0;
        foreach (var idx in samplePositions)
        {
            if (idx < 0 || idx >= pixels.Length) continue;
            var c = pixels[idx];

            // 투명 픽셀은 샘플에서 제외 (이미 진짜 투명한 영역일 수 있음)
            if (c.a < 200) continue;
            opaqueSampleCount++;

            bool found = false;
            foreach (var u in colors)
            {
                if (ColorMatches(u, c, 5))
                {
                    found = true;
                    break;
                }
            }
            if (!found) colors.Add(c);
        }

        // 체커 판정 조건:
        // - 최소 4개 이상의 불투명 샘플이 있어야 함 (가장자리 다 투명이면 체커가 아님)
        // - 2~3개의 distinct 색상
        // - 모두 회색 계열 (채도가 낮음)
        if (opaqueSampleCount < 4) return false;
        if (colors.Count < 2 || colors.Count > 3) return false;

        foreach (var c in colors)
        {
            int maxCh = System.Math.Max(c.r, System.Math.Max(c.g, c.b));
            int minCh = System.Math.Min(c.r, System.Math.Min(c.g, c.b));
            // 채도 = max - min. 25보다 크면 회색이 아닌 것으로 간주
            if (maxCh - minCh > 25) return false;
        }

        return true;
    }

    /// <summary>
    /// 주어진 색상 리스트와 매칭되는 모든 픽셀을 전역적으로 투명화.
    /// flood fill과 달리 연결성 무시. 체커 같은 분리된 패턴 제거에 사용.
    /// </summary>
    private static Color32[] RemoveColorsGlobal(Color32[] pixels, List<Color32> bgColors, int tolerance)
    {
        var result = new Color32[pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
        {
            var p = pixels[i];
            bool isBackground = false;
            foreach (var bg in bgColors)
            {
                if (ColorMatches(p, bg, tolerance))
                {
                    isBackground = true;
                    break;
                }
            }
            result[i] = isBackground ? new Color32(0, 0, 0, 0) : p;
        }
        return result;
    }

    /// <summary>
    /// 4 코너에서 시작해 flood fill로 배경색과 연결된 픽셀만 투명화.
    /// 내부의 동일색 영역(예: 프레임 안쪽 흰색)은 보존된다.
    /// </summary>
    private static Color32[] FloodFillKnockout(Color32[] pixels, int w, int h, Color32 bg, int tolerance)
    {
        int n = pixels.Length;
        var visited = new bool[n];
        var queue = new Queue<int>();

        // 4 코너 시드
        TrySeed(pixels, visited, queue, bg, tolerance, 0);
        TrySeed(pixels, visited, queue, bg, tolerance, w - 1);
        TrySeed(pixels, visited, queue, bg, tolerance, (h - 1) * w);
        TrySeed(pixels, visited, queue, bg, tolerance, h * w - 1);

        while (queue.Count > 0)
        {
            int idx = queue.Dequeue();
            int y = idx / w;
            int x = idx % w;

            if (x > 0)     TryEnqueue(pixels, visited, queue, bg, tolerance, idx - 1);
            if (x < w - 1) TryEnqueue(pixels, visited, queue, bg, tolerance, idx + 1);
            if (y > 0)     TryEnqueue(pixels, visited, queue, bg, tolerance, idx - w);
            if (y < h - 1) TryEnqueue(pixels, visited, queue, bg, tolerance, idx + w);
        }

        var result = new Color32[n];
        for (int i = 0; i < n; i++)
        {
            result[i] = visited[i] ? new Color32(0, 0, 0, 0) : pixels[i];
        }
        return result;
    }

    private static void TrySeed(Color32[] pixels, bool[] visited, Queue<int> queue,
                                 Color32 bg, int tolerance, int idx)
    {
        if (visited[idx]) return;
        if (!ColorMatches(pixels[idx], bg, tolerance)) return;
        visited[idx] = true;
        queue.Enqueue(idx);
    }

    private static void TryEnqueue(Color32[] pixels, bool[] visited, Queue<int> queue,
                                    Color32 bg, int tolerance, int idx)
    {
        if (visited[idx]) return;
        if (!ColorMatches(pixels[idx], bg, tolerance)) return;
        visited[idx] = true;
        queue.Enqueue(idx);
    }

    private static bool ColorMatches(Color32 a, Color32 b, int tolerance)
    {
        int dr = System.Math.Abs(a.r - b.r);
        int dg = System.Math.Abs(a.g - b.g);
        int db = System.Math.Abs(a.b - b.b);
        return dr <= tolerance && dg <= tolerance && db <= tolerance;
    }

    // ---------------------------------------------------------
    // Alpha bounds (crop 모드에서 사용)
    // ---------------------------------------------------------

    private static bool FindOpaqueBounds(
        Color32[] pixels, int w, int h,
        out int minX, out int minY, out int maxX, out int maxY)
    {
        minX = w; minY = h; maxX = -1; maxY = -1;

        for (int y = 0; y < h; y++)
        {
            int rowStart = y * w;
            for (int x = 0; x < w; x++)
            {
                if (pixels[rowStart + x].a >= AlphaThreshold)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }

        return maxX >= 0;
    }

    // ---------------------------------------------------------
    // Encode helpers
    // ---------------------------------------------------------

    private static byte[] EncodeFullSize(Color32[] pixels, int w, int h)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        try
        {
            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: false);
            return ImageConversion.EncodeToPNG(tex);
        }
        finally
        {
            Object.DestroyImmediate(tex);
        }
    }

    private static byte[] EncodeCropped(
        Color32[] srcPixels, int srcW,
        int minX, int minY, int newW, int newH)
    {
        var cropped = new Color32[newW * newH];
        for (int y = 0; y < newH; y++)
        {
            int srcRow = (minY + y) * srcW + minX;
            int dstRow = y * newW;
            for (int x = 0; x < newW; x++)
            {
                cropped[dstRow + x] = srcPixels[srcRow + x];
            }
        }

        var dstTex = new Texture2D(newW, newH, TextureFormat.RGBA32, false);
        try
        {
            dstTex.SetPixels32(cropped);
            dstTex.Apply(updateMipmaps: false);
            return ImageConversion.EncodeToPNG(dstTex);
        }
        finally
        {
            Object.DestroyImmediate(dstTex);
        }
    }
}
#endif
