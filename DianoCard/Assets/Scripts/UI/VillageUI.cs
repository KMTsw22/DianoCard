using System;
using System.Collections.Generic;
using DianoCard.Game;
using UnityEngine;

/// <summary>
/// 마을(캠프) 노드 상호작용 화면. GameState == Village 일 때만 그려짐.
///
/// 두 가지 선택지를 좌/우 카드로 제시:
///  - 보물상자 무료 개봉 → OpenVillageTreasure() 호출 (Reward 화면으로 자동 전환)
///  - 휴식: 최대 HP의 25% 회복 → RestAtVillage() 호출 (다음 층으로 자동 진행)
///
/// ShopUI / RewardUI와 동일한 IMGUI + Reward 아트 재사용 패턴.
/// DefaultExecutionOrder(1000) — BattleUI보다 늦게 OnGUI를 돌려 위에 그려지도록.
/// </summary>
[DefaultExecutionOrder(1000)]
public class VillageUI : MonoBehaviour
{
    private const float RefW = 1280f;
    private const float RefH = 720f;

    // =========================================================
    // Inspector 튜닝 필드
    // =========================================================

    [Header("Backdrop (배경 이미지 + 딤 + 모닥불 글로우)")]
    [Tooltip("배경 이미지 위에 덮는 딤 색. 알파 0=이미지 그대로, 높일수록 어두움.")]
    [SerializeField] private Color backdropColor = new(0.02f, 0.04f, 0.06f, 0.25f);
    [Tooltip("모닥불 글로우 오버레이 — 배경 이미지에 이미 불이 있으면 알파 0으로 해서 끄기.")]
    [SerializeField] private Color campfireGlowColor = new(1f, 0.55f, 0.22f, 0.18f);
    [SerializeField] private Vector2 campfireGlowSize = new(1400f, 900f);

    [Header("모닥불 집중 글로우 (fire pit 근처 강한 빛)")]
    [SerializeField] private bool focalGlowEnabled = true;
    [Tooltip("집중 글로우 중심 위치 (px, 1280x720 기준). 배경에서 모닥불 위치에 맞추세요.")]
    [SerializeField] private Vector2 focalGlowCenter = new(640f, 620f);
    [SerializeField] private Vector2 focalGlowSize = new(520f, 360f);
    [Tooltip("집중 글로우 색 + 알파. 알파 0.3~0.5 권장.")]
    [SerializeField] private Color focalGlowColor = new(1f, 0.55f, 0.20f, 0.40f);
    [Tooltip("펄스 진폭 (알파에 더해지는 미세한 떨림). 0=정적, 0.05=은은하게 깜빡.")]
    [SerializeField, Range(0f, 0.2f)] private float focalGlowPulseAmp = 0.06f;
    [Tooltip("펄스 주기 (초).")]
    [SerializeField, Range(0.5f, 5f)] private float focalGlowPulsePeriod = 1.8f;

    [Header("횃불 불씨 파티클 (좌/우 횃불에서 튀는 불꽃)")]
    [SerializeField] private bool embersEnabled = true;
    [Tooltip("파티클 총 개수 (양쪽 횃불 합산).")]
    [SerializeField, Range(0, 80)] private int embersCount = 32;
    [Tooltip("좌측 횃불 위치 (px). 배경의 왼쪽 횃불 불꽃 위치에 맞추세요.")]
    [SerializeField] private Vector2 torchLeft = new(90f, 220f);
    [Tooltip("우측 횃불 위치 (px). 배경의 오른쪽 횃불 불꽃 위치에 맞추세요.")]
    [SerializeField] private Vector2 torchRight = new(1190f, 220f);
    [Tooltip("파티클 생성 반경 (px). 횃불 주위 이 안에서 랜덤 생성.")]
    [SerializeField, Range(5f, 80f)] private float embersSpawnRadius = 18f;
    [Tooltip("파티클 상승 속도 (px/s).")]
    [SerializeField, Range(20f, 200f)] private float embersRiseSpeed = 30f;
    [Tooltip("파티클 가로 흔들림 폭 (px).")]
    [SerializeField, Range(0f, 40f)] private float embersSway = 12f;
    [Tooltip("파티클 수명 (초). 짧을수록 짧게 튀고 자주 재생성.")]
    [SerializeField, Range(0.5f, 4f)] private float embersLifetime = 3f;
    [Tooltip("파티클 크기 범위 (px).")]
    [SerializeField] private Vector2 embersSizeRange = new(3f, 6f);
    [Tooltip("파티클 색 (시작).")]
    [SerializeField] private Color embersColorStart = new(1f, 0.85f, 0.35f, 1f);
    [Tooltip("파티클 색 (끝 — 페이드).")]
    [SerializeField] private Color embersColorEnd = new(1f, 0.3f, 0.1f, 0f);

    [Header("별 반짝임 (랜덤 분포, 은은)")]
    [SerializeField] private bool starsEnabled = true;
    [Tooltip("별 개수. 많이 뿌려놓고 각자 다른 타이밍에 희미하게 반짝이게.")]
    [SerializeField, Range(0, 200)] private int starsCount = 100;
    [Tooltip("별이 랜덤 생성되는 영역 (px). 배경 그림의 '열린 하늘' 부분만 덮도록 좁게.")]
    [SerializeField] private Rect starsArea = new Rect(400f, 0f, 500f, 150f);
    [Tooltip("별 크기 범위 (px). 2~4 가 자세히 봐야 보이는 수준.")]
    [SerializeField] private Vector2 starsSizeRange = new(2f, 4f);
    [Tooltip("별 최대 색 + 알파. 알파 낮을수록 전체적으로 희미.")]
    [SerializeField] private Color starsColor = new(1f, 0.97f, 0.85f, 0.9f);
    [Tooltip("반짝임 최소 알파 배율 (0~1). 0.0 이면 주기적으로 완전히 사라짐.")]
    [SerializeField, Range(0f, 1f)] private float starsMinAlphaMul = 0.1f;
    [Tooltip("반짝임 주기 범위 (초). 값 크면 천천히 깜빡. 4~10 이 자연스러움.")]
    [SerializeField] private Vector2 starsTwinklePeriodRange = new(5f, 11f);
    [Tooltip("전체 별 중 동시에 반짝이는 비율. 낮으면 띄엄띄엄 반짝여 희소성 연출.")]
    [SerializeField, Range(0.1f, 1f)] private float starsActiveRatio = 0.1f;

    [Header("NPC (상반신 화자)")]
    [Tooltip("NPC 표시 여부.")]
    [SerializeField] private bool npcEnabled = true;
    [Tooltip("NPC 크기 (px). ScaleToFit이라 비율 유지됨.")]
    [SerializeField] private Vector2 npcSize = new(540f, 740f);
    [Tooltip("NPC 좌상단 Y 위치 (px). 값 크게 → 아래로 내려감.")]
    [SerializeField, Range(-100f, 500f)] private float npcY = 260f;
    [Tooltip("NPC X 위치 모드 (true=화면 왼쪽 기준, false=오른쪽 기준).")]
    [SerializeField] private bool npcAlignLeft = true;
    [Tooltip("앵커로부터의 X 거리 (px). 왼쪽 정렬이면 화면 왼쪽에서, 오른쪽이면 화면 오른쪽에서.")]
    [SerializeField, Range(-200f, 400f)] private float npcXOffset = -30f;

