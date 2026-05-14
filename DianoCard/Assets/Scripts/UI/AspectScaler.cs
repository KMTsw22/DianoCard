using UnityEngine;

namespace DianoCard.UI
{
    /// <summary>
    /// 반응형 IMGUI 좌표계 헬퍼.
    ///
    /// 1280×720은 "디자인 캔버스"의 권장 사이즈일 뿐, 실제 화면 비율에 따라
    /// 가상 캔버스가 자동으로 늘어남:
    ///   - 16:9 (1920×1080) → 가상 1280×720 (디자인 그대로)
    ///   - 16:10 (2880×1800) → 가상 1280×800 (세로 더 김)
    ///   - 21:9 (3440×1440)  → 가상 1720×720 (가로 더 김)
    ///
    /// 각 UI 파일에서 `RefW`/`RefH`를 `AspectScaler.ScreenW`/`ScreenH`로 바인딩하면
    /// `RefH - X` (화면 바닥 기준), `RefW * 0.5f` (화면 중앙) 등 좌표 패턴이
    /// 자동으로 실제 화면 edge/center로 정렬됨. letterbox 바 없음, 풀스크린.
    /// </summary>
    public static class AspectScaler
    {
        public const float DesignW = 1280f;
        public const float DesignH = 720f;

        /// <summary>디자인 캔버스를 화면에 inscribe하는 uniform 스케일.
        /// Mathf.Min으로 짧은 축에 맞춤 → 가상 캔버스는 화면보다 크거나 같아짐.</summary>
        public static float Scale =>
            Mathf.Min(Screen.width / DesignW, Screen.height / DesignH);

        /// <summary>실제 화면 width를 가상 좌표로 환산. ≥ DesignW (1280).</summary>
        public static float ScreenW => Screen.width / Scale;

        /// <summary>실제 화면 height를 가상 좌표로 환산. ≥ DesignH (720).</summary>
        public static float ScreenH => Screen.height / Scale;

        /// <summary>OnGUI 시작 시 적용할 표준 GUI.matrix — 좌상단 (0,0) anchored uniform 스케일.</summary>
        public static Matrix4x4 GuiMatrix
        {
            get
            {
                float s = Scale;
                return Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));
            }
        }
    }
}
