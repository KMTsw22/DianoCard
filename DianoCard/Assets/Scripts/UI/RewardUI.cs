using System;
using System.Collections.Generic;
using DianoCard.Data;
using DianoCard.Game;
using UnityEngine;

/// <summary>
/// 전투 승리 후 보상 화면.
/// GameState == Reward 일 때만 그려짐.
///
/// 동작:
/// - 보상 목록이 한 패널에 세로로 나열됨 (골드 → 카드 → 물약 → 유물 순)
/// - "계속하기" 버튼을 누를 때마다 맨 위 항목이 하나씩 소거됨
/// - 카드 항목 차례가 되면 카드 3장 선택 서브뷰로 전환 (선택 또는 스킵 후 리스트로 복귀)
/// - 모든 항목이 소거되면 ProceedAfterReward() 호출
///
/// DefaultExecutionOrder(1000) — BattleUI 등 다른 MonoBehaviour보다 OnGUI가 늦게 돌아서
/// 패널/보상 UI가 전투 IMGUI 드로잉 위로 올라오도록 고정.
/// </summary>
[DefaultExecutionOrder(1000)]
public class RewardUI : MonoBehaviour
{
    private const float RefW = 1280f;
    private const float RefH = 720f;

    // =========================================================
    // 인스펙터 튜닝 값 (플레이모드에서도 실시간 조정 가능)
    // =========================================================

    [Header("Panel")]
    [SerializeField] private Vector2 panelSize = new(440, 580);
    [SerializeField] private float panelYOffset = -10f;
    [SerializeField] private float titleYOffset = 22f;
    [SerializeField] private Vector2 titleImageSize = new(140, 42);

    [Header("Panel Backdrop")]
    [Tooltip("전체 화면 어둡게 덮는 오버레이 알파")]
    [SerializeField, Range(0f, 1f)] private float backdropAlpha = 0.42f;
    [Tooltip("패널 뒤쪽 radial glow 색상")]
    [SerializeField] private Color panelGlowColor = new(1f, 0.78f, 0.35f);
    [Tooltip("패널 뒤쪽 glow 알파")]
    [SerializeField, Range(0f, 1f)] private float panelGlowAlpha = 0.75f;
    [Tooltip("패널 뒤쪽 glow 크기 = 패널 크기 × 이 값 (크게 잡아야 패널 바깥으로 퍼져 보임)")]
    [SerializeField, Range(1.0f, 3.5f)] private float panelGlowSizeFactor = 2.2f;

    [Header("Rows")]
    [SerializeField] private Vector2 rowSize = new(320, 60);
    [SerializeField] private float rowsStartYOffset = 145f;
    [SerializeField] private float rowGap = 25f;
    [SerializeField] private int rowLabelFontSize = 15;
    [SerializeField] private Color rowLabelColor = new(0.99f, 0.95f, 0.78f);

    [Header("Row Medallion (원형 프레임)")]
    [Tooltip("메달리온 프레임 크기 = 행 높이 × 이 값 (행보다 조금 크게 튀어나오도록)")]
    [SerializeField, Range(0.8f, 2.2f)] private float medallionSizeFactor = 1.3f;
    [Tooltip("메달리온 중심 X 위치 = 행 왼쪽 + 행 높이 × 이 값")]
    [SerializeField, Range(-0.5f, 2f)] private float medallionCenterXFactor = 0.5f;

    [Header("Row Icon (메달리온 안쪽)")]
    [Tooltip("아이콘 크기 = 메달리온 크기 × 이 값 (프레임 안쪽에 쏙 들어가도록 작게)")]
    [SerializeField, Range(0.3f, 1.0f)] private float iconSizeFactor = 0.50f;
    [Tooltip("라벨 시작 X = 행 왼쪽 + 행 높이 × 이 값")]
    [SerializeField, Range(0.5f, 2.5f)] private float labelStartXFactor = 1.30f;

    [Header("Row Icon Animation & Glow")]
    [Tooltip("아이콘 둥실둥실 진폭 (픽셀)")]
    [SerializeField, Range(0f, 6f)] private float iconBobAmplitude = 0.6f;
    [Tooltip("아이콘 둥실둥실 주기 (Hz)")]
    [SerializeField, Range(0.2f, 3f)] private float iconBobFrequency = 1.2f;
    [Tooltip("글로우 크기 = 메달리온 크기 × 이 값")]
    [SerializeField, Range(0.5f, 1.5f)] private float glowSizeFactor = 0.95f;
    [Tooltip("글로우 알파 강도")]
    [SerializeField, Range(0f, 1f)] private float glowAlpha = 0.55f;

    [Header("Continue Button")]
    [SerializeField] private Vector2 continueButtonSize = new(400, 84);
    [SerializeField] private float continueButtonBottomMargin = 30f;
    [Tooltip("Continue 버튼 위에 마우스 올렸을 때 확대 배율")]
    [SerializeField, Range(1f, 1.3f)] private float continueHoverScale = 1.08f;

    [Header("Card Picker")]
    [SerializeField] private Vector2 cardPickerCardSize = new(230, 320);
    [SerializeField] private float cardPickerSpacing = 36f;
    [SerializeField] private float cardPickerStartY = 200f;
    [SerializeField] private Vector2 cardPickerSkipSize = new(220, 62);
    [Tooltip("카드에 마우스 올렸을 때 확대 배율")]
    [SerializeField, Range(1f, 1.2f)] private float cardHoverScale = 1.05f;
    [Tooltip("카드 피커 전체 Y 오프셋 (양수 = 아래로)")]
    [SerializeField] private float cardPickerYOffset = 0f;
    [Tooltip("카드 프레임(브론즈+리본) 알파 — 낮출수록 리본에 아트가 비침")]
    [SerializeField, Range(0.5f, 1f)] private float cardFrameAlpha = 0.88f;
    [Tooltip("Panel 인셋 (Left, Top, Right, Bottom) — Frame 안쪽 여백에 딱 맞추기")]
    [SerializeField] private Vector4 cardPanelInset = new(0.07f, 0.04f, 0.048f, 0.04f);
    [Tooltip("카드 아트 영역 (xPct, yPct, widthPct, heightPct)")]
    [SerializeField] private Vector4 cardArtRectPct = new(0.06f, 0.045f, 0.88f, 0.63f);
    [Tooltip("CardArtFrame 오버레이 (골드 사각 + 육각 배너)가 카드 rect 안에서 차지하는 영역. (x, y, w, h) 비율.")]
    [SerializeField] private Vector4 cardArtFrameRectPct = new(0.15f, 0.63f, 0.7f, 0.09f);
    [Tooltip("CardDescPanel (하단 설명 박스)이 카드 rect 안에서 차지하는 영역.")]
    [SerializeField] private Vector4 cardDescPanelRectPct = new(0.07f, 0.69f, 0.86f, 0.24f);
    [Tooltip("CardBorder (외곽 테두리)가 카드 rect 안에서 차지하는 영역. 기본은 전체.")]
    [SerializeField] private Vector4 cardBorderRectPct = new(0f, 0f, 1f, 1f);

