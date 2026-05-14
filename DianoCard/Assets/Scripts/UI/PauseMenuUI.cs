using System.Collections.Generic;
using DianoCard.Audio;
using DianoCard.Game;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// ESC 일시정지 메뉴. StS 스타일 — Resume / Settings / Abandon Run / Quit Game.
/// 어느 GameState에서든 ESC로 토글 (단, BattleUI 서브 오버레이가 열려있으면 그쪽이 우선).
/// 열려있는 동안 Time.timeScale = 0, 다른 UI들은 자체 OnGUI에서 IsOpen 가드로 렌더 차단.
/// </summary>
public class PauseMenuUI : MonoBehaviour
{
    // 반응형 (AspectScaler) — 모든 RefH/RefW 사용 좌표가 자동으로 실제 화면 edge/center로 정렬.
    private static float RefW => DianoCard.UI.AspectScaler.ScreenW;
    private static float RefH => DianoCard.UI.AspectScaler.ScreenH;

    private enum Screen_ { Main, Settings, ConfirmAbandon, ConfirmQuit }

    public static bool IsOpen { get; private set; }

    private Screen_ _screen = Screen_.Main;
    private float _savedTimeScale = 1f;

    private readonly List<System.Action> _pending = new();

    // 캐시: Settings 진입 시 최초 1회 SaveSystem/Screen에서 로드
    private float _bgmVol = 1f;
    private float _sfxVol = 1f;
    private bool _fullscreen;
    private int _resIndex;

    // 공용 해상도 프리셋 (PC 윈도우/풀스크린 양쪽에서 흔한 값)
    private static readonly Vector2Int[] Resolutions =
    {
        new(1280, 720),
        new(1366, 768),
        new(1600, 900),
        new(1920, 1080),
        new(2560, 1440),
    };

    // =========================================================
    // Inspector — 텍스처 (Inspector에서 드롭하지 않으면 Resources/PauseUI/* 폴백)
    // =========================================================
    [Header("Textures (비워두면 Resources/PauseUI/ 폴더에서 자동 로드)")]
    [SerializeField, Tooltip("패널 배경 9-slice 텍스처. 폴백: Resources/PauseUI/Panel_Pause_BG.")]
    private Texture2D _bgTexture;
    [SerializeField, Tooltip("버튼 idle 9-slice 텍스처. 폴백: Resources/PauseUI/Panel_Button_Idle.")]
    private Texture2D _btnIdleTexture;
    [SerializeField, Tooltip("버튼 hover 9-slice 텍스처 (마우스 올라가면 스왑). 폴백: Resources/PauseUI/Panel_Button_Hover.")]
    private Texture2D _btnHoverTexture;

    [Header("Button Render Mode")]
    [SerializeField, Tooltip("ON = 위 Btn Idle/Hover 텍스처 사용 (idle은 Panel_Button_Idle, hover는 Panel_Button_Hover로 자동 스왑). OFF = 코드로 깔끔한 다크+브라스 테두리 버튼 그림.")]
    private bool _useButtonTextures = true;
    [SerializeField, Tooltip("코드 드로우 버튼 채움색.")]
    private Color _cleanBtnFill = new(0.12f, 0.10f, 0.13f, 0.92f);
    [SerializeField, Tooltip("코드 드로우 버튼 idle 테두리 색.")]
    private Color _cleanBtnBorderIdle = new(0.69f, 0.55f, 0.32f, 1f);
    [SerializeField, Tooltip("코드 드로우 버튼 hover 테두리 색 (밝은 브라스).")]
    private Color _cleanBtnBorderHover = new(0.92f, 0.74f, 0.40f, 1f);
    [SerializeField, Range(1f, 4f), Tooltip("코드 드로우 버튼 idle 테두리 두께 (px).")]
    private float _cleanBtnBorderIdleW = 1.5f;
    [SerializeField, Range(1f, 6f), Tooltip("코드 드로우 버튼 hover 테두리 두께 (px).")]
    private float _cleanBtnBorderHoverW = 2.5f;

    [Header("9-slice Border (텍스처 가장자리에서 안쪽으로 보존되는 px). 0 = full stretch.")]
    [SerializeField, Range(0, 256), Tooltip("BG 패널 9-slice border. 0=full stretch (둥근 모서리도 stretch).")]
    private int _bgBorder = 0;
    [SerializeField, Range(0, 256), Tooltip("버튼 9-slice border. 0=full stretch.")]
    private int _btnBorder = 0;

