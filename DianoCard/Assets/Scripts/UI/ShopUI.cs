using System;
using System.Collections.Generic;
using DianoCard.Data;
using DianoCard.Game;
using UnityEngine;

/// <summary>
/// 상점 (V2) — 두 단계 흐름.
///   Intro: Shop_first.png 풀스크린, 책상 클릭 → Main 진입
///   Main:  Shop_background.png 풀스크린 (SHOP 사인 / 섹션 라벨이 BG에 베이크인)
///          - 카드 5장 + Card_Below 플린스 + 카드 아래 가격 플라크(Right_UP)
///          - 포션/유물/서비스: 패널 없이 아이콘+이름+가격만 (BG에 라벨 베이크인)
///          - Leaveshop 버튼 (우하단)
///   RemovePicker: 덱 그리드 → 한 장 제거.
/// </summary>
[DefaultExecutionOrder(1000)]
public class ShopUI : MonoBehaviour
{
    private const float RefW = 1280f;
    private const float RefH = 720f;

    private enum View { Intro, Main, RemovePicker }
    private View _view = View.Intro;

    private readonly List<Action> _pending = new();
    private GameState _prevState = GameState.Lobby;

    // ========== 텍스처 ==========
    private Texture2D _introBg;          // Shop_first.png
    private Texture2D _shopBg;           // Shop_background.png — SHOP 사인 + 섹션 라벨 베이크인
    private Texture2D _pricePlaqueTex;   // Right_UP.png — 카드 한 장당 가격 플라크
    private Texture2D _shopTitlePanel;   // Shop_Title_Panel.png — 상단 타이틀 패널 (드롭다운 애니)
    // 슬림 카드 프레임 4종 — 타입별 색
    private Texture2D _frameSlimSummon;  // Frame_Slim_SUMMON.png — 보라
    private Texture2D _frameSlimMagic;   // Frame_Slim_MAGIC.png  — 파랑
    private Texture2D _frameSlimBuff;    // Frame_Slim_BUFF.png   — 초록
    private Texture2D _frameSlimRitual;  // Frame_Slim_RITUAL.png — 빨강
    private Texture2D _leaveShopTex;     // Leaveshop.png — 라벨 포함 보라 버튼
    private Texture2D _moneyIcon;        // money.png — 골드 아이콘
    private Texture2D _potionIcon;       // Potion.jpg — 파란 포션 (default)
    private Texture2D _relicIcon;        // Relics.jpg — 그린 포션 (default 유물)
    private Texture2D _removeCardIcon;   // Remove_card.jpg — 카드 'C' 아이콘

    // ========== Stage 1: Intro ==========
    [Header("Stage 1 — 책상 클릭 영역 (Shop_first 위)")]
    [Tooltip("책상 클릭 영역 (1280x720 가상좌표). 마우스가 들어오면 호버 틴트.")]
    [SerializeField, Range(0f, 1280f)] private float deskClickX = 360f;
    [SerializeField, Range(0f, 720f)]  private float deskClickY = 320f;
    [SerializeField, Range(20f, 1280f)] private float deskClickW = 560f;
    [SerializeField, Range(20f, 720f)]  private float deskClickH = 280f;
    private Rect deskClickRect => new(deskClickX, deskClickY, deskClickW, deskClickH);
    [SerializeField] private bool drawDeskHoverHint = true;
    [SerializeField] private Color deskHoverTint = new(1f, 0.85f, 0.5f, 0.12f);
    [SerializeField] private string deskHoverLabel = "Click the desk";

    // ========== Stage 2: Gold (정적 '300' 위에 덮음) ==========
    [Header("Stage 2 — Gold overlay (BG의 정적 300 위)")]
    [SerializeField, Range(0f, 1280f)] private float goldOverlayCenterX = 1180f;
    [SerializeField, Range(0f, 720f)]  private float goldOverlayCenterY = 52f;
    [SerializeField, Range(20f, 400f)] private float goldOverlayWidth = 130f;
    [SerializeField, Range(16f, 200f)] private float goldOverlayHeight = 52f;
    private Vector2 goldOverlayCenter => new(goldOverlayCenterX, goldOverlayCenterY);
    private Vector2 goldOverlaySize   => new(goldOverlayWidth, goldOverlayHeight);
    [SerializeField, Range(0f, 1f)] private float goldOverlayPatchAlpha = 0.0f; // 정적 텍스트 가림 패치
    [SerializeField] private bool goldOverlayUsePlaque = true;
    [SerializeField, Range(8f, 80f)] private float goldIconSize = 35f;
    [SerializeField, Range(-20f, 40f)] private float goldIconTextGap = 16f;
    [SerializeField, Range(0f, 60f)] private float goldIconLeftPad = 14f;
    [SerializeField, Range(8, 60)] private int goldFontSize = 22;
    [SerializeField] private Color goldTextColor = new(1f, 0.92f, 0.55f);

    [Header("Stage 2 — Shop title panel (위에서 내려옴)")]
    [SerializeField] private bool showTitlePanel = true;
    [SerializeField, Range(0f, 1280f)] private float titlePanelCenterX = 640f;
    [SerializeField, Range(-200f, 720f)] private float titlePanelTargetY = -40f;
    [SerializeField, Range(40f, 800f)] private float titlePanelWidth = 360f;
    [SerializeField, Range(20f, 200f)] private float titlePanelHeight = 180f;
    [Tooltip("드롭다운 애니 지속시간(초). 0이면 즉시.")]
    [SerializeField, Range(0f, 3f)] private float titlePanelDropDuration = 0.8f;
    [Tooltip("시작 위치 — 타깃 Y에서 위로 얼마나 떨어진 곳에서 내려올지(px).")]
    [SerializeField, Range(0f, 600f)] private float titlePanelDropFrom = 220f;

    // ========== Stage 2: Cards row ==========
    [Header("Stage 2 — Cards row (중앙 5장)")]
    [Tooltip("카드 행 중심 X (1280 가상폭 기준, 640=중앙).")]
    [SerializeField, Range(0f, 1280f)] private float cardsCenterX = 700f;
    [Tooltip("카드 행 중심 Y (720 가상높이 기준).")]
    [SerializeField, Range(60f, 600f)] private float cardsCenterY = 280f;
    [SerializeField, Range(60f, 280f)] private float cardWidth = 155f;
    [SerializeField, Range(80f, 400f)] private float cardHeight = 240f;
    private Vector2 cardSize => new(cardWidth, cardHeight);
    [SerializeField, Range(0f, 80f)] private float cardSpacing = 25f;
    [SerializeField, Range(1f, 1.3f)] private float cardHoverScale = 1.04f;

    [Header("Stage 2 — Slim shop card (Frame_Slim_*.png 텍스처 사용)")]
    [Tooltip("프레임 텍스처 가로 배율 (1=카드 가로 그대로, <1=줄임). 카드 중심으로 스케일.")]
    [SerializeField, Range(0.5f, 1.2f)] private float slimFrameScaleX = 1.0f;
    [Tooltip("프레임 텍스처 세로 배율 (1=카드 세로 그대로, <1=줄임). 카드 중심으로 스케일.")]
    [SerializeField, Range(0.5f, 1.2f)] private float slimFrameScaleY = 1.0f;
    [Tooltip("프레임 텍스처 가로/세로 통합 오프셋(px) — Y 위로/아래로 전체 이동. 0=중심.")]
    [SerializeField, Range(-40f, 40f)] private float slimFrameOffsetY = 0f;
    [Tooltip("프레임 stroke 좌우 두께 (px) — 카드 아트 영역 좌우 마진.")]
    [SerializeField, Range(0f, 40f)] private float framePaddingX = 4f;
    [Tooltip("프레임 stroke 상하 두께 (px) — 카드 아트 영역 상하 마진.")]
    [SerializeField, Range(0f, 40f)] private float framePaddingY = 9f;
    [SerializeField, Range(0f, 60f)] private float slimTopBandHeight = 24f;
    [SerializeField, Range(0f, 60f)] private float slimBottomBandHeight = 28f;
    [Tooltip("상/하 띠 모서리 둥글기.")]
    [SerializeField, Range(0, 15)] private int slimBandCornerRadius = 3;
    [SerializeField, Range(0f, 8f)] private float slimArtPadding = 4f;
    [SerializeField, Range(0f, 1f)] private float slimBandAlpha = 0.92f;
    [Tooltip("상하 띠 색 = Lerp(어두운 베이스, 타입색, 이 값).")]
    [SerializeField, Range(0f, 1f)] private float slimBandTintAmount = 0.25f;
    [SerializeField] private Color slimBandDarkBase = new(0.05f, 0.04f, 0.08f, 1f);
    [SerializeField] private Color slimInnerColor = new(0.07f, 0.05f, 0.10f, 0.98f);
    [SerializeField, Range(8, 28)] private int slimTypeFontSize = 12;
    [SerializeField, Range(8, 28)] private int slimNameFontSize = 10;