    [Header("NPC Intro (Village 진입 시 1회 페이드 인)")]
    [Tooltip("진입 페이드인 재생 여부.")]
    [SerializeField] private bool introEnabled = true;
    [Tooltip("진입 연출 총 지속시간 (초).")]
    [SerializeField, Range(0.3f, 2f)] private float introDuration = 0.7f;
    [Tooltip("진입 시작 위치 오프셋 (px). 여기서 원위치로 슬라이드 인. Y 양수 = 아래에서 위로 올라옴.")]
    [SerializeField] private Vector2 introSlideFrom = new(-30f, 20f);
    [Tooltip("진입 시작 스케일. 1보다 작으면 커지며 등장.")]
    [SerializeField, Range(0.85f, 1f)] private float introScaleFrom = 0.96f;
    [Tooltip("NPC 등장 완료 후 옵션 카드가 페이드 인되는 시간 (초). 0=즉시 표시.")]
    [SerializeField, Range(0f, 1f)] private float optionsFadeInDuration = 0.35f;

    [Header("NPC Idle Breathing (지속 호흡)")]
    [Tooltip("호흡 루프 재생 여부.")]
    [SerializeField] private bool breathingEnabled = true;
    [Tooltip("호흡 진폭 (px). 상하로 이만큼 미세하게 흔들림.")]
    [SerializeField, Range(0f, 10f)] private float breathingAmplitudePx = 3f;
    [Tooltip("호흡 주기 (초). 한 호흡 = 들이쉬고 내쉬는 1사이클.")]
    [SerializeField, Range(1.5f, 8f)] private float breathingPeriod = 3.5f;

    [Header("Header (타이틀 / 부제 / HP)")]
    [SerializeField] private string titleText = "";
    [SerializeField] private string subtitleText = "";
    [SerializeField] private Rect campIconRect = new(40f, 18f, 68f, 68f);
    [SerializeField] private float titleY = 24f;
    [SerializeField] private float titleHeight = 48f;
    [SerializeField] private float subtitleY = 70f;
    [SerializeField] private float subtitleHeight = 22f;
    [SerializeField] private Vector2 hpPanelSize = new(220f, 44f);
    [SerializeField] private float hpPanelRightMargin = 240f;
    [SerializeField] private float hpPanelTopY = 22f;
    [Tooltip("HP 패널 배경색 — ShopUI 섹션 패널과 동일한 짙은 네이비 반투명.")]
    [SerializeField] private Color hpPanelBgColor = new(0.04f, 0.07f, 0.10f, 0.78f);
    [Tooltip("HP 패널 테두리 색 — 연한 골드.")]
    [SerializeField] private Color hpPanelBorderColor = new(0.72f, 0.56f, 0.28f, 0.75f);
    [Tooltip("HP 패널 테두리 두께 (0=없음).")]
    [SerializeField, Range(0f, 4f)] private float hpPanelBorderThickness = 1.5f;
    [Tooltip("HP 하트 아이콘 크기 (px).")]
    [SerializeField, Range(16f, 60f)] private float hpIconSize = 32f;
    [Tooltip("HP 아이콘 왼쪽 여백 (패널 시작부터).")]
    [SerializeField, Range(0f, 40f)] private float hpIconLeftPad = 12f;
    [Tooltip("HP 텍스트 왼쪽 시프트 — 아이콘 오른쪽부터 시작되도록 패널 텍스트 정렬을 보정.")]
    [SerializeField, Range(-40f, 40f)] private float hpTextXShift = 18f;

    // HUD Divider/Strip — BattleUI로 이전됨. GameStateManager의 BattleUI 컴포넌트 Inspector에서 튜닝.

    [Header("Option Cards (좌/우 2장)")]
    [SerializeField] private Vector2 optionCardSize = new(340f, 540f);
    [SerializeField] private float optionCardGap = 50f;
    [SerializeField] private float optionCardYOffset = 10f;
    [Tooltip("카드 2장 묶음의 X 오프셋 (양수 = 오른쪽으로 이동). 기본 0 = 화면 중앙.")]
    [SerializeField, Range(-300f, 300f)] private float optionCardXOffset = 200f;
    [SerializeField, Range(1f, 1.2f)] private float optionHoverScale = 1.04f;

    [Header("Option Content (왼쪽: Treasure / 오른쪽: Rest)")]
    [SerializeField] private string treasureTitle = "";
    [TextArea(2, 4)]
    [Tooltip("카드 1 보물 — 타이틀(큰 글씨). 본문은 아래 desc 필드.")]
    [SerializeField] private string treasureName = "Mystery";
    [Tooltip("카드 1 보물 — 본문. 이름 아래에 표시.")]
    [TextArea(2, 4)]
    [SerializeField] private string treasureDesc = "Free Relic Inside";
    [SerializeField] private Color treasureGlowColor = new(1f, 0.82f, 0.42f);
    [SerializeField] private string restTitle = "";
    [Tooltip("카드 2 휴식 — 타이틀(큰 글씨).")]
    [SerializeField] private string restName = "Heal";
    [SerializeField, Range(0f, 1f)] private float restHealPct = 0.25f;
    [SerializeField] private Color restGlowColor = new(1f, 0.35f, 0.30f);

    [Header("Option Title Banner (상단 텍스트 위치 — 배경/테두리 없음)")]
    [Tooltip("true면 타이틀을 상단 배너 위치에, false면 메달리온 아래에 표시.")]
    [SerializeField] private bool useTitleBanner = true;
    [Tooltip("타이틀 X 오프셋 (카드 중앙 기준, 양수=오른쪽).")]
    [SerializeField, Range(-200f, 200f)] private float titleBannerOffsetX = 0f;
    [Tooltip("타이틀 Y 위치 — 음수면 카드 위쪽으로 튀어나옴.")]
    [SerializeField, Range(-60f, 80f)] private float titleBannerOffsetY = 2.6f;
    [Tooltip("타이틀 배너 영역 폭 (텍스트 wrap/정렬 기준).")]
    [SerializeField, Range(80f, 340f)] private float titleBannerWidth = 280f;
    [Tooltip("타이틀 배너 영역 높이.")]
    [SerializeField, Range(24f, 80f)] private float titleBannerHeight = 46f;
    [Tooltip("배너 모드에서 쓸 타이틀 폰트 크기 (일반 optionTitleFontSize와 별개).")]
    [SerializeField, Range(12, 48)] private int titleBannerFontSize = 22;