    [Header("Button Text Color")]
    [SerializeField, Tooltip("idle 버튼 텍스트 색.")]
    private Color _btnTextIdle = new(0.91f, 0.83f, 0.65f, 1f);
    [SerializeField, Tooltip("hover 버튼 텍스트 색.")]
    private Color _btnTextHover = new(0.98f, 0.90f, 0.62f, 1f);

    [Header("Idle Dim Tint (idle/hover 텍스처가 같을 때 visual 차이용)")]
    [SerializeField, Range(0.3f, 1f), Tooltip("idle 상태에서 GUI.color 곱 — 1=원본, 0.7=약간 어둡게.")]
    private float _btnIdleTint = 0.78f;

    [Header("Hover Border Overlay (호버 시 idle 텍스처 위에 코드로 그리는 노란빛 테두리)")]
    [SerializeField, Tooltip("호버 시 추가로 그릴 노란빛 보더 색.")]
    private Color _hoverBorderColor = new(0.93f, 0.78f, 0.36f, 0.95f);
    [SerializeField, Range(0.5f, 6f), Tooltip("호버 보더 두께 (px).")]
    private float _hoverBorderWidth = 2f;
    [SerializeField, Range(-4f, 12f), Tooltip("버튼 rect에서 안쪽으로 inset (px). 0=정확히 가장자리, +값=안쪽으로.")]
    private float _hoverBorderInset = 1f;

    [Header("Backdrop Dim (메뉴 뒤 화면 어둡게)")]
    [SerializeField, Range(0f, 1f), Tooltip("0 = 안 어두움, 1 = 완전 검정. 0.35 기본 (StS 톤).")]
    private float _backdropAlpha = 0.35f;

    [Header("Font Sizes")]
    [SerializeField, Range(10, 60)] private int _titleFontSize = 26;
    [SerializeField, Range(10, 40)] private int _buttonFontSize = 14;

    [Header("Main Panel Layout (1280x720 가상좌표)")]
    [SerializeField, Range(200f, 800f), Tooltip("메인 패널 폭.")]
    private float _mainPanelWidth = 280f;
    [SerializeField, Range(200f, 700f), Tooltip("메인 패널 높이. BG 텍스처가 정사각형이라 폭과 비슷하게 두면 자연스러움.")]
    private float _mainPanelHeight = 340f;
    [SerializeField, Tooltip("화면 중앙 기준 패널 오프셋. (0,0)=정중앙. X+ 오른쪽, Y+ 아래.")]
    private Vector2 _mainPanelOffset = Vector2.zero;
    [SerializeField, Range(0f, 200f), Tooltip("패널 상단에서 'PAUSED' 타이틀까지 Y 패딩.")]
    private float _titleTopPadding = 28f;
    [SerializeField, Range(60f, 280f), Tooltip("패널 상단에서 첫 번째 버튼까지 Y 위치.")]
    private float _firstButtonY = 108f;
    [SerializeField, Range(120f, 500f), Tooltip("버튼 폭.")]
    private float _buttonWidth = 220f;
    [SerializeField, Range(30f, 120f), Tooltip("버튼 높이. 텍스처 비율이 1.79:1 이라 폭의 ~1/2 정도가 자연스러움.")]
    private float _buttonHeight = 40f;
    [SerializeField, Range(0f, 40f), Tooltip("버튼 사이 세로 간격.")]
    private float _buttonGap = 12f;

    [Header("Title Divider (타이틀 밑 가로선)")]
    [SerializeField, Tooltip("타이틀 밑 brass 가로선 표시.")]
    private bool _showTitleDivider = true;
    [SerializeField, Tooltip("디바이더 색.")]
    private Color _dividerColor = new(0.78f, 0.62f, 0.32f, 0.85f);
    [SerializeField, Range(0.3f, 0.95f), Tooltip("디바이더 폭 = 패널 폭 × 이 비율.")]
    private float _dividerWidthRatio = 0.85f;
    [SerializeField, Range(0.5f, 4f), Tooltip("디바이더 두께 (px).")]
    private float _dividerThickness = 1.5f;
    [SerializeField, Range(-20f, 60f), Tooltip("타이틀 baseline 기준 디바이더 Y 오프셋 (px).")]
    private float _dividerYOffset = 14f;