    [Header("Stage 2 — Slim card cost badge (좌상단 코스트)")]
    [SerializeField, Range(0f, 60f)] private float slimCostSize = 28f;
    [SerializeField, Range(0f, 30f)] private float slimCostOffset = 4f;
    [SerializeField, Range(0, 40)] private int slimCostCornerRadius = 14;
    [Tooltip("코스트 배지 외곽 트림 두께.")]
    [SerializeField, Range(0f, 4f)] private float slimCostBorderThickness = 1.5f;
    [SerializeField, Range(8, 28)] private int slimCostFontSize = 16;

    [Header("Stage 2 — Slim card type colors (cardType 기준 — 프레임 텍스처와 동일 톤)")]
    [Tooltip("SUMMON — 보라. Frame_Slim_SUMMON.png 와 매칭.")]
    [SerializeField] private Color slimColorSummon    = new(0.55f, 0.28f, 0.75f, 1f);
    [Tooltip("MAGIC — 파랑. Frame_Slim_MAGIC.png 와 매칭.")]
    [SerializeField] private Color slimColorMagic     = new(0.22f, 0.42f, 0.85f, 1f);
    [Tooltip("BUFF — 초록. Frame_Slim_BUFF.png 와 매칭.")]
    [SerializeField] private Color slimColorBuff      = new(0.18f, 0.62f, 0.40f, 1f);
    [Tooltip("RITUAL — 빨강. Frame_Slim_RITUAL.png 와 매칭.")]
    [SerializeField] private Color slimColorRitual    = new(0.65f, 0.18f, 0.22f, 1f);
    [Tooltip("UTILITY (Frame_Slim_SUMMON 폴백 사용) — 보라.")]
    [SerializeField] private Color slimColorUtility   = new(0.55f, 0.28f, 0.75f, 1f);
    [Tooltip("타입 매칭 안 될 때 기본 — 차콜.")]
    [SerializeField] private Color slimColorDefault   = new(0.30f, 0.26f, 0.28f, 1f);

    [Header("Stage 2 — Card price plaque (각 카드 아래, Right_UP)")]
    [SerializeField, Range(40f, 240f)] private float plaqueWidth = 70f;
    [SerializeField, Range(20f, 100f)] private float plaqueHeight = 38f;
    private Vector2 plaqueSize => new(plaqueWidth, plaqueHeight);
    [SerializeField, Range(-30f, 80f)] private float plaqueYOffset = -11f;
    [SerializeField, Range(8, 36)] private int plaqueFontSize = 16;
    [SerializeField, Range(8f, 48f)] private float plaqueIconSize = 18f;
    [Tooltip("플라크 좌측 ~ 코인 아이콘 (px).")]
    [SerializeField, Range(0f, 60f)] private float plaqueIconLeftPad = 8f;
    [Tooltip("아이콘 ~ 가격 숫자 시작 (px).")]
    [SerializeField, Range(0f, 60f)] private float plaqueTextLeftGap = 10f;
    [SerializeField] private Color plaqueAffordColor = new(1f, 0.92f, 0.55f);
    [SerializeField] private Color plaqueExpensiveColor = new(1f, 0.55f, 0.55f);

    [Header("Stage 2 — Potion price plaque (각 포션 옆, Right_UP)")]
    [SerializeField] private bool potionPriceUsePlaque = true;
    [SerializeField, Range(40f, 240f)] private float potionPlaqueWidth = 70f;
    [SerializeField, Range(20f, 100f)] private float potionPlaqueHeight = 25f;
    [SerializeField, Range(8, 36)] private int potionPlaqueFontSize = 16;
    [SerializeField, Range(8f, 48f)] private float potionPlaqueIconSize = 18f;
    [Tooltip("플라크 좌측 ~ 코인 아이콘 (px).")]
    [SerializeField, Range(0f, 60f)] private float potionPlaqueIconLeftPad = 8f;
    [Tooltip("아이콘 ~ 가격 숫자 시작 (px).")]
    [SerializeField, Range(0f, 60f)] private float potionPlaqueTextGap = 10f;

    // ========== Stage 2: Per-slot Potions (3) ==========
    [Header("Stage 2 — Potion slot 0")]
    [SerializeField, Range(0f, 1280f)] private float potion0X = 180f;
    [SerializeField, Range(0f, 720f)]  private float potion0Y = 495f;
    [SerializeField, Range(60f, 800f)] private float potion0W = 348f;
    [SerializeField, Range(20f, 120f)] private float potion0H = 50f;
    [SerializeField, Range(16f, 100f)] private float potion0IconSize = 42f;
    [SerializeField, Range(0f, 1280f)] private float potion0NameX = 238f;
    [SerializeField, Range(0f, 720f)]  private float potion0NameY = 495f;
    [SerializeField, Range(0f, 1280f)] private float potion0PriceX = 330f;
    [SerializeField, Range(0f, 720f)]  private float potion0PriceY = 505f;

    [Header("Stage 2 — Potion slot 1")]
    [SerializeField, Range(0f, 1280f)] private float potion1X = 160f;
    [SerializeField, Range(0f, 720f)]  private float potion1Y = 555f;
    [SerializeField, Range(60f, 800f)] private float potion1W = 348f;
    [SerializeField, Range(20f, 120f)] private float potion1H = 50f;
    [SerializeField, Range(16f, 100f)] private float potion1IconSize = 42f;
    [SerializeField, Range(0f, 1280f)] private float potion1NameX = 218f;
    [SerializeField, Range(0f, 720f)]  private float potion1NameY = 555f;
    [SerializeField, Range(0f, 1280f)] private float potion1PriceX = 310f;
    [SerializeField, Range(0f, 720f)]  private float potion1PriceY = 565f;

    [Header("Stage 2 — Potion slot 2")]
    [SerializeField, Range(0f, 1280f)] private float potion2X = 140f;
    [SerializeField, Range(0f, 720f)]  private float potion2Y = 615f;
    [SerializeField, Range(60f, 800f)] private float potion2W = 348f;
    [SerializeField, Range(20f, 120f)] private float potion2H = 50f;
    [SerializeField, Range(16f, 100f)] private float potion2IconSize = 42f;
    [SerializeField, Range(0f, 1280f)] private float potion2NameX = 198f;
    [SerializeField, Range(0f, 720f)]  private float potion2NameY = 615f;
    [SerializeField, Range(0f, 1280f)] private float potion2PriceX = 290f;
    [SerializeField, Range(0f, 720f)]  private float potion2PriceY = 630f;

    // ========== Stage 2: Per-cell Relics (6, 2x3 grid) ==========
    [Header("Stage 2 — Relic cell 0 (top-left)")]
    [SerializeField, Range(0f, 1280f)] private float relic0X = 470f;
    [SerializeField, Range(0f, 720f)]  private float relic0Y = 490f;
    [SerializeField, Range(40f, 200f)] private float relic0W = 109f;
    [SerializeField, Range(30f, 120f)] private float relic0H = 60f;
    [SerializeField, Range(16f, 100f)] private float relic0IconSize = 50f;
    [SerializeField, Range(0f, 1280f)] private float relic0PriceX = 470f;
    [SerializeField, Range(0f, 720f)]  private float relic0PriceY = 552f;

    [Header("Stage 2 — Relic cell 1 (top-mid)")]
    [SerializeField, Range(0f, 1280f)] private float relic1X = 550f;
    [SerializeField, Range(0f, 720f)]  private float relic1Y = 490f;
    [SerializeField, Range(40f, 200f)] private float relic1W = 109f;
    [SerializeField, Range(30f, 120f)] private float relic1H = 60f;
    [SerializeField, Range(16f, 100f)] private float relic1IconSize = 50f;
    [SerializeField, Range(0f, 1280f)] private float relic1PriceX = 550f;
    [SerializeField, Range(0f, 720f)]  private float relic1PriceY = 552f;

    [Header("Stage 2 — Relic cell 2 (top-right)")]
    [SerializeField, Range(0f, 1280f)] private float relic2X = 630f;
    [SerializeField, Range(0f, 720f)]  private float relic2Y = 490f;
    [SerializeField, Range(40f, 200f)] private float relic2W = 109f;
    [SerializeField, Range(30f, 120f)] private float relic2H = 60f;
    [SerializeField, Range(16f, 100f)] private float relic2IconSize = 50f;
    [SerializeField, Range(0f, 1280f)] private float relic2PriceX = 630f;
    [SerializeField, Range(0f, 720f)]  private float relic2PriceY = 552f;

    [Header("Stage 2 — Relic cell 3 (bot-left)")]
    [SerializeField, Range(0f, 1280f)] private float relic3X = 456f;
    [SerializeField, Range(0f, 720f)]  private float relic3Y = 580f;
    [SerializeField, Range(40f, 200f)] private float relic3W = 109f;
    [SerializeField, Range(30f, 120f)] private float relic3H = 60f;
    [SerializeField, Range(16f, 100f)] private float relic3IconSize = 50f;
    [SerializeField, Range(0f, 1280f)] private float relic3PriceX = 456f;
    [SerializeField, Range(0f, 720f)]  private float relic3PriceY = 642f;