    [Header("Option Card Inner (메달리온/아이콘/타이틀/설명)")]
    [Tooltip("메달리온 링 그리기 여부. false면 아이콘만 단독 표시.")]
    [SerializeField] private bool drawMedallion = false;
    [SerializeField] private float medallionSize = 150f;
    [Tooltip("메달리온 중심 Y = card.y + card.h * 이 값")]
    [SerializeField, Range(0.2f, 0.6f)] private float medallionCenterYFactor = 0.359f;
    [Tooltip("아이콘 크기 배율 — 메달리온 없으면 큰 값(0.9~1.1) 권장.")]
    [SerializeField, Range(0.3f, 1.4f)] private float iconSizeFactor = 0.824f;
    [Tooltip("아이콘 뒤 글로우 표시 여부.")]
    [SerializeField] private bool iconGlowEnabled = true;
    [Tooltip("기본(기타) 아이콘 글로우 색.")]
    [SerializeField] private Color iconGlowColor = new(1f, 0.78f, 0.40f, 0.55f);
    [Tooltip("글로우 크기 = 아이콘 크기 × 이 값.")]
    [SerializeField, Range(1f, 3f)] private float iconGlowScale = 2.0f;
    [Tooltip("TREASURE 아이콘 전용 글로우 색 — 앰버/오렌지 계열. 채도·알파 높여 보물상자 톤과 차별화.")]
    [SerializeField] private Color treasureIconGlowColor = new(1f, 0.58f, 0.15f, 0.95f);
    [Tooltip("TREASURE 아이콘 글로우 크기 배율.")]
    [SerializeField, Range(0.8f, 3f)] private float treasureIconGlowScaleMultiplier = 1.7f;
    [Tooltip("REST 아이콘 전용 글로우 색 — 빨간 하트 주변 붉은 아우라.")]
    [SerializeField] private Color restIconGlowColor = new(1f, 0.25f, 0.20f, 0.85f);
    [Tooltip("REST 아이콘 글로우 크기 배율.")]
    [SerializeField, Range(0.8f, 2.5f)] private float restIconGlowScaleMultiplier = 1.5f;

    [Header("아이콘 통합 연출 (프레임과 자연스러운 조화)")]
    [Tooltip("아이콘 틴트 — 흰색(1,1,1,1)은 원본 그대로. 세피아/따뜻하게 밀면 프레임과 묶임.")]
    [SerializeField] private Color iconTint = new(1f, 0.95f, 0.88f, 1f);
    [Tooltip("아이콘 뒤 부드러운 그림자(그라운딩) on/off.")]
    [SerializeField] private bool iconShadowEnabled = true;
    [Tooltip("그림자 색 + 알파. 기본 따뜻한 갈색 반투명.")]
    [SerializeField] private Color iconShadowColor = new(0.15f, 0.08f, 0.04f, 0.45f);
    [Tooltip("그림자 크기 = 아이콘 크기 × 이 값.")]
    [SerializeField, Range(0.5f, 2f)] private float iconShadowScale = 1.15f;
    [Tooltip("그림자 세로 오프셋 (px). 양수=아이콘보다 아래로 깔림, 0=중앙 일치.")]
    [SerializeField, Range(-30f, 40f)] private float iconShadowYOffset = 6f;
    [Tooltip("그림자 세로 납작 비율. 1=원, 0.5=타원(바닥에 깔린 느낌).")]
    [SerializeField, Range(0.3f, 1f)] private float iconShadowVerticalSquish = 0.55f;

    [SerializeField] private float optionTitleTopGap = 14f;
    [SerializeField] private float optionTitleHeight = 36f;
    [SerializeField] private float optionDescTopGap = 10f;
    [SerializeField] private float optionDescXPad = 30f;
    [SerializeField] private float optionDescBottomPad = 16f;

    [Header("Option Card Glow")]
    [SerializeField, Range(0f, 120f)] private float cardGlowPadNormal = 52f;
    [SerializeField, Range(0f, 120f)] private float cardGlowPadHover = 80f;
    [SerializeField, Range(0f, 1f)] private float cardGlowAlphaNormal = 0.48f;
    [SerializeField, Range(0f, 1f)] private float cardGlowAlphaHover = 0.72f;

    [Header("Option Card Planted Shadow (말뚝 끝 꽂힌 느낌 — 지면 접촉 그림자)")]
    [SerializeField] private bool plantedShadowEnabled = true;
    [Tooltip("그림자 색 + 알파. 숲 바닥 톤에 맞게 검정/짙은 갈색 추천.")]
    [SerializeField] private Color plantedShadowColor = new(0f, 0f, 0f, 0.55f);
    [Tooltip("그림자 타원 크기 (px). 말뚝 하나당.")]
    [SerializeField] private Vector2 plantedShadowSize = new(90f, 22f);
    [Tooltip("두 말뚝 중심 X 위치 (카드 폭 비율 0~1). 아트의 실제 말뚝 위치에 맞추세요.")]
    [SerializeField] private Vector2 plantedStakeXFactors = new(0.23f, 0.77f);
    [Tooltip("그림자 Y 오프셋 (카드 하단 기준, 양수=아래로 내려감). 말뚝 끝과 맞추기.")]
    [SerializeField, Range(-40f, 40f)] private float plantedShadowYOffset = -4f;

    [Header("Font Sizes")]
    [SerializeField, Range(20, 60)] private int titleFontSize = 44;
    [SerializeField, Range(10, 24)] private int subtitleFontSize = 16;
    [SerializeField, Range(16, 40)] private int optionTitleFontSize = 26;
    [SerializeField, Range(10, 32)] private int optionDescFontSize = 20;
    [SerializeField, Range(14, 32)] private int hpFontSize = 22;

    [Header("옵션 카드 이름 (Mystery / Heal 폰트/위치)")]
    [Tooltip("이름 폰트 크기.")]
    [SerializeField, Range(16, 60)] private int nameFontSize = 26;
    [Tooltip("이름 위치 오프셋 (px). X 양수=오른쪽, Y 양수=아래. 설명 영역 좌상단 기준.")]
    [SerializeField] private Vector2 nameOffset = new(0f, 25f);
    [Tooltip("이름 보물(Mystery) 색.")]
    [SerializeField] private Color treasureNameColor = new(1f, 0.83f, 0.29f); // #FFD54A
    [Tooltip("이름 휴식(Heal) 색.")]
    [SerializeField] private Color restNameColor = new(0.91f, 0.29f, 0.29f); // #E84A4A
    [Tooltip("본문(Free Relic Inside / Recover 25% HP 등) 위치 오프셋 (px). 이름 하단 기준. Y 양수=아래로.")]
    [SerializeField] private Vector2 bodyOffset = new(0f, 30f);
    [Tooltip("본문 폰트 크기 오버라이드 — 0=Option Desc Font Size 사용, 그 외는 이 값으로.")]
    [SerializeField, Range(0, 32)] private int bodyFontSizeOverride = 0;

    [Header("옵션 카드 이름 외곽선")]
    [Tooltip("이름 외곽선 on/off.")]
    [SerializeField] private bool nameOutlineEnabled = true;
    [Tooltip("외곽선 두께 (px). 0.3~0.6 이 아주 얇음. 값이 클수록 굵어짐.")]
    [SerializeField, Range(0f, 3f)] private float nameOutlineThickness = 0.4f;
    [Tooltip("외곽선 색. 알파로 진하기 조절 (0=안보임, 1=불투명).")]
    [SerializeField] private Color nameOutlineColor = new(0f, 0f, 0f, 0.55f);
    [Tooltip("4방향(상하좌우) 대신 8방향(대각선 포함)으로 렌더 — 더 굵고 둥근 외곽선.")]
    [SerializeField] private bool nameOutline8Dir = false;

