using System.Collections.Generic;
using DianoCard.Data;
using DianoCard.Game;
using UnityEngine;
using static DianoCard.Data.LocaleSettings;

/// <summary>
/// 이벤트 노드 "룬을 만진다" 후 단일 유물 제안 + HP 더 내고 리롤하는 모달.
/// GameState == EventRelicOffer 일 때만 그려지며, EventUI(배경/타이틀/본문) 위에 dim+패널을 얹는다.
///
/// 톤: ESC 메뉴와 동일 — dim 배경 + 미니멀 패널, 브라스 명판/잼 등 장식 없음.
/// </summary>
[DefaultExecutionOrder(1060)]
public class EventRelicOfferUI : MonoBehaviour
{
    private const float RefW = 1280f;
    private const float RefH = 720f;

    [Header("Backdrop Dim")]
    [SerializeField, Range(0f, 1f)] private float backdropAlpha = 0.55f;
    [Tooltip("dim/클릭차단 시작 Y(가상 1280x720 기준). HUD 스트립 바닥(~70) 아래에 두어 상단 네비를 가리지 않게.")]
    [SerializeField, Range(0f, 200f)] private float dimTopY = 80f;

    [Header("Panel")]
    [SerializeField] private float panelWidth = 480f;
    [SerializeField] private float panelHeight = 420f;
    [SerializeField] private Color panelFill = new(0.06f, 0.05f, 0.08f, 0.92f);
    [SerializeField] private Color panelBorder = new(0.78f, 0.66f, 0.41f, 0.85f);
    [SerializeField, Range(0.5f, 4f)] private float panelBorderThickness = 1.5f;
    [SerializeField, Range(0f, 32f)] private float panelCornerRadius = 10f;

    [Header("Header Text")]
    [SerializeField] private string headerText = "룬이 형상을 이룬다…";
    [SerializeField] private int headerFontSize = 18;
    [SerializeField] private Color headerColor = new(0.84f, 0.82f, 0.76f, 1f);

    [Header("Relic Display")]
    [SerializeField] private float iconSize = 128f;
    [SerializeField] private int nameFontSize = 24;
    [SerializeField] private Color nameColor = new(0.99f, 0.95f, 0.78f, 1f);
    [SerializeField] private int descFontSize = 14;
    [SerializeField] private Color descColor = new(0.78f, 0.78f, 0.78f, 1f);

    [Header("Buttons")]
    [SerializeField] private float buttonWidth = 200f;
    [SerializeField] private float buttonHeight = 48f;
    [SerializeField] private float buttonGap = 14f;
    [SerializeField] private float buttonsBottomPadding = 24f;
    [SerializeField] private int buttonFontSize = 16;
    [SerializeField] private Color buttonLabelColor = new(0.788f, 0.659f, 0.412f, 1f);
    [SerializeField] private Color buttonLabelDisabled = new(0.45f, 0.42f, 0.36f, 1f);
    [SerializeField] private Color buttonFill = new(0.04f, 0.03f, 0.06f, 0.55f);
    [SerializeField] private Color buttonHoverFill = new(0.04f, 0.03f, 0.06f, 0.80f);
    [SerializeField] private Color buttonBorder = new(0.92f, 0.89f, 0.83f, 0.80f);
    [SerializeField] private Color buttonHoverBorder = new(1f, 0.95f, 0.78f, 1f);
    [SerializeField] private Color buttonBorderDisabled = new(0.40f, 0.38f, 0.34f, 0.6f);
    [SerializeField, Range(0.5f, 4f)] private float buttonBorderThickness = 1.5f;
    [SerializeField, Range(0f, 24f)] private float buttonCornerRadius = 6f;

    // ─── runtime ──────────────────────────────────────────────
    private GUIStyle _headerStyle;
    private GUIStyle _nameStyle;
    private GUIStyle _descStyle;
    private GUIStyle _buttonStyle;
    private bool _stylesBuilt;

    private Texture2D _defaultRelicIcon;
    private readonly Dictionary<string, Texture2D> _iconCache = new();

