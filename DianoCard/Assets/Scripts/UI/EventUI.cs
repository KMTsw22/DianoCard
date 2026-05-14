using System.Collections.Generic;
using DianoCard.Data;
using DianoCard.Game;
using UnityEngine;
using static DianoCard.Data.LocaleSettings;

/// <summary>
/// 이벤트(미지) 노드 화면. GameState == Event 일 때만 그려짐.
///
/// MVP: 단일 하드코딩 이벤트 "부서진 룬 제단".
///   배경: Resources/MysteryUI/* 의 첫 텍스처 (전체 화면).
///   상단 85% 영역에 배경, 하단/중앙에 타이틀·본문·버튼 오버레이.
///   - 버튼 0: "[ 룬을 만진다 · HP -3, 유물 1개 획득 ]"
///   - 버튼 1: "[ 그냥 지나간다 ]"
/// 선택 결과 처리는 GameStateManager.ResolveEventChoice(idx).
/// </summary>
[DefaultExecutionOrder(1050)]
public class EventUI : MonoBehaviour
{
    // 반응형 (AspectScaler) — 화면 비율에 맞춰 자동 확장.
    private static float RefW => DianoCard.UI.AspectScaler.ScreenW;
    private static float RefH => DianoCard.UI.AspectScaler.ScreenH;

    [Header("Background")]
    [Tooltip("배경 이미지 폴더 (Resources/ 이하). 폴더 내 모든 텍스처 중 하나를 랜덤 선택해 사용.")]
    [SerializeField] private string bgFolder = "MysteryUI";
    [SerializeField] private ScaleMode bgScaleMode = ScaleMode.ScaleAndCrop;

    [Header("Cinematic Treatment — 가볍게만, 텍스트 가독성 우선")]
    [Tooltip("전체 컬러 오버레이. 텍스트 가독성을 위해 거의 비활성에 가깝게.")]
    [SerializeField] private Color colorWashTint = new Color(0f, 0f, 0f, 0.08f);
    [Tooltip("비네팅 활성화 — 모서리만 살짝.")]
    [SerializeField] private bool vignetteEnabled = true;
    [SerializeField, Range(0f, 1f)] private float vignetteAlpha = 0.30f;
    [Tooltip("비네팅 시작 반지름. 1에 가까울수록 가장자리에만 적용.")]
    [SerializeField, Range(0.1f, 1f)] private float vignetteRadius = 0.70f;
    [Tooltip("비네팅 부드러움. 클수록 경계가 부드럽다.")]
    [SerializeField, Range(0.5f, 4f)] private float vignetteSoftness = 1.8f;

    [Header("Title — '부서진 룬 제단'")]
    [SerializeField] private string titleText = "부서진 룬 제단";
    [SerializeField] private int titleFontSize = 56;
    [SerializeField] private Color titleColor = new Color(0.97f, 0.94f, 0.87f, 1f);
    [Tooltip("타이틀 Y (참조 720 기준).")]
    [SerializeField, Range(0f, 720f)] private float titleY = 320f;
    [Tooltip("타이틀 그림자 (밝은 배경에서도 가독성 확보).")]
    [SerializeField] private Color titleShadowColor = new Color(0f, 0f, 0f, 0.85f);
    [SerializeField] private Vector2 titleShadowOffset = new Vector2(3f, 3f);
    [Tooltip("타이틀 외곽 4방향 보조 섀도우 — 더 진한 윤곽 효과.")]
    [SerializeField] private bool titleHaloShadow = true;
    [SerializeField, Range(0f, 1f)] private float titleHaloAlpha = 0.55f;

