using System;
using System.Collections.Generic;
using DianoCard.Data;
using DianoCard.Game;
using UnityEngine;

/// <summary>
/// 상인 노드 상호작용 화면. GameState == Shop 일 때만 그려짐.
///
/// 메인 뷰:
/// - 상단에 타이틀 + 골드 표시
/// - 중단에 카드 5장 (Common 3 / Uncommon 1 / Rare 1)
/// - 하단에 포션 2 / 유물 1 / 카드 제거 서비스 1 (행 형태)
/// - 우하단 LEAVE SHOP 버튼 → ExitShop
///
/// 카드 제거 서브뷰:
/// - 덱 전체를 그리드로 뿌리고 그 중 하나를 고르면 제거 + 가격 차감
/// - CANCEL 버튼으로 복귀
/// </summary>
[DefaultExecutionOrder(1000)]
public class ShopUI : MonoBehaviour
{
    private const float RefW = 1280f;
    private const float RefH = 720f;

    private enum View { Main, RemovePicker }
    private View _view = View.Main;

    private readonly List<Action> _pending = new();
    private GameState _prevState = GameState.Lobby;

    // ShopUI 전용 배경 이미지 (동굴+횃불)
    private Texture2D _bgTex;

    // Reward 쪽 아트 재사용
    private Texture2D _panelTex;
    private Texture2D _rowTex;
    private Texture2D _medallionTex;
    private Texture2D _continueTex;
    private Texture2D _glowTex;
    private Texture2D _iconGold;
    private Texture2D _iconCard;
    private Texture2D _iconPotion;
    private Texture2D _iconRelic;
    private Texture2D _merchantIconTex;
    private Texture2D _cardsBannerTex;
    private Texture2D _rowBannerTex;

    // 카드 본체는 BattleUI.DrawCardPreview에 위임 — 가격 배지만 ShopUI 자체 자산
    private Texture2D _priceBadgeTex;     // CardSlot/CardCountBadge — 우상단 가격 배너 재활용

    [Header("Background")]
    [Tooltip("배경 이미지 위에 덮는 디밍 색상 (UI 가독성).")]
    [SerializeField] private Color bgDimColor = new(0.02f, 0.04f, 0.06f, 0.55f);

    [Header("Shop Card Layout (카드 5장)")]
    [SerializeField] private float cardsStartY = 120f;
    [SerializeField] private Vector2 cardsSize = new(170f, 237f);
    [SerializeField] private float cardsSpacing = 24f;
    [Tooltip("호버 시 카드 확대 배율")]
    [SerializeField, Range(1f, 1.2f)] private float cardsHoverScale = 1.04f;

    [Header("Row Section Layout (POTIONS/RELICS/SERVICES)")]
    [SerializeField] private float rowAreaTop = 490f;
    [SerializeField] private Vector2 rowSize = new(280f, 48f);
    [SerializeField] private float rowGap = 22f;
    [SerializeField] private float colGap = 130f;
    [Tooltip("패널 top ~ 첫 row top 거리 (헤더 배너 공간).")]
    [SerializeField] private float headerPad = 34f;
    [Tooltip("마지막 row bottom ~ 패널 bottom 거리.")]
    [SerializeField] private float bottomPad = 0f;
    [SerializeField, Range(1, 8)] private int potionsSlots = 3;
    [SerializeField, Range(1, 8)] private int relicsSlots = 3;
    [SerializeField, Range(1, 8)] private int servicesSlots = 2;

    [Header("Debug (테스트용 아이템 수)")]
    [Tooltip("0이면 기본 생성. 1 이상이면 포션을 이 수만큼 채움.")]
    [SerializeField, Range(0, 8)] private int debugPotionCount = 3;
    [Tooltip("0이면 기본 생성. 1 이상이면 렐릭을 이 수만큼 채움.")]
    [SerializeField, Range(0, 8)] private int debugRelicCount = 2;

    [Header("Section Panel Style (뒤 배경판)")]
    [SerializeField] private Color sectionPanelFill = new(0.04f, 0.07f, 0.10f, 0.78f);
    [Tooltip("패널 모서리 둥글기 (px). 0이면 직각.")]
    [SerializeField, Range(0, 32)] private int sectionPanelCornerRadius = 10;
    [Tooltip("패널 테두리 색상 (연한 금색).")]
    [SerializeField] private Color sectionPanelBorderColor = new(0.85f, 0.75f, 0.45f, 0.4f);
    [Tooltip("패널 테두리 두께 (px). 0이면 테두리 없음.")]
    [SerializeField, Range(0f, 6f)] private float sectionPanelBorderThickness = 1.5f;

    [Header("Cards Panel Size (카드 섹션 배경 패널)")]
    [Tooltip("카드 그룹 좌우로 배경판이 더 차지하는 가로 패딩 (px).")]
    [SerializeField, Range(0f, 80f)] private float cardsPanelPaddingX = 37.1f;
    [Tooltip("카드 섹션 패널 상단 Y 좌표 (px). 헤더 리본을 덮을 정도로 위에 둠.")]
    [SerializeField, Range(40f, 180f)] private float cardsPanelTopY = 80f;
    [Tooltip("카드 하단 ~ 패널 바닥까지의 여백 (px). 가격 배지가 카드 안에 있어 작아도 됨.")]
    [SerializeField, Range(-20f, 120f)] private float cardsPanelBottomPad = 16.9f;

    [Header("Row Section Panel Size (포션/렐릭/서비스 패널)")]
    [Tooltip("각 행 섹션의 좌우 패딩 (px).")]
    [SerializeField, Range(0f, 40f)] private float rowSectionPanelPaddingX = 30f;
    [Tooltip("패널 위쪽 추가 여백 (px). 헤더+아이템 위에 여유 공간.")]
    [SerializeField, Range(0f, 40f)] private float rowSectionPanelExtraTop = 20f;
    [Tooltip("패널 아래쪽 추가 여백 (px). 마지막 아이템 아래 여유 공간.")]
    [SerializeField, Range(0f, 40f)] private float rowSectionPanelExtraBottom = 0f;
    [Tooltip("패널 안 아이템 시작 Y 오프셋 (px). 음수=위로.")]
    [SerializeField, Range(-40f, 30f)] private float rowContentOffsetY = -10.3f;

    [Header("Cards Banner (카드 섹션 헤더)")]
    [SerializeField, Range(80f, 400f)] private float cardsBannerMinWidth = 138f;
    [SerializeField, Range(0.5f, 3f)] private float cardsBannerHeightScale = 1.67f;
    [SerializeField, Range(-60f, 20f)] private float cardsBannerOffsetY = -47f;

    [Header("Row Banner (포션/렐릭/서비스 헤더)")]
    [SerializeField, Range(80f, 400f)] private float rowBannerMinWidth = 164f;
    [SerializeField, Range(0.5f, 3f)] private float rowBannerHeightScale = 0.86f;
    [SerializeField, Range(-60f, 20f)] private float rowBannerOffsetY = -40f;

    [Header("Section Header Fallback (텍스처 없을 때)")]
    [SerializeField] private Color sectionHeaderFill = new(0.08f, 0.06f, 0.05f, 0.95f);
    [SerializeField] private Vector2 sectionHeaderPad = new(26f, 5f);
    [SerializeField, Range(0, 24)] private int sectionHeaderCornerRadius = 8;