    // 둥근 사각 마스크 캐시 — (w,h,r,t) 기준 재생성.
    private Texture2D _panelFillMask, _panelBorderMask;
    private int _panelW = -1, _panelH = -1;
    private float _panelR = -1f, _panelT = -1f;

    private Texture2D _btnFillMask, _btnBorderMask;
    private int _btnW = -1, _btnH = -1;
    private float _btnR = -1f, _btnT = -1f;

    private readonly List<System.Action> _pending = new();

    void Start()
    {
        // relic_default.png는 실제로 없음 — 상단 HUD 슬롯과 동일한 Relic.png를 폴백으로 사용.
        _defaultRelicIcon = Resources.Load<Texture2D>("InGame/Icon/Relic");
        Debug.Log("[EventRelicOfferUI] attached & ready.");
    }

    private void BuildStyles()
    {
        if (_stylesBuilt) return;
        _stylesBuilt = true;

        var krFont = Resources.Load<Font>("Fonts/Hahmlet-VariableFont_wght");

        _headerStyle = new GUIStyle(GUI.skin.label)
        {
            font = krFont,
            fontSize = headerFontSize,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Italic,
            wordWrap = false,
        };
        _headerStyle.normal.textColor = headerColor;

        _nameStyle = new GUIStyle(GUI.skin.label)
        {
            font = krFont,
            fontSize = nameFontSize,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            wordWrap = false,
        };
        _nameStyle.normal.textColor = nameColor;

        _descStyle = new GUIStyle(GUI.skin.label)
        {
            font = krFont,
            fontSize = descFontSize,
            alignment = TextAnchor.UpperCenter,
            wordWrap = true,
        };
        _descStyle.normal.textColor = descColor;

        _buttonStyle = new GUIStyle(GUI.skin.label)
        {
            font = krFont,
            fontSize = buttonFontSize,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Italic,
            wordWrap = false,
        };
        _buttonStyle.normal.textColor = buttonLabelColor;

        // GUI.skin.label 기본 hover state의 색 swap을 차단해 호버 시 텍스트가 깜빡이는 느낌을 없앤다.
        LockHoverState(_headerStyle);
        LockHoverState(_nameStyle);
        LockHoverState(_descStyle);
        LockHoverState(_buttonStyle);
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

    void OnGUI()
    {
        if (PauseMenuUI.IsOpen) return;
        var gsm = GameStateManager.Instance;
        if (gsm == null || gsm.State != GameState.EventRelicOffer) return;

        // 다음 프레임에서 처리 — OnGUI 중 상태 변경 시 GUI 레이아웃 깨짐 방지.
        foreach (var a in _pending) a?.Invoke();
        _pending.Clear();

        BuildStyles();
        // 인스펙터 변경 즉시 반영 — color 바꾼 직후 LockHoverState로 hover state 동기화
        _headerStyle.fontSize = headerFontSize;
        _headerStyle.normal.textColor = headerColor;
        LockHoverState(_headerStyle);
        _nameStyle.fontSize = nameFontSize;
        _nameStyle.normal.textColor = nameColor;
        LockHoverState(_nameStyle);
        _descStyle.fontSize = descFontSize;
        _descStyle.normal.textColor = descColor;
        LockHoverState(_descStyle);
        _buttonStyle.fontSize = buttonFontSize;

        // 1280x720 가상좌표로 작업 — dim/클릭차단도 같은 좌표에서 그려 상단 HUD 영역(0~dimTopY)은 보존.
        float scale = Mathf.Min(Screen.width / RefW, Screen.height / RefH);
        var matrix = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

        var dimRect = new Rect(0f, dimTopY, RefW, RefH - dimTopY);
        var prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, backdropAlpha);
        GUI.DrawTexture(dimRect, Texture2D.whiteTexture);
        GUI.color = prev;

        DrawPanel(gsm);

        // 외부 클릭 차단 — HUD 영역(0~dimTopY) 클릭은 살려두고 그 외만 막는다.
        // 반드시 DrawPanel 뒤에 둬야 함: IMGUI는 같은 좌표 위 첫 Button이 HotControl을 잡고
        // Event.Use()로 이벤트를 소비하므로, 먼저 그리면 패널 버튼(받는다/리롤)이 클릭을 받지 못한다.
        GUI.Button(dimRect, GUIContent.none, GUIStyle.none);

        GUI.matrix = matrix;
    }