    [Header("Body — 3 lines")]
    [SerializeField] private string[] bodyLines = new[]
    {
        "고대의 제단이 희미한 보랏빛으로 맥동한다.",
        "표면을 덮은 룬 하나가 아직도 살아있는 듯하다.",
        "손을 대면 무언가 일어날지도 모른다.",
    };
    [SerializeField] private int bodyFontSize = 21;
    [SerializeField] private Color bodyColor = new Color(0.84f, 0.82f, 0.76f, 1f);
    [Tooltip("본문 그림자 (옅게).")]
    [SerializeField] private Color bodyShadowColor = new Color(0f, 0f, 0f, 0.70f);
    [SerializeField] private Vector2 bodyShadowOffset = new Vector2(2f, 2f);
    [Tooltip("타이틀 아래 첫 본문 라인까지 간격.")]
    [SerializeField] private float bodyTopGap = 18f;
    [Tooltip("본문 라인 높이.")]
    [SerializeField] private float bodyLineHeight = 30f;

    [Header("Buttons")]
    [SerializeField] private string[] buttonLabels = new[]
    {
        "룬을 만진다  ·  HP -3, 유물 1개 획득",
        "그냥 지나간다",
    };
    [SerializeField, Range(0.2f, 0.95f)] private float buttonWidthRatio = 0.50f;
    [SerializeField] private float buttonHeight = 56f;
    [SerializeField] private float buttonGap = 18f;
    [Tooltip("두 번째(마지막) 버튼의 바닥에서부터의 여백.")]
    [SerializeField] private float buttonBottomMargin = 64f;
    [SerializeField] private int buttonFontSize = 17;
    // 위 타이틀/본문은 크림 톤(0.97/0.84) → 버튼 라벨은 aged-brass(#c9a86a)로 위계 차이.
    [SerializeField] private Color buttonLabelColor = new Color(0.788f, 0.659f, 0.412f, 1f);
    [SerializeField] private Color buttonBorderColor = new Color(0.92f, 0.89f, 0.83f, 0.85f);
    [SerializeField] private Color buttonFillColor = new Color(0.04f, 0.03f, 0.06f, 0.55f);
    [SerializeField] private Color buttonHoverFillColor = new Color(0.04f, 0.03f, 0.06f, 0.75f);
    [SerializeField] private Color buttonHoverBorderColor = new Color(1f, 0.95f, 0.78f, 1f);
    [SerializeField] private float buttonBorderThickness = 1.5f;
    [Tooltip("버튼 코너 라운딩 반경 (px, 참조 720 기준). 8 정도가 가장 깔끔.")]
    [SerializeField, Range(0f, 32f)] private float buttonCornerRadius = 8f;

    // ─── Runtime ───────────────────────────────────────────────
    private Texture2D _bgTex;

    private Texture2D _vignetteTex;
    private float _cachedVigRadius = -1f;
    private float _cachedVigSoftness = -1f;

    // 버튼 라운드 — fill 마스크 + border 마스크 캐시. (w,h,r) 기준 재생성.
    private Texture2D _btnFillMask;
    private Texture2D _btnBorderMask;
    private int _cachedBtnW = -1, _cachedBtnH = -1;
    private float _cachedBtnRadius = -1f;
    private float _cachedBtnBorderT = -1f;

    private GUIStyle _titleStyle;
    private GUIStyle _bodyStyle;
    private GUIStyle _buttonLabelStyle;
    private bool _stylesBuilt;

    // 폰트 — 한글은 Hahmlet 명조(없으면 시스템 Arial로 떨어져 글리프 깨짐), 영문은 Cinzel.
    private Font _displayFont;
    private Font _displayFontKR;

    private readonly List<System.Action> _pending = new();
    private bool _warnedNoTopBar;

    void Start()
    {
        LoadBackground();
    }

    // _pending는 OnGUI 안에서 처리하면 ResolveEventChoice/Accept/Reroll이 state를 다른 상태로
    // 바꾸는 순간 같은 Repaint 이벤트에서 다음 UI의 OnGUI가 호출되어 IMGUI 레이아웃이 꼬인다.
    // Update에서 처리해 state 전이를 프레임 경계로 밀어준다.
    void Update()
    {
        if (_pending.Count == 0) return;
        var gsm = GameStateManager.Instance;
        if (gsm == null
            || (gsm.State != GameState.Event && gsm.State != GameState.EventRelicOffer))
        {
            _pending.Clear();
            return;
        }
        for (int i = 0; i < _pending.Count; i++) _pending[i]?.Invoke();
        _pending.Clear();
    }