    [Header("Card Picker — Title Area")]
    [Tooltip("타이틀/부제 전체에 더해지는 베이스 y 오프셋")]
    [SerializeField] private float cardPickerBaseYOffset = 20f;
    [SerializeField] private Vector2 cpTitleImageSize = new(380f, 70f);
    [SerializeField] private float cpTitleImageY = 28f;
    [SerializeField] private Vector2 cpTitleGlowSize = new(720f, 220f);
    [SerializeField] private Vector2 cpTitleDividerSize = new(280f, 24f);
    [SerializeField] private float cpTitleDividerY = 98f;
    [SerializeField] private float cpSubtitleY = 128f;
    [Tooltip("카드 하단 ~ Skip 버튼 간격")]
    [SerializeField] private float skipButtonTopMargin = 36f;

    [Header("Card Picker — Inner Layout (rect 비율)")]
    [Tooltip("희귀도 rect (xPct, yPct, widthPct, heightPct)")]
    [UnityEngine.Serialization.FormerlySerializedAs("cardMetaRectPct")]
    [SerializeField] private Vector4 cardRarityRectPct = new(0.06f, 0.06f, 0.88f, 0.07f);
    [Tooltip("카드 종류(Herbivore/Spell/Buff 등) rect (xPct, yPct, widthPct, heightPct)")]
    [SerializeField] private Vector4 cardTypeRectPct = new(0.06f, 0.72f, 0.87f, 0.07f);
    [Tooltip("본문 스탯/설명 rect (xPct, yPct, widthPct, heightPct)")]
    [SerializeField] private Vector4 cardBodyRectPct = new(0.15f, 0.78f, 0.7f, 0.15f);
    [Tooltip("골드 디바이더 rect (xPct, yPct, widthPct, 절대높이 px)")]
    [SerializeField] private Vector4 cardDividerRectPct = new(0.07f, 0.672f, 0.87f, 1.3f);
    [SerializeField] private Color cardDividerColor = new(0.75f, 0.58f, 0.25f, 0.75f);
    [Tooltip("카드 종류 라벨 색 (희귀도와 구분)")]
    [SerializeField] private Color cardTypeColor = new(0.55f, 0.85f, 0.55f);

    [Header("Card Picker — Font Sizes")]
    [SerializeField, Range(8, 28)] private int cardNameFontSize = 14;
    [UnityEngine.Serialization.FormerlySerializedAs("cardMetaFontSize")]
    [SerializeField, Range(8, 24)] private int cardRarityFontSize = 16;
    [SerializeField, Range(8, 24)] private int cardTypeFontSize = 15;
    [SerializeField, Range(8, 24)] private int cardBodyFontSize = 12;
    [SerializeField, Range(12, 36)] private int skipButtonFontSize = 24;

    [Header("Card Picker — Card Glow")]
    [SerializeField] private float cardGlowPadNormal = 42f;
    [SerializeField] private float cardGlowPadHover = 60f;
    [SerializeField, Range(0f, 1f)] private float cardGlowAlphaNormal = 0.38f;
    [SerializeField, Range(0f, 1f)] private float cardGlowAlphaHover = 0.65f;
    [SerializeField] private Color cardGlowColor = new(1f, 0.82f, 0.42f);

    [Header("Card Picker — Ribbon")]
    [Tooltip("리본 중심 Y 위치 (rect.height 비율) — 0 = 카드 맨 위")]
    [SerializeField, Range(-0.1f, 0.3f)] private float cardRibbonYPct = -0.041f;
    [Tooltip("리본 폭 (rect.width 비율)")]
    [SerializeField, Range(0.5f, 3f)] private float cardRibbonWidthPct = 3f;
    [Tooltip("리본 높이 (rect.height 비율)")]
    [SerializeField, Range(0.05f, 0.3f)] private float cardRibbonHeightPct = 0.235f;
    [Tooltip("리본 안 이름 텍스트 인셋 (xPct, yPct, widthPct, heightPct) — 리본 rect 기준")]
    [SerializeField] private Vector4 cardNameInRibbonPct = new(0.08f, 0.15f, 0.84f, 1f);

    [Header("Card Picker — Cost Bubble")]
    [Tooltip("코스트 버블 중심 위치 (rect 기준 xPct, yPct)")]
    [SerializeField] private Vector2 cardCostBubbleCenter = new(0.03f, 0.04f);
    [Tooltip("코스트 버블 크기 — rect.width × 이 값")]
    [SerializeField, Range(0.1f, 0.4f)] private float cardCostBubbleSize = 0.24f;
    [Tooltip("버블 안 숫자 폰트 배율 — 버블 크기 × 이 값")]
    [SerializeField, Range(0.3f, 0.9f)] private float cardCostBubbleFontScale = 0.5f;

    private enum View { List, CardPicker }
    private enum RowKind { Gold, Card, Potion, Relic }

    private View _view = View.List;

    public void Cheat_JumpToCardPicker()
    {
        _goldDone = true;
        _potionDone = true;
        _relicDone = true;
        _cardDone = false;
        _view = View.CardPicker;
    }