    private void DrawPanel(GameStateManager gsm)
    {
        float px = (RefW - panelWidth) * 0.5f;
        float py = (RefH - panelHeight) * 0.5f;
        var panelRect = new Rect(px, py, panelWidth, panelHeight);

        DrawRoundedPanel(panelRect);

        float cy = py + 28f;
        GUI.Label(new Rect(px, cy, panelWidth, headerFontSize + 8f), L("The rune takes shape…", headerText), _headerStyle);
        cy += headerFontSize + 22f;

        // 아이콘
        var relic = gsm.EventOfferedRelic;
        var iconRect = new Rect(px + (panelWidth - iconSize) * 0.5f, cy, iconSize, iconSize);
        var icon = GetIcon(relic != null ? relic.id : null);
        if (icon != null)
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
        cy += iconSize + 12f;

        // 이름
        GUI.Label(new Rect(px + 16f, cy, panelWidth - 32f, nameFontSize + 8f),
                  relic != null ? (relic.name ?? relic.id) : "—", _nameStyle);
        cy += nameFontSize + 10f;

        // 설명 — 버튼 영역 위까지 차지
        float btnAreaH = buttonHeight * 2f + buttonGap + buttonsBottomPadding;
        float descTop = cy;
        float descH = (py + panelHeight - btnAreaH) - descTop - 8f;
        if (descH < 28f) descH = 28f;
        GUI.Label(new Rect(px + 18f, descTop, panelWidth - 36f, descH),
                  relic != null ? (relic.description ?? string.Empty) : string.Empty, _descStyle);

        // 버튼들 — 받기 / 리롤
        float bw = buttonWidth;
        float bx = px + (panelWidth - bw) * 0.5f;
        float by = py + panelHeight - btnAreaH + 0f;

        var mouse = Event.current.mousePosition;

        // 받기
        var acceptRect = new Rect(bx, by, bw, buttonHeight);
        bool acceptHover = acceptRect.Contains(mouse);
        DrawButton(acceptRect, acceptHover, true);
        GUI.Label(acceptRect, L("Accept", "받는다"), _buttonStyle);
        if (GUI.Button(acceptRect, GUIContent.none, GUIStyle.none))
            _pending.Add(() => gsm.AcceptEventRelic());

        // 리롤
        int hp = gsm.CurrentRun != null ? gsm.CurrentRun.playerCurrentHp : 0;
        int cost = gsm.EventRerollCost;
        bool canReroll = hp >= cost;

        var rerollRect = new Rect(bx, by + buttonHeight + buttonGap, bw, buttonHeight);
        bool rerollHover = canReroll && rerollRect.Contains(mouse);
        DrawButton(rerollRect, rerollHover, canReroll);
        var labelPrev = _buttonStyle.normal.textColor;
        _buttonStyle.normal.textColor = canReroll
            ? (rerollHover ? new Color(1f, 0.95f, 0.78f, 1f) : buttonLabelColor)
            : buttonLabelDisabled;
        LockHoverState(_buttonStyle); // hover state까지 즉시 동기화 — 마우스 위에서 색이 튀는 현상 방지
        GUI.Label(rerollRect, L($"Reroll  ·  HP -{cost}", $"다시 굴린다  ·  HP -{cost}"), _buttonStyle);
        _buttonStyle.normal.textColor = labelPrev;
        LockHoverState(_buttonStyle);
        if (canReroll && GUI.Button(rerollRect, GUIContent.none, GUIStyle.none))
            _pending.Add(() => gsm.RerollEventRelic());
    }

