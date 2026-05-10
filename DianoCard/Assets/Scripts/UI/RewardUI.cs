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
    [Tooltip("패널 크기 (정적 고정 — 보상 개수와 무관하게 항상 동일).")]
    [SerializeField] private Vector2 panelSize = new(440, 530);
    [Tooltip("패널 Y 오프셋 (양수 = 아래, 음수 = 위로)")]
    [SerializeField] private float panelYOffset = 0f;
    [Tooltip("패널 전체(타이틀/행/컨티뉴 영역)를 클릭해도 보상 진행")]
    [SerializeField] private bool clickAnywhereToAdvance = true;

    [Header("Title (전리품)")]
    [Tooltip("타이틀 텍스트 (보통 '전리품')")]
    [SerializeField] private string titleText = "전리품";
    [Tooltip("타이틀 Y 오프셋 (패널 상단 기준)")]
    [SerializeField] private float titleYOffset = 12f;
    [Tooltip("타이틀 X 오프셋 (양수 = 오른쪽, 음수 = 왼쪽)")]
    [SerializeField] private float titleXOffset = 0f;
    [Tooltip("타이틀 영역 높이 (텍스트 세로 가운데 정렬용)")]
    [SerializeField] private float titleAreaHeight = 52f;
    [SerializeField, Range(12, 64)] private int titleFontSize = 30;
    [SerializeField] private Color titleColor = new(0.788f, 0.659f, 0.412f); // #c9a86a aged-brass
    [Tooltip("폰트 변경 (None = NotoSansKR)")]
    [SerializeField] private Font titleFontOverride;

    [Header("Panel Backdrop")]
    [Tooltip("전체 화면 어둡게 덮는 오버레이 알파")]
    [SerializeField, Range(0f, 1f)] private float backdropAlpha = 0.42f;
    [Tooltip("패널 뒤쪽 radial glow 색상")]
    [SerializeField] private Color panelGlowColor = new(1f, 0.78f, 0.35f);
    [Tooltip("패널 뒤쪽 glow 알파")]
    [SerializeField, Range(0f, 1f)] private float panelGlowAlpha = 0.32f;
    [Tooltip("패널 뒤쪽 glow 크기 = 패널 크기 × 이 값 (크게 잡아야 패널 바깥으로 퍼져 보임)")]
    [SerializeField, Range(1.0f, 3.5f)] private float panelGlowSizeFactor = 2.2f;

    [Header("Rows")]
    [SerializeField] private Vector2 rowSize = new(360, 55);
    [Tooltip("패널 상단에서 첫 행까지의 간격 — 헤더(타이틀+디바이더 라인)가 끝나는 지점에 맞추면 됨")]
    [SerializeField] private float rowsStartYOffset = 95f;
    [SerializeField] private float rowGap = 26f;
    [SerializeField] private int rowLabelFontSize = 18;
    [SerializeField] private Color rowLabelColor = new(0.99f, 0.95f, 0.78f);
    [Tooltip("각 행에 마우스 올렸을 때 행 단위로 부풀어 오르는 배율 (행 안의 모든 요소가 함께 스케일)")]
    [SerializeField, Range(1f, 1.15f)] private float rowHoverScale = 1.05f;

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
    [Tooltip("계속하기/Skip 버튼 hover 시 확대 배율")]
    [SerializeField, Range(1f, 1.3f)] private float continueHoverScale = 1.06f;
    [SerializeField] private Vector2 continueButtonSize = new(160, 58);
    [Tooltip("계속하기 버튼 하단 ~ 패널 바닥 사이 간격 (패널 바닥에 고정)")]
    [SerializeField] private float continueButtonBottomMargin = 36f;
    [Tooltip("버튼 위에 그리는 텍스트 (빈 문자열이면 안 그림)")]
    [SerializeField] private string continueButtonText = "";
    [SerializeField, Range(12, 48)] private int continueButtonFontSize = 24;
    [SerializeField] private Color continueButtonTextColor = new(0.788f, 0.659f, 0.412f); // #c9a86a
    [Tooltip("폰트 변경 (None = NotoSansKR)")]
    [SerializeField] private Font continueButtonFontOverride;
    [Tooltip("버튼 텍스트 Y 미세 조정 (양수 = 아래)")]
    [SerializeField] private float continueButtonTextYOffset = 0f;

    [Header("Card Picker")]
    [SerializeField] private Vector2 cardPickerCardSize = new(230, 320);
    [SerializeField] private float cardPickerSpacing = 36f;
    [SerializeField] private float cardPickerStartY = 200f;
    [SerializeField] private Vector2 cardPickerSkipSize = new(220, 62);
    [Tooltip("카드에 마우스 올렸을 때 확대 배율")]
    [SerializeField, Range(1f, 1.2f)] private float cardHoverScale = 1.05f;
    [Tooltip("카드 피커 전체 Y 오프셋 (양수 = 아래로)")]
    [SerializeField] private float cardPickerYOffset = 0f;

    [Header("Card Picker — Title Block")]
    [Tooltip("타이틀 전체에 더해지는 베이스 y 오프셋")]
    [SerializeField] private float cardPickerBaseYOffset = 20f;
    [Tooltip("한글 메인 타이틀 Y (예: '카드를 선택하세요')")]
    [SerializeField] private float cpTitleY = 100f;
    [SerializeField] private float cpTitleHeight = 70f;
    [SerializeField, Range(20, 80)] private int cpTitleFontSize = 30;
    [Tooltip("한글 타이틀 색상 — 아이보리/크림 톤")]
    [SerializeField] private Color cpTitleColor = new(0.925f, 0.894f, 0.816f); // #ECE4D0
    [Tooltip("타이틀 페이드 인/아웃 1주기 길이(초). 클수록 더 천천히 호흡.")]
    [SerializeField, Range(1f, 12f)] private float titlePulsePeriod = 5f;
    [Tooltip("타이틀 페이드 시 가장 흐려졌을 때 알파 (0=완전히 사라짐)")]
    [SerializeField, Range(0f, 1f)] private float titlePulseMinAlpha = 0.15f;
    [Tooltip("카드 제거 화면(부제)용 — 폰트 사이즈만 공유. 본 카드 보상에는 미사용.")]
    [SerializeField, Range(10, 24)] private int cpDescFontSize = 14;
    [Tooltip("카드 하단 ~ Skip 버튼 간격")]
    [SerializeField] private float skipButtonTopMargin = 36f;

    [Header("Card Picker — Font Sizes")]
    [SerializeField, Range(12, 36)] private int skipButtonFontSize = 20;
    [Tooltip("Skip 버튼 텍스트 색상 — 다크 패널 위 아이보리")]
    [SerializeField] private Color skipButtonTextColor = new(0.925f, 0.894f, 0.816f); // #ECE4D0

    [Header("Card Picker — Card Glow")]
    [SerializeField] private float cardGlowPadNormal = 42f;
    [SerializeField] private float cardGlowPadHover = 60f;
    [SerializeField, Range(0f, 1f)] private float cardGlowAlphaNormal = 0.38f;
    [SerializeField, Range(0f, 1f)] private float cardGlowAlphaHover = 0.65f;
    [SerializeField] private Color cardGlowColor = new(1f, 0.82f, 0.42f);

    private enum View { List, CardPicker, CardRemovePicker }
    private enum RowKind { Gold, Card, Potion, Relic, CardRemove }

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
    private bool _cardRemoveDone;
    private float _removeScrollY;

    // 어떤 BattleReward 인스턴스에 대해 이미 ResetForNewReward를 돌렸는지 추적.
    // 상태 엣지(_prevState != Reward → Reward) 기반으로 판정하면 텍트리/덱 화면 갔다 복귀할 때
    // ResetForNewReward가 또 걸려서 _cardDone/_goldDone 등이 풀려 같은 보상을 무한 수령 가능.
    // pendingReward는 새 보상마다 새 인스턴스로 재할당되므로 참조 동일성으로 판정한다.
    private BattleReward _initializedReward;
    private readonly List<Action> _pending = new();

    // Sprites (SPOILS list view)
    private Texture2D _panelTex;
    private Texture2D _rowTex;
    private Texture2D _medallionTex;
    private Texture2D _continueTex;
    private Texture2D _glowTex;
    // 행 아이콘 — HUD에서 쓰는 InGame/Icon/* 재사용
    private Texture2D _iconGold;
    private Texture2D _iconCard;
    private Texture2D _iconPotion;
    private Texture2D _iconRelic;
    private readonly Dictionary<string, Texture2D> _itemIconCache = new();

    // Sprites (Card picker view) — 타이틀/디바이더는 텍스트+프로시저럴, 스킵 패널만 스프라이트
    private Texture2D _skipButtonTex;

    // Fonts
    private Font _displayFont;
    private Font _displayFontKR;

    // Styles
    private GUIStyle _titleStyle;        // "전리품" — 한글 세리프, aged-brass
    private GUIStyle _continueTextStyle; // "계속하기 →" — 컨티뉴 버튼 텍스트
    private GUIStyle _rowLabelStyle;
    private GUIStyle _pickerTitleStyle;     // "카드를 선택하세요" — 한글 메인 타이틀
    private GUIStyle _pickerSubStyle;       // 카드 제거 화면 부제 등 — 한글 작은 라인
    private GUIStyle _skipButtonStyle;
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

        var pending = gsm.CurrentRun?.pendingReward;
        if (gsm.State == GameState.Reward && pending != null && !ReferenceEquals(_initializedReward, pending))
        {
            ResetForNewReward();
            _initializedReward = pending;
        }
        else if (pending == null)
        {
            // 보상 소진 후 다음 전투 보상에서 새 인스턴스가 들어오면 다시 reset 걸리도록 비움
            _initializedReward = null;
        }

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
        _cardRemoveDone = false;
        _removeScrollY = 0f;
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
        else if (_view == View.CardPicker)
            DrawCardPicker(gsm, reward);
        else
            DrawCardRemovePicker(gsm, run, reward);
    }

    // =========================================================
    // 리스트 뷰
    // =========================================================

    private void DrawListView(GameStateManager gsm, RunState run, BattleReward reward)
    {
        // 인스펙터 값이 런타임에 바뀔 수 있으므로 매번 스타일 폰트 크기 동기화
        SyncStyleFontSizes();

        // ─── 1. 패널 사각형 (정적 크기 — 보상 개수와 무관) ───
        float rowH = rowSize.y;
        var panelRect = new Rect(
            (RefW - panelSize.x) / 2f,
            (RefH - panelSize.y) / 2f + panelYOffset,
            panelSize.x,
            panelSize.y);

        // ─── 2. 뒷쪽 warm glow ───
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

        // ─── 3. 패널 본체 ───
        if (_panelTex != null)
            GUI.DrawTexture(panelRect, _panelTex, ScaleMode.StretchToFill);
        else
            DrawFilledRect(panelRect, new Color(0.10f, 0.14f, 0.20f, 0.96f));

        // ─── 4. 타이틀 ───
        if (!string.IsNullOrEmpty(titleText))
        {
            var titleRect = new Rect(
                panelRect.x + titleXOffset,
                panelRect.y + titleYOffset,
                panelRect.width,
                titleAreaHeight);
            GUI.Label(titleRect, titleText, _titleStyle);
        }

        // ─── 5. 보상 행 (각 행이 자기 영역 hover 시 통째로 부풀어 오름 + 클릭 가능) ───
        float rowW = rowSize.x;
        float rowX = panelRect.x + (panelRect.width - rowW) / 2f;
        float y = panelRect.y + rowsStartYOffset;
        bool rowClicked = false;

        var dm = DataManager.Instance;
        if (!_goldDone && reward.gold > 0)
        {
            rowClicked |= DrawRewardRow(new Rect(rowX, y, rowW, rowH), _iconGold, dm.GetUIString("reward.row.gold", reward.gold), RowKind.Gold);
            y += rowH + rowGap;
        }
        if (!_cardDone && reward.cardChoices != null && reward.cardChoices.Count > 0)
        {
            rowClicked |= DrawRewardRow(new Rect(rowX, y, rowW, rowH), _iconCard, dm.GetUIString("reward.row.card"), RowKind.Card);
            y += rowH + rowGap;
        }
        if (!_potionDone && reward.potion != null)
        {
            string pLabel = run.PotionSlotFull
                ? dm.GetUIString("reward.row.potion_full")
                : dm.GetUIString("reward.row.potion", reward.potion.name);
            rowClicked |= DrawRewardRow(new Rect(rowX, y, rowW, rowH), GetItemIcon(reward.potion.id, true) ?? _iconPotion, pLabel, RowKind.Potion);
            y += rowH + rowGap;
        }
        if (!_relicDone && reward.relic != null)
        {
            rowClicked |= DrawRewardRow(new Rect(rowX, y, rowW, rowH), GetItemIcon(reward.relic.id, false) ?? _iconRelic, dm.GetUIString("reward.row.relic", reward.relic.name), RowKind.Relic);
            y += rowH + rowGap;
        }
        if (!_cardRemoveDone && reward.cardRemoveOffer)
        {
            rowClicked |= DrawRewardRow(new Rect(rowX, y, rowW, rowH), _iconCard, dm.GetUIString("reward.row.card_remove"), RowKind.CardRemove);
            y += rowH + rowGap;
        }

        // ─── 6. 계속하기 버튼 — 패널 바닥에 고정 (정적 위치) + 자체 hover 스케일 ───
        float btnW = continueButtonSize.x;
        float btnH = continueButtonSize.y;
        float btnY = panelRect.yMax - continueButtonBottomMargin - btnH;
        var btnRect = new Rect((RefW - btnW) / 2f, btnY, btnW, btnH);

        bool btnHovered = btnRect.Contains(Event.current.mousePosition);
        var prevMatrix = GUI.matrix;
        if (btnHovered && continueHoverScale > 1f)
        {
            GUIUtility.ScaleAroundPivot(new Vector2(continueHoverScale, continueHoverScale), btnRect.center);
        }

        if (_continueTex != null)
            GUI.DrawTexture(btnRect, _continueTex, ScaleMode.ScaleToFit);

        if (!string.IsNullOrEmpty(continueButtonText) && _continueTextStyle != null)
        {
            var textRect = btnRect;
            textRect.y += continueButtonTextYOffset;
            GUI.Label(textRect, continueButtonText, _continueTextStyle);
        }

        bool continueClicked = GUI.Button(btnRect, GUIContent.none, GUIStyle.none);
        GUI.matrix = prevMatrix;

        // ─── 7. 패널 빈 영역 클릭 (타이틀 영역, 행 사이 공백 등) — 가장 마지막에 폴백 ───
        bool panelEmptyClicked = false;
        if (clickAnywhereToAdvance)
            panelEmptyClicked = GUI.Button(panelRect, GUIContent.none, GUIStyle.none);

        if (rowClicked || continueClicked || panelEmptyClicked)
        {
            _pending.Add(() => OnContinuePressed(gsm, run, reward));
        }
    }

    private Texture2D GetItemIcon(string id, bool isPotion)
    {
        if (string.IsNullOrEmpty(id)) return null; // 절차적 폴백 (DrawRewardRow에서 RowKind 기반으로 그림)
        if (_itemIconCache.TryGetValue(id, out var cached)) return cached;
        var folder = isPotion ? "PotionArt" : "RelicArt";
        var tex = Resources.Load<Texture2D>($"InGame/{folder}/{id}");
        _itemIconCache[id] = tex; // null도 캐시 (재로드 방지)
        return tex;
    }

    /// <summary>
    /// 보상 행 하나를 그림. 행 영역에 마우스가 있으면 행 전체(배경+메달리온+아이콘+라벨)가 통째로 부풀어 오름.
    /// 클릭되면 true 반환.
    /// </summary>
    private bool DrawRewardRow(Rect rect, Texture2D icon, string label, RowKind kind)
    {
        // 행 hover 판정 — 약간 확장된 영역으로 엣지 플리커 방지
        const float hoverPad = 6f;
        var hoverRect = new Rect(rect.x - hoverPad, rect.y - hoverPad,
                                  rect.width + hoverPad * 2, rect.height + hoverPad * 2);
        bool rowHovered = hoverRect.Contains(Event.current.mousePosition);

        // hover 시 행 전체를 한 덩어리로 스케일 (배경/메달리온/아이콘/글로우/라벨 동시 확대)
        // GUIUtility.ScaleAroundPivot는 기존 GUI.matrix(화면 fit 스케일 포함)에 추가로 곱하므로
        // 직접 GUI.matrix를 덮어쓰면 안 됨 — 그러면 화면 fit이 깨져 좌상단으로 밀림.
        var prevMatrix = GUI.matrix;
        if (rowHovered && rowHoverScale > 1f)
        {
            GUIUtility.ScaleAroundPivot(new Vector2(rowHoverScale, rowHoverScale), rect.center);
        }

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

        // 카드 제거 행은 CardBack 위에 빨강 가로 바를 얹어서 "제거" 의미 시각화
        if (kind == RowKind.CardRemove)
            DrawCardRemoveOverlay(iconRect);

        // 6. 라벨
        var labelRect = new Rect(
            rect.x + rect.height * labelStartXFactor,
            rect.y,
            rect.width - rect.height * labelStartXFactor - 12,
            rect.height);
        GUI.Label(labelRect, label, _rowLabelStyle);

        // 7. 클릭 가능한 영역 (스케일된 행 위에 그대로 얹힘 — 마우스 좌표는 inverse matrix로 자동 변환)
        bool clicked = GUI.Button(rect, GUIContent.none, GUIStyle.none);

        GUI.matrix = prevMatrix;
        return clicked;
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
        else if (!_cardRemoveDone && reward.cardRemoveOffer)
        {
            if (run.deck.Count > 0)
            {
                _view = View.CardRemovePicker;
                _removeScrollY = 0f;
                return;
            }
            _cardRemoveDone = true;
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
        if (!_cardRemoveDone && reward.cardRemoveOffer) return false;
        return true;
    }

    // =========================================================
    // 카드 선택 서브뷰
    // =========================================================

    private void DrawCardPicker(GameStateManager gsm, BattleReward reward)
    {
        SyncStyleFontSizes();

        float yOff = cardPickerYOffset + cardPickerBaseYOffset;

        // ─── 타이틀: "카드를 선택하세요" — 천천히 페이드 인/아웃 (호흡 효과) ───
        // 사인파 1주기 = titlePulsePeriod 초. 알파는 [titlePulseMinAlpha .. 1.0] 범위.
        float pulse = (Mathf.Sin(Time.time * 2f * Mathf.PI / Mathf.Max(0.5f, titlePulsePeriod)) + 1f) * 0.5f;
        float titleAlpha = Mathf.Lerp(titlePulseMinAlpha, 1f, pulse);

        var prevTitleColor = GUI.color;
        GUI.color = new Color(prevTitleColor.r, prevTitleColor.g, prevTitleColor.b, prevTitleColor.a * titleAlpha);
        GUI.Label(new Rect(0, cpTitleY + yOff, RefW, cpTitleHeight), DataManager.Instance.GetUIString("reward.title"), _pickerTitleStyle);
        GUI.color = prevTitleColor;

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

        // 스킵 버튼 — 다크 브라스 패널(다이아 양쪽 ◇ 포함) 위에 아이보리 라벨 얹기
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

    // =========================================================
    // 카드 제거 서브뷰 (무료 purge)
    // =========================================================

    private void DrawCardRemovePicker(GameStateManager gsm, RunState run, BattleReward reward)
    {
        SyncStyleFontSizes();

        var prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.75f);
        GUI.DrawTexture(new Rect(0, 0, RefW, RefH), Texture2D.whiteTexture);
        GUI.color = prev;

        var dm = DataManager.Instance;
        GUI.Label(new Rect(0, 22f, RefW, 44f), dm.GetUIString("reward.remove.title"), _pickerTitleStyle);
        GUI.Label(new Rect(0, 70f, RefW, 24f), dm.GetUIString("reward.remove.subtitle"), _pickerSubStyle);

        if (run.deck.Count == 0)
        {
            _pending.Add(() => { _cardRemoveDone = true; _view = View.List; });
            return;
        }

        var ev = Event.current;
        if (ev.type == EventType.ScrollWheel) { _removeScrollY += ev.delta.y * 30f; ev.Use(); }

        const int cols = 6;
        float cardW = 150f, cardH = 209f, gap = 14f;
        float totalW = cols * cardW + (cols - 1) * gap;
        float startX = (RefW - totalW) * 0.5f;
        float gridTop = 102f;
        float gridAreaH = RefH - gridTop - 110f;

        int rowCount = Mathf.CeilToInt(run.deck.Count / (float)cols);
        float contentH = rowCount * cardH + Mathf.Max(0, rowCount - 1) * gap;
        float maxScroll = Mathf.Max(0f, contentH - gridAreaH);
        _removeScrollY = Mathf.Clamp(_removeScrollY, -maxScroll, 0f);

        GUI.BeginGroup(new Rect(0, gridTop, RefW, gridAreaH));

        if (_battleUICache == null) _battleUICache = UnityEngine.Object.FindFirstObjectByType<BattleUI>();

        for (int i = 0; i < run.deck.Count; i++)
        {
            int col = i % cols;
            int row = i / cols;
            var rect = new Rect(startX + col * (cardW + gap), row * (cardH + gap) + _removeScrollY, cardW, cardH);
            if (rect.yMax < 0 || rect.y > gridAreaH) continue;

            if (_battleUICache != null) _battleUICache.DrawCardPreview(rect, run.deck[i]);
            else DrawFilledRect(rect, new Color(0.10f, 0.14f, 0.20f, 0.96f));

            var pill = new Rect(rect.x + 8f, rect.yMax - 38f, rect.width - 16f, 28f);
            DrawFilledRect(pill, new Color(0.55f, 0.05f, 0.05f, 0.75f));
            var pillStyle = new GUIStyle(_rowLabelStyle) { alignment = TextAnchor.MiddleCenter, fontSize = 12 };
            GUI.Label(pill, "PURGE", pillStyle);

            if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
            {
                var cardToRemove = run.deck[i];
                _pending.Add(() =>
                {
                    GameStateManager.Instance?.TakeCardRemoveReward(cardToRemove);
                    _cardRemoveDone = true;
                    _view = View.List;
                });
            }
        }

        GUI.EndGroup();

        float skipW = cardPickerSkipSize.x;
        float skipH = cardPickerSkipSize.y;
        var skipRect = new Rect((RefW - skipW) * 0.5f, RefH - skipH - 24f, skipW, skipH);
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
        if (_skipButtonTex != null) GUI.DrawTexture(skipDraw, _skipButtonTex, ScaleMode.ScaleToFit);
        else DrawFilledRect(skipDraw, new Color(0.10f, 0.14f, 0.20f, 0.96f));
        GUI.Label(skipDraw, dm.GetUIString("reward.remove.skip"), _skipButtonStyle);
        if (GUI.Button(skipRect, GUIContent.none, GUIStyle.none))
            _pending.Add(() => { _cardRemoveDone = true; _view = View.List; });
    }

    private BattleUI _battleUICache;

    private void DrawCardChoice(Rect rect, CardData card, bool hover)
    {
        // 카드 뒤쪽 warm glow — 호버 강조용
        if (_glowTex != null)
        {
            float pad = hover ? cardGlowPadHover : cardGlowPadNormal;
            var glowRect = new Rect(rect.x - pad, rect.y - pad, rect.width + pad * 2, rect.height + pad * 2);
            var prevGuiColor = GUI.color;
            GUI.color = new Color(cardGlowColor.r, cardGlowColor.g, cardGlowColor.b, hover ? cardGlowAlphaHover : cardGlowAlphaNormal);
            GUI.DrawTexture(glowRect, _glowTex, ScaleMode.StretchToFill);
            GUI.color = prevGuiColor;
        }

        // 카드 본체는 인게임 손패와 동일한 BattleUI 슬롯 비주얼로 통일.
        if (_battleUICache == null) _battleUICache = UnityEngine.Object.FindFirstObjectByType<BattleUI>();
        if (_battleUICache != null)
        {
            _battleUICache.DrawCardPreview(rect, card);
        }
        else
        {
            DrawFilledRect(rect, new Color(0.10f, 0.14f, 0.20f, 0.96f));
        }
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

    // 카드 제거 행에 표시할 작은 빨강 가로 바 (X 흉내) — CardBack 위에 오버레이
    private void DrawCardRemoveOverlay(Rect r)
    {
        var ember = new Color(0.541f, 0.227f, 0.227f, 0.95f); // #8a3a3a
        float w = r.width * 0.50f;
        float h = Mathf.Max(2f, r.height * 0.07f);
        var bar = new Rect(r.center.x - w * 0.5f, r.center.y - h * 0.5f, w, h);
        DrawFilledRect(bar, ember);
    }

    private static Color GlowColorFor(RowKind kind)
    {
        switch (kind)
        {
            case RowKind.Gold:       return new Color(1.00f, 0.82f, 0.35f);
            case RowKind.Card:       return new Color(0.55f, 0.70f, 1.00f);
            case RowKind.Potion:     return new Color(1.00f, 0.35f, 0.35f);
            case RowKind.Relic:      return new Color(0.40f, 0.95f, 0.95f);
            case RowKind.CardRemove: return new Color(0.85f, 0.25f, 0.25f);
        }
        return Color.white;
    }

    private static float BobPhaseFor(RowKind kind)
    {
        switch (kind)
        {
            case RowKind.Gold:       return 0f;
            case RowKind.Card:       return 1.2f;
            case RowKind.Potion:     return 2.4f;
            case RowKind.Relic:      return 3.6f;
            case RowKind.CardRemove: return 4.8f;
        }
        return 0f;
    }

    private static string EnName(string en, string kr) =>
        DianoCard.Data.LocaleSettings.Pick(kr, en);

    private void SyncStyleFontSizes()
    {
        if (!_stylesReady) return;
        _rowLabelStyle.fontSize = rowLabelFontSize;
        _rowLabelStyle.normal.textColor = rowLabelColor;

        // 카드 피커 타이틀 — 인스펙터에서 실시간 조정 가능하게 매 프레임 동기화
        _pickerTitleStyle.fontSize = cpTitleFontSize;
        _pickerTitleStyle.normal.textColor = cpTitleColor;

        _pickerSubStyle.fontSize = cpDescFontSize;
        _pickerSubStyle.normal.textColor = new Color(rowLabelColor.r, rowLabelColor.g, rowLabelColor.b, 0.85f);

        _skipButtonStyle.fontSize = skipButtonFontSize;
        _skipButtonStyle.normal.textColor = skipButtonTextColor;

        // 모든 상태(hover/active/...) 색도 normal과 동기화
        LockStateColors(_pickerTitleStyle);
        LockStateColors(_pickerSubStyle);
        LockStateColors(_skipButtonStyle);

        // 타이틀 — 인스펙터에서 실시간 조정 가능하도록 매 프레임 동기화
        if (_titleStyle != null)
        {
            _titleStyle.fontSize = titleFontSize;
            _titleStyle.normal.textColor = titleColor;
            _titleStyle.font = titleFontOverride != null ? titleFontOverride : _displayFontKR;
        }
        // Continue 버튼 텍스트
        if (_continueTextStyle != null)
        {
            _continueTextStyle.fontSize = continueButtonFontSize;
            _continueTextStyle.normal.textColor = continueButtonTextColor;
            _continueTextStyle.font = continueButtonFontOverride != null ? continueButtonFontOverride : _displayFontKR;
        }
    }

    private void EnsureStyles()
    {
        if (_stylesReady) return;

        _panelTex = Resources.Load<Texture2D>("Reward/Panel");
        _rowTex = Resources.Load<Texture2D>("Reward/RowButton");
        _medallionTex = Resources.Load<Texture2D>("Reward/MedallionRing");
        _continueTex = Resources.Load<Texture2D>("Reward/ContinueButton");
        // 행 아이콘 — HUD에서 쓰이는 동일 아이콘 재사용 (톤 일관성)
        _iconGold   = Resources.Load<Texture2D>("InGame/Icon/Gold");
        _iconCard   = Resources.Load<Texture2D>("InGame/Icon/CardBack");
        _iconPotion = Resources.Load<Texture2D>("InGame/Icon/Potion_Bottle");
        _iconRelic  = Resources.Load<Texture2D>("InGame/Icon/Relic");

        _skipButtonTex = Resources.Load<Texture2D>("Reward/CardPicker/SkipButton");

        _displayFont = Resources.Load<Font>("Fonts/Cinzel-VariableFont_wght");
        _displayFontKR = Resources.Load<Font>("Fonts/NotoSansKR-VariableFont_wght");

        _glowTex = CreateRadialGlowTexture(64);

        // 영어 전용 — Cinzel(디스플레이) 사용. 색은 인스펙터에서 동기화됨
        _titleStyle = new GUIStyle(GUI.skin.label)
        {
            font = titleFontOverride != null ? titleFontOverride : _displayFontKR,
            fontSize = titleFontSize,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = titleColor },
        };
        _continueTextStyle = new GUIStyle(GUI.skin.label)
        {
            font = continueButtonFontOverride != null ? continueButtonFontOverride : _displayFontKR,
            fontSize = continueButtonFontSize,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = continueButtonTextColor },
        };
        // 행 라벨은 한글이 섞이므로 KR 로케일에서 Cinzel(라틴 전용)을 쓰면 시스템 폰트로 폴백돼 깨짐.
        bool isKR = DianoCard.Data.LocaleSettings.Current == DianoCard.Data.Language.KR;
        _rowLabelStyle = new GUIStyle(GUI.skin.label)
        {
            font = isKR ? _displayFontKR : _displayFont,
            fontSize = rowLabelFontSize,
            alignment = TextAnchor.MiddleLeft,
            fontStyle = FontStyle.Bold,
            wordWrap = true,
            normal = { textColor = rowLabelColor },
        };
        // 한글 메인 타이틀 — "카드를 선택하세요". NotoSansKR + 큰 사이즈 + 아이보리/크림.
        _pickerTitleStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFontKR,
            fontSize = cpTitleFontSize,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = cpTitleColor },
        };
        // 한글 설명 (카드 제거 화면 부제 등) — NotoSansKR + 작게 + 머티드 아이보리.
        _pickerSubStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFontKR,
            fontSize = cpDescFontSize,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Normal,
            normal = { textColor = new Color(rowLabelColor.r, rowLabelColor.g, rowLabelColor.b, 0.85f) },
        };
        // SKIP 라벨 — 다크 패널 위 아이보리.
        _skipButtonStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFontKR,
            fontSize = skipButtonFontSize,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = skipButtonTextColor },
        };
        // 모든 라벨 스타일의 hover/active 등 state 색을 normal과 동일하게 고정 (호버 색 변화 방지)
        LockStateColors(_rowLabelStyle);
        LockStateColors(_pickerTitleStyle);
        LockStateColors(_pickerSubStyle);
        LockStateColors(_skipButtonStyle);

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