    private bool _goldDone;
    private bool _cardDone;
    private bool _potionDone;
    private bool _relicDone;

    private GameState _prevState = GameState.Lobby;
    private readonly List<Action> _pending = new();

    // Sprites (SPOILS list view)
    private Texture2D _panelTex;
    private Texture2D _rowTex;
    private Texture2D _medallionTex;
    private Texture2D _continueTex;
    private Texture2D _textSpoilsTex;
    private Texture2D _glowTex;
    private Texture2D _iconGold;
    private Texture2D _iconCard;
    private Texture2D _iconPotion;
    private Texture2D _iconRelic;

    // Sprites (Card picker view) — 전투 UI와 동일한 CardSlot/* 5레이어 스택 사용
    private Texture2D _cardPanelTex;      // CardSlot/CardBg — 배경 판
    private Texture2D _cardArtFrameTex;   // CardSlot/CardArtFrame — 아트 테두리 + 육각 타입 배너
    private Texture2D _cardDescPanelTex;  // CardSlot/CardDescPanel — 하단 설명 박스
    private Texture2D _cardFrameTex;      // CardSlot/CardBorder — 외곽 테두리
    private Texture2D _skipButtonTex;
    private Texture2D _textChooseCardTex;
    private Texture2D _titleDividerTex;
    private Texture2D _manaFrameTex;      // CardSlot/ManaFrame — 코스트 버블

    // CardArt 캐시 — BattleUI와 동일 경로 규칙 (Resources/CardArt/{Summon|Spell|Utility}/{filename})
    private readonly Dictionary<string, Texture2D> _cardArtCache = new();

    // Fonts
    private Font _displayFont;

    // Styles
    private GUIStyle _rowLabelStyle;
    private GUIStyle _pickerTitleStyle;
    private GUIStyle _pickerSubStyle;
    private GUIStyle _cardNameStyle;
    private GUIStyle _cardMetaStyle;
    private GUIStyle _cardBodyStyle;
    private GUIStyle _skipButtonStyle;
    private GUIStyle _cardCostStyle;
    private bool _stylesReady;