    [Header("Stage 2 — Relic cell 4 (bot-mid)")]
    [SerializeField, Range(0f, 1280f)] private float relic4X = 536f;
    [SerializeField, Range(0f, 720f)]  private float relic4Y = 580f;
    [SerializeField, Range(40f, 200f)] private float relic4W = 109f;
    [SerializeField, Range(30f, 120f)] private float relic4H = 60f;
    [SerializeField, Range(16f, 100f)] private float relic4IconSize = 50f;
    [SerializeField, Range(0f, 1280f)] private float relic4PriceX = 536f;
    [SerializeField, Range(0f, 720f)]  private float relic4PriceY = 642f;

    [Header("Stage 2 — Relic cell 5 (bot-right)")]
    [SerializeField, Range(0f, 1280f)] private float relic5X = 616f;
    [SerializeField, Range(0f, 720f)]  private float relic5Y = 580f;
    [SerializeField, Range(40f, 200f)] private float relic5W = 109f;
    [SerializeField, Range(30f, 120f)] private float relic5H = 60f;
    [SerializeField, Range(16f, 100f)] private float relic5IconSize = 50f;
    [SerializeField, Range(0f, 1280f)] private float relic5PriceX = 616f;
    [SerializeField, Range(0f, 720f)]  private float relic5PriceY = 642f;
    [SerializeField, Range(0f, 20f)] private float relicsIconLeftPad = 4f;

    [Header("Stage 2 — Relic price plaque (각 유물 아래, Right_UP)")]
    [SerializeField] private bool relicPriceUsePlaque = true;
    [SerializeField, Range(40f, 200f)] private float relicPlaqueWidth = 64f;
    [SerializeField, Range(18f, 80f)] private float relicPlaqueHeight = 25f;
    [SerializeField, Range(0f, 30f)] private float relicPlaqueIconLeftPad = 5f;
    [Tooltip("아이콘 ~ 가격 숫자 시작 (px).")]
    [SerializeField, Range(0f, 30f)] private float relicPlaqueTextGap = 8f;
    [SerializeField, Range(8f, 36f)] private float relicPlaqueIconSize = 16f;
    [SerializeField, Range(8, 30)] private int relicPlaqueFontSize = 14;

    [Header("Stage 2 — Services section rect")]
    [SerializeField, Range(0f, 1280f)] private float servicesX = 730f;
    [SerializeField, Range(0f, 720f)]  private float servicesY = 510f;
    [SerializeField, Range(60f, 1280f)] private float servicesW = 220f;
    [SerializeField, Range(40f, 720f)]  private float servicesH = 130f;
    private Rect servicesSectionRect => new(servicesX, servicesY, servicesW, servicesH);

    [Header("Stage 2 — Services section content (내부 오프셋)")]
    [SerializeField, Range(0f, 60f)] private float servicesContentPadX = 0f;
    [SerializeField, Range(0f, 80f)] private float servicesContentTopY = 30f;

    [Header("Stage 2 — Service icon (카드 리무브)")]
    [SerializeField, Range(20f, 120f)] private float serviceIconSize = 74f;

    [Header("Stage 2 — Service name (카드 리무브 텍스트)")]
    [SerializeField] private bool serviceNameOverride = true;
    [SerializeField, Range(0f, 1280f)] private float serviceNameX = 798f;
    [SerializeField, Range(0f, 720f)]  private float serviceNameY = 540f;
    [SerializeField, Range(40f, 400f)] private float serviceNameW = 110f;
    [SerializeField, Range(20f, 120f)] private float serviceNameH = 50f;

    [Header("Stage 2 — Service price plaque (카드 리무브 밑, Right_UP)")]
    [SerializeField] private bool servicePriceUsePlaque = true;
    [SerializeField, Range(40f, 240f)] private float servicePlaqueWidth = 65f;
    [SerializeField, Range(20f, 100f)] private float servicePlaqueHeight = 34f;
    [SerializeField, Range(-100f, 100f)] private float servicePlaqueXOffset = -17.5f;
    [SerializeField, Range(-30f, 100f)] private float servicePlaqueYOffset = 10f;
    [SerializeField, Range(8, 36)] private int servicePlaqueFontSize = 16;
    [SerializeField, Range(8f, 48f)] private float servicePlaqueIconSize = 18f;
    [SerializeField, Range(0f, 60f)] private float servicePlaqueIconLeftPad = 8f;
    [SerializeField, Range(0f, 60f)] private float servicePlaqueTextGap = 10f;

    [Header("Stage 2 — Item row")]
    [SerializeField, Range(20f, 120f)] private float itemRowHeight = 50f;
    [SerializeField, Range(16f, 100f)] private float itemIconSize = 42f;
    [SerializeField, Range(8, 36)] private int itemNameFontSize = 14;
    [SerializeField, Range(8, 36)] private int itemPriceFontSize = 15;
    [Tooltip("가격 숫자 앞에 들어가는 코인 아이콘 크기 (px). 0이면 아이콘 안 그림.")]
    [SerializeField, Range(0f, 32f)] private float itemPriceIconSize = 18f;
    [Tooltip("코인 아이콘 ~ 가격 숫자 사이 간격 (px).")]
    [SerializeField, Range(0f, 16f)] private float itemPriceIconGap = 4f;
    [SerializeField, Range(1f, 1.2f)] private float itemHoverScale = 1.04f;
    [Tooltip("plaque에 마우스 올렸을 때 plaque 자체 확대 배율 (버튼 피드백).")]
    [SerializeField, Range(1f, 1.3f)] private float plaqueHoverScale = 1.12f;
    [SerializeField] private Color itemNameColor = new(0.95f, 0.92f, 0.78f);

    [Header("Stage 2 — Item row internal offsets (각 줄 내부)")]
    [Tooltip("줄 좌측 ~ 아이콘 시작 거리 (px).")]
    [SerializeField, Range(0f, 60f)] private float itemIconLeftPad = 4f;
    [Tooltip("아이콘 ~ 이름 라벨 시작 거리 (px).")]
    [SerializeField, Range(0f, 60f)] private float itemNameLeftGap = 12f;
    [Tooltip("이름 라벨 우측 예약 폭 (가격 자리, px).")]
    [SerializeField, Range(20f, 200f)] private float itemNameRightReserve = 70f;
    [Tooltip("줄 오른쪽 끝 ~ 가격 영역 좌측 거리 (px).")]
    [SerializeField, Range(20f, 200f)] private float itemPriceRightOffset = 60f;
    [Tooltip("가격 영역 가로 폭 (px).")]
    [SerializeField, Range(20f, 160f)] private float itemPriceWidth = 56f;

    [Header("Stage 2 — Leave shop button")]
    [SerializeField, Range(0f, 1280f)] private float leaveShopCenterX = 1135f;
    [SerializeField, Range(0f, 720f)]  private float leaveShopCenterY = 656f;
    [SerializeField, Range(60f, 500f)] private float leaveShopWidth = 231f;
    [SerializeField, Range(30f, 200f)] private float leaveShopHeight = 79.8f;
    private Vector2 leaveShopCenter => new(leaveShopCenterX, leaveShopCenterY);
    private Vector2 leaveShopSize   => new(leaveShopWidth, leaveShopHeight);
    [SerializeField, Range(1f, 1.2f)] private float leaveShopHoverScale = 1.05f;

    // ========== Tooltip ==========
    [Header("Hover tooltip")]
    [SerializeField, Range(160f, 400f)] private float tooltipWidth = 240f;
    [SerializeField] private Color tooltipFill = new(0.05f, 0.07f, 0.10f, 0.95f);
    [SerializeField] private Color tooltipBorder = new(0.80f, 0.62f, 0.30f, 0.9f);
    [SerializeField, Range(0, 16)] private int tooltipCorner = 6;
    [SerializeField, Range(8, 20)] private int tooltipTitleFont = 14;
    [SerializeField, Range(8, 18)] private int tooltipBodyFont = 12;

    private string _tooltipTitle, _tooltipBody;

    // ========== runtime ==========
    private Font _displayFont;
    private GUIStyle _goldStyle, _itemNameStyle, _itemPriceStyle, _plaqueStyle, _potionPriceStyle, _relicPriceStyle, _servicePriceStyle, _leaveStyle, _hintStyle, _tooltipTitleStyle, _tooltipBodyStyle, _soldStyle;
    private GUIStyle _slimTypeStyle, _slimNameStyle, _slimCostStyle;
    private bool _stylesReady;
    private bool _loaded;
    private BattleUI _battleUI;
    private float _removeScrollY;
    private readonly Dictionary<string, Texture2D> _slimArtCache = new();
    private float _mainEnterTime = -1f;