    [Header("Card Price Badge (카드 우상단 배너)")]
    [Tooltip("카드 폭 대비 배지 가로 비율.")]
    [SerializeField, Range(0.25f, 0.6f)] private float priceBadgeWidthPct = 0.42f;
    [Tooltip("배지 가로 대비 세로 비율 (CardCountBadge.png 원본 약 0.42).")]
    [SerializeField, Range(0.3f, 0.7f)] private float priceBadgeAspect = 0.42f;
    [Tooltip("카드 우상단 코너로부터 배지까지 오프셋(px).")]
    [SerializeField] private Vector2 priceBadgeOffset = new(2f, 4f);
    [Tooltip("배지 안 코인 아이콘 크기 (배지 높이 대비 비율).")]
    [SerializeField, Range(0.3f, 1.4f)] private float priceBadgeIconScale = 0.6f;
    [Tooltip("코인 아이콘 가로 위치 (배지 폭 대비 비율, 0=왼쪽 끝).")]
    [SerializeField, Range(0f, 0.6f)] private float priceBadgeIconXPct = 0.33f;
    [Tooltip("가격 숫자 폰트 크기 (배지 높이 대비 비율).")]
    [SerializeField, Range(0.3f, 1f)] private float priceBadgeFontScale = 0.5f;
    [Tooltip("가격 숫자 좌측 시작 위치 (배지 폭 대비 비율).")]
    [SerializeField, Range(0.2f, 0.7f)] private float priceBadgeTextXPct = 0.43f;
    [SerializeField] private Color priceBadgeAffordColor = new(1f, 0.95f, 0.60f);
    [SerializeField] private Color priceBadgeExpensiveColor = new(1f, 0.50f, 0.50f);

    [Header("Item Row (포션/렐릭/서비스 줄)")]
    [Tooltip("좌측 메달리온 크기 (행 높이 대비 비율).")]
    [SerializeField, Range(0.6f, 2f)] private float rowMedallionScale = 1.35f;
    [Tooltip("메달리온 중심의 가로 위치 (행 높이 대비 비율, 0=왼쪽 끝).")]
    [SerializeField, Range(0.2f, 1.2f)] private float rowMedallionCenterXPct = 0.5f;
    [Tooltip("메달리온 안 아이콘 크기 (메달리온 대비 비율).")]
    [SerializeField, Range(0.3f, 0.9f)] private float rowIconScale = 0.5f;
    [Tooltip("호버 시 행 확대 배율. 1이면 확대 없음.")]
    [SerializeField, Range(1f, 1.2f)] private float rowHoverScale = 1.04f;
    [Tooltip("이름 라벨 시작 X (행 높이 대비 비율).")]
    [SerializeField, Range(0.8f, 2f)] private float rowLabelXPct = 1.5f;

    [Header("Font Sizes (각 텍스트 폰트 크기)")]
    [SerializeField, Range(10, 60)] private int titleFontSize = 42;
    [SerializeField, Range(8, 30)] private int subtitleFontSize = 15;
    [SerializeField, Range(10, 40)] private int goldFontSize = 26;
    [SerializeField, Range(8, 30)] private int sectionHeaderFontSize = 17;
    [SerializeField, Range(8, 24)] private int rowLabelFontSize = 15;
    [SerializeField, Range(8, 24)] private int rowPriceFontSize = 17;
    [SerializeField, Range(8, 24)] private int cardCostBaseFontSize = 18;

    [Header("Cards Banner Text (카드 배너 글씨)")]
    [SerializeField, Range(8, 30)] private int cardsBannerFontSize = 25;
    [SerializeField, Range(-20f, 20f)] private float cardsBannerTextOffsetY = -6f;

    [Header("Row Banner Text (포션/렐릭/서비스 배너 글씨)")]
    [SerializeField, Range(8, 30)] private int rowBannerFontSize = 15;
    [SerializeField, Range(-20f, 20f)] private float rowBannerTextOffsetY = -1.7f;

    [Header("Gold Display (골드 표시 위치/크기)")]
    [Tooltip("골드 패널 가로 크기 (px).")]
    [SerializeField, Range(80f, 300f)] private float goldPanelWidth = 116.8f;
    [Tooltip("골드 패널 세로 크기 (px).")]
    [SerializeField, Range(24f, 60f)] private float goldPanelHeight = 38f;
    [Tooltip("골드 패널 우측 여백 (px).")]
    [SerializeField, Range(0f, 60f)] private float goldPanelRightMargin = 20f;
    [Tooltip("골드 패널 상단 Y (px).")]
    [SerializeField, Range(0f, 60f)] private float goldPanelTopY = 24f;
    [Tooltip("골드 아이콘 크기 (px).")]
    [SerializeField, Range(12f, 48f)] private float goldIconSize = 28f;
    [Tooltip("골드 아이콘 왼쪽 여백 (px).")]
    [SerializeField, Range(0f, 30f)] private float goldIconLeftPad = 16.7f;
    [Tooltip("골드 텍스트 왼쪽 시작 (아이콘 오른쪽부터, px).")]
    [SerializeField, Range(0f, 100f)] private float goldTextLeftPad = 55.1f;
    [Tooltip("골드 숫자 폰트 크기.")]
    [SerializeField, Range(10, 40)] private int goldDisplayFontSize = 21;

    [Header("Icon Glow (아이콘 뒤 글로우)")]
    [Tooltip("메달리온 아이콘 뒤 글로우 활성화.")]
    [SerializeField] private bool medallionGlowEnabled = true;
    [Tooltip("글로우 크기 (아이콘 대비 배율).")]
    [SerializeField, Range(1f, 3f)] private float medallionGlowScale = 1.8f;
    [Tooltip("글로우 색상.")]
    [SerializeField] private Color medallionGlowColor = new(1f, 0.85f, 0.4f, 0.35f);
    [Header("Row Price Badge (아이템 행 가격 배지)")]
    [Tooltip("행 가격 배지 가로 크기 (px).")]
    [SerializeField, Range(40f, 140f)] private float rowPriceBadgeWidth = 71f;
    [Tooltip("행 가격 배지 세로 여백 (px).")]
    [SerializeField, Range(0f, 20f)] private float rowPriceBadgePadY = 7f;
    [Tooltip("행 가격 배지 우측 여백 (px).")]
    [SerializeField, Range(0f, 40f)] private float rowPriceBadgeRightMargin = 7.2f;
    [Tooltip("배지 안 코인 아이콘 크기 (배지 높이 대비 비율).")]
    [SerializeField, Range(0.3f, 1.4f)] private float rowPriceBadgeIconScale = 0.6f;
    [Tooltip("코인 아이콘 가로 위치 (배지 폭 대비 비율).")]
    [SerializeField, Range(0f, 0.6f)] private float rowPriceBadgeIconXPct = 0.328f;
    [Tooltip("가격 숫자 폰트 크기 (배지 높이 대비 비율).")]
    [SerializeField, Range(0.3f, 1f)] private float rowPriceBadgeFontScale = 0.5f;
    [Tooltip("가격 숫자 좌측 시작 위치 (배지 폭 대비 비율).")]
    [SerializeField, Range(0.2f, 0.7f)] private float rowPriceBadgeTextXPct = 0.43f;

    private BattleUI _battleUICache;

    private Font _displayFont;

    private GUIStyle _titleStyle;
    private GUIStyle _subStyle;
    private GUIStyle _goldStyle;
    private GUIStyle _sectionStyle;
    private GUIStyle _rowLabelStyle;
    private GUIStyle _priceStyle;
    private GUIStyle _priceExpensiveStyle;
    private GUIStyle _cardCostStyle;
    private GUIStyle _leaveStyle;
    private GUIStyle _cancelStyle;
    private bool _stylesReady;

    // 카드 제거 선택 스크롤
    private float _removeScrollY;