    [Header("글씨 드롭 섀도우 (양피지에 박힌 느낌)")]
    [Tooltip("글씨 뒤 그림자 on/off. 이름+본문 모두 동일하게 적용.")]
    [SerializeField] private bool textShadowEnabled = true;
    [Tooltip("그림자 오프셋 (px). Y 양수=아래로, X 양수=오른쪽.")]
    [SerializeField] private Vector2 textShadowOffset = new(0.8f, 1.2f);
    [Tooltip("그림자 색 + 알파. 따뜻한 짙은 갈색 권장.")]
    [SerializeField] private Color textShadowColor = new(0.08f, 0.04f, 0.02f, 0.28f);

    [Header("Colors")]
    [SerializeField] private Color titleColor = new(0.98f, 0.88f, 0.52f);
    [SerializeField] private Color creamColor = new(0.99f, 0.95f, 0.78f);
    [SerializeField] private Color hpTextColor = new(0.95f, 0.55f, 0.55f);
    [Tooltip("옵션 설명문 색 — 목조 패널에 어울리는 짙은 세피아.")]
    [SerializeField] private Color optionDescColor = new(0.22f, 0.14f, 0.08f);
    [Tooltip("옵션 타이틀(TREASURE/REST) 글자 색 — 진한 블랙 계열.")]
    [SerializeField] private Color optionTitleColor = new(0.08f, 0.05f, 0.03f);
    [Tooltip("외곽선 위쪽 색 (밝은 실버).")]
    [SerializeField] private Color optionTitleOutlineTop = new(0.92f, 0.92f, 0.94f, 1f);
    [Tooltip("외곽선 아래쪽 색 (어두운 차콜) — 상하 그라데이션용.")]
    [SerializeField] private Color optionTitleOutlineBottom = new(0.35f, 0.35f, 0.38f, 1f);
    [Tooltip("외곽선 두께 (0=외곽선 없음).")]
    [SerializeField, Range(0f, 3f)] private float optionTitleOutlineThickness = 0f;

    private readonly List<Action> _pending = new();

    // 파티클(ember) 상태 — 각 파티클은 (x, y, age, lifetime, size, swayPhase, baseX)
    private struct Ember { public Vector2 pos; public float age; public float lifetime; public float size; public float swayPhase; public float baseX; }
    private readonly List<Ember> _embers = new();

    // 별 상태 — 위치/크기/반짝임 phase 는 Village 진입 시 1회 생성 후 고정.
    private struct Star { public Vector2 pos; public float size; public float period; public float phase; }
    private readonly List<Star> _stars = new();
    private bool _starsInitialized;

    // 아트 (Reward / Map 폴더에서 재사용)
    private Texture2D _panelTex;
    private Texture2D _rowTex;
    private Texture2D _medallionTex;
    private Texture2D _continueTex;
    private Texture2D _glowTex;
    private Texture2D _campIconTex;     // 헤더 — Map/Node_Camp
    private Texture2D _treasureIconTex; // 좌측 옵션 — Reward/RelicIcon
    private Texture2D _restIconTex;     // 우측 옵션 — Reward/Potion_Bottle
    private Texture2D _hpIconTex;       // HP 하트 아이콘 — InGame/Icon/HP (헤더 비활성화 시 미사용)
    private Texture2D _bgTex;           // 전체 화면 배경 — VillageUI/BackGround
    private Texture2D _npcTex;          // NPC 상반신 — VillageUI/NPC (1프레임 + transform 애니메이션)
    private Texture2D _optionCardTex;   // 선택지 카드 패널 — VillageUI/OptionCardPanel

    // 진입 엣지 디텍션 + 제스처 타이머. _gestureT < 0 = 재생 안 함.
    private bool _wasInVillage;
    private float _gestureT = -1f;

    private Font _displayFont;

    private GUIStyle _titleStyle;
    private GUIStyle _subStyle;
    private GUIStyle _optionTitleStyle;
    private GUIStyle _optionDescStyle;
    private GUIStyle _hpStyle;
    private bool _stylesReady;