    private void LoadBackground()
    {
        if (string.IsNullOrEmpty(bgFolder)) return;
        var all = Resources.LoadAll<Texture2D>(bgFolder);
        if (all != null && all.Length > 0)
        {
            _bgTex = all[Random.Range(0, all.Length)];
            Debug.Log($"[EventUI] Loaded bg '{_bgTex.name}' from Resources/{bgFolder} (pool={all.Length})");
        }
        else
        {
            Debug.LogWarning($"[EventUI] No textures found in Resources/{bgFolder} — falling back to flat color.");
        }
    }

    private void BuildStyles()
    {
        if (_stylesBuilt) return;
        _stylesBuilt = true;

        if (_displayFont == null)   _displayFont   = Resources.Load<Font>("Fonts/Cinzel-VariableFont_wght");
        if (_displayFontKR == null) _displayFontKR = Resources.Load<Font>("Fonts/Hahmlet-VariableFont_wght");
        bool isKR = DianoCard.Data.LocaleSettings.Current == DianoCard.Data.Language.KR;
        Font textFont = isKR ? _displayFontKR : _displayFont;

        _titleStyle = new GUIStyle(GUI.skin.label)
        {
            font = textFont,
            fontSize = titleFontSize,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            wordWrap = false,
        };
        _titleStyle.normal.textColor = titleColor;

        _bodyStyle = new GUIStyle(GUI.skin.label)
        {
            font = textFont,
            fontSize = bodyFontSize,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = false,
        };
        _bodyStyle.normal.textColor = bodyColor;

        // 버튼 라벨 — 위 타이틀/본문(Bold/Normal 크림)과 분리해 italic + 가벼운 weight로 "선택지" 톤.
        _buttonLabelStyle = new GUIStyle(GUI.skin.label)
        {
            font = textFont,
            fontSize = buttonFontSize,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Italic,
            wordWrap = false,
        };
        _buttonLabelStyle.normal.textColor = buttonLabelColor;

        // GUI.skin.label 기본 hover state의 색 swap을 차단해 호버 시 텍스트가 깜빡이는 느낌을 없앤다.
        LockHoverState(_titleStyle);
        LockHoverState(_bodyStyle);
        LockHoverState(_buttonLabelStyle);
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
        if (gsm == null) return;
        // EventRelicOffer 상태에서도 배경/타이틀/본문은 계속 깔아둔다 — 모달이 그 위에 뜸.
        bool inEvent = gsm.State == GameState.Event;
        bool inOffer = gsm.State == GameState.EventRelicOffer;
        if (!inEvent && !inOffer) return;

        BuildStyles();
        // 인스펙터 변경 즉시 반영 — color 바꾼 직후 LockHoverState로 hover state 동기화
        _titleStyle.fontSize = titleFontSize;
        _titleStyle.normal.textColor = titleColor;
        LockHoverState(_titleStyle);
        _bodyStyle.fontSize = bodyFontSize;
        _bodyStyle.normal.textColor = bodyColor;
        LockHoverState(_bodyStyle);
        _buttonLabelStyle.fontSize = buttonFontSize;
        _buttonLabelStyle.normal.textColor = buttonLabelColor;
        LockHoverState(_buttonLabelStyle);

        // 반응형 uniform 스케일 (RefW/RefH가 동적이라 edge 정렬 자동).
        var matrix = GUI.matrix;
        GUI.matrix = DianoCard.UI.AspectScaler.GuiMatrix;

        DrawBackground();
        DrawColorWash();
        if (vignetteEnabled) DrawVignette();
        DrawTitleAndBody();
        if (inEvent) DrawButtons(gsm);
        if (inOffer) DrawOfferModal(gsm);

        // 배틀/맵/마을 공용 상단 HUD — 미지 노드도 동일 스트립 유지.
        var battleUI = gsm.GetComponent<BattleUI>();
        if (battleUI != null && gsm.CurrentRun != null)
        {
            var map = gsm.CurrentMap;
            int floor = map != null ? map.currentFloor : gsm.CurrentRun.currentFloor;
            int total = map != null ? map.totalFloors : 15;
            battleUI.DrawTopBar(BattleUI.HudContext.Map, gsm.CurrentRun, floor, total);
            battleUI.DrawDeckViewerOverlay(gsm);
            battleUI.DrawRelicViewerOverlay(gsm);
            battleUI.DrawPotionViewerOverlay(gsm);
        }
        else if (!_warnedNoTopBar)
        {
            _warnedNoTopBar = true;
            Debug.LogWarning($"[EventUI] Top HUD not drawn — battleUI={(battleUI == null ? "null" : "ok")}, run={(gsm.CurrentRun == null ? "null" : "ok")}");
        }

        GUI.matrix = matrix;
    }