    [Header("Settings Panel Layout")]
    [SerializeField, Range(300f, 800f)] private float _settingsPanelWidth = 460f;
    [SerializeField, Range(300f, 700f)] private float _settingsPanelHeight = 460f;

    [Header("Confirm Dialog Layout")]
    [SerializeField, Range(300f, 700f)] private float _confirmPanelWidth = 440f;
    [SerializeField, Range(160f, 400f)] private float _confirmPanelHeight = 240f;

    // =========================================================
    // 내부 상태
    // =========================================================
    private GUIStyle _titleStyle;
    private GUIStyle _btnStyle;
    private GUIStyle _btnHoverStyleCache;
    private GUIStyle _btnTextOnlyStyle;       // 코드 드로우 모드용 — 배경 없음, 텍스트만
    private GUIStyle _btnTextOnlyHoverStyle;  // 호버 시 텍스트 색만 다름
    private GUIStyle _smallBtnStyle;
    private GUIStyle _labelStyle;
    private GUIStyle _hintStyle;
    private GUIStyle _bgPanelStyle;
    private bool _stylesReady;

    private bool _assetsReady;

    // 폰트 — 한글 메뉴는 Hahmlet 명조(아니면 시스템 Arial로 떨어져 글리프 깨짐), 영문은 Cinzel.
    private Font _displayFont;
    private Font _displayFontKR;

    void Update()
    {
        // _pending — OnGUI 중간에 상태 바꾸면 GUI 레이아웃이 깨지므로 다음 프레임에 실행
        if (_pending.Count > 0)
        {
            var snapshot = new List<System.Action>(_pending);
            _pending.Clear();
            foreach (var a in snapshot) a?.Invoke();
        }

        if (!EscapePressedThisFrame()) return;

        if (IsOpen)
        {
            // 서브 화면이면 메인으로, 메인이면 메뉴 닫기
            if (_screen != Screen_.Main) _screen = Screen_.Main;
            else Close();
            return;
        }

        var gsm = GameStateManager.Instance;
        if (gsm == null) return;

        // AnimationTest는 개발 툴이라 ESC 메뉴 비활성
        if (gsm.State == GameState.AnimationTest) return;

        // BattleUI 서브 오버레이(덱/유물/포션/증원 뷰어)가 열려있으면 ESC를 그쪽에 양보.
        // 같은 프레임 내 Update가 OnGUI보다 먼저 호출되므로, 이번 ESC는 무시하고
        // 곧이어 BattleUI.OnGUI가 자기 오버레이를 닫는다.
        var battle = GetComponent<BattleUI>();
        if (battle != null && battle.AnyOverlayOpen) return;

        Open();
    }

    /// <summary>Input System / Legacy Input 양쪽 호환 ESC 폴링.
    /// Project Settings의 Active Input Handling이 어느 쪽이든 동작.</summary>
    private static bool EscapePressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb != null) return kb.escapeKey.wasPressedThisFrame;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.Escape);
#else
        return false;