    void Update()
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null) return;

        // 치트: F7 — 언제든 마을 강제 진입
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.f7Key.wasPressedThisFrame)
        {
            gsm.Cheat_EnterVillage();
        }

        // Village 진입 엣지에서 인트로 타이머 리셋. 진입 후에는 계속 누적 (호흡은 Time.time 사용).
        bool inVillage = gsm.State == GameState.Village;
        if (inVillage && !_wasInVillage && introEnabled) _gestureT = 0f;
        _wasInVillage = inVillage;
        if (_gestureT >= 0f && inVillage) _gestureT += Time.deltaTime;

        // 불씨 파티클 업데이트 — Village 에 있을 때만 틱.
        if (inVillage && embersEnabled) UpdateEmbers(Time.deltaTime);

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
        if (gsm == null || gsm.State != GameState.Village) return;
        var run = gsm.CurrentRun;
        if (run == null) return;

        EnsureStyles();
        ApplyStyleValues();  // Inspector 값 실시간 반영

        GUI.depth = 0;
        float scale = Mathf.Min(Screen.width / RefW, Screen.height / RefH);
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));

        DrawBackdrop();
        DrawNPC();
        DrawHeader(run);

        // 옵션 카드: NPC 인트로 완료 후 페이드 인. 완료 전에는 아예 안 그려서 클릭도 방지.
        float optionsAlpha = 1f;
        if (introEnabled && _gestureT >= 0f)
        {
            float elapsed = _gestureT - introDuration;
            if (elapsed < 0f) optionsAlpha = 0f;
            else if (elapsed < optionsFadeInDuration && optionsFadeInDuration > 0f)
                optionsAlpha = Mathf.SmoothStep(0f, 1f, elapsed / optionsFadeInDuration);
        }
        if (optionsAlpha > 0.001f)
        {
            var prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, optionsAlpha);
            DrawOptions(gsm, run);
            GUI.color = prev;
        }

    }

    // =========================================================
    // Drawing
    // =========================================================

    private void DrawBackdrop()
    {
        var prev = GUI.color;

        // 배경 이미지 (모닥불 야영지). 없으면 단색 폴백.
        if (_bgTex != null)
        {
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(0, 0, RefW, RefH), _bgTex, ScaleMode.ScaleAndCrop);
            // UI 가독성 확보용 딤 오버레이
            GUI.color = backdropColor;
            GUI.DrawTexture(new Rect(0, 0, RefW, RefH), Texture2D.whiteTexture);
        }
        else
        {
            GUI.color = new Color(0.03f, 0.02f, 0.04f, 0.88f);
            GUI.DrawTexture(new Rect(0, 0, RefW, RefH), Texture2D.whiteTexture);
        }

        if (_glowTex != null)
        {
            GUI.color = campfireGlowColor;
            GUI.DrawTexture(new Rect(RefW * 0.5f - campfireGlowSize.x * 0.5f,
                                     RefH * 0.5f - campfireGlowSize.y * 0.5f,
                                     campfireGlowSize.x, campfireGlowSize.y),
                            _glowTex, ScaleMode.StretchToFill);
        }

        // 모닥불 집중 글로우 — 은은한 펄스 효과.
        if (focalGlowEnabled && _glowTex != null)
        {
            float pulse = focalGlowPulsePeriod > 0f
                ? Mathf.Sin(Time.time * (Mathf.PI * 2f / focalGlowPulsePeriod)) * focalGlowPulseAmp
                : 0f;
            Color gc = focalGlowColor;
            gc.a = Mathf.Clamp01(gc.a + pulse);
            GUI.color = gc;
            GUI.DrawTexture(new Rect(
                    focalGlowCenter.x - focalGlowSize.x * 0.5f,
                    focalGlowCenter.y - focalGlowSize.y * 0.5f,
                    focalGlowSize.x, focalGlowSize.y),
                _glowTex, ScaleMode.StretchToFill);
        }

        GUI.color = prev;

        // 별 — 배경 위 가장 뒤쪽 레이어.
        if (starsEnabled) DrawStars();

        // 횃불 불씨 파티클 — 배경 위 / NPC 아래 레이어. 밝은 색 + 덧셈 느낌 위해 알파 블렌드.
        if (embersEnabled) DrawEmbers();
    }

    private void UpdateEmbers(float dt)
    {
        // 개수 맞추기 — 추가 or 제거.
        while (_embers.Count < embersCount) _embers.Add(SpawnEmber(UnityEngine.Random.Range(0f, embersLifetime)));
        while (_embers.Count > embersCount) _embers.RemoveAt(_embers.Count - 1);

        for (int i = 0; i < _embers.Count; i++)
        {
            var e = _embers[i];
            e.age += dt;
            if (e.age >= e.lifetime) { _embers[i] = SpawnEmber(0f); continue; }
            // 상승 + 가로 흔들림 (sin wave).
            e.pos.y -= embersRiseSpeed * dt;
            e.pos.x = e.baseX + Mathf.Sin(Time.time * 1.4f + e.swayPhase) * embersSway;
            _embers[i] = e;
        }
    }

    private Ember SpawnEmber(float startAge)
    {
        // 좌/우 횃불 중 랜덤 선택 (교대로 섞여서 양쪽 모두 활발해 보이게).
        Vector2 center = UnityEngine.Random.value < 0.5f ? torchLeft : torchRight;
        float ang = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        float r = UnityEngine.Random.Range(0f, embersSpawnRadius);
        float x = center.x + Mathf.Cos(ang) * r;
        float y = center.y + Mathf.Sin(ang) * r * 0.3f; // 상하로는 좁게
        return new Ember
        {
            pos = new Vector2(x, y),
            baseX = x,
            age = startAge,
            lifetime = embersLifetime * UnityEngine.Random.Range(0.7f, 1.3f),
            size = UnityEngine.Random.Range(embersSizeRange.x, embersSizeRange.y),
            swayPhase = UnityEngine.Random.Range(0f, Mathf.PI * 2f),
        };
    }

    private void DrawEmbers()
    {
        if (_embers.Count == 0) return;
        var prev = GUI.color;
        for (int i = 0; i < _embers.Count; i++)
        {
            var e = _embers[i];
            float t = Mathf.Clamp01(e.age / e.lifetime);
            Color c = Color.Lerp(embersColorStart, embersColorEnd, t);
            GUI.color = c;
            float half = e.size * 0.5f;
            GUI.DrawTexture(new Rect(e.pos.x - half, e.pos.y - half, e.size, e.size), _glowTex ?? Texture2D.whiteTexture, ScaleMode.StretchToFill);
        }
        GUI.color = prev;
    }

    private int _starsLastCount = -1;
    private Rect _starsLastArea;
    private static Texture2D _starCircleTex;
    private void EnsureStars()
    {
        bool needRebuild = !_starsInitialized
            || _stars.Count != starsCount
            || _starsLastCount != starsCount
            || _starsLastArea != starsArea;
        if (!needRebuild) return;

        _stars.Clear();
        for (int i = 0; i < starsCount; i++)
        {
            _stars.Add(new Star
            {
                pos = new Vector2(
                    UnityEngine.Random.Range(starsArea.xMin, starsArea.xMax),
                    UnityEngine.Random.Range(starsArea.yMin, starsArea.yMax)),
                size = UnityEngine.Random.Range(starsSizeRange.x, starsSizeRange.y),
                period = UnityEngine.Random.Range(starsTwinklePeriodRange.x, starsTwinklePeriodRange.y),
                phase = UnityEngine.Random.Range(0f, Mathf.PI * 2f),
            });
        }
        _starsLastCount = starsCount;
        _starsLastArea = starsArea;
        _starsInitialized = true;
    }

    /// <summary>작은 원 모양 텍스처 — 별을 네모 대신 동그랗게 렌더하기 위한 단순 알파 마스크.</summary>
    private static Texture2D GetStarCircleTexture()
    {
        if (_starCircleTex != null) return _starCircleTex;
        const int size = 16;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[size * size];
        float c = (size - 1) * 0.5f;
        float rMax = c; // 끝까지가 반지름
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
            // 가장자리에서 부드럽게 페이드아웃 — 안티앨리어싱 느낌.
            float a = Mathf.Clamp01(rMax - d);
            px[y * size + x] = new Color(1f, 1f, 1f, a);
        }
        tex.SetPixels(px);
        tex.Apply();
        _starCircleTex = tex;
        return tex;
    }

    private void DrawStars()
    {
        EnsureStars();
        if (_stars.Count == 0) return;
        var prev = GUI.color;
        var tex = GetStarCircleTexture();
        float now = Time.time;
        for (int i = 0; i < _stars.Count; i++)
        {
            var s = _stars[i];
            // 반짝임: sin 기반 0~1 알파 배율. minAlphaMul ~ 1 범위에서 진동.
            float t = (Mathf.Sin(now * (Mathf.PI * 2f / Mathf.Max(0.1f, s.period)) + s.phase) + 1f) * 0.5f;
            float alphaMul = Mathf.Lerp(starsMinAlphaMul, 1f, t);
            // 활성 비율 — phase 기반 결정적 선택으로 N% 만 "활동적"으로 반짝이게.
            // phase 를 0~1 정규화 후 activeRatio 보다 큰 별은 최소 밝기 유지.
            float normPhase = (s.phase / (Mathf.PI * 2f));
            if (normPhase > starsActiveRatio) alphaMul *= 0.35f;
            var c = starsColor;
            c.a *= alphaMul;
            GUI.color = c;
            float half = s.size * 0.5f;
            GUI.DrawTexture(new Rect(s.pos.x - half, s.pos.y - half, s.size, s.size), tex, ScaleMode.StretchToFill);
        }
        GUI.color = prev;
    }

    private void DrawNPC()
    {
        if (!npcEnabled || _npcTex == null) return;

        float baseX = npcAlignLeft ? npcXOffset : (RefW - npcSize.x - npcXOffset);

        // 인트로 진행도 (0 = 막 등장, 1 = 완료). 진입 후엔 항상 1로 고정.
        float introT = 1f;
        if (introEnabled && _gestureT >= 0f && introDuration > 0f)
        {
            introT = Mathf.Clamp01(_gestureT / introDuration);
            introT = Mathf.SmoothStep(0f, 1f, introT);
        }

        float alpha = introT; // 알파: 페이드 인
        float scale = Mathf.Lerp(introScaleFrom, 1f, introT); // 스케일: 살짝 커지며 등장
        Vector2 slideOff = introSlideFrom * (1f - introT); // 슬라이드: 오프셋에서 원위치로

        // 호흡 루프: 상하로 미세하게 흔들림. 인트로 중엔 감쇠해서 등장과 충돌 안 나게.
        float breathOff = 0f;
        if (breathingEnabled && breathingPeriod > 0f)
        {
            float phase = Time.time * (Mathf.PI * 2f / breathingPeriod);
            breathOff = Mathf.Sin(phase) * breathingAmplitudePx * introT;
        }

        float w = npcSize.x * scale;
        float h = npcSize.y * scale;
        float cx = baseX + npcSize.x * 0.5f;
        float cy = npcY  + npcSize.y * 0.5f;
        var rect = new Rect(
            cx - w * 0.5f + slideOff.x,
            cy - h * 0.5f + slideOff.y + breathOff,
            w, h);

        var prev = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.DrawTexture(rect, _npcTex, ScaleMode.ScaleToFit);
        GUI.color = prev;
    }

    /// <summary>
    /// 배틀/맵/마을 공용 상단 HUD (HP/Gold/Potion/Relic + Floor/Total).
    /// 치트 진입 등으로 CurrentMap이 null이어도 HUD 는 여전히 그려짐 (Floor 정보만 RunState 값 사용).
    /// </summary>
    private void DrawHeader(RunState run)
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null) return;
        var battleUI = gsm.GetComponent<BattleUI>();
        if (battleUI == null)
        {
            Debug.LogWarning("[VillageUI] BattleUI 컴포넌트 없음 — 상단 HUD 생략.");
            return;
        }

        // HUD 스트립 + 구분선은 BattleUI.DrawTopBar가 공통 처리 (BattleUI Inspector의 Village 색 사용).
        var map = gsm.CurrentMap;
        int currentFloor = map != null ? map.currentFloor : run.currentFloor;
        int totalFloors = map != null ? map.totalFloors : 15; // fallback — 치트 진입 시 챕터 기본값
        battleUI.DrawTopBar(BattleUI.HudContext.Village, run, currentFloor, totalFloors);

        // 덱 뷰어 오버레이 — 상단 덱 버튼 클릭 시 열림.
        battleUI.DrawDeckViewerOverlay(gsm);
    }

    private void DrawOptions(GameStateManager gsm, RunState run)
    {
        float cardW = optionCardSize.x;
        float cardH = optionCardSize.y;
        float gap = optionCardGap;
        float totalW = cardW * 2 + gap;
        float startX = (RefW - totalW) * 0.5f + optionCardXOffset;
        float startY = (RefH - cardH) * 0.5f + optionCardYOffset;

        var leftRect = new Rect(startX, startY, cardW, cardH);
        var rightRect = new Rect(startX + cardW + gap, startY, cardW, cardH);

        int healAmount = Mathf.Max(1, Mathf.RoundToInt(run.playerMaxHp * restHealPct));
        int afterHp = Mathf.Min(run.playerCurrentHp + healAmount, run.playerMaxHp);
        int pctLabel = Mathf.RoundToInt(restHealPct * 100f);

        var ev = Event.current;
        bool leftHover = leftRect.Contains(ev.mousePosition);
        bool rightHover = rightRect.Contains(ev.mousePosition);

        DrawOptionCard(
            leftRect,
            _treasureIconTex,
            treasureTitle,
            treasureName,
            treasureNameColor,
            treasureDesc,
            treasureGlowColor,
            leftHover);

        string restBody = $"Recover {pctLabel}% HP\n<color=#E84A4A>{run.playerCurrentHp} → {afterHp}</color>";
        DrawOptionCard(
            rightRect,
            _restIconTex,
            restTitle,
            restName,
            restNameColor,
            restBody,
            restGlowColor,
            rightHover);

        if (GUI.Button(leftRect, GUIContent.none, GUIStyle.none))
        {
            _pending.Add(() => GameStateManager.Instance?.OpenVillageTreasure());
        }
        if (GUI.Button(rightRect, GUIContent.none, GUIStyle.none))
        {
            _pending.Add(() => GameStateManager.Instance?.RestAtVillage());
        }
    }

    private void DrawOptionCard(Rect rect, Texture2D icon, string title, string name, Color nameColor, string description, Color glowColor, bool hover)
    {
        // 호버 시 카드 전체를 하나의 단위로 스케일 — GUI.matrix 로 중심 기준 확대.
        Matrix4x4 prevMatrix = GUI.matrix;
        if (hover && optionHoverScale > 1f)
        {
            GUIUtility.ScaleAroundPivot(new Vector2(optionHoverScale, optionHoverScale), rect.center);
        }

        // 글로우
        if (_glowTex != null)
        {
            float pad = hover ? cardGlowPadHover : cardGlowPadNormal;
            var glowRect = new Rect(rect.x - pad, rect.y - pad, rect.width + pad * 2, rect.height + pad * 2);
            var prev = GUI.color;
            GUI.color = new Color(glowColor.r, glowColor.g, glowColor.b, hover ? cardGlowAlphaHover : cardGlowAlphaNormal);
            GUI.DrawTexture(glowRect, _glowTex, ScaleMode.StretchToFill);
            GUI.color = prev;
        }

        // 말뚝 접촉 그림자 — 패널 아래에 깔아서 말뚝 끝이 땅에 박힌 느낌.
        if (plantedShadowEnabled && _glowTex != null)
        {
            var prevShadow = GUI.color;
            GUI.color = plantedShadowColor;
            float shadowCy = rect.yMax + plantedShadowYOffset;
            float[] xFactors = { plantedStakeXFactors.x, plantedStakeXFactors.y };
            foreach (var xf in xFactors)
            {
                float cx = rect.x + rect.width * xf;
                var sRect = new Rect(
                    cx - plantedShadowSize.x * 0.5f,
                    shadowCy - plantedShadowSize.y * 0.5f,
                    plantedShadowSize.x, plantedShadowSize.y);
                GUI.DrawTexture(sRect, _glowTex, ScaleMode.StretchToFill);
            }
            GUI.color = prevShadow;
        }

        // 패널 — 새 tribal OptionCardPanel 우선, 없으면 기존 Panel, 그것도 없으면 단색 fallback
        if (_optionCardTex != null)
            GUI.DrawTexture(rect, _optionCardTex, ScaleMode.StretchToFill);
        else if (_panelTex != null)
            GUI.DrawTexture(rect, _panelTex, ScaleMode.StretchToFill);
        else
            DrawFilledRect(rect, new Color(0.10f, 0.14f, 0.20f, 0.96f));

        // 메달리온 + 아이콘
        float medSize = medallionSize;
        float medCx = rect.center.x;
        float medCy = rect.y + rect.height * medallionCenterYFactor;
        if (drawMedallion && _medallionTex != null)
        {
            var medRect = new Rect(medCx - medSize * 0.5f, medCy - medSize * 0.5f, medSize, medSize);
            GUI.DrawTexture(medRect, _medallionTex, ScaleMode.ScaleToFit);
        }
        if (icon != null)
        {
            float iconSize = medSize * iconSizeFactor;
            float cy = medCy;

            // 1) 그라운딩 그림자 — 아이콘이 양피지에 "앉아있는" 느낌 연출. 글로우보다 먼저 깔림.
            if (iconShadowEnabled && _glowTex != null)
            {
                float shadowW = iconSize * iconShadowScale;
                float shadowH = shadowW * iconShadowVerticalSquish;
                var shadowRect = new Rect(
                    medCx - shadowW * 0.5f,
                    cy - shadowH * 0.5f + iconShadowYOffset,
                    shadowW, shadowH);
                var prev = GUI.color;
                GUI.color = iconShadowColor;
                GUI.DrawTexture(shadowRect, _glowTex, ScaleMode.StretchToFill);
                GUI.color = prev;
            }

            // 2) 글로우 (아이콘 뒤쪽) — 기본 앰버 톤으로 프레임 보석과 조화.
            if (iconGlowEnabled && _glowTex != null)
            {
                bool isRest = (icon == _restIconTex);
                bool isTreasure = (icon == _treasureIconTex);
                Color gc = isRest ? restIconGlowColor
                         : isTreasure ? treasureIconGlowColor
                         : iconGlowColor;
                float mul = isRest ? restIconGlowScaleMultiplier
                          : isTreasure ? treasureIconGlowScaleMultiplier
                          : 1f;
                float glowSize = iconSize * iconGlowScale * mul;
                var glowRect = new Rect(medCx - glowSize * 0.5f, cy - glowSize * 0.5f, glowSize, glowSize);
                var prev = GUI.color;
                GUI.color = gc;
                GUI.DrawTexture(glowRect, _glowTex, ScaleMode.StretchToFill);
                GUI.color = prev;
            }

            // 3) 아이콘 본체 — 따뜻한 틴트 적용해서 프레임 색감과 섞이게.
            var iconRect = new Rect(medCx - iconSize * 0.5f, cy - iconSize * 0.5f, iconSize, iconSize);
            var prevIcon = GUI.color;
            GUI.color = iconTint;
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
            GUI.color = prevIcon;
        }

        // 타이틀 — 상단 배너 위치(글씨만) or 기존(메달리온 아래)
        Rect titleRect;
        int prevFS = _optionTitleStyle.fontSize;
        if (useTitleBanner)
        {
            titleRect = new Rect(
                rect.center.x - titleBannerWidth * 0.5f + titleBannerOffsetX,
                rect.y + titleBannerOffsetY,
                titleBannerWidth,
                titleBannerHeight);
            _optionTitleStyle.fontSize = titleBannerFontSize;
        }
        else
        {
            titleRect = new Rect(rect.x, medCy + medSize * 0.5f + optionTitleTopGap, rect.width, optionTitleHeight);
        }
        // 외곽선 — 상하 그라데이션 (dy=-1: 위쪽=밝은 실버, dy=+1: 아래쪽=어두운 차콜, dy=0: 중간 믹스)
        if (optionTitleOutlineThickness > 0f)
        {
            var prevColor = _optionTitleStyle.normal.textColor;
            float t = optionTitleOutlineThickness;
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                Color c = dy < 0 ? optionTitleOutlineTop
                        : dy > 0 ? optionTitleOutlineBottom
                        : Color.Lerp(optionTitleOutlineTop, optionTitleOutlineBottom, 0.5f);
                _optionTitleStyle.normal.textColor = c;
                GUI.Label(new Rect(titleRect.x + dx * t, titleRect.y + dy * t, titleRect.width, titleRect.height),
                          title, _optionTitleStyle);
            }
            _optionTitleStyle.normal.textColor = prevColor;
        }
        GUI.Label(titleRect, title, _optionTitleStyle);
        _optionTitleStyle.fontSize = prevFS;

        // 설명 — 배너 모드면 메달리온 아래부터, 일반 모드면 타이틀 아래부터
        float descTop = useTitleBanner
            ? (medCy + medSize * 0.5f + optionDescTopGap)
            : (titleRect.y + titleRect.height + optionDescTopGap);
        var descRect = new Rect(rect.x + optionDescXPad, descTop, rect.width - optionDescXPad * 2f, rect.yMax - descTop - optionDescBottomPad);

        // 이름(Mystery / Heal) — 별도 폰트 크기, 색, 위치 오프셋으로 독립 렌더.
        // 호버 스케일은 GUI.matrix 가 처리하므로 폰트/오프셋은 원본 값 그대로 사용.
        int prevNameFS = _optionDescStyle.fontSize;
        var prevNameColor = _optionDescStyle.normal.textColor;
        _optionDescStyle.fontSize = nameFontSize;
        float nameH = _optionDescStyle.CalcHeight(new GUIContent(name), descRect.width);
        var nameRect = new Rect(descRect.x + nameOffset.x, descRect.y + nameOffset.y, descRect.width, nameH);

        // 이름 외곽선 — 4/8방향 오프셋.
        if (nameOutlineEnabled && nameOutlineThickness > 0f && !string.IsNullOrEmpty(name))
        {
            float t = nameOutlineThickness;
            var prevGUIColor = GUI.color;
            GUI.color = nameOutlineColor;
            _optionDescStyle.normal.textColor = Color.white; // GUI.color 곱셈 적용용
            LockStateColors(_optionDescStyle); // 호버 시 textColor 바뀌지 않게 모든 상태 통일
            GUI.Label(new Rect(nameRect.x + t, nameRect.y,     nameRect.width, nameRect.height), name, _optionDescStyle);
            GUI.Label(new Rect(nameRect.x - t, nameRect.y,     nameRect.width, nameRect.height), name, _optionDescStyle);
            GUI.Label(new Rect(nameRect.x,     nameRect.y + t, nameRect.width, nameRect.height), name, _optionDescStyle);
            GUI.Label(new Rect(nameRect.x,     nameRect.y - t, nameRect.width, nameRect.height), name, _optionDescStyle);
            if (nameOutline8Dir)
            {
                GUI.Label(new Rect(nameRect.x + t, nameRect.y + t, nameRect.width, nameRect.height), name, _optionDescStyle);
                GUI.Label(new Rect(nameRect.x - t, nameRect.y + t, nameRect.width, nameRect.height), name, _optionDescStyle);
                GUI.Label(new Rect(nameRect.x + t, nameRect.y - t, nameRect.width, nameRect.height), name, _optionDescStyle);
                GUI.Label(new Rect(nameRect.x - t, nameRect.y - t, nameRect.width, nameRect.height), name, _optionDescStyle);
            }
            GUI.color = prevGUIColor;
        }
        // 이름 드롭 섀도우 — 아래로 깔리는 그림자로 양피지에 박힌 느낌.
        if (textShadowEnabled && !string.IsNullOrEmpty(name))
        {
            var prevGUIColor = GUI.color;
            GUI.color = textShadowColor;
            _optionDescStyle.normal.textColor = Color.white;
            LockStateColors(_optionDescStyle);
            GUI.Label(new Rect(nameRect.x + textShadowOffset.x, nameRect.y + textShadowOffset.y, nameRect.width, nameRect.height), name, _optionDescStyle);
            GUI.color = prevGUIColor;
        }
        // 이름 본체 — 호버 색 변경 방지 위해 모든 상태 color 통일.
        _optionDescStyle.normal.textColor = nameColor;
        LockStateColors(_optionDescStyle);
        GUI.Label(nameRect, name, _optionDescStyle);

        // 이름 아래에 본문 렌더 — 원래 폰트/색 복구 후. 오프셋/폰트 오버라이드 인스펙터 반영.
        int baseBodyFS = bodyFontSizeOverride > 0 ? bodyFontSizeOverride : prevNameFS;
        _optionDescStyle.fontSize = baseBodyFS;
        _optionDescStyle.normal.textColor = prevNameColor;
        LockStateColors(_optionDescStyle);
        float bodyTop = nameRect.y + nameH + bodyOffset.y;
        var bodyRect = new Rect(descRect.x + bodyOffset.x, bodyTop, descRect.width, descRect.yMax - bodyTop);

        // 본문도 외곽선 (이름과 같은 설정 재사용).
        if (nameOutlineEnabled && nameOutlineThickness > 0f && !string.IsNullOrEmpty(description))
        {
            float t = nameOutlineThickness;
            var prevGUIColor = GUI.color;
            GUI.color = nameOutlineColor;
            var prevTC = _optionDescStyle.normal.textColor;
            _optionDescStyle.normal.textColor = Color.white;
            LockStateColors(_optionDescStyle);
            GUI.Label(new Rect(bodyRect.x + t, bodyRect.y,     bodyRect.width, bodyRect.height), description, _optionDescStyle);
            GUI.Label(new Rect(bodyRect.x - t, bodyRect.y,     bodyRect.width, bodyRect.height), description, _optionDescStyle);
            GUI.Label(new Rect(bodyRect.x,     bodyRect.y + t, bodyRect.width, bodyRect.height), description, _optionDescStyle);
            GUI.Label(new Rect(bodyRect.x,     bodyRect.y - t, bodyRect.width, bodyRect.height), description, _optionDescStyle);
            if (nameOutline8Dir)
            {
                GUI.Label(new Rect(bodyRect.x + t, bodyRect.y + t, bodyRect.width, bodyRect.height), description, _optionDescStyle);
                GUI.Label(new Rect(bodyRect.x - t, bodyRect.y + t, bodyRect.width, bodyRect.height), description, _optionDescStyle);
                GUI.Label(new Rect(bodyRect.x + t, bodyRect.y - t, bodyRect.width, bodyRect.height), description, _optionDescStyle);
                GUI.Label(new Rect(bodyRect.x - t, bodyRect.y - t, bodyRect.width, bodyRect.height), description, _optionDescStyle);
            }
            GUI.color = prevGUIColor;
            _optionDescStyle.normal.textColor = prevTC;
            LockStateColors(_optionDescStyle);
        }

        // 본문 드롭 섀도우
        if (textShadowEnabled && !string.IsNullOrEmpty(description))
        {
            var prevGUIColor = GUI.color;
            GUI.color = textShadowColor;
            var prevTC = _optionDescStyle.normal.textColor;
            _optionDescStyle.normal.textColor = Color.white;
            LockStateColors(_optionDescStyle);
            GUI.Label(new Rect(bodyRect.x + textShadowOffset.x, bodyRect.y + textShadowOffset.y, bodyRect.width, bodyRect.height), description, _optionDescStyle);
            GUI.color = prevGUIColor;
            _optionDescStyle.normal.textColor = prevTC;
            LockStateColors(_optionDescStyle);
        }

        GUI.Label(bodyRect, description, _optionDescStyle);
        _optionDescStyle.fontSize = prevNameFS;

        // 호버 스케일 복구.
        GUI.matrix = prevMatrix;
    }

    // 주어진 두께로 rect의 4변 외곽선 그리기 (t<=0이면 생략)
    private static void DrawBorderRect(Rect r, float t, Color c)
    {
        if (t <= 0f) return;
        DrawFilledRect(new Rect(r.x, r.y, r.width, t), c);
        DrawFilledRect(new Rect(r.x, r.yMax - t, r.width, t), c);
        DrawFilledRect(new Rect(r.x, r.y, t, r.height), c);
        DrawFilledRect(new Rect(r.xMax - t, r.y, t, r.height), c);
    }

    // =========================================================
    // 리소스 / 스타일
    // =========================================================

    private void EnsureStyles()
    {
        if (_stylesReady) return;

        // VillageUI 전용 에셋 폴더 — 다른 UI에서 분리되어 독립적으로 튜닝 가능.
        _panelTex        = Resources.Load<Texture2D>("VillageUI/Panel");
        _rowTex          = Resources.Load<Texture2D>("VillageUI/RowButton");
        _medallionTex    = Resources.Load<Texture2D>("VillageUI/MedallionRing");
        _continueTex     = Resources.Load<Texture2D>("VillageUI/ContinueButton");
        _campIconTex     = Resources.Load<Texture2D>("VillageUI/Node_Camp");
        _treasureIconTex = Resources.Load<Texture2D>("VillageUI/TreasureChest");
        _restIconTex     = Resources.Load<Texture2D>("VillageUI/RestHeart");
        _hpIconTex       = Resources.Load<Texture2D>("VillageUI/HP");
        _bgTex           = Resources.Load<Texture2D>("VillageUI/BackGround");
        _npcTex          = Resources.Load<Texture2D>("VillageUI/NPC");
        _optionCardTex   = Resources.Load<Texture2D>("VillageUI/OptionCardPanel");

        _displayFont = Resources.Load<Font>("Fonts/Cinzel-VariableFont_wght");

        _glowTex = CreateRadialGlowTexture(64);

        _titleStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFont, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold,
        };
        _subStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFont, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold,
        };
        _optionTitleStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFont, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold,
        };
        _optionDescStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFont, alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold, wordWrap = true, richText = true,
        };
        _hpStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFont, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold,
        };

        ApplyStyleValues();
        _stylesReady = true;
    }

    // Inspector 값이 바뀌면 매 프레임 스타일에 반영
    private void ApplyStyleValues()
    {
        if (_titleStyle == null) return;
        _titleStyle.fontSize = titleFontSize;
        _titleStyle.normal.textColor = titleColor;
        _subStyle.fontSize = subtitleFontSize;
        _subStyle.normal.textColor = creamColor;
        _optionTitleStyle.fontSize = optionTitleFontSize;
        _optionTitleStyle.normal.textColor = optionTitleColor;
        _optionDescStyle.fontSize = optionDescFontSize;
        _optionDescStyle.normal.textColor = optionDescColor;
        _hpStyle.fontSize = hpFontSize;
        _hpStyle.normal.textColor = hpTextColor;

        // 호버/액티브 시 색 변경 방지 — 모든 state에 동일 색 복사
        LockStateColors(_titleStyle);
        LockStateColors(_subStyle);
        LockStateColors(_optionTitleStyle);
        LockStateColors(_optionDescStyle);
        LockStateColors(_hpStyle);
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
    // Util
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
}