    private void DrawBackground()
    {
        if (_bgTex != null)
        {
            GUI.DrawTexture(new Rect(0, 0, RefW, RefH), _bgTex, bgScaleMode);
        }
        else
        {
            DrawFilledRect(new Rect(0, 0, RefW, RefH), new Color(0.07f, 0.07f, 0.12f, 1f));
        }
    }

    private void DrawColorWash()
    {
        if (colorWashTint.a <= 0f) return;
        DrawFilledRect(new Rect(0, 0, RefW, RefH), colorWashTint);
    }

    private void DrawVignette()
    {
        if (_vignetteTex == null
            || _cachedVigRadius != vignetteRadius
            || _cachedVigSoftness != vignetteSoftness)
        {
            BuildVignetteTex();
        }
        var prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, vignetteAlpha);
        GUI.DrawTexture(new Rect(0, 0, RefW, RefH), _vignetteTex, ScaleMode.StretchToFill);
        GUI.color = prev;
    }

    private void BuildVignetteTex()
    {
        const int Size = 128;
        if (_vignetteTex != null) Destroy(_vignetteTex);
        _vignetteTex = new Texture2D(Size, Size, TextureFormat.Alpha8, false);
        _vignetteTex.wrapMode = TextureWrapMode.Clamp;

        float r = vignetteRadius;
        float soft = vignetteSoftness;
        var pixels = new Color[Size * Size];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                float nx = (x / (float)(Size - 1)) * 2f - 1f;
                float ny = (y / (float)(Size - 1)) * 2f - 1f;
                float dist = Mathf.Sqrt(nx * nx + ny * ny);
                float a = Mathf.Pow(Mathf.Clamp01((dist - r) / (1.42f - r)), soft);
                pixels[y * Size + x] = new Color(0f, 0f, 0f, a);
            }
        }
        _vignetteTex.SetPixels(pixels);
        _vignetteTex.Apply();
        _cachedVigRadius = vignetteRadius;
        _cachedVigSoftness = vignetteSoftness;
    }

    private void DrawTitleAndBody()
    {
        // ─── Title ─────────────────────────────────────────────
        // 4방향 헤일로 섀도우 → 진한 드롭 섀도우 → 본문 순으로 쌓아 가독성 확보.
        // 색 변경 후 즉시 LockHoverState 재호출 — IMGUI가 hover state로 렌더링해
        // 헤일로/섀도우 색이 본문 색으로 보이는 깜빡임을 차단.
        var titleRect = new Rect(0, titleY, RefW, titleFontSize + 16f);
        var prev = _titleStyle.normal.textColor;

        if (titleHaloShadow)
        {
            var halo = new Color(titleShadowColor.r, titleShadowColor.g, titleShadowColor.b,
                                 titleShadowColor.a * titleHaloAlpha);
            _titleStyle.normal.textColor = halo;
            LockHoverState(_titleStyle);
            float d = 2f;
            GUI.Label(new Rect(titleRect.x - d, titleRect.y,     titleRect.width, titleRect.height), titleText, _titleStyle);
            GUI.Label(new Rect(titleRect.x + d, titleRect.y,     titleRect.width, titleRect.height), titleText, _titleStyle);
            GUI.Label(new Rect(titleRect.x,     titleRect.y - d, titleRect.width, titleRect.height), titleText, _titleStyle);
            GUI.Label(new Rect(titleRect.x,     titleRect.y + d, titleRect.width, titleRect.height), titleText, _titleStyle);
        }

        _titleStyle.normal.textColor = titleShadowColor;
        LockHoverState(_titleStyle);
        GUI.Label(new Rect(titleRect.x + titleShadowOffset.x, titleRect.y + titleShadowOffset.y, titleRect.width, titleRect.height),
                  titleText, _titleStyle);

        _titleStyle.normal.textColor = prev;
        LockHoverState(_titleStyle);
        GUI.Label(titleRect, titleText, _titleStyle);

        // ─── Body ──────────────────────────────────────────────
        float bodyStartY = titleY + (titleFontSize + 16f) + bodyTopGap;
        var bodyPrev = _bodyStyle.normal.textColor;
        for (int i = 0; i < bodyLines.Length; i++)
        {
            float y = bodyStartY + i * bodyLineHeight;
            // 옅은 그림자
            _bodyStyle.normal.textColor = bodyShadowColor;
            LockHoverState(_bodyStyle);
            GUI.Label(new Rect(bodyShadowOffset.x, y + bodyShadowOffset.y, RefW, bodyLineHeight + 4f), bodyLines[i], _bodyStyle);
            _bodyStyle.normal.textColor = bodyPrev;
            LockHoverState(_bodyStyle);
            GUI.Label(new Rect(0, y, RefW, bodyLineHeight + 4f), bodyLines[i], _bodyStyle);
        }
    }

    private void DrawButtons(GameStateManager gsm)
    {
        int n = buttonLabels != null ? buttonLabels.Length : 0;
        if (n == 0) return;

        float btnW = RefW * buttonWidthRatio;
        float btnX = (RefW - btnW) * 0.5f;

        // 마지막 버튼이 buttonBottomMargin 만큼 떨어지도록 역산
        float lastY = RefH - buttonBottomMargin - buttonHeight;
        float startY = lastY - (n - 1) * (buttonHeight + buttonGap);

        var mouse = Event.current.mousePosition;

        for (int i = 0; i < n; i++)
        {
            float y = startY + i * (buttonHeight + buttonGap);
            var rect = new Rect(btnX, y, btnW, buttonHeight);
            bool hovered = rect.Contains(mouse);

            DrawButtonChrome(rect, hovered);
            GUI.Label(rect, buttonLabels[i], _buttonLabelStyle);

            if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
            {
                int picked = i;
                _pending.Add(() => gsm.ResolveEventChoice(picked));
            }
        }
    }

    // ─── EventRelicOffer 모달 ──────────────────────────────────
    // 별도 컴포넌트(EventRelicOfferUI)에 두면 에디터 hot-reload 등에서 그려지지 않는
    // 케이스가 발생해 EventUI 내부로 인라인. 전리품(RewardUI)과 동일한 텍스처 셋을
    // 그대로 재사용해 톤 일관성 유지 + 벡터 느낌 제거.
    private readonly Dictionary<string, Texture2D> _relicIconCache = new();
    private Texture2D _defaultRelicIcon;
    private Texture2D _offerPanelTex;     // Reward/Panel
    private Texture2D _offerRowTex;       // Reward/RowButton
    private Texture2D _offerMedallionTex; // Reward/MedallionRing
    private GUIStyle _offerTitleStyle;
    private GUIStyle _offerNameStyle;
    private GUIStyle _offerDescStyle;
    private GUIStyle _offerRowLabelStyle;
    private bool _offerStylesReady;

    private Texture2D GetRelicIcon(string id)
    {
        if (_defaultRelicIcon == null)
            _defaultRelicIcon = Resources.Load<Texture2D>("InGame/Icon/Relic");
        if (string.IsNullOrEmpty(id)) return _defaultRelicIcon;
        if (_relicIconCache.TryGetValue(id, out var cached)) return cached;
        var tex = Resources.Load<Texture2D>($"InGame/RelicArt/{id}");
        if (tex == null) tex = _defaultRelicIcon;
        _relicIconCache[id] = tex;
        return tex;
    }

    private void EnsureOfferStyles()
    {
        if (_offerStylesReady) return;
        _offerStylesReady = true;

        _offerPanelTex     = Resources.Load<Texture2D>("Reward/Panel");
        _offerRowTex       = Resources.Load<Texture2D>("Reward/RowButton");
        _offerMedallionTex = Resources.Load<Texture2D>("Reward/MedallionRing");

        var krFont = Resources.Load<Font>("Fonts/Hahmlet-VariableFont_wght");

        // 타이틀 — 전리품과 동일한 aged-brass(#c9a86a) + Hahmlet Bold.
        _offerTitleStyle = new GUIStyle(GUI.skin.label)
        {
            font = krFont, fontSize = 24, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.788f, 0.659f, 0.412f) },
        };
        // 유물 이름 — 크림 아이보리(#FCF2C7).
        _offerNameStyle = new GUIStyle(GUI.skin.label)
        {
            font = krFont, fontSize = 22, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.99f, 0.95f, 0.78f) },
        };
        // 설명 — 머티드 아이보리, wrap.
        _offerDescStyle = new GUIStyle(GUI.skin.label)
        {
            font = krFont, fontSize = 14, alignment = TextAnchor.UpperCenter, wordWrap = true,
            normal = { textColor = new Color(0.85f, 0.83f, 0.75f) },
        };
        // 행 라벨 — 전리품 행과 동일 톤(크림).
        _offerRowLabelStyle = new GUIStyle(GUI.skin.label)
        {
            font = krFont, fontSize = 17, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.99f, 0.95f, 0.78f) },
        };
        LockHoverState(_offerTitleStyle);
        LockHoverState(_offerNameStyle);
        LockHoverState(_offerDescStyle);
        LockHoverState(_offerRowLabelStyle);
    }

    private void DrawOfferModal(GameStateManager gsm)
    {
        EnsureOfferStyles();

        // 패널 — 전리품과 동일 크기/위치 톤(380x490).
        const float panelW = 380f;
        const float panelH = 490f;
        float panelX = (RefW - panelW) * 0.5f;
        float panelY = (RefH - panelH) * 0.5f;
        var panelRect = new Rect(panelX, panelY, panelW, panelH);

        // dim — HUD 영역(상단 ~80px)은 살려둔다.
        var dimRect = new Rect(0f, 80f, RefW, RefH - 80f);
        var prevColor = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(dimRect, Texture2D.whiteTexture);
        GUI.color = prevColor;

        // 패널 본체 — Reward/Panel 텍스처 재사용. 없으면 단색 폴백.
        if (_offerPanelTex != null)
            GUI.DrawTexture(panelRect, _offerPanelTex, ScaleMode.StretchToFill);
        else
            DrawFilledRect(panelRect, new Color(0.10f, 0.10f, 0.16f, 0.96f));

        // 타이틀 — 패널 상단 헤더 라인 안쪽 (Panel.png 상단 디바이더 위치).
        GUI.Label(new Rect(panelX, panelY + 12f, panelW, 40f),
                  L("Relic Discovered", "유물 발견"), _offerTitleStyle);

        // 메달리온 + 유물 아이콘 — 패널 헤더 아래.
        const float medSize = 156f;
        float medCenterX = panelRect.center.x;
        float medCenterY = panelY + 130f;
        var medRect = new Rect(medCenterX - medSize * 0.5f, medCenterY - medSize * 0.5f, medSize, medSize);
        if (_offerMedallionTex != null)
            GUI.DrawTexture(medRect, _offerMedallionTex, ScaleMode.ScaleToFit);

        var relic = gsm.EventOfferedRelic;
        var icon = GetRelicIcon(relic != null ? relic.id : null);
        if (icon != null)
        {
            // 메달리온 안쪽에 아이콘 — 브라스 링 두께 고려해 안쪽 ~58% 영역에 맞춤.
            float iconSize = medSize * 0.58f;
            var iconRect = new Rect(medCenterX - iconSize * 0.5f, medCenterY - iconSize * 0.5f, iconSize, iconSize);
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
        }

        // 유물 이름 — 메달리온 아래.
        float nameY = medRect.yMax + 8f;
        GUI.Label(new Rect(panelX + 16f, nameY, panelW - 32f, 30f),
                  relic != null ? (relic.name ?? relic.id) : "—", _offerNameStyle);

        // 설명 — 이름 아래, 버튼 위.
        float descY = nameY + 34f;
        float descH = 60f;
        GUI.Label(new Rect(panelX + 22f, descY, panelW - 44f, descH),
                  relic != null ? (relic.description ?? string.Empty) : string.Empty, _offerDescStyle);

        // 두 행 — Accept / Reroll. 전리품 RowButton 텍스처 재사용.
        const float rowW = 300f;
        const float rowH = 56f;
        const float rowGap = 14f;
        float rowX = panelRect.center.x - rowW * 0.5f;
        float rerollRowY = panelRect.yMax - 28f - rowH;
        float acceptRowY = rerollRowY - rowH - rowGap;

        var mouse = Event.current.mousePosition;

        // Accept 행
        DrawOfferRow(new Rect(rowX, acceptRowY, rowW, rowH),
                     L("Accept", "받는다"),
                     enabled: true, mouse: mouse,
                     onClick: () => _pending.Add(() => gsm.AcceptEventRelic()));

        // Reroll 행 — HP 부족 시 비활성.
        int hp = gsm.CurrentRun != null ? gsm.CurrentRun.playerCurrentHp : 0;
        int cost = gsm.EventRerollCost;
        bool canReroll = hp >= cost;
        DrawOfferRow(new Rect(rowX, rerollRowY, rowW, rowH),
                     L($"Reroll  ·  HP -{cost}", $"다시 굴린다  ·  HP -{cost}"),
                     enabled: canReroll, mouse: mouse,
                     onClick: () => _pending.Add(() => gsm.RerollEventRelic()));

        // 패널 바깥 클릭은 흡수 — 뒷쪽 HUD 클릭이 새지 않게.
        GUI.Button(dimRect, GUIContent.none, GUIStyle.none);
    }

    private void DrawOfferRow(Rect rect, string label, bool enabled, Vector2 mouse, System.Action onClick)
    {
        bool hovered = enabled && rect.Contains(mouse);

        // hover 시 살짝 부풀어 오르기 (전리품 행과 동일 패턴).
        var prevMatrix = GUI.matrix;
        if (hovered)
            GUIUtility.ScaleAroundPivot(new Vector2(1.04f, 1.04f), rect.center);

        var prevColor = GUI.color;
        // 비활성 행은 회색 톤으로.
        GUI.color = enabled ? Color.white : new Color(0.55f, 0.55f, 0.55f, 0.85f);
        if (_offerRowTex != null)
            GUI.DrawTexture(rect, _offerRowTex, ScaleMode.StretchToFill);
        else
            DrawFilledRect(rect, new Color(0.08f, 0.07f, 0.10f, 0.92f));
        GUI.color = prevColor;

        // 라벨 — 비활성 시 머티드 톤.
        var prevLabelColor = _offerRowLabelStyle.normal.textColor;
        if (!enabled)
        {
            _offerRowLabelStyle.normal.textColor = new Color(0.55f, 0.52f, 0.45f);
            LockHoverState(_offerRowLabelStyle);
        }
        GUI.Label(rect, label, _offerRowLabelStyle);
        if (!enabled)
        {
            _offerRowLabelStyle.normal.textColor = prevLabelColor;
            LockHoverState(_offerRowLabelStyle);
        }

        GUI.matrix = prevMatrix;

        if (enabled && GUI.Button(rect, GUIContent.none, GUIStyle.none))
            onClick?.Invoke();
    }

    private void DrawButtonChrome(Rect rect, bool hovered)
    {
        // 둥근 모서리: 같은 (w,h,r,t)면 마스크 캐시 재사용. 픽셀 단위로 한번 굽고 GUI.color로 색만 갈아끼움.
        int w = Mathf.Max(2, Mathf.RoundToInt(rect.width));
        int h = Mathf.Max(2, Mathf.RoundToInt(rect.height));
        float r = Mathf.Min(buttonCornerRadius, Mathf.Min(w, h) * 0.5f);
        float t = Mathf.Max(1f, buttonBorderThickness);

        if (_btnFillMask == null || _btnBorderMask == null
            || _cachedBtnW != w || _cachedBtnH != h
            || !Mathf.Approximately(_cachedBtnRadius, r)
            || !Mathf.Approximately(_cachedBtnBorderT, t))
        {
            BuildButtonMasks(w, h, r, t);
        }

        var prev = GUI.color;

        // 1) Fill
        GUI.color = hovered ? buttonHoverFillColor : buttonFillColor;
        GUI.DrawTexture(rect, _btnFillMask, ScaleMode.StretchToFill);

        // 2) Border
        GUI.color = hovered ? buttonHoverBorderColor : buttonBorderColor;
        GUI.DrawTexture(rect, _btnBorderMask, ScaleMode.StretchToFill);

        GUI.color = prev;
    }

    // 둥근 모서리 fill / border 마스크를 픽셀 단위로 굽는다. radius 안쪽은 SDF 기반 부드러운 1px AA.
    private void BuildButtonMasks(int w, int h, float r, float t)
    {
        if (_btnFillMask != null) Destroy(_btnFillMask);
        if (_btnBorderMask != null) Destroy(_btnBorderMask);

        _btnFillMask   = new Texture2D(w, h, TextureFormat.Alpha8, false) { wrapMode = TextureWrapMode.Clamp };
        _btnBorderMask = new Texture2D(w, h, TextureFormat.Alpha8, false) { wrapMode = TextureWrapMode.Clamp };

        var fillPixels   = new Color[w * h];
        var borderPixels = new Color[w * h];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                // 라운드 사각형 SDF — 중앙에서의 부호 거리. 음수면 안쪽, 양수면 바깥.
                float dx = Mathf.Max(Mathf.Abs(x + 0.5f - w * 0.5f) - (w * 0.5f - r), 0f);
                float dy = Mathf.Max(Mathf.Abs(y + 0.5f - h * 0.5f) - (h * 0.5f - r), 0f);
                float outsideDist = Mathf.Sqrt(dx * dx + dy * dy) - r;

                // Fill 알파: outsideDist <= 0 이면 1, 살짝 바깥은 부드럽게 0으로.
                float fillAlpha = Mathf.Clamp01(0.5f - outsideDist);
                fillPixels[y * w + x] = new Color(0f, 0f, 0f, fillAlpha);

                // Border 알파: 안쪽 두께 t 영역만 1. (-(t) ~ 0 사이가 stroke).
                float borderAlpha = 0f;
                if (outsideDist > -t - 0.5f && outsideDist < 0.5f)
                {
                    // 부드러운 양 끝
                    float aOuter = Mathf.Clamp01(0.5f - outsideDist);
                    float aInner = Mathf.Clamp01(0.5f + (outsideDist + t));
                    borderAlpha = Mathf.Min(aOuter, aInner);
                }
                borderPixels[y * w + x] = new Color(0f, 0f, 0f, borderAlpha);
            }
        }

        _btnFillMask.SetPixels(fillPixels);
        _btnFillMask.Apply();
        _btnBorderMask.SetPixels(borderPixels);
        _btnBorderMask.Apply();

        _cachedBtnW = w;
        _cachedBtnH = h;
        _cachedBtnRadius = r;
        _cachedBtnBorderT = t;
    }

    private static void DrawFilledRect(Rect rect, Color color)
    {
        var prev = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill);
        GUI.color = prev;
    }
}