    void Update()
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null) return;

        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.f8Key.wasPressedThisFrame) gsm.Cheat_EnterShop();

        // Shop에 처음 들어올 때마다 Intro로 초기화
        if (_prevState != GameState.Shop && gsm.State == GameState.Shop)
        {
            _view = View.Intro;
            _removeScrollY = 0f;
        }
        _prevState = gsm.State;

        if (_pending.Count > 0)
        {
            var snap = new List<Action>(_pending);
            _pending.Clear();
            foreach (var a in snap) a?.Invoke();
        }
    }

    void OnGUI()
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null || gsm.State != GameState.Shop) return;
        var run = gsm.CurrentRun;
        var shop = gsm.CurrentShop;
        if (run == null || shop == null) return;

        EnsureLoaded();
        EnsureStyles();
        UpdateStyleSizes();

        GUI.depth = 0;
        float scale = Mathf.Min(Screen.width / RefW, Screen.height / RefH);
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

        switch (_view)
        {
            case View.Intro: DrawIntro(); break;
            case View.Main: DrawMain(gsm, run, shop); break;
            case View.RemovePicker: DrawRemovePicker(gsm, run); break;
        }
    }

    // =========================================================
    // Stage 1 — Intro: Shop_first.png + 책상 클릭
    // =========================================================

    private void DrawIntro()
    {
        if (_introBg != null)
            GUI.DrawTexture(new Rect(0, 0, RefW, RefH), _introBg, ScaleMode.ScaleAndCrop);
        else
            DrawFilledRect(new Rect(0, 0, RefW, RefH), new Color(0.04f, 0.03f, 0.05f, 1f));

        var ev = Event.current;
        bool hover = deskClickRect.Contains(ev.mousePosition);

        if (drawDeskHoverHint && hover)
        {
            DrawFilledRect(deskClickRect, deskHoverTint);
            GUI.Label(new Rect(0, RefH - 76f, RefW, 28f), deskHoverLabel, _hintStyle);
        }

        if (GUI.Button(deskClickRect, GUIContent.none, GUIStyle.none))
            _pending.Add(() => { _view = View.Main; _mainEnterTime = Time.realtimeSinceStartup; });
    }

    // =========================================================
    // Stage 2 — Main shop (Shop_background.png + 동적 UI)
    // =========================================================

    private void DrawMain(GameStateManager gsm, RunState run, ShopState shop)
    {
        _tooltipTitle = null; _tooltipBody = null;

        if (_shopBg != null)
            GUI.DrawTexture(new Rect(0, 0, RefW, RefH), _shopBg, ScaleMode.ScaleAndCrop);
        else
            DrawFilledRect(new Rect(0, 0, RefW, RefH), new Color(0.04f, 0.03f, 0.05f, 1f));

        DrawTitlePanel();
        DrawGoldOverlay(run);
        DrawCardsRow(shop, run);
        DrawPotionsSection(shop, run);
        DrawRelicsSection(shop, run);
        DrawServicesSection(shop, run);
        DrawLeaveButton(gsm);
        DrawTooltip();
    }

    private void DrawTitlePanel()
    {
        if (!showTitlePanel || _shopTitlePanel == null) return;

        // 진입 후 경과시간 → ease-out cubic
        float t = _mainEnterTime > 0f
            ? Mathf.Clamp01((Time.realtimeSinceStartup - _mainEnterTime) / Mathf.Max(0.001f, titlePanelDropDuration))
            : 1f;
        float ease = 1f - Mathf.Pow(1f - t, 3f);
        float yOffset = (1f - ease) * titlePanelDropFrom;

        var rect = new Rect(
            titlePanelCenterX - titlePanelWidth * 0.5f,
            titlePanelTargetY - yOffset,
            titlePanelWidth, titlePanelHeight);
        GUI.DrawTexture(rect, _shopTitlePanel, ScaleMode.StretchToFill);
    }

    private void DrawGoldOverlay(RunState run)
    {
        var rect = new Rect(
            goldOverlayCenter.x - goldOverlaySize.x * 0.5f,
            goldOverlayCenter.y - goldOverlaySize.y * 0.5f,
            goldOverlaySize.x, goldOverlaySize.y);

        // 정적 "300" 가림 패치 — 기본은 투명. BG 텍스트가 거슬리면 alpha 올림.
        if (goldOverlayPatchAlpha > 0f)
            DrawFilledRect(rect, new Color(0.05f, 0.04f, 0.08f, goldOverlayPatchAlpha));

        // plaque 백킹 (Right_UP)
        if (goldOverlayUsePlaque && _pricePlaqueTex != null)
            GUI.DrawTexture(rect, _pricePlaqueTex, ScaleMode.StretchToFill);

        if (_moneyIcon != null)
        {
            float iy = rect.y + (rect.height - goldIconSize) * 0.5f;
            GUI.DrawTexture(new Rect(rect.x + goldIconLeftPad, iy, goldIconSize, goldIconSize), _moneyIcon, ScaleMode.ScaleToFit);
        }

        float textX = rect.x + goldIconLeftPad + (_moneyIcon != null ? goldIconSize + goldIconTextGap : 0f);
        var textRect = new Rect(textX, rect.y, rect.x + rect.width - textX, rect.height);
        GUI.Label(textRect, run.gold.ToString(), _goldStyle);
    }

    private void DrawCardsRow(ShopState shop, RunState run)
    {
        int n = shop.cards.Count;
        if (n == 0) return;

        float totalW = n * cardSize.x + (n - 1) * cardSpacing;
        float startX = cardsCenterX - totalW * 0.5f;
        float topY = cardsCenterY - cardSize.y * 0.5f;

        // 호버 인덱스 — plaque 영역에서만 감지
        int hovered = -1;
        for (int i = 0; i < n; i++)
        {
            var r = new Rect(startX + i * (cardSize.x + cardSpacing), topY, cardSize.x, cardSize.y);
            var pr = GetCardPlaqueRect(r);
            if (!shop.cards[i].sold && pr.Contains(Event.current.mousePosition)) { hovered = i; break; }
        }

        if (_battleUI == null) _battleUI = UnityEngine.Object.FindFirstObjectByType<BattleUI>();

        for (int i = 0; i < n; i++)
        {
            var entry = shop.cards[i];
            var rect = new Rect(startX + i * (cardSize.x + cardSpacing), topY, cardSize.x, cardSize.y);
            bool plaqueHover = (i == hovered);

            DrawSlimShopCard(rect, entry.card);

            if (entry.sold)
            {
                var prev = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.55f);
                GUI.DrawTexture(rect, Texture2D.whiteTexture);
                GUI.color = prev;
                GUI.Label(new Rect(rect.x, rect.y + rect.height * 0.42f, rect.width, rect.height * 0.16f),
                          DataManager.Instance.GetUIString("shop.sold_out"), _soldStyle);
                continue;
            }

            // plaque hover 시 plaque만 살짝 확대 (버튼 피드백). 카드 본체는 고정.
            DrawCardPricePlaque(rect, entry.price, run.gold >= entry.price, plaqueHover);

            var plaqueClickRect = GetCardPlaqueRect(rect);

            // 카드 위 또는 plaque 위 hover 시 툴팁
            bool cardHover = rect.Contains(Event.current.mousePosition) || plaqueClickRect.Contains(Event.current.mousePosition);
            if (cardHover)
            {
                _tooltipTitle = EnName(entry.card.nameEn, entry.card.nameKr);
                _tooltipBody = BuildCardTooltipBody(entry.card);
            }

            // 구매는 plaque 영역에서만
            if (GUI.Button(plaqueClickRect, GUIContent.none, GUIStyle.none))
            {
                int captured = i;
                _pending.Add(() => GameStateManager.Instance?.BuyShopCard(shop.cards[captured]));
            }
        }
    }

    private string BuildCardTooltipBody(CardData c)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("Cost ").Append(c.cost);
        if (c.cardType != CardType.NONE) sb.Append("   ").Append(c.cardType);
        sb.Append("   ").Append(c.rarity);
        if (c.attack > 0) sb.Append("\nATK ").Append(c.attack);
        if (c.hp > 0)     sb.Append("   HP ").Append(c.hp);
        if (!string.IsNullOrEmpty(c.description)) sb.Append("\n").Append(c.description);
        return sb.ToString();
    }

    private string BuildPotionTooltipBody(PotionData p)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(p.potionType).Append("   ").Append(p.rarity);
        if (p.value > 0) sb.Append("\nValue ").Append(p.value);
        if (!string.IsNullOrEmpty(p.description)) sb.Append("\n").Append(p.description);
        return sb.ToString();
    }

    private string BuildRelicTooltipBody(RelicData r)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(r.rarity).Append("   ").Append(r.trigger);
        if (!string.IsNullOrEmpty(r.description)) sb.Append("\n").Append(r.description);
        return sb.ToString();
    }

    private Rect GetCardPlaqueRect(Rect cardRect) => new Rect(
        cardRect.center.x - plaqueSize.x * 0.5f,
        cardRect.yMax + plaqueYOffset,
        plaqueSize.x, plaqueSize.y);

    // 상점 전용 슬림 카드 — Frame_Slim_*.png 텍스처를 프레임으로 사용.
    private void DrawSlimShopCard(Rect rect, CardData card)
    {
        Color trim = GetSlimTypeColor(card);
        Texture2D frame = GetSlimFrameTexture(card);

        // 안쪽 패널 영역 — 프레임 stroke 안쪽
        var inner = new Rect(rect.x + framePaddingX, rect.y + framePaddingY,
                             rect.width - framePaddingX * 2f, rect.height - framePaddingY * 2f);

        // 1) 안쪽 어두운 패널 (아트 뒤 배경)
        DrawFilledRect(inner, slimInnerColor);

        // 4) 아트 영역 — 상하 띠 사이
        float artTopY  = inner.y + slimTopBandHeight;
        float artBotY  = inner.yMax - slimBottomBandHeight;
        var artRect = new Rect(inner.x + slimArtPadding, artTopY,
                               inner.width - slimArtPadding * 2f, Mathf.Max(8f, artBotY - artTopY));
        var art = GetSlimCardArt(card);
        if (art != null)
            GUI.DrawTexture(artRect, art, ScaleMode.ScaleAndCrop);
        else
            DrawFilledRect(artRect, new Color(0.04f, 0.03f, 0.06f, 1f));

        // 5) 띠 색 — 어두운 베이스에서 트림색으로 살짝 lerp + 알파 적용
        Color bandColor = Color.Lerp(slimBandDarkBase, trim, slimBandTintAmount);
        bandColor.a = slimBandAlpha;

        // 상단 띠 — 타입 라벨
        var topBand = new Rect(inner.x, inner.y, inner.width, slimTopBandHeight);
        DrawRoundedFilledRect(topBand, bandColor, slimBandCornerRadius);
        string typeLabel = (card.subType != CardSubType.NONE)
            ? card.subType.ToString()
            : card.cardType.ToString();
        GUI.Label(topBand, typeLabel, _slimTypeStyle);

        // 하단 띠 — 이름 (긴 이름은 자동으로 폰트 축소)
        var botBand = new Rect(inner.x, inner.yMax - slimBottomBandHeight, inner.width, slimBottomBandHeight);
        DrawRoundedFilledRect(botBand, bandColor, slimBandCornerRadius);
        string name = EnName(card.nameEn, card.nameKr);
        if (!string.IsNullOrEmpty(name)) name = name.ToUpperInvariant();
        if (!string.IsNullOrEmpty(name))
        {
            int origFS = _slimNameStyle.fontSize;
            float maxW = botBand.width - 8f;
            int fs = origFS;
            // 폰트 축소 — 폭 들어갈 때까지 (최소 8까지)
            for (int i = 0; i < 12 && fs > 8; i++)
            {
                _slimNameStyle.fontSize = fs;
                var size = _slimNameStyle.CalcSize(new GUIContent(name));
                if (size.x <= maxW) break;
                fs--;
            }
            GUI.Label(botBand, name, _slimNameStyle);
            _slimNameStyle.fontSize = origFS;
        }

        // 6) 프레임 텍스처 — 가장 위 (띠 가장자리·아트 가장자리 깔끔하게 마감). 카드 중심으로 스케일.
        if (frame != null)
        {
            float fw = rect.width * slimFrameScaleX;
            float fh = rect.height * slimFrameScaleY;
            var fr = new Rect(rect.center.x - fw * 0.5f,
                              rect.center.y - fh * 0.5f + slimFrameOffsetY,
                              fw, fh);
            GUI.DrawTexture(fr, frame, ScaleMode.StretchToFill);
        }

        // 7) 좌상단 코스트 — 트림색 외곽링 + 어두운 안쪽 (기본은 원형)
        if (card.cost >= 0)
        {
            var cb = new Rect(rect.x + slimCostOffset, rect.y + slimCostOffset, slimCostSize, slimCostSize);
            DrawRoundedFilledRect(cb, trim, slimCostCornerRadius);
            float ct = slimCostBorderThickness;
            if (ct > 0f)
            {
                var cbInner = new Rect(cb.x + ct, cb.y + ct, cb.width - ct * 2f, cb.height - ct * 2f);
                DrawRoundedFilledRect(cbInner, new Color(0.10f, 0.07f, 0.05f, 0.95f),
                                      Mathf.Max(0, slimCostCornerRadius - Mathf.RoundToInt(ct)));
            }
            GUI.Label(cb, card.cost.ToString(), _slimCostStyle);
        }
    }

    // cardType 으로 슬림 카드 프레임 텍스처 선택 (UTILITY/NONE 은 SUMMON 보라로 폴백, 기존 게임 룰).
    private Texture2D GetSlimFrameTexture(CardData card)
    {
        if (card == null) return _frameSlimSummon;
        return card.cardType switch
        {
            CardType.SUMMON  => _frameSlimSummon,
            CardType.MAGIC   => _frameSlimMagic,
            CardType.BUFF    => _frameSlimBuff,
            CardType.RITUAL  => _frameSlimRitual,
            _                => _frameSlimSummon,
        };
    }

    // cardType 으로만 — 프레임 텍스처 매칭. subType 은 무시 (프레임이 cardType 기준이라 색이 둘이 되면 깨짐).
    private Color GetSlimTypeColor(CardData card)
    {
        if (card == null) return slimColorDefault;
        return card.cardType switch
        {
            CardType.SUMMON  => slimColorSummon,
            CardType.MAGIC   => slimColorMagic,
            CardType.BUFF    => slimColorBuff,
            CardType.RITUAL  => slimColorRitual,
            CardType.UTILITY => slimColorUtility,
            _                => slimColorDefault,
        };
    }

    private Texture2D GetSlimCardArt(CardData card)
    {
        if (card == null || string.IsNullOrEmpty(card.id)) return null;
        if (_slimArtCache.TryGetValue(card.id, out var cached)) return cached;

        Texture2D tex = null;
        string filename = string.IsNullOrEmpty(card.image)
            ? card.id
            : System.IO.Path.GetFileNameWithoutExtension(card.image);

        // SUMMON은 완성본 Dino/ 우선, 없으면 Summon/ 으로 폴백 (BattleUI 동일 규칙)
        if (card.cardType == CardType.SUMMON)
            tex = Resources.Load<Texture2D>($"CardArt/Dino/{filename}");
        if (tex == null)
        {
            string subfolder = card.cardType switch
            {
                CardType.SUMMON => "Summon",
                CardType.MAGIC  => "Spell",
                _               => "Utility", // BUFF / UTILITY / RITUAL / NONE
            };
            tex = Resources.Load<Texture2D>($"CardArt/{subfolder}/{filename}");
        }
        _slimArtCache[card.id] = tex; // null도 캐시 (반복 로드 방지)
        return tex;
    }

    private void DrawCardPricePlaque(Rect cardRect, int price, bool canAfford, bool hover = false)
    {
        var pr = new Rect(
            cardRect.center.x - plaqueSize.x * 0.5f,
            cardRect.yMax + plaqueYOffset,
            plaqueSize.x, plaqueSize.y);

        // hover 시 plaque + 내부 모든 요소가 가운데 기준으로 함께 확대
        Matrix4x4 prevMatrix = GUI.matrix;
        if (hover) GUIUtility.ScaleAroundPivot(Vector2.one * plaqueHoverScale, pr.center);

        if (_pricePlaqueTex != null) GUI.DrawTexture(pr, _pricePlaqueTex, ScaleMode.StretchToFill);
        else DrawRoundedFilledRect(pr, new Color(0.08f, 0.06f, 0.05f, 0.92f), 5);

        if (_moneyIcon != null)
        {
            float iy = pr.y + (pr.height - plaqueIconSize) * 0.5f;
            GUI.DrawTexture(new Rect(pr.x + plaqueIconLeftPad, iy, plaqueIconSize, plaqueIconSize), _moneyIcon, ScaleMode.ScaleToFit);
        }

        float textX = pr.x + plaqueIconLeftPad + plaqueIconSize + plaqueTextLeftGap;
        var textRect = new Rect(textX, pr.y, pr.xMax - textX - 4f, pr.height);
        var prevC = _plaqueStyle.normal.textColor;
        _plaqueStyle.normal.textColor = canAfford ? plaqueAffordColor : plaqueExpensiveColor;
        LockStateColors(_plaqueStyle);
        GUI.Label(textRect, price.ToString(), _plaqueStyle);
        _plaqueStyle.normal.textColor = prevC;
        LockStateColors(_plaqueStyle);

        GUI.matrix = prevMatrix;
    }

    // 슬롯 인덱스(0/1/2)별 Rect / IconSize 헬퍼.
    private Rect GetPotionSlotRect(int i) => i switch
    {
        0 => new Rect(potion0X, potion0Y, potion0W, potion0H),
        1 => new Rect(potion1X, potion1Y, potion1W, potion1H),
        2 => new Rect(potion2X, potion2Y, potion2W, potion2H),
        _ => new Rect(potion0X, potion0Y, potion0W, potion0H),
    };
    private float GetPotionSlotIconSize(int i) => i switch
    {
        0 => potion0IconSize,
        1 => potion1IconSize,
        2 => potion2IconSize,
        _ => potion0IconSize,
    };
    private Vector2 GetPotionNamePos(int i) => i switch
    {
        0 => new Vector2(potion0NameX, potion0NameY),
        1 => new Vector2(potion1NameX, potion1NameY),
        2 => new Vector2(potion2NameX, potion2NameY),
        _ => new Vector2(potion0NameX, potion0NameY),
    };
    private Vector2 GetPotionPricePos(int i) => i switch
    {
        0 => new Vector2(potion0PriceX, potion0PriceY),
        1 => new Vector2(potion1PriceX, potion1PriceY),
        2 => new Vector2(potion2PriceX, potion2PriceY),
        _ => new Vector2(potion0PriceX, potion0PriceY),
    };

    private Rect GetRelicCellRect(int i) => i switch
    {
        0 => new Rect(relic0X, relic0Y, relic0W, relic0H),
        1 => new Rect(relic1X, relic1Y, relic1W, relic1H),
        2 => new Rect(relic2X, relic2Y, relic2W, relic2H),
        3 => new Rect(relic3X, relic3Y, relic3W, relic3H),
        4 => new Rect(relic4X, relic4Y, relic4W, relic4H),
        5 => new Rect(relic5X, relic5Y, relic5W, relic5H),
        _ => new Rect(relic0X, relic0Y, relic0W, relic0H),
    };
    private float GetRelicCellIconSize(int i) => i switch
    {
        0 => relic0IconSize,
        1 => relic1IconSize,
        2 => relic2IconSize,
        3 => relic3IconSize,
        4 => relic4IconSize,
        5 => relic5IconSize,
        _ => relic0IconSize,
    };
    private Vector2 GetRelicPricePos(int i) => i switch
    {
        0 => new Vector2(relic0PriceX, relic0PriceY),
        1 => new Vector2(relic1PriceX, relic1PriceY),
        2 => new Vector2(relic2PriceX, relic2PriceY),
        3 => new Vector2(relic3PriceX, relic3PriceY),
        4 => new Vector2(relic4PriceX, relic4PriceY),
        5 => new Vector2(relic5PriceX, relic5PriceY),
        _ => new Vector2(relic0PriceX, relic0PriceY),
    };

    private void DrawPotionsSection(ShopState shop, RunState run)
    {
        const int SLOTS = 3;
        for (int i = 0; i < SLOTS; i++)
        {
            var r = GetPotionSlotRect(i);
            float iconS = GetPotionSlotIconSize(i);

            // 데이터 없는 슬롯 — 플레이스홀더 (아이콘만 흐리게)
            if (i >= shop.potions.Count)
            {
                if (_potionIcon != null)
                {
                    var prev = GUI.color;
                    GUI.color = new Color(1f, 1f, 1f, 0.30f);
                    GUI.DrawTexture(new Rect(r.x + itemIconLeftPad, r.y + (r.height - iconS) * 0.5f, iconS, iconS),
                                    _potionIcon, ScaleMode.ScaleToFit);
                    GUI.color = prev;
                }
                continue;
            }

            var entry = shop.potions[i];
            bool purchasable = !entry.sold && !run.PotionSlotFull;

            // 텍스트 위치 — 슬롯별 절대 좌표
            var namePos = GetPotionNamePos(i);
            var pricePos = GetPotionPricePos(i);
            float nameW = Mathf.Max(20f, pricePos.x - namePos.x - 5f);
            var nameRect = new Rect(namePos.x, namePos.y, nameW, r.height);
            var priceRect = potionPriceUsePlaque
                ? new Rect(pricePos.x, pricePos.y, potionPlaqueWidth, potionPlaqueHeight)
                : new Rect(pricePos.x, pricePos.y, itemPriceWidth, r.height);

            // 클릭/plaque 스케일은 plaque에서만, 툴팁은 행 또는 plaque 어디서든
            bool plaqueHover = purchasable && priceRect.Contains(Event.current.mousePosition);
            bool tooltipHover = r.Contains(Event.current.mousePosition) || priceRect.Contains(Event.current.mousePosition);

            DrawItemRow(r, _potionIcon, EnName(entry.potion.nameEn, entry.potion.nameKr),
                        entry.price, entry.sold, run.gold >= entry.price, plaqueHover, iconS,
                        nameRect, priceRect,
                        plaqueTex: potionPriceUsePlaque ? _pricePlaqueTex : null,
                        priceIconSize: potionPlaqueIconSize,
                        priceIconLeftPad: potionPlaqueIconLeftPad,
                        priceTextGap: potionPlaqueTextGap,
                        priceStyle: _potionPriceStyle);

            if (tooltipHover)
            {
                _tooltipTitle = EnName(entry.potion.nameEn, entry.potion.nameKr);
                _tooltipBody = BuildPotionTooltipBody(entry.potion);
            }
            if (purchasable && GUI.Button(priceRect, GUIContent.none, GUIStyle.none))
            {
                int captured = i;
                _pending.Add(() => GameStateManager.Instance?.BuyShopPotion(shop.potions[captured]));
            }
        }
    }

    private void DrawRelicsSection(ShopState shop, RunState run)
    {
        const int CELLS = 6;
        for (int i = 0; i < CELLS; i++)
        {
            var r = GetRelicCellRect(i);
            float iconS = GetRelicCellIconSize(i);

            // 데이터 없는 셀 — 플레이스홀더 (아이콘만 흐리게)
            if (i >= shop.relics.Count)
            {
                if (_relicIcon != null)
                {
                    var prev = GUI.color;
                    GUI.color = new Color(1f, 1f, 1f, 0.30f);
                    GUI.DrawTexture(new Rect(r.x + relicsIconLeftPad, r.y + (r.height - iconS) * 0.5f, iconS, iconS),
                                    _relicIcon, ScaleMode.ScaleToFit);
                    GUI.color = prev;
                }
                continue;
            }

            var entry = shop.relics[i];
            bool owned = run.relics.Contains(entry.relic);
            bool purchasable = !entry.sold && !owned;

            // 가격 — 셀 아래 plaque
            var pricePos = GetRelicPricePos(i);
            var plaqueRect = new Rect(pricePos.x, pricePos.y, relicPlaqueWidth, relicPlaqueHeight);

            // 클릭/스케일은 plaque에서만, 툴팁은 셀 또는 plaque 어디서든
            bool hover = purchasable && plaqueRect.Contains(Event.current.mousePosition);
            bool tooltipHover = r.Contains(Event.current.mousePosition) || plaqueRect.Contains(Event.current.mousePosition);

            // 아이콘 (좌) — 크기 고정 (hover에 영향 안 받음)
            if (_relicIcon != null)
            {
                GUI.DrawTexture(new Rect(r.x + relicsIconLeftPad, r.y + (r.height - iconS) * 0.5f, iconS, iconS),
                                _relicIcon, ScaleMode.ScaleToFit);
            }

            // plaque hover 시 plaque + 안의 모든 요소가 가운데 기준 확대
            Matrix4x4 prevMatrix = GUI.matrix;
            if (hover) GUIUtility.ScaleAroundPivot(Vector2.one * plaqueHoverScale, plaqueRect.center);

            if (relicPriceUsePlaque && _pricePlaqueTex != null)
            {
                GUI.DrawTexture(plaqueRect, _pricePlaqueTex, ScaleMode.StretchToFill);
            }
            DrawPriceWithCoin(plaqueRect, entry.price, run.gold >= entry.price,
                              relicPriceUsePlaque ? relicPlaqueIconLeftPad : 0f,
                              overrideIconSize: relicPlaqueIconSize,
                              overrideTextGap: relicPlaqueTextGap,
                              overrideStyle: _relicPriceStyle);

            GUI.matrix = prevMatrix;

            if (entry.sold || owned)
            {
                var prev = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.55f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = prev;
            }

            if (tooltipHover)
            {
                _tooltipTitle = EnName(entry.relic.nameEn, entry.relic.nameKr);
                _tooltipBody = BuildRelicTooltipBody(entry.relic);
            }
            if (purchasable && GUI.Button(plaqueRect, GUIContent.none, GUIStyle.none))
            {
                int captured = i;
                _pending.Add(() => GameStateManager.Instance?.BuyShopRelic(shop.relics[captured]));
            }
        }
    }

    private void DrawServicesSection(ShopState shop, RunState run)
    {
        // 서비스 섹션 큰 패널 없음 — Shop_background 의 SERVICES 라벨이 이미 위에 있음.
        var rowR = new Rect(servicesSectionRect.x + servicesContentPadX,
                            servicesSectionRect.y + servicesContentTopY,
                            servicesSectionRect.width - servicesContentPadX * 2f,
                            itemRowHeight);

        bool purchasable = !shop.cardRemoveUsed && run.gold >= shop.cardRemovePrice && run.deck.Count > 0;

        // Services 텍스트 좌표 — override 켜져 있으면 인스펙터 절대좌표 사용
        var svcNameRect = serviceNameOverride
            ? new Rect(serviceNameX, serviceNameY, serviceNameW, serviceNameH)
            : new Rect(
                rowR.x + itemIconLeftPad + serviceIconSize + itemNameLeftGap,
                rowR.y,
                Mathf.Max(20f, rowR.width - (itemIconLeftPad + serviceIconSize + itemNameLeftGap) - itemNameRightReserve),
                rowR.height);
        var svcPriceRect = servicePriceUsePlaque
            ? new Rect(rowR.xMax - itemPriceRightOffset + servicePlaqueXOffset,
                       rowR.y + servicePlaqueYOffset,
                       servicePlaqueWidth, servicePlaqueHeight)
            : new Rect(rowR.xMax - itemPriceRightOffset, rowR.y, itemPriceWidth, rowR.height);

        // 클릭/plaque 스케일은 plaque에서만, 툴팁은 행 또는 plaque 어디서든
        bool hover = purchasable && svcPriceRect.Contains(Event.current.mousePosition);
        bool tooltipHover = rowR.Contains(Event.current.mousePosition) || svcPriceRect.Contains(Event.current.mousePosition);

        DrawItemRow(rowR, _removeCardIcon,
                    DataManager.Instance.GetUIString("shop.remove_card"),
                    shop.cardRemovePrice, shop.cardRemoveUsed,
                    run.gold >= shop.cardRemovePrice, hover, serviceIconSize,
                    svcNameRect, svcPriceRect,
                    plaqueTex: servicePriceUsePlaque ? _pricePlaqueTex : null,
                    priceIconSize: servicePlaqueIconSize,
                    priceIconLeftPad: servicePlaqueIconLeftPad,
                    priceTextGap: servicePlaqueTextGap,
                    priceStyle: _servicePriceStyle);

        if (tooltipHover)
        {
            _tooltipTitle = DataManager.Instance.GetUIString("shop.remove_card");
            _tooltipBody = "Remove one card from your deck permanently.";
        }
        if (purchasable && GUI.Button(svcPriceRect, GUIContent.none, GUIStyle.none))
            _pending.Add(() => { _view = View.RemovePicker; _removeScrollY = 0f; });
    }

    // r = 슬롯(클릭/호버) 영역. nameRect/priceRect = 텍스트 절대 좌표. iconSize는 row 좌측에 그릴 아이콘 크기.
    private void DrawItemRow(Rect r, Texture2D icon, string name, int price, bool sold, bool canAfford, bool hover, float iconSize, Rect nameRect, Rect priceRect,
                             Texture2D plaqueTex = null,
                             float priceIconSize = -1f,
                             float priceIconLeftPad = 0f,
                             float priceTextGap = -1f,
                             GUIStyle priceStyle = null)
    {
        // 행 아이콘은 위치/크기 고정 (hover에 영향 안 받음)
        if (icon != null)
            GUI.DrawTexture(new Rect(r.x + itemIconLeftPad, r.y + (r.height - iconSize) * 0.5f, iconSize, iconSize),
                            icon, ScaleMode.ScaleToFit);

        GUI.Label(nameRect, name, _itemNameStyle);

        // hover 파라미터가 true면 plaque + 안의 코인/숫자가 함께 가운데 기준 확대
        Matrix4x4 prevMatrix = GUI.matrix;
        if (hover) GUIUtility.ScaleAroundPivot(Vector2.one * plaqueHoverScale, priceRect.center);

        if (plaqueTex != null)
            GUI.DrawTexture(priceRect, plaqueTex, ScaleMode.StretchToFill);

        DrawPriceWithCoin(priceRect, price, canAfford, priceIconLeftPad,
                          priceIconSize >= 0f ? priceIconSize : itemPriceIconSize,
                          priceTextGap   >= 0f ? priceTextGap   : itemPriceIconGap,
                          priceStyle ?? _itemPriceStyle);

        GUI.matrix = prevMatrix;

        if (sold)
        {
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.50f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
        }
    }

    // 가격 영역: 좌측에 money.png 코인 + 우측에 숫자.
    private void DrawPriceWithCoin(Rect priceRect, int price, bool canAfford, float iconLeftPad = 0f,
                                   float overrideIconSize = -1f, float overrideTextGap = -1f, GUIStyle overrideStyle = null)
    {
        float iconS = overrideIconSize >= 0f ? overrideIconSize : itemPriceIconSize;
        float gap   = overrideTextGap  >= 0f ? overrideTextGap  : itemPriceIconGap;
        GUIStyle style = overrideStyle ?? _itemPriceStyle;
        float iconX = priceRect.x + iconLeftPad;
        if (_moneyIcon != null && iconS > 0f)
        {
            float iy = priceRect.y + (priceRect.height - iconS) * 0.5f;
            GUI.DrawTexture(new Rect(iconX, iy, iconS, iconS), _moneyIcon, ScaleMode.ScaleToFit);
        }
        float numStart = iconX + (iconS > 0f ? iconS + gap : 0f);
        var numRect = new Rect(numStart, priceRect.y,
                               Mathf.Max(10f, priceRect.xMax - numStart), priceRect.height);
        var prevC = style.normal.textColor;
        style.normal.textColor = canAfford ? plaqueAffordColor : plaqueExpensiveColor;
        LockStateColors(style);
        GUI.Label(numRect, price.ToString(), style);
        style.normal.textColor = prevC;
        LockStateColors(style);
    }

    private void DrawLeaveButton(GameStateManager gsm)
    {
        var rect = new Rect(
            leaveShopCenter.x - leaveShopSize.x * 0.5f,
            leaveShopCenter.y - leaveShopSize.y * 0.5f,
            leaveShopSize.x, leaveShopSize.y);

        bool hover = rect.Contains(Event.current.mousePosition);
        Rect draw = hover ? Scale(rect, leaveShopHoverScale) : rect;

        if (_leaveShopTex != null) GUI.DrawTexture(draw, _leaveShopTex, ScaleMode.ScaleToFit);
        else DrawFilledRect(draw, new Color(0.20f, 0.10f, 0.25f, 0.95f));

        if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
            _pending.Add(() => gsm.ExitShop());
    }

    // =========================================================
    // Remove picker
    // =========================================================

    private void DrawRemovePicker(GameStateManager gsm, RunState run)
    {
        if (_shopBg != null)
            GUI.DrawTexture(new Rect(0, 0, RefW, RefH), _shopBg, ScaleMode.ScaleAndCrop);
        DrawFilledRect(new Rect(0, 0, RefW, RefH), new Color(0f, 0f, 0f, 0.7f));

        var dm = DataManager.Instance;
        GUI.Label(new Rect(0, 24f, RefW, 32f), dm.GetUIString("shop.remove_card_pick"), _itemNameStyle);

        var ev = Event.current;
        if (ev.type == EventType.ScrollWheel) { _removeScrollY += ev.delta.y * 30f; ev.Use(); }

        const int cols = 6;
        float cardW = 150f, cardH = 209f, gap = 14f;
        float totalW = cols * cardW + (cols - 1) * gap;
        float startX = (RefW - totalW) * 0.5f;
        float gridTop = 80f;
        float gridAreaH = RefH - gridTop - 110f;

        int rowCount = Mathf.CeilToInt(run.deck.Count / (float)cols);
        float contentH = rowCount * cardH + Mathf.Max(0, rowCount - 1) * gap;
        float maxScroll = Mathf.Max(0f, contentH - gridAreaH);
        _removeScrollY = Mathf.Clamp(_removeScrollY, -maxScroll, 0f);

        GUI.BeginGroup(new Rect(0, gridTop, RefW, gridAreaH));

        if (_battleUI == null) _battleUI = UnityEngine.Object.FindFirstObjectByType<BattleUI>();

        for (int i = 0; i < run.deck.Count; i++)
        {
            int col = i % cols;
            int row = i / cols;
            var rect = new Rect(startX + col * (cardW + gap), row * (cardH + gap) + _removeScrollY, cardW, cardH);
            if (rect.yMax < 0 || rect.y > gridAreaH) continue;

            if (_battleUI != null) _battleUI.DrawCardPreview(rect, run.deck[i]);
            else DrawFilledRect(rect, new Color(0.10f, 0.14f, 0.20f, 0.96f));

            var pill = new Rect(rect.x + 8f, rect.yMax - 38f, rect.width - 16f, 28f);
            DrawFilledRect(pill, new Color(0.55f, 0.05f, 0.05f, 0.75f));
            GUI.Label(pill, "REMOVE", _itemPriceStyle);

            if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
            {
                int captured = i;
                _pending.Add(() =>
                {
                    var c = run.deck[captured];
                    if (GameStateManager.Instance != null && GameStateManager.Instance.UseCardRemoveService(c))
                        _view = View.Main;
                });
            }
        }

        GUI.EndGroup();

        // 취소 버튼
        var cancelRect = new Rect((RefW - 220f) * 0.5f, RefH - 86f, 220f, 62f);
        if (_leaveShopTex != null) GUI.DrawTexture(cancelRect, _leaveShopTex, ScaleMode.ScaleToFit);
        else DrawFilledRect(cancelRect, new Color(0.12f, 0.16f, 0.22f, 0.95f));
        GUI.Label(cancelRect, dm.GetUIString("shop.cancel"), _leaveStyle);
        if (GUI.Button(cancelRect, GUIContent.none, GUIStyle.none))
            _pending.Add(() => _view = View.Main);
    }

    // =========================================================
    // Tooltip
    // =========================================================

    private void DrawTooltip()
    {
        if (string.IsNullOrEmpty(_tooltipTitle)) return;

        string body = string.IsNullOrEmpty(_tooltipBody) ? "" : _tooltipBody;
        float tw = tooltipWidth;
        var titleSize = _tooltipTitleStyle.CalcSize(new GUIContent(_tooltipTitle));
        float bodyH = string.IsNullOrEmpty(body) ? 0f : _tooltipBodyStyle.CalcHeight(new GUIContent(body), tw - 24f);
        float th = 12f + titleSize.y + 6f + bodyH + 12f;

        var mouse = Event.current.mousePosition;
        float tx = mouse.x + 18f, ty = mouse.y + 14f;
        if (tx + tw > RefW) tx = mouse.x - tw - 10f;
        if (ty + th > RefH) ty = RefH - th - 4f;

        var rect = new Rect(tx, ty, tw, th);
        DrawRoundedFilledRect(rect, tooltipBorder, tooltipCorner);
        var inner = new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f);
        DrawRoundedFilledRect(inner, tooltipFill, Mathf.Max(0, tooltipCorner - 1));

        GUI.Label(new Rect(tx + 12f, ty + 10f, tw - 24f, titleSize.y), _tooltipTitle, _tooltipTitleStyle);
        if (!string.IsNullOrEmpty(body))
            GUI.Label(new Rect(tx + 12f, ty + 10f + titleSize.y + 6f, tw - 24f, bodyH), body, _tooltipBodyStyle);
    }

    // =========================================================
    // Resources / Styles
    // =========================================================

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        _introBg         = Resources.Load<Texture2D>("ShopUI/Shop_first");
        _shopBg          = Resources.Load<Texture2D>("ShopUI/Shop_background");
        _pricePlaqueTex  = Resources.Load<Texture2D>("ShopUI/Right_UP");
        _shopTitlePanel  = Resources.Load<Texture2D>("ShopUI/Shop_Title_Panel");

        _frameSlimSummon = Resources.Load<Texture2D>("ShopUI/Frame_Slim_SUMMON");
        _frameSlimMagic  = Resources.Load<Texture2D>("ShopUI/Frame_Slim_MAGIC");
        _frameSlimBuff   = Resources.Load<Texture2D>("ShopUI/Frame_Slim_BUFF");
        _frameSlimRitual = Resources.Load<Texture2D>("ShopUI/Frame_Slim_RITUAL");
        _leaveShopTex    = Resources.Load<Texture2D>("ShopUI/Leaveshop");

        _moneyIcon       = Resources.Load<Texture2D>("ShopUI/money");
        _potionIcon      = Resources.Load<Texture2D>("ShopUI/Potion");
        _relicIcon       = Resources.Load<Texture2D>("ShopUI/Relics");
        _removeCardIcon  = Resources.Load<Texture2D>("ShopUI/Remove_card");

        _displayFont     = Resources.Load<Font>("Fonts/Cinzel-VariableFont_wght");
    }

    private void EnsureStyles()
    {
        if (_stylesReady) return;
        _stylesReady = true;

        var cream = new Color(0.97f, 0.92f, 0.74f);

        _goldStyle = new GUIStyle(GUI.skin.label) {
            font = _displayFont, fontSize = goldFontSize, alignment = TextAnchor.MiddleLeft,
            fontStyle = FontStyle.Bold, normal = { textColor = goldTextColor },
        };
        _itemNameStyle = new GUIStyle(GUI.skin.label) {
            font = _displayFont, fontSize = itemNameFontSize, alignment = TextAnchor.MiddleLeft,
            fontStyle = FontStyle.Bold, wordWrap = true, normal = { textColor = itemNameColor },
        };
        _itemPriceStyle = new GUIStyle(GUI.skin.label) {
            font = _displayFont, fontSize = itemPriceFontSize, alignment = TextAnchor.MiddleRight,
            fontStyle = FontStyle.Bold, normal = { textColor = plaqueAffordColor },
        };
        _plaqueStyle = new GUIStyle(GUI.skin.label) {
            font = _displayFont, fontSize = plaqueFontSize, alignment = TextAnchor.MiddleLeft,
            fontStyle = FontStyle.Bold, normal = { textColor = plaqueAffordColor },
        };
        _potionPriceStyle = new GUIStyle(GUI.skin.label) {
            font = _displayFont, fontSize = potionPlaqueFontSize, alignment = TextAnchor.MiddleLeft,
            fontStyle = FontStyle.Bold, normal = { textColor = plaqueAffordColor },
        };
        _servicePriceStyle = new GUIStyle(GUI.skin.label) {
            font = _displayFont, fontSize = servicePlaqueFontSize, alignment = TextAnchor.MiddleLeft,
            fontStyle = FontStyle.Bold, normal = { textColor = plaqueAffordColor },
        };
        _relicPriceStyle = new GUIStyle(GUI.skin.label) {
            font = _displayFont, fontSize = relicPlaqueFontSize, alignment = TextAnchor.MiddleLeft,
            fontStyle = FontStyle.Bold, normal = { textColor = plaqueAffordColor },
        };
        _leaveStyle = new GUIStyle(GUI.skin.label) {
            font = _displayFont, fontSize = 22, alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold, normal = { textColor = cream },
        };
        _hintStyle = new GUIStyle(GUI.skin.label) {
            font = _displayFont, fontSize = 18, alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold, normal = { textColor = cream },
        };
        _soldStyle = new GUIStyle(GUI.skin.label) {
            font = _displayFont, fontSize = 22, alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.55f, 0.55f) },
        };
        _tooltipTitleStyle = new GUIStyle(GUI.skin.label) {
            font = _displayFont, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperLeft,
            wordWrap = true, fontSize = tooltipTitleFont, normal = { textColor = new Color(1f, 0.90f, 0.55f) },
        };
        _tooltipBodyStyle = new GUIStyle(GUI.skin.label) {
            font = _displayFont, fontStyle = FontStyle.Normal, alignment = TextAnchor.UpperLeft,
            wordWrap = true, fontSize = tooltipBodyFont, normal = { textColor = new Color(0.92f, 0.88f, 0.74f) },
        };
        _slimTypeStyle = new GUIStyle(GUI.skin.label) {
            font = _displayFont, fontSize = slimTypeFontSize, alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.92f, 0.84f, 0.55f) },
        };
        _slimNameStyle = new GUIStyle(GUI.skin.label) {
            font = _displayFont, fontSize = slimNameFontSize, alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Clip,
            normal = { textColor = cream },
        };
        _slimCostStyle = new GUIStyle(GUI.skin.label) {
            font = _displayFont, fontSize = slimCostFontSize, alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.92f, 0.55f) },
        };

        LockStateColors(_goldStyle);
        LockStateColors(_itemNameStyle);
        LockStateColors(_itemPriceStyle);
        LockStateColors(_plaqueStyle);
        LockStateColors(_potionPriceStyle);
        LockStateColors(_servicePriceStyle);
        LockStateColors(_relicPriceStyle);
        LockStateColors(_leaveStyle);
        LockStateColors(_hintStyle);
        LockStateColors(_soldStyle);
        LockStateColors(_tooltipTitleStyle);
        LockStateColors(_tooltipBodyStyle);
        LockStateColors(_slimTypeStyle);
        LockStateColors(_slimNameStyle);
        LockStateColors(_slimCostStyle);
    }

    private void UpdateStyleSizes()
    {
        if (_goldStyle != null) _goldStyle.fontSize = goldFontSize;
        if (_itemNameStyle != null) _itemNameStyle.fontSize = itemNameFontSize;
        if (_itemPriceStyle != null) _itemPriceStyle.fontSize = itemPriceFontSize;
        if (_plaqueStyle != null) _plaqueStyle.fontSize = plaqueFontSize;
        if (_potionPriceStyle != null) _potionPriceStyle.fontSize = potionPlaqueFontSize;
        if (_servicePriceStyle != null) _servicePriceStyle.fontSize = servicePlaqueFontSize;
        if (_relicPriceStyle != null) _relicPriceStyle.fontSize = relicPlaqueFontSize;
        if (_tooltipTitleStyle != null) _tooltipTitleStyle.fontSize = tooltipTitleFont;
        if (_tooltipBodyStyle != null) _tooltipBodyStyle.fontSize = tooltipBodyFont;
        if (_slimTypeStyle != null) _slimTypeStyle.fontSize = slimTypeFontSize;
        if (_slimNameStyle != null) _slimNameStyle.fontSize = slimNameFontSize;
        if (_slimCostStyle != null) _slimCostStyle.fontSize = slimCostFontSize;
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
    // Helpers
    // =========================================================

    private static Rect Scale(Rect r, float s) => new Rect(
        r.center.x - r.width * s * 0.5f,
        r.center.y - r.height * s * 0.5f,
        r.width * s, r.height * s);

    private static void DrawFilledRect(Rect r, Color c)
    {
        var prev = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = prev;
    }

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
        t.SetPixels32(px);
        t.Apply(false, true);
        _roundedRectCache[radius] = t;
        return t;
    }

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

    private static string EnName(string en, string kr) =>
        string.IsNullOrWhiteSpace(en) ? kr : en;
}