    private void DrawRoundedPanel(Rect rect)
    {
        int w = Mathf.Max(2, Mathf.RoundToInt(rect.width));
        int h = Mathf.Max(2, Mathf.RoundToInt(rect.height));
        float r = Mathf.Min(panelCornerRadius, Mathf.Min(w, h) * 0.5f);
        float t = Mathf.Max(1f, panelBorderThickness);

        if (_panelFillMask == null || _panelBorderMask == null
            || _panelW != w || _panelH != h
            || !Mathf.Approximately(_panelR, r)
            || !Mathf.Approximately(_panelT, t))
        {
            BuildRoundedMasks(w, h, r, t, ref _panelFillMask, ref _panelBorderMask);
            _panelW = w; _panelH = h; _panelR = r; _panelT = t;
        }

        var prev = GUI.color;
        GUI.color = panelFill;
        GUI.DrawTexture(rect, _panelFillMask, ScaleMode.StretchToFill);
        GUI.color = panelBorder;
        GUI.DrawTexture(rect, _panelBorderMask, ScaleMode.StretchToFill);
        GUI.color = prev;
    }

    private void DrawButton(Rect rect, bool hovered, bool enabled)
    {
        int w = Mathf.Max(2, Mathf.RoundToInt(rect.width));
        int h = Mathf.Max(2, Mathf.RoundToInt(rect.height));
        float r = Mathf.Min(buttonCornerRadius, Mathf.Min(w, h) * 0.5f);
        float t = Mathf.Max(1f, buttonBorderThickness);

        if (_btnFillMask == null || _btnBorderMask == null
            || _btnW != w || _btnH != h
            || !Mathf.Approximately(_btnR, r)
            || !Mathf.Approximately(_btnT, t))
        {
            BuildRoundedMasks(w, h, r, t, ref _btnFillMask, ref _btnBorderMask);
            _btnW = w; _btnH = h; _btnR = r; _btnT = t;
        }

        var prev = GUI.color;
        GUI.color = hovered ? buttonHoverFill : buttonFill;
        GUI.DrawTexture(rect, _btnFillMask, ScaleMode.StretchToFill);
        GUI.color = !enabled
            ? buttonBorderDisabled
            : (hovered ? buttonHoverBorder : buttonBorder);
        GUI.DrawTexture(rect, _btnBorderMask, ScaleMode.StretchToFill);
        GUI.color = prev;
    }

    private static void BuildRoundedMasks(int w, int h, float r, float t,
                                          ref Texture2D fillMask, ref Texture2D borderMask)
    {
        if (fillMask != null) Destroy(fillMask);
        if (borderMask != null) Destroy(borderMask);

        fillMask = new Texture2D(w, h, TextureFormat.Alpha8, false) { wrapMode = TextureWrapMode.Clamp };
        borderMask = new Texture2D(w, h, TextureFormat.Alpha8, false) { wrapMode = TextureWrapMode.Clamp };

        var fillPixels = new Color[w * h];
        var borderPixels = new Color[w * h];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float dx = Mathf.Max(Mathf.Abs(x + 0.5f - w * 0.5f) - (w * 0.5f - r), 0f);
                float dy = Mathf.Max(Mathf.Abs(y + 0.5f - h * 0.5f) - (h * 0.5f - r), 0f);
                float outsideDist = Mathf.Sqrt(dx * dx + dy * dy) - r;

                float fillAlpha = Mathf.Clamp01(0.5f - outsideDist);
                fillPixels[y * w + x] = new Color(0f, 0f, 0f, fillAlpha);

                float borderAlpha = 0f;
                if (outsideDist > -t - 0.5f && outsideDist < 0.5f)
                {
                    float aOuter = Mathf.Clamp01(0.5f - outsideDist);
                    float aInner = Mathf.Clamp01(0.5f + (outsideDist + t));
                    borderAlpha = Mathf.Min(aOuter, aInner);
                }
                borderPixels[y * w + x] = new Color(0f, 0f, 0f, borderAlpha);
            }
        }

        fillMask.SetPixels(fillPixels); fillMask.Apply();
        borderMask.SetPixels(borderPixels); borderMask.Apply();
    }

    private Texture2D GetIcon(string id)
    {
        if (string.IsNullOrEmpty(id)) return _defaultRelicIcon;
        if (_iconCache.TryGetValue(id, out var cached)) return cached;
        var tex = Resources.Load<Texture2D>($"InGame/RelicArt/{id}");
        if (tex == null) tex = _defaultRelicIcon;
        _iconCache[id] = tex;
        return tex;
    }
}