    void Update()
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null) return;

        // 치트: F9 — 언제든 Reward 화면 강제 진입 (전리품 리스트부터)
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.f9Key.wasPressedThisFrame)
        {
            gsm.Cheat_TriggerReward();
        }

        // 치트: F10 — 카드 피커 뷰로 바로 점프 (카드 UI 이터레이션용)
        bool jumpToCardPicker = false;
        if (kb != null && kb.f10Key.wasPressedThisFrame)
        {
            gsm.Cheat_TriggerReward();
            jumpToCardPicker = true;
        }

        if (_prevState != GameState.Reward && gsm.State == GameState.Reward)
        {
            ResetForNewReward();
        }
        _prevState = gsm.State;

        // F10 치트는 ResetForNewReward 이후에 플래그를 덮어써서 다른 보상은 모두 완료 처리하고
        // 카드 피커 뷰로 즉시 진입
        if (jumpToCardPicker)
        {
            _goldDone = true;
            _potionDone = true;
            _relicDone = true;
            _cardDone = false;
            _view = View.CardPicker;
        }

        // 이전 프레임 OnGUI에서 쌓인 pending 액션 먼저 실행 (상태 최신화)
        if (_pending.Count > 0)
        {
            var snapshot = new List<Action>(_pending);
            _pending.Clear();
            foreach (var a in snapshot) a?.Invoke();
        }

        // pending이 반영된 뒤에 체크 — 카드 피커에서 마지막 카드 고르고 복귀한 경우도 같은 프레임에 즉시 닫힘
        if (gsm.State == GameState.Reward && _view == View.List)
        {
            var r = gsm.CurrentRun?.pendingReward;
            if (r != null && IsAllRowsDone(r))
            {
                gsm.ProceedAfterReward();
            }
        }
    }

    private void ResetForNewReward()
    {
        _view = View.List;
        _goldDone = false;
        _cardDone = false;
        _potionDone = false;
        _relicDone = false;
    }

    void OnGUI()
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null || gsm.State != GameState.Reward) return;

        var run = gsm.CurrentRun;
        var reward = run?.pendingReward;
        if (reward == null) return;

        // GUI.depth: 낮을수록 앞. BattleUI(10)보다 낮게 해서 보상 패널이 공룡/전장 위로 올라오도록
        GUI.depth = 0;

        EnsureStyles();

        float scale = Mathf.Min(Screen.width / RefW, Screen.height / RefH);
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));

        // 전체 살짝 어두운 반투명 오버레이 (뒤 씬이 살짝만 비쳐 보이도록)
        var prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, backdropAlpha);
        GUI.DrawTexture(new Rect(0, 0, RefW, RefH), Texture2D.whiteTexture);
        GUI.color = prev;

        if (_view == View.List)
            DrawListView(gsm, run, reward);
        else
            DrawCardPicker(gsm, reward);
    }

    // =========================================================
    // 리스트 뷰
    // =========================================================

    private void DrawListView(GameStateManager gsm, RunState run, BattleReward reward)
    {
        // 인스펙터 값이 런타임에 바뀔 수 있으므로 매번 스타일 폰트 크기 동기화
        SyncStyleFontSizes();

        // 패널 스프라이트 (상단 리본 배너 포함)
        var panelRect = new Rect(
            (RefW - panelSize.x) / 2f,
            (RefH - panelSize.y) / 2f + panelYOffset,
            panelSize.x,
            panelSize.y);

        // 패널 뒤쪽 warm glow
        if (_glowTex != null && panelGlowAlpha > 0f)
        {
            float gw = panelRect.width * panelGlowSizeFactor;
            float gh = panelRect.height * panelGlowSizeFactor;
            var glowRect = new Rect(
                panelRect.center.x - gw * 0.5f,
                panelRect.center.y - gh * 0.5f,
                gw,
                gh);
            var prevGuiColor = GUI.color;
            var gc = panelGlowColor;
            gc.a = panelGlowAlpha;
            GUI.color = gc;
            GUI.DrawTexture(glowRect, _glowTex, ScaleMode.StretchToFill);
            GUI.color = prevGuiColor;
        }

        if (_panelTex != null)
            GUI.DrawTexture(panelRect, _panelTex, ScaleMode.StretchToFill);
        else
            DrawFilledRect(panelRect, new Color(0.10f, 0.14f, 0.20f, 0.96f));

        // 타이틀 "SPOILS!" — 리본 배너 내부 (텍스처 이미지)
        if (_textSpoilsTex != null)
        {
            var titleRect = new Rect(
                panelRect.x + (panelRect.width - titleImageSize.x) / 2f,
                panelRect.y + titleYOffset,
                titleImageSize.x,
                titleImageSize.y);
            GUI.DrawTexture(titleRect, _textSpoilsTex, ScaleMode.ScaleToFit);
        }

        // 보상 행
        float rowW = rowSize.x;
        float rowH = rowSize.y;
        float rowX = panelRect.x + (panelRect.width - rowW) / 2f;
        float y = panelRect.y + rowsStartYOffset;

        var dm = DataManager.Instance;
        if (!_goldDone && reward.gold > 0)
        {
            DrawRewardRow(new Rect(rowX, y, rowW, rowH), _iconGold, dm.GetUIString("reward.row.gold", reward.gold), RowKind.Gold);
            y += rowH + rowGap;
        }
        if (!_cardDone && reward.cardChoices != null && reward.cardChoices.Count > 0)
        {
            DrawRewardRow(new Rect(rowX, y, rowW, rowH), _iconCard, dm.GetUIString("reward.row.card"), RowKind.Card);
            y += rowH + rowGap;
        }
        if (!_potionDone && reward.potion != null)
        {
            string pLabel = run.PotionSlotFull
                ? dm.GetUIString("reward.row.potion_full")
                : dm.GetUIString("reward.row.potion", EnName(reward.potion.nameEn, reward.potion.nameKr));
            DrawRewardRow(new Rect(rowX, y, rowW, rowH), _iconPotion, pLabel, RowKind.Potion);
            y += rowH + rowGap;
        }
        if (!_relicDone && reward.relic != null)
        {
            DrawRewardRow(new Rect(rowX, y, rowW, rowH), _iconRelic, dm.GetUIString("reward.row.relic", EnName(reward.relic.nameEn, reward.relic.nameKr)), RowKind.Relic);
            y += rowH + rowGap;
        }

        // 계속하기 버튼 — hover 시 살짝 확대
        float btnW = continueButtonSize.x;
        float btnH = continueButtonSize.y;
        var btnRect = new Rect((RefW - btnW) / 2f, panelRect.yMax - btnH - continueButtonBottomMargin, btnW, btnH);

        bool hovered = btnRect.Contains(Event.current.mousePosition);
        Rect drawRect = btnRect;
        if (hovered)
        {
            float s = continueHoverScale;
            drawRect = new Rect(
                btnRect.center.x - btnRect.width * s * 0.5f,
                btnRect.center.y - btnRect.height * s * 0.5f,
                btnRect.width * s,
                btnRect.height * s);
        }

        if (_continueTex != null)
            GUI.DrawTexture(drawRect, _continueTex, ScaleMode.ScaleToFit);
        if (GUI.Button(btnRect, GUIContent.none, GUIStyle.none))
        {
            _pending.Add(() => OnContinuePressed(gsm, run, reward));
        }
    }

    private void DrawRewardRow(Rect rect, Texture2D icon, string label, RowKind kind)
    {
        // 1. 행 박스 배경
        if (_rowTex != null)
            GUI.DrawTexture(rect, _rowTex, ScaleMode.StretchToFill);
        else
            DrawFilledRect(rect, new Color(0.18f, 0.30f, 0.40f, 1f));

        // 공통: 메달리온 중심 좌표
        float medallionCenterX = rect.x + rect.height * medallionCenterXFactor;
        float medallionCenterY = rect.y + rect.height * 0.5f;

        // 2. 메달리온 프레임
        float medallionSize = rect.height * medallionSizeFactor;
        var medallionRect = new Rect(
            medallionCenterX - medallionSize * 0.5f,
            medallionCenterY - medallionSize * 0.5f,
            medallionSize,
            medallionSize);
        if (_medallionTex != null)
            GUI.DrawTexture(medallionRect, _medallionTex, ScaleMode.ScaleToFit);

        // 3. 둥실둥실 bob 오프셋 (타입별 위상차)
        float bobOffset = Mathf.Sin(Time.time * Mathf.PI * 2f * iconBobFrequency + BobPhaseFor(kind)) * iconBobAmplitude;

        // 4. 글로우 (아이콘 타입별 색상, 메달리온 내부에)
        if (_glowTex != null)
        {
            float glowSize = medallionSize * glowSizeFactor;
            var glowRect = new Rect(
                medallionCenterX - glowSize * 0.5f,
                medallionCenterY - glowSize * 0.5f + bobOffset,
                glowSize,
                glowSize);
            var glowColor = GlowColorFor(kind);
            glowColor.a = glowAlpha;
            var prevColor = GUI.color;
            GUI.color = glowColor;
            GUI.DrawTexture(glowRect, _glowTex, ScaleMode.StretchToFill);
            GUI.color = prevColor;
        }

        // 5. 아이콘 (글로우 위에, bob 적용)
        float iconSize = medallionSize * iconSizeFactor;
        var iconRect = new Rect(
            medallionCenterX - iconSize * 0.5f,
            medallionCenterY - iconSize * 0.5f + bobOffset,
            iconSize,
            iconSize);
        if (icon != null)
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);

        // 6. 라벨
        var labelRect = new Rect(
            rect.x + rect.height * labelStartXFactor,
            rect.y,
            rect.width - rect.height * labelStartXFactor - 12,
            rect.height);
        GUI.Label(labelRect, label, _rowLabelStyle);
    }

    private void OnContinuePressed(GameStateManager gsm, RunState run, BattleReward reward)
    {
        if (!_goldDone && reward.gold > 0)
        {
            _goldDone = true;
        }
        else if (!_cardDone && reward.cardChoices != null && reward.cardChoices.Count > 0)
        {
            _view = View.CardPicker;
            return;
        }
        else if (!_potionDone && reward.potion != null)
        {
            if (!run.PotionSlotFull) gsm.TakePotionReward(reward.potion);
            _potionDone = true;
        }
        else if (!_relicDone && reward.relic != null)
        {
            gsm.TakeRelicReward(reward.relic);
            _relicDone = true;
        }

        // 처리 후 남은 게 없으면 즉시 다음 단계로 (한 번 더 Continue 안 눌러도 됨)
        if (_view == View.List && IsAllRowsDone(reward))
        {
            gsm.ProceedAfterReward();
        }
    }

    private bool IsAllRowsDone(BattleReward reward)
    {
        if (!_goldDone && reward.gold > 0) return false;
        if (!_cardDone && reward.cardChoices != null && reward.cardChoices.Count > 0) return false;
        if (!_potionDone && reward.potion != null) return false;
        if (!_relicDone && reward.relic != null) return false;
        return true;
    }

    // =========================================================
    // 카드 선택 서브뷰
    // =========================================================

    private void DrawCardPicker(GameStateManager gsm, BattleReward reward)
    {
        SyncStyleFontSizes();

        float yOff = cardPickerYOffset + cardPickerBaseYOffset;

        // 타이틀 뒤쪽 warm glow
        if (_glowTex != null)
        {
            float gw = cpTitleGlowSize.x, gh = cpTitleGlowSize.y;
            var glowRect = new Rect((RefW - gw) / 2f, cpTitleImageY + cpTitleImageSize.y * 0.5f - gh * 0.5f + yOff, gw, gh);
            var prevGuiColor = GUI.color;
            var gc = panelGlowColor;
            gc.a = panelGlowAlpha * 0.85f;
            GUI.color = gc;
            GUI.DrawTexture(glowRect, _glowTex, ScaleMode.StretchToFill);
            GUI.color = prevGuiColor;
        }

        // 타이틀 이미지 (CHOOSE A CARD) — 텍스처 없으면 폰트 폴백
        if (_textChooseCardTex != null)
        {
            var titleRect = new Rect((RefW - cpTitleImageSize.x) / 2f, cpTitleImageY + yOff, cpTitleImageSize.x, cpTitleImageSize.y);
            GUI.DrawTexture(titleRect, _textChooseCardTex, ScaleMode.ScaleToFit);
        }
        else
        {
            GUI.Label(new Rect(0, cpTitleImageY + yOff, RefW, cpTitleImageSize.y), DataManager.Instance.GetUIString("reward.title"), _pickerTitleStyle);
        }

        // 장식 구분선
        if (_titleDividerTex != null)
        {
            var dividerRect = new Rect((RefW - cpTitleDividerSize.x) / 2f, cpTitleDividerY + yOff, cpTitleDividerSize.x, cpTitleDividerSize.y);
            GUI.DrawTexture(dividerRect, _titleDividerTex, ScaleMode.ScaleToFit);
        }

        GUI.Label(new Rect(0, cpSubtitleY + yOff, RefW, 24f), DataManager.Instance.GetUIString("reward.subtitle"), _pickerSubStyle);

        int n = reward.cardChoices.Count;
        if (n == 0)
        {
            _pending.Add(() => { _cardDone = true; _view = View.List; });
            return;
        }

        float cardW = cardPickerCardSize.x;
        float cardH = cardPickerCardSize.y;
        float spacing = cardPickerSpacing;
        float totalW = n * cardW + (n - 1) * spacing;
        float startX = (RefW - totalW) / 2f;
        float startY = cardPickerStartY + yOff;

        int hoveredIdx = -1;
        for (int i = 0; i < n; i++)
        {
            var r = new Rect(startX + i * (cardW + spacing), startY, cardW, cardH);
            if (r.Contains(Event.current.mousePosition)) { hoveredIdx = i; break; }
        }

        for (int i = 0; i < n; i++)
        {
            var card = reward.cardChoices[i];
            var rect = new Rect(startX + i * (cardW + spacing), startY, cardW, cardH);

            bool hover = i == hoveredIdx;
            Rect drawRect = rect;
            if (hover)
            {
                float s = cardHoverScale;
                drawRect = new Rect(
                    rect.center.x - rect.width * s * 0.5f,
                    rect.center.y - rect.height * s * 0.5f,
                    rect.width * s,
                    rect.height * s);
            }

            DrawCardChoice(drawRect, card, hover);

            if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
            {
                int captured = i;
                _pending.Add(() =>
                {
                    GameStateManager.Instance?.TakeCardReward(reward.cardChoices[captured]);
                    _cardDone = true;
                    _view = View.List;
                });
            }
        }

        // 스킵 버튼 — 크림 파치먼트 스크롤 스프라이트 위에 다크 브라운 라벨 얹기
        float skipW = cardPickerSkipSize.x;
        float skipH = cardPickerSkipSize.y;
        var skipRect = new Rect((RefW - skipW) / 2f, startY + cardH + skipButtonTopMargin, skipW, skipH);
        bool skipHover = skipRect.Contains(Event.current.mousePosition);
        Rect skipDraw = skipRect;
        if (skipHover)
        {
            float s = continueHoverScale;
            skipDraw = new Rect(
                skipRect.center.x - skipRect.width * s * 0.5f,
                skipRect.center.y - skipRect.height * s * 0.5f,
                skipRect.width * s,
                skipRect.height * s);
        }
        if (_skipButtonTex != null)
            GUI.DrawTexture(skipDraw, _skipButtonTex, ScaleMode.ScaleToFit);
        else
            DrawFilledRect(skipDraw, new Color(0.10f, 0.14f, 0.20f, 0.96f));
        GUI.Label(skipDraw, DataManager.Instance.GetUIString("reward.skip"), _skipButtonStyle);
        if (GUI.Button(skipRect, GUIContent.none, GUIStyle.none))
        {
            _pending.Add(() => { _cardDone = true; _view = View.List; });
        }
    }

    private void DrawCardChoice(Rect rect, CardData card, bool hover)
    {
        // [Layer 1] 카드 뒤쪽 warm glow
        if (_glowTex != null)
        {
            float pad = hover ? cardGlowPadHover : cardGlowPadNormal;
            var glowRect = new Rect(rect.x - pad, rect.y - pad, rect.width + pad * 2, rect.height + pad * 2);
            var prevGuiColor = GUI.color;
            GUI.color = new Color(cardGlowColor.r, cardGlowColor.g, cardGlowColor.b, hover ? cardGlowAlphaHover : cardGlowAlphaNormal);
            GUI.DrawTexture(glowRect, _glowTex, ScaleMode.StretchToFill);
            GUI.color = prevGuiColor;
        }

        // [Layer 2] Panel — 네이비 파치먼트 배경 (Frame 인테리어에 맞춰 인셋)
        var panelRect = new Rect(
            rect.x + rect.width * cardPanelInset.x,
            rect.y + rect.height * cardPanelInset.y,
            rect.width * (1f - cardPanelInset.x - cardPanelInset.z),
            rect.height * (1f - cardPanelInset.y - cardPanelInset.w));
        if (_cardPanelTex != null)
            GUI.DrawTexture(panelRect, _cardPanelTex, ScaleMode.StretchToFill);
        else
            DrawFilledRect(panelRect, new Color(0.10f, 0.14f, 0.20f, 0.96f));

        // [Layer 3] 카드 아트 — Panel 위, Frame 아래. 리본 위치까지 덮도록 y 상단 확장
        var artRect = new Rect(
            rect.x + rect.width * cardArtRectPct.x,
            rect.y + rect.height * cardArtRectPct.y,
            rect.width * cardArtRectPct.z,
            rect.height * cardArtRectPct.w);
        var art = GetCardArt(card);
        if (art != null)
            GUI.DrawTexture(artRect, art, ScaleMode.ScaleToFit);

        // [Layer 3.3] 골드 디바이더 — 좌·우 두 조각으로 나눠 그려서 hex 배너 영역은
        //              아예 그리지 않음. (ArtFrame이 반투명이라 선이 비치는 문제 방지)
        {
            float divL = rect.x + rect.width * cardDividerRectPct.x;
            float divR = divL + rect.width * cardDividerRectPct.z;
            float hexL = rect.x + rect.width * cardArtFrameRectPct.x;
            float hexR = hexL + rect.width * cardArtFrameRectPct.z;
            float divY = rect.y + rect.height * cardDividerRectPct.y;
            float divH = cardDividerRectPct.w;

            // 좌측 조각 (디바이더 시작 ~ hex 시작)
            if (divL < hexL)
                DrawFilledRect(new Rect(divL, divY, hexL - divL, divH), cardDividerColor);
            // 우측 조각 (hex 끝 ~ 디바이더 끝)
            if (divR > hexR)
                DrawFilledRect(new Rect(hexR, divY, divR - hexR, divH), cardDividerColor);
        }

        // [Layer 3.5] ArtFrame — 골드 사각 테두리 + 육각 타입 배너 (아트 위에 오버레이)
        var artFrameRect = new Rect(
            rect.x + rect.width  * cardArtFrameRectPct.x,
            rect.y + rect.height * cardArtFrameRectPct.y,
            rect.width  * cardArtFrameRectPct.z,
            rect.height * cardArtFrameRectPct.w);
        if (_cardArtFrameTex != null)
        {
            // hex 배너(카드 이름 패널)는 완전 불투명 — 뒤가 비치면 안 됨
            GUI.DrawTexture(artFrameRect, _cardArtFrameTex, ScaleMode.StretchToFill);
        }

        // [Layer 4] Frame — 외곽 테두리 (가운데 투명)
        var borderRect = new Rect(
            rect.x + rect.width  * cardBorderRectPct.x,
            rect.y + rect.height * cardBorderRectPct.y,
            rect.width  * cardBorderRectPct.z,
            rect.height * cardBorderRectPct.w);
        if (_cardFrameTex != null)
        {
            var prevColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, cardFrameAlpha);
            GUI.DrawTexture(borderRect, _cardFrameTex, ScaleMode.StretchToFill);
            GUI.color = prevColor;
        }

        // 이름 라벨 위치를 육각 배너 위에 오도록 가상 rect 계산 (기존 ribbonRect 자리에 대응)
        float ribbonW = rect.width * 0.46f;
        float ribbonH = rect.height * 0.07f;
        float ribbonX = rect.x + (rect.width - ribbonW) * 0.5f;
        float ribbonY = rect.y + rect.height * 0.63f;
        var ribbonRect = new Rect(ribbonX, ribbonY, ribbonW, ribbonH);

        // [Layer 4.5] Cost Bubble — 인게임 마나 오브 재사용
        if (_manaFrameTex != null)
        {
            float orbSize = rect.width * cardCostBubbleSize;
            float orbCx = rect.x + rect.width * cardCostBubbleCenter.x;
            float orbCy = rect.y + rect.height * cardCostBubbleCenter.y;
            var orbRect = new Rect(orbCx - orbSize * 0.5f, orbCy - orbSize * 0.5f, orbSize, orbSize);

            var prevC = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(orbRect, _manaFrameTex, ScaleMode.StretchToFill, true);
            GUI.color = prevC;

            int prevFontSize = _cardCostStyle.fontSize;
            _cardCostStyle.fontSize = Mathf.RoundToInt(orbSize * cardCostBubbleFontScale);
            DrawOutlinedLabel(orbRect, card.cost.ToString(), _cardCostStyle,
                              Color.white, new Color(0f, 0f, 0f, 0.95f), 1.2f);
            _cardCostStyle.fontSize = prevFontSize;
        }

        // [Layer 6] 텍스트 — 이름은 리본 rect 위에 얹힘, 나머지는 비율 기반
        var nameRect = new Rect(
            ribbonRect.x + ribbonRect.width * cardNameInRibbonPct.x,
            ribbonRect.y + ribbonRect.height * cardNameInRibbonPct.y,
            ribbonRect.width * cardNameInRibbonPct.z,
            ribbonRect.height * cardNameInRibbonPct.w);
        GUI.Label(nameRect, EnName(card.nameEn, card.nameKr), _cardNameStyle);

        // 희귀도 (크림 기본 색)
        var rarityRect = new Rect(
            rect.x + rect.width * cardRarityRectPct.x,
            rect.y + rect.height * cardRarityRectPct.y,
            rect.width * cardRarityRectPct.z,
            rect.height * cardRarityRectPct.w);
        int prevMetaSize = _cardMetaStyle.fontSize;
        var prevMetaColor = _cardMetaStyle.normal.textColor;
        _cardMetaStyle.fontSize = cardRarityFontSize;
        GUI.Label(rarityRect, $"[{card.rarity}]", _cardMetaStyle);

        // 카드 종류 (색 분리)
        var typeRect = new Rect(
            rect.x + rect.width * cardTypeRectPct.x,
            rect.y + rect.height * cardTypeRectPct.y,
            rect.width * cardTypeRectPct.z,
            rect.height * cardTypeRectPct.w);
        _cardMetaStyle.fontSize = cardTypeFontSize;
        _cardMetaStyle.normal.textColor = cardTypeColor;
        GUI.Label(typeRect, GetCardTypeLabel(card), _cardMetaStyle);
        _cardMetaStyle.fontSize = prevMetaSize;
        _cardMetaStyle.normal.textColor = prevMetaColor;

        // 본문 스탯/설명
        var bodyRect = new Rect(
            rect.x + rect.width * cardBodyRectPct.x,
            rect.y + rect.height * cardBodyRectPct.y,
            rect.width * cardBodyRectPct.z,
            rect.height * cardBodyRectPct.w);
        GUI.Label(bodyRect, BuildCardBody(card), _cardBodyStyle);
    }

    private Texture2D GetCardArt(CardData card)
    {
        if (card == null || string.IsNullOrEmpty(card.image)) return null;
        if (_cardArtCache.TryGetValue(card.id, out var cached)) return cached;

        string filename = System.IO.Path.GetFileNameWithoutExtension(card.image);
        string subfolder = card.cardType switch
        {
            CardType.SUMMON => "Summon",
            CardType.MAGIC  => "Spell",
            _               => "Utility",
        };
        var tex = Resources.Load<Texture2D>($"CardArt/{subfolder}/{filename}");
        _cardArtCache[card.id] = tex; // null도 캐싱 (재시도 방지)
        return tex;
    }

    // =========================================================
    // 드로잉 유틸
    // =========================================================

    private void DrawFilledRect(Rect rect, Color color)
    {
        var prev = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = prev;
    }

    private static void DrawOutlinedLabel(Rect rect, string text, GUIStyle style,
                                          Color textColor, Color outlineColor, float thickness)
    {
        var prev = GUI.color;
        var prevTextColor = style.normal.textColor;

        style.normal.textColor = outlineColor;
        GUI.color = outlineColor;
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                var r = new Rect(rect.x + dx * thickness, rect.y + dy * thickness, rect.width, rect.height);
                GUI.Label(r, text, style);
            }

        style.normal.textColor = textColor;
        GUI.color = textColor;
        GUI.Label(rect, text, style);

        style.normal.textColor = prevTextColor;
        GUI.color = prev;
    }

    private Texture2D CreateRadialGlowTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.ARGB32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };
        float center = size * 0.5f;
        float maxDist = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center + 0.5f;
                float dy = y - center + 0.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / maxDist;
                float a = Mathf.Clamp01(1f - d);
                a = a * a; // smoother falloff
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();
        return tex;
    }

    private static Color GlowColorFor(RowKind kind)
    {
        switch (kind)
        {
            case RowKind.Gold:   return new Color(1.00f, 0.82f, 0.35f);
            case RowKind.Card:   return new Color(0.55f, 0.70f, 1.00f);
            case RowKind.Potion: return new Color(1.00f, 0.35f, 0.35f);
            case RowKind.Relic:  return new Color(0.40f, 0.95f, 0.95f);
        }
        return Color.white;
    }

    private static float BobPhaseFor(RowKind kind)
    {
        switch (kind)
        {
            case RowKind.Gold:   return 0f;
            case RowKind.Card:   return 1.2f;
            case RowKind.Potion: return 2.4f;
            case RowKind.Relic:  return 3.6f;
        }
        return 0f;
    }

    private static string GetCardTypeLabel(CardData c)
    {
        return DataManager.Instance.GetCardTypeLabel(c.cardType, c.subType);
    }

    private string BuildCardBody(CardData c)
    {
        var dm = DataManager.Instance;
        switch (c.cardType)
        {
            case CardType.SUMMON:
                return $"{dm.GetStatLabel("ATK")}: {c.attack}\n{dm.GetStatLabel("HP")}: {c.hp}";
            case CardType.MAGIC:
                return c.subType == CardSubType.ATTACK
                    ? $"{dm.GetStatLabel("DMG")}: {c.value}"
                    : $"{dm.GetStatLabel("BLOCK")}: {c.value}";
            case CardType.BUFF:
            case CardType.UTILITY:
                return Short(c.description);
            default:
                return Short(c.description);
        }
    }

    private static string EnName(string en, string kr)
    {
        return string.IsNullOrWhiteSpace(en) ? kr : en;
    }

    private string Short(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length > 32 ? s.Substring(0, 32) + "..." : s;
    }

    private void SyncStyleFontSizes()
    {
        if (!_stylesReady) return;
        _rowLabelStyle.fontSize = rowLabelFontSize;

        _rowLabelStyle.normal.textColor = rowLabelColor;
        _pickerSubStyle.normal.textColor = rowLabelColor;

        _cardNameStyle.fontSize = cardNameFontSize;
        _cardMetaStyle.fontSize = cardRarityFontSize; // DrawCardChoice에서 타입용으로 다시 스왑됨
        _cardBodyStyle.fontSize = cardBodyFontSize;
        _skipButtonStyle.fontSize = skipButtonFontSize;
    }

    private void EnsureStyles()
    {
        if (_stylesReady) return;

        _panelTex = Resources.Load<Texture2D>("Reward/Panel");
        _rowTex = Resources.Load<Texture2D>("Reward/RowButton");
        _medallionTex = Resources.Load<Texture2D>("Reward/MedallionRing");
        _continueTex = Resources.Load<Texture2D>("Reward/ContinueButton");
        _textSpoilsTex = Resources.Load<Texture2D>("Reward/TextSpoils");
        _iconGold = Resources.Load<Texture2D>("Reward/Gold");
        _iconCard = Resources.Load<Texture2D>("Reward/Deck");
        _iconPotion = Resources.Load<Texture2D>("Reward/Potion_Bottle");
        _iconRelic = Resources.Load<Texture2D>("Reward/RelicIcon");

        // 새 CardSlot/* 5레이어 에셋으로 통합 — BattleUI.DrawCardFrame과 동일한 비주얼 스택
        _cardPanelTex = Resources.Load<Texture2D>("CardSlot/CardBg");
        _cardArtFrameTex = Resources.Load<Texture2D>("CardSlot/CardArtFrame");
        _cardDescPanelTex = Resources.Load<Texture2D>("CardSlot/CardDescPanel");
        _cardFrameTex = Resources.Load<Texture2D>("CardSlot/CardBorder");
        _skipButtonTex = Resources.Load<Texture2D>("Reward/CardPicker/SkipButton");
        _textChooseCardTex = Resources.Load<Texture2D>("Reward/CardPicker/TextChooseCard");
        _titleDividerTex = Resources.Load<Texture2D>("Reward/CardPicker/TitleDivider");
        _manaFrameTex = Resources.Load<Texture2D>("CardSlot/ManaFrame");

        _displayFont = Resources.Load<Font>("Fonts/Cinzel-VariableFont_wght");

        _glowTex = CreateRadialGlowTexture(64);

        // 영어 전용 — Cinzel(디스플레이) 사용. 색은 인스펙터에서 동기화됨
        _rowLabelStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFont,
            fontSize = rowLabelFontSize,
            alignment = TextAnchor.MiddleLeft,
            fontStyle = FontStyle.Bold,
            wordWrap = true,
            normal = { textColor = rowLabelColor },
        };
        _pickerTitleStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFont,
            fontSize = 36,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            // hover 시 색 바뀌지 않게 모든 상태 동일
            normal   = { textColor = new Color(0.98f, 0.88f, 0.52f) },
            hover    = { textColor = new Color(0.98f, 0.88f, 0.52f) },
            active   = { textColor = new Color(0.98f, 0.88f, 0.52f) },
            focused  = { textColor = new Color(0.98f, 0.88f, 0.52f) },
            onNormal = { textColor = new Color(0.98f, 0.88f, 0.52f) },
            onHover  = { textColor = new Color(0.98f, 0.88f, 0.52f) },
            onActive = { textColor = new Color(0.98f, 0.88f, 0.52f) },
            onFocused= { textColor = new Color(0.98f, 0.88f, 0.52f) },
        };
        _pickerSubStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFont,
            fontSize = 15,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            // 라벨 hover/active 시 기본 GUISkin이 텍스트를 흰색으로 바꿔서 그걸 모든 상태 동일색으로 고정.
            normal   = { textColor = rowLabelColor },
            hover    = { textColor = rowLabelColor },
            active   = { textColor = rowLabelColor },
            focused  = { textColor = rowLabelColor },
            onNormal = { textColor = rowLabelColor },
            onHover  = { textColor = rowLabelColor },
            onActive = { textColor = rowLabelColor },
            onFocused= { textColor = rowLabelColor },
        };
        // 카드 이름 — 어두운 아트/배경 위에 얹히므로 크림 색상으로 대비 확보
        _cardNameStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFont,
            fontSize = 15,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            wordWrap = false,
            clipping = TextClipping.Overflow,
            normal = { textColor = new Color(0.99f, 0.93f, 0.74f) },
        };
        _cardMetaStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFont,
            fontSize = 12,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            wordWrap = true,
            // 희귀도 기본 색 — 더 밝은 골드로 가독성 ↑ (타입은 그리기 전 cardTypeColor로 덮어씀)
            normal = { textColor = new Color(1.0f, 0.90f, 0.55f) },
        };
        _cardBodyStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFont,
            fontSize = 13,
            alignment = TextAnchor.UpperCenter,
            fontStyle = FontStyle.Bold,
            wordWrap = true,
            normal = { textColor = new Color(0.96f, 0.92f, 0.74f) },
        };
        // SKIP 라벨 — 크림 파치먼트 스크롤 위에 얹히므로 다크 브라운
        _skipButtonStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFont,
            fontSize = 24,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.22f, 0.13f, 0.05f) },
        };
        // 코스트 버블 안 숫자 — 흰색, 외곽선은 DrawOutlinedLabel에서 처리
        _cardCostStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFont,
            fontSize = 18,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white },
        };
        // 모든 라벨 스타일의 hover/active 등 state 색을 normal과 동일하게 고정 (호버 색 변화 방지)
        LockStateColors(_rowLabelStyle);
        LockStateColors(_pickerTitleStyle);
        LockStateColors(_pickerSubStyle);
        LockStateColors(_cardNameStyle);
        LockStateColors(_cardMetaStyle);
        LockStateColors(_cardBodyStyle);
        LockStateColors(_skipButtonStyle);
        LockStateColors(_cardCostStyle);

        _stylesReady = true;
    }

    private static void LockStateColors(GUIStyle s)
    {
        if (s == null) return;
        var c = s.normal.textColor;
        s.hover.textColor    = c;
        s.active.textColor   = c;
        s.focused.textColor  = c;
        s.onNormal.textColor = c;
        s.onHover.textColor  = c;
        s.onActive.textColor = c;
        s.onFocused.textColor= c;
    }
}