    [Header("Hover Tooltip (호버 상세정보)")]
    [SerializeField, Range(160f, 400f)] private float tooltipWidth = 240f;
    [SerializeField] private Color tooltipFillColor = new(0.05f, 0.07f, 0.10f, 0.95f);
    [SerializeField] private Color tooltipBorderColor = new(0.80f, 0.62f, 0.30f, 0.9f);
    [SerializeField, Range(0, 16)] private int tooltipCornerRadius = 6;
    [SerializeField] private Vector2 tooltipMouseOffset = new(18f, 14f);
    [SerializeField, Range(8, 20)] private int tooltipTitleFontSize = 14;
    [SerializeField] private Color tooltipTitleColor = new(1f, 0.90f, 0.55f);
    [SerializeField, Range(8, 18)] private int tooltipBodyFontSize = 12;
    [SerializeField] private Color tooltipBodyColor = new(0.92f, 0.88f, 0.74f);

    // 호버 툴팁 런타임 상태
    private string _tooltipTitle;
    private string _tooltipBody;
    private GUIStyle _tooltipTitleStyle;
    private GUIStyle _tooltipBodyStyle;

    // 각 섹션 스크롤 위치 — 아이템이 고정 슬롯 수를 초과하면 세로 스크롤

    void Update()
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null) return;

        // 치트: F8 — 언제든 Shop 강제 진입
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.f8Key.wasPressedThisFrame)
        {
            gsm.Cheat_EnterShop();
        }

        if (_prevState != GameState.Shop && gsm.State == GameState.Shop)
        {
            _view = View.Main;
            _removeScrollY = 0f;
        }
        _prevState = gsm.State;

        if (_pending.Count > 0)
        {
            var snapshot = new List<Action>(_pending);
            _pending.Clear();
            foreach (var a in snapshot) a?.Invoke();
        }
    }

    void OnGUI()
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null || gsm.State != GameState.Shop) return;
        var run = gsm.CurrentRun;
        var shop = gsm.CurrentShop;
        if (run == null || shop == null) return;

        EnsureStyles();
        UpdateFontSizes();

        GUI.depth = 0;
        float scale = Mathf.Min(Screen.width / RefW, Screen.height / RefH);
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));

        DrawBackdrop();

        if (_view == View.Main)
            DrawMain(gsm, run, shop);
        else
            DrawRemovePicker(gsm, run, shop);
    }

    // =========================================================
    // 공통
    // =========================================================

    private void DrawBackdrop()
    {
        var prev = GUI.color;

        // 배경 이미지 (동굴/횃불) — 풀스크린 덮기. 없으면 어두운 단색 폴백.
        if (_bgTex != null)
        {
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(0, 0, RefW, RefH), _bgTex, ScaleMode.ScaleAndCrop);
            // UI 가독성 확보용 디밍 (Inspector에서 조정)
            GUI.color = bgDimColor;
            GUI.DrawTexture(new Rect(0, 0, RefW, RefH), Texture2D.whiteTexture);
        }
        else
        {
            GUI.color = new Color(0.03f, 0.02f, 0.04f, 0.88f);
            GUI.DrawTexture(new Rect(0, 0, RefW, RefH), Texture2D.whiteTexture);
        }
        GUI.color = prev;

        // 중앙 warm glow (상점 분위기)
        if (_glowTex != null)
        {
            GUI.color = new Color(1f, 0.78f, 0.35f, 0.18f);
            GUI.DrawTexture(new Rect(RefW * 0.5f - 700f, RefH * 0.5f - 450f, 1400f, 900f), _glowTex, ScaleMode.StretchToFill);
            GUI.color = prev;
        }
    }

    private void DrawHeader(RunState run)
    {
        var dm = DataManager.Instance;

        // 상인 아이콘 (왼쪽)
        if (_merchantIconTex != null)
        {
            GUI.DrawTexture(new Rect(40f, 18f, 68f, 68f), _merchantIconTex, ScaleMode.ScaleToFit);
        }

        // 타이틀 (비활성화)
        // GUI.Label(new Rect(0, 20f, RefW, 48f), dm.GetUIString("shop.title"), _titleStyle);
        // GUI.Label(new Rect(0, 64f, RefW, 22f), dm.GetUIString("shop.subtitle"), _subStyle);

        // 골드 인디케이터 (오른쪽 상단) — 섹션 패널과 동일한 스타일
        var goldRect = new Rect(RefW - goldPanelWidth - goldPanelRightMargin, goldPanelTopY, goldPanelWidth, goldPanelHeight);
        if (sectionPanelBorderThickness > 0f)
        {
            DrawRoundedFilledRect(goldRect, sectionPanelBorderColor, sectionPanelCornerRadius);
            float t = sectionPanelBorderThickness;
            var inner = new Rect(goldRect.x + t, goldRect.y + t, goldRect.width - t * 2f, goldRect.height - t * 2f);
            DrawRoundedFilledRect(inner, sectionPanelFill, Mathf.Max(0, sectionPanelCornerRadius - (int)t));
        }
        else
        {
            DrawRoundedFilledRect(goldRect, sectionPanelFill, sectionPanelCornerRadius);
        }

        if (_iconGold != null)
        {
            float iconY = goldRect.y + (goldRect.height - goldIconSize) * 0.5f;
            GUI.DrawTexture(new Rect(goldRect.x + goldIconLeftPad, iconY, goldIconSize, goldIconSize), _iconGold, ScaleMode.ScaleToFit);
        }
        int prevGoldFS = _goldStyle.fontSize;
        _goldStyle.fontSize = goldDisplayFontSize;
        GUI.Label(new Rect(goldRect.x + goldTextLeftPad, goldRect.y, goldRect.width - goldTextLeftPad - 6f, goldRect.height),
                  run.gold.ToString(), _goldStyle);
        _goldStyle.fontSize = prevGoldFS;
    }

    private void DrawLeaveButton(GameStateManager gsm)
    {
        var dm = DataManager.Instance;
        float btnW = 208f, btnH = 58f;  // 20% 축소 (260→208, 72→58)
        var rect = new Rect(RefW - btnW - 30f, RefH - btnH - 24f, btnW, btnH);
        bool hover = rect.Contains(Event.current.mousePosition);
        Rect draw = hover ? Scale(rect, 1.06f) : rect;

        if (_continueTex != null)
            GUI.DrawTexture(draw, _continueTex, ScaleMode.ScaleToFit);
        else
            DrawFilledRect(draw, new Color(0.12f, 0.16f, 0.22f, 0.95f));

        if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
        {
            _pending.Add(() => gsm.ExitShop());
        }
    }

    // 디버그: Inspector에서 설정한 수만큼 더미 포션/렐릭 채우기
    private void DebugFillShop(ShopState shop)
    {
        var dm = DataManager.Instance;
        if (dm == null) return;

        if (debugPotionCount > 0 && shop.potions.Count < debugPotionCount)
        {
            var potions = new List<PotionData>(dm.Potions.Values);
            while (shop.potions.Count < debugPotionCount && potions.Count > 0)
            {
                var p = potions[shop.potions.Count % potions.Count];
                shop.potions.Add(new ShopPotionEntry { potion = p, price = 50 });
            }
        }

        if (debugRelicCount > 0 && shop.relics.Count < debugRelicCount)
        {
            var relics = new List<RelicData>(dm.Relics.Values);
            while (shop.relics.Count < debugRelicCount && relics.Count > 0)
            {
                var r = relics[shop.relics.Count % relics.Count];
                shop.relics.Add(new ShopRelicEntry { relic = r, price = 100 });
            }
        }
    }

    // =========================================================
    // 메인 뷰
    // =========================================================

    private void DrawMain(GameStateManager gsm, RunState run, ShopState shop)
    {
        // 매 프레임 호버 툴팁 초기화 — 호버 중인 row에서 다시 채워줌
        _tooltipTitle = null;
        _tooltipBody = null;

        DebugFillShop(shop);
        DrawHeader(run);

        // 카드 섹션 — 헤더 + 카드 N장 + 가격바를 감싸는 어두운 패널
        int cardCount = shop.cards.Count;
        float cardW = cardsSize.x, cardH = cardsSize.y;
        float spacing = cardsSpacing;
        float totalW = Mathf.Max(1, cardCount) * cardW + Mathf.Max(0, cardCount - 1) * spacing;
        float startX = (RefW - totalW) * 0.5f;
        float startY = cardsStartY;

        if (cardCount > 0)
        {
            // 카드 섹션 배경 패널 — 헤더 위부터 카드 아래까지. Inspector 패딩으로 크기 조절.
            var cardsPanelRect = new Rect(
                startX - cardsPanelPaddingX,
                cardsPanelTopY,
                totalW + cardsPanelPaddingX * 2f,
                (startY + cardH + cardsPanelBottomPad) - cardsPanelTopY);
            DrawSectionPanel(cardsPanelRect);
        }

        // 카드 섹션 헤더 — 중앙에 골드 테두리 리본 배너
        DrawCardsSectionHeader(RefW * 0.5f, 106f, DataManager.Instance.GetUIString("shop.section.cards"));

        if (cardCount > 0)
        {

            int hoveredIdx = -1;
            for (int i = 0; i < cardCount; i++)
            {
                var r = new Rect(startX + i * (cardW + spacing), startY, cardW, cardH);
                if (r.Contains(Event.current.mousePosition)) { hoveredIdx = i; break; }
            }

            for (int i = 0; i < cardCount; i++)
            {
                var entry = shop.cards[i];
                var rect = new Rect(startX + i * (cardW + spacing), startY, cardW, cardH);
                bool hover = (i == hoveredIdx) && !entry.sold;
                Rect draw = hover ? Scale(rect, cardsHoverScale) : rect;

                DrawShopCard(draw, entry, run);

                if (!entry.sold && GUI.Button(rect, GUIContent.none, GUIStyle.none))
                {
                    int captured = i;
                    _pending.Add(() =>
                    {
                        var e = shop.cards[captured];
                        GameStateManager.Instance?.BuyShopCard(e);
                    });
                }
            }
        }

        // 포션/유물/서비스 섹션 — 3열, 고정 크기 패널 + 오버플로우 시 스크롤. 모두 Inspector 튜닝.
        float rowW = rowSize.x, rowH = rowSize.y;
        float rowsTotalW = rowW * 3f + colGap * 2f;
        float col1X = (RefW - rowsTotalW) * 0.5f;
        float col2X = col1X + rowW + colGap;
        float col3X = col2X + rowW + colGap;

        // 패널 크기 공식: 헤더 영역 + 슬롯수 * (rowH + rowGap) - rowGap + 하단 패딩
        float panelY = rowAreaTop - headerPad;
        float pnlHPot = headerPad + potionsSlots * (rowH + rowGap) - rowGap + bottomPad;
        float pnlHRel = headerPad + relicsSlots * (rowH + rowGap) - rowGap + bottomPad;
        float pnlHSvc = headerPad + servicesSlots * (rowH + rowGap) - rowGap + bottomPad;

        // 3개 섹션 배경 패널 (각자 슬롯 수 맞는 고정 크기 + 상하 추가 여백)
        float padX = rowSectionPanelPaddingX;
        float exT = rowSectionPanelExtraTop;
        float exB = rowSectionPanelExtraBottom;
        DrawSectionPanel(new Rect(col1X - padX, panelY - exT, rowW + padX * 2f, pnlHPot + exT + exB));
        DrawSectionPanel(new Rect(col2X - padX, panelY - exT, rowW + padX * 2f, pnlHRel + exT + exB));
        DrawSectionPanel(new Rect(col3X - padX, panelY - exT, rowW + padX * 2f, pnlHSvc + exT + exB));

        // 섹션 헤더들 — 각 섹션 패널 상단에 리본 배너
        DrawRowSectionHeader(col1X + rowW * 0.5f, panelY + 4f, DataManager.Instance.GetUIString("shop.section.potions"));
        DrawRowSectionHeader(col2X + rowW * 0.5f, panelY + 4f, DataManager.Instance.GetUIString("shop.section.relics"));
        DrawRowSectionHeader(col3X + rowW * 0.5f, panelY + 4f, DataManager.Instance.GetUIString("shop.section.services"));

        // 메달리온 오버플로우 — 행보다 큰 메달리온이 잘리지 않도록 viewport 확장
        float medHalf = rowH * rowMedallionScale * 0.5f;
        float medCx = rowH * rowMedallionCenterXPct;
        float medOverL = Mathf.Max(0f, medHalf - medCx);
        float medOverR = Mathf.Max(0f, medHalf - (rowW - medCx));
        float medOverT = Mathf.Max(0f, medHalf - rowH * 0.5f);
        float contentOff = Mathf.Min(0f, rowContentOffsetY);
        float vpExtraTop = medOverT - contentOff;
        float vpExtraLeft = Mathf.Max(medOverL, rowSectionPanelPaddingX);

        // 1열: 포션 (스크롤)
        DrawPotionsColumn(shop, run, new Rect(col1X - vpExtraLeft, rowAreaTop - vpExtraTop, rowW + vpExtraLeft + medOverR, pnlHPot - headerPad - bottomPad + rowGap + vpExtraTop),
                          rowH, rowGap, potionsSlots, vpExtraLeft, medOverT + rowContentOffsetY);

        // 2열: 유물 (스크롤)
        DrawRelicsColumn(shop, run, new Rect(col2X - vpExtraLeft, rowAreaTop - vpExtraTop, rowW + vpExtraLeft + medOverR, pnlHRel - headerPad - bottomPad + rowGap + vpExtraTop),
                         rowH, rowGap, relicsSlots, vpExtraLeft, medOverT + rowContentOffsetY);

        // 3열: 서비스 (스크롤)
        DrawServicesColumn(shop, run, new Rect(col3X - vpExtraLeft, rowAreaTop - vpExtraTop, rowW + vpExtraLeft + medOverR, pnlHSvc - headerPad - bottomPad + rowGap + vpExtraTop),
                           rowH, rowGap, servicesSlots, vpExtraLeft, medOverT + rowContentOffsetY);

        DrawLeaveButton(gsm);

        // 호버 툴팁 — 모든 UI 위에 마지막으로 그려짐
        DrawHoverTooltip();
    }

    // 세 column 렌더링 — viewport 안에서 스크롤. 아이템 ≤ slots면 스크롤 안 보임, 초과하면 세로 스크롤.
    private void DrawPotionsColumn(ShopState shop, RunState run, Rect viewport, float rowH, float rowGap, int slots, float padL, float padT)
    {
        int count = Mathf.Min(shop.potions.Count, slots);
        float rowW = viewport.width - padL;

        if (count == 0)
            GUI.Label(new Rect(viewport.x + padL, viewport.y + padT + 14f, rowW, rowH), "—", _rowLabelStyle);

        for (int i = 0; i < count; i++)
        {
            var entry = shop.potions[i];
            var r = new Rect(viewport.x + padL, viewport.y + padT + i * (rowH + rowGap), rowW, rowH);
            bool purchasable = !entry.sold && !run.PotionSlotFull;
            bool hover = purchasable && r.Contains(Event.current.mousePosition);
            Rect draw = hover ? Scale(r, rowHoverScale) : r;
            DrawPotionRow(draw, entry, run);
            if (hover)
            {
                var p = entry.potion;
                _tooltipTitle = p.nameEn;
                _tooltipBody = $"Type: {p.potionType}\nValue: {p.value}\nTarget: {p.target}";
            }
            if (purchasable && GUI.Button(r, GUIContent.none, GUIStyle.none))
            {
                int captured = i;
                _pending.Add(() => GameStateManager.Instance?.BuyShopPotion(shop.potions[captured]));
            }
        }
    }

    private void DrawRelicsColumn(ShopState shop, RunState run, Rect viewport, float rowH, float rowGap, int slots, float padL, float padT)
    {
        int count = Mathf.Min(shop.relics.Count, slots);
        float rowW = viewport.width - padL;

        if (count == 0)
            GUI.Label(new Rect(viewport.x + padL, viewport.y + padT + 14f, rowW, rowH), "—", _rowLabelStyle);

        for (int i = 0; i < count; i++)
        {
            var entry = shop.relics[i];
            var r = new Rect(viewport.x + padL, viewport.y + padT + i * (rowH + rowGap), rowW, rowH);
            bool purchasable = !entry.sold && !run.relics.Contains(entry.relic);
            bool hover = purchasable && r.Contains(Event.current.mousePosition);
            Rect draw = hover ? Scale(r, rowHoverScale) : r;
            DrawRelicRow(draw, entry, run);
            if (hover)
            {
                var r2 = entry.relic;
                _tooltipTitle = r2.nameEn;
                _tooltipBody = $"Trigger: {r2.trigger}\nEffect: {r2.effectType}\nValue: {r2.value}";
            }
            if (!entry.sold && GUI.Button(r, GUIContent.none, GUIStyle.none))
            {
                int captured = i;
                _pending.Add(() => GameStateManager.Instance?.BuyShopRelic(shop.relics[captured]));
            }
        }
    }

    private void DrawServicesColumn(ShopState shop, RunState run, Rect viewport, float rowH, float rowGap, int slots, float padL, float padT)
    {
        float rowW = viewport.width - padL;

        var svcRect = new Rect(viewport.x + padL, viewport.y + padT, rowW, rowH);
        bool svcPurchasable = !shop.cardRemoveUsed && run.gold >= shop.cardRemovePrice && run.deck.Count > 0;
        bool svcHover = svcPurchasable && svcRect.Contains(Event.current.mousePosition);
        Rect svcDraw = svcHover ? Scale(svcRect, rowHoverScale) : svcRect;
        DrawRemoveServiceRow(svcDraw, shop, run);
        if (svcHover)
        {
            _tooltipTitle = DataManager.Instance.GetUIString("shop.remove_card");
            _tooltipBody = "Remove one card from your deck permanently.";
        }
        if (svcPurchasable && GUI.Button(svcRect, GUIContent.none, GUIStyle.none))
        {
            _pending.Add(() => { _view = View.RemovePicker; _removeScrollY = 0f; });
        }
    }

    // =========================================================
    // 호버 툴팁 — 마우스 옆 어두운 패널에 이름 + 효과 설명 표시
    // =========================================================

    private void DrawHoverTooltip()
    {
        if (string.IsNullOrEmpty(_tooltipTitle)) return;

        EnsureTooltipStyles();

        string body = string.IsNullOrEmpty(_tooltipBody) ? "" : _tooltipBody;
        float tw = tooltipWidth;

        var titleSize = _tooltipTitleStyle.CalcSize(new GUIContent(_tooltipTitle));
        float bodyH = string.IsNullOrEmpty(body) ? 0f : _tooltipBodyStyle.CalcHeight(new GUIContent(body), tw - 24f);
        float tooltipH = 12f + titleSize.y + 6f + bodyH + 12f;

        // 마우스 근처 배치, 화면 밖 보정
        var mouse = Event.current.mousePosition;
        float tx = mouse.x + tooltipMouseOffset.x;
        float ty = mouse.y + tooltipMouseOffset.y;
        if (tx + tw > RefW) tx = mouse.x - tw - 10f;
        if (ty + tooltipH > RefH) ty = RefH - tooltipH - 4f;

        var tooltipRect = new Rect(tx, ty, tw, tooltipH);

        // 배경 + 테두리 (Inspector 색/둥글기)
        DrawRoundedFilledRect(tooltipRect, tooltipBorderColor, tooltipCornerRadius);
        var tooltipInner = new Rect(tooltipRect.x + 1f, tooltipRect.y + 1f, tooltipRect.width - 2f, tooltipRect.height - 2f);
        DrawRoundedFilledRect(tooltipInner, tooltipFillColor, Mathf.Max(0, tooltipCornerRadius - 1));

        // 타이틀
        var titleRect = new Rect(tx + 12f, ty + 10f, tw - 24f, titleSize.y);
        GUI.Label(titleRect, _tooltipTitle, _tooltipTitleStyle);

        // 본문
        if (!string.IsNullOrEmpty(body))
        {
            var bodyRect = new Rect(tx + 12f, titleRect.yMax + 6f, tw - 24f, bodyH);
            GUI.Label(bodyRect, body, _tooltipBodyStyle);
        }
    }

    private void EnsureTooltipStyles()
    {
        // Inspector에서 값 바뀌면 반영되도록 매번 갱신
        if (_tooltipTitleStyle == null)
        {
            _tooltipTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
            };
            _tooltipBodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
            };
        }
        _tooltipTitleStyle.fontSize = tooltipTitleFontSize;
        _tooltipTitleStyle.normal.textColor = tooltipTitleColor;
        _tooltipBodyStyle.fontSize = tooltipBodyFontSize;
        _tooltipBodyStyle.normal.textColor = tooltipBodyColor;
        LockStateColors(_tooltipTitleStyle);
        LockStateColors(_tooltipBodyStyle);
    }

    private void DrawShopCard(Rect rect, ShopCardEntry entry, RunState run)
    {
        var card = entry.card;

        // Glow
        if (_glowTex != null && !entry.sold)
        {
            float pad = 28f;
            var g = new Rect(rect.x - pad, rect.y - pad, rect.width + pad * 2, rect.height + pad * 2);
            var prev = GUI.color;
            GUI.color = new Color(1f, 0.82f, 0.42f, 0.28f);
            GUI.DrawTexture(g, _glowTex, ScaleMode.StretchToFill);
            GUI.color = prev;
        }

        // 카드 본체 — 인게임 손패와 동일한 BattleUI 슬롯 비주얼로 통일.
        if (_battleUICache == null) _battleUICache = UnityEngine.Object.FindFirstObjectByType<BattleUI>();
        if (_battleUICache != null)
        {
            _battleUICache.DrawCardPreview(rect, card);
        }
        else
        {
            DrawFilledRect(rect, new Color(0.10f, 0.14f, 0.20f, 0.96f));
        }

        // 가격 배지 — 우상단 (마나 코스트와 좌우 대칭). CardCountBadge 텍스처(왼쪽 V-notch) 재활용.
        if (!entry.sold)
        {
            float badgeW = rect.width * priceBadgeWidthPct;
            float badgeH = badgeW * priceBadgeAspect;
            var badgeRect = new Rect(
                rect.xMax - badgeW - priceBadgeOffset.x,
                rect.y + priceBadgeOffset.y,
                badgeW, badgeH);

            if (_priceBadgeTex != null)
            {
                GUI.DrawTexture(badgeRect, _priceBadgeTex, ScaleMode.StretchToFill, alphaBlend: true);
            }
            else
            {
                DrawRoundedFilledRect(badgeRect, new Color(0.10f, 0.07f, 0.05f, 0.92f), 6);
            }

            bool canAfford = run.gold >= entry.price;

            // 코인 아이콘 — V-notch 오른쪽, 세로 중앙
            if (_iconGold != null)
            {
                float iconS = badgeRect.height * priceBadgeIconScale;
                float iconX = badgeRect.x + badgeRect.width * priceBadgeIconXPct - iconS * 0.5f;
                float iconY = badgeRect.y + (badgeRect.height - iconS) * 0.5f;
                GUI.DrawTexture(new Rect(iconX, iconY, iconS, iconS), _iconGold, ScaleMode.ScaleToFit);
            }

            // 가격 숫자 — 채워진 영역 중앙
            var priceTextRect = new Rect(
                badgeRect.x + badgeRect.width * priceBadgeTextXPct,
                badgeRect.y,
                badgeRect.width * (1f - priceBadgeTextXPct - 0.04f),
                badgeRect.height);
            int prevFS = _cardCostStyle.fontSize;
            var prevAlign = _cardCostStyle.alignment;
            _cardCostStyle.fontSize = Mathf.RoundToInt(badgeRect.height * priceBadgeFontScale);
            _cardCostStyle.alignment = TextAnchor.MiddleCenter;
            DrawOutlinedLabel(priceTextRect, entry.price.ToString(), _cardCostStyle,
                canAfford ? priceBadgeAffordColor : priceBadgeExpensiveColor,
                new Color(0f, 0f, 0f, 0.85f), 1f);
            _cardCostStyle.fontSize = prevFS;
            _cardCostStyle.alignment = prevAlign;
        }

        // Sold 오버레이 + 라벨
        if (entry.sold)
        {
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = prev;

            var soldRect = new Rect(rect.x, rect.y + rect.height * 0.42f, rect.width, rect.height * 0.16f);
            GUI.Label(soldRect, DataManager.Instance.GetUIString("shop.sold_out"), _priceStyle);
        }
    }

    private void DrawPotionRow(Rect rect, ShopPotionEntry entry, RunState run)
    {
        string name = EnName(entry.potion.nameEn, entry.potion.nameKr);
        string subLabel = run.PotionSlotFull && !entry.sold
            ? DataManager.Instance.GetUIString("shop.potion_full")
            : null;
        DrawItemRow(rect, _iconPotion, name, subLabel, entry.price, entry.sold, run.gold >= entry.price && !run.PotionSlotFull);
    }

    private void DrawRelicRow(Rect rect, ShopRelicEntry entry, RunState run)
    {
        string name = EnName(entry.relic.nameEn, entry.relic.nameKr);
        string subLabel = null;
        if (run.relics.Contains(entry.relic))
            subLabel = DataManager.Instance.GetUIString("shop.owned");
        DrawItemRow(rect, _iconRelic, name, subLabel, entry.price, entry.sold, run.gold >= entry.price && !run.relics.Contains(entry.relic));
    }

    private void DrawRemoveServiceRow(Rect rect, ShopState shop, RunState run)
    {
        var dm = DataManager.Instance;
        string name = dm.GetUIString("shop.remove_card");
        string subLabel = shop.cardRemoveUsed ? dm.GetUIString("shop.remove_card_used") : null;
        bool purchasable = !shop.cardRemoveUsed && run.gold >= shop.cardRemovePrice && run.deck.Count > 0;
        DrawItemRow(rect, _iconCard, name, subLabel, shop.cardRemovePrice, shop.cardRemoveUsed, purchasable);
    }

    private void DrawItemRow(Rect rect, Texture2D icon, string name, string subLabel, int price, bool sold, bool purchasable)
    {
        // 바닥
        if (_rowTex != null) GUI.DrawTexture(rect, _rowTex, ScaleMode.StretchToFill);
        else DrawFilledRect(rect, new Color(0.18f, 0.30f, 0.40f, 1f));

        // 메달리온 + 아이콘
        float cx = rect.x + rect.height * rowMedallionCenterXPct;
        float cy = rect.y + rect.height * 0.5f;
        float medSize = rect.height * rowMedallionScale;
        if (_medallionTex != null)
            GUI.DrawTexture(new Rect(cx - medSize * 0.5f, cy - medSize * 0.5f, medSize, medSize), _medallionTex, ScaleMode.ScaleToFit);
        if (icon != null)
        {
            float iconS = medSize * rowIconScale;
            if (medallionGlowEnabled && _glowTex != null && !sold)
            {
                var prevC = GUI.color;
                GUI.color = medallionGlowColor;
                float glowS = iconS * medallionGlowScale;
                GUI.DrawTexture(new Rect(cx - glowS * 0.5f, cy - glowS * 0.5f, glowS, glowS), _glowTex, ScaleMode.StretchToFill);
                GUI.color = prevC;
            }
            GUI.DrawTexture(new Rect(cx - iconS * 0.5f, cy - iconS * 0.5f, iconS, iconS), icon, ScaleMode.ScaleToFit);
        }

        // 이름 + 서브 라벨
        float labelX = rect.x + rect.height * rowLabelXPct;
        float labelW = rect.xMax - rowPriceBadgeWidth - rowPriceBadgeRightMargin - labelX;
        var labelRect = new Rect(labelX, rect.y + 6f, Mathf.Max(20f, labelW), rect.height - 12f);
        GUI.Label(labelRect, name, _rowLabelStyle);
        if (!string.IsNullOrEmpty(subLabel))
        {
            var subRect = new Rect(labelRect.x, labelRect.y + labelRect.height * 0.5f, labelRect.width, labelRect.height * 0.5f);
            GUI.Label(subRect, subLabel, _priceExpensiveStyle);
        }

        // 가격 배지 (오른쪽) — CardCountBadge 배너 스타일
        if (!sold)
        {
            float badgeW = rowPriceBadgeWidth;
            float badgeH = rect.height - rowPriceBadgePadY * 2f;
            var badgeRect = new Rect(
                rect.xMax - badgeW - rowPriceBadgeRightMargin,
                rect.y + rowPriceBadgePadY,
                badgeW, badgeH);

            if (_priceBadgeTex != null)
                GUI.DrawTexture(badgeRect, _priceBadgeTex, ScaleMode.StretchToFill, alphaBlend: true);
            else
                DrawRoundedFilledRect(badgeRect, new Color(0.10f, 0.07f, 0.05f, 0.92f), 6);

            if (_iconGold != null)
            {
                float iconS = badgeRect.height * rowPriceBadgeIconScale;
                float iconX = badgeRect.x + badgeRect.width * rowPriceBadgeIconXPct - iconS * 0.5f;
                float iconY = badgeRect.y + (badgeRect.height - iconS) * 0.5f;
                GUI.DrawTexture(new Rect(iconX, iconY, iconS, iconS), _iconGold, ScaleMode.ScaleToFit);
            }

            var priceTextRect = new Rect(
                badgeRect.x + badgeRect.width * rowPriceBadgeTextXPct,
                badgeRect.y,
                badgeRect.width * (1f - rowPriceBadgeTextXPct - 0.04f),
                badgeRect.height);
            int prevFS = _cardCostStyle.fontSize;
            var prevAlign = _cardCostStyle.alignment;
            _cardCostStyle.fontSize = Mathf.RoundToInt(badgeRect.height * rowPriceBadgeFontScale);
            _cardCostStyle.alignment = TextAnchor.MiddleCenter;
            DrawOutlinedLabel(priceTextRect, price.ToString(), _cardCostStyle,
                purchasable ? priceBadgeAffordColor : priceBadgeExpensiveColor,
                new Color(0f, 0f, 0f, 0.85f), 1f);
            _cardCostStyle.fontSize = prevFS;
            _cardCostStyle.alignment = prevAlign;
        }

        // 구매 불가 오버레이
        if (sold)
        {
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.50f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = prev;
        }
    }

    // =========================================================
    // 카드 제거 선택 뷰
    // =========================================================

    private void DrawRemovePicker(GameStateManager gsm, RunState run, ShopState shop)
    {
        var dm = DataManager.Instance;

        GUI.Label(new Rect(0, 22f, RefW, 48f), dm.GetUIString("shop.remove_card").ToUpperInvariant(), _titleStyle);
        GUI.Label(new Rect(0, 68f, RefW, 22f), dm.GetUIString("shop.remove_card_pick"), _subStyle);

        // 스크롤 입력
        var ev = Event.current;
        if (ev.type == EventType.ScrollWheel)
        {
            _removeScrollY += ev.delta.y * 30f;
            ev.Use();
        }

        const int cols = 6;
        float cardW = 150f, cardH = 209f, gap = 14f;  // 카드피커와 동일 비율
        float totalW = cols * cardW + (cols - 1) * gap;
        float startX = (RefW - totalW) * 0.5f;
        float gridTop = 110f;
        float gridAreaH = RefH - gridTop - 110f;

        // 컨텐츠 최대 스크롤
        int rowCount = Mathf.CeilToInt(run.deck.Count / (float)cols);
        float contentH = rowCount * cardH + Mathf.Max(0, rowCount - 1) * gap;
        float maxScroll = Mathf.Max(0f, contentH - gridAreaH);
        _removeScrollY = Mathf.Clamp(_removeScrollY, -maxScroll, 0f);

        GUI.BeginGroup(new Rect(0, gridTop, RefW, gridAreaH));

        int hoveredIdx = -1;
        for (int i = 0; i < run.deck.Count; i++)
        {
            int col = i % cols;
            int row = i / cols;
            var rect = new Rect(startX + col * (cardW + gap),
                                row * (cardH + gap) + _removeScrollY,
                                cardW, cardH);
            if (rect.yMax < 0 || rect.y > gridAreaH) continue;
            if (rect.Contains(ev.mousePosition)) { hoveredIdx = i; break; }
        }

        for (int i = 0; i < run.deck.Count; i++)
        {
            int col = i % cols;
            int row = i / cols;
            var rect = new Rect(startX + col * (cardW + gap),
                                row * (cardH + gap) + _removeScrollY,
                                cardW, cardH);
            if (rect.yMax < 0 || rect.y > gridAreaH) continue;

            bool hover = (i == hoveredIdx);
            Rect draw = hover ? Scale(rect, cardsHoverScale) : rect;

            // 동일한 미니 카드 그리기 — 가격 대신 "REMOVE" 라벨
            DrawRemoveCardOption(draw, run.deck[i]);

            if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
            {
                int captured = i;
                _pending.Add(() =>
                {
                    var c = run.deck[captured];
                    if (GameStateManager.Instance != null
                        && GameStateManager.Instance.UseCardRemoveService(c))
                    {
                        _view = View.Main;
                    }
                });
            }
        }

        GUI.EndGroup();

        // 취소 버튼
        float btnW = 220f, btnH = 62f;
        var cancelRect = new Rect((RefW - btnW) * 0.5f, RefH - btnH - 24f, btnW, btnH);
        bool hoverCancel = cancelRect.Contains(ev.mousePosition);
        Rect cancelDraw = hoverCancel ? Scale(cancelRect, 1.06f) : cancelRect;
        if (_continueTex != null)
            GUI.DrawTexture(cancelDraw, _continueTex, ScaleMode.ScaleToFit);
        else
            DrawFilledRect(cancelDraw, new Color(0.12f, 0.16f, 0.22f, 0.95f));
        GUI.Label(cancelDraw, dm.GetUIString("shop.cancel"), _cancelStyle);
        if (GUI.Button(cancelRect, GUIContent.none, GUIStyle.none))
        {
            _pending.Add(() => { _view = View.Main; });
        }
    }

    private void DrawRemoveCardOption(Rect rect, CardData card)
    {
        // 미니 카드 — 인게임 손패와 동일한 BattleUI 슬롯 비주얼로 통일.
        if (_battleUICache == null) _battleUICache = UnityEngine.Object.FindFirstObjectByType<BattleUI>();
        if (_battleUICache != null)
        {
            _battleUICache.DrawCardPreview(rect, card);
        }
        else
        {
            DrawFilledRect(rect, new Color(0.10f, 0.14f, 0.20f, 0.96f));
        }

        // 바닥: REMOVE 라벨
        var pill = new Rect(rect.x + 8f, rect.yMax - 38f, rect.width - 16f, 28f);
        DrawFilledRect(pill, new Color(0.55f, 0.05f, 0.05f, 0.75f));
        GUI.Label(pill, "REMOVE", _priceStyle);
    }

    // =========================================================
    // 리소스 / 스타일
    // =========================================================

    private void EnsureStyles()
    {
        if (_stylesReady) return;

        // ShopUI 전용 에셋 폴더 — RewardUI/BattleUI에서 분리되어 독립적으로 교체/튜닝 가능.
        _panelTex        = Resources.Load<Texture2D>("ShopUI/Panel");
        _rowTex          = Resources.Load<Texture2D>("ShopUI/RowButton");
        _medallionTex    = Resources.Load<Texture2D>("ShopUI/MedallionRing");
        _continueTex     = Resources.Load<Texture2D>("ShopUI/Task_generate_1");
        _cardsBannerTex  = Resources.Load<Texture2D>("ShopUI/SectionBanner");
        _rowBannerTex    = Resources.Load<Texture2D>("ShopUI/SectionBanner");
        _iconGold        = Resources.Load<Texture2D>("ShopUI/Gold");
        _iconCard        = Resources.Load<Texture2D>("ShopUI/Deck");
        _iconPotion      = Resources.Load<Texture2D>("ShopUI/Potion_Bottle");
        _iconRelic       = Resources.Load<Texture2D>("ShopUI/RelicIcon");
        _merchantIconTex = Resources.Load<Texture2D>("ShopUI/Node_Merchant");
        _bgTex           = Resources.Load<Texture2D>("ShopUI/BackGround");

        _priceBadgeTex    = Resources.Load<Texture2D>("CardSlot/CardCountBadge");

        _displayFont = Resources.Load<Font>("Fonts/Cinzel-VariableFont_wght");

        _glowTex = CreateRadialGlowTexture(64);

        var cream = new Color(0.99f, 0.95f, 0.78f);
        var darkBrown = new Color(0.22f, 0.13f, 0.05f);

        _titleStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFont, fontSize = titleFontSize, alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.98f, 0.88f, 0.52f) },
        };
        _subStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFont, fontSize = subtitleFontSize, alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold, normal = { textColor = cream },
        };
        _goldStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFont, fontSize = goldFontSize, alignment = TextAnchor.MiddleLeft,
            fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.85f, 0.45f) },
        };
        _sectionStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFont, fontSize = sectionHeaderFontSize, alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.85f, 0.75f, 0.45f) },
        };
        _rowLabelStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFont, fontSize = rowLabelFontSize, alignment = TextAnchor.MiddleLeft,
            fontStyle = FontStyle.Bold, wordWrap = true, normal = { textColor = cream },
        };
        _priceStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFont, fontSize = rowPriceFontSize, alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.85f, 0.45f) },
        };
        _priceExpensiveStyle = new GUIStyle(_priceStyle) { normal = { textColor = new Color(0.85f, 0.40f, 0.40f) } };
        _cardCostStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFont, fontSize = cardCostBaseFontSize, alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold, normal = { textColor = Color.white },
        };
        _leaveStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFont, fontSize = 22, alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold, normal = { textColor = darkBrown },
        };
        _cancelStyle = new GUIStyle(_leaveStyle);

        // 모든 라벨 스타일의 hover/active 색을 normal과 동일하게 고정 (호버 색 변화 방지)
        LockStateColors(_titleStyle);
        LockStateColors(_subStyle);
        LockStateColors(_goldStyle);
        LockStateColors(_sectionStyle);
        LockStateColors(_rowLabelStyle);
        LockStateColors(_priceStyle);
        LockStateColors(_priceExpensiveStyle);
        LockStateColors(_cardCostStyle);
        LockStateColors(_leaveStyle);
        LockStateColors(_cancelStyle);

        _stylesReady = true;
    }

    private void UpdateFontSizes()
    {
        if (_titleStyle != null) _titleStyle.fontSize = titleFontSize;
        if (_subStyle != null) _subStyle.fontSize = subtitleFontSize;
        if (_goldStyle != null) _goldStyle.fontSize = goldFontSize;
        if (_sectionStyle != null) _sectionStyle.fontSize = sectionHeaderFontSize;
        if (_rowLabelStyle != null) _rowLabelStyle.fontSize = rowLabelFontSize;
        if (_priceStyle != null) _priceStyle.fontSize = rowPriceFontSize;
        if (_priceExpensiveStyle != null) _priceExpensiveStyle.fontSize = rowPriceFontSize;
        if (_cardCostStyle != null) _cardCostStyle.fontSize = cardCostBaseFontSize;
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

    // =========================================================
    // 작은 유틸
    // =========================================================

    private static Rect Scale(Rect r, float s) => new Rect(
        r.center.x - r.width * s * 0.5f,
        r.center.y - r.height * s * 0.5f,
        r.width * s,
        r.height * s);

    private static void DrawFilledRect(Rect r, Color c)
    {
        var prev = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = prev;
    }

    // 둥근 사각형 — 9-slice용 텍스처를 radius별로 캐시.
    private readonly Dictionary<int, Texture2D> _roundedRectCache = new();
    private GUIStyle _roundedRectStyle;

    private Texture2D GetRoundedRectTexture(int radius)
    {
        if (radius < 1) radius = 1;
        if (_roundedRectCache.TryGetValue(radius, out var cached) && cached != null) return cached;

        int size = radius * 2 + 4;
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave,
        };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = 0f, dy = 0f;
                if (x < radius) dx = radius - x - 0.5f;
                else if (x >= size - radius) dx = x - (size - radius) + 0.5f;
                if (y < radius) dy = radius - y - 0.5f;
                else if (y >= size - radius) dy = y - (size - radius) + 0.5f;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(radius - dist + 0.5f);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        }
        t.SetPixels32(px);
        t.Apply(false, true);
        _roundedRectCache[radius] = t;
        return t;
    }

    // 둥근 사각형 채우기 — radius=0이면 직각. Repaint 이벤트에서만 Style.Draw 호출.
    private void DrawRoundedFilledRect(Rect r, Color c, int radius)
    {
        if (radius <= 0) { DrawFilledRect(r, c); return; }
        if (Event.current.type != EventType.Repaint) return;
        var tex = GetRoundedRectTexture(radius);
        if (_roundedRectStyle == null) _roundedRectStyle = new GUIStyle();
        _roundedRectStyle.normal.background = tex;
        _roundedRectStyle.border = new RectOffset(radius, radius, radius, radius);
        var prevBg = GUI.backgroundColor;
        GUI.backgroundColor = c;
        _roundedRectStyle.Draw(r, GUIContent.none, false, false, false, false);
        GUI.backgroundColor = prevBg;
    }

    // 섹션 배경 패널 — 둥근 모서리 + 얇은 테두리.
    private void DrawSectionPanel(Rect r)
    {
        if (sectionPanelBorderThickness > 0f)
        {
            DrawRoundedFilledRect(r, sectionPanelBorderColor, sectionPanelCornerRadius);
            float t = sectionPanelBorderThickness;
            var inner = new Rect(r.x + t, r.y + t, r.width - t * 2f, r.height - t * 2f);
            DrawRoundedFilledRect(inner, sectionPanelFill, Mathf.Max(0, sectionPanelCornerRadius - (int)t));
        }
        else
        {
            DrawRoundedFilledRect(r, sectionPanelFill, sectionPanelCornerRadius);
        }
    }

    // Cards 섹션 헤더 — CardsBanner 텍스처 사용.
    private void DrawCardsSectionHeader(float centerX, float topY, string label)
    {
        DrawBannerHeader(centerX, topY, label, _cardsBannerTex,
            cardsBannerMinWidth, cardsBannerHeightScale, cardsBannerOffsetY,
            cardsBannerFontSize, cardsBannerTextOffsetY);
    }

    // 포션/렐릭/서비스 섹션 헤더 — RowBanner 텍스처 사용.
    private void DrawRowSectionHeader(float centerX, float topY, string label)
    {
        DrawBannerHeader(centerX, topY, label, _rowBannerTex,
            rowBannerMinWidth, rowBannerHeightScale, rowBannerOffsetY,
            rowBannerFontSize, rowBannerTextOffsetY);
    }

    private void DrawBannerHeader(float centerX, float topY, string label,
        Texture2D tex, float minW, float hScale, float offY, int fontSize, float textOffY)
    {
        int prevFS = _sectionStyle.fontSize;

        // 배너 크기는 기본 폰트로 계산 (폰트 크기 변경이 배너 크기에 영향 안 줌)
        _sectionStyle.fontSize = sectionHeaderFontSize;
        var size = _sectionStyle.CalcSize(new UnityEngine.GUIContent(label));
        float w = size.x + sectionHeaderPad.x * 2f;
        float h = size.y + sectionHeaderPad.y * 2f;

        // 라벨은 개별 폰트 크기로
        _sectionStyle.fontSize = fontSize;

        if (tex != null)
        {
            float aspect = (float)tex.width / tex.height;
            float bannerH = h * hScale;
            float bannerW = bannerH * aspect;
            if (bannerW < w) bannerW = w;
            if (bannerW < minW) bannerW = minW;
            var bannerRect = new Rect(centerX - bannerW * 0.5f, topY - (bannerH - h) * 0.5f + offY, bannerW, bannerH);
            GUI.DrawTexture(bannerRect, tex, ScaleMode.StretchToFill);
            var textRect = new Rect(bannerRect.x, bannerRect.y + textOffY, bannerRect.width, bannerRect.height);
            GUI.Label(textRect, label, _sectionStyle);
        }
        else
        {
            var bannerRect = new Rect(centerX - w * 0.5f, topY, w, h);
            DrawRoundedFilledRect(bannerRect, sectionHeaderFill, sectionHeaderCornerRadius);
            var textRect = new Rect(bannerRect.x, bannerRect.y + textOffY, bannerRect.width, bannerRect.height);
            GUI.Label(textRect, label, _sectionStyle);
        }

        _sectionStyle.fontSize = prevFS;
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
                GUI.Label(new Rect(rect.x + dx * thickness, rect.y + dy * thickness, rect.width, rect.height), text, style);
            }
        style.normal.textColor = textColor;
        GUI.color = textColor;
        GUI.Label(rect, text, style);
        style.normal.textColor = prevTextColor;
        GUI.color = prev;
    }

    private static Texture2D CreateRadialGlowTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.ARGB32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };
        float center = size * 0.5f, maxDist = size * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = x - center + 0.5f, dy = y - center + 0.5f;
            float d = Mathf.Sqrt(dx * dx + dy * dy) / maxDist;
            float a = Mathf.Clamp01(1f - d);
            a *= a;
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        return tex;
    }

    private static string EnName(string en, string kr) =>
        string.IsNullOrWhiteSpace(en) ? kr : en;
}