#endif
    }

    private void Open()
    {
        IsOpen = true;
        _screen = Screen_.Main;
        _savedTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
    }

    private void Close()
    {
        IsOpen = false;
        Time.timeScale = _savedTimeScale;
    }

    void OnGUI()
    {
        if (!IsOpen) return;

        EnsureStyles();

        // 가장 앞에 그림 (다른 UI 위)
        GUI.depth = -1000;

        // 풀스크린 매트릭스 — 화면 전체에 dim 깔기
        // 배경은 그대로 보이게, 살짝만 어둡게 (StS 스타일).
        GUI.matrix = Matrix4x4.identity;
        var dimPrev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, _backdropAlpha);
        GUI.DrawTexture(new Rect(0, 0, UnityEngine.Screen.width, UnityEngine.Screen.height), Texture2D.whiteTexture);
        GUI.color = dimPrev;

        // (이전 버전에 풀스크린 invisible 클릭 트랩이 있었으나, IMGUI는 같은 OnGUI 패스에서
        //  먼저 호출된 GUI.Button이 MouseDown을 Event.Use()로 소비하기 때문에
        //  트랩이 패널 자체의 버튼/슬라이더 클릭까지 흡수해버리는 버그가 있었음.
        //  다른 UI들은 모두 OnGUI 맨 위에서 `if (PauseMenuUI.IsOpen) return;` 가드로
        //  안 그리므로 트랩이 막아야 할 underlying 버튼 자체가 없어 트랩을 제거.)

        // 반응형 IMGUI — RefH/RefW가 실제 화면 크기 따라가므로 letterbox 없이 모든 비율에서 정상 정렬.
        GUI.matrix = DianoCard.UI.AspectScaler.GuiMatrix;

        switch (_screen)
        {
            case Screen_.Main:           DrawMain();          break;
            case Screen_.Settings:       DrawSettings();      break;
            case Screen_.ConfirmAbandon: DrawConfirmAbandon();break;
            case Screen_.ConfirmQuit:    DrawConfirmQuit();   break;
        }
    }

    // =========================================================
    // 메인 패널 — 4 버튼
    // =========================================================
    private void DrawMain()
    {
        float pw = _mainPanelWidth;
        float ph = _mainPanelHeight;
        float px = (RefW - pw) * 0.5f + _mainPanelOffset.x;
        float py = (RefH - ph) * 0.5f + _mainPanelOffset.y;

        DrawPanel(px, py, pw, ph);

        GUI.Label(new Rect(px, py + _titleTopPadding, pw, 40f), L("PAUSED", "일시정지"), _titleStyle);
        DrawTitleDivider(px, py + _titleTopPadding + 40f, pw);

        float bw = _buttonWidth;
        float bh = _buttonHeight;
        float gap = _buttonGap;
        float bx = px + (pw - bw) * 0.5f;
        float by = py + _firstButtonY;

        if (DrawPauseButton(new Rect(bx, by, bw, bh), L("Resume", "계속하기")))
            Close();

        by += bh + gap;
        if (DrawPauseButton(new Rect(bx, by, bw, bh), L("Settings", "설정")))
            EnterSettings();

        by += bh + gap;
        var gsm = GameStateManager.Instance;
        bool inRun = gsm != null && gsm.State != GameState.Lobby
                                 && gsm.State != GameState.CharacterSelect
                                 && gsm.State != GameState.RelicPick
                                 && gsm.State != GameState.TechTree;
        GUI.enabled = inRun;
        if (DrawPauseButton(new Rect(bx, by, bw, bh), L("Abandon Run", "런 포기")))
            _screen = Screen_.ConfirmAbandon;
        GUI.enabled = true;

        by += bh + gap;
        if (DrawPauseButton(new Rect(bx, by, bw, bh), L("Quit Game", "게임 종료")))
            _screen = Screen_.ConfirmQuit;
    }

    // =========================================================
    // 설정 — BGM/SFX 볼륨 + 해상도 + 풀스크린
    // =========================================================
    private void EnterSettings()
    {
        var am = AudioManager.Instance;
        _bgmVol = am != null ? am.BgmVolume : 1f;
        _sfxVol = am != null ? am.SfxVolume : 1f;
        _fullscreen = UnityEngine.Screen.fullScreen;

        // 현재 해상도와 가장 가까운 프리셋 인덱스
        int curW = UnityEngine.Screen.width;
        int curH = UnityEngine.Screen.height;
        int best = 0;
        long bestDist = long.MaxValue;
        for (int i = 0; i < Resolutions.Length; i++)
        {
            long dw = Resolutions[i].x - curW;
            long dh = Resolutions[i].y - curH;
            long d = dw * dw + dh * dh;
            if (d < bestDist) { bestDist = d; best = i; }
        }
        _resIndex = best;

        _screen = Screen_.Settings;
    }

    private void DrawSettings()
    {
        float pw = _settingsPanelWidth;
        float ph = _settingsPanelHeight;
        float px = (RefW - pw) * 0.5f + _mainPanelOffset.x;
        float py = (RefH - ph) * 0.5f + _mainPanelOffset.y;

        DrawPanel(px, py, pw, ph);

        GUI.Label(new Rect(px, py + 22f, pw, 36f), L("SETTINGS", "설정"), _titleStyle);
        DrawTitleDivider(px, py + 22f + 40f, pw);

        float rowY = py + 110f;
        const float rowH = 28f;
        const float labelW = 100f;
        const float sliderW = 220f;
        const float valueW = 50f;
        float contentX = px + 30f;

        // BGM
        GUI.Label(new Rect(contentX, rowY, labelW, rowH), "BGM", _labelStyle);
        float newBgm = GUI.HorizontalSlider(new Rect(contentX + labelW, rowY + 8f, sliderW, rowH),
                                            _bgmVol, 0f, 1f);
        GUI.Label(new Rect(contentX + labelW + sliderW + 8f, rowY, valueW, rowH),
                  Mathf.RoundToInt(newBgm * 100f) + "%", _labelStyle);
        if (!Mathf.Approximately(newBgm, _bgmVol))
        {
            _bgmVol = newBgm;
            if (AudioManager.Instance != null) AudioManager.Instance.BgmVolume = newBgm;
        }

        // SFX
        rowY += 50f;
        GUI.Label(new Rect(contentX, rowY, labelW, rowH), "SFX", _labelStyle);
        float newSfx = GUI.HorizontalSlider(new Rect(contentX + labelW, rowY + 8f, sliderW, rowH),
                                            _sfxVol, 0f, 1f);
        GUI.Label(new Rect(contentX + labelW + sliderW + 8f, rowY, valueW, rowH),
                  Mathf.RoundToInt(newSfx * 100f) + "%", _labelStyle);
        if (!Mathf.Approximately(newSfx, _sfxVol))
        {
            _sfxVol = newSfx;
            if (AudioManager.Instance != null) AudioManager.Instance.SfxVolume = newSfx;
        }

        // 해상도
        rowY += 70f;
        GUI.Label(new Rect(contentX, rowY, labelW, rowH), L("Resolution", "해상도"), _labelStyle);
        const float arrowW = 32f;
        float arrowY = rowY - 4f;
        float resX = contentX + labelW;
        if (GUI.Button(new Rect(resX, arrowY, arrowW, 36f), "<", _smallBtnStyle))
            _resIndex = (_resIndex - 1 + Resolutions.Length) % Resolutions.Length;
        var res = Resolutions[_resIndex];
        GUI.Label(new Rect(resX + arrowW + 8f, rowY, 160f, rowH),
                  res.x + " × " + res.y, _labelStyle);
        if (GUI.Button(new Rect(resX + arrowW + 8f + 160f, arrowY, arrowW, 36f), ">", _smallBtnStyle))
            _resIndex = (_resIndex + 1) % Resolutions.Length;

        // 풀스크린
        rowY += 60f;
        GUI.Label(new Rect(contentX, rowY, labelW, rowH), L("Fullscreen", "전체화면"), _labelStyle);
        _fullscreen = GUI.Toggle(new Rect(contentX + labelW + 8f, rowY + 4f, 60f, rowH), _fullscreen, "");

        // Apply / Back
        rowY += 70f;
        const float btnW = 130f;
        const float btnH = 44f;
        const float btnGap = 20f;
        float totalW = btnW * 2 + btnGap;
        float btnX = px + (pw - totalW) * 0.5f;
        if (DrawPauseButton(new Rect(btnX, rowY, btnW, btnH), L("Apply", "적용")))
            ApplyDisplaySettings();
        if (DrawPauseButton(new Rect(btnX + btnW + btnGap, rowY, btnW, btnH), L("Back", "뒤로")))
            _screen = Screen_.Main;
    }

    private void ApplyDisplaySettings()
    {
        var res = Resolutions[_resIndex];
        UnityEngine.Screen.SetResolution(res.x, res.y, _fullscreen);
    }

    // =========================================================
    // 확인 다이얼로그
    // =========================================================
    private void DrawConfirmAbandon()
    {
        DrawConfirm(
            L("ABANDON RUN?", "런을 포기하시겠습니까?"),
            L("Abandon the current run and return to the lobby.\nProgress will not be saved.",
              "현재 런을 포기하고 로비로 돌아갑니다.\n진행 상황은 저장되지 않습니다."),
            L("Abandon", "포기"),
            () => _pending.Add(() =>
            {
                Close();
                GameStateManager.Instance?.ReturnToLobby();
            })
        );
    }

    private void DrawConfirmQuit()
    {
        DrawConfirm(
            L("QUIT GAME?", "게임을 종료하시겠습니까?"),
            L("The game will close.", "게임을 종료합니다."),
            L("Quit", "종료"),
            () => _pending.Add(QuitGame)
        );
    }

    private void DrawConfirm(string title, string body, string yesLabel, System.Action onYes)
    {
        float pw = _confirmPanelWidth;
        float ph = _confirmPanelHeight;
        float px = (RefW - pw) * 0.5f + _mainPanelOffset.x;
        float py = (RefH - ph) * 0.5f + _mainPanelOffset.y;

        DrawPanel(px, py, pw, ph);

        GUI.Label(new Rect(px, py + 24f, pw, 36f), title, _titleStyle);
        DrawTitleDivider(px, py + 24f + 40f, pw);
        GUI.Label(new Rect(px + 24f, py + 76f, pw - 48f, 80f), body, _hintStyle);

        const float btnW = 150f;
        const float btnH = 44f;
        const float btnGap = 20f;
        float totalW = btnW * 2 + btnGap;
        float btnX = px + (pw - totalW) * 0.5f;
        float btnY = py + ph - btnH - 24f;

        if (DrawPauseButton(new Rect(btnX, btnY, btnW, btnH), yesLabel))
            onYes?.Invoke();
        if (DrawPauseButton(new Rect(btnX + btnW + btnGap, btnY, btnW, btnH), L("Cancel", "취소")))
            _screen = Screen_.Main;
    }

    private static void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // =========================================================
    // 패널 그리기 + 스타일
    // =========================================================
    private void DrawPanel(float px, float py, float pw, float ph)
    {
        var rect = new Rect(px, py, pw, ph);
        if (_bgPanelStyle != null && _bgPanelStyle.normal.background != null)
        {
            GUI.Box(rect, GUIContent.none, _bgPanelStyle);
            return;
        }

        // 텍스처 로드 실패 시 폴백 — 옛날 코드 그대로
        var prev = GUI.color;
        GUI.color = new Color(0.08f, 0.06f, 0.10f, 0.96f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = new Color(0.70f, 0.55f, 0.25f, 0.85f);
        GUI.DrawTexture(new Rect(px, py, pw, 2f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(px, py + ph - 2f, pw, 2f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(px, py, 2f, ph), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(px + pw - 2f, py, 2f, ph), Texture2D.whiteTexture);
        GUI.color = prev;
    }

    /// <summary>버튼 1개 그리기 — _useButtonTextures 토글에 따라 텍스처 모드/코드 드로우 모드.
    /// 텍스처 모드에선 idle 텍스처만 사용하고 hover 시 노란빛 보더를 코드로 오버레이.</summary>
    private bool DrawPauseButton(Rect rect, string label)
    {
        bool hovered = rect.Contains(Event.current.mousePosition);

        if (_useButtonTextures && _btnIdleTexture != null)
        {
            // 텍스처 모드 — idle 텍스처를 항상 사용. hover 시 idle dim 해제 + 노란 보더 오버레이.
            var prev = GUI.color;
            if (!hovered) GUI.color = new Color(_btnIdleTint, _btnIdleTint, _btnIdleTint, prev.a);
            bool clicked = GUI.Button(rect, label, hovered ? _btnHoverStyleCache : _btnStyle);
            GUI.color = prev;
            if (hovered) DrawHoverBorderOverlay(rect);
            return clicked;
        }

        // 코드 드로우 모드 — 다크 채움 + 얇은 브라스 테두리 (이미지2 스타일)
        DrawCleanButton(rect, hovered);
        var textStyle = hovered ? _btnTextOnlyHoverStyle : _btnTextOnlyStyle;
        return GUI.Button(rect, label, textStyle);
    }

    /// <summary>호버 시 idle 텍스처의 brass border 위에 노란빛 보더를 그려서
    /// "테두리만 색이 바뀌는" 효과. 라운드 corner는 직선으로 근사.</summary>
    private void DrawHoverBorderOverlay(Rect rect)
    {
        var prev = GUI.color;
        GUI.color = _hoverBorderColor;
        float bw = _hoverBorderWidth;
        float i = _hoverBorderInset;
        float x = rect.x + i, y = rect.y + i, w = rect.width - i * 2f, h = rect.height - i * 2f;
        GUI.DrawTexture(new Rect(x,         y,             w,  bw), Texture2D.whiteTexture); // top
        GUI.DrawTexture(new Rect(x,         y + h - bw,    w,  bw), Texture2D.whiteTexture); // bottom
        GUI.DrawTexture(new Rect(x,         y,             bw, h ), Texture2D.whiteTexture); // left
        GUI.DrawTexture(new Rect(x + w - bw, y,            bw, h ), Texture2D.whiteTexture); // right
        GUI.color = prev;
    }

    /// <summary>타이틀 밑 brass 가로 디바이더 라인.</summary>
    private void DrawTitleDivider(float panelX, float titleBaselineY, float panelWidth)
    {
        if (!_showTitleDivider) return;
        float w = panelWidth * Mathf.Clamp01(_dividerWidthRatio);
        float x = panelX + (panelWidth - w) * 0.5f;
        float y = titleBaselineY + _dividerYOffset;
        var prev = GUI.color;
        GUI.color = _dividerColor;
        GUI.DrawTexture(new Rect(x, y, w, _dividerThickness), Texture2D.whiteTexture);
        GUI.color = prev;
    }

    /// <summary>코드 드로우 버튼 — 다크 채움 + 4면 얇은 브라스 테두리.</summary>
    private void DrawCleanButton(Rect rect, bool hovered)
    {
        var prev = GUI.color;
        // 1) 다크 채움
        GUI.color = _cleanBtnFill;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        // 2) 브라스 테두리
        Color bc = hovered ? _cleanBtnBorderHover : _cleanBtnBorderIdle;
        float bw = hovered ? _cleanBtnBorderHoverW : _cleanBtnBorderIdleW;
        GUI.color = bc;
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, bw), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x, rect.y + rect.height - bw, rect.width, bw), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x, rect.y, bw, rect.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x + rect.width - bw, rect.y, bw, rect.height), Texture2D.whiteTexture);
        GUI.color = prev;
    }

    /// <summary>로케일에 맞춰 한 언어만 반환. KR이면 한글, EN이면 영어.</summary>
    private static string L(string en, string kr)
    {
        return DianoCard.Data.LocaleSettings.Current == DianoCard.Data.Language.KR ? kr : en;
    }

    private void EnsureAssets()
    {
        if (_assetsReady) return;
        // Inspector에 안 박혔으면 Resources/PauseUI/ 에서 로드 (런타임 폴백)
        if (_bgTexture == null)
            _bgTexture = Resources.Load<Texture2D>("PauseUI/Panel_Pause_BG");
        if (_btnIdleTexture == null)
            _btnIdleTexture = Resources.Load<Texture2D>("PauseUI/Panel_Button_Idle");
        if (_btnHoverTexture == null)
            _btnHoverTexture = Resources.Load<Texture2D>("PauseUI/Panel_Button_Hover");
        if (_bgTexture      == null) Debug.LogWarning("[PauseMenuUI] Missing BG texture (Inspector + Resources/PauseUI/Panel_Pause_BG 모두 비어있음).");
        if (_btnIdleTexture == null) Debug.LogWarning("[PauseMenuUI] Missing button idle texture.");
        // _btnHoverTexture는 optional — 없으면 idle 텍스처 + 코드 보더 오버레이로 hover 표현
        _assetsReady = true;
    }

    private void EnsureStyles()
    {
        if (_stylesReady) return;
        EnsureAssets();

        // 폰트 로드 — KR 로케일이면 Hahmlet(다크판타지 명조), EN이면 Cinzel. 미적재 시 GUI 기본(Arial)로 폴백.
        if (_displayFont == null)    _displayFont    = Resources.Load<Font>("Fonts/Cinzel-VariableFont_wght");
        if (_displayFontKR == null)  _displayFontKR  = Resources.Load<Font>("Fonts/Hahmlet-VariableFont_wght");
        bool isKR = DianoCard.Data.LocaleSettings.Current == DianoCard.Data.Language.KR;
        Font menuFont = isKR ? _displayFontKR : _displayFont;

        _titleStyle = new GUIStyle(GUI.skin.label)
        {
            font = menuFont,
            fontSize = _titleFontSize,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.93f, 0.86f, 0.66f, 1f) },
        };
        _hintStyle = new GUIStyle(GUI.skin.label)
        {
            font = menuFont,
            fontSize = 16,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.78f, 0.74f, 0.62f, 0.9f) },
            wordWrap = true,
        };
        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            font = menuFont,
            fontSize = 16,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = new Color(0.88f, 0.84f, 0.72f) },
        };

        // 배경 패널 — 9-slice border: Inspector의 _bgBorder px가 corner+brass border 영역.
        _bgPanelStyle = new GUIStyle();
        if (_bgTexture != null)
        {
            _bgPanelStyle.normal.background = _bgTexture;
            _bgPanelStyle.border = new RectOffset(_bgBorder, _bgBorder, _bgBorder, _bgBorder);
        }

        // 버튼 idle/hover — 9-slice border: Inspector의 _btnBorder px.
        if (_btnIdleTexture != null)
        {
            _btnStyle = new GUIStyle
            {
                font = menuFont,
                fontSize = _buttonFontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(_btnBorder, _btnBorder, _btnBorder, _btnBorder),
                padding = new RectOffset(12, 12, 8, 8),
            };
            _btnStyle.normal.background = _btnIdleTexture;
            _btnStyle.hover.background = _btnIdleTexture;
            _btnStyle.active.background = _btnIdleTexture;
        }
        else
        {
            // 폴백 — 텍스처 못 찾으면 기본 GUI.skin.button 사용
            _btnStyle = new GUIStyle(GUI.skin.button) { font = menuFont, fontSize = _buttonFontSize, fontStyle = FontStyle.Bold };
        }
        _btnStyle.normal.textColor = _btnTextIdle;
        _btnStyle.hover.textColor = _btnTextIdle;
        _btnStyle.active.textColor = _btnTextIdle;

        _btnHoverStyleCache = new GUIStyle(_btnStyle);
        if (_btnHoverTexture != null)
        {
            _btnHoverStyleCache.normal.background = _btnHoverTexture;
            _btnHoverStyleCache.hover.background = _btnHoverTexture;
            _btnHoverStyleCache.active.background = _btnHoverTexture;
        }
        _btnHoverStyleCache.normal.textColor = _btnTextHover;
        _btnHoverStyleCache.hover.textColor = _btnTextHover;
        _btnHoverStyleCache.active.textColor = _btnTextHover;

        // 코드 드로우 모드 텍스트 전용 스타일 — 배경 없음
        _btnTextOnlyStyle = new GUIStyle
        {
            font = menuFont,
            fontSize = _buttonFontSize,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
        };
        _btnTextOnlyStyle.normal.textColor = _btnTextIdle;
        _btnTextOnlyStyle.hover.textColor = _btnTextIdle;
        _btnTextOnlyStyle.active.textColor = _btnTextIdle;

        _btnTextOnlyHoverStyle = new GUIStyle(_btnTextOnlyStyle);
        _btnTextOnlyHoverStyle.normal.textColor = _btnTextHover;
        _btnTextOnlyHoverStyle.hover.textColor = _btnTextHover;
        _btnTextOnlyHoverStyle.active.textColor = _btnTextHover;

        // Settings 화면 좌우 화살표는 좁고 작은 버튼 — GUI.skin.button 베이스지만
        // 호버 시 배경/텍스트 색이 swap되며 글자가 시프트되는 느낌이 나서 모든 state를 normal로 고정.
        _smallBtnStyle = new GUIStyle(GUI.skin.button)
        {
            font = menuFont,
            fontSize = _buttonFontSize,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
        };

        // GUI.skin.label/button 기본 hover/active state의 색·배경 변화를 차단해
        // 호버 시 텍스트가 미세하게 흔들리거나 색이 깜빡이는 느낌을 없앤다.
        LockHoverState(_titleStyle);
        LockHoverState(_hintStyle);
        LockHoverState(_labelStyle);
        LockHoverState(_btnStyle);
        LockHoverState(_btnHoverStyleCache);
        LockHoverState(_btnTextOnlyStyle);
        LockHoverState(_btnTextOnlyHoverStyle);
        LockHoverState(_smallBtnStyle);

        _stylesReady = true;
    }

    private static void LockHoverState(GUIStyle s)
    {
        if (s == null) return;
        var c = s.normal.textColor;
        var bg = s.normal.background;
        s.hover.textColor = c;     s.hover.background = bg;
        s.active.textColor = c;    s.active.background = bg;
        s.focused.textColor = c;   s.focused.background = bg;
        s.onNormal.textColor = c;  s.onNormal.background = bg;
        s.onHover.textColor = c;   s.onHover.background = bg;
        s.onActive.textColor = c;  s.onActive.background = bg;
        s.onFocused.textColor = c; s.onFocused.background = bg;
    }

    /// <summary>Inspector에서 값 변경 시 다음 OnGUI에서 GUIStyle 재빌드.</summary>
    void OnValidate()
    {
        _stylesReady = false;
    }
}
