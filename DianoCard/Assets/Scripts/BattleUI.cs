using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using DianoCard.Battle;
using DianoCard.Data;
using DianoCard.Game;
using UnityEngine;
using static DianoCard.Data.LocaleSettings;

/// <summary>
/// 전투 화면 IMGUI 프로토타입.
/// GameStateManager가 있을 때만 동작하며, State == Battle일 때만 그려짐.
///
/// 진입: GameStateManager.StartNewRun() 또는 ProceedAfterReward()가
///       State를 Battle로 바꾸면, 이 컴포넌트가 CurrentRun을 바탕으로
///       BattleManager를 초기화함.
///
/// 종료: _battle.state.IsOver가 감지되면 1.5초 대기 후
///       GameStateManager.EndBattle(won, hp)로 결과 전달 → 상태 전환.
/// </summary>
public class BattleUI : MonoBehaviour
{
    // 가상 해상도 — 실제 화면 크기에 맞춰 스케일링됨
    private const float RefW = 1280f;
    private const float RefH = 720f;

    private BattleManager _battle;
    /// <summary>치트/훈련장 UI에서 현재 전투의 매니저에 접근하기 위한 퍼블릭 getter.</summary>
    public BattleManager Battle => _battle;
    private bool _battleInitialized;
    private bool _battleEndQueued;
    private float _battleEndDelay;

    // 전투 진입 시 검은 페이드아웃 — Map→Battle 전환 동안 카메라 클리어 컬러(파란 빈 화면) 노출 차단.
    // _battle 초기화 완료된 프레임부터 카운트 시작, BattleEnterFadeDuration 동안 1→0 알파.
    private float _battleEnterFadeStart = -1f;
    private const float BattleEnterFadeDuration = 0.35f;

    // 타겟팅 모드: 공격 카드 클릭 후 적 클릭 대기 중 (-1 = 비활성)
    private int _targetingCardIndex = -1;
    // 소환수 공격 타겟팅: 공룡 클릭 후 적 클릭 대기 중 (-1 = 비활성).
    // _targetingCardIndex와 상호 배타적 — 하나가 활성이면 다른 하나는 자동 해제.
    private int _targetingSummonIndex = -1;
    // 공룡 스킬 타겟팅: 스킬 핀 클릭 후 적 클릭 대기 중 (-1 = 비활성). target=ENEMY 스킬에서만 사용.
    // _targetingSummonIndex / _targetingCardIndex와 상호 배타적.
    private int _targetingSummonSkillIndex = -1;
    // 공룡 교체 모드: 필드 꽉 찬 상태에서 SUMMON 카드 클릭 후 교체할 필드 공룡 클릭 대기 중 (-1 = 비활성).
    private int _swapFromCardIndex = -1;
    // 포션 사용 타겟팅: 상단 슬롯 포션 클릭 후 적/아군 클릭 대기 중 (-1 = 비활성).
    // ENEMY 타겟 포션 → 적 클릭, ALLY 타겟 포션 → 아군 공룡 클릭.
    // SELF / ALL_* 포션은 즉시 사용되어 이 값이 켜지지 않음.
    private int _targetingPotionIndex = -1;

    // 포션 아이콘 클릭 시 열리는 "마시기" 팝업 슬롯 (-1 = 닫힘).
    private int _selectedPotionIndex = -1;

    // 직전 프레임에 호버된 손패 인덱스. 호버 시 카드가 확대되어 원본 부채꼴 영역을 벗어날 수 있어,
    // 확대된 hoverRect 기준으로 hover를 유지(끈적이게)하기 위한 sticky 상태.
    private int _handStickyHoverIdx = -1;

    // 융합 모드: UTILITY/FUSION 카드가 _targetingCardIndex로 지정된 상태에서, 첫 재료를 선택 → 두 번째 선택 → 실행.
    // _fusionMaterialAPicked == false면 첫 번째 재료 대기 중, true면 두 번째 대기 중.
    // _fusionMaterialA는 선택된 재료의 (필드/손, 인덱스) 기록.
    private bool _fusionMaterialAPicked;
    private DianoCard.Battle.FusionMaterial _fusionMaterialA;

    // 증원(C156) 픽커 모드: REINFORCE 카드 클릭 시 활성. -1이면 비활성, 그 외엔 손패 카드 인덱스.
    // 활성 동안 보유 덱(run.deck)의 T0 SUMMON 그리드 모달이 화면 중앙에 뜬다.
    // 모달에서 카드 클릭 → BattleManager.PlayCard(idx, …, reinforcementCardId: id) 호출.
    // 우클릭/모달 외부 클릭 → 취소.
    private int _reinforcePickerCardIndex = -1;
    private Vector2 _reinforcePickerScroll;

    // 동족포식 모드: CANNIBAL 패시브 보유자(마준가)의 송곳니 뱃지 클릭 시 활성.
    // -1이면 비활성, 그 외엔 eater의 필드 인덱스. 다른 아군 클릭 → BattleManager.FeedCannibal.
    // 우클릭/같은 뱃지 재클릭 → 취소.
    private int _cannibalFeedFromIndex = -1;

    // StS 스타일 타겟팅 화살표 — 매 OnGUI 프레임 리셋되고 DrawHand/DrawSummon이 source를,
    // DrawEnemy/DrawSummon의 타겟팅 브랜치가 target 후보 rect를 채운다. 융합 모드에는 사용하지 않음.
    private bool _arrowSourceValid;
    private Rect _arrowSourceRect;
    private readonly List<Rect> _arrowTargetRects = new();
    // 곡선 위에 찍히는 부드러운 동그라미 + 화살촉용 텍스처(중심 1.0, 가장자리 0.0 알파).
    private Texture2D _arrowDotTex;

    // 공룡 공격 타겟팅 중 마우스가 적 위에 올라갔을 때 화살표 호 위에 띄울 데미지 프리뷰.
    // DrawEnemy(summon-attack 브랜치)에서 매 프레임 채우고 DrawTargetingArrow에서 그린다.
    // 약화/기습/취약을 모두 반영한 실효 데미지를 보여줘서 1.5배 표기 여부를 적별로 정확히 표시.
    private EnemyInstance _attackPreviewEnemy;
    private int _attackPreviewDamage;

    // 패시브 호버 툴팁 — 프레임마다 리셋. 해당 프레임에 마우스가 칩 위에 있으면 채워진다.
    private string _hoveredPassiveTitle;
    private string _hoveredPassiveBody;
    private GUIStyle _passiveChipStyle;
    private GUIStyle _tooltipTitleStyle;
    private GUIStyle _tooltipBodyStyle;

    // 손패 숨김 토글 — 공룡/전투 장면이 카드에 가려질 때 카드를 화면 아래로 슬라이드해서 살짝만 보이게.
    // _handHidden은 목표 상태, _handHideProgress는 선형 진행도(0=표시, 1=숨김 상태),
    // 드로우 시 ease-in-out 커브를 적용해 "스르륵" 부드럽게 내려가는 느낌.
    // HandHideDistance=130 → 카드 상단이 555→685로 내려가 30px 정도의 상단만 드러남.
    private bool _handHidden;
    private float _handHideProgress;
    private const float HandHideDuration = 0.9f;
    private const float HandHideDistance = 130f;

    // EndTurn 애니메이션: 소환수→적 순차 lunge 모션
    private bool _endTurnAnimating;
    private object _attackingUnit;       // 현재 lunge 중인 SummonInstance 또는 EnemyInstance
    private float _attackProgress;       // 0..1
    private const float LungePixels = 70f;
    private const float LungeDuration = 0.70f;
    private const float BetweenAttacksPause = 0.30f;
    private const float MultiAttackHitGap = 0.10f; // MULTI_ATTACK hit 사이의 짧은 호흡 — 연타감.

    // 플레이어(Arkane) 공격 모션 총 길이. attack/ 9프레임 시퀀스 + 화염구 발사 타이밍 모두 이 값에 동기화.
    private const float PlayerAttackDuration = 0.75f;

    // 화염구가 적에 도달하는 시점 (launchDelay = 0.75*0.55 = 0.4125s + flight 0.55s ≈ 0.96s).
    // PlayCard 호출을 이 시점까지 지연 → 데미지/HP/상태 업데이트가 시각적 임팩트와 동기화.
    private const float PlayerFireballImpactDelay = 0.96f;

    // EndTurn 시 손패 카드가 버린 더미로 날아가는 애니메이션.
    // 3단계: (1) 화면 중앙으로 모이며 아치형으로 떠오름 (2) 잠깐 머무름 (3) 우하단 더미로 흘러감
    // 애니메이션 구동 중에는 DrawHand가 비어있는 상태를 그리고, 날아가는 카드는 DrawDiscardFlyingCards에서 그린다.
    private struct DiscardFlyCard
    {
        public CardData data;
        public Vector2 startCenter;   // 가상 좌표상 시작 중심 (부채꼴)
        public float startAngleDeg;   // 부채꼴 회전 각도
        public Vector2 gatherTarget;  // 중앙에 모일 때의 도달 위치
        public float disperseDelay;   // 모인 뒤 버려지기 시작할 때까지의 추가 지연
    }
    private readonly List<DiscardFlyCard> _discardFlyCards = new();
    private float _discardAnimStartTime = -1f;  // -1 = 비활성
    private int _discardBaseCount;              // 애니 시작 시점의 discard pile 개수
    private const float DiscardGatherDuration   = 0.80f;  // 부채꼴 → 중앙으로 모이는 구간
    private const float DiscardHoldDuration     = 0.28f;  // 중앙에서 머무는 구간
    private const float DiscardDisperseDuration = 0.70f;  // 중앙 → 더미로 흘러가는 구간
    private const float DiscardDisperseStagger  = 0.06f;  // 카드별 흩어짐 간격
    private const float DiscardLandPulseDuration = 0.25f;
    // 모이기 단계에서 사용하는 2차 Bezier 제어점 — 곡선이 제어점에 끌려올라가며
    // 결과적으로 화면 중앙 높이를 지나가는 아치를 만든다.
    private static readonly Vector2 DiscardFlyControl = new Vector2(RefW * 0.5f, 150f);
    // 카드가 모이는 최종 지점 Y — 화면 중앙보다 살짝 위
    private const float DiscardGatherCenterY = RefH * 0.48f;
    // 모일 때 카드 간 가로 간격 (중앙을 기준으로 좌우로 배치)
    private const float DiscardGatherSpacing = 22f;

    // ---------- 소진(exhaust) 카드 소멸 애니메이션 ----------
    // 잡초(C901) 등 STATUS 카드 또는 "소진/Exhaust" 키워드를 가진 카드가 사용되면
    // 제자리에서 하얗게 덮였다가 같이 페이드아웃 — 모션/회전/스케일/글로우 일체 없음.
    // 손에서 RemoveAt되기 직전 BattleManager.OnCardExhausting 이벤트로 트리거.
    private struct ExhaustFlyCard
    {
        public CardData data;
        public Vector2 startCenter;   // 부채꼴상 출발 중심 — 애니 동안 고정
        public float startAngleDeg;   // 부채꼴 회전 각도 — 애니 동안 고정
        public float startTime;       // Time.time 기준 시작 시각
    }
    private readonly List<ExhaustFlyCard> _exhaustFlyCards = new();
    // 페이즈: 0.30s 동안 흰 오버레이가 1.0까지 올라와 카드를 덮음 → 0.20s 동안 0으로 빠짐.
    private const float ExhaustWhitenDuration    = 0.30f;
    private const float ExhaustDisappearDuration = 0.20f;
    private const float ExhaustTotalDuration     = ExhaustWhitenDuration + ExhaustDisappearDuration;

    // 손패 phantom slot — 카드가 RemoveAt된 직후 옆 카드들이 즉시 reflow하면 부자연스러우므로,
    // 사라지는 카드의 슬롯을 잠시 유지했다가 후반에 부드럽게 collapse.
    private int _exhaustPhantomIndex = -1;
    private float _exhaustPhantomStartTime = -1f;
    private const float PhantomHoldDuration     = 0.30f;  // 흰색이 다 덮일 때까지 슬롯 유지
    private const float PhantomCollapseDuration = 0.20f;  // 흰색 빠질 동안 옆 카드 부드럽게 collapse

    // ---------- 드로우 (덱 → 손패) 애니메이션 ----------
    // 버림 애니와 동일한 3단계 구조의 역방향:
    //   (1) 덱 더미에서 뒷면으로 떠올라 화면 중앙으로 모임 (아치 Bezier)
    //   (2) 중앙에서 잠깐 머물며 플립 (뒷면 → 앞면)
    //   (3) 부채꼴의 자기 자리로 흩어져 안착
    // DrawHand는 "현재 비행 중인" CardInstance를 건너뛴다.
    private struct DrawFlyCard
    {
        public CardInstance instance;    // state.hand의 실제 참조 (skip 판별용)
        public CardData data;
        public int targetIndex;          // 부채꼴 상에서 도달할 인덱스
        public Vector2 gatherTarget;     // 중앙에 모일 때의 도달 위치
        public float disperseDelay;      // 모인 뒤 자기 자리로 날아갈 때까지의 추가 지연
    }
    private readonly List<DrawFlyCard> _drawFlyCards = new();
    private readonly HashSet<CardInstance> _drawFlyingInstances = new();
    private float _drawAnimStartTime = -1f;
    private int _drawTotalHandCount;     // 애니 시점 손패 총 개수 (부채꼴 기하에 사용)
    // 버림 애니와 대칭되는 페이즈 길이 — 전체 톤을 맞추기 위해 같은 값 사용
    private const float DrawGatherDuration   = 0.80f;  // 덱 → 중앙 모임
    private const float DrawHoldDuration     = 0.32f;  // 중앙에서 머무름 (플립이 일어남)
    private const float DrawDisperseDuration = 0.70f;  // 중앙 → 부채꼴 자리
    private const float DrawDisperseStagger  = 0.06f;

    // ---------- Reshuffle (버림 → 덱) 애니메이션 ----------
    // 덱이 비었을 때 Draw() 내부에서 discard를 deck으로 옮기고 셔플하는데,
    // 이 전환이 시각적으로 "뚝" 끊어지지 않도록 카드들이 우측 버림 더미에서
    // 좌측 덱 더미로 흘러가는 스트림 애니메이션을 보여준다.
    // 카드 정체성은 중요하지 않고(어차피 셔플됨), 뒷면 N장이 이동하는 것처럼 연출.
    private struct ReshuffleFlyCard
    {
        public float delay;          // 애니 시작 이후 출발 지연 (stagger)
        public float rotSpin;        // 비행 중 회전량 (살짝 뒤뚱거리는 느낌)
    }
    private readonly List<ReshuffleFlyCard> _reshuffleFlyCards = new();
    private float _reshuffleAnimStartTime = -1f;
    private int _reshuffleTotalCards;  // 옮겨지는 총 카드 수 (= 애니 시작 시점 discard 개수)
    private const float ReshuffleFlyDuration = 0.48f;
    private const float ReshuffleFlyStagger  = 0.035f;

    // OnGUI에서 state를 즉시 변경하면 Layout/Repaint 이벤트 간 불일치로
    // ArgumentException이 뜨므로, 버튼 클릭 시에는 액션을 지연시켜 Update에서 실행.
    private readonly List<Action> _pending = new();

    // 배경 텍스처 (적 타입에 따라 자동 선택)
    private Texture2D _backgroundTexture;

    // CH01 배경 프리로드 캐시 — Start에서 한 번 로드, InitBattleFromRunState 시 즉시 사용해
    // 첫 전투 진입 시 1920x1080 PNG 동기 디코딩 스파이크 제거.
    private Texture2D _bgCh1Normal;
    private Texture2D _bgCh1Elite;
    private Texture2D _bgCh1Boss;

    // 배경을 world-space로 렌더링해서 파티클이 배경 위에 나오게 한다.
    // (IMGUI는 world 렌더링 뒤에 그려지므로, OnGUI로 배경을 그리면 파티클이 가려짐)
    private SpriteRenderer _worldBgSr;

    // 손패/마나 공용 텍스처.
    private Texture2D _cardCountBadgeTexture;
    private Texture2D _manaFrameTexture;

    // ===== 덱 뷰어 스킨 (DeckUi 폴더) =====
    [Header("Deck Viewer — Skin")]
    [Tooltip("덱 모달 9-slice border (px). 코너 필리그리가 안 늘어나도록 조정. native 1200×864.")]
    [SerializeField] private Vector2Int _deckPanelBorder = new Vector2Int(150, 150);

    [Header("Deck Viewer — Layout / Panel")]
    [Tooltip("덱 모달 폭 (RefW=1280 가상 픽셀).")]
    [SerializeField, Range(400f, 1260f)] private float _deckPanelW = 1160f;
    [Tooltip("덱 모달 높이 (RefH=720 가상 픽셀).")]
    [SerializeField, Range(300f, 700f)] private float _deckPanelH = 660f;

    [Header("Deck Viewer — Title")]
    [Tooltip("제목 좌상단 offset (panel.x+x, panel.y+y).")]
    [SerializeField] private Vector2 _deckTitleOffset = new Vector2(60f, 30f);
    [SerializeField, Range(12, 40)] private int _deckTitleFontSize = 24;

    [Header("Deck Viewer — Title Divider")]
    [Tooltip("타이틀 아래 구분선 Y (panel.y+이값).")]
    [SerializeField, Range(0f, 200f)] private float _deckDividerY = 72f;
    [Tooltip("좌우 안쪽 padding (panel.x+이값 ~ panel.xMax-이값).")]
    [SerializeField, Range(0f, 200f)] private float _deckDividerSidePadding = 30f;
    [Tooltip("두께 (px). 0이면 안 그림.")]
    [SerializeField, Range(0f, 6f)] private float _deckDividerThickness = 1.5f;
    [Tooltip("구분선 색.")]
    [SerializeField] private Color _deckDividerColor = new Color(0.72f, 0.58f, 0.32f, 0.65f);

    [Header("Deck Viewer — Close (✕)")]
    [Tooltip("우상단 offset (panel.xMax-x, panel.y+y).")]
    [SerializeField] private Vector2 _deckCloseOffset = new Vector2(53f, 15f);
    [SerializeField] private Vector2 _deckCloseSize = new Vector2(34f, 34f);
    [SerializeField, Range(12, 48)] private int _deckCloseFontSize = 40;

    [Header("Deck Viewer — Sort Tabs")]
    [Tooltip("탭 시작 위치 (panel.x+x, panel.y+y).")]
    [SerializeField] private Vector2 _deckTabStart = new Vector2(40f, 90f);
    [SerializeField, Range(60f, 280f)] private float _deckTabW = 100f;
    [SerializeField, Range(20f, 80f)] private float _deckTabH = 40f;
    [SerializeField, Range(0f, 30f)] private float _deckTabGap = 10f;
    [SerializeField, Range(8, 28)] private int _deckTabFontSize = 13;

    [Header("Deck Viewer — Card Grid")]
    [SerializeField, Range(2, 10)] private int _deckGridCols = 6;
    [SerializeField, Range(0f, 80f)] private float _deckGridPadX = 49.6f;
    [SerializeField, Range(0f, 30f)] private float _deckCellGap = 12f;
    [Tooltip("탭 아래 그리드 시작까지의 여백 (gridTop = tabsY + tabH + this).")]
    [SerializeField, Range(0f, 80f)] private float _deckGridTopGap = 14f;
    [Tooltip("패널 하단까지의 여백 (gridBottom = panel.yMax - this).")]
    [SerializeField, Range(0f, 60f)] private float _deckGridBottomPad = 18f;
    [Tooltip("카드 height/width 비율.")]
    [SerializeField, Range(1.0f, 2.0f)] private float _deckCardAspect = 1.35f;

    [Header("Deck Viewer — ×2 Badge")]
    [Tooltip("메달 폭 / 카드 폭.")]
    [SerializeField, Range(0.15f, 0.5f)] private float _deckBadgeWidthRatio = 0.30f;
    [Tooltip("메달 height/width 비율. 56×40 ≈ 0.71, 정사각=1.0.")]
    [SerializeField, Range(0.4f, 1.2f)] private float _deckBadgeAspect = 0.78f;
    [Tooltip("카드 우상단 기준 offset. X=오른쪽 안쪽으로 들여보내는 px (음수면 카드 밖으로), Y=아래로 내리는 px.")]
    [SerializeField] private Vector2 _deckBadgeOffset = new Vector2(8f, 6f);
    [Tooltip("폰트 크기 (메달 height 대비 비율).")]
    [SerializeField, Range(0.3f, 0.9f)] private float _deckBadgeFontRatio = 0.55f;
    [Tooltip("×N 텍스트 색.")]
    [SerializeField] private Color _deckBadgeTextColor = new Color(1f, 0.95f, 0.60f, 1f);
    [Tooltip("×N 텍스트 외곽선 색.")]
    [SerializeField] private Color _deckBadgeOutlineColor = new Color(0f, 0f, 0f, 0.85f);
    [Tooltip("×N 텍스트 외곽선 두께 (px). 0이면 외곽선 없음.")]
    [SerializeField, Range(0f, 4f)] private float _deckBadgeOutlinePx = 1f;
    [Tooltip("×N 텍스트 위치 미세조정 (메달 중앙 기준 px).")]
    [SerializeField] private Vector2 _deckBadgeTextOffset = new Vector2(0f, 0f);

    private Texture2D _deckPanelFrameTex;
    private Texture2D _deckTabSelectedTex;
    private Texture2D _deckTabUnselectedTex;
    private Texture2D _deckBadgeTex;
    private Texture2D _manaOrbTexture; // 좌하단 마나 오브 본체 — 다크판타지 톤 디테일 에셋. 없으면 _manaFrameTexture로 폴백.
    private Texture2D _manaOrbAkaneTexture; // CH002(아케네) 전용 빨강 오브 — InGame/Icon/Mana_Akane.png
    private Texture2D _manaOrbRinneTexture; // CH002B(린네) 전용 초록 오브 — InGame/Icon/Mana_Rinne.png
    private Texture2D _shieldFxTexture;

    // YJ 통합 프레임 (2026-04-28) — 카드 종류별 프리렌더 PNG 한 장.
    // 외곽/명판/아트 윈도우/코스트 보석이 모두 포함되어 있어 단일 레이어로 그린다.
    // 희귀도는 더 이상 시각적으로 구분되지 않는다.
    private Texture2D _frameSummon;
    private Texture2D _frameMagic;
    private Texture2D _frameBuff;
    private Texture2D _frameUtility;
    private Texture2D _frameRitual;

    // 상단 HUD 아이콘
    private Texture2D _iconHP;
    private Texture2D _iconGold;
    private Texture2D _iconMana;
    private Texture2D _iconPotion;
    private Texture2D _iconRelic;
    private Texture2D _iconDeck;
    private Texture2D _iconDiscard;
    private Texture2D _iconCardBack;  // 드로우 애니메이션의 뒷면 표시용
    private Texture2D _iconDeckRinne;     // CH002B 린네 전용 초록 젬 덱
    private Texture2D _iconDiscardRinne;  // CH002B 린네 전용 초록 젬 디스카드
    private Texture2D _iconCardBackRinne; // CH002B 린네 전용 초록 젬 카드백
    private Texture2D _iconFloor;
    private Texture2D _iconTechTree;
    private Texture2D _iconTurn;
    // 신규 보상/포인트 획득 알림 — 유물/포션/테크트리 아이콘 우상단에 오버레이로 그려짐.
    private Texture2D _iconAlertNew;
    // 인텐트/상태이상이 공유하는 머리 위·HP바 아래 아이콘 풀 (Resources/InGame/HeadIcon/<ID>.png)
    private Dictionary<string, Texture2D> _headIcons;
    private static readonly string[] HeadIconIds = {
        "ATTACK","MULTI_ATTACK","DEFEND","BUFF","DEBUFF","SUMMON","COUNTDOWN",
        "HEAL","UNKNOWN","POISON","VULNERABLE","WEAK","BIND","FEAR","STOLEN",
        "STRENGTH","WARD","ROOTED",
        "MULTI_ACTION","MOSS_LEAF",
        "TARGET_PLAYER","TARGET_DINO",
    };
    private Texture2D _topBarBg;
    private Texture2D _endTurnButtonTex;
    private Texture2D _endTurnButtonAkane; // CH002 아케네 전용
    private Texture2D _endTurnButtonRinne; // CH002B 린네 전용
    private Texture2D _hudDividerTexMap;     // 맵 전용 구분선 — Map/divider_map
    private Texture2D _hudDividerTexBattle;  // 전투/마을 공용 구분선 — InGame/divider_battle (없으면 스킵)
    private float _endTurnHoverScale = 1f;

    // 카드 위에 표시되는 일러스트 (카드 id → 텍스처). 카테고리별 CardArt/{Spell|Summon|Utility}/.
    private readonly Dictionary<string, Texture2D> _cardSprites = new();
    // 필드 위에 그려지는 공룡 스프라이트 (투명 배경). Dinos/ 폴더.
    private readonly Dictionary<string, Texture2D> _fieldDinoSprites = new();
    // 공룡 평타 모션 프레임. Dinos/animation/<filename>/attack_f01..f12. 카드 id → 프레임 배열.
    // AnimateLunge가 진행되는 동안 _attackProgress 비율로 프레임 인덱스 계산해 텍스처 스왑.
    // 폴더가 없는 진화체(_T1/_T2 등)는 키가 없어 폴백으로 정적 idle만 표시됨.
    private readonly Dictionary<string, Texture2D[]> _fieldDinoAttackFrames = new();
    // T1/T2 공룡 전용 attack scale boost — PhotoRoom 타이트크롭으로 attack 캔버스가 idle보다 큰 비율(wScale/hScale).
    // _tools/measure_dino_t12_boost.py 결과: boost = idle_h / atk_h. attack 그릴 때 wScale, hScale 양쪽에 곱해 body 높이를 idle과 매칭.
    // T0(베이스 공룡)는 attack 캔버스가 swing margin 포함하도록 author되어 있어 보정 불필요.
    private static readonly Dictionary<string, float> _fieldDinoT12AttackScaleBoost = new Dictionary<string, float>()
    {
        { "C004_T1", 0.7929f }, { "C004_T2", 0.7824f },  // Raptor
        { "C005_T1", 1.3029f }, { "C005_T2", 0.6940f },  // Carnotaurus
        { "C008_T1", 0.6556f }, { "C008_T2", 0.6926f },  // T-Rex
        { "C010_T1", 0.5867f }, { "C010_T2", 0.5850f },  // Compsognathus
        { "C012_T1", 0.6981f }, { "C012_T2", 0.6962f },  // Allosaurus
        { "C018_T1", 0.7300f }, { "C018_T2", 0.7300f },  // Giganotosaurus
        { "C019_T1", 0.7025f }, { "C019_T2", 0.6992f },  // Troodon
        { "C020_T1", 0.7082f }, { "C020_T2", 0.7072f },  // Baryonyx
        { "C021_T1", 0.6613f }, { "C021_T2", 0.6398f },  // Acrocanthosaurus
        { "C022_T1", 0.6146f }, { "C022_T2", 0.6169f },  // Carcharodontosaurus
    };
    // 공룡 패시브 타입 아이콘. Dinos/passive/<lowercase_enum>.png
    private readonly Dictionary<DinoPassiveType, Texture2D> _passiveIcons = new();

    // 적 스프라이트 (적 id → 텍스처). Start()에서 한 번만 로드.
    private readonly Dictionary<string, Texture2D> _enemySprites = new();

    // 플레이어 캐릭터 스프라이트 (필드 위에 서있는 모습)
    private Texture2D _playerSprite;
    // 애니메이션용 world-space 뷰 (Phase 1)
    private BattleEntityView _playerView;
    private string _loadedPlayerCharacterId; // _playerView가 어떤 캐릭터로 로드됐는지 — 캐릭터 변경 시 재생성용
    private bool _rewardDimmed;
    private SpriteRenderer _rewardDimOverlay;
    private static readonly Color RewardOverlayColor = new Color(0f, 0f, 0f, 0.4f);
    private Sprite _playerWorldSprite;

    // 적 애니메이션 뷰 (적 id → world Sprite, EnemyInstance → view)
    private readonly Dictionary<string, Sprite> _enemyWorldSprites = new();
    private readonly Dictionary<EnemyInstance, BattleEntityView> _enemyViews = new();

    // E901 이끼 잡몹 — 4코너 전용 스프라이트 + 코너별 원근 스케일.
    // ComputeSlotPositions에서 코너 인덱스로 스왑하고 스케일 dict에 기록 → GetEnemyDrawHeight가 읽음.
    private Sprite _mossWorldSpriteLeftUp;
    private Sprite _mossWorldSpriteRightUp;
    private Sprite _mossWorldSpriteLeftDown;
    private Sprite _mossWorldSpriteRightDown;
    private readonly Dictionary<EnemyInstance, float> _mossDepthScale = new();

    // 데미지 시 스폰되는 VFX 프리팹 (Inspector에서 할당)
    // 기본값으로 Resources 또는 AssetDatabase로는 못 불러오므로 SerializeField로 노출.
    [Header("HUD Strip & Divider (상단 네비바 공용 — Battle/Map/Village 전부)")]
    [Tooltip("HUD 스트립 배경 + 구분선 표시 여부.")]
    [SerializeField] private bool hudStripEnabled = true;
    [Tooltip("HUD 스트립 높이 (px).")]
    [SerializeField, Range(40f, 300f)] private float hudStripHeight = 74f;
    [Tooltip("배틀/마을 공용 HUD 스트립 배경색. 알파는 아래 Alpha Battle 슬라이더가 최종값을 결정.")]
    [SerializeField] private Color hudStripBgColorBattle = new(0.059f, 0.043f, 0.137f, 1f);
    [Tooltip("맵 화면용 HUD 스트립 배경색. 알파는 아래 Alpha Map 슬라이더가 최종값을 결정.")]
    [SerializeField] private Color hudStripBgColorMap = new(0.059f, 0.043f, 0.137f, 1f);
    [Tooltip("배틀/마을 공용 HUD 스트립 최종 알파. 0=완전 투명, 1=완전 불투명.")]
    [SerializeField, Range(0f, 1f)] private float hudStripAlphaBattle = 0.5f;
    [Tooltip("맵 HUD 스트립 최종 알파. 0=완전 투명, 1=완전 불투명.")]
    [SerializeField, Range(0f, 1f)] private float hudStripAlphaMap = 0.84f;
    [Tooltip("구분선 중심 Y (px). 기본적으로 스트립 하단 경계와 맞춤.")]
    [SerializeField, Range(0f, 400f)] private float hudDividerCenterY = 78f;
    [Tooltip("구분선 높이 (px). 붓자국 두께 느낌.")]
    [SerializeField, Range(2f, 600f)] private float hudDividerHeight = 120f;
    [Tooltip("가로 오버스캔 (px). 양끝 페이드를 화면 밖으로 밀어 가장자리까지 선이 이어지게. (Width가 0일 때만 사용)")]
    [SerializeField, Range(0f, 600f)] private float hudDividerOverscan = 600f;
    [Tooltip("구분선 가로 길이 (px). 0이면 오버스캔 기반 자동(전체+오버스캔). >0이면 이 값 직접 사용해 가운데 정렬.")]
    [SerializeField, Range(0f, 4000f)] private float hudDividerWidth = 0f;
    [Tooltip("구분선 틴트 색 + 알파. 검정-회색 스트립과 어울리는 어두운 회색으로 기본값.")]
    [SerializeField] private Color hudDividerTint = new(0.412f, 0.412f, 0.412f, 1f);
    [Tooltip("전투 HUD 바 하단 골드 트림 라인 색 + 알파. 시안 A 스타일. 알파 0이면 안 보임.")]
    [SerializeField] private Color hudBattleBottomLineColor = new(0.82f, 0.68f, 0.38f, 0.55f);
    [Tooltip("전투 HUD 바 하단 골드 트림 라인 두께 (px). 0이면 안 그림.")]
    [SerializeField, Range(0f, 12f)] private float hudBattleBottomLineThickness = 3f;

    [Header("HUD 상단 네비바 — 마스터 스케일 (한 번에 묶어서 키우기/줄이기)")]
    [Tooltip("상단 네비바 전체 크기를 비례 스케일. 1=원본, 0.5=절반, 2=두배.\n다음 모두 한꺼번에 곱해짐:\n• 스트립 높이 / 디바이더 위치+두께 / 골드 트림\n• 장식 텍스처 높이+Y오프셋\n• 아이콘 영역(barY/barH) / 아이콘 크기 / 라벨 간격 / 슬롯 간격 / 좌·우 패딩")]
    [SerializeField, Range(0.3f, 2.0f)] private float navBarMasterScale = 0.95f;

    [Header("HUD 상단 장식 텍스처 (TopBar 오버레이) — 배틀 컨텍스트 전용")]
    [Tooltip("InGame/TopBar.png 텍스처를 HUD 스트립 위에 오버레이로 그릴지.")]
    [SerializeField] private bool topBarTexEnabled = true;
    [Tooltip("오버레이 텍스처 높이 (px). HUD 스트립 높이와 무관하게 시각적 크기만 조절.")]
    [SerializeField, Range(20f, 400f)] private float topBarTexHeight = 90f;
    [Tooltip("오버레이 Y 위치 (px). 0=상단 정렬, 음수=위로, 양수=아래로.")]
    [SerializeField, Range(-200f, 200f)] private float topBarTexYOffset = -5f;
    [Tooltip("오버레이 좌우 여백 (px). 양쪽에서 안쪽으로 들이는 마진.")]
    [SerializeField, Range(0f, 300f)] private float topBarTexHorizontalInset = 0f;

    [Header("HUD 상단 슬롯 — HP/Gold/Potion/Relic/Deck/Floor 아이콘")]
    [Tooltip("상단 HUD 슬롯 아이콘 한 변 크기 (px).")]
    [SerializeField, Range(20f, 120f)] private float hudSlotIconSize = 45f;
    [Tooltip("아이콘과 라벨 사이 간격 (px).")]
    [SerializeField, Range(0f, 30f)] private float hudSlotIconLabelGap = 5.13f;
    [Tooltip("좌측 슬롯 사이의 간격 (px).")]
    [SerializeField, Range(0f, 100f)] private float hudSlotGap = 25f;

    [Header("좌하단 덱 / 우하단 디스카드 더미")]
    [Tooltip("좌하단 덱 더미 한 변 크기 (px).")]
    [SerializeField, Range(30f, 200f)] private float cornerDeckPileSize = 65f;
    [Tooltip("우하단 디스카드 더미 한 변 크기 (px).")]
    [SerializeField, Range(30f, 200f)] private float cornerDiscardPileSize = 65f;
    [Tooltip("화면 하단으로부터 더미 상단까지 거리 (px). RefH - 이 값 = 더미 top y.")]
    [SerializeField, Range(0f, 300f)] private float cornerPileTopFromBottom = 110f;
    [Tooltip("좌측 덱 더미의 좌측 X 좌표 (px).")]
    [SerializeField, Range(0f, 300f)] private float cornerPileLeftX = 22f;
    [Tooltip("우측 디스카드 더미의 우측 인셋 (px). RefW - 이 값 = 더미 left x.")]
    [SerializeField, Range(0f, 300f)] private float cornerPileRightInset = 95f;

    [Header("END TURN 버튼")]
    [Tooltip("END TURN 버튼 가로 (px).")]
    [SerializeField, Range(80f, 500f)] private float endTurnButtonWidth = 190f;
    [Tooltip("END TURN 버튼 세로 (px).")]
    [SerializeField, Range(40f, 250f)] private float endTurnButtonHeight = 95f;
    [Tooltip("화면 우측에서 버튼 우측까지 거리 (px). RefW - 이 값 = 버튼 left x.")]
    [SerializeField, Range(0f, 700f)] private float endTurnButtonRightOffset = 280f;
    [Tooltip("화면 하단에서 버튼 하단까지 거리 (px). RefH - 이 값 = 버튼 top y.")]
    [SerializeField, Range(0f, 250f)] private float endTurnButtonBottomOffset = 100f;

    [Header("손패 카드 크기 — 부채꼴 hand 전체 적용")]
    [Tooltip("손패 카드 가로 (px). 모든 hand/discard/draw 애니에 공통.")]
    [SerializeField, Range(80f, 400f)] private float handCardWidth = 157.5f;
    [Tooltip("손패 카드 세로 (px). 모든 hand/discard/draw 애니에 공통.")]
    [SerializeField, Range(120f, 600f)] private float handCardHeight = 219.45f;

    [Header("HUD 슬롯 좌/우 패딩")]
    [Tooltip("HUD 좌측 첫 슬롯 좌측 패딩 (px).")]
    [SerializeField, Range(0f, 100f)] private float hudSlotLeftPadX = 17.1f;
    [Tooltip("HUD 우측 마지막 슬롯과 화면 우측 가장자리 사이 패딩 (px).")]
    [SerializeField, Range(0f, 100f)] private float hudRightPad = 23.94f;
    [Tooltip("HUD 우측 슬롯 사이 간격 (px). 좌측 slotGap 보다 넉넉하게.")]
    [SerializeField, Range(0f, 150f)] private float hudRightSlotGap = 47.88f;

    public enum HudContext { Battle, Map, Village }

    [Header("Damage VFX Prefabs")]
    [SerializeField] private GameObject _vfxHitA;
    [SerializeField] private GameObject _vfxHitD;
    [SerializeField] private GameObject _vfxSmokeF;
    [SerializeField] private float _vfxZDistance = 10f;

    [Header("Entity Shadow (플레이어 발밑 그림자)")]
    [SerializeField, Range(0.02f, 0.4f), Tooltip("캐릭터 높이 대비 그림자 세로 길이 비율.")]
    private float _entityShadowHeight = 0.10f;
    [SerializeField, Range(0.3f, 3f), Tooltip("그림자 가로 폭 배수 (텍스처 원본 종횡비 기준).")]
    private float _entityShadowWidthScale = 1f;
    [SerializeField, Range(-0.5f, 0.5f), Tooltip("그림자 좌우 오프셋. 캐릭터 높이 대비 비율. 양수=오른쪽.")]
    private float _entityShadowOffsetX = -0.106f;
    [SerializeField, Range(-0.5f, 0.5f), Tooltip("그림자 상하 오프셋. 캐릭터 높이 대비 비율. 양수=위쪽.")]
    private float _entityShadowOffsetY = 0.106f;
    [SerializeField, Range(0f, 1f), Tooltip("그림자 알파.")]
    private float _entityShadowAlpha = 1f;

    [Header("Enemy Shadow (몬스터 발밑 그림자)")]
    [SerializeField, Tooltip("몬스터 발밑 그림자 사용 여부. 스프라이트는 Resources/Monsters/shadow/{이미지이름}_shadow.png 규칙으로 로드.")]
    private bool _enemyShadowEnabled = true;
    [SerializeField, Range(0.02f, 0.4f), Tooltip("몬스터 높이 대비 그림자 세로 길이 비율.")]
    private float _enemyShadowHeight = 0.10f;
    [SerializeField, Range(0.3f, 3f), Tooltip("그림자 가로 폭 배수 (텍스처 원본 종횡비 기준).")]
    private float _enemyShadowWidthScale = 1f;
    [SerializeField, Range(-0.5f, 0.5f), Tooltip("그림자 좌우 오프셋. 몬스터 높이 대비 비율. 양수=오른쪽.")]
    private float _enemyShadowOffsetX = 0f;
    [SerializeField, Range(-0.5f, 0.5f), Tooltip("그림자 상하 오프셋. 몬스터 높이 대비 비율. 양수=위쪽.")]
    private float _enemyShadowOffsetY = 0f;
    [SerializeField, Range(0f, 1f), Tooltip("그림자 알파.")]
    private float _enemyShadowAlpha = 1f;

    // 전투 배경 앰비언스 VFX (전투 시작 시 스폰, 종료 시 파괴)
    // 각 엔트리는 특정 배경(backgroundName)에만 스폰된다.
    // backgroundName이 비어있으면 모든 배경에 스폰.
    [Serializable]
    public class BackgroundAmbienceEntry
    {
        public string backgroundName;
        public GameObject prefab;
        public Vector2 guiPos = new Vector2(640f, 360f);
        [Range(0.05f, 2f)] public float scale = 0.25f;
        [Range(0.05f, 2f)] public float intensity = 0.3f;
    }

    // 레이어별 볼더(외곽선) 설정 — 색/두께/활성/샘플 개별 조정.
    [Serializable]
    public class LayerBorderConfig
    {
        [Tooltip("볼더 활성화.")] public bool enabled = true;
        [Tooltip("볼더 색 — alpha 낮추면 은은하게.")] public Color color = new Color(0.10f, 0.06f, 0.06f, 0.5f);
        [Tooltip("볼더 두께 (픽셀).")] [Range(0f, 12f)] public float widthPx = 2f;
        [Tooltip("샘플 개수 — 원 둘레에 균등 배치. 높을수록 부드럽지만 draw call 증가. 8=거침, 16=균형, 24+=매우 부드러움.")]
        [Range(4, 32)] public int samples = 16;
    }

    // ───────── 필드 공룡 레이아웃 (Inspector 노출) ─────────
    [Header("Field Dino Layout")]
    [Tooltip("필드 공룡 스프라이트 크기 (정사각형).")]
    [Range(100f, 400f)]
    [SerializeField] private float dinoSize = 180f;

    // ── 플레이어 캐릭터 위치 ────────────────────────────────
    [Header("Player Position")]
    [Tooltip("플레이어 캐릭터 X 중심.")]
    [Range(0f, 600f)]
    [SerializeField] private float playerX = 190f;

    // ── 1마리일 때 ──────────────────────────────────────
    [Header("Dino Position")]
    [Tooltip("1마리일 때 공룡의 X 중심.")]
    [Range(300f, 900f)]
    [SerializeField] private float dinoSingleX = 400f;

    [Tooltip("1마리일 때 공룡 발끝 Y.")]
    [Range(300f, 700f)]
    [SerializeField] private float dinoSingleFootY = 485f;

    // ── 2마리일 때 (각 슬롯 독립 컨트롤) ─────────────────────
    [Tooltip("2마리 시 슬롯 0 (앞쪽 공룡) X 중심.")]
    [Range(300f, 900f)]
    [SerializeField] private float dinoTwoSlot0X = 380f;

    [Tooltip("2마리 시 슬롯 0 (앞쪽 공룡) 발끝 Y.")]
    [Range(300f, 700f)]
    [SerializeField] private float dinoTwoSlot0FootY = 500f;

    // ── 페어 자동 패킹 (공룡별 크기는 card.csv field_scale에서 로드) ───
    [Tooltip("2마리 페어의 가로 겹침 비율. 0.55가 기존 dinoTwoSlot1X=500 셋팅과 동일한 느낌. 0=떨어져, 0.7=많이 겹침.")]
    [Range(0f, 0.7f)]
    [SerializeField] private float pairOverlapPct = 0.245f;

    [Tooltip("뒤 공룡의 발이 앞 공룡 발보다 위로 올라가는 비율 (앞 공룡 키 기준). 0.28이 기존 dinoTwoSlot1FootY=530과 동일.")]
    [Range(0f, 0.5f)]
    [SerializeField] private float pairStaggerYPct = 0.32f;

    [Tooltip("뒤 공룡 중심이 앞 공룡 중심에서 떨어져야 하는 최소 거리 (앞 공룡 너비 비율). 0.4 = 뒤 공룡이 앞 공룡 어깨 바깥에 위치. 큰 앞 공룡 + 작은 뒤 공룡 페어에서 작은 공룡이 안 가려지게.")]
    [Range(0f, 0.6f)]
    [SerializeField] private float pairMinSpacingPct = 0.4f;

    [Tooltip("앞 공룡이 뒤 공룡보다 클 때 추가로 뒤 공룡을 위로 올리는 강도. 0=비활성, 1=뒤 공룡 머리가 앞 공룡 머리에 정렬. 기본 0.8.")]
    [Range(0f, 1.5f)]
    [SerializeField] private float pairSizeStaggerBoost = 0.8f;

    // CheatPanel에서 라이브 슬라이더로 조작 가능하도록 노출.
    public float PairOverlapPct { get => pairOverlapPct; set => pairOverlapPct = Mathf.Clamp(value, 0f, 0.7f); }
    public float PairStaggerYPct { get => pairStaggerYPct; set => pairStaggerYPct = Mathf.Clamp(value, 0f, 0.5f); }
    public float PairMinSpacingPct { get => pairMinSpacingPct; set => pairMinSpacingPct = Mathf.Clamp(value, 0f, 0.6f); }
    public float PairSizeStaggerBoost { get => pairSizeStaggerBoost; set => pairSizeStaggerBoost = Mathf.Clamp(value, 0f, 1.5f); }

    // ───────── HP 바 크기 (Inspector 노출) ─────────
    // 스프라이트 크기에 비례하되 min/max로 차이 폭을 제한.
    // width = Clamp(spriteWidth × ratio, min, max)
    [Header("HP Bar")]
    [Tooltip("스프라이트 너비 대비 HP 바 너비 비율. 0.6이면 너비의 60%.")]
    [Range(0.2f, 1.2f)]
    [SerializeField] private float hpBarSpriteRatio = 0.6f;

    [Tooltip("HP 바 최소 너비 — 작은 스프라이트도 이 값 이상.")]
    [Range(50f, 200f)]
    [SerializeField] private float hpBarMinWidth = 110f;

    [Tooltip("HP 바 최대 너비 — 큰 스프라이트도 이 값 이하.")]
    [Range(100f, 300f)]
    [SerializeField] private float hpBarMaxWidth = 170f;

    [Tooltip("모든 HP 바의 고정 세로 두께.")]
    [Range(6f, 40f)]
    [SerializeField] private float hpBarHeight = 18f;

    private float ComputeHpBarWidth(float spriteW)
        => Mathf.Clamp(spriteW * hpBarSpriteRatio, hpBarMinWidth, hpBarMaxWidth);

    // ───────── 손패 부채꼴 레이아웃 (Inspector 노출) ─────────
    [Header("Hand Fan Layout")]
    [Tooltip("손패 카드의 화면 하단 노출 오프셋. 값↑ = 카드가 더 아래로 가려짐. 기본 57")]
    [Range(0f, 200f)]
    [SerializeField] private float handBottomOffset = 57f;

    [Tooltip("카드 사이 각도(도). 값↑ = 부채꼴 더 펼쳐짐. 기본 6.5")]
    [Range(0f, 20f)]
    [SerializeField] private float handAnglePerCard = 6.5f;

    [Tooltip("손패 부채꼴 최대 총 각도(도). 카드 수가 늘어도 이 값을 초과하지 않도록 간격 자동 축소. 기본 26(=4×6.5, 5장 기준)")]
    [Range(10f, 80f)]
    [SerializeField] private float handMaxTotalAngle = 26f;

    [Tooltip("부채꼴 가상 원 반지름. 값↑ = 곡률 줄어듦(평평해짐). 기본 1100")]
    [Range(400f, 2500f)]
    [SerializeField] private float handFanRadius = 1100f;

    // ───────── YJ 통합 프레임 rect 튜닝 (2026-04-28) ─────────
    // 손패/호버/날아가는 카드/덱 뷰어 — 모든 BattleUI 카드 렌더링에 적용.
    // (x, y, w, h) = 카드 rect 내부 비율.
    [Header("Card Frame (YJ 통합 프레임)")]
    [Tooltip("아트(일러스트) 영역 — 프레임의 아치형 아트 윈도우 안에 들어가도록 비율 조정.")]
    [SerializeField] private Vector4 cardArtRectV2Pct = new(0.05f, 0.20f, 0.90f, 0.50f);
    [Tooltip("카드명 텍스트 영역 — 프레임 상단.")]
    [SerializeField] private Vector4 cardNameOnRibbonRectPct = new(0.16f, 0.075f, 0.68f, 0.12f);
    [Tooltip("본문 영역 (ATK/HP 또는 설명) — 명판.")]
    [SerializeField] private Vector4 cardBodyV2RectPct = new(0.11f, 0.75f, 0.78f, 0.24f);
    [Tooltip("좌상단 코스트 보석 — (centerX, centerY, sizeFrac). 프레임의 보석 위치에 맞춤.")]
    [SerializeField] private Vector3 cardCostOrbPct = new(0.115f, 0.20f, 0.22f);

    [Header("Card Extra Tints")]
    [Tooltip("아트 일러스트 tint 곱셈. 흰색 = 원본.")]
    [SerializeField] private Color cardArtTint = Color.white;
    [Tooltip("아트 텍스처 없을 때 placeholder fill 색.")]
    [SerializeField] private Color cardArtPlaceholderTint = new(0.5f, 0.5f, 0.5f, 0.35f);

    [Header("Card State")]
    [Tooltip("플레이 불가 카드 dim 곱셈 색 (프레임 전체).")]
    [SerializeField] private Color cardDisabledDim = new(0.55f, 0.55f, 0.55f, 0.9f);

    [Header("Card Text Tints")]
    [Tooltip("카드명 텍스트 tint 곱셈 (등급 색 × 이 값).")]
    [SerializeField] private Color cardNameTextTint = Color.white;
    [Tooltip("카드명 외곽선 색.")]
    [SerializeField] private Color cardNameOutline = new(0f, 0f, 0f, 0.9f);
    [Tooltip("카드명 외곽선 두께.")]
    [SerializeField, Range(0f, 3f)] private float cardNameOutlineThickness = 1.0f;
    [Tooltip("본문(ATK/HP, 설명) 텍스트 색 — 명판 베이지 위 최대 가독성.")]
    [SerializeField] private Color cardBodyTextColor = Color.white;
    [Tooltip("본문 외곽선 색 — 필요 시 사용.")]
    [SerializeField] private Color cardBodyOutline = new(0f, 0f, 0f, 0.7f);
    [Tooltip("본문 외곽선 두께 — 0 = 외곽선 없음(기본), 0.5 = 살짝 굵게, 1.0 = 또렷한 외곽선.")]
    [SerializeField, Range(0f, 2f)] private float cardBodyOutlineThickness = 0f;
    [Tooltip("코스트 젬 숫자 색.")]
    [SerializeField] private Color cardCostTextColor = Color.white;
    [Tooltip("코스트 젬 숫자 외곽선 색.")]
    [SerializeField] private Color cardCostOutline = new(0f, 0f, 0f, 0.95f);
    [Tooltip("코스트 숫자 외곽선 두께.")]
    [SerializeField, Range(0f, 3f)] private float cardCostOutlineThickness = 1.2f;
    [Tooltip("플레이 불가 시 카드명 색.")]
    [SerializeField] private Color cardNameDisabledColor = new(0.75f, 0.75f, 0.75f, 0.9f);
    [Tooltip("플레이 불가 시 코스트 숫자 색.")]
    [SerializeField] private Color cardCostDisabledColor = new(0.75f, 0.75f, 0.75f, 0.9f);

    [Header("Card Font Sizes")]
    [Tooltip("카드명 (제목) 폰트 크기 — 기준 카드 폭(187px) 기준. 실제 폰트는 카드 폭에 비례 자동 스케일.")]
    [SerializeField, Range(6, 64)] private int cardNameFontSize = 14;
    [Tooltip("카드명 폰트 크기 — 손패 (작은 카드, drawCost=false 경로).")]
    [SerializeField, Range(6, 48)] private int cardNameFontSizeSmall = 11;
    [Tooltip("본문 (ATK/HP, 설명) 폰트 크기.")]
    [SerializeField, Range(6, 48)] private int cardBodyFontSize = 10;
    [Tooltip("코스트 젬 숫자 크기 비율 (orb 지름 × 이 비율). 0.57 = 젬의 57%.")]
    [SerializeField, Range(0.2f, 1.0f)] private float cardCostFontSizeRatio = 0.57f;

    [Header("Card Text Rects")]
    [Tooltip("코스트 숫자 위치 오프셋 (orb 중심 기준, 카드 폭 대비 비율). X=우측, Y=아래.")]
    [SerializeField] private Vector2 cardCostTextOffsetPct = new(0.001f, -0.042f);
    [Tooltip("코스트 숫자 크기 오프셋 (orb 크기 대비 비율 추가). 0 = orb 크기 그대로.")]
    [SerializeField, Range(-0.5f, 0.5f)] private float cardCostTextRectShrinkPct = 0f;

    [Header("Mana Orb (좌하단)")]
    [Tooltip("좌하단 마나 오브 지름 (RefH 좌표 기준 px).")]
    [SerializeField, Range(40f, 240f)] private float manaOrbSize = 125f;
    [Tooltip("좌하단 마나 오브 중심 X (RefW 좌표 기준 px, 좌측 0).")]
    [SerializeField, Range(40f, 400f)] private float manaOrbCenterX = 200f;
    [Tooltip("좌하단 마나 오브 중심이 화면 하단에서 떨어진 거리 (px). 클수록 위로 올라감.")]
    [SerializeField, Range(20f, 200f)] private float manaOrbBottomOffset = 70f;
    [Tooltip("마나 텍스트 크기 비율 (orb 지름 × 이 비율).")]
    [SerializeField, Range(0.10f, 0.50f)] private float manaOrbFontSizeRatio = 0.18f;
    [Tooltip("Mana Orb 안 \"3/3\" 텍스트 가로 오프셋 (오브 사이즈 대비 비율). 0=중앙, 음수=왼쪽, 양수=오른쪽.")]
    [SerializeField, Range(-0.5f, 0.5f)] private float manaOrbTextOffsetXPct = 0f;
    [Tooltip("Mana Orb 안 \"3/3\" 텍스트 세로 오프셋 (오브 사이즈 대비 비율). 0=중앙, 음수=위, 양수=아래.")]
    [SerializeField, Range(-0.5f, 0.5f)] private float manaOrbTextOffsetYPct = -0.034f;

    [Header("Battle Background Ambience")]
    [SerializeField] private List<BackgroundAmbienceEntry> _bgFxEntries = new();
    private readonly List<GameObject> _spawnedBgFx = new();

    // 배경에 오버레이되는 살랑거리는 덩굴 (SpriteRenderer + VineSway)
    [Serializable]
    public class BackgroundVineEntry
    {
        public string backgroundName;
        public string resourcePath;          // 예: "FX/Vines/Vine1"
        public Vector2 guiPos = new Vector2(640f, 50f);
        public float scale = 1f;
        public int sortingOrder = -50;        // 배경(-100)과 파티클(0) 사이
        [Range(0f, 20f)] public float swayAngle = 2f;
        [Range(0f, 5f)] public float swaySpeed = 0.5f;
        public float swayPhase = 0f;
        public bool flipX = false;
        public Color color = Color.white;

        // true면 VineSway 대신 GodRayFX 를 사용 (알파 펄스 + 회전 흔들림)
        public bool useGodRay = false;
        [Range(0f, 1f)] public float godRayMinAlpha = 0.15f;
        [Range(0f, 1f)] public float godRayMaxAlpha = 0.45f;
        public float godRayPulseSpeed = 0.6f;
    }

    [Header("Battle Background Vines")]
    [SerializeField] private List<BackgroundVineEntry> _bgVineEntries = new();
    private readonly List<GameObject> _spawnedVines = new();

    // ───────── Normal1 전용 바닥 안개 ─────────
    // LobbyUI의 "Bottom Smoke" 이미터와 같은 느낌. BG_Ch1_Battle_01 배경일 때만 렌더.
    [Header("Normal1 Bottom Fog (BG_Ch1_Battle_01 전용)")]
    [Tooltip("normal1 전투 배경에서 바닥 안개 활성화.")]
    [SerializeField] private bool _normal1FogEnabled = true;
    [Tooltip("안개 파티클 개수.")]
    [SerializeField, Range(0, 60)] private int _normal1FogCount = 9;
    [Tooltip("1280x720 가상 좌표 기준 스폰 영역 (바닥 띠).")]
    [SerializeField] private Rect _normal1FogSpawnRect = new Rect(0f, 580f, 1280f, 30f);
    [Tooltip("파티클 크기 범위(px).")]
    [SerializeField] private Vector2 _normal1FogSizeRange = new Vector2(30f, 55f);
    [Tooltip("떠오르는 높이(px).")]
    [SerializeField, Range(20f, 300f)] private float _normal1FogRiseHeight = 120f;
    [Tooltip("떠오르는 속도.")]
    [SerializeField, Range(0.05f, 1f)] private float _normal1FogRiseSpeed = 0.15f;
    [Tooltip("가로 흔들림 폭(px).")]
    [SerializeField, Range(0f, 60f)] private float _normal1FogSwayAmount = 25f;
    [Tooltip("가로 흔들림 주기.")]
    [SerializeField, Range(0.1f, 3f)] private float _normal1FogSwayFrequency = 0.4f;
    [Tooltip("안개 안쪽 색.")]
    [SerializeField] private Color _normal1FogInnerColor = new Color(0.6f, 0.55f, 0.55f, 1f);
    [Tooltip("안개 바깥 글로우 색.")]
    [SerializeField] private Color _normal1FogOuterColor = new Color(0.35f, 0.32f, 0.32f, 1f);
    [Tooltip("전체 알파 곱셈.")]
    [SerializeField, Range(0f, 2f)] private float _normal1FogAlphaMul = 0.35f;
    [Tooltip("깜빡임 속도.")]
    [SerializeField, Range(0f, 10f)] private float _normal1FogFlickerSpeed = 2f;
    [Tooltip("깜빡임 깊이(0=없음).")]
    [SerializeField, Range(0f, 1f)] private float _normal1FogFlickerDepth = 0.2f;
    [Tooltip("외곽 블룸 크기 배수.")]
    [SerializeField, Range(1f, 6f)] private float _normal1FogBloomScale = 4.5f;
    [Tooltip("외곽 블룸 알파 배수.")]
    [SerializeField, Range(0f, 1f)] private float _normal1FogBloomAlphaMul = 0.55f;
    private Texture2D _normal1FogTex;

    // HP 변화 감지용 (unit reference → 직전 프레임 hp)
    private readonly Dictionary<object, int> _lastKnownHp = new();
    // HP 바 entity별 '표시 fraction' — 실제 hp가 내려가면 이 값이 천천히 따라내려가며 pale trail을 만든다
    // Vector2 키 대신 entity reference를 써야 슬라이드 중에도 키가 안정됨
    private readonly Dictionary<object, float> _hpBarDisplayedFrac = new();
    private readonly HashSet<object> _seenThisFrame = new();

    // 떠오르는 데미지 플로터
    private readonly List<DamageFloater> _floaters = new();

    // 캐릭터 슬롯 위치 (매 OnGUI 시작 시 갱신 → 플로터가 참조)
    private readonly Dictionary<object, Vector2> _slotPositions = new();

    // 필드 소환수의 "표시용" 위치 — 슬롯 타겟 위치로 프레임마다 lerp해서 부드럽게 이동.
    // 새 소환수가 생기거나 빠져서 슬롯 레이아웃이 재계산될 때 순간이동 없이 밀려나는 연출용.
    private readonly Dictionary<SummonInstance, Vector2> _summonDisplayPositions = new();
    private const float SummonSlideSpeed = 7f;

    // 방패(블록) 이펙트 — 플레이어 block이 증가한 프레임에 트리거, 일정 시간 동안 재생
    private int _prevPlayerBlock;
    private float _playerShieldFxStartTime = -1f;
    // HP 바 파란 틴트 페이드인 시작 — block이 0→>0 으로 처음 생긴 순간에만 세팅.
    // 이미 block이 있는 상태에서 추가로 block을 쌓을 때 페이드인이 재시작되면
    // intensity가 0(=빨간 fill)부터 다시 올라가면서 바가 빨간색으로 깜빡임 → 분리.
    private float _playerBlockTintStartTime = -1f;
    private const float ShieldFxDuration = 1.2f;
    // 공룡(SummonInstance) / 적(EnemyInstance) 방어막 FX — entity 참조를 키로 시작 시각을 보관.
    // 매 프레임 block 증가를 감지해 트리거하고, DrawSummon/DrawEnemy에서 같은 비주얼로 재생한다.
    private readonly Dictionary<object, int> _lastKnownBlock = new();
    private readonly Dictionary<object, float> _entityShieldFxStart = new();

    private GUIStyle _boxStyle;
    private GUIStyle _buttonStyle;
    private GUIStyle _labelStyle;
    private GUIStyle _centerStyle;
    private GUIStyle _damageStyle;
    private GUIStyle _intentStyle;
    private GUIStyle _intentNumberStyle;
    private GUIStyle _targetHintStyle;
    private GUIStyle _cardCostStyle;
    private GUIStyle _cardNameStyle;
    private GUIStyle _cardDescStyle;
    private bool _stylesReady;

    // 덱 뷰어 — 상단 바 계단(Floor) 아이콘 왼쪽 버튼 클릭 시 오픈.
    // run.deck 전체를 id로 그룹핑해 카드 그리드로 보여주며, 정렬 탭과 스크롤 지원.
    private bool _deckViewerOpen;
    private int _deckViewerSortMode;  // 0=획득순, 1=유형, 2=비용
    private Vector2 _deckViewerScroll;

    // 덱 뷰어 source — StS 스타일. 0=전체 덱(run.deck), 1=뽑을 카드(state.deck), 2=버린 카드(state.discard).
    // 좌하 더미 클릭 → 1, 우하 더미 클릭 → 2, HUD/맵 진입 → 0.
    private int _deckViewerSource;

    // 유물 뷰어 — 상단 바 유물 슬롯 클릭 시 오픈.
    private bool _relicViewerOpen;
    private Vector2 _relicViewerScroll;
    private readonly Dictionary<string, Texture2D> _relicIconCache = new();

    // 포션 뷰어 — 상단 바 포션 슬롯 클릭 시 오픈.
    private bool _potionViewerOpen;
    private Vector2 _potionViewerScroll;

    /// <summary>덱/유물/포션/증원 뷰어 중 하나라도 열려 있는지. PauseMenuUI가 ESC 우선순위 판단에 사용.</summary>
    public bool AnyOverlayOpen =>
        _deckViewerOpen || _relicViewerOpen || _potionViewerOpen || _reinforcePickerCardIndex >= 0;

    // 드롭다운 앵커 — DrawTopBar에서 매 프레임 갱신.
    private float _potionDropdownAnchorX;
    private float _relicDropdownAnchorX;
    private float _navBarBottomY = 70f;

    private enum DamageFloaterKind { Damage, Heal, BlockAbsorbed }

    private class DamageFloater
    {
        public object anchor;
        public int amount;
        public float delay;
        public float age;
        // 앵커가 죽어 _slotPositions에서 사라진 뒤에도 마지막 위치에서 그릴 수 있게 캐시.
        public Vector2 lastPos;
        public bool hasPos;
        public DamageFloaterKind kind;
        // 같은 프레임 동시 spawn 시 좌우로 분리해 겹침 방지. (예: 블록 흡수 -55, 데미지 0)
        public float xOffset;

        // 스폰 시 결정되는 무작위 변동값 — 같은 데미지여도 매번 다르게 보이도록 무게감 부여.
        // 매 프레임 새로 뽑으면 떨림이 너무 강해지므로 spawn 시 한 번 결정해 유지.
        public float spawnRotation;   // -8 ~ +8도. 큰 데미지일수록 진폭 큼.
        public float xJitter;         // 시작 위치 좌우 변동, ±12px 내외.
        public float swayPhase;       // 부유 흔들림용 0~2π 랜덤 위상.
        public float swayAmp;         // 부유 진폭(px). 큰 데미지일수록 큼.

        public const float LifeTime = 1.05f; // 1.2 → 살짝 짧게 — 다음 데미지와 겹쳐 보이는 시간 단축.
        // 펀치 인 = 더 짧고 강렬하게. 시작 1.75배 → 0.92 살짝 작아짐 → 1.0 정착 (overshoot).
        public const float PunchDuration = 0.10f;
        public const float PunchStartScale = 1.75f;
    }

    // ============================================================================
    // 공룡(SummonInstance) 사망 모션 — IMGUI라 별도 페이드 그리기 시스템.
    // BattleEntityView의 잉크 잔향 모션과 동일한 디자인 룰(흰 플래시 → 잉크 차콜 → 알파+Y 페이드).
    // ============================================================================

    private class DyingSummonView
    {
        public SummonInstance source;     // 참조용 (중복 등록 방지). 그리기 시점엔 캐싱된 sprite/size 사용.
        public Texture2D sprite;
        public Vector2 pos;               // 발(rect.yMax 부근) 기준 마지막 위치.
        public float w;
        public float h;
        public float age;
        public float deathRotation;       // 사망 회전 ±7도. spawn 시 한 번 결정.
        public const float LifeTime = 0.66f; // BattleEntityView.DeathRoutine 총 길이와 동일 (0.06+0.20+0.40).
    }

    private readonly List<DyingSummonView> _dyingSummons = new();

    private void RegisterDyingSummon(SummonInstance s)
    {
        if (s == null) return;
        // 중복 등록 방지 — IsDead 공룡이 EndTurnCleanup까지 남아 매 OnGUI마다 이벤트 재발사될 일은 없지만 안전망.
        foreach (var d in _dyingSummons)
            if (ReferenceEquals(d.source, s)) return;

        // sprite/사이즈 캡처. _cardSprites는 카드 이미지, _summonDisplayPositions는 애니된 위치.
        Texture2D tex = null;
        if (s.data != null) _cardSprites.TryGetValue(s.data.id, out tex);

        float scale = s.data != null ? s.data.SafeFieldScale : 1f;
        float w = dinoSize * scale;
        float h = dinoSize * scale;

        Vector2 pos;
        if (!_summonDisplayPositions.TryGetValue(s, out pos))
            _slotPositions.TryGetValue(s, out pos);

        _dyingSummons.Add(new DyingSummonView
        {
            source = s,
            sprite = tex,
            pos = pos,
            w = w,
            h = h,
            age = 0f,
            deathRotation = UnityEngine.Random.Range(-7f, 7f),
        });
    }

    private void AdvanceDyingSummons()
    {
        if (_dyingSummons.Count == 0) return;
        float dt = Time.deltaTime;
        for (int i = 0; i < _dyingSummons.Count; i++) _dyingSummons[i].age += dt;
        _dyingSummons.RemoveAll(d => d.age >= DyingSummonView.LifeTime);
    }

    private void DrawDyingSummons()
    {
        if (_dyingSummons.Count == 0) return;
        Color inkCharcoal = new Color(0.102f, 0.078f, 0.063f, 1f);
        var prevColor = GUI.color;
        var prevMatrix = GUI.matrix;

        // BattleEntityView.DeathRoutine과 동일 페이즈 길이.
        // Phase 1 (0.06s): 짓눌림 + 흰 플래시
        // Phase 2 (0.20s): 잉크 톤다운 + 위로 솟구침 도입 + Y stretch
        // Phase 3 (0.40s): 알파 페이드 + 더 떠오름 + 추가 stretch + 회전 정착
        const float p1 = 0.06f / DyingSummonView.LifeTime;
        const float p2 = (0.06f + 0.20f) / DyingSummonView.LifeTime;

        foreach (var d in _dyingSummons)
        {
            if (d.sprite == null) continue;
            float p = Mathf.Clamp01(d.age / DyingSummonView.LifeTime);

            Color tint;
            float yRiseRatio; // 캐릭터 높이 비율
            float xScale;
            float yScale;
            float rotation;
            float alpha;

            if (p < p1)
            {
                float k = p / p1;
                tint = Color.Lerp(new Color(1.4f, 1.2f, 1.1f, 1f), Color.white, k);
                yRiseRatio = 0f;
                xScale = Mathf.Lerp(1.05f, 1f, k);    // X 살짝 퍼졌다 정상
                yScale = Mathf.Lerp(0.92f, 1f, k);    // Y 짓눌림 → 정상
                rotation = 0f;
                alpha = 1f;
            }
            else if (p < p2)
            {
                float k = (p - p1) / (p2 - p1);
                float eased = 1f - (1f - k) * (1f - k);
                tint = Color.Lerp(Color.white, inkCharcoal, eased);
                yRiseRatio = 0.08f * eased;
                xScale = Mathf.Lerp(1f, 0.96f, eased); // X 좁아짐
                yScale = Mathf.Lerp(1f, 1.08f, eased); // Y 늘어남 — 영혼 빠지는 도입
                rotation = d.deathRotation * 0.3f * eased;
                alpha = 1f;
            }
            else
            {
                float k = (p - p2) / (1f - p2);
                float eased = 1f - Mathf.Pow(1f - k, 3f);
                tint = inkCharcoal;
                yRiseRatio = Mathf.Lerp(0.08f, 0.32f, eased);
                xScale = Mathf.Lerp(0.96f, 0.88f, eased);
                yScale = Mathf.Lerp(1.08f, 1.18f, eased);
                rotation = Mathf.Lerp(d.deathRotation * 0.3f, d.deathRotation, eased);
                alpha = 1f - eased;
            }

            float drawW = d.w * xScale;
            float drawH = d.h * yScale;
            float yRise = d.h * yRiseRatio;
            float footY = d.pos.y + d.h * 0.5f - yRise;
            var rect = new Rect(d.pos.x - drawW * 0.5f, footY - drawH, drawW, drawH);

            // 회전 — 발 위치(footY) 기준으로 회전해야 자연스러움(머리가 흔들리는 느낌).
            GUI.matrix = prevMatrix * RotateAroundPivotMatrix(rotation, new Vector2(d.pos.x, footY));
            GUI.color = new Color(tint.r, tint.g, tint.b, alpha);
            GUI.DrawTexture(rect, d.sprite, ScaleMode.StretchToFill, alphaBlend: true);
        }

        GUI.matrix = prevMatrix;
        GUI.color = prevColor;
    }

    // =========================================================
    // Lifecycle
    // =========================================================

    // 부팅 로딩 진행도 — BootSplashUI가 폴링해 스플래시 화면 진행바를 그림.
    public static float PreloadProgress01 { get; private set; }
    public static bool IsPreloading { get; private set; } = true;

    IEnumerator Start()
    {
        PreloadProgress01 = 0f;
        IsPreloading = true;

        if (!DataManager.Instance.IsLoaded) DataManager.Instance.Load();

        // 카메라 셰이크 컴포넌트가 메인 카메라에 없으면 부착. 데미지 시점에 Shake() 호출 가능.
        var mainCam = Camera.main;
        if (mainCam != null && mainCam.GetComponent<DianoCard.FX.CameraShaker>() == null)
            mainCam.gameObject.AddComponent<DianoCard.FX.CameraShaker>();
        PreloadProgress01 = 0.05f;
        yield return null;

        yield return StartCoroutine(LoadCardSpritesCo(progressFrom: 0.05f, progressTo: 0.20f));
        PreloadProgress01 = 0.20f;
        yield return null;

        LoadEnemySprites();
        PreloadProgress01 = 0.30f;
        yield return null;

        PreloadBackgrounds();
        PreloadProgress01 = 0.35f;
        yield return null;

        // 플레이어 시퀀스만 프리로드 — 첫 전투 시작 즉시 보임. 적 attack 시퀀스는 EnsureEnemyView가
        // 그 적이 처음 등장할 때 lazy 로드 (LoadAndApplyAttackSequence + _attackSeqCache로 캐시).
        // 부팅에서 PNG 170+ 장이 빠져 freeze 사라짐. 첫 등장 프레임에만 가벼운 stall (배경 전환과 묶임).
        var playerAttackPivot = new Vector2(0.409f, 0f);
        LoadFrameSequenceWithPivot("Character_infield/character_basic/attack/", playerAttackPivot);
        PreloadProgress01 = 0.55f;
        yield return null;
        LoadFrameSequence("Character_infield/character_basic/hit/");
        PreloadProgress01 = 0.70f;
        yield return null;
        LoadFrameSequence("Character_infield/character_basic/summon/");
        PreloadProgress01 = 0.85f;
        yield return null;

        _cardCountBadgeTexture = Resources.Load<Texture2D>("CardSlot/CardCountBadge");
        if (_cardCountBadgeTexture == null)
            Debug.LogWarning("[BattleUI] CardCountBadge texture not found: Resources/CardSlot/CardCountBadge");

        // 덱 뷰어 스킨 — 폴백: 기존 단색 패널 + DrawBorder.
        _deckPanelFrameTex     = Resources.Load<Texture2D>("DeckUi/deck_panel_frame");
        _deckTabSelectedTex    = Resources.Load<Texture2D>("DeckUi/sort_tab_selected");
        _deckTabUnselectedTex  = Resources.Load<Texture2D>("DeckUi/sort_tab_unselected");
        _deckBadgeTex          = Resources.Load<Texture2D>("DeckUi/duplicate_tag_blank");
        if (_deckPanelFrameTex    == null) Debug.LogWarning("[BattleUI] DeckUi/deck_panel_frame not found");
        if (_deckTabSelectedTex   == null) Debug.LogWarning("[BattleUI] DeckUi/sort_tab_selected not found");
        if (_deckTabUnselectedTex == null) Debug.LogWarning("[BattleUI] DeckUi/sort_tab_unselected not found");
        if (_deckBadgeTex         == null) Debug.LogWarning("[BattleUI] DeckUi/duplicate_tag_blank not found");

        _manaFrameTexture = Resources.Load<Texture2D>("CardSlot/ManaFrame");
        if (_manaFrameTexture == null)
            Debug.LogWarning("[BattleUI] ManaFrame texture not found: Resources/CardSlot/ManaFrame");

        _manaOrbTexture = Resources.Load<Texture2D>("CardSlot/ManaOrb");
        if (_manaOrbTexture == null)
            Debug.LogWarning("[BattleUI] ManaOrb texture not found: Resources/CardSlot/ManaOrb");

        // 캐릭터별 마나 오브 — CH002=아케네(빨강), CH002B=린네(초록). 둘 다 없으면 _manaOrbTexture 폴백.
        _manaOrbAkaneTexture = Resources.Load<Texture2D>("InGame/Icon/Mana_Akane");
        _manaOrbRinneTexture = Resources.Load<Texture2D>("InGame/Icon/Mana_Rinne");
        if (_manaOrbAkaneTexture == null) Debug.LogWarning("[BattleUI] Mana_Akane not found: Resources/InGame/Icon/Mana_Akane");
        if (_manaOrbRinneTexture == null) Debug.LogWarning("[BattleUI] Mana_Rinne not found: Resources/InGame/Icon/Mana_Rinne");

        // YJ 통합 프레임 — 종류별 5종. UTILITY는 RITUAL과 동일한 보라 프레임 공유.
        _frameSummon  = Resources.Load<Texture2D>("CardSlot/Frames/Frame_SUMMON");
        _frameMagic   = Resources.Load<Texture2D>("CardSlot/Frames/Frame_MAGIC");
        _frameBuff    = Resources.Load<Texture2D>("CardSlot/Frames/Frame_BUFF");
        _frameUtility = Resources.Load<Texture2D>("CardSlot/Frames/Frame_UTILITY");
        _frameRitual  = Resources.Load<Texture2D>("CardSlot/Frames/Frame_RITUAL");
        if (_frameSummon  == null) Debug.LogWarning("[BattleUI] Frame_SUMMON not found: Resources/CardSlot/Frames/Frame_SUMMON");
        if (_frameMagic   == null) Debug.LogWarning("[BattleUI] Frame_MAGIC not found: Resources/CardSlot/Frames/Frame_MAGIC");
        if (_frameBuff    == null) Debug.LogWarning("[BattleUI] Frame_BUFF not found: Resources/CardSlot/Frames/Frame_BUFF");
        if (_frameUtility == null) Debug.LogWarning("[BattleUI] Frame_UTILITY not found: Resources/CardSlot/Frames/Frame_UTILITY");
        if (_frameRitual  == null) Debug.LogWarning("[BattleUI] Frame_RITUAL not found: Resources/CardSlot/Frames/Frame_RITUAL");

        _shieldFxTexture = Resources.Load<Texture2D>("CardArt/Spell/Effect/ShieldBubble");
        if (_shieldFxTexture == null)
            Debug.LogWarning("[BattleUI] ShieldBubble texture not found: Resources/CardArt/Spell/Effect/ShieldBubble");

        _iconHP     = Resources.Load<Texture2D>("InGame/Icon/HP");
        _iconGold   = Resources.Load<Texture2D>("InGame/Icon/Gold");
        _iconMana   = Resources.Load<Texture2D>("InGame/Icon/Mana");
        _iconPotion = Resources.Load<Texture2D>("InGame/Icon/Potion_Bottle");
        _iconRelic  = Resources.Load<Texture2D>("InGame/Icon/Relic");
        _iconDeck    = Resources.Load<Texture2D>("InGame/Icon/Deck");
        _iconDiscard = Resources.Load<Texture2D>("InGame/Icon/Discard");
        _iconCardBack = Resources.Load<Texture2D>("InGame/Icon/CardBack");
        _iconFloor    = Resources.Load<Texture2D>("InGame/Icon/Floor");
        _iconTechTree = Resources.Load<Texture2D>("InGame/Icon/TechTree");
        _iconTurn     = Resources.Load<Texture2D>("InGame/Icon/Turn");
        _iconAlertNew = Resources.Load<Texture2D>("InGame/Icon/Alert_New");
        _iconDeckRinne     = Resources.Load<Texture2D>("InGame/Icon/Deck_Rinne");
        _iconDiscardRinne  = Resources.Load<Texture2D>("InGame/Icon/Discard_Rinne");
        _iconCardBackRinne = Resources.Load<Texture2D>("InGame/Icon/CardBack_Rinne");
        _headIcons = new Dictionary<string, Texture2D>(HeadIconIds.Length);
        foreach (var id in HeadIconIds)
        {
            var tex = Resources.Load<Texture2D>("InGame/HeadIcon/" + id);
            _headIcons[id] = tex;
            if (tex == null) Debug.LogWarning($"[BattleUI] HeadIcon not found: Resources/InGame/HeadIcon/{id}");
        }
        _topBarBg   = Resources.Load<Texture2D>("InGame/TopBar");
        _hudDividerTexMap     = Resources.Load<Texture2D>("Map/divider_map");
        _hudDividerTexBattle  = Resources.Load<Texture2D>("InGame/divider_battle"); // 전투/마을 공용 — 없으면 null
        _endTurnButtonTex   = Resources.Load<Texture2D>("InGame/EndTurnButton");
        _endTurnButtonAkane = Resources.Load<Texture2D>("InGame/EndTurnButton_Akane");
        _endTurnButtonRinne = Resources.Load<Texture2D>("InGame/EndTurnButton_Rinne");
        if (_endTurnButtonAkane == null) Debug.LogWarning("[BattleUI] EndTurnButton_Akane not found: Resources/InGame/EndTurnButton_Akane");
        if (_endTurnButtonRinne == null) Debug.LogWarning("[BattleUI] EndTurnButton_Rinne not found: Resources/InGame/EndTurnButton_Rinne");
        if (_iconHP     == null) Debug.LogWarning("[BattleUI] HP icon not found: Resources/InGame/Icon/HP");
        if (_iconGold   == null) Debug.LogWarning("[BattleUI] Gold icon not found: Resources/InGame/Icon/Gold");
        if (_iconMana   == null) Debug.LogWarning("[BattleUI] Mana icon not found: Resources/InGame/Icon/Mana");
        if (_iconPotion == null) Debug.LogWarning("[BattleUI] Potion icon not found: Resources/InGame/Icon/Potion_Bottle");
        if (_iconRelic  == null) Debug.LogWarning("[BattleUI] Relic icon not found: Resources/InGame/Icon/Relic");
        if (_iconDeck    == null) Debug.LogWarning("[BattleUI] Deck icon not found: Resources/InGame/Icon/Deck");
        if (_iconDiscard == null) Debug.LogWarning("[BattleUI] Discard icon not found: Resources/InGame/Icon/Discard");
        if (_iconCardBack == null) Debug.LogWarning("[BattleUI] CardBack icon not found: Resources/InGame/Icon/CardBack");
        if (_iconFloor   == null) Debug.LogWarning("[BattleUI] Floor icon not found: Resources/InGame/Icon/Floor");

        PreloadProgress01 = 1f;
        IsPreloading = false;
    }

    private Texture2D HeadIcon(string id)
    {
        if (id == null) return null;
        return _headIcons != null && _headIcons.TryGetValue(id, out var t) ? t : null;
    }

    // 진화 공룡 시그니처 스킬 아이콘 — DinoSkillData.nameEn(스페이스→밑줄, 대문자) 키로 lazy-load.
    // 누락 아이콘은 null로 캐시해 매 프레임 Resources.Load 재시도하지 않는다.
    private readonly Dictionary<string, Texture2D> _skillIconCache = new();
    private Texture2D GetSkillIcon(DianoCard.Data.DinoSkillData skill)
    {
        if (skill == null || string.IsNullOrEmpty(skill.nameEn)) return null;
        var key = skill.nameEn.ToUpperInvariant().Replace(' ', '_');
        if (_skillIconCache.TryGetValue(key, out var cached)) return cached;
        var tex = Resources.Load<Texture2D>("InGame/HeadIcon/Skill/" + key);
        _skillIconCache[key] = tex;
        return tex;
    }

    // 현재 런의 캐릭터 ID에 맞춰 마나 오브 텍스처 선택. CH002=아케네 빨강, CH002B/CH001=린네 초록.
    private Texture2D GetCharacterManaOrb()
    {
        var run = GameStateManager.Instance?.CurrentRun;
        string cid = run?.characterId;
        if (cid == "CH002B" || cid == "CH001") return _manaOrbRinneTexture;
        return _manaOrbAkaneTexture; // CH002 및 미지정 기본
    }

    // 현재 캐릭터가 린네 계열이면 true. CardBack/Deck/Discard 변형 선택용 공통 분기.
    private bool IsRinneCharacter()
    {
        string cid = GameStateManager.Instance?.CurrentRun?.characterId;
        return cid == "CH002B" || cid == "CH001";
    }

    private Texture2D GetCharacterDeckIcon()    => (IsRinneCharacter() && _iconDeckRinne     != null) ? _iconDeckRinne     : _iconDeck;
    private Texture2D GetCharacterDiscardIcon() => (IsRinneCharacter() && _iconDiscardRinne  != null) ? _iconDiscardRinne  : _iconDiscard;
    private Texture2D GetCharacterCardBack()    => (IsRinneCharacter() && _iconCardBackRinne != null) ? _iconCardBackRinne : _iconCardBack;

    private Texture2D GetCharacterEndTurnButton()
    {
        if (IsRinneCharacter() && _endTurnButtonRinne != null) return _endTurnButtonRinne;
        if (!IsRinneCharacter() && _endTurnButtonAkane != null) return _endTurnButtonAkane;
        return _endTurnButtonTex; // 둘 다 없으면 레거시 폴백
    }

    // 현재 캐릭터의 인필드 스프라이트 베이스 폴더. 린네면 rinne/, 아니면 character_basic/.
    private string GetCharacterSpriteFolder()
    {
        return IsRinneCharacter() ? "Character_infield/rinne/" : "Character_infield/character_basic/";
    }

    void Update()
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null) return;

        // Battle/Reward 상태가 아닐 때는 다음 전투를 위해 리셋
        // (Reward 상태에서도 BattleUI가 뒷배경/전장을 계속 그려 보상 화면 뒤로 비춰야 하므로 유지)
        if (gsm.State != GameState.Battle && gsm.State != GameState.Reward)
        {
            if (_battleInitialized)
            {
                _battleInitialized = false;
                _battleEndQueued = false;
                _rewardDimmed = false;
                if (_rewardDimOverlay != null)
                {
                    Destroy(_rewardDimOverlay.gameObject);
                    _rewardDimOverlay = null;
                }
                _battle = null;
                _lastKnownHp.Clear();
                _lastKnownBlock.Clear();
                _entityShieldFxStart.Clear();
                _hpBarDisplayedFrac.Clear();
                _floaters.Clear();
        _dyingSummons.Clear();
                _exhaustFlyCards.Clear();
                _exhaustPhantomIndex = -1;
                _exhaustPhantomStartTime = -1f;
                _targetingCardIndex = -1;
                _targetingSummonIndex = -1;
                _targetingSummonSkillIndex = -1;
                _swapFromCardIndex = -1;
                _endTurnAnimating = false;
                _attackingUnit = null;
                _attackProgress = 0;
                _prevPlayerBlock = 0;
                _playerShieldFxStartTime = -1f;
                _playerBlockTintStartTime = -1f;
                StopAllCoroutines();
                DespawnBackgroundFX();
                DespawnBackgroundVines();
                DestroyWorldBackground();
                DestroyAllEnemyViews();
            }
            return;
        }

        // Reward 상태에서는 렌더링 상태만 유지하고 전투 로직은 정지
        if (gsm.State == GameState.Reward)
        {
            // world-space 캐릭터/적 스프라이트를 IMGUI 오버레이에 맞춰 dim 처리
            // (IMGUI 오버레이는 world-space 렌더링을 못 덮기 때문)
            ApplyRewardDimming();
            return;
        }
        else if (_rewardDimmed)
        {
            // Reward에서 빠져나왔을 때 복구 (보통 Map으로 가면 뷰가 파괴되지만 안전장치)
            RestoreRewardDimming();
        }

        // 치트로 전투 중 적 갈아타기 — GSM의 신호 받으면 강제 재초기화
        if (gsm.CheatBattleReinitRequested && _battleInitialized)
        {
            gsm.CheatBattleReinitRequested = false;
            _battleInitialized = false;
            _battleEndQueued = false;
            _battle = null;
            _lastKnownHp.Clear();
            _lastKnownBlock.Clear();
            _entityShieldFxStart.Clear();
            _hpBarDisplayedFrac.Clear();
            _floaters.Clear();
        _dyingSummons.Clear();
            _exhaustFlyCards.Clear();
            _exhaustPhantomIndex = -1;
            _exhaustPhantomStartTime = -1f;
            _targetingCardIndex = -1;
            _targetingSummonIndex = -1;
            _targetingSummonSkillIndex = -1;
            _swapFromCardIndex = -1;
            _endTurnAnimating = false;
            _attackingUnit = null;
            _attackProgress = 0;
            _prevPlayerBlock = 0;
            _playerShieldFxStartTime = -1f;
            _playerBlockTintStartTime = -1f;
            StopAllCoroutines();
            DespawnBackgroundFX();
            DespawnBackgroundVines();
            DestroyWorldBackground();
            DestroyAllEnemyViews();
        }

        // Battle 상태로 진입한 첫 프레임 → 초기화
        if (!_battleInitialized)
        {
            InitBattleFromRunState();
            _battleInitialized = true;
            return;
        }

        // 지연 실행 액션
        if (_pending.Count > 0)
        {
            var snapshot = new List<Action>(_pending);
            _pending.Clear();
            foreach (var a in snapshot) a?.Invoke();
        }

        // HP 변화 감지 & 플로터 진행
        if (_battle?.state != null)
        {
            DetectDamage();
            AdvanceFloaters();
            AdvanceDyingSummons();
            CleanupDeadEnemyViews();

            // 플레이어 block 증가 감지 → 방패 이펙트 트리거
            int curBlock = _battle.state.player.block;
            if (curBlock > _prevPlayerBlock)
                _playerShieldFxStartTime = Time.time;
            // 바 파란 틴트 페이드인은 block이 처음 생긴 순간(0→>0)에만 시작.
            // 이미 파란 상태인데 추가 block을 쌓을 때마다 재시작되면 빨간색으로 깜빡인다.
            if (_prevPlayerBlock == 0 && curBlock > 0)
                _playerBlockTintStartTime = Time.time;
            _prevPlayerBlock = curBlock;

            // 공룡/적 block 증가 감지 — 호위 선풍(C111 ALLY), 무리의 천막(C112 ALL_ALLY),
            // 적 BLOCK intent 등 모든 경로에서 같은 방패 모션을 띄운다.
            DetectEntityBlockGain();
        }

        // 전투 종료 감지 → 1.5초 뒤 GSM에 결과 전달
        if (!_battleEndQueued && _battle?.state?.IsOver == true)
        {
            _battleEndQueued = true;
            _battleEndDelay = 1.5f;
        }
        if (_battleEndQueued)
        {
            _battleEndDelay -= Time.deltaTime;
            if (_battleEndDelay <= 0f)
            {
                NotifyBattleEnd();
            }
        }
    }

    // 카드 한 장이 부팅에서 끌어오는 PNG 수.
    // - CardArt 일러스트 1장 (모든 카드)
    // - 필드 sprite 1장 (SUMMON만)
    // - attack 12프레임 (SUMMON만) ← 부팅 시점엔 미로드, LoadAttackFramesInBackgroundCo가 늦게 채움.
    // 부팅 핫패스에서 SUMMON 1장당 PNG가 14 → 2로 감소.
    private const int CardsPerFrameBudget = 5;

    // 부팅 후 background로 attack 프레임 흘려넣기 — 카드 단위 yield라 한 프레임당 12 PNG 정도.
    // 첫 평타 시점(보통 부팅 후 3~10초)엔 보유 카드 attack 프레임은 이미 채워져 있음.
    // 안 채워진 카드는 DrawSummon이 시퀀스 없으면 lunge 모션으로 자동 폴백.
    private System.Collections.IEnumerator LoadAttackFramesInBackgroundCo(System.Collections.Generic.List<CardData> summonCards)
    {
        foreach (var card in summonCards)
        {
            if (card == null || string.IsNullOrEmpty(card.image)) continue;
            if (_fieldDinoAttackFrames.ContainsKey(card.id)) continue;
            LoadOneCardAttackFrames(card);
            yield return null;
        }
    }

    private System.Collections.IEnumerator LoadCardSpritesCo(float progressFrom, float progressTo)
    {
        var cards = new System.Collections.Generic.List<CardData>(DataManager.Instance.Cards.Values);
        var summonCards = new System.Collections.Generic.List<CardData>();
        int n = cards.Count;
        int loaded = 0;
        for (int idx = 0; idx < n; idx++)
        {
            var card = cards[idx];
            LoadOneCardSprites(card);
            if (card.cardType == CardType.SUMMON) summonCards.Add(card);
            loaded++;
            if (loaded % CardsPerFrameBudget == 0)
            {
                PreloadProgress01 = Mathf.Lerp(progressFrom, progressTo, idx / (float)n);
                yield return null;
            }
        }

        // 패시브 타입 아이콘 — Dinos/passive/<lowercase_enum>.png
        foreach (DinoPassiveType pt in System.Enum.GetValues(typeof(DinoPassiveType)))
        {
            if (pt == DinoPassiveType.NONE) continue;
            var ptex = Resources.Load<Texture2D>("Dinos/passive/" + pt.ToString().ToLower());
            if (ptex != null) _passiveIcons[pt] = ptex;
        }
        yield return null;

        // 정적 폴백 스프라이트 — attack 시퀀스가 없을 때만 사용. 없어도 PlayerView는 시퀀스로 만들 수 있음.
        _playerSprite = Resources.Load<Texture2D>("Character_infield/Char_Archaeologist_Field");
        EnsurePlayerView();

        // 부팅 핫패스가 종료된 다음 attack 프레임을 background coroutine으로 천천히 흘려넣기.
        // BattleUI.Start 코루틴은 즉시 종료 가능, 사용자는 로비 입력 가능.
        StartCoroutine(LoadAttackFramesInBackgroundCo(summonCards));
    }

    private void LoadOneCardSprites(CardData card)
    {
        if (string.IsNullOrEmpty(card.image)) return;

        string filename = Path.GetFileNameWithoutExtension(card.image);

        // 카드 표시용 일러스트 — 타입별 서브폴더
        // SUMMON은 완성본을 Dino/ 에 두고 미완성은 Summon/ (REF 원본)으로 폴백
        string subfolder = card.cardType switch
        {
            CardType.SUMMON => "Summon",
            CardType.MAGIC  => "Spell",
            _               => "Utility", // BUFF / UTILITY / RITUAL
        };
        Texture2D tex = null;
        if (card.cardType == CardType.SUMMON)
        {
            tex = Resources.Load<Texture2D>($"CardArt/Dino/{filename}");
        }
        if (tex == null)
        {
            tex = Resources.Load<Texture2D>($"CardArt/{subfolder}/{filename}");
        }
        if (tex != null) _cardSprites[card.id] = tex;
        else Debug.LogWarning($"[BattleUI] Card sprite not found: CardArt/{(card.cardType == CardType.SUMMON ? "Dino|Summon" : subfolder)}/{filename}");

        // 필드용 공룡 스프라이트 (투명 배경) — SUMMON만. attack 12프레임은 background coroutine이 채움.
        if (card.cardType == CardType.SUMMON)
        {
            var fieldTex = Resources.Load<Texture2D>("Dinos/" + filename);
            if (fieldTex != null) _fieldDinoSprites[card.id] = fieldTex;
            else Debug.LogWarning($"[BattleUI] Field dino sprite not found: Dinos/{filename}");
        }
    }

    // 평타 모션 12프레임 — Resources/Dinos/animation/<filename>/attack_f01..f12.png.
    // 끊기는 번호에서 중단 (idle만 있는 카드는 attack_f01 미존재 → 시퀀스 미등록 → 기존 정적 표시).
    private void LoadOneCardAttackFrames(CardData card)
    {
        if (card == null || string.IsNullOrEmpty(card.image)) return;
        string filename = Path.GetFileNameWithoutExtension(card.image);
        var atkFrames = new System.Collections.Generic.List<Texture2D>();
        for (int i = 1; i <= 12; i++)
        {
            var f = Resources.Load<Texture2D>($"Dinos/animation/{filename}/attack_f{i:D2}");
            if (f == null) break;
            atkFrames.Add(f);
        }
        if (atkFrames.Count > 0) _fieldDinoAttackFrames[card.id] = atkFrames.ToArray();
    }

    private void EnsurePlayerView()
    {
        // 캐릭터가 바뀌었으면 기존 뷰 파괴하고 재생성
        string currentCid = GameStateManager.Instance?.CurrentRun?.characterId ?? "";
        if (_playerView != null)
        {
            if (_loadedPlayerCharacterId == currentCid) return;
            // 캐릭터 변경 — 기존 뷰 정리
            if (_playerView.gameObject != null) Destroy(_playerView.gameObject);
            _playerView = null;
        }

        string charFolder = GetCharacterSpriteFolder();
        const string fallbackFolder = "Character_infield/character_basic/";

        // 캐릭터 폴더 → fallback(아케네) 순으로 시퀀스 로드. 린네는 현재 Idle만 있어 모두 폴백 예정.
        var attackPivot = new Vector2(0.409f, 0f);
        var attackSeq = LoadFrameSequenceWithPivot(charFolder + "attack/", attackPivot);
        if (attackSeq == null || attackSeq.Length == 0)
            attackSeq = LoadFrameSequenceWithPivot(fallbackFolder + "attack/", attackPivot);
        var hitSeq = LoadFrameSequence(charFolder + "hit/");
        if (hitSeq == null || hitSeq.Length == 0)
            hitSeq = LoadFrameSequence(fallbackFolder + "hit/");
        var summonSeq = LoadFrameSequence(charFolder + "summon/");
        if (summonSeq == null || summonSeq.Length == 0)
            summonSeq = LoadFrameSequence(fallbackFolder + "summon/");
        // hit 시퀀스 누락은 폴백하지 않음 — attackSeq를 갖다 쓰면 피격이 공격 모션처럼 보임.
        // BattleEntityView.HitRoutine은 시퀀스 없을 때 shake+빨간 플래시만 재생 (피격감 충분).
        if (summonSeq == null || summonSeq.Length == 0) summonSeq = attackSeq;

        // Idle = 캐릭터 폴더 우선, 없으면 character_basic 폴백.
        var idleTex = Resources.Load<Texture2D>(charFolder + "Idle")
                   ?? Resources.Load<Texture2D>(fallbackFolder + "Idle");
        Sprite idleSprite = idleTex != null ? TexToSprite(idleTex) : null;
        Sprite baseSprite = idleSprite
            ?? (attackSeq != null && attackSeq.Length > 0 ? attackSeq[0] : null)
            ?? (_playerSprite != null ? TexToSprite(_playerSprite) : null);

        if (baseSprite == null)
        {
            Debug.LogWarning($"[BattleUI] PlayerView init skipped — {charFolder}Idle 와 {fallbackFolder}Idle 모두 없음 + 폴백도 없음 (cid={currentCid})");
            return;
        }

        _playerWorldSprite = baseSprite;
        _loadedPlayerCharacterId = currentCid;

        var go = new GameObject("PlayerView");
        go.transform.SetParent(transform, worldPositionStays: false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = _playerWorldSprite;
        _playerView = go.AddComponent<BattleEntityView>();
        _playerView.SetSprite(_playerWorldSprite);
        _playerView.SetSortingOrder(50);
        _playerView.breathingEnabled = true; // CharacterSelectUI의 호흡 공식과 동일
        _playerView.breathingFreq = 0.14f;   // 플레이어 고유 주기 (~7.1s)
        _playerView.breathingPhase = 1.5f;

        if (attackSeq != null && attackSeq.Length > 0)
        {
            _playerView.SetAttackSequence(attackSeq);
            // attack 캔버스(1272x1628)는 우상단 화염구 공간 + 발 정렬을 위한 하단 여백 포함.
            // 본체(머리~발)가 sprite 1628 중 1439 차지 (≈88%) → 그대로 두면 idle 높이에 맞출 때 본체가 12% 작아짐.
            // 1628/1439 ≈ 1.131 부스트로 본체 높이 = idle 높이가 되도록 보정.
            _playerView.SetSequenceScaleBoost(1628f / 1439f);
            Debug.Log($"[BattleUI] Player attack sequence loaded: {attackSeq.Length} frames (character_basic/attack/)");
        }
        if (hitSeq != null && hitSeq.Length > 0)    _playerView.SetHitSequence(hitSeq);
        if (summonSeq != null && summonSeq.Length > 0)
        {
            _playerView.SetSummonSequence(summonSeq);
            // 하위 호환: 시퀀스 미지원 경로에서도 뭔가 보이도록 첫 프레임을 SummonCast로도 세팅.
            _playerView.SetSummonFrame(summonSeq[0]);
        }

        // 공격 FX 스프라이트 로드 — FX/Attack/slash_gold.png (기본) 또는 캐릭터별 전용 이름.
        Texture2D fxTex = null;
        foreach (var candidate in new[] {
            "FX/Attack/CH001_fx",
            "FX/Attack/slash_gold",
            "FX/Attack/impact_punch",
        })
        {
            fxTex = Resources.Load<Texture2D>(candidate);
            if (fxTex != null) { Debug.Log($"[BattleUI] Player attack FX loaded: {candidate}"); break; }
        }
        if (fxTex != null) _playerAttackFxSprite = TexToSprite(fxTex);
        else Debug.LogWarning("[BattleUI] Player attack FX not found. Place PNG at Resources/FX/Attack/slash_gold.png (or CH001_fx.png).");

        // CH002(Arkane) 발사체 — 시전 모션 끝나갈 때 손에서 출발해 적까지 비행하는 화염구.
        var fireballTex = Resources.Load<Texture2D>("FX/Attack/CH002_fireball");
        if (fireballTex != null)
        {
            _playerFireballSprite = TexToSprite(fireballTex);
            Debug.Log("[BattleUI] Player fireball projectile loaded: FX/Attack/CH002_fireball");
        }

        if (attackSeq == null || attackSeq.Length == 0)
            Debug.LogWarning("[BattleUI] Character_infield/character_basic/attack/## 시퀀스 없음 — 정적 폴백 사용");

        // 발 밑 그림자 — pivot을 이미지 중앙(0.5, 0.5)으로 잡아 발 위치에 타원 중심이 오도록.
        var shadowTex = Resources.Load<Texture2D>("Character_infield/character_basic/shadow/character_shadow");
        if (shadowTex != null)
        {
            var shadowSprite = Sprite.Create(
                shadowTex,
                new Rect(0, 0, shadowTex.width, shadowTex.height),
                new Vector2(0.5f, 0.5f),
                100f);
            _playerView.SetShadowSprite(shadowSprite, _entityShadowHeight, Vector2.zero, _entityShadowAlpha);
        }
        else Debug.LogWarning("[BattleUI] Player shadow not found: Resources/Character_infield/character_basic/shadow/character_shadow.png");

        // PreparePlayerView가 발 위치/스케일을 잡기 전까지는 렌더 차단. 안 그러면 BattleUI.Start()의
        // LoadCardSprites 단계에서 만들어진 PlayerView가 (0,0,0)에 native PPU 스케일(이미지 1134px → 11.34 world units)로
        // 떠 있어 로비/맵→배틀 전환 로딩 중 카메라 클리어 컬러 위로 거대 캐릭터 다리만 노출된다.
        // SetActive(false)는 Awake 누락으로 _sr=null 시리즈 NullRef를 일으키므로 SR.enabled 토글 사용.
        _playerView.SetVisible(false);
    }

    /// <summary>Resources 경로 프리픽스 뒤에 01, 02… 를 붙여가며 연속적으로 로드한다 (끊기는 번호에서 중단, 최대 99).
    /// 예: LoadFrameSequence("Character_infield/Archaeologist/attack_f") → attack_f01, attack_f02, ... 를 순서대로.</summary>
    private static Sprite[] LoadFrameSequence(string pathPrefix)
    {
        if (_attackSeqCache.TryGetValue(pathPrefix, out var cached)) return cached;
        var list = new System.Collections.Generic.List<Sprite>();
        for (int i = 1; i <= 99; i++)
        {
            var tex = Resources.Load<Texture2D>($"{pathPrefix}{i:D2}");
            if (tex == null) break;
            list.Add(TexToSprite(tex));
        }
        var arr = list.Count > 0 ? list.ToArray() : null;
        if (arr != null) _attackSeqCache[pathPrefix] = arr;
        return arr;
    }

    private static Sprite TexToSprite(Texture2D tex)
    {
        return Sprite.Create(
            tex,
            new Rect(0, 0, tex.width, tex.height),
            new Vector2(0.5f, 0f),
            100f);
    }

    /// <summary>커스텀 pivot으로 시퀀스 로드 — 캔버스 안에서 캐릭터가 중앙이 아닌 위치에 그려진
    /// GIF 분해본 등에서 캐릭터의 발 위치(또는 임의 anchor)를 sprite pivot으로 잡아 idle 정적 스프라이트와 위치를 맞춤.
    /// pivot은 0..1 정규화 좌표 (0,0=좌하단, 1,1=우상단).</summary>
    private static Sprite[] LoadFrameSequenceWithPivot(string pathPrefix, Vector2 pivot)
    {
        // 시퀀스 캐시 — 동일 pathPrefix 재호출 시 즉시 반환. 매 전투 진입마다 12프레임 PNG
        // 디코딩하던 비용 제거. pivot은 첫 호출 기준 고정(같은 시퀀스 두 pivot으로 부를 일 없음).
        if (_attackSeqCache.TryGetValue(pathPrefix, out var cached)) return cached;

        var list = new System.Collections.Generic.List<Sprite>();
        for (int i = 1; i <= 99; i++)
        {
            var tex = Resources.Load<Texture2D>($"{pathPrefix}{i:D2}");
            if (tex == null) break;
            list.Add(Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                pivot,
                100f));
        }
        var arr = list.Count > 0 ? list.ToArray() : null;
        if (arr != null) _attackSeqCache[pathPrefix] = arr;
        return arr;
    }

    // 적별 attack 시퀀스 캐시 — pathPrefix → Sprite[]. 첫 로드 후 모든 후속 전투에서 재사용.
    private static readonly System.Collections.Generic.Dictionary<string, Sprite[]> _attackSeqCache = new();


    /// <summary>잡몹 공격 시퀀스 로드 + 적용. pivot/scaleBoost는 idle PNG 대비 attack PNG의 캐릭터 fill ratio·발 위치로 측정한 값을 넣음.
    /// pivot이 null이면 LoadFrameSequence의 기본 (0.5, 0). scaleBoost가 1f면 부스트 미적용.</summary>
    private static void LoadAndApplyAttackSequence(BattleEntityView view, string eid, string pathPrefix, Vector2? pivot = null, float scaleBoost = 1f)
    {
        Sprite[] seq = pivot.HasValue
            ? LoadFrameSequenceWithPivot(pathPrefix, pivot.Value)
            : LoadFrameSequence(pathPrefix);
        if (seq != null && seq.Length > 0)
        {
            view.SetAttackSequence(seq);
            if (Mathf.Abs(scaleBoost - 1f) > 0.001f) view.SetSequenceScaleBoost(scaleBoost);
            Debug.Log($"[BattleUI] {eid} attack sequence loaded: {seq.Length} frames (pivot={(pivot.HasValue ? pivot.Value.ToString("F3") : "default")}, boost={scaleBoost:F3})");
        }
    }

    private void OnDestroy()
    {
        if (_playerView != null && _playerView.gameObject != null)
            Destroy(_playerView.gameObject);
        DestroyAllEnemyViews();
        if (_normal1FogTex != null) Destroy(_normal1FogTex);
    }

    private void OnEnable()
    {
        DianoCard.Battle.BattleEvents.OnBlockAbsorbed += HandleBlockAbsorbed;
        DianoCard.Battle.BattleEvents.OnEntityKilled  += HandleEntityKilled;
        DianoCard.Battle.BattleEvents.OnMidTurnCardsDrawn += HandleMidTurnCardsDrawn;
    }

    private void OnDisable()
    {
        DianoCard.Battle.BattleEvents.OnBlockAbsorbed -= HandleBlockAbsorbed;
        DianoCard.Battle.BattleEvents.OnEntityKilled  -= HandleEntityKilled;
        DianoCard.Battle.BattleEvents.OnMidTurnCardsDrawn -= HandleMidTurnCardsDrawn;
    }

    private void HandleMidTurnCardsDrawn(int count, int fromHandIdx)
    {
        if (_battle?.state == null || count <= 0) return;
        StartCoroutine(MidTurnDrawCoroutine(fromHandIdx));
    }

    private IEnumerator MidTurnDrawCoroutine(int fromHandIdx)
    {
        BeginDrawFlyAnimation(_battle.state, fromHandIdx);
        float wait = GetDrawFlyTotalDuration() + 0.05f;
        yield return new WaitForSeconds(wait);
        EndDrawFlyAnimation();
    }

    // OnEntityKilled — entity HP가 0이 된 그 순간 한 번만 호출.
    // 적: 월드 BattleEntityView가 있으면 PlayDeath 코루틴 시작. 보스/엘리트면 가벼운 카메라 셰이크.
    // 공룡: IMGUI 그리기라 별도 dying 리스트(_dyingSummons)에 등록 — 페이드 모션 처리는 DrawDyingSummons.
    // 플레이어: 일단 무시 (GameOver UI가 별도로 진입 처리).
    private void HandleEntityKilled(object entity)
    {
        if (entity is EnemyInstance ei)
        {
            if (_enemyViews.TryGetValue(ei, out var view) && view != null && !view.IsDying)
                view.PlayDeath();
            // 보스/엘리트만 가벼운 카메라 셰이크 — 잡몹 죽음에 셰이크 박으면 어지러움.
            var et = ei.data?.enemyType ?? DianoCard.Data.EnemyType.NORMAL;
            if (et == DianoCard.Data.EnemyType.BOSS || et == DianoCard.Data.EnemyType.ELITE)
                DianoCard.FX.CameraShaker.Instance?.Shake(0.015f, 0.20f);
        }
        else if (entity is SummonInstance s)
        {
            RegisterDyingSummon(s);
        }
        // PlayerState 사망은 GameOverUI 진입 흐름이 따로 있어 추가 후크 안 함.
    }

    // BattleEvents.OnBlockAbsorbed 콜백 — 방어막이 데미지를 흡수한 순간 페일 블루 floater 스폰.
    // 같은 TakeDamage에서 HP도 깎였다면 폴링 기반 데미지 floater가 곧이어 별도로 스폰됨(중앙).
    // BlockAbsorbed는 xOffset=-55로 왼쪽으로 살짝 비껴서 데미지 숫자와 시각적으로 분리된다.
    private void HandleBlockAbsorbed(object entity, int amount)
    {
        if (amount <= 0) return;
        bool hasGuiPos = _slotPositions.TryGetValue(entity, out var guiPos);
        var f = new DamageFloater
        {
            anchor = entity,
            amount = amount,
            delay = 0f,
            age = 0f,
            lastPos = hasGuiPos ? guiPos : default,
            hasPos = hasGuiPos,
            kind = DamageFloaterKind.BlockAbsorbed,
            xOffset = -55f,
        };
        SeedFloaterRandomness(f);
        _floaters.Add(f);
    }

    private void LoadEnemySprites()
    {
        foreach (var enemy in DataManager.Instance.Enemies.Values)
        {
            Texture2D tex = null;
            if (!string.IsNullOrEmpty(enemy.image))
            {
                string filename = Path.GetFileNameWithoutExtension(enemy.image);
                tex = Resources.Load<Texture2D>("Monsters/" + filename);
                if (tex == null)
                    Debug.LogWarning($"[BattleUI] Enemy sprite not found: Monsters/{filename} — placeholder 사용");
            }

            // 아트가 없거나 로드 실패 → 카드형 placeholder 생성
            if (tex == null) tex = BuildEnemyPlaceholderTex(enemy);

            _enemySprites[enemy.id] = tex;
            _enemyWorldSprites[enemy.id] = TexToSprite(tex);
        }

        // E901 보스가 인라인으로 소환하는 이끼 잡몹 — DataManager.Enemies엔 없으니 별도 등록.
        // 4코너 전용 스프라이트를 코너 인덱스로 ComputeSlotPositions에서 스왑.
        // _enemySprites/_enemyWorldSprites["MOSS_E901"] 기본값은 left_up — 첫 프레임 view 생성 시 폴백.
        var mossTexLeftUp    = Resources.Load<Texture2D>("Monsters/E901_Moss_left_up");
        var mossTexRightUp   = Resources.Load<Texture2D>("Monsters/E901_Moss_right_up");
        var mossTexLeftDown  = Resources.Load<Texture2D>("Monsters/E901_Moss_left_down");
        var mossTexRightDown = Resources.Load<Texture2D>("Monsters/E901_Moss_right_down");
        if (mossTexLeftUp != null)
        {
            _mossWorldSpriteLeftUp = TexToSprite(mossTexLeftUp);
            _enemySprites["MOSS_E901"] = mossTexLeftUp;
            _enemyWorldSprites["MOSS_E901"] = _mossWorldSpriteLeftUp;
        }
        if (mossTexRightUp   != null) _mossWorldSpriteRightUp   = TexToSprite(mossTexRightUp);
        if (mossTexLeftDown  != null) _mossWorldSpriteLeftDown  = TexToSprite(mossTexLeftDown);
        if (mossTexRightDown != null) _mossWorldSpriteRightDown = TexToSprite(mossTexRightDown);
    }

    /// <summary>
    /// 아트 없는 적용 임시 placeholder. 둥근 마름모형 실루엣 + 반투명 외곽으로 실제 적 옆에 있어도 덜 두드러짐.
    /// 폰트는 못 굽기 때문에 IMGUI 라벨로 별도. 여기는 실루엣 컬러 도형만.
    /// </summary>
    private Texture2D BuildEnemyPlaceholderTex(EnemyData enemy)
    {
        const int W = 192, H = 192;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;

        Color body = enemy.enemyType switch
        {
            EnemyType.BOSS  => new Color(0.65f, 0.20f, 0.22f, 1f),
            EnemyType.ELITE => new Color(0.45f, 0.30f, 0.65f, 1f),
            _               => new Color(0.32f, 0.50f, 0.35f, 1f),
        };
        Color outline = new Color(body.r * 0.4f, body.g * 0.4f, body.b * 0.4f, 1f);

        var pixels = new Color[W * H];
        Vector2 center = new Vector2(W / 2f, H / 2f);
        // 둥근 모서리 사각형 마스크 — radius로 4구석 잘라냄, 외곽 8px는 outline
        float radius = W * 0.35f;
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                int idx = y * W + x;
                float dx = Mathf.Max(0f, Mathf.Abs(x - center.x) - (W / 2f - radius));
                float dy = Mathf.Max(0f, Mathf.Abs(y - center.y) - (H / 2f - radius));
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                if (dist > radius + 1f)
                {
                    pixels[idx] = new Color(0, 0, 0, 0); // 투명
                }
                else if (dist > radius - 4f)
                {
                    // 외곽 라인 (안티에일리어싱 흉내)
                    float a = Mathf.Clamp01(radius + 1f - dist);
                    pixels[idx] = new Color(outline.r, outline.g, outline.b, a);
                }
                else
                {
                    // 본체 — 약간 그라데이션
                    float t = (y / (float)H);
                    pixels[idx] = Color.Lerp(body, body * 0.7f, t);
                }
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        tex.name = "EnemyPlaceholder_" + enemy.id;
        return tex;
    }

    /// <summary>
    /// 지정된 EnemyInstance에 대응하는 BattleEntityView를 보장. 이미 있으면 no-op.
    /// 적 id별 world Sprite가 로드돼 있어야 작동 (없으면 IMGUI 폴백).
    /// </summary>
    private void EnsureEnemyView(EnemyInstance e)
    {
        if (e == null || _enemyViews.ContainsKey(e)) return;

        // 런타임 소환된 쫄(EnemyData가 DataManager에 없음) 등은 캐시에 없을 수 있음 —
        // e.data.image가 지정돼 있으면 Resources/Monsters/<filename>에서 로드 시도, 실패 시에만 placeholder.
        if (!_enemyWorldSprites.TryGetValue(e.data.id, out var sprite))
        {
            Texture2D tex = null;
            if (!string.IsNullOrEmpty(e.data.image))
            {
                string filename = Path.GetFileNameWithoutExtension(e.data.image);
                tex = Resources.Load<Texture2D>("Monsters/" + filename);
                if (tex == null)
                    Debug.LogWarning($"[BattleUI] Dynamic enemy sprite not found: Monsters/{filename} ({e.data.id}) — placeholder 사용");
            }
            if (tex == null) tex = BuildEnemyPlaceholderTex(e.data);
            sprite = TexToSprite(tex);
            _enemySprites[e.data.id] = tex;
            _enemyWorldSprites[e.data.id] = sprite;
        }

        var go = new GameObject($"EnemyView_{e.data.id}");
        go.transform.SetParent(transform, worldPositionStays: false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        var view = go.AddComponent<BattleEntityView>();
        view.SetSprite(sprite);
        view.SetSortingOrder(50);
        view.breathingEnabled = true;

        // 적 공격 시퀀스 wiring — Resources/Monsters/<EID>_<Name>/attack_fNN.png 로드.
        // 일반 잡몹은 LoadAndApplyAttackSequence 헬퍼로 통일, 보스(E901)만 pivot/scaleBoost 커스텀 필요.
        switch (e.data.id)
        {
            // E901 폐허군주 P1 공격 시퀀스 — Monsters/E901_RuinLord_P1/attack_f01..f12.png 로드 (Kling 121프레임에서 12 키프레임 추림).
            // _idleSprite는 이미 SetSprite에서 정적 E901_RuinLord로 잡혔으므로, 시퀀스 종료 후엔 그 idle로 복귀.
            // Pivot — f01 idle 포즈에서 보스 발 위치 (916, 1337) of (1440, 1440). Sprite pivot은 좌하단 기준 정규화이므로
            //   X = 916/1440 = 0.636, Y = (1440-1337)/1440 = 0.072.
            //   캐릭터가 텍스처 우측에 위치 → 어택 시퀀스 안에서 좌측으로 대쉬하는 lunge 모션이 자연스럽게 표현됨.
            // ScaleBoost 1.64 — idle 자세에서 보스가 캔버스 높이의 ~60.8% 차지 → idle(보스=캔버스 100%)과 시각 크기 맞추려면 1/0.608.
            case "E901":
            {
                var bossSeq = LoadFrameSequenceWithPivot("Monsters/E901_RuinLord_P1/attack_f", new Vector2(0.636f, 0.072f));
                if (bossSeq != null && bossSeq.Length > 0)
                {
                    view.SetAttackSequence(bossSeq);
                    view.SetSequenceScaleBoost(1.64f);
                    Debug.Log($"[BattleUI] E901 attack sequence loaded: {bossSeq.Length} frames (pivot=0.636,0.072, boost=1.64)");
                }
                break;
            }
            // pivot/scaleBoost — idle PNG 대비 attack PNG의 캐릭터 fill ratio·발 위치를 PIL bbox로 측정한 값.
            // peak-height frame 기준으로 ratio 계산해서 idle 시각 크기와 매칭.

            // E001 이끼 슬라임 — 0.5~2.0s 1.5초 트림 18프레임. squash 시작 → peak 압축 → hold → 솟구침.
            case "E001":
                LoadAndApplyAttackSequence(view, "E001", "Monsters/E001_MossSlime/attack_f", new Vector2(0.501f, 0.066f), 1.103f);
                break;
            // E002 바위 슬라임 — 1.5~2.5s 1초 트림 12프레임. 무겁게 squash → peak → 천천히 rebound. 모델이 0.5s 늦게 시작.
            case "E002":
                LoadAndApplyAttackSequence(view, "E002", "Monsters/E002_RockSlime/attack_f", new Vector2(0.496f, 0.106f), 1.269f);
                break;
            // E003 독 슬라임 — 1.25~2.25s 1초 트림 12프레임. squash → peak → rebound + 머리 위 독 droplets 발사. 모델이 0.25s 늦게 시작.
            case "E003":
                LoadAndApplyAttackSequence(view, "E003", "Monsters/E003_ToxicSlime/attack_f", new Vector2(0.500f, 0.121f), 1.134f);
                break;
            // E004 가시 덩굴 — 1.25~2.25s 1초 트림 12프레임. idle → 덩굴 자세 변환 → peak whip 좌측 확장 → retract → idle. 본체 anchored, 덩굴만 움직임.
            case "E004":
                LoadAndApplyAttackSequence(view, "E004", "Monsters/E004_SpikeVine/attack_f", new Vector2(0.500f, 0.091f), 1.154f);
                break;
            // E005 발광 버섯 — 12프레임. crouch → 위로 포자 분출. peak(f05)는 포자까지 포함된 bbox라 boost는 f01(본체) 기준 = 1/0.727.
            case "E005":
                LoadAndApplyAttackSequence(view, "E005", "Monsters/E005_GlowMushroom/attack_f", new Vector2(0.501f, 0.013f), 1.376f);
                break;
            // E007 가고일 — 12프레임. 캐릭터가 캔버스 우측에 위치(돌진 lunge용), pivot.x≈0.66. peak 높이 81.7% → boost 1.224. peak f01.
            case "E007":
                LoadAndApplyAttackSequence(view, "E007", "Monsters/E007_ShardGargoyle/attack_f", new Vector2(0.662f, 0.077f), 1.224f);
                break;
            // E008 뿌리 정령 — 12프레임. 캐릭터 우측 위치 pivot.x≈0.64. peak 높이 80.5% → boost 1.243. peak f01.
            case "E008":
                LoadAndApplyAttackSequence(view, "E008", "Monsters/E008_RootSprite/attack_f", new Vector2(0.641f, 0.107f), 1.243f);
                break;
            // E009 유적의 망령 — 12프레임. 캐릭터 우측 위치 pivot.x≈0.65(돌진용), pivot.y 거의 바닥. peak 81.4% → boost 1.228.
            case "E009":
                LoadAndApplyAttackSequence(view, "E009", "Monsters/E009_RuinWraith/attack_f", new Vector2(0.647f, 0.039f), 1.228f);
                break;
            // E010 그림자 박쥐 — 12프레임. 비행체라 pivot.y가 28%로 공중에 위치, 우측 lunge용 pivot.x≈0.61. peak f03 72.1% → boost 1.387.
            case "E010":
                LoadAndApplyAttackSequence(view, "E010", "Monsters/E010_ShadeBat/attack_f", new Vector2(0.611f, 0.279f), 1.387f);
                break;
            // E012 어둠 페어리 — 12프레임. 캐릭터 약간 우측 pivot.x≈0.54. idle/attack 둘 다 +15% 위해 field_scale=1.15. peak f05 80.9% → boost 1.236.
            case "E012":
                LoadAndApplyAttackSequence(view, "E012", "Monsters/E012_ShadowFairy/attack_f", new Vector2(0.544f, 0.099f), 1.236f);
                break;
            // E013 호박머리 허수아비 — 12프레임. 본체 정지, 입에서 ember pulse만. 캐릭터 중앙 pivot.x≈0.50, 발 17%. peak f09 68.0% → boost 1.471.
            case "E013":
                LoadAndApplyAttackSequence(view, "E013", "Monsters/E013_PumpkinScarecrow/attack_f", new Vector2(0.495f, 0.172f), 1.471f);
                break;
            // E104 새끼 어둠용(ELITE) — 12프레임. 비행체. 캐릭터 우측 pivot.x≈0.68(돌진용), 발 19%. peak f06 77.0% → boost 1.298. (E015 → E104 ELITE 승격)
            case "E104":
                LoadAndApplyAttackSequence(view, "E104", "Monsters/E104_ShadowDrakeling/attack_f", new Vector2(0.684f, 0.193f), 1.298f);
                break;
            // E014 해골 촛대 — 12프레임. 본체 정지, 입에서 ember pulse만. 캐릭터 중앙 pivot.x≈0.49, 발 1.5%. peak f05 77.4% → boost 1.293.
            case "E014":
                LoadAndApplyAttackSequence(view, "E014", "Monsters/E014_SkullCandelabra/attack_f", new Vector2(0.491f, 0.015f), 1.293f);
                break;
            // E016 망령 등불 — 12프레임. 캐릭터 약간 우측 pivot.x≈0.57, 발 5.5%. peak f08 73.5% → boost 1.360.
            case "E016":
                LoadAndApplyAttackSequence(view, "E016", "Monsters/E016_WraithLantern/attack_f", new Vector2(0.567f, 0.055f), 1.360f);
                break;
            // E103 망령 기사 쌍둥이(ELITE, intH=260) — 12프레임. 캐릭터 우측 pivot.x≈0.58(돌진), 발 9.3%. peak f04 72.0% → boost 1.389.
            case "E103":
                LoadAndApplyAttackSequence(view, "E103", "Monsters/E103_WraithKnightTwins/attack_f", new Vector2(0.582f, 0.093f), 1.389f);
                break;
            // E102 폭풍 독수리(ELITE, intH=260) — 12프레임. 비행체 pivot.y=27%, 중앙 pivot.x≈0.50. idle 47.6% / peak f04 70.8% (공격 시 펴짐) → boost 0.672로 축소.
            case "E102":
                LoadAndApplyAttackSequence(view, "E102", "Monsters/E102_StormEagle/attack_f", new Vector2(0.497f, 0.270f), 0.672f);
                break;
        }
        // 동시 박자 방지 — 개체별 해시로 주기(freq)와 위상(phase)을 모두 분산.
        // freq: 0.12 ~ 0.19Hz (~5.3s ~ 8.3s), phase: 0 ~ 2π
        int hash = e.GetHashCode();
        float freqNoise = ((hash >> 10) & 0x3FF) / 1024f;        // 0~1
        float phaseNoise = (hash & 0x3FF) / 1024f;               // 0~1
        view.breathingFreq = 0.12f + freqNoise * 0.07f;
        view.breathingPhase = phaseNoise * Mathf.PI * 2f;
        // 이끼 잡몹: 도깨비불 톤 — 본체 50% 알파 + 펄스 글로우 차일드 + 숨쉬기 진폭/주파수 강화.
        if (e.isMoss)
        {
            view.SetPhantomMode(true, 0.75f, new Color(0.65f, 0.36f, 0.12f, 1f));
            view.breathingAmp = 0.06f;                    // 기본 0.015 → 4x: 불꽃 흔들림
            view.breathingFreq = 0.30f + freqNoise * 0.15f; // 0.30~0.45Hz: 본체보다 2~3배 빠른 깜빡임
        }
        _enemyViews[e] = view;

        // 발밑 그림자 — 이미지 파일명 규칙(`Monsters/shadow/{이름}_shadow`)으로 로드.
        // 예: crow.png → Monsters/shadow/crow_shadow, E103_WraithKnightTwins.png → E103_WraithKnightTwins_shadow.
        // 없으면 조용히 스킵(모든 몬스터에 그림자 에셋이 있어야 하는 건 아님).
        if (_enemyShadowEnabled && !string.IsNullOrEmpty(e.data.image))
        {
            string imgName = Path.GetFileNameWithoutExtension(e.data.image);
            var shadowTex = Resources.Load<Texture2D>($"Monsters/shadow/{imgName}_shadow");
            if (shadowTex != null)
            {
                var shadowSprite = Sprite.Create(
                    shadowTex,
                    new Rect(0, 0, shadowTex.width, shadowTex.height),
                    new Vector2(0.5f, 0.5f),
                    100f);
                view.SetShadowSprite(shadowSprite, _enemyShadowHeight, Vector2.zero, _enemyShadowAlpha);
            }
        }
    }

    private void CleanupDeadEnemyViews()
    {
        if (_enemyViews.Count == 0) return;
        List<EnemyInstance> toRemove = null;
        foreach (var kv in _enemyViews)
        {
            if (!kv.Key.IsDead) continue;

            // view가 이미 destroy됐다면(DeathRoutine self-destroy 완료) dict에서만 cleanup.
            if (kv.Value == null || kv.Value.gameObject == null)
            {
                (toRemove ??= new List<EnemyInstance>()).Add(kv.Key);
                continue;
            }

            // 사망 모션이 아직 시작 안 됐다면 시작. OnEntityKilled 구독이 먼저 잡았다면 이미 IsDying.
            if (!kv.Value.IsDying)
                kv.Value.PlayDeath();
        }
        if (toRemove != null)
            foreach (var k in toRemove) _enemyViews.Remove(k);
    }

    private void DestroyAllEnemyViews()
    {
        foreach (var kv in _enemyViews)
        {
            if (kv.Value != null && kv.Value.gameObject != null)
                Destroy(kv.Value.gameObject);
        }
        _enemyViews.Clear();
    }

    private void InitBattleFromRunState()
    {
        var gsm = GameStateManager.Instance;
        var run = gsm.CurrentRun;
        var enemies = gsm.CurrentEnemies;

        Debug.Log($"[BattleUI] InitBattleFromRunState: enemies={enemies?.Count ?? 0}, hp={run?.playerCurrentHp ?? -1}");

        if (run == null || enemies == null || enemies.Count == 0)
        {
            Debug.LogError("[BattleUI] Cannot init battle — run is null or enemies empty");
            return;
        }

        _backgroundTexture = LoadBackgroundFor(enemies[0]);
        UpdateWorldBackground();
        EnsurePlayerView(); // 캐릭터 변경 시 플레이어 뷰 재생성 (린네 ↔ 아케네)
        DestroyAllEnemyViews();
        _lastKnownHp.Clear();
        _lastKnownBlock.Clear();
        _entityShieldFxStart.Clear();
        _hpBarDisplayedFrac.Clear();
        _floaters.Clear();
        _dyingSummons.Clear();
        _exhaustFlyCards.Clear();
        _exhaustPhantomIndex = -1;
        _exhaustPhantomStartTime = -1f;
        _pending.Clear();
        _battleEndQueued = false;
        _targetingCardIndex = -1;
        _targetingSummonIndex = -1;
        _targetingSummonSkillIndex = -1;
        _swapFromCardIndex = -1;
        _targetingPotionIndex = -1;
        _selectedPotionIndex = -1;

        var chapter = DataManager.Instance.GetChapter(run.chapterId);
        int mana = chapter?.mana ?? 3;
        int maxFieldSize = chapter?.maxFieldSize ?? 2;

        // 이전 전투에서 남은 애니메이션 상태를 정리
        EndDiscardFlyAnimation();
        EndDrawFlyAnimation();
        EndReshuffleAnimation();

        // floor 진행 스케일 — 일반 적만 대상. 엘리트/보스는 BattleManager에서 1.0 유지.
        int floor = run.currentFloor;
        float hpScale = DianoCard.Game.GameStateManager.NormalEnemyHpScaleForFloor(floor);
        float dmgScale = DianoCard.Game.GameStateManager.NormalEnemyDamageScaleForFloor(floor);

        // 튜토리얼 sandbox: 슬라임이 본 게임 스펙대로면 학습 흐름 안에 처치 불가 → 약화 강제.
        if (DianoCard.Game.GameStateManager.Instance != null
            && DianoCard.Game.GameStateManager.Instance.IsTutorialMode)
        {
            hpScale = 0.35f;
            dmgScale = 0.5f;
        }

        _battle = new BattleManager();
        // 소진 카드 번업 연출 — BattleManager가 RemoveAt 직전에 발행하는 이벤트를 받아
        // 손패 부채꼴 위치에 비행 카드 한 장을 띄운다. _battle 교체 시 옛 핸들러는 GC.
        _battle.OnCardExhausting += HandleCardExhausting;
        // 유물/포션 디스패처가 RunState를 참조해야 함 — StartBattle 호출 전에 세팅.
        _battle.run = run;
        _battle.StartBattle(
            new List<CardData>(run.deck),
            new List<EnemyData>(enemies), // 복사본 전달
            mana,
            run.playerMaxHp,
            maxFieldSize,
            hpScale,
            dmgScale,
            run.playerCurrentHp);

        PrepareEnemyViews();
        PreparePlayerView();
        SpawnBackgroundFX();
        SpawnBackgroundVines();

        // 전투 시작 시 이미 Draw된 첫 손패를 드로우 애니메이션으로 등장시킨다.
        if (_battle.state.hand.Count > 0)
        {
            StartCoroutine(InitialDrawCoroutine());
        }

        // 진입 페이드아웃 시작 — OnGUI 끝부분에서 검은 오버레이가 점차 투명해진다.
        _battleEnterFadeStart = Time.unscaledTime;
    }

    /// <summary>전투 시작 직후 초기 손패를 덱에서 뽑혀나오는 것처럼 애니메이션.</summary>
    private IEnumerator InitialDrawCoroutine()
    {
        // 한 프레임 대기 — OnGUI가 뷰를 한 번 셋업한 뒤 애니메이션 시작
        yield return null;
        if (_battle?.state == null || _battle.state.hand.Count == 0) yield break;

        BeginDrawFlyAnimation(_battle.state, 0);
        float wait = GetDrawFlyTotalDuration() + 0.05f;
        yield return new WaitForSeconds(wait);
        EndDrawFlyAnimation();
    }

    /// <summary>
    /// 전투 시작 직후 적 뷰를 생성하고 올바른 world 위치로 초기화.
    /// 이걸 안 하면 OnGUI 전까지 (0,0,0)에서 한 프레임 깜빡이는 현상이 생긴다.
    /// </summary>
    private void PrepareEnemyViews()
    {
        if (_battle?.state == null || Camera.main == null) return;
        ComputeSlotPositions(_battle.state);
        foreach (var e in _battle.state.enemies)
        {
            if (e.IsDead) continue;
            EnsureEnemyView(e);
            if (!_enemyViews.TryGetValue(e, out var view)) continue;
            if (!_slotPositions.TryGetValue(e, out var center)) continue;

            float h = GetEnemyDrawHeight(e);
            float w = h;
            var rect = new Rect(center.x - w / 2f, center.y - h / 2f, w, h);
            Vector3 feetWorld = GuiToWorld(new Vector2(center.x, rect.yMax));
            Vector3 topWorld  = GuiToWorld(new Vector2(center.x, rect.y));
            float worldHeight = Mathf.Abs(feetWorld.y - topWorld.y);
            view.SetBasePosition(feetWorld);
            view.SetWorldHeight(worldHeight);
        }
    }

    /// <summary>
    /// PlayerView 도 적 뷰처럼 OnGUI 전에 world 위치/스케일을 미리 셋업.
    /// 안 하면 EnsurePlayerView 직후 한 프레임 동안 (0,0,0)에 native PPU 스케일로
    /// 거대하게 그려져서 다리만 보이는 버그가 발생한다 (DrawPlayerNPC와 동일 공식).
    /// </summary>
    private void PreparePlayerView()
    {
        if (_playerView == null || _battle?.state == null || Camera.main == null) return;
        if (!_slotPositions.TryGetValue(_battle.state.player, out var center)) return;

        const float h = 257f; // DrawPlayerNPC의 h와 일치
        Vector2 feetGui = new Vector2(center.x, center.y + h / 2f);
        Vector2 topGui  = new Vector2(center.x, center.y - h / 2f);
        Vector3 feetWorld = GuiToWorld(feetGui);
        Vector3 topWorld  = GuiToWorld(topGui);
        float worldHeight = Mathf.Abs(feetWorld.y - topWorld.y);

        Vector3 pivotOffset = Vector3.zero;
        var psr = _playerView.GetComponent<SpriteRenderer>();
        if (psr != null && psr.sprite != null && psr.sprite.bounds.size.y > 0.001f)
        {
            float s = worldHeight / psr.sprite.bounds.size.y;
            pivotOffset = new Vector3(0f, -psr.sprite.bounds.min.y * s, 0f);
        }
        _playerView.SetBasePosition(feetWorld + pivotOffset);
        _playerView.SetWorldHeight(worldHeight);
        Vector2 shadowOffset = new Vector2(_entityShadowOffsetX, _entityShadowOffsetY) * worldHeight;
        _playerView.UpdateShadowParams(_entityShadowHeight, _entityShadowWidthScale, shadowOffset, _entityShadowAlpha);
        // 위치/스케일 셋업 끝났으니 이제 노출. EnsurePlayerView에서 SetVisible(false)로 렌더 차단해둔 상태.
        _playerView.SetVisible(true);
    }

    private void SpawnBackgroundVines()
    {
        DespawnBackgroundVines();
        if (Camera.main == null || _backgroundTexture == null) return;

        string bgName = _backgroundTexture.name;
        foreach (var v in _bgVineEntries)
        {
            if (v == null || string.IsNullOrEmpty(v.resourcePath)) continue;
            if (!string.IsNullOrEmpty(v.backgroundName) && v.backgroundName != bgName) continue;

            var tex = Resources.Load<Texture2D>(v.resourcePath);
            if (tex == null)
            {
                Debug.LogWarning($"[BattleUI] Vine texture not found: {v.resourcePath}");
                continue;
            }

            var sprite = Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 1.0f),
                100f);

            var go = new GameObject($"_Vine ({System.IO.Path.GetFileName(v.resourcePath)})");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = v.sortingOrder;
            sr.flipX = v.flipX;
            sr.color = v.color;

            go.transform.position = GuiToWorld(v.guiPos);
            go.transform.localScale = Vector3.one * v.scale;

            if (v.useGodRay)
            {
                var god = go.AddComponent<DianoCard.FX.GodRayFX>();
                god.minAlpha = v.godRayMinAlpha;
                god.maxAlpha = v.godRayMaxAlpha;
                god.pulseSpeed = v.godRayPulseSpeed;
                god.swayAngle = v.swayAngle;
                god.swaySpeed = v.swaySpeed;
                god.phaseOffset = v.swayPhase;
            }
            else
            {
                var sway = go.AddComponent<DianoCard.FX.VineSway>();
                sway.angle = v.swayAngle;
                sway.speed = v.swaySpeed;
                sway.phase = v.swayPhase;
            }

            _spawnedVines.Add(go);
        }
    }

    private void DespawnBackgroundVines()
    {
        for (int i = 0; i < _spawnedVines.Count; i++)
            if (_spawnedVines[i] != null) Destroy(_spawnedVines[i]);
        _spawnedVines.Clear();
    }

    private void SpawnBackgroundFX()
    {
        DespawnBackgroundFX();
        if (Camera.main == null || _backgroundTexture == null)
        {
            Debug.LogWarning($"[BattleUI] SpawnBackgroundFX skipped: cam={Camera.main}, bg={_backgroundTexture}");
            return;
        }

        string bgName = _backgroundTexture.name;
        Debug.Log($"[BattleUI] SpawnBackgroundFX: bg='{bgName}', entryCount={_bgFxEntries.Count}");

        int spawned = 0;
        foreach (var e in _bgFxEntries)
        {
            if (e == null || e.prefab == null) continue;
            if (!string.IsNullOrEmpty(e.backgroundName) && e.backgroundName != bgName) continue;

            Vector3 world = GuiToWorld(e.guiPos);
            var go = Instantiate(e.prefab, world, Quaternion.identity);
            ApplyScaleAndIntensity(go, e.scale, e.intensity);
            _spawnedBgFx.Add(go);
            spawned++;
            Debug.Log($"[BattleUI]   spawned '{e.prefab.name}' @ gui({e.guiPos.x},{e.guiPos.y}) -> world({world.x:F2},{world.y:F2}), scale={e.scale}, intensity={e.intensity}");
        }
        Debug.Log($"[BattleUI] SpawnBackgroundFX done: {spawned} instances");
    }

    private static void ApplyScaleAndIntensity(GameObject go, float scale, float intensity)
    {
        go.transform.localScale = Vector3.one * scale;
        var systems = go.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < systems.Length; i++)
        {
            var ps = systems[i];
            var main = ps.main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var emission = ps.emission;
            emission.rateOverTimeMultiplier *= intensity;
            emission.rateOverDistanceMultiplier *= intensity;
        }
    }

    private void DespawnBackgroundFX()
    {
        for (int i = 0; i < _spawnedBgFx.Count; i++)
            if (_spawnedBgFx[i] != null) Destroy(_spawnedBgFx[i]);
        _spawnedBgFx.Clear();
    }

    // =========================================================
    // Normal1 바닥 안개 (IMGUI 파티클) — LobbyUI의 "Bottom Smoke" 포팅
    // =========================================================

    private void DrawNormal1Fog()
    {
        if (!_normal1FogEnabled) return;
        if (_normal1FogCount <= 0) return;
        if (_backgroundTexture == null || _backgroundTexture.name != "BG_Ch1_Battle_01") return;
        if (_normal1FogSpawnRect.width <= 0f || _normal1FogSpawnRect.height <= 0f) return;

        if (_normal1FogTex == null) _normal1FogTex = MakeFogRadialGlow(64);
        if (_normal1FogTex == null) return;

        float t = Time.unscaledTime;
        var prevCol = GUI.color;
        const int seedOffset = 500; // 다른 파티클 시드와 겹치지 않게

        for (int i = 0; i < _normal1FogCount; i++)
        {
            int idx = i + seedOffset;
            float seed = FogHash01(idx * 0.6180339f + 0.13f);
            float speed = _normal1FogRiseSpeed * (0.75f + seed * 0.6f);
            float phase = seed * 7.13f;
            float life = ((t * speed) + phase) % 1f;
            if (life < 0f) life += 1f;

            float spawnU = FogHash01(idx * 12.9898f);
            float spawnV = FogHash01(idx * 78.233f);
            float sway = Mathf.Sin(life * Mathf.PI * 2f * _normal1FogSwayFrequency + seed * 6f) * _normal1FogSwayAmount;

            float centerX = _normal1FogSpawnRect.x + _normal1FogSpawnRect.width * 0.5f;
            float x = centerX + (spawnU - 0.5f) * _normal1FogSpawnRect.width + sway;
            float y = _normal1FogSpawnRect.y + spawnV * _normal1FogSpawnRect.height - life * _normal1FogRiseHeight;

            float sizeT = Mathf.Sin(life * Mathf.PI);
            float baseSize = Mathf.Lerp(_normal1FogSizeRange.x, _normal1FogSizeRange.y, FogHash01(idx * 37.719f));
            float size = baseSize * (0.45f + 0.55f * sizeT);

            float fade = Mathf.Sin(life * Mathf.PI);
            float flicker = (1f - _normal1FogFlickerDepth) + _normal1FogFlickerDepth * Mathf.Sin(t * _normal1FogFlickerSpeed + seed * 17f);
            float alpha = Mathf.Clamp01(fade * flicker) * _normal1FogAlphaMul;

            // 외곽 블룸 (크고 흐리게)
            float bloomSize = size * _normal1FogBloomScale;
            GUI.color = new Color(_normal1FogOuterColor.r, _normal1FogOuterColor.g, _normal1FogOuterColor.b,
                _normal1FogOuterColor.a * alpha * _normal1FogBloomAlphaMul);
            GUI.DrawTexture(new Rect(x - bloomSize * 0.5f, y - bloomSize * 0.5f, bloomSize, bloomSize),
                _normal1FogTex, ScaleMode.StretchToFill, alphaBlend: true);

            // 중간 글로우
            float glowSize = size * 1.6f;
            GUI.color = new Color(_normal1FogOuterColor.r, _normal1FogOuterColor.g, _normal1FogOuterColor.b,
                _normal1FogOuterColor.a * alpha * 0.7f);
            GUI.DrawTexture(new Rect(x - glowSize * 0.5f, y - glowSize * 0.5f, glowSize, glowSize),
                _normal1FogTex, ScaleMode.StretchToFill, alphaBlend: true);

            // 안쪽 코어
            GUI.color = new Color(_normal1FogInnerColor.r, _normal1FogInnerColor.g, _normal1FogInnerColor.b,
                _normal1FogInnerColor.a * alpha);
            GUI.DrawTexture(new Rect(x - size * 0.5f, y - size * 0.5f, size, size),
                _normal1FogTex, ScaleMode.StretchToFill, alphaBlend: true);
        }
        GUI.color = prevCol;
    }

    private static float FogHash01(float x)
    {
        float s = Mathf.Sin(x) * 43758.5453f;
        s -= Mathf.Floor(s);
        return s;
    }

    private static Texture2D MakeFogRadialGlow(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave,
        };
        var px = new Color[size * size];
        float c = (size - 1) * 0.5f;
        float maxR = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - c) / maxR;
                float dy = (y - c) / maxR;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                a = a * a * (3f - 2f * a);
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    // =========================================================
    // 공격 이펙트 FX — peak 프레임 타이밍에 타겟 위치에 오버레이 스폰
    // =========================================================

    private Sprite _playerAttackFxSprite;
    private Sprite _playerFireballSprite;  // CH002 시전 발사체 — 손→적 비행 (있을 때만 임팩트 FX 대신 사용)

    /// <summary>
    /// 공격 이펙트 스프라이트를 타겟 world 위치에 잠깐 스폰.
    /// scale-up(0→1) → hold → fade-out 으로 자연스럽게 사라짐.
    /// </summary>
    private void SpawnAttackFx(Sprite sprite, Vector3 targetWorld, float peakDelay, float lifetime = 0.35f, float size = 1.6f)
    {
        if (sprite == null) return;
        StartCoroutine(AttackFxRoutine(sprite, targetWorld, peakDelay, lifetime, size));
    }

    private IEnumerator AttackFxRoutine(Sprite sprite, Vector3 targetWorld, float peakDelay, float lifetime, float size)
    {
        if (peakDelay > 0f) yield return new WaitForSeconds(peakDelay);

        var go = new GameObject("AttackFx");
        go.transform.SetParent(transform, worldPositionStays: false);
        go.transform.position = targetWorld;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = 120; // 캐릭터(50)보다 위에

        // 스프라이트 월드 높이를 size에 맞춤
        float baseH = sprite.bounds.size.y;
        if (baseH <= 0.01f) baseH = 1f;
        float scaleVal = size / baseH;

        // 0~20%: scale-up + 약한 회전, 20~65%: 유지, 65~100%: 페이드/축소 아웃
        float t = 0f;
        float rot0 = UnityEngine.Random.Range(-15f, 15f);
        while (t < lifetime)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / lifetime);
            float s, a;
            if (p < 0.20f)
            {
                float k = p / 0.20f;
                s = Mathf.Lerp(0.6f, 1.1f, k);
                a = Mathf.Lerp(0f, 1f, k);
            }
            else if (p < 0.65f)
            {
                s = Mathf.Lerp(1.1f, 1.0f, (p - 0.20f) / 0.45f);
                a = 1f;
            }
            else
            {
                float k = (p - 0.65f) / 0.35f;
                s = Mathf.Lerp(1.0f, 1.15f, k);
                a = Mathf.Lerp(1f, 0f, k);
            }
            go.transform.localScale = new Vector3(scaleVal * s, scaleVal * s, 1f);
            go.transform.rotation = Quaternion.Euler(0, 0, rot0 * (1f - p));
            var c = sr.color; c.a = a; sr.color = c;
            yield return null;
        }
        Destroy(go);
    }

    /// <summary>화염구 임팩트 시점에 _battle.PlayCard 호출 — 데미지/HP/상태 업데이트가 시각 임팩트와 동기화.
    /// 트레이드오프: PlayCard 지연 동안 카드는 손에 남아있고 마나도 안 빠짐. 더블클릭 방지 필요할 수 있음.</summary>
    private IEnumerator DelayedPlayCardOnImpact(System.Action playCardAction)
    {
        yield return new WaitForSeconds(PlayerFireballImpactDelay);
        playCardAction();
    }

    /// <summary>플레이어 공격 시 ComputeAttackDir + 타겟 world 좌표 기반으로 FX 예약.
    /// CH002(Arkane) 절차 화염구 발사체 — 시전 중 피크 버스트 시점에 손→적 비행. PlayerView가 있어야 정확한 손 위치 계산 가능.</summary>
    private void TriggerPlayerAttackFx(int preferredEnemyIdx, float attackDuration = 0.75f)
    {
        var targetWorld = GetAttackTargetWorld(preferredEnemyIdx);
        if (targetWorld == Vector3.zero) return;

        // 절차 화염구 발사체 (BossProjectile.SpawnCrescent를 화염색으로 재활용)
        // 0.55 = 9프레임 / 0.75s 시퀀스 기준 frame 5(피크 버스트, 화염구 완전 형성) 시점에 손에서 발사.
        if (_playerView != null)
        {
            StartCoroutine(FireballProjectileRoutine(attackDuration * 0.55f, targetWorld));
            return;
        }

        // 폴백: 임팩트 FX (slash_gold 등) — PlayerView 없을 때만
        if (_playerAttackFxSprite == null) return;
        SpawnAttackFx(_playerAttackFxSprite, targetWorld, peakDelay: attackDuration * 0.55f, lifetime: 0.35f, size: 1.8f);
    }

    /// <summary>화염구 발사체 — 보스 SpawnCrescent를 그대로 재활용, 색만 화염 주황으로 덮어쓴다.
    /// 모양/잔상/wobble/페이드 모두 보스와 동일.</summary>
    private IEnumerator FireballProjectileRoutine(float launchDelay, Vector3 targetWorld)
    {
        if (launchDelay > 0f) yield return new WaitForSeconds(launchDelay);
        if (_playerView == null) yield break;

        // attack/10.png(1272x1628, pivot 0.409,0) frame 10 화염구 코어 픽셀 = (965, 316).
        // pivot pixel: (520, 0 bottom). sprite-local (PPU=100): ((965-520)/100, (1628-316)/100) = (4.45, 13.12).
        // 월드 좌표는 transform.localScale (ApplyWorldHeight × _sequenceScaleBoost 1.131) 곱해서 변환.
        const float handLocalX = 4.45f;
        const float handLocalY = 13.12f;
        float renderScale = _playerView.transform.localScale.x;
        Vector3 handPos = _playerView.transform.position + new Vector3(handLocalX * renderScale, handLocalY * renderScale, 0f);

        // 화구 — 보스 비행 곡선 차용, 모양/잔상/wobble은 화구용으로 커스텀.
        // - customSprite: 중앙 진함→바깥 옅어지는 양방향 cos 페이드 반달
        // - yGrowEnd 3.5: Y(두께)가 3.5배까지 크게 부풀어 오름
        // - easeOutPower 3.5: 처음 매우 빠름 → 끝 부드럽게 감속
        // - alphaFadeEnd 0.30: 끝에서 30%까지 옅어짐
        // - enableWobble false: 검 휘두르는 펄럭임 끄기
        // - afterimageCount 0: 잔상(두 겹 보이는 사본) 제거
        var proj = BossProjectile.SpawnCrescent(
            from: handPos,
            to: targetWorld,
            duration: 0.55f,
            worldHeight: 1.4f,
            sortingOrder: 130,
            yGrowEnd: 3.5f,
            easeOutPower: 3.5f,
            alphaFadeEnd: 0.30f,
            customSprite: BossProjectile.GetSharedCrescentSpriteSoft(),
            enableWobble: false,
            afterimageCount: 0);

        // 본체 + 잔상 모든 SpriteRenderer를 활활 타는 주황으로 덮어쓰기. alpha는 보존(잔상 페이드 유지).
        Color flame = new Color(1.0f, 0.45f, 0.10f, 1f);
        foreach (var sr in proj.GetComponentsInChildren<SpriteRenderer>())
        {
            var c = sr.color;
            sr.color = new Color(flame.r, flame.g, flame.b, c.a);
        }
    }

    // 공격 방향 (플레이어 → 타겟 적). 기본은 오른쪽(+x). 적 위치를 world로 변환해 벡터 계산.
    private Vector3 ComputeAttackDir(int preferredEnemyIdx)
    {
        var target = GetAttackTargetWorld(preferredEnemyIdx);
        if (target == Vector3.zero || _playerView == null) return Vector3.right;
        Vector3 dir = target - _playerView.transform.position;
        dir.z = 0f;
        if (dir.sqrMagnitude < 0.0001f) return Vector3.right;
        return dir.normalized;
    }

    // 공격 타겟 적의 world 위치 (torso 부근). preferredIdx 유효하면 그 적, 아니면 첫 살아있는 적.
    private Vector3 GetAttackTargetWorld(int preferredEnemyIdx = -1)
    {
        var enemies = _battle?.state?.enemies;
        if (enemies == null || enemies.Count == 0 || Camera.main == null) return Vector3.zero;

        EnemyInstance target = null;
        if (preferredEnemyIdx >= 0 && preferredEnemyIdx < enemies.Count && !enemies[preferredEnemyIdx].IsDead)
            target = enemies[preferredEnemyIdx];
        else
        {
            foreach (var e in enemies)
            {
                if (!e.IsDead) { target = e; break; }
            }
        }
        if (target == null || !_slotPositions.TryGetValue(target, out var slot)) return Vector3.zero;

        // slot은 발 부근 IMGUI 좌표. 몸통 중앙 부근을 타겟으로 잡기 위해 위로 올림.
        return GuiToWorld(new Vector2(slot.x, slot.y - 60f));
    }

    private Vector3 GuiToWorld(Vector2 guiPos)
    {
        var cam = Camera.main;
        float scale = Mathf.Min(Screen.width / RefW, Screen.height / RefH);
        float sx = guiPos.x * scale;
        float sy = Screen.height - guiPos.y * scale;
        Vector3 world = cam.ScreenToWorldPoint(new Vector3(sx, sy, _vfxZDistance));
        world.z = 0f;
        return world;
    }

    private void NotifyBattleEnd()
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null || _battle == null) return;

        bool won = _battle.state.PlayerWon;
        int hp = _battle.state.player.hp;
        gsm.EndBattle(won, hp);
    }

    /// <summary>치트: 런타임에 배경을 특정 파일로 교체.</summary>
    public void Cheat_SetBackground(string resourcePath)
    {
        var tex = Resources.Load<Texture2D>(resourcePath);
        if (tex == null)
        {
            Debug.LogWarning($"[Cheat] Background not found: Resources/{resourcePath}");
            return;
        }
        _backgroundTexture = tex;
        // 기존 sprite를 다시 만들게끔 강제 — _worldBgSr는 그대로 두고 sprite만 교체됨
        UpdateWorldBackground();
    }

    /// CheatUI 공격 모션 미리보기 — 카드 사용 없이 PlayAttack + TriggerPlayerAttackFx 만 재생.
    /// 살아있는 적이 있으면 그 적 방향으로, 없으면 우측 기본.
    public void Cheat_PlayPlayerAttack()
    {
        if (_playerView == null)
        {
            Debug.LogWarning("[BattleUI] Cheat_PlayPlayerAttack: PlayerView 없음 — 전투 진입 후 사용");
            return;
        }

        int eIdx = -1;
        var enemies = _battle?.state?.enemies;
        if (enemies != null)
        {
            for (int i = 0; i < enemies.Count; i++)
            {
                if (!enemies[i].IsDead) { eIdx = i; break; }
            }
        }

        _playerView.PlayAttack(ComputeAttackDir(eIdx), distance: 0.08f, duration: PlayerAttackDuration);
        TriggerPlayerAttackFx(eIdx, attackDuration: PlayerAttackDuration);
    }

    /// CheatUI 라이브 튜닝용 — 실전 시퀀스 그대로 재생: 보스 swing → strike 정점 spawn → 명중 시 PlayHit.
    /// HP 데미지는 적용하지 않음 (시각 확인 전용). 보스가 없으면 화면 좌→우 폴백.
    public void Cheat_FireBossCrescent()
    {
        StartCoroutine(CheatFireBossCrescentRoutine());
    }

    private IEnumerator CheatFireBossCrescentRoutine()
    {
        Vector3 spawnPos;
        Vector3 hitPos;
        Vector3 dir = Vector3.right;
        float distToTarget = 6f;

        BattleEntityView bossView = null;
        foreach (var kv in _enemyViews)
        {
            if (kv.Value != null) { bossView = kv.Value; break; }
        }

        const float swingDuration = 1.5f;

        if (bossView != null && _playerView != null)
        {
            Vector3 toTarget = _playerView.transform.position - bossView.transform.position;
            if (toTarget.sqrMagnitude > 0.01f)
            {
                dir = toTarget.normalized;
                distToTarget = toTarget.magnitude;
            }
            // 보스 swing 모션 재생.
            bossView.PlayAttack(dir, distance: 0.30f, duration: swingDuration);

            // strike 페이즈 끝(swing의 45%)까지 대기 → 검이 정점 찍은 순간 spawn.
            yield return new WaitForSeconds(swingDuration * 0.45f);

            var bossSr = bossView.GetComponent<SpriteRenderer>();
            float bossH = (bossSr != null && bossSr.bounds.size.y > 0.001f) ? bossSr.bounds.size.y : 2.0f;
            // 검 끝 — 보스 어깨~머리 사이(70%) → 완만한 위→아래 각도.
            spawnPos = bossView.transform.position + Vector3.up * (bossH * 0.70f) + dir * (bossH * 0.50f);

            var psr = _playerView.GetComponent<SpriteRenderer>();
            if (psr != null && psr.sprite != null)
            {
                Bounds b = psr.bounds;
                hitPos = b.center + Vector3.up * (b.size.y * 0.15f);
            }
            else hitPos = _playerView.transform.position;
        }
        else if (Camera.main != null)
        {
            var cam = Camera.main;
            float z = -cam.transform.position.z;
            // 위→아래 각도 — 좌상에서 우중으로.
            spawnPos = cam.ViewportToWorldPoint(new Vector3(0.20f, 0.75f, z));
            hitPos   = cam.ViewportToWorldPoint(new Vector3(0.75f, 0.45f, z));
            dir = (hitPos - spawnPos).normalized;
            distToTarget = (hitPos - spawnPos).magnitude;
        }
        else
        {
            yield break;
        }

        float projHeight = Mathf.Clamp(distToTarget * 0.32f, 1.8f, 2.8f);
        float flightTime = Mathf.Clamp(distToTarget * 0.09f, 0.35f, 0.55f);

        // 명중 시 플레이어 PlayHit — 실전에선 DealAttack 경로로 자동 트리거되지만 치트는 데미지 없으니 직접.
        DianoCard.Battle.BossProjectile.SpawnCrescent(
            spawnPos, hitPos,
            duration: flightTime,
            worldHeight: projHeight,
            sortingOrder: 110,
            onHit: () =>
            {
                if (_playerView != null) _playerView.PlayHit();
            });
    }

    /// <summary>
    /// 챕터 BG를 Start 시점에 묶어서 Resources.Load → 첫 전투 진입 스파이크 제거.
    /// LoadBackgroundFor는 이 캐시를 그대로 반환한다. null이면 lazy fallback.
    /// </summary>
    private void PreloadBackgrounds()
    {
        _bgCh1Normal = Resources.Load<Texture2D>("Backgrounds/BG_Ch1_Battle_01");
        _bgCh1Elite  = Resources.Load<Texture2D>("Backgrounds/BG_Ch1_Elite_01");
        _bgCh1Boss   = Resources.Load<Texture2D>("Backgrounds/BG_Ch1_Boss_01");
        if (_bgCh1Normal == null) Debug.LogWarning("[BattleUI] Preload missing: Resources/Backgrounds/BG_Ch1_Battle_01");
        if (_bgCh1Elite  == null) Debug.LogWarning("[BattleUI] Preload missing: Resources/Backgrounds/BG_Ch1_Elite_01");
        if (_bgCh1Boss   == null) Debug.LogWarning("[BattleUI] Preload missing: Resources/Backgrounds/BG_Ch1_Boss_01");
    }

    private Texture2D LoadBackgroundFor(EnemyData enemy)
    {
        if (enemy.enemyType == EnemyType.BOSS)
            return _bgCh1Boss  ?? Resources.Load<Texture2D>("Backgrounds/BG_Ch1_Boss_01");
        if (enemy.enemyType == EnemyType.ELITE)
            return _bgCh1Elite ?? Resources.Load<Texture2D>("Backgrounds/BG_Ch1_Elite_01");
        return _bgCh1Normal    ?? Resources.Load<Texture2D>("Backgrounds/BG_Ch1_Battle_01");
    }

    // =========================================================
    // Damage detection & floaters
    // =========================================================

    /// <summary>매 프레임 공룡/적 block을 직전 값과 비교 — 증가했으면 방패 FX 시작 시각을 기록.
    /// 첫 등장 프레임은 seed만 하고 트리거하지 않음(플레이어 block 추적과 동일 정책).
    /// 사망/제거된 entity는 _lastKnownHp가 비워질 때 함께 정리하지 않으나, 항목 수가 작아 무시 가능.</summary>
    private void DetectEntityBlockGain()
    {
        var state = _battle.state;
        if (state == null) return;

        foreach (var s in state.field)
        {
            int cur = s.block;
            if (_lastKnownBlock.TryGetValue(s, out int prev) && cur > prev)
                _entityShieldFxStart[s] = Time.time;
            _lastKnownBlock[s] = cur;
        }
        foreach (var e in state.enemies)
        {
            int cur = e.block;
            if (_lastKnownBlock.TryGetValue(e, out int prev) && cur > prev)
                _entityShieldFxStart[e] = Time.time;
            _lastKnownBlock[e] = cur;
        }
    }

    private void DetectDamage()
    {
        var state = _battle.state;
        _seenThisFrame.Clear();

        int newFloatersThisFrame = 0;

        TryCheckHp(state.player, state.player.hp, ref newFloatersThisFrame);
        foreach (var s in state.field) TryCheckHp(s, s.hp, ref newFloatersThisFrame);
        foreach (var e in state.enemies) TryCheckHp(e, e.hp, ref newFloatersThisFrame);

        if (_lastKnownHp.Count > _seenThisFrame.Count)
        {
            var toRemove = new List<object>();
            foreach (var key in _lastKnownHp.Keys)
                if (!_seenThisFrame.Contains(key)) toRemove.Add(key);
            foreach (var key in toRemove) _lastKnownHp.Remove(key);
        }
    }

    private void TryCheckHp(object unit, int currentHp, ref int newFloatersThisFrame)
    {
        _seenThisFrame.Add(unit);

        if (_lastKnownHp.TryGetValue(unit, out int prev))
        {
            int delta = prev - currentHp;
            if (delta > 0)
            {
                float delay = newFloatersThisFrame * 0.30f;
                bool hasGuiPos = _slotPositions.TryGetValue(unit, out var guiPos);
                // 이 데미지로 entity가 사망했는지 — 사망이면 hit VFX/PlayHit를 스킵해
                // 죽음 모션(DeathRoutine 잉크 잔향)만 단독으로 보이도록 통일.
                bool fatal = IsEntityDead(unit);

                var f = new DamageFloater
                {
                    anchor = unit,
                    amount = delta,
                    delay = delay,
                    age = 0,
                    lastPos = hasGuiPos ? guiPos : default,
                    hasPos = hasGuiPos,
                    kind = DamageFloaterKind.Damage,
                };
                SeedFloaterRandomness(f);
                _floaters.Add(f);
                if (!fatal && hasGuiPos)
                {
                    StartCoroutine(SpawnDamageVFXDelayed(guiPos, delay));
                }
                if (!fatal)
                {
                    if (unit is EnemyInstance ei
                        && _enemyViews.TryGetValue(ei, out var eView)
                        && eView != null)
                    {
                        StartCoroutine(PlayHitDelayed(eView, delay));
                    }
                    else if (unit is Player && _playerView != null)
                    {
                        StartCoroutine(PlayHitDelayed(_playerView, delay));
                    }
                }
                // 카메라 셰이크와 데미지 숫자는 사망 데미지에도 그대로 발동 — 마지막 한 방의 임팩트 유지.
                TriggerDamageShake(delta, delay);
                newFloatersThisFrame++;
            }
            else if (delta < 0)
            {
                // HP 증가 = 회복. 모스 그린 + '+값' 표기. VFX/Hit 모션은 생략(피격이 아니므로).
                float delay = newFloatersThisFrame * 0.30f;
                bool hasGuiPos = _slotPositions.TryGetValue(unit, out var guiPos);
                var f = new DamageFloater
                {
                    anchor = unit,
                    amount = -delta,
                    delay = delay,
                    age = 0,
                    lastPos = hasGuiPos ? guiPos : default,
                    hasPos = hasGuiPos,
                    kind = DamageFloaterKind.Heal,
                };
                SeedFloaterRandomness(f);
                _floaters.Add(f);
                newFloatersThisFrame++;
            }
        }
        _lastKnownHp[unit] = currentHp;
    }

    // 같은 데미지값이어도 spawn마다 다른 모션이 되도록 무작위값 한 번에 결정.
    // 큰 데미지(>=10)는 회전/sway 진폭이 더 큼 → 무게감 차등.
    // 흡수/회복은 모션 변동을 약하게 → 위계상 일반 데미지보다 잔잔하게.
    private void SeedFloaterRandomness(DamageFloater f)
    {
        bool isHeavy = f.kind == DamageFloaterKind.Damage && f.amount >= 10;
        bool isLight = f.kind != DamageFloaterKind.Damage;

        float rotMax = isHeavy ? 10f : (isLight ? 4f : 7f);
        float jitMax = isHeavy ? 14f : (isLight ? 6f : 10f);
        float swayMax = isHeavy ? 6f : (isLight ? 2.5f : 4f);

        f.spawnRotation = UnityEngine.Random.Range(-rotMax, rotMax);
        f.xJitter       = UnityEngine.Random.Range(-jitMax, jitMax);
        f.swayPhase     = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        f.swayAmp       = swayMax;
    }

    // 객체 타입에 따라 IsDead 판정 — TryCheckHp가 hit VFX 스킵 여부 결정에 사용.
    private static bool IsEntityDead(object unit)
    {
        return unit switch
        {
            Player p => p.IsDead,
            SummonInstance s => s.IsDead,
            EnemyInstance e => e.IsDead,
            _ => false,
        };
    }

    private IEnumerator SpawnDamageVFXDelayed(Vector2 guiPos, float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        SpawnDamageVFX(guiPos);
    }

    private IEnumerator PlayHitDelayed(BattleEntityView view, float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        if (view != null) view.PlayHit();
    }

    // 데미지 양에 따라 카메라 셰이크 트리거. 작은 데미지(<10)는 셰이크 없음 — 잡몹 단타에 어지러워지지 않도록.
    // CameraShaker가 자체적으로 "더 약한 요청은 무시" 스택 가드를 갖고 있어 연타 누적 없음.
    private void TriggerDamageShake(int dmg, float delay)
    {
        // 진폭은 화면 높이의 % (0.020 = 2%). 최대 3%로 제한.
        float amp, dur;
        if (dmg >= 30)      { amp = 0.030f; dur = 0.28f; }
        else if (dmg >= 20) { amp = 0.020f; dur = 0.22f; }
        else if (dmg >= 10) { amp = 0.012f; dur = 0.16f; }
        else                return;

        if (delay > 0f) StartCoroutine(ShakeAfterDelay(amp, dur, delay));
        else            DianoCard.FX.CameraShaker.Instance?.Shake(amp, dur);
    }

    private IEnumerator ShakeAfterDelay(float amp, float dur, float delay)
    {
        yield return new WaitForSeconds(delay);
        DianoCard.FX.CameraShaker.Instance?.Shake(amp, dur);
    }

    private void SpawnDamageVFX(Vector2 guiPos)
    {
        if (Camera.main == null) return;
        Vector3 world = GuiToWorld(guiPos);

        // 프리팹에 자체 destroy 로직이 없을 때 화면에 누적되는 걸 막는 안전망.
        const float vfxMaxLifetime = 2f;
        if (_vfxHitA   != null) Destroy(Instantiate(_vfxHitA,   world, Quaternion.identity), vfxMaxLifetime);
        if (_vfxHitD   != null) Destroy(Instantiate(_vfxHitD,   world, Quaternion.identity), vfxMaxLifetime);
        if (_vfxSmokeF != null) Destroy(Instantiate(_vfxSmokeF, world, Quaternion.identity), vfxMaxLifetime);
    }

    private void AdvanceFloaters()
    {
        float dt = Time.deltaTime;
        for (int i = 0; i < _floaters.Count; i++)
        {
            var f = _floaters[i];
            if (f.delay > 0) f.delay = Mathf.Max(0, f.delay - dt);
            else f.age += dt;
        }
        _floaters.RemoveAll(f => f.age >= DamageFloater.LifeTime);
    }

    // =========================================================
    // OnGUI
    // =========================================================

    void OnGUI()
    {
        if (PauseMenuUI.IsOpen) return;
        var gsm = GameStateManager.Instance;
        if (gsm == null) return;
        // Reward 상태에서도 배경/전장은 계속 그려서 보상 화면 뒤로 비춰야 함
        if (gsm.State != GameState.Battle && gsm.State != GameState.Reward) return;
        if (_battle == null || _battle.state == null)
        {
            // _battle 초기화 전 프레임 — 카메라 클리어 컬러(파란 빈 화면) 노출 차단을 위해 검은 화면으로 덮음.
            // Update에서 InitBattleFromRunState가 끝나면 _battle != null이 되고 페이드아웃이 작동.
            DrawFullscreenBlack(1f);
            return;
        }

        // GUI.depth: 낮을수록 앞. BattleUI는 뒤에 깔리고 RewardUI(=0)가 위로 올라오도록
        GUI.depth = 10;

        // 매 프레임 호버 툴팁 상태 리셋 — 이번 프레임에 패시브 칩 위에 마우스 있으면 채워짐.
        _hoveredPassiveTitle = null;
        _hoveredPassiveBody = null;

        EnsureStyles();

        // 타겟팅 화살표 상태는 매 OnGUI 호출마다 다시 빌드된다.
        // (Layout/Repaint 양쪽 모두 — 둘 다 동일한 source/target 후보를 모은다.)
        _arrowSourceValid = false;
        _arrowTargetRects.Clear();
        _attackPreviewEnemy = null;

        bool active = gsm.State == GameState.Battle;

        if (active)
        {
            // 우클릭으로 타겟팅 취소 (카드/공룡/공룡스킬/교체/융합/포션/동족포식/증원 모두)
            if ((_targetingCardIndex >= 0 || _targetingSummonIndex >= 0 || _targetingSummonSkillIndex >= 0 || _swapFromCardIndex >= 0 || _targetingPotionIndex >= 0 || _cannibalFeedFromIndex >= 0 || _reinforcePickerCardIndex >= 0)
                && Event.current.type == EventType.MouseDown
                && Event.current.button == 1)
            {
                if (_targetingSummonIndex >= 0) ShowToast("공격을 취소합니다");
                else if (_targetingSummonSkillIndex >= 0) ShowToast("스킬을 취소합니다");
                else if (_targetingPotionIndex >= 0) ShowToast("포션 사용을 취소합니다");
                else if (_cannibalFeedFromIndex >= 0) ShowToast("동족포식을 취소합니다");
                else if (_reinforcePickerCardIndex >= 0) ShowToast("증원을 취소합니다");
                _targetingCardIndex = -1;
                _targetingSummonIndex = -1;
                _targetingSummonSkillIndex = -1;
                _swapFromCardIndex = -1;
                _targetingPotionIndex = -1;
                _selectedPotionIndex = -1;
                _fusionMaterialAPicked = false;
                _cannibalFeedFromIndex = -1;
                _reinforcePickerCardIndex = -1;
                Event.current.Use();
            }

            // 증원 픽커 활성 동안에도 손에서 카드 인덱스가 invalid 되면 리셋
            if (_reinforcePickerCardIndex >= _battle.state.hand.Count
                || (_reinforcePickerCardIndex >= 0 && _reinforcePickerCardIndex < _battle.state.hand.Count
                    && _battle.state.hand[_reinforcePickerCardIndex].data.subType != CardSubType.REINFORCE))
            {
                _reinforcePickerCardIndex = -1;
            }

            // 손에 없는 인덱스를 가리키고 있으면 리셋
            if (_targetingCardIndex >= _battle.state.hand.Count)
            {
                _targetingCardIndex = -1;
                _fusionMaterialAPicked = false;
            }
            // 융합 카드가 더 이상 hand에 없으면 융합 상태 리셋
            if (_targetingCardIndex < 0) _fusionMaterialAPicked = false;
            if (_swapFromCardIndex >= _battle.state.hand.Count)
            {
                _swapFromCardIndex = -1;
            }
            // 필드에 없는 공룡을 가리키고 있으면 리셋
            if (_targetingSummonIndex >= _battle.state.field.Count
                || (_targetingSummonIndex >= 0
                    && _targetingSummonIndex < _battle.state.field.Count
                    && !_battle.state.field[_targetingSummonIndex].CanAttack))
            {
                _targetingSummonIndex = -1;
            }
            // 스킬 타겟팅 — 필드 인덱스 invalid 또는 스킬 사용 불가 상태로 변하면 리셋
            if (_targetingSummonSkillIndex >= _battle.state.field.Count
                || (_targetingSummonSkillIndex >= 0
                    && !_battle.CanUseSkill(_targetingSummonSkillIndex)))
            {
                _targetingSummonSkillIndex = -1;
            }
            // 카드 타겟팅과 공룡 타겟팅은 상호 배타 — 카드 선택되면 공룡/스킬 해제
            if (_targetingCardIndex >= 0 || _swapFromCardIndex >= 0)
            {
                _targetingSummonIndex = -1;
                _targetingSummonSkillIndex = -1;
                _cannibalFeedFromIndex = -1;
            }
            // 공룡 공격 타겟팅과 스킬 타겟팅도 상호 배타
            if (_targetingSummonIndex >= 0) _targetingSummonSkillIndex = -1;
            if (_targetingSummonSkillIndex >= 0) _targetingSummonIndex = -1;
            // 동족포식 모드 — eater가 사라졌거나 더 이상 사용 불가면 리셋. 카드/스킬 타겟팅과도 상호 배타.
            if (_cannibalFeedFromIndex >= _battle.state.field.Count
                || (_cannibalFeedFromIndex >= 0 && !_battle.CanFeedCannibal(_cannibalFeedFromIndex)))
            {
                _cannibalFeedFromIndex = -1;
            }
            if (_cannibalFeedFromIndex >= 0)
            {
                _targetingSummonIndex = -1;
                _targetingSummonSkillIndex = -1;
            }

            // 포션 타겟팅 — 슬롯이 사라졌거나 포션이 null이면 리셋. 다른 타겟팅과도 상호 배타.
            var runForPotion = GameStateManager.Instance?.CurrentRun;
            if (_targetingPotionIndex >= 0)
            {
                if (runForPotion == null
                    || _targetingPotionIndex >= runForPotion.potions.Count
                    || runForPotion.potions[_targetingPotionIndex] == null)
                {
                    _targetingPotionIndex = -1;
                }
                else
                {
                    // 포션 타겟팅 활성 시 다른 타겟팅 모드 자동 해제.
                    _targetingCardIndex = -1;
                    _targetingSummonIndex = -1;
                    _targetingSummonSkillIndex = -1;
                    _swapFromCardIndex = -1;
                }
            }
        }

        // 1) 배경은 스크린 원본 좌표로 꽉 채움
        GUI.matrix = Matrix4x4.identity;
        DrawBackground();

        // 2) 이후 UI는 1280x720 가상 좌표로 그린 뒤 스케일링
        float scale = Mathf.Min(Screen.width / RefW, Screen.height / RefH);
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));

        // Normal1 배경 전용 바닥 안개 — 월드 스프라이트(캐릭터/배경)는 뒤, 손패/HP 바 등 IMGUI는 앞.
        DrawNormal1Fog();

        var state = _battle.state;

        ComputeSlotPositions(state);

        DrawBattleField(state);
        DrawDyingSummons();
        DrawFloaters();
        var run = gsm.CurrentRun;
        if (run != null)
        {
            var map = gsm.CurrentMap;
            int totalFloors = map != null ? map.totalFloors : 5;
            DrawTopBar(HudContext.Battle, run, run.currentFloor, totalFloors,
                       hpCurrent: state.player.hp, hpMax: state.player.maxHp,
                       turnNumber: state.turn);
        }
        DrawTurnInfo(state);

        // Reward 상태에서는 상호작용 UI(손패/턴 종료/타겟팅 힌트) 숨김.
        // 덱 뷰어가 열려있을 때만 손패 상호작용을 막는다(오버레이가 화면 전체를 덮어 클릭이 새지 않도록).
        // 유물/포션 뷰어는 상단 row 형태라 손패가 가려지지 않으므로 손패는 계속 보이게 둔다.
        if (active && !_deckViewerOpen)
        {
            DrawHand(state);
            HandleHandHideWheelInput();
            DrawEndTurn(state);
            DrawTargetingHint(state);
            DrawSummonAttackHint(state);
            DrawSummonSkillHint(state);
            DrawCannibalFeedHint(state);
            // 손패/배틀필드 렌더가 끝난 뒤 source/target rect가 다 모인 상태에서 그린다.
            DrawTargetingArrow(state);
        }
        DrawToast();

        // 버린 더미로 날아가는 카드 — reward 상태와 관계없이 위에 그려져야 자연스럽다.
        DrawDiscardFlyingCards();

        // 소진 카드 번업 — discard fly와 같은 레이어. ember 입자가 위로 흩어진다.
        DrawExhaustFlyingCards();

        // 덱 리셔플 — 버림 더미 → 덱 더미 스트림
        DrawReshuffleFlyingCards();

        // 덱에서 뽑혀오는 카드 (뒷면 → 플립 → 앞면) — 최상단에 그려 손패/UI 위로 드러나게 함.
        DrawDrawFlyingCards();

        // 증원 픽커 오버레이 — 보유 공룡 그리드. 덱 뷰어보다 먼저 그려도 (둘은 동시에 못 켜짐) 무관.
        DrawReinforcePickerOverlay(gsm);

        // 덱 뷰어 오버레이 — 모든 UI 위에 그려짐.
        DrawDeckViewerOverlay(gsm);

        // 유물 뷰어 오버레이 — 덱 뷰어와 동일 레이어.
        DrawRelicViewerOverlay(gsm);

        // 포션 뷰어 오버레이 — 유물 뷰어와 동일 레이어.
        DrawPotionViewerOverlay(gsm);

        // 패시브 호버 툴팁 — 최상단에 그려야 다른 UI 위로 나옴.
        DrawPassiveTooltip();

        // 진입 페이드아웃 — InitBattleFromRunState 끝나면 시작. 검은 화면이 점차 투명해져 전투 화면이 드러남.
        if (_battleEnterFadeStart >= 0f)
        {
            float t = (Time.unscaledTime - _battleEnterFadeStart) / BattleEnterFadeDuration;
            if (t >= 1f) _battleEnterFadeStart = -1f;
            else
            {
                float alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
                DrawFullscreenBlack(alpha);
            }
        }
    }

    /// <summary>전체 화면을 검정 단색으로 덮는 IMGUI 헬퍼. alpha 0~1.</summary>
    private void DrawFullscreenBlack(float alpha)
    {
        var prevMatrix = GUI.matrix;
        var prevColor = GUI.color;
        GUI.matrix = Matrix4x4.identity;
        GUI.color = new Color(0f, 0f, 0f, Mathf.Clamp01(alpha));
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.matrix = prevMatrix;
        GUI.color = prevColor;
    }

    // 일부 패시브는 단일 아이콘에 숫자 오버레이로 단계 표현.
    private static int PassiveBadgeNumber(string pid)
    {
        switch (pid)
        {
            case "P_DUAL_ACTION":   return 2;
            case "P_TRIPLE_ACTION": return 3;
            case "P_QUAD_ACTION":   return 4;
            default: return 0;
        }
    }

    /// <summary>아이콘 칩을 그리고 호버 시 툴팁 상태를 채운다. 그린 폭을 반환(0=실패).</summary>
    /// <param name="badgeValue">>0이면 우하단에 숫자 오버레이</param>
    private float DrawIconChip(Rect chipRect, Texture2D icon, int badgeValue,
                               string tipTitle, string tipBody)
    {
        if (icon == null) return 0f;

        var ev = Event.current;
        bool hovered = ev != null && chipRect.Contains(ev.mousePosition);

        // 호버 시 살짝 밝히는 게 아닌, 톤만 통일 — 아이콘이 이미 시각적 무게를 가짐.
        var prev = GUI.color;
        GUI.color = hovered ? new Color(1f, 1f, 1f, 1f) : new Color(1f, 1f, 1f, 0.92f);
        GUI.DrawTexture(chipRect, icon, ScaleMode.ScaleToFit, alphaBlend: true);
        GUI.color = prev;

        if (badgeValue > 0)
        {
            float nw = chipRect.width * 0.55f;
            float nh = chipRect.height * 0.55f;
            var nr = new Rect(chipRect.xMax - nw * 0.85f,
                              chipRect.yMax - nh * 0.85f, nw, nh);
            DrawTextWithOutline(nr, badgeValue.ToString(), _intentNumberStyle,
                                Color.white, new Color(0f, 0f, 0f, 0.95f), 1.2f);
        }

        if (hovered && !string.IsNullOrEmpty(tipTitle))
        {
            _hoveredPassiveTitle = tipTitle;
            _hoveredPassiveBody = tipBody;
        }
        return chipRect.width;
    }

    /// <summary>적의 패시브 + 누적 STRENGTH를 한 줄 아이콘 칩으로 그림.</summary>
    private void DrawEnemyPassives(Rect rowRect, EnemyInstance e)
    {
        if (e == null) return;

        EnsurePassiveStyles();

        const float chipSize = 26.4f;
        const float chipGap  = 4f;

        float x = rowRect.x;
        float y = rowRect.y + (rowRect.height - chipSize) * 0.5f;

        // 1) 누적 STRENGTH (extraAttack > 0) — "왜 점점 세지지?"가 한눈에 보이게 맨 앞.
        if (e.extraAttack > 0 && x + chipSize <= rowRect.xMax)
        {
            DrawIconChip(new Rect(x, y, chipSize, chipSize),
                         HeadIcon("STRENGTH"), e.extraAttack,
                         "강화 (Strength)",
                         $"이 적의 공격력이 영구 +{e.extraAttack} 누적되었습니다.");
            x += chipSize + chipGap;
        }

        // 2) 패시브 — enemy_passive.csv의 icon ID 사용. icon 비어있으면 DEBUFF로 폴백.
        if (e.data?.passiveIds != null)
        {
            foreach (var pid in e.data.passiveIds)
            {
                if (x + chipSize > rowRect.xMax) break;

                var p = DianoCard.Data.DataManager.Instance.GetPassive(pid);
                string iconId = (p != null && !string.IsNullOrEmpty(p.icon)) ? p.icon : "DEBUFF";
                var tex = HeadIcon(iconId) ?? HeadIcon("DEBUFF");

                string title = p != null ? p.name : pid;
                string body  = p != null ? p.description : pid;
                int badge = PassiveBadgeNumber(pid);

                DrawIconChip(new Rect(x, y, chipSize, chipSize), tex, badge, title, body);
                x += chipSize + chipGap;
            }
        }

        // 3) 취약 — 공룡 TENDERIZE 패시브나 스펠로 부여.
        if (e.vulnerableTurns > 0 && x + chipSize <= rowRect.xMax)
        {
            DrawIconChip(new Rect(x, y, chipSize, chipSize),
                         HeadIcon("VULNERABLE"), e.vulnerableTurns,
                         "취약 (Vulnerable)",
                         $"받는 피해 50% 증가. {e.vulnerableTurns}턴 남음.");
            x += chipSize + chipGap;
        }

        // 4) 독 스택 — TOXIC_SLASH 등 공룡 패시브가 부여.
        if (e.poisonStacks > 0 && x + chipSize <= rowRect.xMax)
        {
            DrawIconChip(new Rect(x, y, chipSize, chipSize),
                         HeadIcon("POISON"), e.poisonStacks,
                         "독 (Poison)",
                         $"턴 종료마다 {e.poisonStacks} 피해. 매 턴 1씩 감소.");
            x += chipSize + chipGap;
        }

        // 5) 출혈 스택 — LACERATE / TOXIC_SLASH 패시브가 부여.
        if (e.bleedStacks > 0 && x + chipSize <= rowRect.xMax)
        {
            DrawIconChip(new Rect(x, y, chipSize, chipSize),
                         HeadIcon("FEAR"), e.bleedStacks,
                         "출혈 (Bleed)",
                         $"턴 종료마다 {e.bleedStacks} 피해. 매 턴 1씩 감소.");
            x += chipSize + chipGap;
        }

        // 6) 약화 — INTIMIDATE 패시브나 스펠로 부여.
        if (e.weakTurns > 0 && x + chipSize <= rowRect.xMax)
        {
            DrawIconChip(new Rect(x, y, chipSize, chipSize),
                         HeadIcon("WEAK"), e.weakTurns,
                         "약화 (Weak)",
                         $"가하는 피해 25% 감소. {e.weakTurns}턴 남음.");
            x += chipSize + chipGap;
        }

        // 7) 속박은 인텐트 자리(머리 위)에 BIND 아이콘으로 표시되므로 여기서는 다시 그리지 않음.
    }

    /// <summary>플레이어의 디버프/버프를 HP 바 아래 아이콘 칩으로 그림.</summary>
    private void DrawPlayerStatusChips(Rect rowRect, Player p)
    {
        if (p == null) return;
        EnsurePassiveStyles();

        const float chipSize = 26.4f;
        const float chipGap  = 4f;
        float x = rowRect.x;
        float y = rowRect.y + (rowRect.height - chipSize) * 0.5f;

        if (p.poisonStacks > 0 && x + chipSize <= rowRect.xMax)
        {
            DrawIconChip(new Rect(x, y, chipSize, chipSize),
                         HeadIcon("POISON"), p.poisonStacks,
                         "독 (Poison)",
                         $"턴 종료마다 {p.poisonStacks} 피해. 매 턴 1씩 감소.");
            x += chipSize + chipGap;
        }
        if (p.weakTurns > 0 && x + chipSize <= rowRect.xMax)
        {
            DrawIconChip(new Rect(x, y, chipSize, chipSize),
                         HeadIcon("WEAK"), p.weakTurns,
                         "약화 (Weak)",
                         $"가하는 피해 25% 감소. {p.weakTurns}턴 남음.");
            x += chipSize + chipGap;
        }
        if (p.vulnerableTurns > 0 && x + chipSize <= rowRect.xMax)
        {
            DrawIconChip(new Rect(x, y, chipSize, chipSize),
                         HeadIcon("VULNERABLE"), p.vulnerableTurns,
                         "취약 (Vulnerable)",
                         $"받는 피해 50% 증가. {p.vulnerableTurns}턴 남음.");
            x += chipSize + chipGap;
        }
        if (p.summonCostReduction > 0 && x + chipSize <= rowRect.xMax)
        {
            DrawIconChip(new Rect(x, y, chipSize, chipSize),
                         HeadIcon("BUFF"), p.summonCostReduction,
                         "총출동",
                         $"이번 턴 소환 카드 코스트 -{p.summonCostReduction}.");
            x += chipSize + chipGap;
        }
    }

    /// <summary>아군 공룡(소환수)의 도발/침묵/일시 공격 강화를 아이콘 칩으로 그림.</summary>
    private void DrawSummonStatusChips(Rect rowRect, SummonInstance s)
    {
        if (s == null) return;
        EnsurePassiveStyles();

        const float chipSize = 26.4f;
        const float chipGap  = 3f;

        // 패시브는 소멸(passiveConsumed)하지 않은 경우에만 표시
        bool showPassive = s.data?.passiveType != DinoPassiveType.NONE
                        && !s.passiveConsumed
                        && _passiveIcons.ContainsKey(s.data.passiveType);

        int count = (showPassive ? 1 : 0)
                  + (s.tauntTurns > 0 ? 1 : 0)
                  + (s.silencedTurns > 0 ? 1 : 0)
                  + (s.tempAttackBonus > 0 ? 1 : 0)
                  + (s.fuseHpBonusTurns > 0 ? 1 : 0)
                  + (s.fuseBlockRefreshTurns > 0 ? 1 : 0)
                  + (s.regenTurns > 0 ? 1 : 0);
        if (count == 0) return;

        // HP바 왼쪽에서 오른쪽으로 쌓임
        float x = rowRect.x;
        float y = rowRect.y + (rowRect.height - chipSize) * 0.5f;

        // 패시브 — 항상 맨 앞 (영구 특성이므로 숫자 뱃지 없음)
        if (showPassive)
        {
            (string ptitle, string pbody) = GetDinoPassiveTooltip(s.data);
            DrawIconChip(new Rect(x, y, chipSize, chipSize),
                         _passiveIcons[s.data.passiveType], 0,
                         ptitle, pbody);
            x += chipSize + chipGap;
        }

        if (s.tauntTurns > 0)
        {
            DrawIconChip(new Rect(x, y, chipSize, chipSize),
                         HeadIcon("TARGET_DINO"), s.tauntTurns,
                         "도발",
                         $"적 공격을 이 공룡이 받습니다. {s.tauntTurns}턴 남음.");
            x += chipSize + chipGap;
        }
        if (s.silencedTurns > 0)
        {
            DrawIconChip(new Rect(x, y, chipSize, chipSize),
                         HeadIcon("BIND"), s.silencedTurns,
                         "침묵",
                         $"이 공룡은 행동 불가. {s.silencedTurns}턴 남음.");
            x += chipSize + chipGap;
        }
        if (s.tempAttackBonus > 0)
        {
            // 격노의 각인이 활성이면 다음 턴 재충전 예정임을 안내.
            string atkBody = s.fuseAtkRefreshTurns > 0
                ? $"이번 턴 ATK +{s.tempAttackBonus}. 격노의 각인으로 다음 턴에도 +{s.fuseAtkRefreshValue} 유지."
                : $"이번 턴 ATK +{s.tempAttackBonus}.";
            DrawIconChip(new Rect(x, y, chipSize, chipSize),
                         HeadIcon("STRENGTH"), s.tempAttackBonus,
                         "공격 강화", atkBody);
            x += chipSize + chipGap;
        }
        if (s.fuseHpBonusTurns > 0)
        {
            DrawIconChip(new Rect(x, y, chipSize, chipSize),
                         HeadIcon("HEAL"), s.fuseHpBonusValue,
                         "생명의 각인",
                         $"최대 HP +{s.fuseHpBonusValue}. {s.fuseHpBonusTurns}턴 후 환원.");
            x += chipSize + chipGap;
        }
        if (s.fuseBlockRefreshTurns > 0)
        {
            DrawIconChip(new Rect(x, y, chipSize, chipSize),
                         HeadIcon("DEFEND"), s.fuseBlockRefreshValue,
                         "가호의 각인",
                         $"턴 시작 시 방어 +{s.fuseBlockRefreshValue} 충전. {s.fuseBlockRefreshTurns}턴 더 적용.");
            x += chipSize + chipGap;
        }
        if (s.regenTurns > 0)
        {
            DrawIconChip(new Rect(x, y, chipSize, chipSize),
                         HeadIcon("HEAL"), s.regenTurns,
                         "재생",
                         $"매 턴 시작 시 +{s.regenPerTurn} HP. {s.regenTurns}턴 남음.");
            x += chipSize + chipGap;
        }
    }

    /// <summary>호버 중인 패시브 툴팁 — 마우스 근처에 둥근 패널로. ShopUI 툴팁과 같은 톤.</summary>
    private void DrawPassiveTooltip()
    {
        if (string.IsNullOrEmpty(_hoveredPassiveTitle)) return;
        EnsurePassiveStyles();

        const float tw = 260f;
        string body = _hoveredPassiveBody ?? "";
        var titleSize = _tooltipTitleStyle.CalcSize(new GUIContent(_hoveredPassiveTitle));
        float bodyH = string.IsNullOrEmpty(body) ? 0f : _tooltipBodyStyle.CalcHeight(new GUIContent(body), tw - 24f);
        float th = 10f + titleSize.y + 6f + bodyH + 10f;

        var mouse = Event.current.mousePosition;
        float tx = mouse.x + 18f;
        float ty = mouse.y + 18f;
        if (tx + tw > RefW) tx = mouse.x - tw - 12f;
        if (ty + th > RefH) ty = RefH - th - 6f;

        var outer = new Rect(tx, ty, tw, th);
        FillRect(outer, new Color(1f, 0.8f, 0.4f, 1f));
        var inner = new Rect(outer.x + 1, outer.y + 1, outer.width - 2, outer.height - 2);
        FillRect(inner, new Color(0.08f, 0.05f, 0.08f, 0.96f));

        var titleRect = new Rect(tx + 12f, ty + 8f, tw - 24f, titleSize.y);
        GUI.Label(titleRect, _hoveredPassiveTitle, _tooltipTitleStyle);

        if (!string.IsNullOrEmpty(body))
        {
            var bodyRect = new Rect(tx + 12f, titleRect.yMax + 4f, tw - 24f, bodyH);
            GUI.Label(bodyRect, body, _tooltipBodyStyle);
        }
    }

    private static (string title, string body) GetDinoPassiveTooltip(CardData d)
    {
        int v = d.passiveValue;
        return d.passiveType switch
        {
            DinoPassiveType.LACERATE      => ("열상 (Lacerate)",
                $"공격마다 적에게 출혈 +{v} 스택.\n출혈은 매 턴 종료 시 스택만큼 피해 후 1 감소."),
            DinoPassiveType.TENDERIZE     => ("연육 (Tenderize)",
                $"공격 명중 후 적에게 취약 +{v}턴 부여.\n취약 상태의 적은 받는 피해 +50% (이번 공격에는 적용되지 않음)."),
            DinoPassiveType.APEX_PRESENCE => ("정점의 위압 (Apex Presence)",
                $"매 턴 시작, 모든 적에게 약화 {v}턴 부여.\n약화 상태의 적은 가하는 피해 -25%."),
            DinoPassiveType.SCOUT         => ("정찰 (Scout)",
                $"매 턴 시작, 카드 +{v}장 추가 드로우."),
            DinoPassiveType.BLOOD_FRENZY  => ("피의 광란 (Blood Frenzy)",
                $"자신 HP가 50% 이하일 때 ATK +{v} (자동 발동)."),
            DinoPassiveType.CANNIBAL      => ("동족포식 (Cannibal)",
                "필드의 다른 아군 공룡 1마리를 잡아먹는다 (1턴 1회, 코스트 0).\n제물의 현재 ATK·HP를 흡수해 자신의 공격력·최대 HP가 영구 상승.\n발동: 마준가 머리 위 송곳니(牙) 뱃지 클릭 → 잡아먹을 아군 클릭."),
            DinoPassiveType.REAPER        => ("수확 (Reaper)",
                $"공격마다 자신 방어도 +{v}."),
            DinoPassiveType.COUNTER       => ("반격 (Counter)",
                $"공격받을 때마다 공격자에게 {v} 반격 피해."),
            DinoPassiveType.TOXIC_SLASH   => ("독성 베기 (Toxic Slash)",
                $"공격마다 적에게 출혈 +{v} 및 독 +{v}.\n출혈·독 모두 매 턴 종료 시 스택만큼 피해 후 1 감소."),
            DinoPassiveType.SWIFT_DODGE   => ("기민한 회피 (Swift Dodge)",
                $"매 턴 시작, 방어도 +{v} 충전."),
            DinoPassiveType.ENRAGE        => ("분노 (Enrage)",
                $"피격마다 ATK 영구 +{v} (누적 무제한)."),
            DinoPassiveType.AMBUSH        => ("매복 기습 (Ambush)",
                "소환 후 첫 공격만 2배 데미지.\n발동 후 소멸 (1회 한정)."),
            DinoPassiveType.RAMPAGE       => ("난동 (Rampage)",
                "적 처치 시 즉시 다음 적에게 추가 공격 1회."),
            DinoPassiveType.INTIMIDATE    => ("위협 (Intimidate)",
                $"공격마다 목표 적 ATK 영구 -{v}.\n적 ATK는 0 이하로 내려가지 않음."),
            DinoPassiveType.EXECUTE       => ("처형 (Execute)",
                $"목표 적 HP가 {v} 이하이면 즉시 처치."),
            DinoPassiveType.BULWARK       => ("방벽 (Bulwark)",
                $"매 턴 시작, 모든 아군 공룡에게 방어 +{v}."),
            DinoPassiveType.OSTEODERM     => ("등판 갑주 (Osteoderm)",
                $"공격받을 때마다 자신 방어 +{v}."),
            DinoPassiveType.IRON_HIDE     => ("강철 가죽 (Iron Hide)",
                $"받는 모든 피해 -{v} (최소 1)."),
            DinoPassiveType.HERD_RALLY    => ("무리 호령 (Herd Rally)",
                $"매 턴 시작, 모든 아군 공룡 ATK +{v} (1턴)."),
            _                             => (d.nameKr, ""),
        };
    }

    private void EnsurePassiveStyles()
    {
        if (_passiveChipStyle == null)
        {
            _passiveChipStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(1f, 0.88f, 0.6f) },
            };
        }
        if (_tooltipTitleStyle == null)
        {
            _tooltipTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                normal = { textColor = new Color(1f, 0.88f, 0.5f) },
            };
        }
        if (_tooltipBodyStyle == null)
        {
            _tooltipBodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                wordWrap = true,
                normal = { textColor = new Color(0.9f, 0.9f, 0.9f) },
            };
        }
    }

    // 짧은 토스트 메시지 — 우클릭 취소 등에 사용. 화면 하단에서 1.5초간 페이드 표시.
    private string _toastText;
    private float _toastExpireTime;
    private void ShowToast(string text, float duration = 1.5f)
    {
        _toastText = text;
        _toastExpireTime = Time.time + duration;
    }
    private void DrawToast()
    {
        if (string.IsNullOrEmpty(_toastText) || Time.time >= _toastExpireTime) return;
        float remaining = _toastExpireTime - Time.time;
        float alpha = Mathf.Clamp01(remaining / 0.4f);
        var prev = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.Label(new Rect(0, 480, RefW, 30), _toastText, _targetHintStyle);
        GUI.color = prev;
    }

    // 손패 자동 숨김 — 공룡 공격 타겟팅 중에는 카드를 아래로 내려서 필드를 가림 없이 보이게.
    // 사용자 수동 토글(_handHidden)과 OR로 합쳐 효과를 결정.
    private bool EffectiveHandHidden => _handHidden || _targetingSummonIndex >= 0 || _targetingSummonSkillIndex >= 0;

    private void DrawSummonAttackHint(BattleState state)
    {
        if (_targetingSummonIndex < 0 || _targetingSummonIndex >= state.field.Count) return;
        var s = state.field[_targetingSummonIndex];
        string text = $"▶ {s.data.name} 공격 — 적을 클릭하세요  (우클릭: 취소)";
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 2.2f);
        float alpha = Mathf.Lerp(0.35f, 0.95f, pulse);
        var prev = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.Label(new Rect(0, 115, RefW, 30), text, _targetHintStyle);
        GUI.color = prev;
    }

    private void DrawSummonSkillHint(BattleState state)
    {
        if (_targetingSummonSkillIndex < 0 || _targetingSummonSkillIndex >= state.field.Count) return;
        var s = state.field[_targetingSummonSkillIndex];
        var skill = DianoCard.Data.DataManager.Instance.GetSkill(s.data.id);
        if (skill == null) return;
        string text = $"✦ {s.data.name} {skill.name} — 적을 클릭하세요  (우클릭: 취소)";
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 2.2f);
        float alpha = Mathf.Lerp(0.35f, 0.95f, pulse);
        var prev = GUI.color;
        GUI.color = new Color(0.85f, 1f, 0.95f, alpha);
        GUI.Label(new Rect(0, 115, RefW, 30), text, _targetHintStyle);
        GUI.color = prev;
    }

    private void DrawCannibalFeedHint(BattleState state)
    {
        if (_cannibalFeedFromIndex < 0 || _cannibalFeedFromIndex >= state.field.Count) return;
        var s = state.field[_cannibalFeedFromIndex];
        string text = $"🩸 {s.data.name} 동족포식 — 잡아먹을 아군 공룡을 클릭  (우클릭: 취소)";
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 2.2f);
        float alpha = Mathf.Lerp(0.35f, 0.95f, pulse);
        var prev = GUI.color;
        GUI.color = new Color(1f, 0.55f, 0.45f, alpha);
        GUI.Label(new Rect(0, 115, RefW, 30), text, _targetHintStyle);
        GUI.color = prev;
    }

    private void DrawTargetingHint(BattleState state)
    {
        if (_targetingCardIndex < 0 || _targetingCardIndex >= state.hand.Count) return;
        var c = state.hand[_targetingCardIndex].data;
        string text;
        bool isFusion = CardNeedsFusionTargets(c);
        if (isFusion)
        {
            text = _fusionMaterialAPicked
                ? $"▶ {c.name} — 두 번째 재료(같은 종·같은 티어)를 클릭  (우클릭: 취소)"
                : $"▶ {c.name} — 융합할 육식공룡 두 마리 중 첫 재료를 클릭 (필드/손)  (우클릭: 취소)";
        }
        else if (CardNeedsAllyTarget(c))
        {
            text = $"▶ {c.name} 사용 중 — 아군 공룡을 클릭하세요  (우클릭: 취소)";
        }
        else
        {
            text = $"▶ {c.name} 사용 중 — 적을 클릭하세요  (우클릭: 취소)";
        }

        // 살짝 보였다 사라졌다 하는 알파 펄스 (sin 0~1 → 0.35~0.95)
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 2.2f);
        float alpha = Mathf.Lerp(0.35f, 0.95f, pulse);

        var prevColor = GUI.color;
        // 융합 힌트는 글씨만 살짝 보라로 틴팅해 적/아군 타겟팅 힌트와 구분 — 패널/테두리 없이 글씨로만.
        if (isFusion)
        {
            GUI.color = new Color(0.93f, 0.78f, 1f, alpha);
        }
        else
        {
            GUI.color = new Color(1f, 1f, 1f, alpha);
        }
        GUI.Label(new Rect(0, 115, RefW, 30), text, _targetHintStyle);
        GUI.color = prevColor;
    }

    private void ApplyRewardDimming()
    {
        if (_rewardDimmed) return;
        EnsureRewardDimOverlay();
        if (_rewardDimOverlay != null) _rewardDimOverlay.enabled = true;
        // Reward 진입 시점의 공격 애니메이션 lunge를 리셋 — 안 그러면 공룡이 앞으로 튀어나온 채 얼어붙음
        _attackingUnit = null;
        _attackProgress = 0f;
        _rewardDimmed = true;
    }

    private void RestoreRewardDimming()
    {
        if (_rewardDimOverlay != null) _rewardDimOverlay.enabled = false;
        _rewardDimmed = false;
    }

    private void EnsureRewardDimOverlay()
    {
        var cam = Camera.main;
        if (cam == null) return;

        if (_rewardDimOverlay == null)
        {
            var go = new GameObject("_RewardDimOverlay");
            _rewardDimOverlay = go.AddComponent<SpriteRenderer>();

            // 1×1 흰 텍스처로 스프라이트 생성
            var tex = new Texture2D(1, 1, TextureFormat.ARGB32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            // PPU=1 로 해서 1×1 스프라이트의 월드 크기 = 1 unit → localScale로 직접 제어 가능
            _rewardDimOverlay.sprite = Sprite.Create(
                tex,
                new Rect(0, 0, 1, 1),
                new Vector2(0.5f, 0.5f),
                1f);
            _rewardDimOverlay.color = RewardOverlayColor;
            // 어떤 SpriteRenderer보다도 앞에 오도록 큰 sorting order (배경·캐릭터·적 전부 뒤로)
            _rewardDimOverlay.sortingOrder = 9999;
        }

        // 매번 카메라 영역을 덮도록 위치/스케일 갱신
        if (cam.orthographic)
        {
            float camH = cam.orthographicSize * 2f;
            float camW = camH * cam.aspect;
            _rewardDimOverlay.transform.localScale = new Vector3(camW, camH, 1f);
        }
        var camPos = cam.transform.position;
        _rewardDimOverlay.transform.position = new Vector3(camPos.x, camPos.y, 0f);
    }

    private void DrawBackground()
    {
        // World-space SpriteRenderer로 그리므로 OnGUI 경로는 비워둔다.
        // world 경로가 실패해서 텍스처만 있고 sr이 없을 때만 OnGUI 폴백.
        if (_worldBgSr != null || _backgroundTexture == null) return;
        GUI.DrawTexture(
            new Rect(0, 0, Screen.width, Screen.height),
            _backgroundTexture,
            ScaleMode.ScaleAndCrop,
            alphaBlend: true);
    }

    private void UpdateWorldBackground()
    {
        if (_backgroundTexture == null) return;
        var cam = Camera.main;
        if (cam == null) return;

        if (_worldBgSr == null)
        {
            var go = new GameObject("_BattleBackground");
            _worldBgSr = go.AddComponent<SpriteRenderer>();
            _worldBgSr.sortingOrder = -100;
        }

        var tex = _backgroundTexture;
        _worldBgSr.sprite = Sprite.Create(
            tex,
            new Rect(0, 0, tex.width, tex.height),
            new Vector2(0.5f, 0.5f),
            100f);

        if (cam.orthographic)
        {
            float camH = cam.orthographicSize * 2f;
            float camW = camH * cam.aspect;
            float spriteW = tex.width / 100f;
            float spriteH = tex.height / 100f;
            float s = Mathf.Max(camW / spriteW, camH / spriteH);
            _worldBgSr.transform.localScale = new Vector3(s, s, 1f);
        }

        var camPos = cam.transform.position;
        _worldBgSr.transform.position = new Vector3(camPos.x, camPos.y, 0f);
        _worldBgSr.enabled = true;
    }

    private void DestroyWorldBackground()
    {
        if (_worldBgSr != null)
        {
            Destroy(_worldBgSr.gameObject);
            _worldBgSr = null;
        }
    }

    private void EnsureStyles()
    {
        if (_stylesReady) return;
        _boxStyle = new GUIStyle(GUI.skin.box)
        {
            fontSize = 14,
            alignment = TextAnchor.UpperLeft,
            padding = new RectOffset(10, 10, 8, 8),
            wordWrap = true,
        };
        _buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 13,
            alignment = TextAnchor.UpperLeft,
            padding = new RectOffset(8, 8, 8, 8),
            wordWrap = true,
        };
        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 19,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white },
        };
        _centerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white },
        };
        _intentStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.9f, 0.5f) },
        };
        _intentNumberStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 10,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            wordWrap = false,
            clipping = TextClipping.Overflow,
            normal = { textColor = Color.white },
        };
        // 카드 텍스트용 폰트 — 다크판타지 톤. 제목은 Cinzel(영문 세리프).
        // 본문은 Hahmlet 명조 — IMFellEnglish는 한글 글리프가 없어 OS 폴백이 일어나고
        // 숫자만 옛날체로 남아 한글과 톤이 어긋났다. 한글/숫자 폰트를 한 벌로 통일.
        var fontTitle = Resources.Load<Font>("Fonts/Cinzel-VariableFont_wght");
        var fontBody  = Resources.Load<Font>("Fonts/Hahmlet-VariableFont_wght");
        // 데미지/회복 부동 라벨 — Cinzel(라피더리 세리프)로 무게감 강조. fontSize는 매 프레임 동적으로 덮어쓴다.
        _damageStyle = new GUIStyle(GUI.skin.label)
        {
            font = fontTitle,
            fontSize = 36,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white },
            wordWrap = false,
            clipping = TextClipping.Overflow,
        };
        _targetHintStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 20,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Normal,
            normal = { textColor = new Color(1f, 0.96f, 0.85f) },
        };
        _cardCostStyle = new GUIStyle(GUI.skin.label)
        {
            font = fontTitle,
            fontSize = 22,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.95f, 0.6f) },
        };
        _cardNameStyle = new GUIStyle(GUI.skin.label)
        {
            font = fontTitle,
            fontSize = 11,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            wordWrap = false, // 두 줄로 깨지지 않도록 강제 — 폭 초과 시 코드에서 폰트 축소.
            clipping = TextClipping.Overflow,
            normal = { textColor = new Color(1f, 0.92f, 0.75f) },
        };
        _cardDescStyle = new GUIStyle(GUI.skin.label)
        {
            font = fontBody,
            fontSize = 11,
            alignment = TextAnchor.UpperCenter,
            fontStyle = FontStyle.Bold,
            wordWrap = true,
            padding = new RectOffset(6, 6, 4, 4),
            normal = { textColor = Color.black }, // 명판 베이지 위 최대 가독성 — 외곽선으로 살짝 굵기 보강.
        };
        // GUI.skin.label 기본값은 hover 시 색이 바뀌는 상태가 있어 모든 라벨 스타일의
        // normal 색을 모든 state로 복사해서 호버/액티브/포커스 시 색 변화를 막는다.
        LockStateColors(_boxStyle);
        LockStateColors(_buttonStyle);
        LockStateColors(_labelStyle);
        LockStateColors(_centerStyle);
        LockStateColors(_damageStyle);
        LockStateColors(_intentStyle);
        LockStateColors(_intentNumberStyle);
        LockStateColors(_targetHintStyle);
        LockStateColors(_cardCostStyle);
        LockStateColors(_cardNameStyle);
        LockStateColors(_cardDescStyle);

        _stylesReady = true;
    }

    // GUIStyle의 모든 인터랙션 state의 텍스트 색·배경을 normal과 동일하게 고정.
    // GUI.skin.button 베이스 스타일은 hover/active에서 다른 background로 swap되며
    // 텍스트가 미세하게 시프트되는 느낌을 주므로 background도 함께 락한다.
    private static void LockStateColors(GUIStyle s)
    {
        if (s == null) return;
        var c = s.normal.textColor;
        var bg = s.normal.background;
        s.hover.textColor    = c; s.hover.background    = bg;
        s.active.textColor   = c; s.active.background   = bg;
        s.focused.textColor  = c; s.focused.background  = bg;
        s.onNormal.textColor = c; s.onNormal.background = bg;
        s.onHover.textColor  = c; s.onHover.background  = bg;
        s.onActive.textColor = c; s.onActive.background = bg;
        s.onFocused.textColor= c; s.onFocused.background= bg;
    }

    private static bool CardNeedsTarget(CardData c)
    {
        return CardNeedsEnemyTarget(c) || CardNeedsAllyTarget(c) || CardNeedsFusionTargets(c);
    }

    private static bool CardNeedsEnemyTarget(CardData c)
    {
        if (c.cardType != CardType.MAGIC) return false;
        if (c.target != TargetType.ENEMY) return false;
        return c.subType == CardSubType.ATTACK
            || c.subType == CardSubType.DEBUFF;
    }

    // ALLY 단일 타겟 카드 — 수호 마법(DEFENSE) 또는 도발(TAUNT) + ALLY.
    // 융합(UTILITY/FUSION)은 2개 재료 지정이 필요해 별도 흐름으로 처리 (CardNeedsFusionTargets).
    private static bool CardNeedsAllyTarget(CardData c)
    {
        if (c.target != TargetType.ALLY) return false;
        if (c.cardType != CardType.MAGIC && c.cardType != CardType.BUFF) return false;
        return c.subType == CardSubType.DEFENSE || c.subType == CardSubType.TAUNT;
    }

    // 융합 카드 — 재료 2개(필드/손 자유 조합) 지정 필요.
    private static bool CardNeedsFusionTargets(CardData c)
    {
        return c.cardType == CardType.UTILITY && c.subType == CardSubType.FUSION;
    }

    // 증원 카드 — 보유 덱(run.deck)의 T0 SUMMON 그리드 모달이 필요.
    private static bool CardNeedsReinforcePicker(CardData c)
    {
        return c.cardType == CardType.UTILITY && c.subType == CardSubType.REINFORCE;
    }

    /// <summary>주어진 후보(필드 SummonInstance 또는 손패 인덱스)가 현재 융합 흐름에서 재료로 선택 가능한지 판정.
    /// 첫 재료 단계면 "육식 SUMMON + 티어 &lt; 2"만 체크하고, 두 번째 단계면 A와 종/티어가 일치하는지까지 검증한다.</summary>
    private bool IsFusionMaterialEligible(DianoCard.Battle.SummonInstance s, int index, bool isHand)
    {
        if (_targetingCardIndex < 0) return false;
        var state = _battle?.state;
        if (state == null) return false;

        CardData candidateData;
        string candidateBaseId;
        int candidateTier;
        if (isHand)
        {
            if (index < 0 || index >= state.hand.Count) return false;
            if (index == _targetingCardIndex) return false; // 촉매 자기 자신 제외
            candidateData = state.hand[index].data;
            candidateBaseId = candidateData.id;
            candidateTier = 0; // 손 카드는 항상 T0 (T1/T2 결과체는 덱/보상 풀에서 제외됨)
        }
        else
        {
            if (s == null || s.IsDead) return false;
            candidateData = s.data;
            candidateBaseId = s.originCardId;
            candidateTier = GetCarnivoreTierFromCardId(s.data.id);
        }

        if (candidateData.cardType != CardType.SUMMON) return false;
        if (candidateData.subType != CardSubType.CARNIVORE) return false;
        if (candidateTier >= 2) return false; // T2는 더 이상 진화 불가

        if (!_fusionMaterialAPicked) return true;

        // 두 번째 재료 — A와 종/티어 일치해야 함
        if (_fusionMaterialA.isHand == isHand && _fusionMaterialA.index == index) return false;

        string aBaseId;
        int aTier;
        if (_fusionMaterialA.isHand)
        {
            if (_fusionMaterialA.index < 0 || _fusionMaterialA.index >= state.hand.Count) return false;
            aBaseId = state.hand[_fusionMaterialA.index].data.id;
            aTier = 0;
        }
        else
        {
            if (_fusionMaterialA.index < 0 || _fusionMaterialA.index >= state.field.Count) return false;
            var aInst = state.field[_fusionMaterialA.index];
            aBaseId = aInst.originCardId;
            aTier = GetCarnivoreTierFromCardId(aInst.data.id);
        }

        if (candidateBaseId != aBaseId) return false;
        // 같은 티어 또는 T1+T0 교차 허용
        int tMax = Math.Max(candidateTier, aTier);
        int tMin = Math.Min(candidateTier, aTier);
        return candidateTier == aTier || (tMax == 1 && tMin == 0);
    }

    private void HandleFusionMaterialClick(DianoCard.Battle.FusionMaterial m)
    {
        if (!_fusionMaterialAPicked)
        {
            _fusionMaterialA = m;
            _fusionMaterialAPicked = true;
        }
        else
        {
            int catalystIdx = _targetingCardIndex;
            var targets = new DianoCard.Battle.FusionTargets { a = _fusionMaterialA, b = m };
            _targetingCardIndex = -1;
            _fusionMaterialAPicked = false;
            _pending.Add(() => { _battle.PlayCard(catalystIdx, -1, -1, -1, targets); });
        }
    }

    private static int GetCarnivoreTierFromCardId(string cardId)
    {
        if (string.IsNullOrEmpty(cardId)) return 0;
        if (cardId.EndsWith("_T2")) return 2;
        if (cardId.EndsWith("_T1")) return 1;
        return 0;
    }

    // 플레이어가 공격 모션(채찍 lunge)을 취해야 하는 카드인지 여부.
    // 단일 적(ENEMY) / 광역(ALL_ENEMY) 공격 주문 모두 포함.
    // C135(부식의 선풍)은 DEBUFF지만 피해(value=3)도 주므로 lunge 포함.
    private static bool IsAttackSpell(CardData c)
    {
        if (c.cardType != CardType.MAGIC) return false;
        if (c.subType == CardSubType.ATTACK) return true;
        if (c.subType == CardSubType.DEBUFF && c.value > 0) return true;
        return false;
    }

    // =========================================================
    // Battle field rendering
    // =========================================================

    // 지면 라인 — 플레이어 캐릭터 발끝이 닿는 GUI Y. 카드 상단(≈567) 약간 위로 잡아 HP 바 겹침 방지.
    // 공룡 발끝 위치는 모두 이 라인 기준으로 계산 (사람 기준).
    private const float GroundY = 470f;

    private void ComputeSlotPositions(BattleState state)
    {
        // DrawPlayerNPC의 h(=257)와 일치 — h/2여야 발끝이 GroundY에 정확히 닿음.
        const float PlayerHalfH = 128f;

        _slotPositions.Clear();
        _mossDepthScale.Clear();
        _slotPositions[state.player] = new Vector2(playerX, GroundY - PlayerHalfH - 10);

        int fieldCount = state.field.Count;
        CardData front = fieldCount > 0 ? state.field[0].data : null;
        CardData back = fieldCount > 1 ? state.field[1].data : null;
        for (int i = 0; i < fieldCount; i++)
            _slotPositions[state.field[i]] = ComputeFieldSlot(i, fieldCount, front, back);
        UpdateSummonDisplayPositions(state);

        // 1) 본체 적들(이끼 잡몹 제외)을 기존 일렬 레이아웃으로 배치.
        //    이끼는 보스 4코너에 따로 둬야 하므로 별도 처리.
        int aliveIdx = 0;
        EnemyInstance bossRef = null;
        var mossAlive = new List<EnemyInstance>();
        foreach (var e in state.enemies)
        {
            // 사망 모션 진행 중인 적은 슬롯을 그대로 점유 — 다른 적이 그 자리로 슬라이드해 즉시 채우지 않게.
            // view가 destroy(모션 종료)된 다음 OnGUI부터 슬롯에서 빠져 살아있는 적들이 슬라이드.
            if (e.IsDead)
            {
                bool stillAnimating = _enemyViews.TryGetValue(e, out var deathView)
                                      && deathView != null && deathView.gameObject != null;
                if (!stillAnimating) continue;
            }
            if (e.isMoss) { mossAlive.Add(e); continue; }
            // 적 크기는 타입별로 다름 — 발끝이 GroundY에 닿도록 센터 Y를 h/2만큼 위로.
            // staggerY는 뒤쪽 적이 멀어 보이게 하되, 안개 지평선(40%)으로 밀려나지 않을 정도로만.
            float h = GetEnemyDrawHeight(e);
            // 보스는 검·갑옷 실루엣이 우측 끝을 벗어나지 않게 살짝 안쪽으로.
            float baseX = (e.data.enemyType == EnemyType.BOSS) ? 970f : 1070f;
            // 보스(E901)는 머리가 상단 HUD 바에 너무 붙어 보여 발끝 기준에서 50px 더 아래로.
            float yDrop = (e.data.enemyType == EnemyType.BOSS) ? 50f : 0f;
            _slotPositions[e] = new Vector2(baseX - aliveIdx * 160, GroundY - h / 2f - aliveIdx * 22 + yDrop);
            if (bossRef == null && e.data.enemyType == EnemyType.BOSS) bossRef = e;
            aliveIdx++;
        }

        // 2) 이끼 잡몹은 보스 주변 4코너(위-좌/위-우/아래-좌/아래-우)에 배치.
        //    보스가 없으면(이론상 안 일어남) 폴백으로 일렬.
        if (mossAlive.Count > 0 && bossRef != null && _slotPositions.TryGetValue(bossRef, out var bossPos))
        {
            float bossH = GetEnemyDrawHeight(bossRef);
            // 보스 실루엣 옆+위/아래로 적당히 떨어진 4개 슬롯. 좌측 슬롯은 보스 몸통 왼쪽으로 더 멀리 —
            // 보스 망토·검 폭이 크고, 또 도깨비불이 좌측 빈 공간에 더 잘 보임.
            // 코너별 원근 스케일: 위 한 쌍은 살짝 작게(멀리), 아래 한 쌍은 살짝 크게(가까이).
            Vector2[] corners =
            {
                new Vector2(-170f, -bossH * 0.30f),  // 0: 위-좌
                new Vector2(+170f, -bossH * 0.30f),  // 1: 위-우
                new Vector2(-170f, +bossH * 0.22f),  // 2: 아래-좌
                new Vector2(+170f, +bossH * 0.22f),  // 3: 아래-우
            };
            float[] cornerScale = { 0.85f, 0.85f, 1.05f, 1.05f }; // 위 작게, 아래 크게
            for (int i = 0; i < mossAlive.Count; i++)
            {
                int cornerIdx = i % 4;
                var m = mossAlive[i];
                _slotPositions[m] = bossPos + corners[cornerIdx];
                _mossDepthScale[m] = cornerScale[cornerIdx];

                // 코너별 전용 스프라이트. 누락 시 다른 코너로 폴백 (left_up 우선).
                Sprite target = cornerIdx switch
                {
                    0 => _mossWorldSpriteLeftUp,
                    1 => _mossWorldSpriteRightUp,
                    2 => _mossWorldSpriteLeftDown,
                    _ => _mossWorldSpriteRightDown,
                } ?? (_mossWorldSpriteLeftUp ?? _mossWorldSpriteRightUp ?? _mossWorldSpriteLeftDown ?? _mossWorldSpriteRightDown);
                if (target != null && _enemyViews.TryGetValue(m, out var mview))
                    mview.SetSprite(target);
            }
        }
        else
        {
            // 폴백: 보스 못 찾으면 기존 방식대로 좌측 일렬.
            foreach (var m in mossAlive)
            {
                float h = GetEnemyDrawHeight(m);
                _slotPositions[m] = new Vector2(1070f - aliveIdx * 160, GroundY - h / 2f - aliveIdx * 22);
                aliveIdx++;
            }
        }
    }

    // 스프라이트를 컨테이너 rect 내부에 "바닥 정렬"로 그리기 위한 draw rect 계산.
    // 가로로 긴 스프라이트는 rect 너비에 맞추되 발끝이 rect.yMax에 닿도록 위쪽 여백을 둠.
    // 세로로 긴 스프라이트는 높이에 맞추고 좌우 중앙 정렬.
    // 결과: 어떤 스프라이트든 발이 rect.yMax 라인에 닿아서 HP 바 위치가 일관됨.
    private static Rect ComputeBottomAnchoredDrawRect(Rect container, float texAspect)
    {
        if (texAspect <= 0f) return container;
        float rectAspect = container.width / container.height;
        if (texAspect >= rectAspect)
        {
            float drawH = container.width / texAspect;
            return new Rect(container.x, container.yMax - drawH, container.width, drawH);
        }
        else
        {
            float drawW = container.height * texAspect;
            return new Rect(container.x + (container.width - drawW) * 0.5f, container.y, drawW, container.height);
        }
    }

    // 적 타입별 드로잉 높이 — 엘리트/보스는 플레이어보다 크게.
    // enemy.csv의 field_scale 컬럼으로 종별 미세 조정 (비어있으면 1.0).
    // 이끼 잡몹은 코너별 원근 스케일도 추가 적용 (ComputeSlotPositions에서 _mossDepthScale에 기록).
    private float GetEnemyDrawHeight(EnemyInstance e)
    {
        // 이끼 쫄: 보호막 시각화이지 본체가 아니므로 보스를 가리지 않게 작게.
        if (e.isMoss)
        {
            float depth = _mossDepthScale.TryGetValue(e, out var d) ? d : 1f;
            return 95f * e.data.SafeFieldScale * depth;
        }
        // SUMMON 쫄(ADD_*): 부모 몬스터보다 한 단계 작게 — NORMAL의 ~60% (108f).
        if (e.isMinion)
        {
            return 108f * e.data.SafeFieldScale;
        }
        float baseH = e.data.enemyType switch
        {
            EnemyType.BOSS  => 380f,
            EnemyType.ELITE => 260f,
            _               => 180f,
        };
        return baseH * e.data.SafeFieldScale;
    }

    // 필드 소환수 슬롯 레이아웃. fieldScale은 CardData.SafeFieldScale (card.csv field_scale 컬럼).
    //   1마리: dinoSingleX/FootY 그대로.
    //   2마리: 앞 공룡(index 0)은 dinoTwoSlot0X/FootY 고정. 뒤 공룡(index 1)은 두 공룡의
    //          fieldScale을 반영해 자동 패킹 — pairOverlapPct만큼 가로 겹침,
    //          pairStaggerYPct만큼 발이 위로 올라가 원근감.
    // halfH에 카드별 fieldScale을 곱해야 DrawSummon에서 footY 복원 시 발이 지면선에 맞음.
    private Vector2 ComputeFieldSlot(int index, int total, CardData front, CardData back)
    {
        if (total <= 1)
        {
            float scale1 = front?.SafeFieldScale ?? 1f;
            float halfH1 = dinoSize * scale1 * 0.5f;
            return new Vector2(dinoSingleX, dinoSingleFootY - halfH1);
        }

        // 2마리 — 앞 공룡 위치는 고정.
        float frontScale = front?.SafeFieldScale ?? 1f;
        float frontHalfH = dinoSize * frontScale * 0.5f;
        float frontW = dinoSize * frontScale;

        if (index == 0)
            return new Vector2(dinoTwoSlot0X, dinoTwoSlot0FootY - frontHalfH);

        // 뒤 공룡 — 자동 패킹.
        float backScale = back?.SafeFieldScale ?? 1f;
        float backHalfH = dinoSize * backScale * 0.5f;
        float backW = dinoSize * backScale;
        float frontDrawnH = dinoSize * frontScale;
        float backDrawnH = dinoSize * backScale;

        // 가로 — 평균 폭 기반 + 사이즈 차 안전 마진.
        // 자연 spacing: 두 공룡 폭의 절반씩 더한 거리에서 overlapPct만큼 겹침.
        // 최소 spacing: 앞 공룡 너비의 minSpacingPct만큼 — 작은 뒤 공룡이 큰 앞 공룡 안에 빨려들지 않게.
        float naturalSpacing = (frontW * 0.5f + backW * 0.5f) * (1f - pairOverlapPct);
        float minSpacing = frontW * pairMinSpacingPct;
        float spacingX = Mathf.Max(naturalSpacing, minSpacing);

        // 세로 — 기본 staggerPct(절대 픽셀) + 사이즈 차 보너스.
        // 기본: dinoSize × staggerPct (앞 공룡 키와 무관 → 큰 공룡 페어도 안 뜸).
        // 보너스: 앞이 뒤보다 크면 (1 - backH/frontH) × boost만큼 추가로 위로 → 작은 뒤 공룡이 큰 앞 공룡 등 위로.
        float baseStagger = dinoSize * pairStaggerYPct;
        float sizeRatio = backDrawnH / Mathf.Max(0.01f, frontDrawnH);
        float bonusStagger = frontDrawnH * Mathf.Max(0f, 1f - sizeRatio) * pairSizeStaggerBoost;
        float staggerY = Mathf.Max(baseStagger, bonusStagger);
        float backFootY = dinoTwoSlot0FootY - staggerY;

        float backCenterX = dinoTwoSlot0X + spacingX;
        return new Vector2(backCenterX, backFootY - backHalfH);
    }

    // 슬롯 타겟 위치로 표시 위치를 프레임마다 lerp.
    // 처음 등장한 소환수는 즉시 타겟에 배치(등장 순간이동은 기존 유지), 이후 레이아웃 재계산 시에만 부드럽게 이동.
    private void UpdateSummonDisplayPositions(BattleState state)
    {
        // 사라진 소환수 정리
        if (_summonDisplayPositions.Count > 0)
        {
            List<SummonInstance> stale = null;
            foreach (var kv in _summonDisplayPositions)
            {
                if (!state.field.Contains(kv.Key))
                {
                    stale ??= new List<SummonInstance>();
                    stale.Add(kv.Key);
                }
            }
            if (stale != null)
                foreach (var k in stale) _summonDisplayPositions.Remove(k);
        }

        float t = 1f - Mathf.Exp(-SummonSlideSpeed * Time.deltaTime);
        foreach (var s in state.field)
        {
            if (!_slotPositions.TryGetValue(s, out var target)) continue;
            if (_summonDisplayPositions.TryGetValue(s, out var cur))
                _summonDisplayPositions[s] = Vector2.Lerp(cur, target, t);
            else
                _summonDisplayPositions[s] = target; // 신규 소환수는 즉시 배치
        }
    }

    private void DrawBattleField(BattleState state)
    {
        DrawPlayerNPC(state.player, _slotPositions[state.player]);

        // Y-sort: 뒤쪽(Y 작은) 공룡부터 먼저 그려서 앞쪽 공룡이 자연스럽게 가리게.
        // field index가 커질수록 스태거로 위(Y 작음)에 배치되므로 역순 순회.
        for (int i = state.field.Count - 1; i >= 0; i--)
        {
            var s = state.field[i];
            // IsDead 공룡은 IMGUI 그리기 스킵 — _dyingSummons에서 페이드 그림이 별도로 그려짐.
            if (s.IsDead) continue;
            if (_summonDisplayPositions.TryGetValue(s, out var pos)) DrawSummon(s, i, pos);
        }

        // 적 IMGUI 순회를 역순으로 — 작은 이끼 잡몹(높은 인덱스)이 보스(0)보다 먼저 클릭 검사를 받게 한다.
        // 보스 IMGUI rect(400×400)가 코너 이끼와 겹쳐 항상 보스가 클릭을 가로채고 ResolveCard에서 첫 이끼로 자동 리다이렉트되던 문제 해결.
        // 월드 스프라이트 렌더링은 SpriteRenderer 정렬과 무관하므로 IMGUI 순서가 시각에 영향 안 줌.
        for (int i = state.enemies.Count - 1; i >= 0; i--)
        {
            var e = state.enemies[i];
            if (e.IsDead) continue;
            if (_slotPositions.TryGetValue(e, out var pos)) DrawEnemy(e, i, pos);
        }
    }

    private void DrawPlayerNPC(Player p, Vector2 center)
    {
        // 캐릭터 스프라이트는 world-space BattleEntityView가 그림. IMGUI에서는 HP 바만 처리.
        const float h = 257;
        if (_playerSprite != null)
        {
            float texAspect = _playerSprite.width / (float)_playerSprite.height;
            float w = h * texAspect;
            var rect = new Rect(center.x - w / 2, center.y - h / 2, w, h);

            // PlayerView world 위치/크기 동기화 — IMGUI 좌표(발 위치)를 world로 변환
            if (_playerView != null && Camera.main != null)
            {
                Vector2 feetGui = new Vector2(center.x, rect.yMax);
                Vector3 feetWorld = GuiToWorld(feetGui);
                Vector3 topWorld  = GuiToWorld(new Vector2(center.x, rect.y));
                float worldHeight = Mathf.Abs(feetWorld.y - topWorld.y);

                // pivot 보정 — 스프라이트 pivot이 Center면 bounds.min.y가 음수라 발이 아래로 쏠린다.
                // 새 시퀀스 에셋들은 pivot=Center가 기본이라 이 보정 없으면 캐릭터가 지면 아래로 박힘.
                // pivot=Bottom인 스프라이트면 bounds.min.y≈0 → 영향 없음.
                Vector3 pivotOffset = Vector3.zero;
                var psr = _playerView.GetComponent<SpriteRenderer>();
                if (psr != null && psr.sprite != null && psr.sprite.bounds.size.y > 0.001f)
                {
                    float s = worldHeight / psr.sprite.bounds.size.y;
                    pivotOffset = new Vector3(0f, -psr.sprite.bounds.min.y * s, 0f);
                }
                _playerView.SetBasePosition(feetWorld + pivotOffset);
                _playerView.SetWorldHeight(worldHeight);
                Vector2 shadowOffset = new Vector2(_entityShadowOffsetX, _entityShadowOffsetY) * worldHeight;
                _playerView.UpdateShadowParams(_entityShadowHeight, _entityShadowWidthScale, shadowOffset, _entityShadowAlpha);
            }

            DrawPlayerShieldFx(new Vector2(center.x, rect.center.y), Mathf.Max(w, 160f), h);

            // HP 바 — 캐릭터 발 아래, 스프라이트 너비에 비례 (min/max 클램프)
            float playerBarW = ComputeHpBarWidth(w);
            var barRect = new Rect(center.x - playerBarW / 2, rect.yMax + 6, playerBarW, hpBarHeight);
            DrawHpBar(barRect, p.hp, p.maxHp, new Color(0.65f, 0.16f, 0.18f), p.block > 0, _playerBlockTintStartTime, entity: p);

            if (p.block > 0)
            {
                // 방패 뱃지를 HP 바 왼쪽 끝에 살짝 겹치게 — 머리 위 대신 인라인
                DrawBlockBadge(new Vector2(barRect.x, barRect.center.y), p.block, 40f);
            }

            // 상태 칩 — HP 바 바로 아래 한 줄 (적 패시브 줄과 동일 형식).
            DrawPlayerStatusChips(new Rect(barRect.x, barRect.yMax + 4f, barRect.width, 26f), p);
        }
        else
        {
            const float fbW = 140, fbH = 200;
            var rect = new Rect(center.x - fbW / 2, center.y - fbH / 2, fbW, fbH);

            FillRect(rect, new Color(0.25f, 0.45f, 0.8f, 0.88f));
            DrawBorder(rect, 2, new Color(0.15f, 0.3f, 0.6f, 1f));

            DrawPlayerShieldFx(new Vector2(rect.center.x, rect.center.y), fbW, fbH);

            float fbBarW = ComputeHpBarWidth(rect.width);
            var fbHpRect = new Rect(rect.center.x - fbBarW / 2, rect.y + rect.height - 50, fbBarW, hpBarHeight);
            DrawHpBar(fbHpRect, p.hp, p.maxHp, new Color(0.65f, 0.16f, 0.18f), p.block > 0, _playerBlockTintStartTime, entity: p);

            if (p.block > 0)
            {
                DrawBlockBadge(new Vector2(fbHpRect.x, fbHpRect.center.y), p.block, 40f);
            }
        }
    }

    // EnemyAction을 18개 머리 위 아이콘 풀의 ID로 매핑. 흡혈은 별도 모티프 없어 POISON 임시 사용.
    private static string IntentIconForAction(DianoCard.Data.EnemyAction action)
    {
        switch (action)
        {
            case DianoCard.Data.EnemyAction.ATTACK:           return "ATTACK";
            case DianoCard.Data.EnemyAction.MULTI_ATTACK:     return "MULTI_ATTACK";
            case DianoCard.Data.EnemyAction.DEFEND:           return "DEFEND";
            case DianoCard.Data.EnemyAction.BLOCK_BOSS:       return "DEFEND";
            case DianoCard.Data.EnemyAction.POISON:           return "POISON";
            case DianoCard.Data.EnemyAction.DRAIN:            return "ATTACK";
            case DianoCard.Data.EnemyAction.WEAK:             return "WEAK";
            case DianoCard.Data.EnemyAction.VULNERABLE:       return "VULNERABLE";
            case DianoCard.Data.EnemyAction.SILENCE:          return "BIND";
            case DianoCard.Data.EnemyAction.STEAL_SUMMON:     return "STOLEN";
            case DianoCard.Data.EnemyAction.CLOG_DECK:        return "DEBUFF";
            case DianoCard.Data.EnemyAction.SUMMON:           return "SUMMON";
            case DianoCard.Data.EnemyAction.REFILL_MOSS:      return "SUMMON";
            case DianoCard.Data.EnemyAction.BUFF_SELF:        return "STRENGTH";
            case DianoCard.Data.EnemyAction.EMPOWER_BOSS:     return "STRENGTH";
            case DianoCard.Data.EnemyAction.ARMOR_UP:         return "ROOTED";
            case DianoCard.Data.EnemyAction.HEAL_BOSS:        return "HEAL";
            case DianoCard.Data.EnemyAction.COUNTDOWN_ATTACK: return "COUNTDOWN";
            case DianoCard.Data.EnemyAction.COUNTDOWN_AOE:    return "COUNTDOWN";
            case DianoCard.Data.EnemyAction.IDLE:             return "UNKNOWN";
            default:                                          return "UNKNOWN";
        }
    }

    // 숫자가 의미 없는 액션 (소환·강탈·휴식 등)은 인텐트 아이콘에서 숫자 생략.
    private static bool IntentShowsNumber(DianoCard.Data.EnemyAction action)
    {
        switch (action)
        {
            case DianoCard.Data.EnemyAction.SUMMON:
            case DianoCard.Data.EnemyAction.REFILL_MOSS:
            case DianoCard.Data.EnemyAction.STEAL_SUMMON:
            case DianoCard.Data.EnemyAction.SILENCE:
            case DianoCard.Data.EnemyAction.IDLE:
            case DianoCard.Data.EnemyAction.UNKNOWN:
                return false;
            default:
                return true;
        }
    }

    // 인텐트가 데미지 액션이면 실제 BattleManager가 산출하는 피해량을 매 프레임 산출.
    // 라이브 인풋: IntentLiveValue(= intentBaseValue + 현재 extraAttack), damageScale, 적 weakTurns,
    //              플레이어 vulnerableTurns(공룡 타겟이면 미적용), 타겟 공룡의 IRON_HIDE 패시브.
    // ATTACK/MULTI/COUNTDOWN_ATTACK: DealAttack과 동일 순서 — scale → weak → IRON_HIDE → 취약.
    //   타겟은 ResolveLiveAttackTarget로 미러링 (도발/사망/이탈 반영). 공룡은 vulnerable 안 걸리므로
    //   취약 보너스는 타겟이 플레이어(null)일 때만 player.vulnerableTurns를 본다.
    // COUNTDOWN_AOE: 자체 산출 — scale → 플레이어 취약(공룡 측 IRON_HIDE는 단일 숫자로 표기 곤란해 생략).
    // 비-데미지 액션은 raw intentValue.
    private int DisplayedIntentValue(EnemyInstance e)
    {
        switch (e.intentAction)
        {
            case DianoCard.Data.EnemyAction.ATTACK:
            case DianoCard.Data.EnemyAction.MULTI_ATTACK:
            case DianoCard.Data.EnemyAction.COUNTDOWN_ATTACK:
            {
                int scaled = Mathf.Max(1, Mathf.RoundToInt(e.IntentLiveValue * e.damageScale));
                int afterWeak = e.weakTurns > 0 ? Mathf.Max(1, Mathf.FloorToInt(scaled * 0.75f)) : scaled;
                var target = ResolveLiveAttackTarget(e);
                int afterIronHide = afterWeak;
                if (target != null && target.data?.passiveType == DianoCard.Data.DinoPassiveType.IRON_HIDE)
                    afterIronHide = Mathf.Max(1, afterWeak - target.data.passiveValue);
                // 공룡은 취약 상태가 없음 → 플레이어 대상(target == null)일 때만 +50% 적용.
                int playerVuln = target == null ? (_battle?.state?.player?.vulnerableTurns ?? 0) : 0;
                return playerVuln > 0 ? Mathf.Max(1, Mathf.RoundToInt(afterIronHide * 1.5f)) : afterIronHide;
            }
            case DianoCard.Data.EnemyAction.COUNTDOWN_AOE:
            {
                int scaled = Mathf.Max(1, Mathf.RoundToInt(e.IntentLiveValue * e.damageScale));
                int playerVuln = _battle?.state?.player?.vulnerableTurns ?? 0;
                return playerVuln > 0 ? Mathf.Max(1, Mathf.RoundToInt(scaled * 1.5f)) : scaled;
            }
            default:
                return e.intentValue;
        }
    }

    // 적 머리 위 intent 표시 — 18개 아이콘 풀에서 액션별로 매핑된 아이콘 + (선택) 숫자 + hover 툴팁.
    private void DrawEnemyIntent(Vector2 center, EnemyInstance e)
    {
        bool drawnAsIcon = false;

        // 속박 중이면 인텐트 자리에 BIND 아이콘을 표시 — 행동이 봉인된 상태가 머리 위에서 즉시 읽혀야 함.
        if (e.stunTurns > 0)
        {
            var bindTex = HeadIcon("BIND");
            if (bindTex != null)
            {
                DrawSideBySideBadge(center, e.stunTurns, bindTex, 0f, Color.white);
            }
            else
            {
                GUI.Label(new Rect(center.x - 80f, center.y - 12f, 160f, 24f),
                          $"속박 {e.stunTurns}T", _intentStyle);
            }
            return;
        }

        // MULTI_ATTACK은 ATTACK과 같이 intentType=ATTACK이지만 분할타격 아이콘 + "Nx히트수" 표기로 차별화.
        if (e.intentAction == DianoCard.Data.EnemyAction.MULTI_ATTACK)
        {
            var multiTex = HeadIcon("MULTI_ATTACK");
            if (multiTex != null)
            {
                int hits = Mathf.Max(1, e.intentCount);
                DrawSideBySideBadgeText(center, $"{DisplayedIntentValue(e)}x{hits}", multiTex, 0f, Color.white);
                drawnAsIcon = true;
            }
            else
            {
                DrawAttackIconBadge(center, DisplayedIntentValue(e), 0f, boosted: false);
                drawnAsIcon = true;
            }
        }
        else if (e.intentType == EnemyIntentType.ATTACK)
        {
            DrawAttackIconBadge(center, DisplayedIntentValue(e), 0f, boosted: false);
            drawnAsIcon = true;
        }
        else
        {
            string iconId = IntentIconForAction(e.intentAction);
            var tex = HeadIcon(iconId);
            if (tex != null)
            {
                int shownValue = IntentShowsNumber(e.intentAction) ? DisplayedIntentValue(e) : 0;
                DrawSideBySideBadge(center, shownValue, tex, 0f, Color.white);
                drawnAsIcon = true;
            }
            else
            {
                GUI.Label(new Rect(center.x - 80f, center.y - 12f, 160f, 24f),
                          $"▲ {e.IntentLabel}", _intentStyle);
            }
        }

        DrawTargetBadge(center, e);
    }

    private (string title, string body) GetIntentTooltipText(EnemyInstance e)
    {
        // 속박 중이면 행동이 봉인되므로 원래 인텐트 대신 속박 정보를 그대로 노출.
        if (e.stunTurns > 0)
        {
            return ("속박 (Bind)", $"이번 턴 행동 불가. {e.stunTurns}턴 남음.");
        }

        // 데미지 액션은 스케일된 실효 피해량(d), 그 외 효과 수치는 raw intentValue(v).
        int v = e.intentValue;
        int d = DisplayedIntentValue(e);
        int c = e.intentCount;
        int t = e.telegraphRemaining;

        // 라이브 재해결 — 도발/사망/이탈 후 실제 DealAttack이 향할 곳을 그대로 표기 (뱃지와 동일 로직).
        var liveTarget = ResolveLiveAttackTarget(e);
        string atkTarget = liveTarget != null
            ? (!string.IsNullOrEmpty(liveTarget.data?.nameKr)
                ? liveTarget.data.nameKr
                : "아군 공룡")
            : "플레이어";

        // 카운트다운 잔여 턴 표기 — 0/1을 자연스럽게 분기.
        string countdownPhrase =
            t <= 0 ? "이번 턴" :
            t == 1 ? "다음 턴" :
                     $"{t}턴 후";

        switch (e.intentAction)
        {
            case DianoCard.Data.EnemyAction.ATTACK:           return ("공격",         $"{atkTarget}에게 {d} 피해.");
            case DianoCard.Data.EnemyAction.MULTI_ATTACK:     return ("다중 공격",    $"{atkTarget}에게 {d} 피해 × {c}회.");
            case DianoCard.Data.EnemyAction.DEFEND:           return ("방어",         $"자신 방어도 +{v}.");
            case DianoCard.Data.EnemyAction.POISON:           return ("독 부여",      $"플레이어에게 독 +{v}.\n독은 매 턴 종료 시 스택만큼 피해 후 1씩 감소.");
            case DianoCard.Data.EnemyAction.WEAK:             return ("약화 부여",    $"플레이어를 {v}턴 약화.\n약화 상태에서는 가하는 피해 -25%.");
            case DianoCard.Data.EnemyAction.VULNERABLE:       return ("취약 부여",    $"플레이어를 {v}턴 취약.\n취약 상태에서는 받는 피해 +50%.");
            case DianoCard.Data.EnemyAction.DRAIN:            return ("흡혈",         $"플레이어에게 {d} 피해 후 자신 HP를 같은 양만큼 회복.");
            case DianoCard.Data.EnemyAction.SILENCE:          return ("침묵",         $"아군 공룡 전체를 {v}턴 침묵.\n침묵된 공룡은 행동 불가.");
            case DianoCard.Data.EnemyAction.STEAL_SUMMON:     return ("공룡 강탈",    "아군 공룡 1체를 적 진영으로 전환.\n정화(PURIFY) 효과로 되찾을 수 있음.");
            case DianoCard.Data.EnemyAction.SUMMON:           return ("소환",         $"쫄 {Mathf.Max(1, v)}체 소환.");
            case DianoCard.Data.EnemyAction.REFILL_MOSS:      return ("이끼 보충",    $"이끼 정령을 최대 {v}체까지 보충.");
            case DianoCard.Data.EnemyAction.BUFF_SELF:        return ("자가 강화",    $"자신 ATK 영구 +{v}.");
            case DianoCard.Data.EnemyAction.EMPOWER_BOSS:     return ("보스 강화",    $"보스의 다음 공격 피해 +{v}.");
            case DianoCard.Data.EnemyAction.ARMOR_UP:         return ("장갑",         $"매 턴 시작 방어도 +{v} (영구).");
            case DianoCard.Data.EnemyAction.HEAL_BOSS:        return ("보스 회복",    $"보스 HP {v} 회복.");
            case DianoCard.Data.EnemyAction.BLOCK_BOSS:       return ("보스 방어",    $"보스 방어도 +{v}.");
            case DianoCard.Data.EnemyAction.COUNTDOWN_ATTACK: return ("예고 강타",    $"{countdownPhrase}에 {atkTarget}에게 {d} 피해.");
            case DianoCard.Data.EnemyAction.COUNTDOWN_AOE:    return ("예고 광역",    $"{countdownPhrase}에 플레이어 및 모든 공룡에게 {d} 피해.");
            case DianoCard.Data.EnemyAction.CLOG_DECK:        return ("방해 카드",    $"플레이어 버림더미에 잡초 카드 {v}장 추가.");
            case DianoCard.Data.EnemyAction.IDLE:             return ("대기",         "이번 턴 행동하지 않음.");
            default:                                          return ("?",           "다음 행동을 알 수 없음.");
        }
    }

    // StS 스타일 — 적 좌측에 고정 표시되는 정보 패널. 적 이름 + 인텐트 아이콘/설명 + 디버프 스택.
    private void DrawEnemyTooltip(Rect enemyRect, EnemyInstance e)
    {
        EnsurePassiveStyles();

        var (intentTitle, intentBody) = GetIntentTooltipText(e);
        string nameStr = !string.IsNullOrEmpty(e.data?.nameKr) ? e.data.nameKr
                          : (!string.IsNullOrEmpty(e.data?.nameEn) ? e.data.nameEn : "Enemy");

        const float tw = 240f;
        const float pad = 11f;
        var nameSize  = _tooltipTitleStyle.CalcSize(new GUIContent(nameStr));
        var titleSize = _tooltipTitleStyle.CalcSize(new GUIContent(intentTitle));
        float bodyH   = _tooltipBodyStyle.CalcHeight(new GUIContent(intentBody), tw - pad * 2f);

        string statusLine = BuildEnemyStatusLine(e);
        float statusH = string.IsNullOrEmpty(statusLine)
            ? 0f
            : _tooltipBodyStyle.CalcHeight(new GUIContent(statusLine), tw - pad * 2f);

        float iconSize = Mathf.Max(titleSize.y, 18f);
        float intentRowH = Mathf.Max(iconSize, titleSize.y);
        float th = 9f + nameSize.y + 6f + 1f + 7f + intentRowH + 4f + bodyH
                   + (string.IsNullOrEmpty(statusLine) ? 0f : (7f + 1f + 7f + statusH))
                   + 9f;

        // 적 좌측에 붙임. 화면 좌측 끝이면 우측으로 플립.
        float tx = enemyRect.x - tw - 12f;
        float ty = enemyRect.y;
        if (tx < 6f) tx = enemyRect.xMax + 12f;
        if (tx + tw > RefW) tx = RefW - tw - 6f;
        if (ty + th > RefH) ty = RefH - th - 6f;
        if (ty < 6f) ty = 6f;

        // 그림자
        FillRect(new Rect(tx + 3f, ty + 4f, tw, th), new Color(0f, 0f, 0f, 0.45f));

        // 외곽: 에이지드 브라스
        var outer = new Rect(tx, ty, tw, th);
        FillRect(outer, new Color(0.62f, 0.48f, 0.28f, 0.95f));

        // 안쪽: 잉크 차콜 + 미세 보라
        var inner = new Rect(outer.x + 2f, outer.y + 2f, outer.width - 4f, outer.height - 4f);
        FillRect(inner, new Color(0.085f, 0.07f, 0.115f, 0.97f));

        // 미세 하이라이트
        FillRect(new Rect(inner.x, inner.y, inner.width, 1f), new Color(1f, 0.85f, 0.55f, 0.18f));

        Color divider = new Color(0.62f, 0.48f, 0.28f, 0.55f);

        // 1) 적 이름 헤더
        var nameRect = new Rect(tx + pad, ty + 7f, tw - pad * 2f, nameSize.y);
        GUI.Label(nameRect, nameStr, _tooltipTitleStyle);

        float lineY = nameRect.yMax + 6f;
        FillRect(new Rect(tx + pad, lineY, tw - pad * 2f, 1f), divider);

        // 2) 인텐트 아이콘 + 제목 — 속박 중이면 머리 위 아이콘과 동일하게 BIND로 노출.
        float intentTopY = lineY + 7f;
        string iconId = e.stunTurns > 0
            ? "BIND"
            : (e.intentAction == DianoCard.Data.EnemyAction.MULTI_ATTACK
                ? "MULTI_ATTACK"
                : (e.intentType == EnemyIntentType.ATTACK ? "ATTACK" : IntentIconForAction(e.intentAction)));
        var iconTex = HeadIcon(iconId);
        float titleX = tx + pad;
        if (iconTex != null)
        {
            var iconR = new Rect(tx + pad, intentTopY + (intentRowH - iconSize) * 0.5f, iconSize, iconSize);
            GUI.DrawTexture(iconR, iconTex, ScaleMode.ScaleToFit, true);
            titleX = iconR.xMax + 5f;
        }
        var titleR = new Rect(titleX, intentTopY + (intentRowH - titleSize.y) * 0.5f, tx + tw - pad - titleX, titleSize.y);
        GUI.Label(titleR, intentTitle, _tooltipTitleStyle);

        // 3) 인텐트 본문
        float bodyY = intentTopY + intentRowH + 4f;
        var bodyR = new Rect(tx + pad, bodyY, tw - pad * 2f, bodyH);
        GUI.Label(bodyR, intentBody, _tooltipBodyStyle);

        // 4) 디버프 스택 (있을 때만)
        if (!string.IsNullOrEmpty(statusLine))
        {
            float lineY2 = bodyY + bodyH + 7f;
            FillRect(new Rect(tx + pad, lineY2, tw - pad * 2f, 1f), divider);
            var statusR = new Rect(tx + pad, lineY2 + 7f, tw - pad * 2f, statusH);
            GUI.Label(statusR, statusLine, _tooltipBodyStyle);
        }
    }

    private static string BuildEnemyStatusLine(EnemyInstance e)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (e.poisonStacks    > 0) parts.Add($"독 {e.poisonStacks}");
        if (e.bleedStacks     > 0) parts.Add($"출혈 {e.bleedStacks}");
        if (e.burnStacks      > 0) parts.Add($"화상 {e.burnStacks}");
        if (e.weakTurns       > 0) parts.Add($"약화 {e.weakTurns}턴");
        if (e.vulnerableTurns > 0) parts.Add($"취약 {e.vulnerableTurns}턴");
        if (e.stunTurns       > 0) parts.Add($"기절 {e.stunTurns}턴");
        return parts.Count == 0 ? "" : "상태이상:  " + string.Join("  ·  ", parts);
    }

    // 인텐트 아이콘 우상단 코너에 작은 타겟 뱃지(플레이어/공룡) 표시.
    // 데미지 숫자가 우하단 안쪽에 그려지므로 뱃지는 우상단으로 빼서 세로로 분리.
    // RollIntent 결과에 따라 갈라지는 액션(단일 공격/AOE)에만 노출. 무조건 한쪽으로 가는 액션은
    // 인텐트 아이콘 자체로 자명하므로 뱃지 생략.
    private void DrawTargetBadge(Vector2 center, EnemyInstance e)
    {
        if (_battle?.state == null) return;
        var ids = GetTargetBadgeIds(e);
        if (ids == null || ids.Length == 0) return;

        const float intentHalf = 20f;   // 인텐트 아이콘 size 40f의 절반
        const float badgeSize = 12f;
        const float gap = 2f;
        float totalW = ids.Length * badgeSize + (ids.Length - 1) * gap;
        // 우상단 정렬 — 우측 라인은 아이콘 우측 끝에 맞추고, 위쪽 라인도 아이콘 위쪽 끝에 맞춤.
        float startX = center.x + intentHalf - totalW;
        float y = center.y - intentHalf;

        for (int i = 0; i < ids.Length; i++)
        {
            var tex = HeadIcon(ids[i]);
            if (tex == null) continue;
            var r = new Rect(startX + i * (badgeSize + gap), y, badgeSize, badgeSize);
            // 작은 차콜 보더로 실루엣이 배경에 묻히지 않게.
            FillRect(new Rect(r.x - 1f, r.y - 1f, r.width + 2f, r.height + 2f),
                     new Color(0.085f, 0.07f, 0.115f, 0.9f));
            GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit, true);
        }
    }

    // BattleManager.DealAttack의 타겟 결정 로직 미러링 — live 도발 > 사전롤 intentTargetDino(사망/이탈 시 살아있는 공룡으로 폴백) > player.
    // 인텐트 롤 이후 도발 발동/타겟 사망/필드 이탈이 있으면 뱃지가 실제 데미지 행선지와 어긋나는 버그 방지.
    // 원래 공룡을 노렸는데 그 공룡이 사라진 경우 다른 살아있는 공룡으로 인계 — 공룡이 모두 없을 때만 플레이어 뱃지로 전환.
    private SummonInstance ResolveLiveAttackTarget(EnemyInstance e)
    {
        if (_battle?.state == null) return null;
        foreach (var s in _battle.state.field) if (s.IsTaunting) return s;
        var t = e.intentTargetDino;
        if (t == null) return null;
        if (!t.IsDead && _battle.state.field.Contains(t)) return t;
        foreach (var s in _battle.state.field)
        {
            if (!s.IsDead) return s;
        }
        return null;
    }

    private string[] GetTargetBadgeIds(EnemyInstance e)
    {
        switch (e.intentAction)
        {
            // 단일 대상 공격 — DealAttack과 동일하게 라이브 재해결한 결과를 노출.
            case DianoCard.Data.EnemyAction.ATTACK:
            case DianoCard.Data.EnemyAction.MULTI_ATTACK:
            case DianoCard.Data.EnemyAction.COUNTDOWN_ATTACK:
                return ResolveLiveAttackTarget(e) != null
                    ? new[] { "TARGET_DINO" }
                    : new[] { "TARGET_PLAYER" };

            // 광역 — 플레이어 + 공룡 둘 다.
            case DianoCard.Data.EnemyAction.COUNTDOWN_AOE:
                return new[] { "TARGET_PLAYER", "TARGET_DINO" };

            // 그 외(STEAL/SILENCE/POISON/WEAK/DRAIN/VULNERABLE/CLOG_DECK/DEFEND/...)는
            // 액션 아이콘만으로 타겟 종류가 자명하므로 뱃지 생략.
            default:
                return null;
        }
    }

    // 공격 아이콘(검) + 데미지 숫자 뱃지. 적은 -45°, 아군은 +45°. boosted면 숫자를 강조 색으로.
    // dimmed=true면 검 아이콘과 숫자가 살짝 어두워짐 (아군 공룡이 이번 턴 공격 완료한 상태).
    private void DrawAttackIconBadge(Vector2 center, int value, float angleDeg, bool boosted, bool dimmed = false)
    {
        var tex = HeadIcon("ATTACK");
        if (tex == null) return;
        Color textCol = dimmed ? new Color(0.55f, 0.55f, 0.58f, 1f)
                               : (boosted ? new Color(1f, 0.85f, 0.3f) : Color.white);
        if (dimmed)
        {
            Color prev = GUI.color;
            GUI.color = new Color(0.50f, 0.50f, 0.52f, 1f);
            DrawSideBySideBadge(center, value, tex, angleDeg, textCol);
            GUI.color = prev;
        }
        else
        {
            DrawSideBySideBadge(center, value, tex, angleDeg, textCol);
        }
    }

    // 아이콘은 center에 정중앙으로 배치, 숫자는 아이콘 우하단 코너에 작은 뱃지로 살짝 겹쳐 표시.
    private void DrawSideBySideBadge(Vector2 center, int value, Texture2D icon, float angleDeg, Color textCol)
    {
        const float iconSize = 40f;
        const float numW = 18f;
        const float numH = 16f;

        var iconRect = new Rect(center.x - iconSize / 2f, center.y - iconSize / 2f, iconSize, iconSize);

        // 아이콘 먼저 (회전 옵션). 숫자가 위에 올라가야 가려지지 않음.
        if (Mathf.Abs(angleDeg) > 0.01f)
        {
            Matrix4x4 baseMatrix = GUI.matrix;
            GUI.matrix = baseMatrix * RotateAroundPivotMatrix(angleDeg, iconRect.center);
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, alphaBlend: true);
            GUI.matrix = baseMatrix;
        }
        else
        {
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, alphaBlend: true);
        }

        // 숫자: 아이콘 우하단 코너에 살짝 겹치게. value <= 0이면 숫자 생략 (소환·강탈 등).
        if (value > 0)
        {
            var numRect = new Rect(iconRect.xMax - numW * 0.6f, iconRect.yMax - numH * 0.6f, numW, numH);
            DrawTextWithOutline(numRect, value.ToString(), _intentNumberStyle,
                                textCol, new Color(0f, 0f, 0f, 0.95f), 1.4f);
        }
    }

    // DrawSideBySideBadge의 문자열 버전 — "3x3"처럼 곱셈 표기 라벨용. 문자 수가 늘어나므로 라벨 폭을 확장.
    private void DrawSideBySideBadgeText(Vector2 center, string label, Texture2D icon, float angleDeg, Color textCol)
    {
        const float iconSize = 40f;
        const float numW = 28f;
        const float numH = 16f;

        var iconRect = new Rect(center.x - iconSize / 2f, center.y - iconSize / 2f, iconSize, iconSize);

        if (Mathf.Abs(angleDeg) > 0.01f)
        {
            Matrix4x4 baseMatrix = GUI.matrix;
            GUI.matrix = baseMatrix * RotateAroundPivotMatrix(angleDeg, iconRect.center);
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, alphaBlend: true);
            GUI.matrix = baseMatrix;
        }
        else
        {
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, alphaBlend: true);
        }

        if (!string.IsNullOrEmpty(label))
        {
            var numRect = new Rect(iconRect.xMax - numW * 0.7f, iconRect.yMax - numH * 0.6f, numW, numH);
            DrawTextWithOutline(numRect, label, _intentNumberStyle,
                                textCol, new Color(0f, 0f, 0f, 0.95f), 1.4f);
        }
    }

    // 방패 아이콘 + 숫자 뱃지. center를 중심으로 size 크기로 그림. icon으로 플레이어/적 텍스처 분리.
    private void DrawBlockBadge(Vector2 center, int block, float size = 40f, Texture2D icon = null)
    {
        var iconRect = new Rect(center.x - size / 2, center.y - size / 2, size, size);
        var tex = icon != null ? icon : HeadIcon("DEFEND");
        if (tex != null)
        {
            GUI.DrawTexture(iconRect, tex, ScaleMode.ScaleToFit, alphaBlend: true);
        }

        int prevFontSize = _centerStyle.fontSize;
        Color prevColor = _centerStyle.normal.textColor;
        _centerStyle.fontSize = Mathf.RoundToInt(size * 0.3f);

        // GUIStyle은 마우스가 라벨 위에 있을 때 hover.textColor로 폴백한다. normal만 갱신하면
        // 호버 시 그림자(검정) 자리에도 본문 색(흰색)이 찍혀 "8"이 +1,+2 오프셋으로 두 번 보이는 버그가 난다.
        // 모든 state를 같이 갱신해서 hover/normal 모두 동일하게 만든다.
        var shadowRect = new Rect(iconRect.x + 1, iconRect.y + 2, iconRect.width, iconRect.height);
        SetAllStateColors(_centerStyle, new Color(0f, 0f, 0f, 0.75f));
        GUI.Label(shadowRect, block.ToString(), _centerStyle);

        SetAllStateColors(_centerStyle, Color.white);
        GUI.Label(iconRect, block.ToString(), _centerStyle);

        _centerStyle.fontSize = prevFontSize;
        SetAllStateColors(_centerStyle, prevColor);
    }

    // 플레이어 주위에 떠오르는 반투명 방패 버블. block이 증가한 프레임에 트리거되어
    // ShieldFxDuration 동안 페이드 인 → 유지(펄스) → 페이드 아웃.
    private void DrawPlayerShieldFx(Vector2 center, float targetW, float targetH)
    {
        if (_playerShieldFxStartTime < 0f) return;
        float elapsed = Time.time - _playerShieldFxStartTime;
        if (elapsed >= ShieldFxDuration)
        {
            _playerShieldFxStartTime = -1f;
            return;
        }
        DrawShieldFxAt(center, _playerShieldFxStartTime, targetW, targetH);
    }

    /// <summary>공룡/적 등 임의 entity의 방어막 FX. _entityShieldFxStart 사전에서 시작 시각을 읽어
    /// 플레이어와 동일한 비주얼로 재생. 듀레이션 종료 시 dict에서 제거해 메모리 누수를 막는다.</summary>
    private void DrawEntityShieldFx(object entityKey, Vector2 center, float targetW, float targetH)
    {
        if (entityKey == null) return;
        if (!_entityShieldFxStart.TryGetValue(entityKey, out float start)) return;
        float elapsed = Time.time - start;
        if (elapsed >= ShieldFxDuration)
        {
            _entityShieldFxStart.Remove(entityKey);
            return;
        }
        DrawShieldFxAt(center, start, targetW, targetH);
    }

    /// <summary>방어막 FX 비주얼 본체 — 시작 시각만 외부에서 받아 트리거 출처에 무관하게 동작.
    /// 페이드인(0~0.2) → 홀드(0.2~0.6) → 페이드아웃(0.6~1) 엔벨로프, 펄스, 확산링 3레이어.</summary>
    private void DrawShieldFxAt(Vector2 center, float startTime, float targetW, float targetH)
    {
        var tex = _shieldFxTexture != null ? _shieldFxTexture : _manaFrameTexture;
        if (tex == null) return;

        float t = Time.time - startTime;
        if (t < 0f || t >= ShieldFxDuration) return;

        float n = t / ShieldFxDuration;

        // 엔벨로프: 0~0.2 fade-in → 0.2~0.6 hold → 0.6~1 fade-out (in/out 길게 잡아 더 부드럽게)
        float envelope;
        if (n < 0.2f) envelope = n / 0.2f;
        else if (n < 0.6f) envelope = 1f;
        else envelope = 1f - (n - 0.6f) / 0.4f;
        envelope = Mathf.Clamp01(envelope);

        float pulse = 0.95f + 0.05f * Mathf.Sin(Time.time * 5f);

        // 캐릭터 실루엣 대비 살짝 크게 잡은 버블 기준 크기
        float baseSize = Mathf.Max(targetW, targetH) * 1.35f;

        var prevColor = GUI.color;

        // 1) 바깥 soft glow — 매우 옅은 오라
        {
            float size = baseSize * 1.25f * pulse;
            var r = new Rect(center.x - size / 2f, center.y - size / 2f, size, size);
            GUI.color = new Color(1f, 1f, 1f, 0.10f * envelope);
            GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, alphaBlend: true);
        }

        // 2) 메인 bubble — 캐릭터를 감싸는 중심 방패. 완전 흰색 틴트로 원본 색감을 살림.
        {
            float size = baseSize * pulse;
            var r = new Rect(center.x - size / 2f, center.y - size / 2f, size, size);
            GUI.color = new Color(1f, 1f, 1f, 0.30f * envelope);
            GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, alphaBlend: true);
        }

        // 3) 확산 링 — 트리거 직후 0.5초 동안 밖으로 퍼지며 페이드 (옅게)
        {
            float ringN = Mathf.Clamp01(n / 0.5f);
            float ringAlpha = (1f - ringN) * 0.20f;
            if (ringAlpha > 0f)
            {
                float size = baseSize * (1.05f + ringN * 0.55f);
                var r = new Rect(center.x - size / 2f, center.y - size / 2f, size, size);
                GUI.color = new Color(1f, 1f, 1f, ringAlpha);
                GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, alphaBlend: true);
            }
        }

        GUI.color = prevColor;
    }

    private void DrawSummon(SummonInstance s, int summonIndex, Vector2 center)
    {
        // Lunge 오프셋: 공격 중이고 attack 시퀀스가 없는 소환수만 오른쪽으로 sin 곡선 전진.
        // 시퀀스가 있는 공룡은 프레임 자체가 공격 모션을 표현하므로 좌표 이동은 이중 모션이 됨.
        if (ReferenceEquals(_attackingUnit, s)
            && !_fieldDinoAttackFrames.ContainsKey(s.data.id))
        {
            float lunge = Mathf.Sin(_attackProgress * Mathf.PI) * LungePixels;
            center.x += lunge;
        }

        float scale = s.data.SafeFieldScale;
        float w = dinoSize * scale, h = dinoSize * scale;

        // Idle breathing — Y만 살짝 늘리고 발 위치(rect 바닥)는 고정.
        // 공식: CharacterSelectUI / BattleEntityView.breathing과 동일 (smoothstep eased sin, Y만 0.9%).
        // 주기(freq)와 위상(phase)을 개체 해시로 분산 → 여러 공룡이 동시 박자로 움직이지 않음.
        // freq: 0.12 ~ 0.19Hz (~5.3s ~ 8.3s), phase: 0 ~ 2π
        const float breathAmp = 0.015f;
        int sHash = s.GetHashCode();
        float freqNoise = ((sHash >> 10) & 0x3FF) / 1024f;
        float phaseNoise = (sHash & 0x3FF) / 1024f;
        float breathFreq = 0.12f + freqNoise * 0.07f;
        float phase = phaseNoise * Mathf.PI * 2f;
        float tBreath = Time.time * Mathf.PI * 2f * breathFreq + phase;
        float rawSin = Mathf.Sin(tBreath);
        float eased = rawSin * rawSin * Mathf.Sign(rawSin);
        float breathY = 1f + eased * breathAmp;

        float drawH = h * breathY;
        float footY = center.y + h / 2f;          // 원래 rect의 바닥 — 발 위치로 사용
        var rect = new Rect(center.x - w / 2f, footY - drawH, w, drawH);

        // 융합 모드 컨텍스트 — sprite tint, 마커, 클릭 처리 모두에서 재사용.
        bool fusionModeField = _battle?.state != null
            && _targetingCardIndex >= 0
            && _targetingCardIndex < _battle.state.hand.Count
            && CardNeedsFusionTargets(_battle.state.hand[_targetingCardIndex].data);
        bool isFusionMaterialAField = fusionModeField && _fusionMaterialAPicked
            && !_fusionMaterialA.isHand
            && _fusionMaterialA.index == summonIndex;
        bool fieldFusionEligible = fusionModeField && IsFusionMaterialEligible(s, summonIndex, isHand: false);
        // 스테이지 2: A도 후보도 아니면 융합 대상 아님 — 어둡게 깔아 후보만 도드라지게.
        bool fusionInactive = fusionModeField && _fusionMaterialAPicked
            && !isFusionMaterialAField && !fieldFusionEligible;

        // 융합 후보(또는 이미 선택된 A) 위에 호버되면 발 고정한 채 1.08x 살짝 키움 — "버튼처럼 누르라" 시그널.
        // hit rect는 18px 패딩이 있어 스케일 변화로 호버 토글이 진동하지 않음.
        if (fieldFusionEligible || isFusionMaterialAField)
        {
            var evHov = Event.current;
            if (evHov != null)
            {
                var hitPre = new Rect(rect.x - 18f, rect.y - 18f,
                                      rect.width + 36f, rect.height + 36f);
                if (hitPre.Contains(evHov.mousePosition))
                {
                    const float kFusionHoverBoost = 1.08f;
                    float newW = rect.width * kFusionHoverBoost;
                    float newH = rect.height * kFusionHoverBoost;
                    rect = new Rect(rect.center.x - newW * 0.5f,
                                    rect.yMax - newH,
                                    newW, newH);
                }
            }
        }

        // 이 공룡이 공격/스킬 타겟팅의 source면 화살표 출발 rect로 기록.
        // 융합은 A로 선택된 필드 공룡이 source — A에서 두 번째 재료 후보로 화살표가 뻗어나간다.
        if (summonIndex == _targetingSummonIndex
            || summonIndex == _targetingSummonSkillIndex
            || isFusionMaterialAField)
        {
            _arrowSourceRect = rect;
            _arrowSourceValid = true;
        }

        // Reward 상태면 공룡도 world-space overlay와 같은 톤으로 어둡게 tint
        bool inReward = GameStateManager.Instance != null && GameStateManager.Instance.State == GameState.Reward;
        // 공격 불가 상태(이미 공격 / 침묵)는 머리 위 검 뱃지만 어둡게 — 공룡 본체는 평상 색 유지.
        bool selected = _targetingSummonIndex == summonIndex;
        bool attackBadgeDim = !s.CanAttack && !inReward;
        Color prevGuiColor = GUI.color;
        if (inReward) GUI.color = new Color(0.6f, 0.6f, 0.6f, 1f);
        else if (fusionInactive) GUI.color = new Color(0.20f, 0.20f, 0.22f, 0.78f);
        else if (selected) GUI.color = new Color(1.12f, 1.08f, 0.9f, 1f);

        if (_fieldDinoSprites.TryGetValue(s.data.id, out var tex) && tex.height > 0)
        {
            float aspect = tex.width / (float)tex.height;
            var drawRect = ComputeBottomAnchoredDrawRect(rect, aspect);

            // 공격 중이고 attack 시퀀스가 있으면 프레임 스왑 — _attackProgress(0..1) 기준 균등 분배.
            // attack 캔버스는 idle 캔버스보다 크게 그려져 있어(스윙 여유분 포함) 그대로 idle 슬롯에 fit하면
            // 캐릭터가 작아 보임. 동일한 idle 영역을 attack 캔버스 안에서 동일 위치(가로 중앙·세로 발맞춤)로
            // 가정하고, attack 텍스처를 idle 대비 (atkW/idleW, atkH/idleH) 만큼 확대해 그린다.
            // 결과: attack 캔버스 안에 들어있는 캐릭터의 발 위치 = idle 발 위치, 캐릭터 크기 = idle 크기.
            bool drewAttackFrame = false;
            if (ReferenceEquals(_attackingUnit, s)
                && _fieldDinoAttackFrames.TryGetValue(s.data.id, out var atkFrames)
                && atkFrames != null && atkFrames.Length > 0)
            {
                int frameIdx = Mathf.Clamp(Mathf.FloorToInt(_attackProgress * atkFrames.Length), 0, atkFrames.Length - 1);
                var atkTex = atkFrames[frameIdx];
                if (atkTex != null && atkTex.width > 0 && atkTex.height > 0 && tex.width > 0 && tex.height > 0)
                {
                    float wScale = atkTex.width  / (float)tex.width;
                    float hScale = atkTex.height / (float)tex.height;
                    // T1/T2 PhotoRoom-cropped: attack 캔버스에 swing margin 없이 body가 100% 차지 →
                    // wScale/hScale 그대로 적용하면 body가 idle 대비 (wScale, hScale)배 커 보임.
                    // 측정된 boost (= idle_h / atk_h)를 양 축에 곱해 body 높이를 idle과 매칭. T0은 dictionary 미등록 → 그대로.
                    if (_fieldDinoT12AttackScaleBoost.TryGetValue(s.data.id, out var sboost))
                    {
                        wScale *= sboost;
                        hScale *= sboost;
                    }
                    float atkDrawW = drawRect.width  * wScale;
                    float atkDrawH = drawRect.height * hScale;
                    var atkDrawRect = new Rect(
                        drawRect.center.x - atkDrawW * 0.5f,
                        drawRect.yMax    - atkDrawH,
                        atkDrawW,
                        atkDrawH);
                    GUI.DrawTexture(atkDrawRect, atkTex, ScaleMode.StretchToFill, alphaBlend: true);
                    drewAttackFrame = true;
                }
            }

            if (!drewAttackFrame)
                GUI.DrawTexture(drawRect, tex, ScaleMode.StretchToFill, alphaBlend: true);
        }
        else
        {
            FillRect(rect, new Color(0.4f, 0.7f, 0.4f, 0.8f));
            GUI.Label(new Rect(rect.x, rect.y + h / 2 - 10, rect.width, 22),
                      s.data.nameKr, _centerStyle);
        }

        GUI.color = prevGuiColor;

        // 방어막 FX — 호위 선풍(C111)/무리의 천막(C112)/등판 갑주 등으로 block이 증가한 직후 트리거.
        // Update의 DetectEntityBlockGain이 시작 시각을 _entityShieldFxStart에 기록.
        DrawEntityShieldFx(s, new Vector2(rect.center.x, rect.center.y), rect.width, rect.height);

        // HP 바 — 적과 동일 규칙: 스프라이트 발(rect.yMax) 바로 아래 통일 오프셋.
        float summonBarW = ComputeHpBarWidth(rect.width);
        var summonHpRect = new Rect(rect.center.x - summonBarW / 2, rect.yMax + 4f, summonBarW, hpBarHeight);
        DrawHpBar(summonHpRect, s.hp, s.maxHp, new Color(0.65f, 0.16f, 0.18f), entity: s);

        // 방어도 뱃지 — HP 바 왼쪽에 겹치게 (플레이어와 동일 스타일)
        if (s.block > 0)
        {
            DrawBlockBadge(new Vector2(summonHpRect.x, summonHpRect.center.y), s.block, 40f, HeadIcon("DEFEND"));
        }


        // 티어/스택 인디케이터 — T1은 숨기고 T2 MAX만 표시. 초식: 누적 스택.
        string stackText = null;
        if (s.data.subType == CardSubType.CARNIVORE)
        {
            if (s.data.id.EndsWith("_T2")) stackText = "T2 · MAX";
        }
        else if (s.stacks > 0)
        {
            stackText = $"스택 {s.stacks}";
        }

        // 상태 칩 — HP바 바로 아래, HP바 왼쪽 끝에서 오른쪽으로 쌓임.
        DrawSummonStatusChips(new Rect(summonHpRect.x, summonHpRect.yMax + 4f, summonHpRect.width, 26f), s);

        // T1/T2·스택 텍스트는 칩 아래에 표시
        if (!string.IsNullOrEmpty(stackText))
        {
            var stackRect = new Rect(rect.x, summonHpRect.yMax + 32f, rect.width, 16f);
            var prev = _centerStyle.normal.textColor;
            _centerStyle.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
            GUI.Label(new Rect(stackRect.x + 1, stackRect.y + 1, stackRect.width, stackRect.height), stackText, _centerStyle);
            _centerStyle.normal.textColor = new Color(1f, 0.88f, 0.55f);
            GUI.Label(stackRect, stackText, _centerStyle);
            _centerStyle.normal.textColor = prev;
        }

        // 스킬/동족포식 뱃지 유무에 따라 ATK 뱃지 위치가 달라짐 — 둘 다 뜨면 한 쌍을 공룡 정중앙으로 묶고
        // 검만 뜨면 그대로 정중앙. 옆 공룡 영역을 침범하던 우측 쏠림 해소.
        // CANNIBAL 패시브 보유자는 스킬 슬롯 자리에 송곳니 뱃지를 그린다 (스킬과 공존하지 않음 — 마준가는 시그니처 스킬 없음).
        var skillData = DianoCard.Data.DataManager.Instance.GetSkill(s.data.id);
        bool hasSkill = skillData != null;
        bool hasCannibalBadge = !hasSkill
            && s.data?.passiveType == DianoCard.Data.DinoPassiveType.CANNIBAL
            && !s.IsDead;
        const float kBadgeSize = 40f;
        const float kBadgeGap = 8f;
        float pairOffset = (hasSkill || hasCannibalBadge) ? -(kBadgeSize + kBadgeGap) * 0.5f : 0f;

        // ATK 뱃지 — 머리 위 (적 intent와 미러 대칭). 아군은 검을 +45°로 회전.
        // 이 뱃지를 클릭하면 공격 타겟팅 시작 (예전엔 공룡 전체 클릭). 클릭 영역은 시인성보다 살짝 크게.
        Vector2 badgeCenter = new Vector2(rect.center.x + pairOffset, rect.y - 12f);
        DrawAttackIconBadge(badgeCenter, s.TotalAttack, 0f, s.tempAttackBonus > 0, attackBadgeDim);
        var badgeHitRect = new Rect(badgeCenter.x - 36f, badgeCenter.y - 36f, 72f, 72f);
        bool badgeActive = !inReward && _battle?.state != null && !_battle.state.IsOver
            && _targetingCardIndex < 0 && _swapFromCardIndex < 0
            && _reinforcePickerCardIndex < 0 && s.CanAttack;
        if (badgeActive)
        {
            var ev2 = Event.current;
            if (ev2 != null && ev2.type == EventType.MouseDown && ev2.button == 0
                && badgeHitRect.Contains(ev2.mousePosition))
            {
                ev2.Use();
                _targetingSummonIndex = (_targetingSummonIndex == summonIndex) ? -1 : summonIndex;
            }
        }

        // 스킬 아이콘 — T1+ 진화 공룡만 (DinoSkillData 존재 시). 평타와 별개 자원.
        // 위치: 검 우측에 같은 높이로 나란히. 위 pairOffset으로 한 쌍이 공룡 중앙에 정렬됨.
        if (hasSkill)
        {
            DrawSummonSkillBadge(s, summonIndex, skillData, rect, summonHpRect, inReward);
        }
        else if (hasCannibalBadge)
        {
            DrawSummonCannibalBadge(s, summonIndex, rect, inReward);
        }

        // 클릭 처리 우선순위:
        //   1) 교체 모드 (swap) — 필드 꽉 찬 상태에서 SUMMON 카드 플레이 시
        //   2) 아군 타겟 카드 모드 — 수호 마법/먹이 단일 타겟 카드
        //   3) 포션 ALLY 타겟 모드
        //   4) 융합 모드 — 같은 종/티어 재료 선택
        //   5) 동족포식 모드 — 마준가가 잡아먹을 아군 1마리 선택
        //   (일반 summon-attack은 검 뱃지로 대체되어 본체 클릭은 무시)
        if (!inReward && _battle?.state != null && !_battle.state.IsOver)
        {
            var ev = Event.current;
            bool hovered = ev != null && rect.Contains(ev.mousePosition);

            bool allyTargetMode = _targetingCardIndex >= 0
                && _targetingCardIndex < _battle.state.hand.Count
                && CardNeedsAllyTarget(_battle.state.hand[_targetingCardIndex].data);
            // fusionModeField / isFusionMaterialAField / fieldFusionEligible 는 sprite tint 직전에 이미 계산됨.

            if (_swapFromCardIndex >= 0)
            {
                // 이미 이번 턴 공격한 공룡은 교체 금지(2회 공격 방지) — 글로우/타겟 등록 모두 스킵.
                bool swapEligible = !s.hasAttackedThisTurn;
                if (swapEligible)
                {
                    DrawTargetFootGlow(rect, hovered);
                    _arrowTargetRects.Add(rect);
                    if (ev != null && ev.type == EventType.MouseDown && ev.button == 0 && hovered)
                    {
                        ev.Use();
                        int cardIdx = _swapFromCardIndex;
                        int swapIdx = summonIndex;
                        _swapFromCardIndex = -1;
                        _pending.Add(() => {
                            _battle.PlayCard(cardIdx, -1, swapIdx);
                            _playerView?.PlaySummon(ComputeAttackDir(-1));
                        });
                    }
                }
            }
            else if (allyTargetMode)
            {
                DrawTargetFootGlow(rect, hovered);
                _arrowTargetRects.Add(rect);
                if (ev != null && ev.type == EventType.MouseDown && ev.button == 0 && hovered)
                {
                    ev.Use();
                    int cardIdx = _targetingCardIndex;
                    int allyIdx = summonIndex;
                    _targetingCardIndex = -1;
                    _pending.Add(() => { _battle.PlayCard(cardIdx, -1, -1, allyIdx); });
                }
            }
            else if (_targetingPotionIndex >= 0 && CurrentPotionTargetsAlly())
            {
                // 포션 ALLY 타겟팅: 이 공룡에게 포션 사용 (P013 강화 / P015 각성).
                DrawTargetFootGlow(rect, hovered);
                _arrowTargetRects.Add(rect);
                if (ev != null && ev.type == EventType.MouseDown && ev.button == 0 && hovered)
                {
                    ev.Use();
                    int slotIdx = _targetingPotionIndex;
                    int allyIdx = summonIndex;
                    _targetingPotionIndex = -1;
                    _pending.Add(() => _battle.UsePotion(slotIdx, allyIdx));
                }
            }
            else if (fusionModeField)
            {
                // 필드 공룡 클릭 영역 — 스프라이트 투명 영역도 잡히도록 외곽 18px 패딩.
                // 히트 rect는 시각 rect 위에 깔리는 invisible padded box.
                var fusionHitRect = new Rect(rect.x - 18f, rect.y - 18f,
                                             rect.width + 36f, rect.height + 36f);
                bool fusionHovered = ev != null && fusionHitRect.Contains(ev.mousePosition);

                if (isFusionMaterialAField)
                {
                    // 이미 선택된 재료 A — 마커 없이 재클릭으로 선택 해제만 처리.
                    if (ev != null && ev.type == EventType.MouseDown && ev.button == 0 && fusionHovered)
                    {
                        ev.Use();
                        _fusionMaterialAPicked = false;
                    }
                }
                else if (fieldFusionEligible)
                {
                    // 양성 마커 없음 — 비후보 dim + 호버 스케일업으로 신호 충분.
                    // 화살표 끝점 스냅 대상으로 등록 — 호버 시 화살표가 이 공룡 중심으로 빨려간다.
                    _arrowTargetRects.Add(rect);
                    if (ev != null && ev.type == EventType.MouseDown && ev.button == 0 && fusionHovered)
                    {
                        ev.Use();
                        HandleFusionMaterialClick(DianoCard.Battle.FusionMaterial.Field(summonIndex));
                    }
                }
            }
            else if (_cannibalFeedFromIndex >= 0)
            {
                // 동족포식 모드 — eater(마준가) 외 다른 살아있는 아군이 클릭 가능 타겟.
                // eater 본인 클릭은 모드 취소로 처리.
                bool isEater = _cannibalFeedFromIndex == summonIndex;
                if (isEater)
                {
                    DrawFusionSelectedMarker(rect);
                    if (ev != null && ev.type == EventType.MouseDown && ev.button == 0 && hovered)
                    {
                        ev.Use();
                        _cannibalFeedFromIndex = -1;
                    }
                }
                else if (!s.IsDead)
                {
                    DrawTargetFootGlow(rect, hovered);
                    _arrowTargetRects.Add(rect);
                    if (ev != null && ev.type == EventType.MouseDown && ev.button == 0 && hovered)
                    {
                        ev.Use();
                        int eaterIdx = _cannibalFeedFromIndex;
                        int preyIdx = summonIndex;
                        _cannibalFeedFromIndex = -1;
                        _pending.Add(() => _battle.FeedCannibal(eaterIdx, preyIdx));
                    }
                }
            }
            // 공룡 본체 클릭은 공격 타겟팅 토글에 사용하지 않음 (검 뱃지로 대체).
            // 카드 타겟팅이 아닐 때 공룡 영역 클릭은 무시 (이벤트 안 잡음 → 다른 UI에 영향 없음).
        }

        // 선택 하이라이트 (발치 글로우) — 적 타겟팅 글로우와 유사한 톤.
        if (selected && _battle?.state != null && !_battle.state.IsOver)
        {
            DrawTargetFootGlow(rect, true);
        }
    }

    /// <summary>
    /// 진화 공룡(T1+)의 스킬 아이콘 — 평타와 별개 자원, 턴 단위 쿨다운.
    /// 위치: 검(ATTACK) 뱃지 우측, 같은 높이. 40px 정사각.
    /// 상태:
    ///  - READY: 풀컬러 + 청록 펄스 글로우 + 클릭 가능. ENEMY 타겟이면 _targetingSummonSkillIndex 세팅, AOE/SELF면 즉시 발동.
    ///  - 쿨다운 중: 회색 dim + 우하단 "{n}" 칩, 비활성.
    ///  - 전투당 1회 사용 후: 회색 dim + 우하단 "✓" 칩, 비활성.
    /// 아이콘은 Resources/InGame/HeadIcon/Skill/{nameEn}.png — 누락 시 ✦ 알약 폴백.
    /// </summary>
    private void DrawSummonSkillBadge(SummonInstance s, int summonIndex, DianoCard.Data.DinoSkillData skill,
                                       Rect summonRect, Rect summonHpRect, bool inReward)
    {
        if (_battle?.state == null) return;

        bool ready = _battle.CanUseSkill(summonIndex);
        bool onCooldown = !ready && skill.cooldownTurns > 0 && s.skillCooldownRemaining > 0;
        bool used = !ready && skill.isOnceBattle && s.skillUsedThisBattle;

        // 검+스킬 한 쌍을 공룡 정중앙에 정렬 — DrawSummon의 pairOffset과 동일 식.
        // 검 단독일 땐 정중앙, 둘 다 뜨면 검은 좌측 -24 / 스킬은 우측 +24.
        const float iconSize = 40f;
        const float swordSize = 40f;
        const float gap = 8f;
        Vector2 swordCenter = new Vector2(summonRect.center.x - (swordSize + gap) * 0.5f, summonRect.y - 12f);
        Vector2 iconCenter = new Vector2(swordCenter.x + (swordSize / 2f) + gap + (iconSize / 2f), swordCenter.y);
        var iconRect = new Rect(iconCenter.x - iconSize / 2f, iconCenter.y - iconSize / 2f, iconSize, iconSize);

        var icon = GetSkillIcon(skill);
        var prevColor = GUI.color;

        if (icon != null)
        {
            // not-ready(쿨다운/사용 완료)는 검 뱃지 dim과 동일하게 살짝 어둡게.
            if (!ready) GUI.color = new Color(0.50f, 0.50f, 0.52f, 1f);
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, alphaBlend: true);
            GUI.color = prevColor;
        }
        else
        {
            // 폴백 — Skill/{nameEn}.png 아직 미배치. 검은 사각 + ✦.
            FillRect(iconRect, ready ? new Color(0.10f, 0.40f, 0.40f, 0.85f)
                                     : new Color(0.05f, 0.05f, 0.07f, 0.95f));
            DrawBorder(iconRect, ready ? 2 : 1,
                       ready ? new Color(0.35f, 0.95f, 0.85f, 0.9f) : new Color(0.20f, 0.20f, 0.22f, 0.9f));
            var prevTextCol = _centerStyle.normal.textColor;
            int prevFontSize = _centerStyle.fontSize;
            _centerStyle.fontSize = 22;
            _centerStyle.normal.textColor = ready ? new Color(0.85f, 1f, 0.95f) : new Color(0.30f, 0.30f, 0.34f);
            GUI.Label(iconRect, "✦", _centerStyle);
            _centerStyle.normal.textColor = prevTextCol;
            _centerStyle.fontSize = prevFontSize;
        }

        // 데미지 숫자 — 검 뱃지와 동일 패턴으로 우하단(쿨다운 없을 때) 또는 좌하단(쿨다운 있을 때).
        if (skill.damage > 0)
        {
            const float dmgW = 22f, dmgH = 18f;
            bool hasCdChip = onCooldown || used;
            var dmgRect = hasCdChip
                ? new Rect(iconRect.x - dmgW * 0.3f, iconRect.yMax - dmgH * 0.7f, dmgW, dmgH)   // 좌하단
                : new Rect(iconRect.xMax - dmgW * 0.7f, iconRect.yMax - dmgH * 0.7f, dmgW, dmgH); // 우하단
            Color dmgCol = ready ? new Color(1f, 0.85f, 0.35f) : new Color(0.75f, 0.65f, 0.40f);
            DrawTextWithOutline(dmgRect, skill.damage.ToString(), _intentNumberStyle,
                                dmgCol, new Color(0f, 0f, 0f, 0.95f), 1.4f);
        }

        // 우하단 칩 — 쿨다운 N 또는 사용 완료 ✓. ATTACK 뱃지 숫자와 동일 톤.
        if (onCooldown || used)
        {
            const float chipW = 22f, chipH = 18f;
            var chipRect = new Rect(iconRect.xMax - chipW * 0.7f, iconRect.yMax - chipH * 0.7f, chipW, chipH);
            FillRect(new Rect(chipRect.x - 1f, chipRect.y - 1f, chipRect.width + 2f, chipRect.height + 2f),
                     new Color(0f, 0f, 0f, 0.55f));
            string label = used ? "✓" : s.skillCooldownRemaining.ToString();
            DrawTextWithOutline(chipRect, label, _intentNumberStyle,
                                Color.white, new Color(0f, 0f, 0f, 0.95f), 1.4f);
        }

        // 클릭 — 다른 타겟팅이 진행 중이면 무시. 발동 분기:
        //   - 이미 _targetingSummonSkillIndex가 이 공룡: 토글로 해제
        //   - target=ENEMY: _targetingSummonSkillIndex 세팅 (공격 타겟팅 해제)
        //   - target=ALL_ENEMY / SELF: 즉시 발동 (-1 = AOE/SELF)
        if (inReward) return;
        if (_battle.state.IsOver) return;
        if (!ready) return;
        if (_targetingCardIndex >= 0 || _swapFromCardIndex >= 0 || _reinforcePickerCardIndex >= 0) return;

        var ev = Event.current;
        if (ev == null) return;
        if (ev.type != EventType.MouseDown || ev.button != 0) return;
        // 클릭 영역은 시인성보다 살짝 크게 (검 뱃지와 동일 패턴).
        var hitRect = new Rect(iconRect.x - 6f, iconRect.y - 6f, iconRect.width + 12f, iconRect.height + 12f);
        if (!hitRect.Contains(ev.mousePosition)) return;
        ev.Use();

        // 같은 공룡 스킬 재클릭 → 타겟팅 해제
        if (_targetingSummonSkillIndex == summonIndex)
        {
            _targetingSummonSkillIndex = -1;
            return;
        }

        _targetingSummonIndex = -1; // 공격 타겟팅과 상호 배타
        if (skill.target == DianoCard.Data.TargetType.ENEMY)
        {
            _targetingSummonSkillIndex = summonIndex;
        }
        else
        {
            // AOE / SELF — 즉시 발동
            _targetingSummonSkillIndex = -1;
            var summon = s;
            _pending.Add(() => StartCoroutine(ManualSummonSkillCoroutine(summon, -1)));
        }
    }

    /// <summary>
    /// CANNIBAL 패시브(마준가) 동족포식 발동 뱃지. 스킬 뱃지와 동일 슬롯/스타일을 공유 — 시그니처 스킬 없는
    /// 공룡에게만 표시된다. 클릭 시 _cannibalFeedFromIndex 세팅 → 다른 아군 공룡 클릭으로 발동.
    /// 1턴 1회 사용 후 회색 dim + 우하단 ✓ 칩.
    /// </summary>
    private void DrawSummonCannibalBadge(SummonInstance s, int summonIndex, Rect summonRect, bool inReward)
    {
        if (_battle?.state == null) return;

        bool ready = _battle.CanFeedCannibal(summonIndex);
        bool used = !ready && s.cannibalUsedThisTurn;

        const float iconSize = 40f;
        const float swordSize = 40f;
        const float gap = 8f;
        Vector2 swordCenter = new Vector2(summonRect.center.x - (swordSize + gap) * 0.5f, summonRect.y - 12f);
        Vector2 iconCenter = new Vector2(swordCenter.x + (swordSize / 2f) + gap + (iconSize / 2f), swordCenter.y);
        var iconRect = new Rect(iconCenter.x - iconSize / 2f, iconCenter.y - iconSize / 2f, iconSize, iconSize);

        var prevColor = GUI.color;

        // READY 펄스 글로우 — 깊은 적색 톤(스킬은 청록).
        if (ready)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 4f);
            float glowSize = iconSize * 1.55f;
            var glowRect = new Rect(iconCenter.x - glowSize / 2f, iconCenter.y - glowSize / 2f, glowSize, glowSize);
            FillRect(glowRect, new Color(0.55f, 0.10f, 0.10f, 0.18f * pulse));
        }

        // 본체 — 크림슨 사각 + 어두운 보더 + 단일 한자 "牙"(이빨/송곳니).
        FillRect(iconRect, ready ? new Color(0.45f, 0.10f, 0.12f, 0.92f)
                                 : new Color(0.18f, 0.10f, 0.10f, 0.85f));
        DrawBorder(iconRect, ready ? 2 : 1,
                   ready ? new Color(0.95f, 0.55f, 0.45f, 0.95f) : new Color(0.40f, 0.30f, 0.30f, 0.85f));

        var prevTextCol = _centerStyle.normal.textColor;
        int prevFontSize = _centerStyle.fontSize;
        _centerStyle.fontSize = 22;
        _centerStyle.normal.textColor = ready ? new Color(1f, 0.92f, 0.85f) : new Color(0.65f, 0.55f, 0.55f);
        GUI.Label(iconRect, "牙", _centerStyle);
        _centerStyle.normal.textColor = prevTextCol;
        _centerStyle.fontSize = prevFontSize;

        GUI.color = prevColor;

        // 우하단 칩 — 사용 완료 ✓.
        if (used)
        {
            const float chipW = 22f, chipH = 18f;
            var chipRect = new Rect(iconRect.xMax - chipW * 0.7f, iconRect.yMax - chipH * 0.7f, chipW, chipH);
            FillRect(new Rect(chipRect.x - 1f, chipRect.y - 1f, chipRect.width + 2f, chipRect.height + 2f),
                     new Color(0f, 0f, 0f, 0.55f));
            DrawTextWithOutline(chipRect, "✓", _intentNumberStyle,
                                Color.white, new Color(0f, 0f, 0f, 0.95f), 1.4f);
        }

        // 클릭 — 다른 타겟팅 진행 중이면 무시.
        if (inReward) return;
        if (_battle.state.IsOver) return;
        if (!ready) return;
        if (_targetingCardIndex >= 0 || _swapFromCardIndex >= 0
            || _targetingSummonSkillIndex >= 0 || _targetingPotionIndex >= 0
            || _reinforcePickerCardIndex >= 0) return;

        var ev = Event.current;
        if (ev == null) return;
        if (ev.type != EventType.MouseDown || ev.button != 0) return;
        var hitRect = new Rect(iconRect.x - 6f, iconRect.y - 6f, iconRect.width + 12f, iconRect.height + 12f);
        if (!hitRect.Contains(ev.mousePosition)) return;
        ev.Use();

        // 동일 뱃지 재클릭 → 모드 토글 해제
        if (_cannibalFeedFromIndex == summonIndex)
        {
            _cannibalFeedFromIndex = -1;
            return;
        }

        // 모드 진입 — 다른 모드와 상호 배타.
        _targetingSummonIndex = -1;
        _targetingSummonSkillIndex = -1;
        _cannibalFeedFromIndex = summonIndex;
    }

    private void DrawEnemy(EnemyInstance e, int enemyIndex, Vector2 center)
    {
        float h = GetEnemyDrawHeight(e);
        float w = h; // 정사각 rect — 스프라이트는 ScaleToFit으로 aspect 유지
        var rect = new Rect(center.x - w / 2, center.y - h / 2, w, h);

        // 적 애니메이션 뷰는 world-space BattleEntityView가 그림. IMGUI는 HP/intent만.
        EnsureEnemyView(e);
        bool hasView = _enemyViews.TryGetValue(e, out var view);
        if (hasView)
        {
            if (Camera.main != null)
            {
                Vector2 feetGui = new Vector2(center.x, rect.yMax);
                Vector3 feetWorld = GuiToWorld(feetGui);
                Vector3 topWorld  = GuiToWorld(new Vector2(center.x, rect.y));
                float worldHeight = Mathf.Abs(feetWorld.y - topWorld.y);
                view.SetBasePosition(feetWorld);
                view.SetWorldHeight(worldHeight);
                Vector2 shadowOffset = new Vector2(_enemyShadowOffsetX, _enemyShadowOffsetY) * worldHeight;
                view.UpdateShadowParams(_enemyShadowHeight, _enemyShadowWidthScale, shadowOffset, _enemyShadowAlpha);
            }
        }
        else if (_enemySprites.TryGetValue(e.data.id, out var tex) && tex.height > 0)
        {
            float aspect = tex.width / (float)tex.height;
            var drawRect = ComputeBottomAnchoredDrawRect(rect, aspect);
            GUI.DrawTexture(drawRect, tex, ScaleMode.StretchToFill, alphaBlend: true);
        }
        else
        {
            Color col = e.data.enemyType switch
            {
                EnemyType.BOSS => new Color(0.75f, 0.15f, 0.15f, 0.88f),
                EnemyType.ELITE => new Color(0.8f, 0.45f, 0.1f, 0.88f),
                _ => new Color(0.55f, 0.25f, 0.25f, 0.88f),
            };
            FillRect(rect, col);
            DrawBorder(rect, 2, Color.black);
            GUI.Label(new Rect(rect.x, rect.y + h / 2 - 10, rect.width, 22),
                      e.data.nameKr, _centerStyle);
        }

        // intent 앵커 — 검 아이콘(56px) + 타겟 힌트 박스(~22px)가 스프라이트 위로 완전히 올라가도록 충분히 띄움.
        // 이끼 잡몹은 보호막용이라 의미 있는 intent가 거의 없음 → "▲ —" 더미 아이콘이 4개 떠다녀서 시각 오염. 숨김.
        if (!e.isMoss)
        {
            DrawEnemyIntent(new Vector2(rect.center.x, rect.y - 44), e);

            // hover 영역: 인텐트 아이콘 박스 영역만 — 타겟팅·조준 중에 적 본체에 마우스가 닿아도
            // 정보 패널이 안 떠서 시야를 가리지 않게. 의도적으로 인텐트 아이콘 위에 마우스를 올렸을 때만 노출.
            var ev = Event.current;
            if (ev != null)
            {
                var iconHoverArea = new Rect(rect.center.x - 36f, rect.y - 70f, 72f, 56f);
                if (iconHoverArea.Contains(ev.mousePosition))
                    DrawEnemyTooltip(rect, e);
            }
        }

        // 아트 없는 placeholder 적은 가운데에 이름 라벨 (식별용)
        if (string.IsNullOrEmpty(e.data.image))
        {
            GUI.Label(new Rect(rect.x, rect.center.y - 11, rect.width, 22),
                      e.data.nameKr, _centerStyle);
        }

        // 방어막 FX — 적이 BLOCK intent를 수행해 block이 증가한 직후 트리거.
        // 보호 중인 이끼는 sprite가 작아 FX가 과해 보이지 않도록 width/height 그대로 사용.
        DrawEntityShieldFx(e, new Vector2(rect.center.x, rect.center.y), rect.width, rect.height);

        // 이끼 잡몹은 본체 적보다 작으니 HP바도 비례 축소 — min clamp 우회 + 두께도 얇게.
        float enemyBarW = e.isMoss ? rect.width * 0.65f : ComputeHpBarWidth(rect.width);
        float enemyBarH = e.isMoss ? 8f : hpBarHeight;
        // 스프라이트별 시각 무게중심이 캔버스 중앙과 다를 때 hp_bar_anchor_x로 보정 (default 0.5).
        float anchorX = e.data.hpBarAnchorX > 0.001f ? Mathf.Clamp01(e.data.hpBarAnchorX) : 0.5f;
        float enemyBarCenterX = rect.x + rect.width * anchorX;
        var enemyHpRect = new Rect(enemyBarCenterX - enemyBarW / 2, rect.yMax + 4f, enemyBarW, enemyBarH);

        // 이끼 보호막 활성 여부 — isBossProtected + 이끼 1체 이상 생존. true면 HP바 회색 + 살아있는 이끼 수가 적힌 방패 아이콘 표시.
        int mossAliveCount = 0;
        if (e.isBossProtected && _battle != null && _battle.state != null)
        {
            foreach (var x in _battle.state.enemies)
                if (!x.IsDead && x.isMoss) mossAliveCount++;
        }
        bool mossShielded = mossAliveCount > 0;

        Color hpFill = mossShielded
            ? new Color(0.45f, 0.45f, 0.50f)   // 차콜 그레이 — "지금은 데미지 안 들어감"
            : new Color(0.65f, 0.16f, 0.18f);
        DrawHpBar(enemyHpRect, e.hp, e.maxHp, hpFill, entity: e);

        // moss 보호막은 HP 바 회색 틴트로만 표시 — 인라인 MOSS_LEAF 뱃지는 HP 바 아래 패시브 칩과 겹쳐 제거.
        if (!mossShielded && e.block > 0)
        {
            DrawBlockBadge(new Vector2(enemyHpRect.x, enemyHpRect.center.y), e.block, 40f,
                           HeadIcon("DEFEND"));
        }

        // 패시브 + 누적 STRENGTH — HP 바 바로 아래 한 줄.
        // X/width를 HP 바에 맞춰 정렬 — 플레이어/소환수와 동일하게 칩이 HP 바 좌측 끝에서 시작.
        DrawEnemyPassives(new Rect(enemyHpRect.x, enemyHpRect.yMax + 4f, enemyHpRect.width, 26f), e);

        // 타겟팅 모드: 발치 둥근 글로우 + 클릭 처리 — 적을 대상으로 하는 카드일 때만
        if (_targetingCardIndex >= 0
            && _targetingCardIndex < _battle.state.hand.Count
            && CardNeedsEnemyTarget(_battle.state.hand[_targetingCardIndex].data))
        {
            var ev = Event.current;
            bool hovered = rect.Contains(ev.mousePosition);
            DrawTargetFootGlow(rect, hovered);
            _arrowTargetRects.Add(rect);

            // 호버 시 카드 실효 데미지를 프리뷰 — 약화/취약/카드별 특수 산식(C124/C131)을 반영.
            if (hovered)
                ComputeCardAttackPreview(_battle.state.hand[_targetingCardIndex].data, e);

            if (ev.type == EventType.MouseDown && ev.button == 0 && hovered)
            {
                ev.Use();
                int cardIdx = _targetingCardIndex;
                int eIdx = enemyIndex;
                _targetingCardIndex = -1;
                // SFX는 카드 클릭 즉시 재생 — PlayCard가 화염구 임팩트 시점까지 지연되므로
                // ResolveCard에서 트리거하면 사운드가 늦게 들림.
                DianoCard.Audio.AudioManager.Instance?.PlaySFX("card_attack");
                _pending.Add(() => {
                    // 모션과 화염구는 즉시 시작. PlayCard(데미지/마나/상태)는 화염구 임팩트 시점까지 지연.
                    _playerView?.PlayAttack(ComputeAttackDir(eIdx), distance: 0.08f, duration: PlayerAttackDuration);
                    TriggerPlayerAttackFx(eIdx, attackDuration: PlayerAttackDuration);
                    StartCoroutine(DelayedPlayCardOnImpact(() => _battle.PlayCard(cardIdx, eIdx)));
                });
            }
        }
        // 소환수 타겟팅 모드: 선택된 공룡이 이 적을 공격
        else if (_targetingSummonIndex >= 0)
        {
            var ev = Event.current;
            bool hovered = rect.Contains(ev.mousePosition);
            DrawTargetFootGlow(rect, hovered);
            _arrowTargetRects.Add(rect);

            // 호버 시 실효 데미지 계산 — 약화/기습/취약을 CommandSummonAttack과 동일한 순서로 반영해
            // 적별로 다른 1.5배 적용 여부가 그대로 노출된다.
            if (hovered)
            {
                int sIdx = _targetingSummonIndex;
                if (sIdx >= 0 && sIdx < _battle.state.field.Count)
                    ComputeAttackPreview(_battle.state.field[sIdx], e);
            }

            if (ev.type == EventType.MouseDown && ev.button == 0 && hovered)
            {
                ev.Use();
                int sIdx = _targetingSummonIndex;
                int eIdx = enemyIndex;
                var summon = (sIdx >= 0 && sIdx < _battle.state.field.Count) ? _battle.state.field[sIdx] : null;
                _targetingSummonIndex = -1;
                _pending.Add(() => StartCoroutine(ManualSummonAttackCoroutine(summon, eIdx)));
            }
        }
        // 스킬 타겟팅 모드 (target=ENEMY 스킬): 선택된 공룡이 이 적에게 스킬 시전
        else if (_targetingSummonSkillIndex >= 0)
        {
            var ev = Event.current;
            bool hovered = rect.Contains(ev.mousePosition);
            DrawTargetFootGlow(rect, hovered);
            _arrowTargetRects.Add(rect);

            if (ev.type == EventType.MouseDown && ev.button == 0 && hovered)
            {
                ev.Use();
                int sIdx = _targetingSummonSkillIndex;
                int eIdx = enemyIndex;
                var summon = (sIdx >= 0 && sIdx < _battle.state.field.Count) ? _battle.state.field[sIdx] : null;
                _targetingSummonSkillIndex = -1;
                _pending.Add(() => StartCoroutine(ManualSummonSkillCoroutine(summon, eIdx)));
            }
        }
        // 포션 타겟팅 모드 (target=ENEMY 포션): 선택된 포션이 이 적에게 적용
        // 공격 카드와 동일한 톤 — 박스 링 대신 발치 글로우 + 상단 바 포션 아이콘에서 뻗는 화살표.
        else if (_targetingPotionIndex >= 0 && CurrentPotionTargetsEnemy())
        {
            var ev = Event.current;
            bool hovered = rect.Contains(ev.mousePosition);
            DrawTargetFootGlow(rect, hovered);
            _arrowTargetRects.Add(rect);

            if (ev.type == EventType.MouseDown && ev.button == 0 && hovered)
            {
                ev.Use();
                int slotIdx = _targetingPotionIndex;
                int eIdx = enemyIndex;
                _targetingPotionIndex = -1;
                _pending.Add(() => _battle.UsePotion(slotIdx, eIdx));
            }
        }
    }

    /// <summary>현재 타겟팅 중인 포션의 target이 ENEMY인가? (DrawEnemy의 분기 조건).</summary>
    private bool CurrentPotionTargetsEnemy()
    {
        var run = GameStateManager.Instance?.CurrentRun;
        if (run == null || _targetingPotionIndex < 0 || _targetingPotionIndex >= run.potions.Count) return false;
        var p = run.potions[_targetingPotionIndex];
        return p != null && p.target == DianoCard.Data.TargetType.ENEMY;
    }

    /// <summary>현재 타겟팅 중인 포션의 target이 ALLY인가? (DrawSummon의 분기 조건).</summary>
    private bool CurrentPotionTargetsAlly()
    {
        var run = GameStateManager.Instance?.CurrentRun;
        if (run == null || _targetingPotionIndex < 0 || _targetingPotionIndex >= run.potions.Count) return false;
        var p = run.potions[_targetingPotionIndex];
        return p != null && p.target == DianoCard.Data.TargetType.ALLY;
    }

    /// <summary>수동 소환수 공격 — lunge 애니메이션 후 데미지 적용.</summary>
    private IEnumerator ManualSummonAttackCoroutine(SummonInstance summon, int enemyIndex)
    {
        if (summon == null || _battle?.state == null) yield break;
        if (!summon.CanAttack) yield break;
        int currentIdx = _battle.state.field.IndexOf(summon);
        if (currentIdx < 0) yield break;
        DianoCard.Audio.AudioManager.Instance?.PlaySFX("attack");
        yield return AnimateLunge(summon, isSummon: true);
        _battle.CommandSummonAttack(currentIdx, enemyIndex);
    }

    /// <summary>수동 소환수 스킬 — lunge 애니메이션 후 스킬 발동. enemyIndex는 ENEMY 타겟에서만 사용 (-1 = AOE/SELF).
    /// 다타격 스킬(연격 등)은 lunge → 해당 hit 데미지 → 다음 lunge 순으로 끊어서 보여준다.</summary>
    private IEnumerator ManualSummonSkillCoroutine(SummonInstance summon, int enemyIndex)
    {
        if (summon == null || _battle?.state == null) yield break;
        int currentIdx = _battle.state.field.IndexOf(summon);
        if (currentIdx < 0) yield break;
        if (!_battle.CanUseSkill(currentIdx)) yield break;

        var ctx = _battle.BeginSummonSkill(currentIdx, enemyIndex);
        if (ctx == null) yield break;

        int hits = (ctx.skill.damage > 0 && ctx.damageTargets.Count > 0) ? ctx.skill.hits : 0;
        if (hits > 0)
        {
            for (int i = 0; i < hits; i++)
            {
                yield return AnimateLunge(summon, isSummon: true);
                _battle.ApplySummonSkillHit(ctx);
            }
        }
        else
        {
            // 데미지 없는 스킬도 한 번은 모션을 보여준다.
            yield return AnimateLunge(summon, isSummon: true);
        }
        _battle.EndSummonSkill(ctx);
    }

    // 타겟팅 모드에서 선택된 카드 외곽에 부드럽게 빛나는 글로우.
    // 단단한 노란 외곽선 대신 여러 겹의 옅은 보더가 바깥으로 퍼지며 펄스.
    // 융합 재료 A로 선택된 카드/공룡 마커 — 부드러운 시안 헤일로.
    // 후보(보라)와 색을 분리해 "고른 것" vs "고를 수 있는 것"을 한눈에 구분.
    // 라벨은 ATK 뱃지·코스트 원과 충돌해서 생략 — 색 차이만으로 충분히 도드라짐.
    private void DrawFusionSelectedMarker(Rect rect)
    {
        EnsureArrowDotTexture();
        if (_arrowDotTex == null) return;

        float pulse = 0.55f + 0.45f * Mathf.Sin(Time.time * 4f);
        Color tint = new Color(0.55f, 1f, 0.95f);

        var prev = GUI.color;
        // 외곽 wide bloom
        float padOuter = 32f;
        float aOuter = 0.55f * pulse;
        GUI.color = new Color(tint.r, tint.g, tint.b, aOuter);
        var rOuter = new Rect(rect.x - padOuter, rect.y - padOuter,
                              rect.width + padOuter * 2f, rect.height + padOuter * 2f);
        GUI.DrawTexture(rOuter, _arrowDotTex, ScaleMode.StretchToFill, alphaBlend: true);

        // 안쪽 tight halo — 더 진하게.
        float padInner = 12f;
        float aInner = 0.95f * pulse;
        GUI.color = new Color(tint.r, tint.g, tint.b, aInner);
        var rInner = new Rect(rect.x - padInner, rect.y - padInner,
                              rect.width + padInner * 2f, rect.height + padInner * 2f);
        GUI.DrawTexture(rInner, _arrowDotTex, ScaleMode.StretchToFill, alphaBlend: true);
        GUI.color = prev;
    }

    // StS 스타일 타겟팅 화살표 — 카드/공룡(source) → 마우스(또는 스냅된 타겟) 사이를 cubic 베지어로 잇는다.
    // 곡선 위에 점점 커지는 부드러운 동그라미를 찍고 끝에 V자 화살촉을 그림.
    // 융합도 같은 화살표를 사용 — A 미선택이면 촉매 카드, A 선택 후엔 A에서 출발.
    private void DrawTargetingArrow(BattleState state)
    {
        if (!_arrowSourceValid || state == null) return;

        // 출발 — 카드/공룡 rect의 상단보다 약간 안쪽. 회전된 카드라도 center.x는 회전 피벗이라 안정적.
        Vector2 from = new Vector2(_arrowSourceRect.center.x, _arrowSourceRect.y + 12f);

        // 끝점 — 호버된 valid 타겟이 있으면 중심에 스냅, 없으면 마우스를 따라간다.
        Vector2 mouse = Event.current != null ? Event.current.mousePosition : from;
        bool snapped = false;
        Vector2 to = mouse;
        for (int i = 0; i < _arrowTargetRects.Count; i++)
        {
            var tr = _arrowTargetRects[i];
            if (tr.Contains(mouse))
            {
                to = tr.center;
                snapped = true;
                break;
            }
        }

        // 마우스가 카드 위에 머물러 화살표 길이가 거의 0일 때는 그리지 않는다 — 자기 자신 가리키는 모양 방지.
        if (!snapped && Vector2.Distance(from, to) < 36f) return;

        EnsureArrowDotTexture();
        DrawBezierArrow(from, to, snapped);

        // 공룡 공격 타겟팅에서만 — 화살표 끝(V) 바로 위에 실효 데미지 숫자를 띄운다.
        // 아이콘 없이 진한 빨간 큰 숫자 + 두꺼운 검은 외곽선만 — 다른 ATK 뱃지/인텐트와 즉각 구분.
        if (snapped && _attackPreviewEnemy != null && _attackPreviewDamage > 0)
        {
            Vector2 chipCenter = new Vector2(to.x, to.y - 36f);
            DrawAttackPreviewNumber(chipCenter, _attackPreviewDamage);
        }
    }

    // 공룡 공격 타겟팅 프리뷰 — 아이콘 없는 빨간 숫자. 화살표 끝 위에 떠서 "여기 이만큼 박힘"을 즉시 전달.
    private void DrawAttackPreviewNumber(Vector2 center, int value)
    {
        int prevSize = _intentNumberStyle.fontSize;
        _intentNumberStyle.fontSize = 36;
        const float w = 90f;
        const float h = 48f;
        var r = new Rect(center.x - w * 0.5f, center.y - h * 0.5f, w, h);
        // 진한 적색(#FF3320) + 두꺼운 흑 외곽선 — 주황 화살표/흰 ATK 뱃지/노란 인텐트와 모두 구분.
        Color textCol = new Color(1.00f, 0.20f, 0.12f, 1f);
        DrawTextWithOutline(r, value.ToString(), _intentNumberStyle,
                            textCol, new Color(0f, 0f, 0f, 1f), 3f);
        _intentNumberStyle.fontSize = prevSize;
    }

    // CommandSummonAttack의 데미지 산식을 미러링 — TotalAttack → 플레이어 약화(*0.75 floor, min1)
    // → AMBUSH 첫 공격 *2 → 적 취약(*1.5 round) 순서. 칩 자체 색은 통일이라 숫자만 갱신.
    private void ComputeAttackPreview(SummonInstance summon, EnemyInstance target)
    {
        if (summon == null || target == null || _battle?.state?.player == null) return;
        int dmg = summon.TotalAttack;
        if (_battle.state.player.weakTurns > 0)
            dmg = Mathf.Max(1, (int)(dmg * 0.75f));
        bool ambush = summon.data != null
            && summon.data.passiveType == DinoPassiveType.AMBUSH
            && !summon.passiveConsumed;
        if (ambush) dmg *= 2;
        if (target.vulnerableTurns > 0)
            dmg = Mathf.RoundToInt(dmg * 1.5f);
        _attackPreviewEnemy = target;
        _attackPreviewDamage = dmg;
    }

    // ResolveMagic(ATTACK 서브타입)의 데미지 산식 미러링 — c.value → 카드별 특수(C124 처형/C131 합산)
    // → ApplyPlayerWeak → 적 취약(*1.5 round). DEBUFF 카드는 대부분 비데미지라 0이면 칩 미노출.
    private void ComputeCardAttackPreview(CardData c, EnemyInstance target)
    {
        if (c == null || target == null || _battle?.state?.player == null) return;
        int baseDmg = c.value;
        if (c.id == "C124")
        {
            if (!target.IsDead && target.hp * 2 <= target.maxHp) baseDmg = 10;
        }
        else if (c.id == "C131")
        {
            int sum = 0;
            foreach (var s in _battle.state.field) if (!s.IsDead) sum += s.TotalAttack;
            baseDmg = sum;
        }
        if (baseDmg <= 0) return;
        int dmg = baseDmg;
        if (_battle.state.player.weakTurns > 0)
            dmg = Mathf.Max(1, (int)(dmg * 0.75f));
        if (target.vulnerableTurns > 0)
            dmg = Mathf.RoundToInt(dmg * 1.5f);
        _attackPreviewEnemy = target;
        _attackPreviewDamage = dmg;
    }

    // 부드러운 원형 알파 마스크 텍스처 — 가장자리로 갈수록 부드럽게 사라지도록 (1-d)^2.
    private void EnsureArrowDotTexture()
    {
        if (_arrowDotTex != null) return;
        const int S = 64;
        _arrowDotTex = new Texture2D(S, S, TextureFormat.RGBA32, false, false)
        {
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        var px = new Color32[S * S];
        Vector2 c = new Vector2((S - 1) * 0.5f, (S - 1) * 0.5f);
        float maxR = (S - 1) * 0.5f;
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float d = Vector2.Distance(new Vector2(x, y), c) / maxR;
            float a = Mathf.Clamp01(1f - d);
            a = a * a;
            px[y * S + x] = new Color(1f, 1f, 1f, a);
        }
        _arrowDotTex.SetPixels32(px);
        _arrowDotTex.Apply(false, false);
    }

    private void DrawBezierArrow(Vector2 from, Vector2 to, bool snapped)
    {
        // 컨트롤 포인트: source에서 위로 들어올린 뒤 target 위쪽에서 내려오도록 잡음 → 카드에서 솟아오르는 호.
        float dist = Vector2.Distance(from, to);
        float lift = Mathf.Min(240f, dist * 0.55f);
        Vector2 c1 = new Vector2(from.x, from.y - lift * 0.95f);
        Vector2 c2 = new Vector2(to.x, to.y - lift * 0.55f);

        // 두 가지 컬러: 외부 헤일로(어두운 적색)와 내부 코어(밝은 옐로우-오렌지) — STS 톤.
        // 헤일로가 검은 외곽선처럼 카드/배경 어디에서나 곡선을 분리시켜 가독성 확보.
        // 펄스는 살짝만 — 항상 또렷이 보이도록.
        float pulse = 0.88f + 0.12f * Mathf.Sin(Time.time * 5f);
        Color halo = snapped
            ? new Color(0.55f, 0.05f, 0.02f, 1f)   // 스냅: 짙은 진홍 외곽
            : new Color(0.40f, 0.10f, 0.03f, 1f);  // 평소: 짙은 적갈 외곽
        Color core = snapped
            ? new Color(1.00f, 0.55f, 0.20f, 1f)   // 스냅: 강렬한 오렌지 — "박힌다"
            : new Color(1.00f, 0.85f, 0.35f, 1f);  // 평소: 따뜻한 골든

        // 충분히 촘촘하게 찍어 점이 아닌 두꺼운 연속 리본으로 보이게 (간격 < 점 반지름).
        const int N = 56;
        var prevColor = GUI.color;

        // Pass 1 — 어두운 외곽 헤일로 (큰 점, 낮은 알파). 카드/배경에서 곡선을 분리.
        for (int i = 0; i < N; i++)
        {
            float t = i / (float)(N - 1);
            Vector2 p = CubicBezier(from, c1, c2, to, t);
            float size = Mathf.Lerp(22f, 44f, t);  // 시작 22 → 끝 44 (두꺼운 리본 폭)
            float a = Mathf.Lerp(0.55f, 0.95f, t) * pulse;
            GUI.color = new Color(halo.r, halo.g, halo.b, a);
            GUI.DrawTexture(
                new Rect(p.x - size * 0.5f, p.y - size * 0.5f, size, size),
                _arrowDotTex, ScaleMode.StretchToFill, alphaBlend: true);
        }

        // Pass 2 — 밝은 코어 (헤일로보다 약 절반 크기, 높은 알파). 곡선 본체.
        for (int i = 0; i < N; i++)
        {
            float t = i / (float)(N - 1);
            Vector2 p = CubicBezier(from, c1, c2, to, t);
            float size = Mathf.Lerp(11f, 24f, t);
            float a = Mathf.Lerp(0.85f, 1.00f, t) * pulse;
            GUI.color = new Color(core.r, core.g, core.b, a);
            GUI.DrawTexture(
                new Rect(p.x - size * 0.5f, p.y - size * 0.5f, size, size),
                _arrowDotTex, ScaleMode.StretchToFill, alphaBlend: true);
        }

        GUI.color = prevColor;

        // 화살촉 — 끝점 접선 기준 V자. 큰 헤일로 깃 → 작은 코어 깃 두 패스로 두꺼운 외곽선 효과.
        Vector2 tangent = CubicBezierTangent(from, c1, c2, to, 1f);
        float angleDeg = Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg;
        float headLen = snapped ? 44f : 38f;
        DrawArrowHead(to, angleDeg, headLen + 6f, 12f, new Color(halo.r, halo.g, halo.b, pulse));
        DrawArrowHead(to, angleDeg, headLen,      7f,  new Color(core.r, core.g, core.b, pulse));
    }

    // 끝점 to에서 진행 방향(angleDeg) 반대쪽으로 ±32° 벌어진 두 짧은 막대를 그려 V자 화살촉을 만든다.
    // 막대는 1×1 불투명 화이트 텍스처를 회전된 직사각형으로 늘려 sharp한 라인을 만든다.
    private void DrawArrowHead(Vector2 tip, float angleDeg, float length, float thickness, Color color)
    {
        Matrix4x4 baseMat = GUI.matrix;
        var prevColor = GUI.color;
        GUI.color = color;

        // 깃은 tip에서 뒤쪽으로 뻗는다 → 진행 방향 반대(+180) 기준 ±32°.
        float[] flutes = { angleDeg + 180f - 32f, angleDeg + 180f + 32f };
        foreach (float a in flutes)
        {
            GUI.matrix = baseMat * RotateAroundPivotMatrix(a, tip);
            // 회전 전 좌표계에서 tip → tip + (length, 0) 방향의 가는 막대.
            var r = new Rect(tip.x, tip.y - thickness * 0.5f, length, thickness);
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, alphaBlend: true);
        }

        GUI.matrix = baseMat;
        GUI.color = prevColor;

        // 끝점에 약간 큰 부드러운 글로우 — 화살촉이 "박히는" 임팩트.
        if (_arrowDotTex != null)
        {
            float glow = length * 1.3f;
            GUI.color = new Color(color.r, color.g, color.b, color.a * 0.55f);
            GUI.DrawTexture(
                new Rect(tip.x - glow * 0.5f, tip.y - glow * 0.5f, glow, glow),
                _arrowDotTex, ScaleMode.StretchToFill, alphaBlend: true);
            GUI.color = prevColor;
        }
    }

    private static Vector2 CubicBezier(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float t)
    {
        float u = 1f - t;
        return u * u * u * a
             + 3f * u * u * t * b
             + 3f * u * t * t * c
             + t * t * t * d;
    }

    private static Vector2 CubicBezierTangent(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float t)
    {
        float u = 1f - t;
        return 3f * u * u * (b - a)
             + 6f * u * t * (c - b)
             + 3f * t * t * (d - c);
    }

    private void DrawTargetFootGlow(Rect enemyRect, bool hovered)
    {
        if (_manaFrameTexture == null) return;

        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 4f);
        float glowW = enemyRect.width * 0.95f;
        float glowH = enemyRect.width * 0.32f;
        float cx = enemyRect.center.x;
        float cy = enemyRect.yMax - glowH * 0.45f;

        var prevColor = GUI.color;

        // 1) 외부 soft halo
        {
            float w = glowW * 1.5f;
            float h = glowH * 1.5f;
            var r = new Rect(cx - w * 0.5f, cy - h * 0.5f, w, h);
            float a = (hovered ? 0.42f : 0.22f) * (0.7f + 0.3f * pulse);
            GUI.color = new Color(1f, 0.50f, 0.32f, a);
            GUI.DrawTexture(r, _manaFrameTexture, ScaleMode.StretchToFill, alphaBlend: true);
        }

        // 2) 내부 메인 글로우
        {
            var r = new Rect(cx - glowW * 0.5f, cy - glowH * 0.5f, glowW, glowH);
            float a = (hovered ? 0.78f : 0.48f) * (0.78f + 0.22f * pulse);
            GUI.color = new Color(1f, 0.32f, 0.22f, a);
            GUI.DrawTexture(r, _manaFrameTexture, ScaleMode.StretchToFill, alphaBlend: true);
        }

        GUI.color = prevColor;
    }

    private void DrawHpBar(Rect rect, int curr, int max, Color fill, bool blueTint = false, float blueTintStart = -1f, object entity = null)
    {
        // 블록이 살아있는 동안 fill 색을 파란 톤으로 유지. 시작 직후 짧게 더 강한 페이드 인.
        if (blueTint)
        {
            float intensity = 0.85f;
            if (blueTintStart >= 0f)
            {
                const float fadeIn = 0.18f;
                float ft = Time.time - blueTintStart;
                if (ft >= 0f && ft < fadeIn)
                    intensity = Mathf.Lerp(0f, 0.85f, ft / fadeIn);
            }
            var blue = new Color(0.30f, 0.62f, 1f);
            fill = Color.Lerp(fill, blue, intensity);
        }

        float realFrac = max > 0 ? Mathf.Clamp01((float)curr / max) : 0f;

        // entity reference 기반 키 — 슬라이드/레이아웃 재계산으로 rect 위치가 바뀌어도 키가 흔들리지 않는다.
        // entity가 null이면 Vector2 위치 폴백 (backwards-compat).
        object trackerKey = entity ?? (object)new Vector2(rect.x, rect.y);
        if (!_hpBarDisplayedFrac.TryGetValue(trackerKey, out float displayed))
            displayed = realFrac;

        if (Event.current.type == EventType.Repaint)
        {
            if (realFrac < displayed)
                displayed = Mathf.MoveTowards(displayed, realFrac, Time.unscaledDeltaTime * 0.85f);
            else
                displayed = realFrac; // 힐은 즉시
            _hpBarDisplayedFrac[trackerKey] = displayed;
        }

        bool isThin = rect.height < 12f;
        int gradSteps = isThin ? 4 : 10;

        // 1) 배경 인셋 — 잉크 차콜 위에 위→아래로 부드럽게 사라지는 어두운 베일 + 아래쪽 미세 온기.
        FillRect(rect, new Color(0.07f, 0.06f, 0.08f, 0.90f));
        if (!isThin)
        {
            FillVerticalGradient(rect,
                                 new Color(0f, 0f, 0f, 0.26f),
                                 new Color(0.20f, 0.15f, 0.10f, 0.06f),
                                 gradSteps);
        }

        // 2) 딜레이 트레일 — 실제 hp 구간 ~ displayed 구간 사이에만 머티드 잔상
        if (displayed > realFrac)
        {
            float trailStartX = rect.x + rect.width * realFrac;
            float trailWidth = rect.width * (displayed - realFrac);
            FillRect(new Rect(trailStartX, rect.y, trailWidth, rect.height),
                     new Color(0.78f, 0.62f, 0.30f, 0.72f));
        }

        // 3) 본 HP 채움 — 평평한 fill 위로 위쪽 하이라이트 / 아래쪽 섀도가 알파 페이드로 자연스럽게 녹아듦.
        if (realFrac > 0f)
        {
            var fillRect = new Rect(rect.x, rect.y, rect.width * realFrac, rect.height);
            FillRect(fillRect, fill);

            // 상단 글로스 — fill에 흰빛 섞은 톤이 위(가장 진함) → 아래(투명)로 점진 페이드.
            var glossColor = Color.Lerp(fill, Color.white, 0.38f);
            FillVerticalGradient(fillRect,
                                 new Color(glossColor.r, glossColor.g, glossColor.b, isThin ? 0.30f : 0.40f),
                                 new Color(glossColor.r, glossColor.g, glossColor.b, 0f),
                                 gradSteps);

            // 하단 섀도 — 투명(위) → 검정(아래) 페이드. 두꺼운 바에서만.
            if (!isThin)
            {
                FillVerticalGradient(fillRect,
                                     new Color(0f, 0f, 0f, 0f),
                                     new Color(0f, 0f, 0f, 0.48f),
                                     gradSteps);
            }

            // 리딩 엣지 — 채움 오른쪽 끝(아직 비어있을 때만)에 밝은 슬라이스, 액체의 wet edge 느낌.
            if (realFrac < 0.999f)
            {
                float edgeW = isThin ? 1.5f : 2.5f;
                float edgeX = fillRect.xMax - edgeW;
                var edgeColor = Color.Lerp(fill, Color.white, 0.55f);
                edgeColor.a = 0.55f;
                FillRect(new Rect(edgeX, fillRect.y, edgeW, fillRect.height), edgeColor);
            }
        }

        // 4) 저체력 펄스 — 30% 이하일 때 빨간 발광이 숨쉬듯 박동
        if (realFrac > 0f && realFrac < 0.3f)
        {
            float pulse = (Mathf.Sin(Time.time * 4.5f) + 1f) * 0.5f;
            float alpha = Mathf.Lerp(0.14f, 0.36f, pulse) * (1f - realFrac / 0.3f);
            FillRect(rect, new Color(0.85f, 0.18f, 0.20f, alpha));
        }

        // 5) 머티드 차콜 외곽 프레임 + 내부 암색 인셋 라인 — 배경(보라+석조)에 묻히도록 톤 다운.
        //    바 두께(rect.height)에 비례해 보더 두께도 스케일 — 작은 이끼바(8px)에서 1px 보더가 과해 보이는 문제 해소.
        float borderW = Mathf.Max(0.5f, rect.height / 18f); // 18px 기준 1px, 작아지면 비례 축소(최소 0.5)
        DrawBorder(rect, borderW, new Color(0.18f, 0.14f, 0.18f, 0.92f));
        var innerRect = new Rect(rect.x + borderW, rect.y + borderW, rect.width - borderW * 2f, rect.height - borderW * 2f);
        DrawBorder(innerRect, borderW, new Color(0f, 0f, 0f, 0.45f));

        // 6) 외곽선 텍스트 — 흰 글자 + 검정 외곽. 바 높이에 맞춰 폰트 축소.
        int prevFs = _centerStyle.fontSize;
        _centerStyle.fontSize = Mathf.Clamp(Mathf.RoundToInt(rect.height * 0.95f), 9, 14);
        DrawTextWithOutline(rect, $"{curr}/{max}", _centerStyle,
                            Color.white, new Color(0f, 0f, 0f, 0.95f), 1f);
        _centerStyle.fontSize = prevFs;
    }

    // 데미지/회복 부동 라벨 컬러 팔레트 — 다크판타지 톤(머티드 주얼/베이지/모스 그린).
    private static readonly Color FloaterColorDamageSmall = new(0.961f, 0.914f, 0.816f); // #f5e9d0 베이지 화이트
    private static readonly Color FloaterColorDamageBig   = new(0.851f, 0.306f, 0.227f); // #d94e3a 머티드 크림슨
    private static readonly Color FloaterColorHeal        = new(0.561f, 0.749f, 0.478f); // #8fbf7a 모스 그린
    private static readonly Color FloaterColorBlock       = new(0.427f, 0.643f, 0.769f); // #6da4c4 페일 블루 — 방어막 흡수
    private static readonly Color FloaterOutline          = new(0.102f, 0.078f, 0.063f); // #1a1410 잉크 차콜

    private void DrawFloaters()
    {
        var prevMatrix = GUI.matrix;
        foreach (var f in _floaters)
        {
            if (f.delay > 0) continue;

            // 앵커가 살아있으면 위치 갱신, 죽었으면 마지막으로 알려진 위치에서 계속 떠오름.
            if (_slotPositions.TryGetValue(f.anchor, out var basePos))
            {
                f.lastPos = basePos;
                f.hasPos = true;
            }
            else if (!f.hasPos) continue;

            float progress = Mathf.Clamp01(f.age / DamageFloater.LifeTime);

            // 알파: 첫 20%는 풀 가시, 이후 ease-out(quart)으로 자연스럽게 사라짐.
            float fadeT = Mathf.Clamp01((progress - 0.20f) / 0.80f);
            float alpha = 1f - fadeT * fadeT;

            // 위로 떠오름: 초반 가속 → 후반 거의 정지 (easeOut quart). 80px로 통일.
            float riseT = 1f - Mathf.Pow(1f - progress, 4f);
            float yOffset = -82f * riseT;

            // 부유 sway — 떠오르는 동안 좌우로 사인 흔들림. 모션을 organic하게.
            float swayT = f.age * 4.2f + f.swayPhase;
            float sway = Mathf.Sin(swayT) * f.swayAmp * (1f - progress * 0.6f);

            // 펀치 인 = 오버슈팅 (Back easeOut). 시작 1.75 → 0.92 살짝 작아짐 → 1.0 정착.
            // 잔여 떨림: 펀치 직후 0.06초 동안 미세 jitter로 충격 잔향.
            float punchT = Mathf.Clamp01(f.age / DamageFloater.PunchDuration);
            const float c = 1.70158f;
            float backOut = 1f + (c + 1f) * Mathf.Pow(punchT - 1f, 3f) + c * Mathf.Pow(punchT - 1f, 2f);
            float scale = Mathf.Lerp(DamageFloater.PunchStartScale, 1f, backOut);

            // 회전 — 처음엔 spawnRotation의 1.4배까지 흔들렸다가, 천천히 정착.
            // 큰 데미지일수록 시작 회전이 커서 더 격렬해 보임.
            float rotSettle = 1f - Mathf.Pow(1f - progress, 2f);
            float rotation = f.spawnRotation * (1.4f - rotSettle * 0.9f);

            // 타입+크기에 따라 컬러/폰트 사이즈 결정.
            Color bodyColor;
            Color glowColor;
            int fontSize;
            string text;
            if (f.kind == DamageFloaterKind.Heal)
            {
                bodyColor = FloaterColorHeal;
                glowColor = bodyColor;
                fontSize = f.amount >= 10 ? 38 : 30;
                text = $"+{f.amount}";
            }
            else if (f.kind == DamageFloaterKind.BlockAbsorbed)
            {
                bodyColor = FloaterColorBlock;
                glowColor = bodyColor;
                fontSize = f.amount >= 10 ? 32 : 26;
                text = $"◆{f.amount}";
            }
            else
            {
                // 일반 데미지: 크기별 폰트 + 컬러 그라데이션. 글로우 컬러는 본체보다 더 진한 핏빛.
                if (f.amount >= 30)        { bodyColor = FloaterColorDamageBig;   fontSize = 48; }
                else if (f.amount >= 10)   { bodyColor = Color.Lerp(FloaterColorDamageSmall, FloaterColorDamageBig, 0.6f); fontSize = 36; }
                else                       { bodyColor = FloaterColorDamageSmall; fontSize = 28; }
                glowColor = FloaterColorDamageBig;
                text = $"-{f.amount}";
            }

            int scaledFontSize = Mathf.Max(8, Mathf.RoundToInt(fontSize * scale));
            _damageStyle.fontSize = scaledFontSize;

            // 라벨 박스 — 폰트 스케일에 비례한 너비/높이로 줘서 텍스트 잘림 방지.
            float boxW = scaledFontSize * 4.2f;
            float boxH = scaledFontSize * 1.55f;
            float cx = f.lastPos.x + f.xOffset + f.xJitter + sway;
            float cy = f.lastPos.y - 110 + yOffset;
            var rect = new Rect(cx - boxW * 0.5f, cy - boxH * 0.5f, boxW, boxH);

            // 회전: 텍스트 중심 기준. 살짝 흔들리는 잉크 느낌.
            GUI.matrix = prevMatrix * RotateAroundPivotMatrix(rotation, new Vector2(cx, cy));

            // 1) 글로우 외곽 — 색상 톤(핏빛/페일블루/그린)을 살짝 퍼뜨려 깊이감.
            //    펀치 직후 0.15초 동안만 풀 강도, 이후 빠르게 감소.
            float glowT = 1f - Mathf.Clamp01(f.age / 0.18f);
            float glowAlpha = alpha * 0.42f * glowT;
            if (glowAlpha > 0.01f)
            {
                GUI.color = new Color(glowColor.r, glowColor.g, glowColor.b, glowAlpha);
                const float gPx = 4f;
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var gr = new Rect(rect.x + dx * gPx, rect.y + dy * gPx, rect.width, rect.height);
                    GUI.Label(gr, text, _damageStyle);
                }
            }

            // 2) 외곽선 — 잉크 차콜. 8방향, 어떤 배경 위에서도 가독성.
            GUI.color = new Color(FloaterOutline.r, FloaterOutline.g, FloaterOutline.b, alpha);
            const float oPx = 2f;
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                var or = new Rect(rect.x + dx * oPx, rect.y + dy * oPx, rect.width, rect.height);
                GUI.Label(or, text, _damageStyle);
            }

            // 3) 드롭 섀도 — 잉크 떨어진 흔적. 본체 아래로 살짝, 외곽선보다 멀리.
            GUI.color = new Color(0f, 0f, 0f, 0.55f * alpha);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 4f, rect.width, rect.height), text, _damageStyle);

            // 4) 본체 텍스트
            GUI.color = new Color(bodyColor.r, bodyColor.g, bodyColor.b, alpha);
            GUI.Label(rect, text, _damageStyle);
        }
        GUI.matrix = prevMatrix;
        GUI.color = Color.white;
    }

    // =========================================================
    // Overlay panels
    // =========================================================

    // 상단 HUD 아이콘 뒤에 깔리는 다층 글로우 — 마나 오브의 후광과 동일한 결로
    // 부드럽게 호흡하며 가장자리는 자연스럽게 사라진다.
    private void DrawIconGlow(Rect iconRect, Color tint, float intensity = 1f)
    {
        if (_manaFrameTexture == null) return;

        var prevColor = GUI.color;

        float slow = (Mathf.Sin(Time.time * 1.4f) + 1f) * 0.5f;
        float pulse = Mathf.Lerp(0.85f, 1.0f, slow);

        const int glowLayers = 6;
        const float glowMinScale = 1.15f;
        const float glowMaxScale = 2.10f;
        const float glowBaseAlpha = 0.22f;

        float cx = iconRect.center.x;
        float cy = iconRect.center.y;
        float baseSize = Mathf.Max(iconRect.width, iconRect.height);

        for (int i = 0; i < glowLayers; i++)
        {
            float t = i / (float)(glowLayers - 1);
            float scale = Mathf.Lerp(glowMinScale, glowMaxScale, t) + 0.04f * slow * t;
            float alpha = Mathf.Min(1f, glowBaseAlpha * (1f - t) * (1f - t) * pulse * intensity);
            float gs = baseSize * scale;
            var gr = new Rect(cx - gs * 0.5f, cy - gs * 0.5f, gs, gs);
            GUI.color = new Color(tint.r, tint.g, tint.b, alpha);
            GUI.DrawTexture(gr, _manaFrameTexture, ScaleMode.StretchToFill, alphaBlend: true);
        }

        GUI.color = prevColor;
    }

    // 신규 보상/포인트 획득 알림 — 아이콘 우상단에 빨간 느낌표 뱃지.
    // 유물 획득 / 포션 획득 / 테크 포인트 획득 시 점등, 해당 패널/화면을 열면 꺼짐.
    private void DrawNewBadge(Rect iconRect)
    {
        if (_iconAlertNew == null) return;
        float badgeSz = iconRect.width * 0.55f;
        var badgeRect = new Rect(
            iconRect.xMax - badgeSz * 0.70f,
            iconRect.y    - badgeSz * 0.25f,
            badgeSz, badgeSz);
        float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 5f);
        // 살짝 빨갛게 뜨거운 글로우
        DrawIconGlow(badgeRect, new Color(1f, 0.30f, 0.25f, pulse), 1.4f);
        var prev = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, pulse);
        GUI.DrawTexture(badgeRect, _iconAlertNew, ScaleMode.ScaleToFit);
        GUI.color = prev;
    }

    // Battle/Map/Village 공통 상단 HUD 스트립 + 구분선 — 호출자가 컨텍스트를 넘겨주면 그 색 사용.
    public void DrawHudStripAndDivider(HudContext ctx = HudContext.Battle)
    {
        if (!hudStripEnabled) return;

        // 마을은 전투와 동일하게 처리 — bg/alpha/divider/topBar/하단라인 모두 공유.
        Color bg = ctx == HudContext.Map ? hudStripBgColorMap : hudStripBgColorBattle;
        bg.a = Mathf.Clamp01(ctx == HudContext.Map ? hudStripAlphaMap : hudStripAlphaBattle);
        Texture2D divTex = ctx == HudContext.Map
            ? null // 맵은 노란 디바이더 제거 — 검은 바만 사용
            : _hudDividerTexBattle; // 전투/마을 공용

        // 마스터 스케일 적용 — 모든 사이즈를 한 번에 비례 조절.
        float s = navBarMasterScale;
        float effStripH    = hudStripHeight * s;
        float effDivCenterY = hudDividerCenterY * s;
        float effDivH      = hudDividerHeight * s;
        float effBottomLineT = hudBattleBottomLineThickness * s;
        float effTexH      = topBarTexHeight * s;
        float effTexY      = topBarTexYOffset * s;

        // 1) 바 배경 채우기. 한 번만 — 이중 fill은 알파 반투명을 깨뜨림.
        FillRect(new Rect(0f, 0f, RefW, effStripH), bg);

        // 1.5) 장식 텍스처 — 전투/마을 공통. 알파가 fill을 통과시켜 톤은 유지.
        if (_topBarBg != null && (ctx == HudContext.Battle || ctx == HudContext.Village) && topBarTexEnabled)
        {
            float texW = Mathf.Max(0f, RefW - topBarTexHorizontalInset * 2f);
            var texRect = new Rect(topBarTexHorizontalInset, effTexY, texW, effTexH);
            GUI.DrawTexture(texRect, _topBarBg, ScaleMode.StretchToFill, alphaBlend: true);
        }

        // 2) 디바이더는 마지막에 그려서 바 위로 겹치도록. Width가 0이면 오버스캔 기반 자동, >0이면 그 값 직접 사용해 가운데 정렬.
        if (divTex != null)
        {
            float divW = hudDividerWidth > 0f ? hudDividerWidth : (RefW + hudDividerOverscan * 2f);
            float divX = hudDividerWidth > 0f ? (RefW - divW) * 0.5f : -hudDividerOverscan;
            var prev = GUI.color;
            GUI.color = hudDividerTint;
            GUI.DrawTexture(
                new Rect(divX,
                         effDivCenterY - effDivH * 0.5f,
                         divW,
                         effDivH),
                divTex, ScaleMode.StretchToFill, alphaBlend: true);
            GUI.color = prev;
        }
        // 텍스처 없으면 아예 선 생략 — 호출 측에서 나중에 따로 붙이도록.

        // 바 하단 골드 트림 — 전투/맵/마을 공용. 두께 0이거나 알파 0이면 스킵.
        if (effBottomLineT > 0f && hudBattleBottomLineColor.a > 0f)
        {
            FillRect(new Rect(0f, effStripH - effBottomLineT, RefW, effBottomLineT),
                     hudBattleBottomLineColor);
        }
    }

    // 우측 정렬 슬롯들 (DeckView + Floor). 우→좌 순서로 그려서 cursor 계산을 단순화.
    private void DrawRightSlots(
        Rect barRect, float barY, float barH,
        float iconY, float iconSize, float iconLabelGap,
        string floorLabel, int deckCount = -1, HudContext ctx = HudContext.Battle, int turnNumber = -1)
    {
        float rightPad = hudRightPad * navBarMasterScale;         // 화면 우측 가장자리 여백 (padX보다 살짝 크게)
        float rightSlotGap = hudRightSlotGap * navBarMasterScale; // 슬롯 사이 간격 (좌측 slotGap보다 넓게)

        float right = barRect.xMax - rightPad;
        bool anyDrawn = false;

        // Turn 슬롯 (가장 오른쪽) — 전투 중에만 표시
        if (turnNumber >= 0)
        {
            right = DrawRightSlot(right, barY, barH, iconY, iconSize, iconLabelGap,
                _iconTurn, turnNumber.ToString(), new Color(0.75f, 0.90f, 1f), wobblePhase: 1.8f,
                tipTitle: "턴 (Turn)",
                tipBody: $"현재 {turnNumber}턴.\n매 턴 시작에 마나가 가득 차고, 카드를 새로 드로우한다.");
            anyDrawn = true;
        }

        // Floor 슬롯 — 계단은 아주 미세하게 좌우로 기울음.
        // 전투 중에는 진행도가 의미 없으므로 숨김 (맵/마을에서만 노출).
        if (floorLabel != null && ctx != HudContext.Battle)
        {
            if (anyDrawn) right -= rightSlotGap;
            right = DrawRightSlot(right, barY, barH, iconY, iconSize, iconLabelGap,
                _iconFloor, floorLabel, new Color(1f, 0.82f, 0.35f), wobblePhase: 2.4f,
                tipTitle: "층 (Floor)",
                tipBody: $"현재 진행도 {floorLabel}.\n맨 위층의 보스를 처치하면 챕터가 끝난다.");
            anyDrawn = true;
        }

        // Deck View 버튼 — 계단 왼쪽. 클릭하면 덱 전체 보기 오버레이 오픈.
        if (deckCount >= 0)
        {
            if (anyDrawn) right -= rightSlotGap;
            right = DrawDeckViewRightSlot(right, barY, barH, iconY, iconSize, iconLabelGap, deckCount);
            anyDrawn = true;
        }

        // 테크트리 진입 슬롯 — 맵 화면에서만 표시 (전투 중 진입 시 상태 초기화 방지).
        var gsm = GameStateManager.Instance;
        if (ctx == HudContext.Map && gsm != null && gsm.TechTree != null)
        {
            if (anyDrawn) right -= rightSlotGap;
            DrawTechTreeRightSlot(right, barY, barH, iconY, iconSize, iconLabelGap, gsm.TechTree.points);
        }
    }

    // 계단 왼쪽에 위치한 덱 뷰 버튼. 덱 카운트를 라벨로 표시하고 클릭 시 오버레이를 토글.
    private float DrawDeckViewRightSlot(float right, float barY, float barH,
        float iconY, float iconSize, float iconLabelGap, int deckCount)
    {
        string label = deckCount.ToString();
        var labelSize = _labelStyle.CalcSize(new GUIContent(label));
        float labelX = right - labelSize.x;
        var labelRect = new Rect(labelX, barY + (barH - labelSize.y) * 0.5f, labelSize.x + 2f, labelSize.y);

        float iconX = labelX - iconLabelGap - iconSize;
        var iconRect = new Rect(iconX, iconY, iconSize, iconSize);

        // 클릭 히트 영역 — 아이콘 + 라벨 묶어 살짝 여유 있게
        var hitRect = new Rect(iconX - 8f, barY, (right - iconX) + 16f, barH);
        var ev = Event.current;
        bool hover = hitRect.Contains(ev.mousePosition);

        if (hover)
        {
            FillRect(hitRect, new Color(1f, 0.82f, 0.35f, 0.10f));
            DrawBorder(hitRect, 1f, new Color(1f, 0.82f, 0.35f, 0.35f));
            _hoveredPassiveTitle = "덱 보기 (Deck)";
            _hoveredPassiveBody  = $"전체 덱 카드 {deckCount}장.\n클릭하면 덱 전체를 펼쳐 본다 (전투/맵 어디서든).";
        }

        // HUD 우측 덱 카운트 슬롯 — Floor 바로 옆 — CardBack 텍스처 사용 (코너 더미는 _iconDeck 별도 사용).
        var hudDeckTex = GetCharacterCardBack() ?? GetCharacterDeckIcon();
        if (hudDeckTex != null)
        {
            Color glowTint = hover ? new Color(1f, 0.92f, 0.60f) : new Color(0.70f, 0.88f, 1f);
            DrawIconGlow(iconRect, glowTint, hover ? 1.35f : 1f);

            GUI.DrawTexture(iconRect, hudDeckTex, ScaleMode.ScaleToFit);
        }

        GUI.Label(labelRect, label, _labelStyle);

        if (hover && ev.type == EventType.MouseDown && ev.button == 0)
        {
            _deckViewerOpen = !_deckViewerOpen;
            _deckViewerSource = 0;  // HUD에서 열면 항상 전체 덱 모드.
            _deckViewerScroll = Vector2.zero;
            ev.Use();
        }

        return iconX;
    }

    // 덱 슬롯 왼쪽에 위치한 테크트리 진입 슬롯 — 절차적 트리 아이콘(루트 → 중간 → T2 3분기).
    // 클릭 시 GameStateManager.EnterTechTree() 호출, 보유 포인트 1+ 면 라벨로 카운트 노출.
    private float DrawTechTreeRightSlot(float right, float barY, float barH,
        float iconY, float iconSize, float iconLabelGap, int totalPoints)
    {
        // 라벨은 포인트 보유 시에만 표시 (0이면 아이콘만)
        string label = totalPoints > 0 ? totalPoints.ToString() : "";
        Vector2 labelSize = string.IsNullOrEmpty(label)
            ? Vector2.zero
            : _labelStyle.CalcSize(new GUIContent(label));

        float labelX = right - labelSize.x;
        float iconRightEdge = string.IsNullOrEmpty(label) ? right : labelX - iconLabelGap;
        float iconX = iconRightEdge - iconSize;
        var iconRect = new Rect(iconX, iconY, iconSize, iconSize);

        // 클릭 히트 영역 — 아이콘 + 라벨 묶기
        var hitRect = new Rect(iconX - 8f, barY, (right - iconX) + 16f, barH);
        var ev = Event.current;
        bool hover = hitRect.Contains(ev.mousePosition);

        // 호버 강조 + 포인트 보유 시 따뜻한 글로우
        Color glowTint = hover
            ? new Color(1f, 0.92f, 0.55f)
            : (totalPoints > 0 ? new Color(1f, 0.82f, 0.40f) : new Color(0.85f, 0.75f, 0.55f));
        float glowIntensity = hover ? 1.5f : (totalPoints > 0 ? 1.25f : 0.9f);

        if (hover)
        {
            FillRect(hitRect, new Color(1f, 0.82f, 0.35f, 0.10f));
            DrawBorder(hitRect, 1f, new Color(1f, 0.82f, 0.35f, 0.35f));
            _hoveredPassiveTitle = "테크트리 (Tech Tree)";
            _hoveredPassiveBody  = totalPoints > 0
                ? $"보유 포인트 {totalPoints}.\n클릭해 4방위 노드에서 영구 강화를 해금한다."
                : "포인트가 없다.\n맵 진행 보상으로 모이며, 4방위 노드에서 영구 강화를 해금한다.";
        }

        DrawIconGlow(iconRect, glowTint, glowIntensity);

        if (_iconTechTree != null)
            GUI.DrawTexture(iconRect, _iconTechTree, ScaleMode.ScaleToFit);
        else
            DrawTechTreeProcIcon(iconRect, hover, totalPoints > 0);

        if (!string.IsNullOrEmpty(label))
        {
            var labelRect = new Rect(labelX, barY + (barH - labelSize.y) * 0.5f, labelSize.x + 2f, labelSize.y);
            GUI.Label(labelRect, label, _labelStyle);
        }

        // 새 포인트 획득 알림 — 느낌표 뱃지 (유물/포션과 공통 룩)
        var gsmDot = GameStateManager.Instance;
        if (gsmDot?.TechTree != null && gsmDot.TechTree.hasNewPoints)
        {
            DrawNewBadge(iconRect);
        }

        if (hover && ev.type == EventType.MouseDown && ev.button == 0)
        {
            var gsm = GameStateManager.Instance;
            if (gsm != null) gsm.EnterTechTree();
            ev.Use();
        }

        return iconX;
    }

    // 절차적 트리 아이콘 — 루트(아래) → 중간 → 상단 3분기 + 가는 연결선.
    // hover/active 따라 색을 따뜻하게.
    private void DrawTechTreeProcIcon(Rect rect, bool hovered, bool active)
    {
        // 아이콘 안쪽 패딩
        float pad = rect.width * 0.12f;
        Rect ico = new Rect(rect.x + pad, rect.y + pad, rect.width - pad * 2f, rect.height - pad * 2f);

        Color line = hovered
            ? new Color(1f, 0.92f, 0.60f, 1f)
            : (active ? new Color(1f, 0.85f, 0.45f, 1f) : new Color(0.85f, 0.75f, 0.50f, 0.95f));
        Color nodeFill = hovered
            ? new Color(1f, 0.88f, 0.50f, 1f)
            : (active ? new Color(0.92f, 0.78f, 0.40f, 1f) : new Color(0.55f, 0.48f, 0.30f, 1f));

        Vector2 root = new Vector2(ico.center.x, ico.yMax - ico.height * 0.05f);
        Vector2 mid  = new Vector2(ico.center.x, ico.center.y + ico.height * 0.05f);
        Vector2 tl   = new Vector2(ico.x + ico.width * 0.10f, ico.y + ico.height * 0.10f);
        Vector2 tc   = new Vector2(ico.center.x,              ico.y);
        Vector2 tr   = new Vector2(ico.xMax - ico.width * 0.10f, ico.y + ico.height * 0.10f);

        float thick = Mathf.Max(1.2f, rect.width * 0.04f);
        DrawProcLine(root, mid, line, thick);
        DrawProcLine(mid, tl, line, thick);
        DrawProcLine(mid, tc, line, thick);
        DrawProcLine(mid, tr, line, thick);

        float nSize = Mathf.Max(2.5f, rect.width * 0.10f);
        DrawProcNode(root, nSize * 1.05f, nodeFill, line);
        DrawProcNode(mid,  nSize,         nodeFill, line);
        DrawProcNode(tl,   nSize * 0.9f,  nodeFill, line);
        DrawProcNode(tc,   nSize * 0.9f,  nodeFill, line);
        DrawProcNode(tr,   nSize * 0.9f,  nodeFill, line);
    }

    private void DrawProcLine(Vector2 a, Vector2 b, Color color, float thickness)
    {
        float dx = b.x - a.x;
        float dy = b.y - a.y;
        float len = Mathf.Sqrt(dx * dx + dy * dy);
        if (len < 0.01f) return;
        float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;

        var prevMatrix = GUI.matrix;
        var prevColor = GUI.color;

        GUI.matrix = prevMatrix
                     * Matrix4x4.Translate(new Vector3(a.x, a.y, 0f))
                     * Matrix4x4.Rotate(Quaternion.Euler(0, 0, angle))
                     * Matrix4x4.Translate(new Vector3(-a.x, -a.y, 0f));

        GUI.color = color;
        GUI.DrawTexture(new Rect(a.x, a.y - thickness * 0.5f, len, thickness), Texture2D.whiteTexture);

        GUI.matrix = prevMatrix;
        GUI.color = prevColor;
    }

    private void DrawProcNode(Vector2 c, float size, Color fill, Color border)
    {
        var prev = GUI.color;
        var rect = new Rect(c.x - size, c.y - size, size * 2f, size * 2f);
        GUI.color = fill;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        DrawBorder(rect, Mathf.Max(1f, size * 0.18f), border);
        GUI.color = prev;
    }

    // =========================================================
    // 배틀 / 맵 / 마을 공용 상단 HUD — HP/Gold/Potion/Relic + (우측) Deck/Floor.
    // 전투 중 실시간 HP를 반영하려면 hpCurrent/hpMax 오버라이드를 넘긴다.
    // 맵·마을에서는 RunState 값을 그대로 쓴다.
    // =========================================================
    public void DrawTopBar(HudContext ctx, RunState run, int currentFloor, int totalFloors,
                           int? hpCurrent = null, int? hpMax = null, int turnNumber = -1)
    {
        if (run == null) return;
        EnsureStyles();

        DrawHudStripAndDivider(ctx);

        // 마스터 스케일 — 바 내부 아이콘/슬롯도 비례 조절.
        float s = navBarMasterScale;
        const float barX = 10f;
        float barY = 8f * s;
        const float barW = RefW - 20f;
        float barH = 58.14f * s;
        var barRect = new Rect(barX, barY, barW, barH);
        _navBarBottomY = barY + barH + 4f;

        float iconSize = hudSlotIconSize * s;
        float iconLabelGap = hudSlotIconLabelGap * s;
        float slotGap = hudSlotGap * s;
        float padX = hudSlotLeftPadX * s;
        float iconY = barY + (barH - iconSize) * 0.5f;
        float cursorX = barX + padX;

        void DrawSlot(Texture2D tex, string label, Color glowTint, float glowIntensity = 1f,
                      string tipTitle = null, string tipBody = null)
        {
            float startX = cursorX;
            if (tex != null)
            {
                var iconRect = new Rect(cursorX, iconY, iconSize, iconSize);
                DrawIconGlow(iconRect, glowTint, glowIntensity);
                GUI.DrawTexture(iconRect, tex, ScaleMode.ScaleToFit);
                cursorX += iconSize + iconLabelGap;
            }
            var size = _labelStyle.CalcSize(new GUIContent(label));
            var labelRect = new Rect(cursorX, barY + (barH - size.y) * 0.5f, size.x + 2f, size.y);
            GUI.Label(labelRect, label, _labelStyle);
            cursorX += size.x + slotGap;

            if (!string.IsNullOrEmpty(tipTitle))
            {
                float endX = cursorX - slotGap;
                var hitRect = new Rect(startX - 4f, barY, (endX - startX) + 8f, barH);
                if (hitRect.Contains(Event.current.mousePosition))
                {
                    _hoveredPassiveTitle = tipTitle;
                    _hoveredPassiveBody = tipBody;
                }
            }
        }

        int hpNow = hpCurrent ?? run.playerCurrentHp;
        int hpCap = hpMax ?? run.playerMaxHp;
        DrawSlot(_iconHP,     $"{hpNow}/{hpCap}",                          new Color(1f, 0.55f, 0.50f), 1.6f,
                 tipTitle: "체력 (HP)",
                 tipBody: $"현재 체력 {hpNow} / 최대 {hpCap}.\n0이 되면 런이 종료된다. 휴식지·포션·유물로 회복할 수 있다.");
        DrawSlot(_iconGold,   $"{run.gold}",                               new Color(1f, 0.82f, 0.35f),
                 tipTitle: "골드 (Gold)",
                 tipBody: $"보유 골드 {run.gold}. 상점에서 카드·유물·포션을 구매하거나 카드 제거에 사용한다.");
        // 포션 슬롯 — 호버 시 아이콘 확대 + 클릭 시 포션 row 패널 토글
        {
            var evP = Event.current;
            string potionLabel = $"{run.potions.Count}/{run.MaxPotionSlots}";
            var potionLabelSz = _labelStyle.CalcSize(new GUIContent(potionLabel));
            float totalPotionW = (_iconPotion != null ? iconSize + iconLabelGap : 0f) + potionLabelSz.x;
            var potionHitRect = new Rect(cursorX - 4f, barY, totalPotionW + 12f, barH);
            _potionDropdownAnchorX = potionHitRect.x;
            bool potionHov = potionHitRect.Contains(evP.mousePosition);

            if (_iconPotion != null)
            {
                float potionIconScale = _potionViewerOpen ? 1.24f : (potionHov ? 1.18f : 1f);
                float scaledSzP = iconSize * potionIconScale;
                float offXP = (iconSize - scaledSzP) * 0.5f;
                float offYP = (iconSize - scaledSzP) * 0.5f;
                var potionIconRect = new Rect(cursorX + offXP, iconY + offYP, scaledSzP, scaledSzP);
                Color potionGlowTint = _potionViewerOpen ? new Color(0.55f, 1f, 0.65f)
                                     : (potionHov ? new Color(0.45f, 0.95f, 0.55f) : new Color(0.35f, 0.85f, 0.45f));
                DrawIconGlow(potionIconRect, potionGlowTint, _potionViewerOpen ? 1.7f : (potionHov ? 1.4f : 1f));
                GUI.DrawTexture(potionIconRect, _iconPotion, ScaleMode.ScaleToFit);
                if (run.hasNewPotion) DrawNewBadge(potionIconRect);
                // 포션 타겟팅 중이면 상단 바 포션 아이콘이 화살표 출발점 — 공격 카드처럼 source→대상 아크가 그려진다.
                if (_targetingPotionIndex >= 0)
                {
                    _arrowSourceRect = potionIconRect;
                    _arrowSourceValid = true;
                }
                cursorX += iconSize + iconLabelGap;
            }
            var potionLabelRect = new Rect(cursorX, barY + (barH - potionLabelSz.y) * 0.5f,
                                           potionLabelSz.x + 2f, potionLabelSz.y);
            GUI.Label(potionLabelRect, potionLabel, _labelStyle);
            cursorX += potionLabelSz.x + slotGap;

            if (potionHov)
            {
                _hoveredPassiveTitle = "포션 (Potion)";
                _hoveredPassiveBody  = $"보유 {run.potions.Count} / 슬롯 {run.MaxPotionSlots}.\n클릭하면 포션 목록이 펼쳐진다. 전투 중 마시거나 휴식지에서 보관한다.";
            }

            if (potionHov && evP.type == EventType.MouseDown && evP.button == 0)
            {
                _potionViewerOpen = !_potionViewerOpen;
                _selectedPotionIndex = -1;
                if (_potionViewerOpen) _relicViewerOpen = false;
                run.hasNewPotion = false; // 클릭 = 인지, 토글 방향 무관하게 즉시 OFF
                evP.Use();
            }
        }
        // 유물 슬롯 — 호버 시 아이콘 확대 + 클릭 시 유물 row 패널 토글
        {
            var evR = Event.current;
            string relicLabel = $"{run.relics.Count}";
            var relicLabelSz = _labelStyle.CalcSize(new GUIContent(relicLabel));
            float totalRelicW = (_iconRelic != null ? iconSize + iconLabelGap : 0f) + relicLabelSz.x;
            var relicHitRect = new Rect(cursorX - 4f, barY, totalRelicW + 12f, barH);
            _relicDropdownAnchorX = relicHitRect.x;
            bool relicHov = relicHitRect.Contains(evR.mousePosition);

            if (_iconRelic != null)
            {
                float relicIconScale = _relicViewerOpen ? 1.24f : (relicHov ? 1.18f : 1f);
                float scaledSz = iconSize * relicIconScale;
                float offX = (iconSize - scaledSz) * 0.5f;
                float offY = (iconSize - scaledSz) * 0.5f;
                var relicIconRect = new Rect(cursorX + offX, iconY + offY, scaledSz, scaledSz);
                Color relicGlowTint = _relicViewerOpen ? new Color(1f, 0.55f, 1f)
                                    : (relicHov ? new Color(0.90f, 0.55f, 1f) : new Color(0.85f, 0.55f, 1f));
                DrawIconGlow(relicIconRect, relicGlowTint, _relicViewerOpen ? 1.7f : (relicHov ? 1.4f : 1f));
                GUI.DrawTexture(relicIconRect, _iconRelic, ScaleMode.ScaleToFit);
                if (run.hasNewRelic) DrawNewBadge(relicIconRect);
                cursorX += iconSize + iconLabelGap;
            }
            var relicLabelRect = new Rect(cursorX, barY + (barH - relicLabelSz.y) * 0.5f,
                                          relicLabelSz.x + 2f, relicLabelSz.y);
            GUI.Label(relicLabelRect, relicLabel, _labelStyle);
            cursorX += relicLabelSz.x + slotGap;

            if (relicHov)
            {
                _hoveredPassiveTitle = "유물 (Relic)";
                _hoveredPassiveBody  = $"보유 유물 {run.relics.Count}개.\n클릭하면 유물 목록이 펼쳐진다. 전투/맵에서 자동 발동되는 영구 효과들.";
            }

            if (relicHov && evR.type == EventType.MouseDown && evR.button == 0)
            {
                _relicViewerOpen = !_relicViewerOpen;
                if (_relicViewerOpen)
                {
                    _potionViewerOpen = false;
                    _selectedPotionIndex = -1;
                }
                run.hasNewRelic = false; // 클릭 = 인지, 토글 방향 무관하게 즉시 OFF
                evR.Use();
            }
        }

        DrawRightSlots(barRect, barY, barH, iconY, iconSize, iconLabelGap,
            $"{currentFloor}/{totalFloors}", deckCount: run.deck.Count, ctx: ctx, turnNumber: turnNumber);
    }

    // 포션 아이콘 캐시 — 상단 바 포션 row / 마시기 팝업에서 사용
    private Texture2D _potionFallbackIcon;
    private readonly Dictionary<string, Texture2D> _potionIconCache = new();

    private Texture2D GetPotionIcon(string potionId)
    {
        if (string.IsNullOrEmpty(potionId)) return _potionFallbackIcon ??= Resources.Load<Texture2D>("InGame/Icon/Potion_Bottle");
        if (_potionIconCache.TryGetValue(potionId, out var cached)) return cached;
        // 개별 아이콘 (Resources/InGame/PotionArt/{id}.png) — 없으면 공용 폴백.
        var tex = Resources.Load<Texture2D>($"InGame/PotionArt/{potionId}");
        if (tex == null) tex = _potionFallbackIcon ??= Resources.Load<Texture2D>("InGame/Icon/Potion_Bottle");
        _potionIconCache[potionId] = tex;
        return tex;
    }

    // 한 슬롯을 right 기준으로 우→좌로 그리고, 이 슬롯의 left x를 반환
    // wobblePhase가 >=0 이면 미세한 좌우 기울임 적용 (양옆으로 살짝 기우는 느낌)
    private float DrawRightSlot(float right, float barY, float barH,
        float iconY, float iconSize, float iconLabelGap,
        Texture2D icon, string label, Color glowTint, float wobblePhase,
        string tipTitle = null, string tipBody = null)
    {
        var labelSize = _labelStyle.CalcSize(new GUIContent(label));
        float labelX = right - labelSize.x;
        var labelRect = new Rect(labelX, barY + (barH - labelSize.y) * 0.5f, labelSize.x + 2f, labelSize.y);
        GUI.Label(labelRect, label, _labelStyle);

        float iconX = labelX - iconLabelGap - iconSize;
        if (icon != null)
        {
            var iconRect = new Rect(iconX, iconY, iconSize, iconSize);
            DrawIconGlow(iconRect, glowTint);

            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
        }

        if (!string.IsNullOrEmpty(tipTitle))
        {
            var hitRect = new Rect(iconX - 4f, barY, (right - iconX) + 8f, barH);
            if (hitRect.Contains(Event.current.mousePosition))
            {
                _hoveredPassiveTitle = tipTitle;
                _hoveredPassiveBody = tipBody;
            }
        }
        return iconX;
    }

    private void DrawTurnInfo(BattleState state)
    {
        var p = state.player;

        // 좌하단 마나 오브 — 정적, 잔잔한 주황 글로우만. 위치/크기는 Inspector에서 조정.
        float orbSize = manaOrbSize;
        float orbCx = manaOrbCenterX;
        float orbCy = RefH - manaOrbBottomOffset;
        var orbRect = new Rect(orbCx - orbSize * 0.5f, orbCy - orbSize * 0.5f, orbSize, orbSize);

        var orbBodyTex = GetCharacterManaOrb() ?? (_manaOrbTexture != null ? _manaOrbTexture : _manaFrameTexture);

        if (orbBodyTex != null)
        {
            var prevColor = GUI.color;

            // 잔잔한 주황 글로우 — 호흡 펄스만 살짝, 흔들림/다층 후광/코어 하이라이트 모두 제거.
            // 본체 자체에 디테일이 풍부하므로 generic blob(_manaFrameTexture) 있을 때만 글로우 한 겹.
            if (_manaFrameTexture != null)
            {
                float pulse = 0.85f + 0.15f * (Mathf.Sin(Time.time * 1.4f) + 1f) * 0.5f;
                float gs = orbSize * 1.35f;
                var gr = new Rect(orbCx - gs * 0.5f, orbCy - gs * 0.5f, gs, gs);
                GUI.color = new Color(1.00f, 0.55f, 0.20f, 0.28f * pulse);
                GUI.DrawTexture(gr, _manaFrameTexture, ScaleMode.StretchToFill, alphaBlend: true);
            }

            // 본체 오브 — 정적, 흔들림 없음.
            GUI.color = Color.white;
            GUI.DrawTexture(orbRect, orbBodyTex, ScaleMode.StretchToFill, alphaBlend: true);

            GUI.color = prevColor;
        }

        int prevFontSize = _cardCostStyle.fontSize;
        _cardCostStyle.fontSize = Mathf.RoundToInt(orbSize * manaOrbFontSizeRatio);
        // 오브 안 텍스트 위치 조정 — 인스펙터 오프셋(오브 사이즈 비율)을 px로 환산해 rect를 평행 이동.
        float manaTextOffX = orbSize * manaOrbTextOffsetXPct;
        float manaTextOffY = orbSize * manaOrbTextOffsetYPct;
        var manaTextRect = new Rect(orbRect.x + manaTextOffX, orbRect.y + manaTextOffY,
                                    orbRect.width, orbRect.height);
        DrawTextWithOutline(manaTextRect, $"{p.mana}/{p.maxMana}", _cardCostStyle,
                            Color.white, new Color(0, 0, 0, 0.95f), 1.5f);
        _cardCostStyle.fontSize = prevFontSize;

        // 마나 오브 호버 툴팁 — 오브 본체 영역 위에 마우스가 있을 때만.
        if (orbRect.Contains(Event.current.mousePosition))
        {
            _hoveredPassiveTitle = "마나 (Mana)";
            _hoveredPassiveBody  = $"현재 {p.mana} / 최대 {p.maxMana}.\n카드 사용 시 코스트만큼 소모되며, 매 턴 시작에 가득 채워진다.";
        }

        // 좌하단 덱 더미 / 우하단 버린 카드 더미 — 호버 시 살짝 커지는 버튼 느낌.
        // 양쪽 모두 클릭하면 해당 더미를 펼쳐 본다.
        var skyBlue = new Color(0.30f, 0.65f, 1f, 1f);
        Vector2 mousePos = Event.current.mousePosition;

        // 좌하단 덱 더미
        int deckDisplay = GetDeckDisplayCount(state);
        float deckPulse = GetReshuffleDeckLandPulse();
        var deckPileBaseRect = new Rect(cornerPileLeftX, RefH - cornerPileTopFromBottom, cornerDeckPileSize, cornerDeckPileSize);
        bool deckHover = deckPileBaseRect.Contains(mousePos);
        Rect deckPileRect = ExpandRectFromCenter(deckPileBaseRect, deckHover ? 1.15f : 1f);
        if (deckHover)
        {
            DrawIconGlow(deckPileRect, new Color(0.55f, 0.85f, 1f), 1.3f);
        }
        DrawCardPile(deckPileRect, GetCharacterDeckIcon() ?? _iconDeck, deckDisplay, skyBlue, deckPulse);
        if (deckHover)
        {
            _hoveredPassiveTitle = "남은 덱 (Draw Pile)";
            _hoveredPassiveBody  = $"앞으로 뽑을 카드 {deckDisplay}장.\n클릭하면 남은 카드를 펼쳐 본다.";
        }

        // 우하단 버린 카드 더미 — 좌측 덱과 동일한 하늘색 뱃지.
        // 손패가 버려지는 애니메이션 중에는 착지한 카드 수만큼 카운트가 틱틱 올라가며,
        // 카드가 착지할 때마다 뱃지가 잠깐 커졌다 돌아오는 펄스가 들어간다.
        int discardDisplay = GetDiscardDisplayCount(state);
        float discardPulse = GetDiscardLandPulse();
        var discardPileBaseRect = new Rect(RefW - cornerPileRightInset, RefH - cornerPileTopFromBottom, cornerDiscardPileSize, cornerDiscardPileSize);
        bool discardHover = discardPileBaseRect.Contains(mousePos);
        Rect discardPileRect = ExpandRectFromCenter(discardPileBaseRect, discardHover ? 1.15f : 1f);
        if (discardHover)
        {
            DrawIconGlow(discardPileRect, new Color(0.55f, 0.85f, 1f), 1.3f);
        }
        DrawCardPile(discardPileRect, GetCharacterDiscardIcon() ?? _iconDiscard, discardDisplay, skyBlue, discardPulse);
        if (discardHover)
        {
            _hoveredPassiveTitle = "버린 덱 (Discard Pile)";
            _hoveredPassiveBody  = $"이번 전투에서 버려진 카드 {discardDisplay}장.\n덱이 비면 섞여 다시 드로우 풀로 돌아간다. 클릭하면 펼쳐 본다.";
        }

        // StS 스타일 — 좌하 더미 클릭 → 뽑을 카드, 우하 더미 클릭 → 버린 카드 뷰어.
        // 호버로 확장된 rect 기준으로 클릭 판정 — 커진 영역 어디를 눌러도 열림.
        var ev = Event.current;
        if (ev.type == EventType.MouseDown && ev.button == 0 && !_deckViewerOpen
            && _reinforcePickerCardIndex < 0)
        {
            if (deckPileRect.Contains(ev.mousePosition))
            {
                _deckViewerOpen = true;
                _deckViewerSource = 1;
                _deckViewerSortMode = 0;
                _deckViewerScroll = Vector2.zero;
                ev.Use();
            }
            else if (discardPileRect.Contains(ev.mousePosition))
            {
                _deckViewerOpen = true;
                _deckViewerSource = 2;
                _deckViewerSortMode = 0;
                _deckViewerScroll = Vector2.zero;
                ev.Use();
            }
        }
    }

    // 중심 기준으로 사각형을 비례 확장. 호버 시 더미가 가운데에서 부풀어 오르는 효과.
    private static Rect ExpandRectFromCenter(Rect r, float factor)
    {
        if (Mathf.Approximately(factor, 1f)) return r;
        float w = r.width * factor;
        float h = r.height * factor;
        return new Rect(r.center.x - w * 0.5f, r.center.y - h * 0.5f, w, h);
    }

    // 덱 더미에 표시할 카운트 — reshuffle 중엔 착지한 카드 수(0에서 증가),
    // 드로우 애니 중엔 실제 덱 개수 + 아직 손에 도달하지 않은 카드(덱에서 빠져나가는 중처럼 보이게).
    private int GetDeckDisplayCount(BattleState state)
    {
        if (IsReshuffleActive) return GetReshuffleLandedCount();
        if (IsDrawFlyActive) return state.deck.Count + GetDrawFlyInFlightCount();
        return state.deck.Count;
    }

    // 드로우 애니에서 아직 손에 도달하지 않은 카드 수 (덱에서 "빠져나가는 중"인 카드)
    private int GetDrawFlyInFlightCount()
    {
        if (!IsDrawFlyActive) return 0;
        float localNow = Time.time - _drawAnimStartTime;
        float holdEnd = DrawGatherDuration + DrawHoldDuration;
        int inFlight = 0;
        for (int k = 0; k < _drawFlyCards.Count; k++)
        {
            float disperseLocal = localNow - holdEnd - _drawFlyCards[k].disperseDelay;
            if (disperseLocal < 0f) { inFlight++; continue; }
            if (disperseLocal / DrawDisperseDuration < 1f) inFlight++;
        }
        return inFlight;
    }

    // Reshuffle 중 가장 최근에 덱에 착지한 카드로부터의 경과 시간 → 덱 뱃지 펄스
    private float GetReshuffleDeckLandPulse()
    {
        if (!IsReshuffleActive) return 0f;
        float localNow = Time.time - _reshuffleAnimStartTime;
        float mostRecent = -999f;
        for (int k = 0; k < _reshuffleFlyCards.Count; k++)
        {
            float end = _reshuffleFlyCards[k].delay + ReshuffleFlyDuration;
            if (end <= localNow && end > mostRecent) mostRecent = end;
        }
        if (mostRecent < 0f) return 0f;
        float t = (localNow - mostRecent) / DiscardLandPulseDuration;
        if (t < 0f || t > 1f) return 0f;
        return Mathf.Sin(t * Mathf.PI);
    }

    private void DrawCardPile(Rect rect, Texture2D icon, int count, Color? badgeColor, float badgePulse = 0f)
    {
        if (icon != null)
        {
            GUI.DrawTexture(rect, icon, ScaleMode.ScaleToFit, alphaBlend: true);
        }
        else
        {
            FillRect(rect, new Color(0.18f, 0.13f, 0.09f, 0.85f));
            DrawBorder(rect, 2f, new Color(0.7f, 0.55f, 0.3f, 1f));
        }

        // 카운트 — 아이콘 위에 외곽선 텍스트만. 프레임/오브 없이 다크판타지 톤에 자연스럽게 얹힘.
        // 착지 펄스: 짧게 살짝 커지고, badgeColor 톤의 부드러운 빛이 깜빡인다.
        float pulse = Mathf.Clamp01(badgePulse);
        float scale = 1f + 0.20f * pulse;

        if (pulse > 0.01f && badgeColor.HasValue && _manaFrameTexture != null)
        {
            Color tint = badgeColor.Value;
            float glowSize = rect.height * 0.70f * scale;
            var gr = new Rect(rect.center.x - glowSize * 0.5f,
                              rect.center.y - glowSize * 0.5f,
                              glowSize, glowSize);
            var prev = GUI.color;
            GUI.color = new Color(tint.r, tint.g, tint.b, 0.32f * pulse);
            GUI.DrawTexture(gr, _manaFrameTexture, ScaleMode.StretchToFill, alphaBlend: true);
            GUI.color = prev;
        }

        int prevFontSize = _centerStyle.fontSize;
        _centerStyle.fontSize = Mathf.RoundToInt(rect.height * 0.34f * scale);
        DrawTextWithOutline(rect, count.ToString(), _centerStyle,
                            Color.white, new Color(0f, 0f, 0f, 0.95f), 1.8f);
        _centerStyle.fontSize = prevFontSize;
    }

    // =========================================================
    // 손패 카드 위치 외부 노출 — 튜토리얼 등에서 특정 카드 강조용.
    // DrawHand의 부채꼴 기하를 그대로 재계산해 같은 좌표를 반환한다.
    // =========================================================

    /// <summary>특정 손패 카드의 화면 좌표(Screen px, axis-aligned bounding box) + 회전각(deg).
    /// 호버 중이면 raised hover rect 위치(회전 0)를, 아니면 부채꼴 base 위치 + 회전각을 반환.
    /// 좌표는 GUI.matrix 적용 후의 실제 화면 픽셀이라 OnGUI 외부에서도 그대로 사용 가능.</summary>
    public bool TryGetHandCardScreenRect(int handIndex, out Rect screenRect, out float rotationDeg)
    {
        screenRect = default;
        rotationDeg = 0f;
        if (_battle?.state == null) return false;
        int n = _battle.state.hand.Count;
        if (handIndex < 0 || handIndex >= n) return false;

        // 카드가 비행 중이면 부채꼴 base 위치와 실제 화면 위치가 다름 → 강조 생략.
        // - IsDiscardFlyActive: END TURN 직후 손 → 버림더미 비행 중 (DrawHand 자체가 return)
        // - IsDrawFlyActive: 다음 턴 시작 후 덱 → 손 비행 중 (도착 전엔 화면에 카드 없음)
        // - IsBeingDrawnInto: 해당 카드 인스턴스가 아직 부채꼴 base 도착 안 함
        // - _handHideProgress > 0: 손 슬라이드 다운 중 (공격 타게팅 등으로 손 숨김)
        if (IsDiscardFlyActive) return false;
        if (IsDrawFlyActive) return false;
        if (_handHideProgress > 0.01f) return false;
        if (IsBeingDrawnInto(_battle.state.hand[handIndex])) return false;

        float cardW = handCardWidth;
        float cardH = handCardHeight;
        float easedHide = EaseInOutCubic(_handHideProgress);
        float hideOffset = easedHide * HandHideDistance;
        float centerCardY = RefH - cardH * 0.5f + handBottomOffset + hideOffset;
        float fanRadius = handFanRadius;
        float fanOriginX = RefW * 0.5f;
        float fanOriginY = centerCardY + fanRadius;

        // Phantom slot 반영 — DrawHand와 같은 effCount 기하 사용해야 강조 좌표가 어긋나지 않음.
        float phantomAlpha = GetExhaustPhantomAlpha();
        float effCount = n + phantomAlpha;
        float totalAngle = (effCount - 1) * handAnglePerCard;
        if (totalAngle > handMaxTotalAngle) totalAngle = handMaxTotalAngle;
        float anglePerCard = effCount > 1 ? totalAngle / (effCount - 1) : handAnglePerCard;
        float startAngle = -totalAngle * 0.5f;

        // GUI.matrix와 같은 uniform scale 적용. Reference 1280×720 → 실제 화면 픽셀.
        float scale = Mathf.Min(Screen.width / RefW, Screen.height / RefH);

        // 호버 중이면 raised hover rect 사용 — 카드가 위로 올라오고 확대되며 회전 0.
        if (handIndex == _handStickyHoverIdx)
        {
            Rect hr = ComputeHandHoverRect(GetHandSlotForIndex(handIndex), startAngle, anglePerCard,
                fanOriginX, fanOriginY, fanRadius, cardW, cardH, hideOffset);
            screenRect = new Rect(hr.x * scale, hr.y * scale, hr.width * scale, hr.height * scale);
            rotationDeg = 0f;
            return true;
        }

        // 부채꼴 base 위치 + 회전각.
        float angle = startAngle + GetHandSlotForIndex(handIndex) * anglePerCard;
        Vector2 center = FanCardCenter(fanOriginX, fanOriginY, fanRadius, angle);
        center.y += CardIdleBob(handIndex);

        // 타게팅/스왑 중인 카드는 부채꼴에서 28px 위로 raised — DrawHand의 isActiveCard와 동일 보정.
        // 이걸 빼먹으면 카드는 위로 들떴는데 강조선만 base 위치에 남아 어긋난다.
        if (handIndex == _targetingCardIndex || handIndex == _swapFromCardIndex)
        {
            center.y -= 28f;
        }

        float w = cardW * scale;
        float h = cardH * scale;
        float cx = center.x * scale;
        float cy = center.y * scale;
        screenRect = new Rect(cx - w * 0.5f, cy - h * 0.5f, w, h);
        rotationDeg = angle;
        return true;
    }

    /// <summary>손에서 cardId가 일치하는 첫 번째 카드 인덱스 반환. 못 찾으면 false.</summary>
    public bool TryFindHandCardIndexById(string cardId, out int handIndex)
    {
        handIndex = -1;
        if (_battle?.state == null || string.IsNullOrEmpty(cardId)) return false;
        for (int i = 0; i < _battle.state.hand.Count; i++)
        {
            var c = _battle.state.hand[i]?.data;
            if (c != null && c.id == cardId) { handIndex = i; return true; }
        }
        return false;
    }

    private void DrawHand(BattleState state)
    {
        float cardW = handCardWidth;
        float cardH = handCardHeight;

        // 숨김 진행도 업데이트 — 고정 지속시간으로 선형 진행, 표시에는 ease-in-out 적용.
        // EffectiveHandHidden = 수동 토글 OR 공룡 공격 타겟팅 중 → 자동 슬라이드 다운.
        float hideTarget = EffectiveHandHidden ? 1f : 0f;
        _handHideProgress = Mathf.MoveTowards(
            _handHideProgress, hideTarget, Time.deltaTime / HandHideDuration);

        // 버린 더미 비행 애니메이션 중이면 일반 손패 렌더링을 건너뛴다 —
        // 날아가는 카드는 DrawDiscardFlyingCards가 별도로 그린다.
        if (IsDiscardFlyActive) return;

        int n = state.hand.Count;
        if (n == 0) return;

        // 부채꼴 기하: 화면 하단 훨씬 아래 가상의 원 중심에서 반지름만큼 떨어진 호 위에 카드 배치
        // 카드를 화면 아래로 내려서 배틀필드(발끝 Y≈540)를 가리지 않게 함.
        // 숨김 슬라이드 진행도에 ease-in-out 적용 후 Y 오프셋 계산 — 천천히 시작, 중간은 부드럽게, 끝은 잦아듦.
        float easedHide = EaseInOutCubic(_handHideProgress);
        float hideOffset = easedHide * HandHideDistance;
        float centerCardY = RefH - cardH * 0.5f + handBottomOffset + hideOffset; // 중앙 카드의 y 중심 (상단 ≈ Y 588, 노출 ≈ 139px)
        float fanRadius   = handFanRadius;
        float fanOriginX  = RefW * 0.5f;
        float fanOriginY  = centerCardY + fanRadius;

        // Phantom slot 반영 — 직전에 소진된 카드 자리를 잠시 유지해 옆 카드 reflow snap을 방지.
        // effCount는 float, 0.42s 유지 후 0.22s에 걸쳐 n으로 collapse.
        float phantomAlpha = GetExhaustPhantomAlpha();
        float effCount = n + phantomAlpha;

        // 카드 간 각도 — 카드 수가 많아지면 handMaxTotalAngle을 초과하지 않도록 간격 자동 축소
        float totalAngle = (effCount - 1) * handAnglePerCard;
        if (totalAngle > handMaxTotalAngle) totalAngle = handMaxTotalAngle;
        float anglePerCard = effCount > 1 ? totalAngle / (effCount - 1) : handAnglePerCard;
        float startAngle = -totalAngle * 0.5f;

        // 드로우 순서: 가장자리 카드부터, 중앙 카드가 마지막(최상단)에 오도록
        // 이렇게 해야 좌우 겹침이 대칭이 됨 (왼쪽 카드가 오른쪽 이웃을 덮고, 오른쪽 카드는 왼쪽 이웃을 덮음)
        float midIdx = (n - 1) * 0.5f;
        var drawOrder = new int[n];
        for (int k = 0; k < n; k++) drawOrder[k] = k;
        System.Array.Sort(drawOrder, (a, b) => Mathf.Abs(b - midIdx).CompareTo(Mathf.Abs(a - midIdx)));

        // 1) 호버 인덱스 계산 — 최상단(= drawOrder의 마지막)부터 역순 검사
        // 숨김 슬라이드가 조금이라도 진행 중이면 호버/클릭 비활성 — 사라지는 카드 클릭으로 인한 오조작 방지
        bool inputActive = _handHideProgress < 0.01f;

        Vector2 mouse = Event.current.mousePosition;
        int hoverIdx = -1;
        if (inputActive && !IsDrawFlyActive)
        {
            // 직전 프레임에 호버되어 확대된 카드는, 확대된 영역(hoverRect)을 벗어나야만 hover 해제.
            // 부채꼴 원본 rect는 작아서 마우스가 확대 카드 위에 있어도 빠져나갈 수 있음 → sticky 처리.
            if (_handStickyHoverIdx >= 0 && _handStickyHoverIdx < n
                && !IsBeingDrawnInto(state.hand[_handStickyHoverIdx]))
            {
                Rect stickyRect = ComputeHandHoverRect(GetHandSlotForIndex(_handStickyHoverIdx),
                    startAngle, anglePerCard, fanOriginX, fanOriginY, fanRadius,
                    cardW, cardH, hideOffset);
                if (stickyRect.Contains(mouse))
                {
                    hoverIdx = _handStickyHoverIdx;
                }
            }

            if (hoverIdx < 0)
            {
                for (int k = n - 1; k >= 0; k--)
                {
                    int i = drawOrder[k];
                    if (IsBeingDrawnInto(state.hand[i])) continue;
                    float angle = startAngle + GetHandSlotForIndex(i) * anglePerCard;
                    Vector2 center = FanCardCenter(fanOriginX, fanOriginY, fanRadius, angle);
                    center.y += CardIdleBob(i);
                    if (PointInRotatedRect(mouse, center, cardW, cardH, angle))
                    {
                        hoverIdx = i;
                        break;
                    }
                }
            }
        }
        _handStickyHoverIdx = hoverIdx;

        // 융합 모드 여부 — fan/hover 양쪽 루프에서 재사용. 타겟팅 카드가 UTILITY/FUSION이면 true.
        bool fusionMode = _targetingCardIndex >= 0
            && _targetingCardIndex < state.hand.Count
            && CardNeedsFusionTargets(state.hand[_targetingCardIndex].data);

        // 융합 모드 우선 클릭 핸들러 — 호버 카드 외 부채꼴 위치(회전된 fan rect)나 호버 raised rect 어디든 클릭 잡음.
        // 기존 흐름: 호버 카드만 클릭 처리 → 마우스가 raised hoverRect 밖(부채꼴 원위치)에 있으면 클릭 누락.
        // 여기서 모든 손패 카드의 회전 rect 위로 hit-test해 융합 후보(또는 촉매 자기자신)를 잡고 즉시 처리·소비.
        if (fusionMode && inputActive
            && Event.current.type == EventType.MouseDown && Event.current.button == 0
            && !IsDrawFlyActive)
        {
            Vector2 mousePos = Event.current.mousePosition;
            int clickedIdx = -1;

            // 1순위: 호버된 카드의 raised hoverRect 위
            if (hoverIdx >= 0)
            {
                var hr = ComputeHandHoverRect(GetHandSlotForIndex(hoverIdx), startAngle, anglePerCard,
                    fanOriginX, fanOriginY, fanRadius, cardW, cardH, hideOffset);
                if (hr.Contains(mousePos)) clickedIdx = hoverIdx;
            }

            // 2순위: 부채꼴 위치(회전 rect) — 위에서부터(drawOrder 마지막) 검사
            if (clickedIdx < 0)
            {
                for (int k = n - 1; k >= 0; k--)
                {
                    int i = drawOrder[k];
                    if (IsBeingDrawnInto(state.hand[i])) continue;
                    float angle = startAngle + GetHandSlotForIndex(i) * anglePerCard;
                    Vector2 c2 = FanCardCenter(fanOriginX, fanOriginY, fanRadius, angle);
                    c2.y += CardIdleBob(i);
                    if (PointInRotatedRect(mousePos, c2, cardW, cardH, angle))
                    {
                        clickedIdx = i;
                        break;
                    }
                }
            }

            if (clickedIdx >= 0)
            {
                if (clickedIdx == _targetingCardIndex)
                {
                    // 촉매 카드 재클릭 → 융합 모드 취소
                    Event.current.Use();
                    _targetingCardIndex = -1;
                    _fusionMaterialAPicked = false;
                }
                else if (IsFusionMaterialEligible(null, clickedIdx, isHand: true))
                {
                    Event.current.Use();
                    HandleFusionMaterialClick(DianoCard.Battle.FusionMaterial.Hand(clickedIdx));
                }
                // 비후보(dim) 카드 클릭: 의도적으로 무시 — 일반 카드 플레이로 떨어지지 않게 소비.
                else
                {
                    Event.current.Use();
                }
            }
        }

        // 2) 비호버 카드 — drawOrder 순서대로(바깥 → 안쪽) 회전시켜 드로우
        // 주의: GUIUtility.RotateAroundPivot은 pivot을 스크린 픽셀 좌표로 다루므로
        //       (newMat * baseMatrix 순서로 합성), 가상 1280×720 좌표인 center를 그대로
        //       넘기면 baseMatrix 스케일이 1이 아닐 때 좌우 비대칭이 발생한다.
        //       대신 baseMatrix 안쪽에서 가상 좌표 기준으로 회전 행렬을 직접 합성한다.
        Matrix4x4 baseMatrix = GUI.matrix;
        foreach (int i in drawOrder)
        {
            if (i == hoverIdx) continue;
            if (IsBeingDrawnInto(state.hand[i])) continue;

            var c = state.hand[i].data;
            bool canPlay = IsCardPlayable(state, c);

            float angle = startAngle + GetHandSlotForIndex(i) * anglePerCard;
            Vector2 center = FanCardCenter(fanOriginX, fanOriginY, fanRadius, angle);
            center.y += CardIdleBob(i);
            // 활성 카드(타겟팅 / 스왑 출발)는 hand fan에서 위로 살짝 들어올림 — "뽑혀 있다" 시그널.
            // 글로우 대신 위치 변화로 표현. ▼ 마커와 결합되면 글자/광원 노이즈 없이 명확.
            bool isActiveCard = (i == _targetingCardIndex || i == _swapFromCardIndex);
            if (isActiveCard) center.y -= 28f;
            var rect = new Rect(center.x - cardW * 0.5f, center.y - cardH * 0.5f, cardW, cardH);

            GUI.matrix = baseMatrix * RotateAroundPivotMatrix(angle, center);

            bool isFusionFanA = _fusionMaterialAPicked
                && _fusionMaterialA.isHand
                && _fusionMaterialA.index == i;
            // 융합 후보 카드 — 촉매/재료A가 아니면서 자격 만족(스테이지1: 모든 carnivore T<2, 스테이지2: A와 종/티어 일치).
            bool isFusionEligibleHand = fusionMode
                && i != _targetingCardIndex
                && !isFusionFanA
                && IsFusionMaterialEligible(null, i, isHand: true);
            // 스테이지 2: 촉매도 A도 후보도 아닌 손 카드는 융합과 무관 — 어둡게.
            bool fusionInactiveCard = fusionMode && _fusionMaterialAPicked
                && i != _targetingCardIndex && !isFusionFanA && !isFusionEligibleHand;
            // 화살표 source — 일반 타겟팅 카드 / 스왑 출발 카드 / 융합(A 미선택이면 촉매, A 선택 후 손이면 A 카드).
            if (i == _swapFromCardIndex)
            {
                _arrowSourceRect = rect;
                _arrowSourceValid = true;
            }
            else if (i == _targetingCardIndex)
            {
                if (!fusionMode || !_fusionMaterialAPicked)
                {
                    _arrowSourceRect = rect;
                    _arrowSourceValid = true;
                }
            }
            else if (isFusionFanA)
            {
                _arrowSourceRect = rect;
                _arrowSourceValid = true;
            }
            Color prevFanCol = GUI.color;
            if (fusionInactiveCard) GUI.color = new Color(0.22f, 0.22f, 0.24f, 0.78f);
            // 코스트도 카드 본체 layer에서 함께 그린다 — 손패가 겹치면 옆 카드의 본체가
            // 이 카드의 보석/숫자를 자연스럽게 가려, 코스트만 떠 보이는 현상이 사라진다.
            DrawCardFrame(rect, c, canPlay, drawCost: true, displayCost: EffectiveCost(state, c));
            GUI.color = prevFanCol;
            // 융합 후보/재료 A는 비후보 dim으로만 구분 — 양성 마커 제거.
        }
        GUI.matrix = baseMatrix;

        // 3) 호버 카드 — 회전 없이, 크게, 위로 올라옴 (맨 위에 그려져야 하므로 마지막)
        if (hoverIdx >= 0)
        {
            int i = hoverIdx;
            var c = state.hand[i].data;
            bool canPlay = IsCardPlayable(state, c);

            // 호버 카드는 부채꼴 위치와 무관하게 화면 하단에 고정 앵커해서 전체가 항상 보이게 함.
            // x는 부채꼴 위치 유지(손 위 어느 카드인지 직관적으로 보이게), y만 화면 하단 기준.
            // 숨김 진행도에 따라 함께 아래로 슬라이드. ← sticky hover 판정도 동일한 rect 사용.
            var hoverRect = ComputeHandHoverRect(GetHandSlotForIndex(i), startAngle, anglePerCard,
                fanOriginX, fanOriginY, fanRadius, cardW, cardH, hideOffset);

            bool isFusionHoverA = _fusionMaterialAPicked
                && _fusionMaterialA.isHand
                && _fusionMaterialA.index == i;
            // 호버 카드가 융합 후보(촉매/재료A 제외)면 글로우.
            bool isFusionEligibleHover = fusionMode
                && i != _targetingCardIndex
                && !isFusionHoverA
                && IsFusionMaterialEligible(null, i, isHand: true);
            // 스테이지 2 inactive — 호버 카드도 동일하게 dim.
            bool fusionInactiveHover = fusionMode && _fusionMaterialAPicked
                && i != _targetingCardIndex && !isFusionHoverA && !isFusionEligibleHover;
            // 호버된 융합 후보 손 카드는 화살표 끝점 스냅 대상 — 들어올린 hoverRect 중심으로 빨려간다.
            if (isFusionEligibleHover)
            {
                _arrowTargetRects.Add(hoverRect);
            }
            // 호버 자체가 카드 lift 효과를 주므로 활성 카드 별도 글로우 불필요.
            // 호버 중인 카드가 화살표 source면 들어올린 hoverRect에서 시작.
            // 융합은 A 미선택이면 촉매 카드, A 선택 후 손이면 A 카드가 source.
            if (i == _swapFromCardIndex)
            {
                _arrowSourceRect = hoverRect;
                _arrowSourceValid = true;
            }
            else if (i == _targetingCardIndex)
            {
                if (!fusionMode || !_fusionMaterialAPicked)
                {
                    _arrowSourceRect = hoverRect;
                    _arrowSourceValid = true;
                }
            }
            else if (isFusionHoverA)
            {
                _arrowSourceRect = hoverRect;
                _arrowSourceValid = true;
            }
            Color prevHoverCol = GUI.color;
            if (fusionInactiveHover) GUI.color = new Color(0.22f, 0.22f, 0.24f, 0.78f);
            DrawCardFrame(hoverRect, c, canPlay, drawCost: true, displayCost: EffectiveCost(state, c));
            GUI.color = prevHoverCol;
            // 융합 재료 A/후보 호버는 hand fan의 기본 hover 처리(카드 들어올림)로 충분 — 양성 마커 제거.

            // 융합 모드 클릭은 DrawHand 상단의 우선 핸들러에서 모두 처리됨 (Event.current.Use()로 소비).
            // 여기 도달했다는 건 융합 모드가 아니거나 클릭이 이미 소비된 상태.

            // 클릭 처리: 호버된 카드에서만. 증원 픽커 모달이 떠 있으면 손패 클릭은 픽커가 사이드 카드 클릭을
            // 가로채지 못하도록 막는다 — 픽커 패널 중앙은 들어올린 hoverRect와 위치가 겹칠 수 있다.
            if (canPlay && _reinforcePickerCardIndex < 0)
            {
                var ev = Event.current;
                if (ev.type == EventType.MouseDown && ev.button == 0 && hoverRect.Contains(ev.mousePosition))
                {
                    ev.Use();
                    int captured = i;
                    bool isSummon = c.cardType == CardType.SUMMON;
                    bool fieldFull = _battle.state.field.Count >= _battle.state.maxFieldSize;

                    if (CardNeedsReinforcePicker(c))
                    {
                        // 증원 카드 — 보유 공룡 그리드 모달을 띄우고 선택 대기.
                        _reinforcePickerCardIndex = captured;
                        _reinforcePickerScroll = Vector2.zero;
                        _targetingCardIndex = -1;
                        _swapFromCardIndex = -1;
                        _fusionMaterialAPicked = false;
                    }
                    else if (CardNeedsTarget(c))
                    {
                        _targetingCardIndex = captured;
                        _swapFromCardIndex = -1;
                        _fusionMaterialAPicked = false;
                    }
                    else if (isSummon && fieldFull)
                    {
                        // 필드 꽉 참 → 교체 모드 진입. 교체할 공룡 클릭 대기.
                        _swapFromCardIndex = captured;
                        _targetingCardIndex = -1;
                    }
                    else
                    {
                        _targetingCardIndex = -1;
                        _swapFromCardIndex = -1;
                        bool isAttack = IsAttackSpell(c);
                        if (isAttack)
                        {
                            // SFX는 클릭 즉시 — PlayCard가 화염구 임팩트까지 지연되므로 ResolveCard에서 재생하면 늦음.
                            string sfxKey = (c.subType == CardSubType.DEBUFF) ? "card_debuff" : "card_attack";
                            DianoCard.Audio.AudioManager.Instance?.PlaySFX(sfxKey);
                        }
                        _pending.Add(() => {
                            if (isAttack)
                            {
                                // 공격 카드: 모션/화염구 즉시 → 데미지(PlayCard)는 임팩트 시점까지 지연.
                                _playerView?.PlayAttack(ComputeAttackDir(-1), distance: 0.08f, duration: PlayerAttackDuration);
                                TriggerPlayerAttackFx(-1, attackDuration: PlayerAttackDuration);
                                StartCoroutine(DelayedPlayCardOnImpact(() => _battle.PlayCard(captured, -1)));
                            }
                            else
                            {
                                _battle.PlayCard(captured, -1);
                                if (isSummon)
                                    _playerView?.PlaySummon(ComputeAttackDir(-1));
                            }
                        });
                    }
                }
            }
        }
    }

    // 부드러운 ease-in-out 커브 (cubic). 0..1 입력을 0..1 출력으로 매핑 — 시작/끝은 천천히, 중간은 빠르게.
    private static float EaseInOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        return t < 0.5f
            ? 4f * t * t * t
            : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
    }

    // 사인 기반 ease-in-out — cubic보다 C∞ 부드러움. 도함수가 전 구간에서 매끄러워
    // 감속/가속 전환이 시각적으로 더 자연스럽다. 버림 애니에 사용.
    private static float EaseInOutSine(float t)
    {
        t = Mathf.Clamp01(t);
        return 0.5f - 0.5f * Mathf.Cos(Mathf.PI * t);
    }

    // 손패 숨김/표시 — 마우스 휠로 토글. 휠 다운 → 숨김(카드가 아래로 슬라이드), 휠 업 → 표시.
    // 오버레이(덱/유물/포션/증원)가 떠 있으면 휠은 그 모달의 스크롤로 가야 하므로 여기선 처리하지 않음.
    private void HandleHandHideWheelInput()
    {
        if (IsDrawFlyActive || IsReshuffleActive) return;
        if (AnyOverlayOpen) return;

        var ev = Event.current;
        if (ev.type != EventType.ScrollWheel) return;

        if (ev.delta.y > 0f && !_handHidden)
        {
            _handHidden = true;
            ev.Use();
        }
        else if (ev.delta.y < 0f && _handHidden)
        {
            _handHidden = false;
            ev.Use();
        }
    }

    // SUMMON 카드는 state.player.summonCostReduction(C132 등) 만큼 비용이 깎인다 — 최소 0.
    // 다른 카드 타입은 베이스 비용 그대로.
    private static int EffectiveCost(BattleState state, CardData c)
    {
        if (c == null) return 0;
        if (c.cardType != CardType.SUMMON) return c.cost;
        return System.Math.Max(0, c.cost - state.player.summonCostReduction);
    }

    private bool IsCardPlayable(BattleState state, CardData c)
    {
        if (state.IsOver || _endTurnAnimating || IsDrawFlyActive) return false;
        if (state.player.mana < EffectiveCost(state, c)) return false;
        // SUMMON은 슬롯 꽉 차도 교체 모드로 플레이 가능하므로 별도 필드 체크 없음.
        // ALLY 타겟 카드(수호 마법) / ALL_ALLY 방어는 필드에 공룡 없으면 플레이 불가.
        if (CardNeedsAllyTarget(c) && state.field.Count == 0) return false;
        if (c.cardType == CardType.MAGIC && c.subType == CardSubType.DEFENSE
            && c.target == TargetType.ALL_ALLY && state.field.Count == 0) return false;
        // 융합 카드: 필드 + 손 조합에 같은 종·같은 티어 육식이 최소 2마리 있어야 재료 확보 가능.
        if (CardNeedsFusionTargets(c) && !HasAnyFusionPair(state)) return false;
        // C131 발톱 폭우: 필드 ATK 합산이라 빈 필드면 데미지 0 — 마나만 낭비되므로 차단.
        if (c.id == "C131" && state.field.Count == 0) return false;
        return true;
    }

    /// <summary>필드 + 손 조합에 융합 가능한 같은 종·같은 티어 육식 쌍이 하나라도 있는지 판정.
    /// 엄밀하게는 코스트까지 고려해야 하지만 MVP에선 재료 존재만 체크 — 실제 플레이 시점에 코스트 재검증됨.</summary>
    private static bool HasAnyFusionPair(BattleState state)
    {
        // (originCardId, tier) → 개수
        var counts = new Dictionary<(string, int), int>();
        foreach (var s in state.field)
        {
            if (s == null || s.IsDead) continue;
            if (s.data.subType != CardSubType.CARNIVORE) continue;
            int tier = GetCarnivoreTierFromCardId(s.data.id);
            if (tier >= 2) continue; // T2는 진화 불가
            var key = (s.originCardId, tier);
            counts.TryGetValue(key, out int n);
            counts[key] = n + 1;
        }
        foreach (var inst in state.hand)
        {
            var c = inst.data;
            if (c.cardType != CardType.SUMMON) continue;
            if (c.subType != CardSubType.CARNIVORE) continue;
            var key = (c.id, 0); // 손 카드는 항상 T0, originCardId == data.id
            counts.TryGetValue(key, out int n);
            counts[key] = n + 1;
        }
        // 같은 티어 쌍 확인
        foreach (var kvp in counts) if (kvp.Value >= 2) return true;
        // 교차 티어(T1+T0) 쌍 확인
        foreach (var kvp in counts)
        {
            if (kvp.Key.Item2 == 1 && counts.TryGetValue((kvp.Key.Item1, 0), out _))
                return true;
        }
        return false;
    }

    private static Vector2 FanCardCenter(float originX, float originY, float radius, float angleDeg)
    {
        float rad = angleDeg * Mathf.Deg2Rad;
        return new Vector2(originX + Mathf.Sin(rad) * radius,
                           originY - Mathf.Cos(rad) * radius);
    }

    // 손패 호버 시 확대되어 화면 하단에 고정 앵커되는 카드의 화면 사각형.
    // hover 그리기와 sticky hover 판정이 같은 rect를 봐야 마우스가 확대 카드 위에 있는 동안 hover가 풀리지 않는다.
    // slot은 hand index가 아닌 phantom-보정 슬롯 (소진 phantom slot 적용 시 float 값).
    private const float HandHoverScale = 1.18f;
    private const float HandHoverBottomPad = 20f;
    private static Rect ComputeHandHoverRect(float slot, float startAngle, float anglePerCard,
                                             float fanOriginX, float fanOriginY, float fanRadius,
                                             float cardW, float cardH, float hideOffset)
    {
        float angle = startAngle + slot * anglePerCard;
        Vector2 fanCenter = FanCardCenter(fanOriginX, fanOriginY, fanRadius, angle);
        float hw = cardW * HandHoverScale;
        float hh = cardH * HandHoverScale;
        return new Rect(fanCenter.x - hw * 0.5f, RefH - hh - HandHoverBottomPad + hideOffset, hw, hh);
    }

    // 손패 카드의 idle 수직 호흡 — 카드마다 위상이 어긋나 자연스럽게 출렁인다.
    private static float CardIdleBob(int i)
    {
        return Mathf.Sin(Time.time * 1.6f + i * 0.55f) * 1.6f;
    }

    private static Matrix4x4 RotateAroundPivotMatrix(float angleDeg, Vector2 pivot)
    {
        Vector3 p = new Vector3(pivot.x, pivot.y, 0f);
        return Matrix4x4.Translate(p)
             * Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, angleDeg))
             * Matrix4x4.Translate(-p);
    }

    private static bool PointInRotatedRect(Vector2 p, Vector2 center, float w, float h, float angleDeg)
    {
        float rad = -angleDeg * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);
        Vector2 d = p - center;
        Vector2 local = new Vector2(d.x * cos - d.y * sin, d.x * sin + d.y * cos);
        return Mathf.Abs(local.x) <= w * 0.5f && Mathf.Abs(local.y) <= h * 0.5f;
    }

    /// <summary>
    /// YJ 통합 프레임 (2026-04-28).
    /// 카드 종류별 프리렌더 PNG 한 장으로 외곽/명판/아트 윈도우/코스트 보석을 모두 처리한다.
    /// 그리는 순서: 1) 아트 → 2) Type Frame (위에 덮어 아치 윈도우 안에 아트가 보임) →
    ///             3) CostGem 디스크/링 (선택) → 4) 코스트 숫자 → 5) 카드명/카테고리/본문.
    /// 희귀도는 더 이상 시각적으로 구분되지 않는다.
    /// </summary>
    private void DrawCardFrame(Rect rect, CardData c, bool canPlay, bool drawCost, bool slotOnly = false, int displayCost = -1)
    {
        var prevColor = GUI.color;
        Color dim = canPlay ? Color.white : cardDisabledDim;

        // 1) 아트 — Type Frame 뒤에 깔아 아치형 아트 윈도우로 보이게 한다.
        var artRect = RectFromPct(rect, cardArtRectV2Pct);
        if (!slotOnly)
        {
            GUI.color = MultColor(cardArtTint, dim);
            if (c != null && _cardSprites.TryGetValue(c.id, out var cardTex))
            {
                GUI.DrawTexture(artRect, cardTex, ScaleMode.ScaleAndCrop, alphaBlend: true);
            }
            else
            {
                FillRect(artRect, cardArtPlaceholderTint);
            }
        }
        else
        {
            FillRect(artRect, cardArtPlaceholderTint);
        }

        // 2) Type Frame — 카드 종류별 통합 프레임 한 장. 색은 PNG에 이미 입혀져 있다.
        Texture2D frameTex = (c != null && !slotOnly) ? GetCardTypeFrameTexture(c) : _frameUtility;
        if (frameTex != null)
        {
            GUI.color = dim;
            GUI.DrawTexture(rect, frameTex, ScaleMode.StretchToFill, alphaBlend: true);
        }

        GUI.color = prevColor;

        if (slotOnly)
        {
            // 슬롯 프리뷰: 카드 데이터 텍스트/코스트는 생략.
            return;
        }

        // 3) 코스트 — 프레임의 보석 위에 숫자(필요 시 디스크/링) 오버레이.
        if (drawCost && c != null)
        {
            DrawCardCost(rect, c, canPlay, displayCost);
        }

        if (c == null) return;

        // 패시브 아이콘 — SUMMON 카드 우상단, 코스트 보석과 대칭. 호버 시 툴팁 표시.
        if (c.cardType == CardType.SUMMON
            && c.passiveType != DinoPassiveType.NONE
            && _passiveIcons.TryGetValue(c.passiveType, out var passiveHandTex))
        {
            float ps = rect.width * 0.13f;
            float pm = rect.width * 0.04f;
            var pr = new Rect(rect.xMax - ps - pm, rect.y + pm, ps, ps);
            (string ptitle, string pbody) = GetDinoPassiveTooltip(c);
            DrawIconChip(pr, passiveHandTex, 0, ptitle, pbody);
        }

        // 4) 카드명 — 폰트 크기는 카드 폭에 비례해 자동 스케일.
        // 손패/호버/치트 어디서든 같은 시각 비율이 보이도록 reference width(187)로 정규화한다.
        // 그 다음 텍스트 폭이 rect 폭을 넘으면 추가 축소(두 줄 깨짐 방지).
        const float kReferenceCardW = 187f; // 손패 호버 카드 폭 (157.5 × 1.18 hoverScale)
        float fontScale = rect.width / kReferenceCardW;

        var nameRect = RectFromPct(rect, cardNameOnRibbonRectPct);
        int prevNameSize = _cardNameStyle.fontSize;
        Color prevNameCol = _cardNameStyle.normal.textColor;
        int baseNameSize = drawCost ? cardNameFontSize : cardNameFontSizeSmall;
        int targetNameSize = Mathf.Max(6, Mathf.RoundToInt(baseNameSize * fontScale));
        string nameText = GetCardTypeLabel(c);
        _cardNameStyle.fontSize = targetNameSize;
        Vector2 measured = _cardNameStyle.CalcSize(new GUIContent(nameText));
        if (measured.x > nameRect.width && measured.x > 0f)
        {
            float shrink = nameRect.width / measured.x;
            _cardNameStyle.fontSize = Mathf.Max(6, Mathf.FloorToInt(targetNameSize * shrink));
        }
        Color nameCol = canPlay ? cardNameTextTint : cardNameDisabledColor;
        DrawTextWithOutline(nameRect, nameText, _cardNameStyle, nameCol, cardNameOutline, cardNameOutlineThickness);
        _cardNameStyle.fontSize = prevNameSize;
        _cardNameStyle.normal.textColor = prevNameCol;

        // 본문 — 하단 패널 (ATK/HP 또는 짧은 설명). 외곽선으로 살짝 굵기 강조.
        int prevBodySize = _cardDescStyle.fontSize;
        Color prevBodyCol = _cardDescStyle.normal.textColor;
        _cardDescStyle.fontSize = Mathf.Max(6, Mathf.RoundToInt(cardBodyFontSize * fontScale));
        Color bodyCol = canPlay ? cardBodyTextColor : cardNameDisabledColor;
        DrawTextWithOutline(RectFromPct(rect, cardBodyV2RectPct), GetCardBody(c), _cardDescStyle, bodyCol, cardBodyOutline, cardBodyOutlineThickness);
        _cardDescStyle.fontSize = prevBodySize;
        _cardDescStyle.normal.textColor = prevBodyCol;
    }

    private static Color MultColor(Color a, Color b)
    {
        return new Color(a.r * b.r, a.g * b.g, a.b * b.b, a.a * b.a);
    }

    /// <summary>
    /// 레이어 PNG 를 원형 샘플링으로 외곽선을 그리고 위에 fill 을 덮는다.
    /// Stretching 이 아니라 offset 이라 복잡한 실루엣도 자연스럽게 외곽선이 따라가고,
    /// 원형 샘플링이라 커브 구간도 균일한 두께로 둘러싼다.
    /// </summary>
    private void DrawLayerWithBorder(Texture2D tex, Rect r, Color fillTint, Color dim, LayerBorderConfig border)
    {
        if (tex == null) return;
        if (border != null && border.enabled && border.color.a > 0f && border.widthPx > 0f)
        {
            float w = border.widthPx;
            int n = Mathf.Max(4, border.samples);
            GUI.color = MultColor(border.color, dim);
            for (int i = 0; i < n; i++)
            {
                float angle = (i * 2f * Mathf.PI) / n;
                float dx = Mathf.Cos(angle) * w;
                float dy = Mathf.Sin(angle) * w;
                var offsetRect = new Rect(r.x + dx, r.y + dy, r.width, r.height);
                GUI.DrawTexture(offsetRect, tex, ScaleMode.StretchToFill, alphaBlend: true);
            }
        }
        GUI.color = MultColor(fillTint, dim);
        GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, alphaBlend: true);
    }

    // Cheat: 카드 한 장만 큰 사이즈로 그리기 — 프레임 디자인 확인용.
    // slotOnly=true 이면 카드 데이터 생략 — 빈 슬롯(프레임)만 그려서 rect 튜닝용.
    public void DrawCardPreview(Rect rect, CardData c, bool slotOnly = false)
    {
        EnsureStyles();
        if (slotOnly)
        {
            DrawCardFrame(rect, null, canPlay: true, drawCost: false, slotOnly: true);
            return;
        }
        DrawCardFrame(rect, c, canPlay: true, drawCost: true);
    }

    private void DrawCardCost(Rect rect, CardData c, bool canPlay, int displayCost = -1)
    {
        // 코스트 위치 — 프레임의 좌상단 보석 위. Inspector cardCostOrbPct (centerX, centerY, sizeFrac).
        float orbSize = rect.width * cardCostOrbPct.z;
        float orbCx = rect.x + rect.width  * cardCostOrbPct.x;
        float orbCy = rect.y + rect.height * cardCostOrbPct.y;
        var orbRect = new Rect(orbCx - orbSize * 0.5f, orbCy - orbSize * 0.5f, orbSize, orbSize);

        // displayCost < 0 → 기본 c.cost. 손에서는 C132 등으로 감면된 effectiveCost를 전달한다.
        int shownCost = displayCost >= 0 ? displayCost : c.cost;
        bool reduced = displayCost >= 0 && displayCost < c.cost;

        // 숫자만 그리기 — 프레임 PNG에 보석이 이미 그려져 있으므로 디스크/링/마나오브는 생략.
        // 감면 적용 시 텍스트를 그린 톤으로 강조 (자체 비활성 회색이 우선).
        Color textCol = canPlay
            ? (reduced ? new Color(0.55f, 0.95f, 0.55f, 1f) : cardCostTextColor)
            : cardCostDisabledColor;
        int prevFontSize = _cardCostStyle.fontSize;
        _cardCostStyle.fontSize = Mathf.RoundToInt(orbSize * cardCostFontSizeRatio);
        float costTextOffX = rect.width * cardCostTextOffsetPct.x;
        float costTextOffY = rect.height * cardCostTextOffsetPct.y;
        float costShrink = orbSize * cardCostTextRectShrinkPct;
        var costTextRect = new Rect(
            orbRect.x + costTextOffX + costShrink * 0.5f,
            orbRect.y + costTextOffY + costShrink * 0.5f,
            orbRect.width - costShrink,
            orbRect.height - costShrink);
        DrawTextWithOutline(costTextRect, shownCost.ToString(), _cardCostStyle, textCol, cardCostOutline, cardCostOutlineThickness);
        _cardCostStyle.fontSize = prevFontSize;
    }

    private static void DrawTextWithOutline(Rect rect, string text, GUIStyle style,
                                            Color textColor, Color outlineColor, float thickness)
    {
        var prev = GUI.color;
        var prevNormal    = style.normal.textColor;
        var prevHover     = style.hover.textColor;
        var prevActive    = style.active.textColor;
        var prevFocused   = style.focused.textColor;
        var prevOnNormal  = style.onNormal.textColor;
        var prevOnHover   = style.onHover.textColor;
        var prevOnActive  = style.onActive.textColor;
        var prevOnFocused = style.onFocused.textColor;

        // 마우스가 라벨 위에 있을 때 GUIStyle은 hover state의 textColor를 적용한다.
        // normal만 갱신하면 init 시점의 hover 색이 그대로 보이므로(예: 본문이 검정으로 다시 보임),
        // 그릴 때마다 모든 state를 함께 맞춰준다.
        SetAllStateColors(style, outlineColor);
        GUI.color = outlineColor;
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                var r = new Rect(rect.x + dx * thickness, rect.y + dy * thickness, rect.width, rect.height);
                GUI.Label(r, text, style);
            }

        SetAllStateColors(style, textColor);
        GUI.color = textColor;
        GUI.Label(rect, text, style);

        style.normal.textColor    = prevNormal;
        style.hover.textColor     = prevHover;
        style.active.textColor    = prevActive;
        style.focused.textColor   = prevFocused;
        style.onNormal.textColor  = prevOnNormal;
        style.onHover.textColor   = prevOnHover;
        style.onActive.textColor  = prevOnActive;
        style.onFocused.textColor = prevOnFocused;
        GUI.color = prev;
    }

    private static void SetAllStateColors(GUIStyle s, Color c)
    {
        s.normal.textColor    = c;
        s.hover.textColor     = c;
        s.active.textColor    = c;
        s.focused.textColor   = c;
        s.onNormal.textColor  = c;
        s.onHover.textColor   = c;
        s.onActive.textColor  = c;
        s.onFocused.textColor = c;
    }

    // 카드 종류별 통합 프레임 — 색은 PNG에 이미 입혀져 있으므로 텍스처 선택만.
    private Texture2D GetCardTypeFrameTexture(CardData c)
    {
        if (c == null) return _frameUtility;
        return c.cardType switch
        {
            CardType.SUMMON => _frameSummon,
            CardType.MAGIC => _frameMagic,
            CardType.BUFF => _frameBuff,
            CardType.UTILITY => _frameUtility,
            CardType.RITUAL => _frameRitual,
            _ => _frameUtility,
        };
    }

    private static string GetCardTypeLabel(CardData c) => c.name;

    private static string GetCardBody(CardData c)
    {
        if (c.cardType == CardType.SUMMON)
            return $"ATK {c.attack}\nHP {c.hp}";
        return ShortDesc(c);
    }

    private static string ShortDesc(CardData c)
    {
        if (string.IsNullOrEmpty(c.description)) return "";
        // 문장 구분점("。 " / ". ")은 줄바꿈으로 치환하고 끝의 마침표는 제거 — 카드 본문 가독성.
        string s = c.description
            .Replace(". ", "\n")
            .Replace("。 ", "\n")
            .TrimEnd('.', '。', ' ');
        return s.Length > 80 ? s.Substring(0, 80) + "…" : s;
    }

    private void DrawEndTurn(BattleState state)
    {
        GUI.enabled = !state.IsOver && !_endTurnAnimating && !IsDrawFlyActive;

        // 베이스 사이즈(살짝 작아짐) + 호버 시 확대
        var baseRect = new Rect(RefW - endTurnButtonRightOffset,
                                RefH - endTurnButtonBottomOffset,
                                endTurnButtonWidth, endTurnButtonHeight);
        bool hovered = GUI.enabled && baseRect.Contains(Event.current.mousePosition);

        // 호버 스케일 — 즉각적인 펌프 느낌을 위해 약간 보간 (Repaint에서만 누적)
        float targetScale = hovered ? 1.12f : 1.0f;
        if (Event.current.type == EventType.Repaint)
            _endTurnHoverScale = Mathf.Lerp(_endTurnHoverScale, targetScale, Time.unscaledDeltaTime * 14f);

        float w = baseRect.width * _endTurnHoverScale;
        float h = baseRect.height * _endTurnHoverScale;
        var rect = new Rect(baseRect.center.x - w * 0.5f, baseRect.center.y - h * 0.5f, w, h);

        var endTurnTex = GetCharacterEndTurnButton();
        if (endTurnTex != null)
        {
            var prev = GUI.color;
            GUI.color = GUI.enabled ? Color.white : new Color(1f, 1f, 1f, 0.5f);
            GUI.DrawTexture(rect, endTurnTex, ScaleMode.ScaleToFit, alphaBlend: true);
            GUI.color = prev;

            // 클릭 hit-test는 baseRect로 고정 — Lerp로 매 프레임 변하는 rect를 쓰면 hot control이 흔들려 클릭 누락됨
            if (GUI.Button(baseRect, GUIContent.none, GUIStyle.none))
            {
                _targetingCardIndex = -1;
                _swapFromCardIndex = -1;
                _pending.Add(() => StartCoroutine(EndTurnCoroutine()));
            }
        }
        else if (GUI.Button(baseRect, "END\nTURN", _buttonStyle))
        {
            _targetingCardIndex = -1;
            _swapFromCardIndex = -1;
            _pending.Add(() => StartCoroutine(EndTurnCoroutine()));
        }

        GUI.enabled = true;
    }

    // =========================================================
    // EndTurn 애니메이션 코루틴
    // =========================================================

    private IEnumerator EndTurnCoroutine()
    {
        if (_battle == null || _battle.state == null) yield break;
        DianoCard.Tutorial.TutorialEvents.NotifyTurnEnded();
        _endTurnAnimating = true;
        var state = _battle.state;

        // Phase 1: 아직 공격 안 한 공룡들 자동 랜덤 공격.
        var summons = new List<SummonInstance>(state.field);
        foreach (var s in summons)
        {
            if (s.IsDead || !s.CanAttack) continue;
            if (state.AllEnemiesDead) break;
            int targetIdx = _battle.PickRandomTargetIndex();
            if (targetIdx < 0) break;
            yield return AnimateLunge(s, isSummon: true);
            int currentSIdx = state.field.IndexOf(s);
            if (currentSIdx < 0) continue;
            _battle.CommandSummonAttack(currentSIdx, targetIdx);
            yield return new WaitForSeconds(BetweenAttacksPause);
        }

        // 적 전부 사망 → 전투 종료 감지에 맡기고 코루틴 종료
        if (state.AllEnemiesDead)
        {
            _endTurnAnimating = false;
            _attackingUnit = null;
            yield break;
        }

        // 플레이어 상태이상 틱 (적 행동 전)
        _battle.TickPlayerStatuses();
        if (state.PlayerLost)
        {
            _endTurnAnimating = false;
            _attackingUnit = null;
            yield break;
        }

        // 적이 차례대로 행동 — 공격 계열만 lunge 애니메이션.
        var enemies = new List<EnemyInstance>(state.enemies);
        foreach (var e in enemies)
        {
            if (e.IsDead) continue;
            if (state.PlayerLost) break;

            // 카운트다운/속박/각성 중인 적은 실제 행동 없음 → 공격 애니메이션도 스킵.
            bool active = e.telegraphRemaining <= 0 && e.stunTurns <= 0;

            // MULTI_ATTACK은 hit별로 애니메이션 반복 — 시각적으로 N회 공격이 명확히 보이게.
            // 데미지 처리는 DealEnemyMultiAttackHit가 hit 단위로 적용, DoEnemyAction은 호출하지 않음.
            if (active && e.intentAction == EnemyAction.MULTI_ATTACK)
            {
                int hits = Mathf.Max(1, e.intentCount);
                for (int i = 0; i < hits; i++)
                {
                    if (e.IsDead || state.PlayerLost) break;
                    yield return AnimateEnemyAttack(e);
                    _battle.DealEnemyMultiAttackHit(e);
                    if (i < hits - 1) yield return new WaitForSeconds(MultiAttackHitGap);
                }
            }
            else
            {
                // 카운트다운/속박 진행 중이면 실제 데미지 발동 안 함 → 모션도 재생 안 함.
                if (IsAttackAction(e.intentAction) && active)
                    yield return AnimateEnemyAttack(e);
                else if (active && IsNonAttackActionMotionWorthy(e.intentAction))
                    yield return AnimateEnemyAction(e);
                _battle.DoEnemyAction(e);
            }

            // 적 상태이상 틱: DoT 데미지 + 독/출혈/약화/취약/기절 감소
            _battle.TickEnemyStatuses(e);

            yield return new WaitForSeconds(BetweenAttacksPause);
        }

        if (state.PlayerLost)
        {
            _endTurnAnimating = false;
            _attackingUnit = null;
            yield break;
        }

        // 소환수 침묵/도발 카운트다운 — 적 행동 후, 다음 턴 시작 전
        _battle.TickSummonStatuses();

        // Phase 3: 손패 → (중앙 모임 → 머뭄 → 더미) 3단계 비행 애니메이션
        if (state.hand.Count > 0)
        {
            BeginDiscardFlyAnimation(state);

            // 마지막 카드가 착지할 때까지 대기
            int n = _discardFlyCards.Count;
            float wait = DiscardGatherDuration + DiscardHoldDuration
                       + DiscardDisperseDuration + Mathf.Max(0, n - 1) * DiscardDisperseStagger
                       + 0.05f;
            yield return new WaitForSeconds(wait);

            _battle.EndTurnCleanup();
            EndDiscardFlyAnimation();
        }
        else
        {
            _battle.EndTurnCleanup();
        }

        // Phase 4: 다음 턴 시작 — StartNextTurnIfAlive가 내부에서 Draw를 호출하고
        // 덱이 비어있으면 discard→deck reshuffle까지 해버린다. 애니메이션을 위해
        // 호출 전 상태를 스냅샷해두고, 호출 후 상태 변화를 보고 reshuffle/draw를 분기 재생.
        int handBeforeNextTurn = state.hand.Count;
        int deckBeforeNextTurn = state.deck.Count;
        int discardBeforeNextTurn = state.discard.Count;
        _battle.StartNextTurnIfAlive();

        // 덱이 비어있었고 지금은 차있다면 reshuffle이 일어난 것.
        // 이 경우 버림 → 덱 스트림 애니메이션을 먼저 재생.
        bool reshuffled = deckBeforeNextTurn == 0 && discardBeforeNextTurn > 0 && state.deck.Count > 0;
        if (reshuffled && !state.IsOver)
        {
            BeginReshuffleAnimation(discardBeforeNextTurn);
            float reshuffleWait = GetReshuffleTotalDuration() + 0.1f;
            yield return new WaitForSeconds(reshuffleWait);
            EndReshuffleAnimation();
        }

        if (!state.IsOver && state.hand.Count > handBeforeNextTurn)
        {
            BeginDrawFlyAnimation(state, handBeforeNextTurn);
            float drawWait = GetDrawFlyTotalDuration() + 0.05f;
            yield return new WaitForSeconds(drawWait);
            EndDrawFlyAnimation();
        }

        _endTurnAnimating = false;
        _attackingUnit = null;
    }

    /// <summary>적 인텐트 액션이 "공격"에 해당해서 lunge 애니메이션을 재생해야 하는지.</summary>
    private static bool IsAttackAction(EnemyAction a)
    {
        return a == EnemyAction.ATTACK
            || a == EnemyAction.MULTI_ATTACK
            || a == EnemyAction.DRAIN
            || a == EnemyAction.COUNTDOWN_ATTACK
            || a == EnemyAction.COUNTDOWN_AOE;
    }

    // 공격이 아닌 액션(DEFEND/BUFF/SUMMON/POISON/WEAK/HEAL/...)은 모두 가벼운 호핑 모션 재생 대상.
    // IDLE/UNKNOWN은 "행동 안 함"이므로 모션 없음.
    private static bool IsNonAttackActionMotionWorthy(EnemyAction a)
    {
        return a != EnemyAction.IDLE
            && a != EnemyAction.UNKNOWN
            && !IsAttackAction(a);
    }

    /// <summary>
    /// 적 비공격 행동(방어/버프/소환/디버프 등)용 가벼운 호핑 모션. 스프라이트 스왑 없이
    /// transform Y만 살짝 떴다 복귀. BattleEntityView가 없으면 모션 생략.
    /// </summary>
    private IEnumerator AnimateEnemyAction(EnemyInstance e)
    {
        if (_enemyViews.TryGetValue(e, out var view) && view != null)
        {
            view.PlayAction();
            yield return new WaitForSeconds(0.4f);
        }
    }

    /// <summary>
    /// 적의 공격 애니메이션 — BattleEntityView가 있으면 world-space PlayAttack,
    /// 없으면 IMGUI lunge 폴백.
    /// </summary>
    private IEnumerator AnimateEnemyAttack(EnemyInstance e)
    {
        if (_enemyViews.TryGetValue(e, out var view) && view != null)
        {
            // 타겟(플레이어) 방향 + 거리 동적 계산.
            Vector3 dir = Vector3.left;
            float distToTarget = 1.5f;
            if (_playerView != null)
            {
                Vector3 toTarget = _playerView.transform.position - view.transform.position;
                if (toTarget.sqrMagnitude > 0.01f)
                {
                    dir = toTarget.normalized;
                    distToTarget = toTarget.magnitude;
                }
            }

            // E901 P1/P2: 제자리 스윙 + 검은 초승달 투사체(라인하르트 화염강타 톤).
            // 보스가 플레이어에게 다가가지 않음 — 발만 살짝 디딘 swing 모션(distance 0.3).
            // strike 페이즈 진입 시점에 BossProjectile.SpawnCrescent로 검은 초승달을 발사하고,
            // 투사체 비행 시간만큼 추가 대기 → DealAttack(피격) 시점이 명중 시점과 일치.
            bool isE901P1 = e.data.id == "E901" && e.currentPhase < 3;
            if (isE901P1)
            {
                // 묵직한 스윙 — duration 1.5s. windup(~0.45s)에서 검을 뒤로 충분히 당기는 시간 확보.
                const float swingDuration = 1.5f;
                DianoCard.Audio.AudioManager.Instance?.PlaySFX("attack");
                view.PlayAttack(dir, distance: 0.30f, duration: swingDuration);

                // strike 페이즈 끝(duration의 45%) — 검이 가장 앞쪽에 도달해 정점 찍은 순간 발사.
                // BattleEntityView 페이즈: 0~30 windup, 30~45 strike, 45~80 extended, 80~100 return.
                yield return new WaitForSeconds(swingDuration * 0.45f);

                // 검 끝 위치 추정 — 보스 sprite bounds 기반: 위쪽 55% + 앞쪽 50%.
                var bossSr = view.GetComponent<SpriteRenderer>();
                float bossH = (bossSr != null && bossSr.bounds.size.y > 0.001f)
                    ? bossSr.bounds.size.y
                    : 2.0f;
                // 검 끝 — 보스 어깨~머리 사이(높이 70%) → 살짝만 위에서 내려오는 완만한 각도.
                Vector3 spawnPos = view.transform.position
                                 + Vector3.up * (bossH * 0.70f)
                                 + dir * (bossH * 0.50f);
                Vector3 hitPos;
                if (_playerView != null)
                {
                    var psr = _playerView.GetComponent<SpriteRenderer>();
                    if (psr != null && psr.sprite != null)
                    {
                        // sprite 중심에서 +15% 위 ≈ 가슴/얼굴 부근(전체 높이의 65% 위치).
                        Bounds b = psr.bounds;
                        hitPos = b.center + Vector3.up * (b.size.y * 0.15f);
                    }
                    else hitPos = _playerView.transform.position;
                }
                else
                {
                    hitPos = view.transform.position + dir * Mathf.Max(distToTarget, 1.5f);
                }

                // 라인하르트 화염강타 톤 — 큰 초승달이 화면을 가로지름. 캐릭터보다 큰 범위감.
                float projHeight = Mathf.Clamp(distToTarget * 0.32f, 1.8f, 2.8f);
                // 빠른 비행 — 화염강타 속도감.
                float flightTime = Mathf.Clamp(distToTarget * 0.09f, 0.35f, 0.55f);
                DianoCard.Battle.BossProjectile.SpawnCrescent(
                    spawnPos, hitPos,
                    duration: flightTime,
                    worldHeight: projHeight,
                    sortingOrder: 110);

                // 투사체 도착 직후 yield 종료 → DoEnemyAction → DealAttack → PlayHit.
                yield return new WaitForSeconds(flightTime + 0.05f);
            }
            else
            {
                DianoCard.Audio.AudioManager.Instance?.PlaySFX("attack");
                view.PlayAttack(dir);
                yield return new WaitForSeconds(0.55f);
            }
        }
        else
        {
            DianoCard.Audio.AudioManager.Instance?.PlaySFX("attack");
            yield return AnimateLunge(e, isSummon: false);
        }
    }

    /// <summary>
    /// 단일 유닛이 lunge 모션을 수행. _attackingUnit / _attackProgress를 갱신해서
    /// DrawSummon/DrawEnemy가 위치 오프셋을 적용하게 함.
    /// </summary>
    private IEnumerator AnimateLunge(object unit, bool isSummon)
    {
        _attackingUnit = unit;
        _attackProgress = 0f;

        float elapsed = 0f;
        while (elapsed < LungeDuration)
        {
            elapsed += Time.deltaTime;
            _attackProgress = Mathf.Clamp01(elapsed / LungeDuration);
            yield return null;
        }

        _attackProgress = 0f;
        _attackingUnit = null;
    }

    // =========================================================
    // 손패 → 버린 더미 비행 애니메이션
    // =========================================================

    // 현재 손패의 각 카드 위치/각도를 캡처해서 _discardFlyCards에 채우고
    // Time.time 기준으로 애니메이션을 시작한다. DrawHand는 비활성 상태가 된다.
    private void BeginDiscardFlyAnimation(BattleState state)
    {
        _discardFlyCards.Clear();
        _discardBaseCount = state.discard.Count;

        float cardW = handCardWidth;
        float cardH = handCardHeight;

        int n = state.hand.Count;
        if (n == 0) return;

        // DrawHand와 동일한 부채꼴 기하 — 현재 숨김 오프셋도 그대로 반영해서
        // 캡처 시점의 실제 화면 위치에서 카드가 날아가는 것처럼 보이게 함.
        float easedHide = EaseInOutCubic(_handHideProgress);
        float hideOffset = easedHide * HandHideDistance;
        float centerCardY = RefH - cardH * 0.5f + handBottomOffset + hideOffset;
        float fanRadius = handFanRadius;
        float fanOriginX = RefW * 0.5f;
        float fanOriginY = centerCardY + fanRadius;

        float anglePerCard = handAnglePerCard;
        float totalAngle = (n - 1) * anglePerCard;
        float startAngleDeg = -totalAngle * 0.5f;

        // 가운데 카드부터 바깥쪽 순서로 순차 날아가게 — 중앙이 먼저 뜨고 양옆이 뒤따름
        float midIdx = (n - 1) * 0.5f;
        var order = new int[n];
        for (int k = 0; k < n; k++) order[k] = k;
        System.Array.Sort(order, (a, b) => Mathf.Abs(a - midIdx).CompareTo(Mathf.Abs(b - midIdx)));

        // 모일 위치 — 화면 중앙 기준으로 좌우 균등하게 배치, 원래 순서(i) 기준으로 나열.
        float gatherCenterX = RefW * 0.5f;
        float gatherMid = (n - 1) * 0.5f;

        for (int k = 0; k < n; k++)
        {
            int i = order[k];
            float angle = startAngleDeg + i * anglePerCard;
            Vector2 center = FanCardCenter(fanOriginX, fanOriginY, fanRadius, angle);

            // 각 카드의 최종 "모임" 위치 — i 기준 좌우 정렬.
            float gx = gatherCenterX + (i - gatherMid) * DiscardGatherSpacing;
            // 약간의 Y 편차로 겹침 느낌 (중앙 카드가 살짝 앞으로 나옴)
            float gy = DiscardGatherCenterY - Mathf.Abs(i - gatherMid) * 2f;

            _discardFlyCards.Add(new DiscardFlyCard
            {
                data = state.hand[i].data,
                startCenter = center,
                startAngleDeg = angle,
                gatherTarget = new Vector2(gx, gy),
                // 중앙(k=0) 카드부터 먼저 버려지고 바깥으로 갈수록 뒤따라 감
                disperseDelay = k * DiscardDisperseStagger,
            });
        }

        _discardAnimStartTime = Time.time;
    }

    private void EndDiscardFlyAnimation()
    {
        _discardFlyCards.Clear();
        _discardAnimStartTime = -1f;
        _discardBaseCount = 0;
    }

    private bool IsDiscardFlyActive => _discardAnimStartTime >= 0f && _discardFlyCards.Count > 0;

    // 모이는 단계가 끝나는 시각 (애니 시작 기준)
    private const float DiscardGatherEndLocal = DiscardGatherDuration;
    private const float DiscardHoldEndLocal   = DiscardGatherDuration + DiscardHoldDuration;

    // 카드 i가 실제 더미에 착지하는 시각 (애니 시작 기준)
    private float DiscardLandLocalTime(int cardIndex)
    {
        return DiscardHoldEndLocal + _discardFlyCards[cardIndex].disperseDelay + DiscardDisperseDuration;
    }

    // 버린 더미 UI에 표시할 카운트 — 애니메이션 중에는 착지한 카드 수만큼만 더해줘서
    // 숫자가 한 장씩 틱틱 올라가는 것처럼 보이게 함.
    private int GetDiscardDisplayCount(BattleState state)
    {
        // reshuffle 중엔 버린 더미가 점점 줄어드는 것처럼 보여야 함 (_reshuffleTotalCards → 0)
        if (IsReshuffleActive)
        {
            return Mathf.Max(0, _reshuffleTotalCards - GetReshuffleLandedCount());
        }
        if (!IsDiscardFlyActive) return state.discard.Count;
        int landed = 0;
        float localNow = Time.time - _discardAnimStartTime;
        for (int i = 0; i < _discardFlyCards.Count; i++)
        {
            if (localNow >= DiscardLandLocalTime(i)) landed++;
        }
        return _discardBaseCount + landed;
    }

    // 가장 최근 "착지" 이후 경과 시간을 바탕으로 한 뱃지 펄스 (0..1 → 정점→감쇠).
    private float GetDiscardLandPulse()
    {
        if (!IsDiscardFlyActive) return 0f;
        float localNow = Time.time - _discardAnimStartTime;
        float mostRecent = -999f;
        for (int i = 0; i < _discardFlyCards.Count; i++)
        {
            float land = DiscardLandLocalTime(i);
            if (land <= localNow && land > mostRecent) mostRecent = land;
        }
        if (mostRecent < 0f) return 0f;
        float t = (localNow - mostRecent) / DiscardLandPulseDuration;
        if (t < 0f || t > 1f) return 0f;
        return Mathf.Sin(t * Mathf.PI);
    }

    // 날아가는 카드들을 실제로 그린다. OnGUI에서 UI 스케일이 적용된 상태로 호출.
    // 3단계 페이즈를 공유하되, disperseDelay만 카드별로 달라진다.
    private void DrawDiscardFlyingCards()
    {
        if (!IsDiscardFlyActive) return;

        float cardW = handCardWidth;
        float cardH = handCardHeight;

        // 버린 더미 중심 (DrawTurnInfo의 디스카드 더미 Rect와 일치)
        Vector2 pileTarget = new Vector2(RefW - cornerPileRightInset + cornerDiscardPileSize * 0.5f,
                                         RefH - cornerPileTopFromBottom + cornerDiscardPileSize * 0.5f);

        float localNow = Time.time - _discardAnimStartTime;
        Matrix4x4 baseMatrix = GUI.matrix;

        // 드로우 순서 — 바깥쪽 카드부터 안쪽 카드로. 원래 중앙 카드가 맨 위에 오도록.
        // _discardFlyCards는 중앙(k=0)부터 바깥 순서로 저장되어 있으므로, 역순으로 그린다.
        for (int k = _discardFlyCards.Count - 1; k >= 0; k--)
        {
            var fc = _discardFlyCards[k];

            Vector2 center;
            float angle;
            float scale;

            if (localNow < DiscardGatherEndLocal)
            {
                // Phase 1: 부채꼴 → 모임 위치. 사인 ease로 부드럽게 감속, 상단 제어점으로 아치
                float t = EaseInOutSine(Mathf.Clamp01(localNow / DiscardGatherDuration));
                float u = 1f - t;
                center = u * u * fc.startCenter
                       + 2f * u * t * DiscardFlyControl
                       + t * t * fc.gatherTarget;
                angle = Mathf.Lerp(fc.startAngleDeg, 0f, t);
                scale = Mathf.Lerp(1f, 0.72f, t);
            }
            else if (localNow < DiscardHoldEndLocal)
            {
                // Phase 2: 중앙에서 잠깐 머무름 — 튀는 바빙 대신, 가운데로 수렴하는 완만한 드리프트.
                // gather 마무리 속도(0)에서 hold 마무리 속도(0)로 이어지며 바운스 없이 "숨을 고르는" 느낌.
                float holdT = (localNow - DiscardGatherEndLocal) / DiscardHoldDuration;
                // 0→1→0으로 부드럽게 오르내리는 곡선 (사인 반주기)
                float breathe = Mathf.Sin(holdT * Mathf.PI);
                // 아주 미세한 수직 떠오름 (+2px 이내) — 한 번만 완만하게 올라갔다 내려옴
                float lift = -1.8f * breathe;
                center = new Vector2(fc.gatherTarget.x, fc.gatherTarget.y + lift);
                angle = 0f;
                // 숨쉬기처럼 아주 살짝만 커졌다 줄어듦 (±1.5%)
                scale = 0.72f * (1f + 0.015f * breathe);
            }
            else
            {
                // Phase 3: 중앙 → 더미. disperseDelay만큼 기다렸다 출발. 사인 ease로 부드럽게.
                float disperseLocal = localNow - DiscardHoldEndLocal - fc.disperseDelay;
                if (disperseLocal < 0f)
                {
                    // 아직 자기 차례 아님 — 모임 위치에 조용히 대기 (hold 마지막 상태 유지)
                    center = fc.gatherTarget;
                    angle = 0f;
                    scale = 0.72f;
                }
                else
                {
                    float t = disperseLocal / DiscardDisperseDuration;
                    if (t >= 1f) continue;  // 착지 완료
                    float et = EaseInOutSine(t);
                    center = Vector2.Lerp(fc.gatherTarget, pileTarget, et);
                    // 더미에 가까워질수록 작아지며 흡수
                    scale = Mathf.Lerp(0.72f, 0.25f, et);
                    angle = 0f;
                }
            }

            float w = cardW * scale;
            float h = cardH * scale;
            var rect = new Rect(center.x - w * 0.5f, center.y - h * 0.5f, w, h);

            if (Mathf.Abs(angle) > 0.01f)
                GUI.matrix = baseMatrix * RotateAroundPivotMatrix(angle, center);
            else
                GUI.matrix = baseMatrix;

            DrawCardFrame(rect, fc.data, canPlay: true, drawCost: true);
        }
        GUI.matrix = baseMatrix;
    }

    // =========================================================
    // 소진(exhaust) 카드 번업 애니메이션
    // 손패에서 즉시 떠오르며 ember 입자와 함께 알파가 빠진다.
    // 손/덱/버림 어느 더미에도 안 들어가는 카드 전용 — discard fly와는 별개 시스템.
    // 외부 API: BeginExhaustFlyAt(데이터, 위치, 각도) — 미래에 손이 아닌 곳(덱/필드 등)에서
    // 카드를 소멸시키는 효과가 생기면 그쪽에서도 직접 호출 가능.
    // =========================================================

    /// <summary>BattleManager.OnCardExhausting 구독 핸들러. 손패 부채꼴 기하로 시작 위치를
    /// 계산해 ExhaustFly에 등록 + phantom slot도 활성화. handCount는 RemoveAt 직전 값(이 카드 포함).</summary>
    private void HandleCardExhausting(CardData card, int handIndex, int handCount)
    {
        if (card == null || handCount <= 0) return;
        if (handIndex < 0 || handIndex >= handCount) return;

        // DrawHand와 동일한 부채꼴 기하 — handMaxTotalAngle 캡까지 반영해야 위치가 정확히 맞는다.
        float cardH = handCardHeight;
        float easedHide = EaseInOutCubic(_handHideProgress);
        float hideOffset = easedHide * HandHideDistance;
        float centerCardY = RefH - cardH * 0.5f + handBottomOffset + hideOffset;
        float fanRadius = handFanRadius;
        float fanOriginX = RefW * 0.5f;
        float fanOriginY = centerCardY + fanRadius;

        float totalAngle = (handCount - 1) * handAnglePerCard;
        if (totalAngle > handMaxTotalAngle) totalAngle = handMaxTotalAngle;
        float anglePerCard = handCount > 1 ? totalAngle / (handCount - 1) : handAnglePerCard;
        float startAngleDeg = -totalAngle * 0.5f;
        float angle = startAngleDeg + handIndex * anglePerCard;
        Vector2 center = FanCardCenter(fanOriginX, fanOriginY, fanRadius, angle);

        BeginExhaustFlyAt(card, center, angle);

        // 옆 카드 reflow 방지 — 이 슬롯을 잠시 유지했다가 끝물에 부드럽게 닫는다.
        _exhaustPhantomIndex = handIndex;
        _exhaustPhantomStartTime = Time.time;

        DianoCard.Audio.AudioManager.Instance?.PlaySFX("card_exhaust");
    }

    /// <summary>임의 위치에서 소멸 모션 시작 — 손패 외 출처에서도 호출 가능 (이 경로는 phantom slot 미사용).</summary>
    public void BeginExhaustFlyAt(CardData data, Vector2 startCenter, float startAngleDeg)
    {
        if (data == null) return;
        _exhaustFlyCards.Add(new ExhaustFlyCard
        {
            data = data,
            startCenter = startCenter,
            startAngleDeg = startAngleDeg,
            startTime = Time.time,
        });
    }

    /// <summary>현재 phantom slot의 가중치 (0=비활성, 1=완전 유지). 만료된 phantom은 자동 해제.</summary>
    private float GetExhaustPhantomAlpha()
    {
        if (_exhaustPhantomIndex < 0 || _exhaustPhantomStartTime < 0f) return 0f;
        float local = Time.time - _exhaustPhantomStartTime;
        if (local < 0f) return 0f;
        if (local < PhantomHoldDuration) return 1f;
        float collapseT = (local - PhantomHoldDuration) / PhantomCollapseDuration;
        if (collapseT >= 1f)
        {
            _exhaustPhantomIndex = -1;
            _exhaustPhantomStartTime = -1f;
            return 0f;
        }
        return 1f - collapseT;
    }

    /// <summary>주어진 hand index가 현재 phantom slot 보정 하에서 차지하는 부채꼴 슬롯(float).</summary>
    private float GetHandSlotForIndex(int handIndex)
    {
        if (_exhaustPhantomIndex < 0) return handIndex;
        float phantomAlpha = GetExhaustPhantomAlpha();
        if (phantomAlpha <= 0f) return handIndex;
        return handIndex + (handIndex >= _exhaustPhantomIndex ? phantomAlpha : 0f);
    }

    private void DrawExhaustFlyingCards()
    {
        if (_exhaustFlyCards.Count == 0) return;

        float cardW = handCardWidth;
        float cardH = handCardHeight;
        float now = Time.time;
        Matrix4x4 baseMatrix = GUI.matrix;
        Color prevColor = GUI.color;

        // 끝난 카드는 역순으로 제거.
        for (int i = _exhaustFlyCards.Count - 1; i >= 0; i--)
        {
            float local = now - _exhaustFlyCards[i].startTime;
            if (local >= ExhaustTotalDuration) _exhaustFlyCards.RemoveAt(i);
        }

        for (int i = 0; i < _exhaustFlyCards.Count; i++)
        {
            var fc = _exhaustFlyCards[i];
            float local = now - fc.startTime;

            // 페이즈 A: 흰 오버레이가 0→1로 올라오며 카드를 덮음. 카드 본체는 그대로.
            // 페이즈 B: 카드는 이미 완전히 가려졌으므로 그리지 않고, 흰 오버레이만 1→0으로 빠짐.
            float cardAlpha, overlayAlpha;
            if (local < ExhaustWhitenDuration)
            {
                cardAlpha = 1f;
                overlayAlpha = Mathf.Clamp01(local / ExhaustWhitenDuration);
            }
            else
            {
                cardAlpha = 0f;
                float t = (local - ExhaustWhitenDuration) / ExhaustDisappearDuration;
                overlayAlpha = Mathf.Clamp01(1f - t);
            }

            var rect = new Rect(fc.startCenter.x - cardW * 0.5f, fc.startCenter.y - cardH * 0.5f,
                                cardW, cardH);

            if (Mathf.Abs(fc.startAngleDeg) > 0.01f)
                GUI.matrix = baseMatrix * RotateAroundPivotMatrix(fc.startAngleDeg, fc.startCenter);
            else
                GUI.matrix = baseMatrix;

            // 카드 본체 — 흰색이 다 덮이기 전까지만 렌더. DrawCardFrame은 외부 GUI.color의 알파를
            // art/frame에 전달하지 않으므로, 가시구간(cardAlpha=1)에만 그린다.
            if (cardAlpha > 0.01f)
            {
                GUI.color = new Color(1f, 1f, 1f, cardAlpha);
                DrawCardFrame(rect, fc.data, canPlay: true, drawCost: true);
            }

            // 흰 오버레이 — 카드 모양에 맞춰 inset해서 둥근 모서리 노출을 최소화.
            // Texture2D.whiteTexture는 1×1 흰 픽셀 — Stretch로 사각형 면을 채운다.
            if (overlayAlpha > 0.01f)
            {
                const float inset = 6f;
                var ov = new Rect(rect.x + inset, rect.y + inset,
                                  rect.width - inset * 2f, rect.height - inset * 2f);
                GUI.color = new Color(1f, 1f, 1f, overlayAlpha);
                GUI.DrawTexture(ov, Texture2D.whiteTexture, ScaleMode.StretchToFill, alphaBlend: true);
            }
        }

        GUI.matrix = baseMatrix;
        GUI.color = prevColor;
    }

    // =========================================================
    // 덱 → 손패 드로우 애니메이션
    // =========================================================

    // state.hand의 [fromIndex..끝] 구간을 "새로 드로우된 카드"로 간주하고
    // 중앙으로 모였다가 자기 부채꼴 자리로 흩어지는 3단계 애니메이션을 시작한다.
    // 호출 시점에 state.hand는 이미 새 카드를 포함하고 있어야 한다.
    private void BeginDrawFlyAnimation(BattleState state, int fromIndex)
    {
        _drawFlyCards.Clear();
        _drawFlyingInstances.Clear();

        int n = state.hand.Count;
        if (fromIndex < 0 || fromIndex >= n) return;

        _drawTotalHandCount = n;

        int drawn = n - fromIndex;
        // 중앙 클러스터 위치 — 버림 애니와 동일한 기하. 중앙 기준 좌우 균등.
        float gatherCenterX = RefW * 0.5f;
        float gatherMid = (drawn - 1) * 0.5f;

        // 흩어짐 순서: 중앙(k=0) 카드부터 먼저 자기 자리로 날아가고 바깥으로 퍼짐
        var order = new int[drawn];
        for (int k = 0; k < drawn; k++) order[k] = k;
        System.Array.Sort(order, (a, b) => Mathf.Abs(a - gatherMid).CompareTo(Mathf.Abs(b - gatherMid)));

        for (int k = 0; k < drawn; k++)
        {
            int localK = order[k];
            int handIdx = fromIndex + localK;
            var inst = state.hand[handIdx];
            _drawFlyingInstances.Add(inst);

            float gx = gatherCenterX + (localK - gatherMid) * DiscardGatherSpacing;
            float gy = DiscardGatherCenterY - Mathf.Abs(localK - gatherMid) * 2f;

            _drawFlyCards.Add(new DrawFlyCard
            {
                instance = inst,
                data = inst.data,
                targetIndex = handIdx,
                gatherTarget = new Vector2(gx, gy),
                disperseDelay = k * DrawDisperseStagger,
            });
        }

        _drawAnimStartTime = Time.time;
    }

    private void EndDrawFlyAnimation()
    {
        _drawFlyCards.Clear();
        _drawFlyingInstances.Clear();
        _drawAnimStartTime = -1f;
        _drawTotalHandCount = 0;
    }

    private bool IsDrawFlyActive => _drawAnimStartTime >= 0f && _drawFlyCards.Count > 0;

    // 특정 CardInstance가 지금 드로우 애니 때문에 DrawHand에서 건너뛰어져야 하는지 검사.
    // Phase 3가 끝난 카드는 더 이상 "비행 중"이 아니므로 즉시 DrawHand가 이어받는다.
    // (이게 없으면 carousel의 마지막 카드를 기다리는 동안 먼저 착지한 카드가 투명 상태가 됨)
    private bool IsBeingDrawnInto(CardInstance inst)
    {
        if (!IsDrawFlyActive) return false;
        if (!_drawFlyingInstances.Contains(inst)) return false;

        float localNow = Time.time - _drawAnimStartTime;
        float holdEnd = DrawGatherDuration + DrawHoldDuration;

        for (int k = 0; k < _drawFlyCards.Count; k++)
        {
            if (!ReferenceEquals(_drawFlyCards[k].instance, inst)) continue;
            float disperseLocal = localNow - holdEnd - _drawFlyCards[k].disperseDelay;
            if (disperseLocal < 0f) return true;            // gather/hold/대기 중
            return disperseLocal < DrawDisperseDuration;    // disperse 끝난 카드는 DrawHand가 그린다
        }
        return false;
    }

    // 드로우 애니 총 시간 (마지막으로 안착하는 카드의 끝 시각) — 대기 계산용
    private float GetDrawFlyTotalDuration()
    {
        if (_drawFlyCards.Count == 0) return 0f;
        float max = 0f;
        for (int i = 0; i < _drawFlyCards.Count; i++)
        {
            float end = DrawGatherDuration + DrawHoldDuration
                      + _drawFlyCards[i].disperseDelay + DrawDisperseDuration;
            if (end > max) max = end;
        }
        return max;
    }

    // 드로우 카드의 최종 부채꼴 위치/각도 — DrawHand의 부채꼴 계산과 일치해야 함.
    private void GetDrawFanTarget(int targetIndex, int handCount, out Vector2 center, out float angleDeg)
    {
        float cardH = handCardHeight;
        float hideOffset = EaseInOutCubic(_handHideProgress) * HandHideDistance;
        float centerCardY = RefH - cardH * 0.5f + handBottomOffset + hideOffset;
        float fanRadius = handFanRadius;
        float fanOriginX = RefW * 0.5f;
        float fanOriginY = centerCardY + fanRadius;

        float anglePerCard = handAnglePerCard;
        float totalAngle = (handCount - 1) * anglePerCard;
        float startAngle = -totalAngle * 0.5f;

        angleDeg = startAngle + targetIndex * anglePerCard;
        center = FanCardCenter(fanOriginX, fanOriginY, fanRadius, angleDeg);
    }

    // 드로우 애니: 버림 애니와 동일한 3단계 구조의 역방향.
    //   Phase 1 (gather): 덱 → 중앙 클러스터, 뒷면 유지, Bezier 아치, 사인 ease
    //   Phase 2 (hold):   중앙에서 은은한 숨쉬기 + 플립 (뒷면 → 앞면)
    //   Phase 3 (disperse): 중앙 → 부채꼴 자리, 앞면, 사인 ease, 회전 정렬
    private void DrawDrawFlyingCards()
    {
        if (!IsDrawFlyActive) return;

        float cardW = handCardWidth;
        float cardH = handCardHeight;

        // 덱 더미 중심 (DrawTurnInfo의 덱 더미 Rect와 일치)
        Vector2 deckCenter = new Vector2(cornerPileLeftX + cornerDeckPileSize * 0.5f,
                                         RefH - cornerPileTopFromBottom + cornerDeckPileSize * 0.5f);
        // 버림 애니와 동일한 상단 아치 제어점 — 전체 톤 통일
        Vector2 control = DiscardFlyControl;

        float localNow = Time.time - _drawAnimStartTime;
        float gatherEnd = DrawGatherDuration;
        float holdEnd   = DrawGatherDuration + DrawHoldDuration;

        Matrix4x4 baseMatrix = GUI.matrix;

        // 드로우 순서: 바깥 → 안쪽. 중앙 카드가 맨 위에 겹치도록.
        // _drawFlyCards는 중앙(k=0)부터 저장되어 있으므로 역순 드로우.
        for (int k = _drawFlyCards.Count - 1; k >= 0; k--)
        {
            var fc = _drawFlyCards[k];

            Vector2 center;
            float angleDeg;
            float scale;
            float scaleX = 1f;
            bool showFront = false;

            if (localNow < gatherEnd)
            {
                // Phase 1: 덱 → 모임 위치. Bezier 아치 + 사인 ease
                float t = EaseInOutSine(Mathf.Clamp01(localNow / DrawGatherDuration));
                float u = 1f - t;
                center = u * u * deckCenter
                       + 2f * u * t * control
                       + t * t * fc.gatherTarget;
                angleDeg = 0f;
                // 덱에서 작게 나와 클러스터에서 적당히 커짐
                scale = Mathf.Lerp(0.32f, 0.72f, t);
                scaleX = 1f;
                showFront = false;  // 가는 동안은 계속 뒷면
            }
            else if (localNow < holdEnd)
            {
                // Phase 2: 중앙에서 머무름 — 은은한 숨쉬기 + 플립
                float holdT = (localNow - gatherEnd) / DrawHoldDuration;
                float breathe = Mathf.Sin(holdT * Mathf.PI);
                float lift = -1.8f * breathe;
                center = new Vector2(fc.gatherTarget.x, fc.gatherTarget.y + lift);
                angleDeg = 0f;
                scale = 0.72f * (1f + 0.015f * breathe);

                // 플립 — hold 구간 전체에 걸쳐 1 → 0 → 1. 중간에 앞면으로 교체.
                scaleX = Mathf.Abs(Mathf.Cos(holdT * Mathf.PI));
                showFront = holdT >= 0.5f;
            }
            else
            {
                // Phase 3: 중앙 → 부채꼴 자기 자리. disperseDelay만큼 기다렸다 출발.
                float disperseLocal = localNow - holdEnd - fc.disperseDelay;
                GetDrawFanTarget(fc.targetIndex, _drawTotalHandCount, out Vector2 fanCenter, out float fanAngle);

                if (disperseLocal < 0f)
                {
                    // 아직 자기 차례 아님 — 모임 위치에 조용히 대기 (앞면)
                    center = fc.gatherTarget;
                    angleDeg = 0f;
                    scale = 0.72f;
                    scaleX = 1f;
                    showFront = true;
                }
                else
                {
                    float t = disperseLocal / DrawDisperseDuration;
                    if (t >= 1f) continue;  // 착지 완료 — DrawHand가 이어서 그린다
                    float et = EaseInOutSine(t);
                    center = Vector2.Lerp(fc.gatherTarget, fanCenter, et);
                    // 착지 시점의 DrawHand 위치와 정확히 맞추기 위해 idle bob을 점진적으로 블렌딩.
                    // 이게 없으면 핸드오프 프레임에서 ±1.6px 정도 Y가 튈 수 있다.
                    center.y += CardIdleBob(fc.targetIndex) * et;
                    angleDeg = Mathf.Lerp(0f, fanAngle, et);
                    scale = Mathf.Lerp(0.72f, 1f, et);
                    scaleX = 1f;
                    showFront = true;
                }
            }

            float w = cardW * scale * scaleX;
            float h = cardH * scale;
            var rect = new Rect(center.x - w * 0.5f, center.y - h * 0.5f, w, h);

            if (Mathf.Abs(angleDeg) > 0.01f)
                GUI.matrix = baseMatrix * RotateAroundPivotMatrix(angleDeg, center);
            else
                GUI.matrix = baseMatrix;

            if (showFront)
            {
                DrawCardFrame(rect, fc.data, canPlay: true, drawCost: true);
            }
            else if (GetCharacterCardBack() != null)
            {
                GUI.DrawTexture(rect, GetCharacterCardBack(), ScaleMode.StretchToFill, alphaBlend: true);
            }
            else
            {
                FillRect(rect, new Color(0.16f, 0.20f, 0.28f, 1f));
                DrawBorder(rect, 2f, new Color(0.70f, 0.55f, 0.28f, 1f));
            }
        }
        GUI.matrix = baseMatrix;
    }

    // =========================================================
    // 덱 리셔플 (버림 → 덱) 애니메이션
    // =========================================================

    private void BeginReshuffleAnimation(int cardCount)
    {
        _reshuffleFlyCards.Clear();
        _reshuffleTotalCards = cardCount;
        if (cardCount <= 0) return;

        for (int k = 0; k < cardCount; k++)
        {
            // 카드별 살짝 다른 회전 스핀 — 진짜 한 묶음이 쏟아져 흐르는 느낌
            float spin = (k % 2 == 0 ? -1f : 1f) * (8f + (k % 3) * 4f);
            _reshuffleFlyCards.Add(new ReshuffleFlyCard
            {
                delay = k * ReshuffleFlyStagger,
                rotSpin = spin,
            });
        }
        _reshuffleAnimStartTime = Time.time;
    }

    private void EndReshuffleAnimation()
    {
        _reshuffleFlyCards.Clear();
        _reshuffleAnimStartTime = -1f;
        _reshuffleTotalCards = 0;
    }

    private bool IsReshuffleActive => _reshuffleAnimStartTime >= 0f && _reshuffleFlyCards.Count > 0;

    private float GetReshuffleTotalDuration()
    {
        if (_reshuffleFlyCards.Count == 0) return 0f;
        return ReshuffleFlyDuration
             + (_reshuffleFlyCards.Count - 1) * ReshuffleFlyStagger;
    }

    // 지금까지 덱에 착지한 카드 수 — 덱/버림 더미 카운트 표시에 사용
    private int GetReshuffleLandedCount()
    {
        if (!IsReshuffleActive) return 0;
        float localNow = Time.time - _reshuffleAnimStartTime;
        int landed = 0;
        for (int k = 0; k < _reshuffleFlyCards.Count; k++)
        {
            float end = _reshuffleFlyCards[k].delay + ReshuffleFlyDuration;
            if (localNow >= end) landed++;
        }
        return landed;
    }

    private void DrawReshuffleFlyingCards()
    {
        if (!IsReshuffleActive) return;
        if (GetCharacterCardBack() == null) return;  // 뒷면 텍스처 없으면 조용히 스킵

        // 양쪽 더미 중심 (DrawTurnInfo의 덱/디스카드 Rect와 일치)
        float deckCenterY    = RefH - cornerPileTopFromBottom + cornerDeckPileSize * 0.5f;
        float discardCenterY = RefH - cornerPileTopFromBottom + cornerDiscardPileSize * 0.5f;
        Vector2 discardCenter = new Vector2(RefW - cornerPileRightInset + cornerDiscardPileSize * 0.5f, discardCenterY);
        Vector2 deckCenter    = new Vector2(cornerPileLeftX + cornerDeckPileSize * 0.5f,                deckCenterY);
        // 부드러운 아치 — 화면 중앙 근처까지 살짝 떠올랐다 우→좌로 흘러감
        Vector2 control       = new Vector2(RefW * 0.5f, RefH - 380f);

        // 덱에 카드가 착지할 때마다 터지는 빛 플래시 — 카드 드로우보다 먼저 그려
        // 플래시 위에 카드 뒷면이 겹쳐 흡수되는 느낌을 만든다.
        DrawReshuffleDeckFlash(deckCenter);

        // 비행 중 카드 크기 — 더미 아이콘보다 약간 작게 (이동 중 느낌)
        const float baseW = 52f;
        const float baseH = 78f;

        float localNow = Time.time - _reshuffleAnimStartTime;
        Matrix4x4 baseMatrix = GUI.matrix;

        for (int k = 0; k < _reshuffleFlyCards.Count; k++)
        {
            var fc = _reshuffleFlyCards[k];
            float raw = (localNow - fc.delay) / ReshuffleFlyDuration;
            if (raw <= 0f || raw >= 1f) continue;  // 아직 안 출발 또는 착지 완료

            float t = EaseInOutSine(raw);
            float u = 1f - t;
            Vector2 center = u * u * discardCenter
                           + 2f * u * t * control
                           + t * t * deckCenter;

            // 시작 스케일 0.85 → 끝 0.70으로 살짝 작아지며 덱에 흡수되는 느낌
            float scale = Mathf.Lerp(0.85f, 0.70f, t);
            float angle = fc.rotSpin * Mathf.Sin(t * Mathf.PI);  // 중간에 가장 많이 기울었다 돌아옴

            float w = baseW * scale;
            float h = baseH * scale;
            var rect = new Rect(center.x - w * 0.5f, center.y - h * 0.5f, w, h);

            if (Mathf.Abs(angle) > 0.01f)
                GUI.matrix = baseMatrix * RotateAroundPivotMatrix(angle, center);
            else
                GUI.matrix = baseMatrix;

            GUI.DrawTexture(rect, GetCharacterCardBack(), ScaleMode.StretchToFill, alphaBlend: true);
        }
        GUI.matrix = baseMatrix;
    }

    // 덱에 카드가 착지할 때마다 덱 위에 퍼지는 방사형 빛 플래시.
    // 가장 최근 착지 이벤트의 펄스 값을 받아 확장/감쇠하는 여러 레이어로 표현.
    // 추가로 리셔플 전체 구간에는 은은한 상시 오라가 깔려 있어 "마법적인" 느낌을 준다.
    private void DrawReshuffleDeckFlash(Vector2 deckCenter)
    {
        if (!IsReshuffleActive || _manaFrameTexture == null) return;

        var prevColor = GUI.color;

        // (1) 상시 오라 — 리셔플 동안 덱이 은은하게 숨 쉬는 듯한 약한 글로우
        {
            float breathe = 0.5f + 0.5f * Mathf.Sin(Time.time * 3.2f);
            float auraAlpha = 0.10f + 0.08f * breathe;
            float auraSize = 110f + 8f * breathe;
            var auraRect = new Rect(deckCenter.x - auraSize * 0.5f,
                                    deckCenter.y - auraSize * 0.5f,
                                    auraSize, auraSize);
            GUI.color = new Color(0.45f, 0.80f, 1f, auraAlpha);
            GUI.DrawTexture(auraRect, _manaFrameTexture, ScaleMode.StretchToFill, alphaBlend: true);
        }

        // (2) 착지 임팩트 플래시 — 매 카드 착지마다 팽창하며 페이드
        float pulse = GetReshuffleDeckLandPulse();
        if (pulse > 0.01f)
        {
            // 여러 레이어를 다른 크기/알파로 겹쳐 soft radial burst
            const int layers = 4;
            for (int i = 0; i < layers; i++)
            {
                float t = i / (float)(layers - 1);
                float scale = Mathf.Lerp(1.1f, 2.4f, t) * (0.85f + 0.25f * pulse);
                float alpha = 0.55f * pulse * (1f - t) * (1f - t);
                float size = 90f * scale;
                var r = new Rect(deckCenter.x - size * 0.5f,
                                 deckCenter.y - size * 0.5f,
                                 size, size);
                GUI.color = new Color(0.60f, 0.90f, 1f, alpha);
                GUI.DrawTexture(r, _manaFrameTexture, ScaleMode.StretchToFill, alphaBlend: true);
            }

            // (3) 중심 하이라이트 — 짧고 강한 흰색 번쩍임
            float coreSize = 60f * (0.8f + 0.4f * pulse);
            var coreRect = new Rect(deckCenter.x - coreSize * 0.5f,
                                    deckCenter.y - coreSize * 0.5f,
                                    coreSize, coreSize);
            GUI.color = new Color(1f, 1f, 1f, 0.35f * pulse);
            GUI.DrawTexture(coreRect, _manaFrameTexture, ScaleMode.StretchToFill, alphaBlend: true);
        }

        GUI.color = prevColor;
    }

    // =========================================================
    // 유물 아이콘 호버 툴팁 — 이름 + 희귀도 + 설명 소형 패널
    // =========================================================

    private void DrawRelicTooltip(RelicData relic, float x, float y)
    {
        if (relic == null) return;
        EnsureStyles();

        string nameText = relic.nameKr ?? relic.id;
        string descText = relic.description ?? "";
        Color rarityCol = RelicRarityColor(relic.rarity);

        const float TipW = 210f;
        bool hasDesc = !string.IsNullOrEmpty(descText);
        float tipH = hasDesc ? 70f : 36f;
        float clampedX = Mathf.Clamp(x, 10f, RefW - TipW - 10f);
        // 정수 픽셀 스냅 — 1px 트림이 4면 모두 동일한 두께로 래스터되도록 함.
        float rx = Mathf.Round(clampedX);
        float ry = Mathf.Round(y);
        var tipRect = new Rect(rx, ry, TipW, tipH);

        // 상단 네비 톤 — 다크 바이올렛 불투명 + 사방 브론즈 트림 (4면 동일)
        FillRect(tipRect, new Color(0.059f, 0.043f, 0.137f, 1f));
        // 트림은 1px 정수 두께 + 코너 중복 방지(좌/우는 위/아래 사이 영역만 채움) → 4면 시각적으로 동일.
        var trimCol = new Color(0.82f, 0.68f, 0.38f, 0.7f);
        FillRect(new Rect(tipRect.x, tipRect.y, tipRect.width, 1f), trimCol);
        FillRect(new Rect(tipRect.x, tipRect.yMax - 1f, tipRect.width, 1f), trimCol);
        FillRect(new Rect(tipRect.x, tipRect.y + 1f, 1f, tipRect.height - 2f), trimCol);
        FillRect(new Rect(tipRect.xMax - 1f, tipRect.y + 1f, 1f, tipRect.height - 2f), trimCol);
        _ = rarityCol;

        int prevFS = _labelStyle.fontSize;
        var prevC = _labelStyle.normal.textColor;
        FontStyle prevStyle = _labelStyle.fontStyle;
        bool prevWrap = _labelStyle.wordWrap;

        // 이름
        _labelStyle.fontSize = 13;
        _labelStyle.fontStyle = FontStyle.Bold;
        _labelStyle.normal.textColor = Color.white;
        GUI.Label(new Rect(tipRect.x + 8f, tipRect.y + 6f, TipW - 16f, 20f), nameText, _labelStyle);
        _labelStyle.fontStyle = FontStyle.Normal;

        // 설명
        if (hasDesc)
        {
            _labelStyle.fontSize = 11;
            _labelStyle.wordWrap = true;
            _labelStyle.normal.textColor = new Color(0.80f, 0.76f, 0.88f);
            GUI.Label(new Rect(tipRect.x + 8f, tipRect.y + 26f, TipW - 16f, tipH - 32f), descText, _labelStyle);
        }

        _labelStyle.fontSize = prevFS;
        _labelStyle.normal.textColor = prevC;
        _labelStyle.fontStyle = prevStyle;
        _labelStyle.wordWrap = prevWrap;
    }

    // =========================================================
    // 유물 뷰어 오버레이 — 상단 바 유물 슬롯 클릭 시 보유 유물 전체 보기 팝업
    // =========================================================

    private Texture2D GetRelicIcon(string relicId)
    {
        if (string.IsNullOrEmpty(relicId)) return _iconRelic;
        if (_relicIconCache.TryGetValue(relicId, out var cached)) return cached;
        var tex = Resources.Load<Texture2D>($"InGame/RelicArt/{relicId}");
        if (tex == null) tex = _iconRelic;
        _relicIconCache[relicId] = tex;
        return tex;
    }

    private static Color RelicRarityColor(Rarity r)
    {
        return r switch
        {
            Rarity.UNCOMMON => new Color(0.40f, 0.82f, 1.00f),
            Rarity.RARE     => new Color(1.00f, 0.82f, 0.35f),
            Rarity.SHOP     => new Color(0.55f, 1.00f, 0.65f),
            _               => new Color(0.72f, 0.72f, 0.72f),  // COMMON — 실버
        };
    }

    private static string RelicRarityLabel(Rarity r)
    {
        return r switch
        {
            Rarity.UNCOMMON => L("Uncommon", "언커먼"),
            Rarity.RARE     => L("Rare", "레어"),
            Rarity.SHOP     => L("Shop", "상점"),
            _               => L("Common", "커먼"),
        };
    }

    // 가로 1자 row — 보유 유물 아이콘만 좌→우로. 패널 배경 없이 상단 네비 바로 아래에 정렬.
    // Map/Village 화면에서도 호출할 수 있도록 public.
    public void DrawRelicViewerOverlay(GameStateManager gsm)
    {
        if (!_relicViewerOpen) return;
        var run = gsm?.CurrentRun;
        if (run == null) { _relicViewerOpen = false; return; }

        EnsureStyles();
        var ev = Event.current;

        const float IconSz = 44f;
        const float IconGap = 8f;
        const float StartX = 14f;          // 화면 좌측 정렬 — 상단 네비 첫 슬롯과 동일 라인
        float startY = _navBarBottomY + 4f; // 네비 바 바로 아래

        int hovered = -1;
        if (run.relics.Count > 0)
        {
            float ix = StartX;
            for (int i = 0; i < run.relics.Count; i++)
            {
                var relic = run.relics[i];
                if (relic == null) { ix += IconSz + IconGap; continue; }

                var iconRect = new Rect(ix, startY, IconSz, IconSz);
                bool iHov = iconRect.Contains(ev.mousePosition);
                if (iHov) hovered = i;

                Color rarityCol = RelicRarityColor(relic.rarity);
                DrawIconGlow(iconRect, rarityCol, iHov ? 1.3f : 0.6f);

                var relicTex = GetRelicIcon(relic.id);
                if (relicTex != null) GUI.DrawTexture(iconRect, relicTex, ScaleMode.ScaleToFit);
                else
                {
                    FillRect(iconRect, new Color(rarityCol.r * 0.4f, rarityCol.g * 0.4f, rarityCol.b * 0.4f, 0.9f));
                    DrawBorder(iconRect, 1.5f, rarityCol * new Color(1, 1, 1, 0.6f));
                }

                ix += IconSz + IconGap;
            }
        }

        // 호버 툴팁 — 해당 아이콘 아래
        if (hovered >= 0 && hovered < run.relics.Count)
        {
            float tipX = StartX + hovered * (IconSz + IconGap);
            float tipY = startY + IconSz + 4f;
            DrawRelicTooltip(run.relics[hovered], tipX, tipY);
        }

        // 닫기는 상단 바 유물 슬롯 재클릭 토글로만. 다른 곳 클릭/ESC로 닫지 않음.
    }

    // =========================================================
    // 포션 뷰어 오버레이 — 상단 바 포션 슬롯 클릭 시 보유 포션 목록 + 마시기 버튼.
    // Map/Village 화면에서도 호출할 수 있도록 public.
    // 전투 중(_battle != null)일 때만 마시기 버튼 활성화.
    // =========================================================
    public void DrawPotionViewerOverlay(GameStateManager gsm)
    {
        if (!_potionViewerOpen) return;
        var run = gsm?.CurrentRun;
        if (run == null) { _potionViewerOpen = false; return; }

        EnsureStyles();
        var ev = Event.current;
        bool inBattle = _battle != null;

        const float IconSz = 44f;
        const float IconGap = 8f;
        const float StartX = 14f;          // 화면 좌측 정렬 — 유물 row와 동일 라인
        float startY = _navBarBottomY + 4f;

        int hovered = -1;
        if (run.potions.Count > 0)
        {
            float ix = StartX;
            for (int i = 0; i < run.potions.Count; i++)
            {
                var p = run.potions[i];
                if (p == null) { ix += IconSz + IconGap; continue; }

                var iconRect = new Rect(ix, startY, IconSz, IconSz);
                bool iHov = iconRect.Contains(ev.mousePosition);
                bool isSelected = (_selectedPotionIndex == i);
                if (iHov) hovered = i;

                Color typeCol = PotionTypeColor(p.potionType);
                DrawIconGlow(iconRect, typeCol, isSelected ? 1.6f : (iHov ? 1.3f : 0.6f));

                var potionTex = GetPotionIcon(p.id);
                if (potionTex != null) GUI.DrawTexture(iconRect, potionTex, ScaleMode.ScaleToFit);
                else
                {
                    FillRect(iconRect, new Color(typeCol.r * 0.4f, typeCol.g * 0.4f, typeCol.b * 0.4f, 0.9f));
                    DrawBorder(iconRect, 1.5f, typeCol * new Color(1, 1, 1, 0.6f));
                }

                if (isSelected) DrawBorder(iconRect, 2f, new Color(1f, 0.95f, 0.45f, 0.95f));

                if (iHov && ev.type == EventType.MouseDown && ev.button == 0)
                {
                    ev.Use();
                    _selectedPotionIndex = (_selectedPotionIndex == i) ? -1 : i;
                }

                ix += IconSz + IconGap;
            }
        }

        // 호버 툴팁 — 선택 팝업이 떠 있는 슬롯에는 띄우지 않음
        if (hovered >= 0 && hovered < run.potions.Count && _selectedPotionIndex != hovered)
        {
            float tipX = StartX + hovered * (IconSz + IconGap);
            float tipY = startY + IconSz + 4f;
            DrawPotionTooltip(run.potions[hovered], tipX, tipY);
        }

        // 선택된 포션 "마시기" 팝업 — 이건 패널 유지 (액션 입력이 필요한 컨테이너)
        if (_selectedPotionIndex >= 0 && _selectedPotionIndex < run.potions.Count)
        {
            var selP = run.potions[_selectedPotionIndex];
            if (selP != null)
            {
                const float PopW = 220f;
                const float PopH = 100f;
                float popX = StartX + _selectedPotionIndex * (IconSz + IconGap);
                float clampedPopX = Mathf.Clamp(popX, 10f, RefW - PopW - 10f);
                float popY = startY + IconSz + 6f;
                var popRect = new Rect(clampedPopX, popY, PopW, PopH);

                // 상단 네비 톤 — 다크 바이올렛 불투명 + 사방 브론즈 트림
                FillRect(popRect, new Color(0.059f, 0.043f, 0.137f, 1f));
                var popTrim = new Color(0.82f, 0.68f, 0.38f, 0.55f);
                FillRect(new Rect(popRect.x, popRect.y, popRect.width, 1.2f), popTrim);
                FillRect(new Rect(popRect.x, popRect.yMax - 1.2f, popRect.width, 1.2f), popTrim);
                FillRect(new Rect(popRect.x, popRect.y, 1.2f, popRect.height), popTrim);
                FillRect(new Rect(popRect.xMax - 1.2f, popRect.y, 1.2f, popRect.height), popTrim);
                Color selTypeCol = PotionTypeColor(selP.potionType);
                FillRect(new Rect(popRect.x + 1.2f, popRect.y + 1.2f, 2.5f, popRect.height - 2.4f),
                         selTypeCol * new Color(1f, 1f, 1f, 0.75f));

                int prevFS = _labelStyle.fontSize;
                var prevC = _labelStyle.normal.textColor;
                FontStyle prevStyle = _labelStyle.fontStyle;
                bool prevWrap = _labelStyle.wordWrap;

                _labelStyle.fontSize = 13;
                _labelStyle.fontStyle = FontStyle.Bold;
                _labelStyle.normal.textColor = Color.white;
                GUI.Label(new Rect(popRect.x + 10f, popRect.y + 6f, PopW - 18f, 20f),
                          selP.nameKr ?? selP.id, _labelStyle);
                _labelStyle.fontStyle = FontStyle.Normal;

                _labelStyle.fontSize = 11;
                _labelStyle.wordWrap = true;
                _labelStyle.normal.textColor = new Color(0.78f, 0.92f, 0.80f);
                GUI.Label(new Rect(popRect.x + 10f, popRect.y + 26f, PopW - 18f, 38f),
                          selP.description ?? "", _labelStyle);

                _labelStyle.fontSize = prevFS;
                _labelStyle.normal.textColor = prevC;
                _labelStyle.fontStyle = prevStyle;
                _labelStyle.wordWrap = prevWrap;

                bool needsTarget = inBattle && PotionEffects.RequiresTarget(selP);
                var btnRect = new Rect(popRect.x + 10f, popRect.yMax - 30f, PopW - 20f, 24f);
                bool btnHov = inBattle && btnRect.Contains(ev.mousePosition);
                Color btnBg = !inBattle ? new Color(0.10f, 0.08f, 0.16f, 0.70f)
                              : btnHov   ? new Color(0.22f, 0.18f, 0.28f, 0.95f)
                                         : new Color(0.13f, 0.10f, 0.20f, 0.90f);
                FillRect(btnRect, btnBg);
                DrawBorder(btnRect, 1f, new Color(0.82f, 0.68f, 0.38f, btnHov ? 0.90f : 0.55f));

                int prevBFS = _centerStyle.fontSize;
                var prevBC = _centerStyle.normal.textColor;
                _centerStyle.fontSize = 12;
                _centerStyle.normal.textColor = !inBattle ? new Color(0.40f, 0.50f, 0.41f) : Color.white;
                GUI.Label(btnRect, !inBattle ? L("Combat only", "전투 중에만 사용") : (needsTarget ? L("Select target", "타겟 선택") : L("Drink", "마시기")), _centerStyle);
                _centerStyle.fontSize = prevBFS;
                _centerStyle.normal.textColor = prevBC;

                if (inBattle && btnHov && ev.type == EventType.MouseDown && ev.button == 0)
                {
                    ev.Use();
                    int slotIdx = _selectedPotionIndex;
                    _selectedPotionIndex = -1;
                    _potionViewerOpen = false;
                    if (needsTarget) _targetingPotionIndex = slotIdx;
                    else _pending.Add(() => _battle.UsePotion(slotIdx, -1));
                }
            }
        }

        // 닫기는 상단 바 포션 슬롯 재클릭 토글로만. 다른 곳 클릭/ESC로 닫지 않음.
    }

    private void DrawPotionTooltip(PotionData p, float x, float y)
    {
        if (p == null) return;
        EnsureStyles();

        string nameText = p.nameKr ?? p.id;
        string descText = p.description ?? "";
        Color typeCol = PotionTypeColor(p.potionType);

        const float TipW = 210f;
        bool hasDesc = !string.IsNullOrEmpty(descText);
        float tipH = hasDesc ? 70f : 36f;
        float clampedX = Mathf.Clamp(x, 10f, RefW - TipW - 10f);
        var tipRect = new Rect(clampedX, y, TipW, tipH);

        // 상단 네비 톤 — 다크 바이올렛 불투명 + 사방 브론즈 트림 (4면 동일)
        FillRect(tipRect, new Color(0.059f, 0.043f, 0.137f, 1f));
        var pTrim = new Color(0.82f, 0.68f, 0.38f, 0.55f);
        FillRect(new Rect(tipRect.x, tipRect.y, tipRect.width, 1.2f), pTrim);
        FillRect(new Rect(tipRect.x, tipRect.yMax - 1.2f, tipRect.width, 1.2f), pTrim);
        FillRect(new Rect(tipRect.x, tipRect.y, 1.2f, tipRect.height), pTrim);
        FillRect(new Rect(tipRect.xMax - 1.2f, tipRect.y, 1.2f, tipRect.height), pTrim);
        _ = typeCol;

        int prevFS = _labelStyle.fontSize;
        var prevC = _labelStyle.normal.textColor;
        FontStyle prevStyle = _labelStyle.fontStyle;
        bool prevWrap = _labelStyle.wordWrap;

        _labelStyle.fontSize = 13;
        _labelStyle.fontStyle = FontStyle.Bold;
        _labelStyle.normal.textColor = Color.white;
        GUI.Label(new Rect(tipRect.x + 8f, tipRect.y + 6f, TipW - 16f, 20f), nameText, _labelStyle);
        _labelStyle.fontStyle = FontStyle.Normal;

        if (hasDesc)
        {
            _labelStyle.fontSize = 11;
            _labelStyle.wordWrap = true;
            _labelStyle.normal.textColor = new Color(0.78f, 0.92f, 0.80f);
            GUI.Label(new Rect(tipRect.x + 8f, tipRect.y + 26f, TipW - 16f, tipH - 32f), descText, _labelStyle);
        }

        _labelStyle.fontSize = prevFS;
        _labelStyle.normal.textColor = prevC;
        _labelStyle.fontStyle = prevStyle;
        _labelStyle.wordWrap = prevWrap;
    }

    private static Color PotionTypeColor(PotionType t)
    {
        return t switch
        {
            PotionType.ATTACK  => new Color(1.00f, 0.45f, 0.35f),
            PotionType.DEFENSE => new Color(0.35f, 0.65f, 1.00f),
            _                  => new Color(0.55f, 1.00f, 0.65f),  // UTILITY
        };
    }

    // =========================================================
    // 덱 뷰어 오버레이 — 상단 바 계단 왼쪽 버튼을 누르면 뜨는 전체 덱 보기 팝업
    // =========================================================

    // Map/Village 화면에서도 덱 뷰어를 띄울 수 있도록 public. 내부적으로 _deckViewerOpen 체크.
    // 9-slice용 GUIStyle을 매 OnGUI마다 빌드 — Inspector에서 border/aspect 변경이 즉시 반영되도록.
    // 매 프레임 4개 GUIStyle alloc은 OnGUI 비용 대비 무시 가능.
    private static GUIStyle BuildSlicedStyle(Texture2D tex, RectOffset border)
    {
        if (tex == null) return null;
        var s = new GUIStyle();
        s.normal.background = tex;
        s.border = border;
        return s;
    }

    public void DrawDeckViewerOverlay(GameStateManager gsm)
    {
        if (!_deckViewerOpen) return;
        var run = gsm?.CurrentRun;
        if (run == null)
        {
            _deckViewerOpen = false;
            return;
        }

        var ev = Event.current;

        // Source 결정 — 1=뽑을 카드(state.deck), 2=버린 카드(state.discard), 0/기본=전체 덱(run.deck).
        // BattleState.deck/discard는 List<CardInstance>이므로 .data만 뽑아 List<CardData>로 투영.
        // 전투 외(state==null)에서 1/2를 누른 적 없겠지만 안전망으로 0으로 폴백.
        var battleState = _battle?.state;
        List<CardData> sourceCards = run.deck;
        string sourceLabel = "덱";
        if (_deckViewerSource == 1 && battleState != null)
        {
            sourceCards = new List<CardData>(battleState.deck.Count);
            for (int i = 0; i < battleState.deck.Count; i++) sourceCards.Add(battleState.deck[i].data);
            sourceLabel = "뽑을 카드";
        }
        else if (_deckViewerSource == 2 && battleState != null)
        {
            sourceCards = new List<CardData>(battleState.discard.Count);
            for (int i = 0; i < battleState.discard.Count; i++) sourceCards.Add(battleState.discard[i].data);
            sourceLabel = "버린 카드";
        }

        // 패널은 9-slice (코너 필리그리 보존). 탭은 DrawTexture로 단순 stretch — 9-slice 캡이
        // 작은 탭 안에서 글로우 헤일로 아티팩트를 만들기 때문.
        // border가 rect 절반을 넘으면 9-slice 코너가 충돌하므로 clamp.
        int pBorderX = Mathf.Min(_deckPanelBorder.x, Mathf.FloorToInt(_deckPanelW * 0.45f));
        int pBorderY = Mathf.Min(_deckPanelBorder.y, Mathf.FloorToInt(_deckPanelH * 0.45f));
        var panelStyle = BuildSlicedStyle(_deckPanelFrameTex,
            new RectOffset(pBorderX, pBorderX, pBorderY, pBorderY));

        // 1) 화면 전체 어둡게 — 뒤 UI를 가리고 클릭 이벤트도 흡수
        FillRect(new Rect(0f, 0f, RefW, RefH), new Color(0f, 0f, 0f, 0.72f));

        // 2) 패널 — 가운데 배치. 스프라이트 없으면 그냥 안 그림.
        var panelRect = new Rect((RefW - _deckPanelW) * 0.5f, (RefH - _deckPanelH) * 0.5f, _deckPanelW, _deckPanelH);
        if (panelStyle != null)
            GUI.Box(panelRect, GUIContent.none, panelStyle);

        // 3) 제목
        int prevLabelFS = _labelStyle.fontSize;
        _labelStyle.fontSize = _deckTitleFontSize;
        var titleRect = new Rect(
            panelRect.x + _deckTitleOffset.x,
            panelRect.y + _deckTitleOffset.y,
            panelRect.width - _deckTitleOffset.x - (_deckCloseOffset.x + _deckCloseSize.x + 8f),
            _deckTitleFontSize + 10f);
        GUI.Label(titleRect, L($"{sourceLabel} · {sourceCards.Count} cards", $"{sourceLabel} · {sourceCards.Count}장"), _labelStyle);
        _labelStyle.fontSize = prevLabelFS;

        // 3.5) 타이틀 아래 구분선 — 두께 0이면 안 그림.
        if (_deckDividerThickness > 0.01f)
        {
            var divRect = new Rect(
                panelRect.x + _deckDividerSidePadding,
                panelRect.y + _deckDividerY,
                Mathf.Max(0f, panelRect.width - _deckDividerSidePadding * 2f),
                _deckDividerThickness);
            FillRect(divRect, _deckDividerColor);
        }

        // 4) Close 버튼 (우상단) — 박스/보더 없이 ✕ 글리프만. 호버 시 색상 변화로만 피드백.
        var closeRect = new Rect(
            panelRect.xMax - _deckCloseOffset.x,
            panelRect.y + _deckCloseOffset.y,
            _deckCloseSize.x, _deckCloseSize.y);
        bool closeHover = closeRect.Contains(ev.mousePosition);
        int prevCenterFS = _centerStyle.fontSize;
        var prevCenterC = _centerStyle.normal.textColor;
        _centerStyle.fontSize = _deckCloseFontSize;
        _centerStyle.normal.textColor = closeHover ? Color.white : new Color(0.92f, 0.85f, 0.70f);
        GUI.Label(closeRect, "×", _centerStyle);
        _centerStyle.fontSize = prevCenterFS;
        _centerStyle.normal.textColor = prevCenterC;

        if (closeHover && ev.type == EventType.MouseDown && ev.button == 0)
        {
            _deckViewerOpen = false;
            ev.Use();
            return;
        }

        // 5) 정렬 탭 — 획득순 / 유형 / 비용. 9-slice 안 쓰고 그냥 stretch.
        string[] tabs = { "획득순", "유형", "비용" };
        float tabsY = panelRect.y + _deckTabStart.y;
        float tabsStartX = panelRect.x + _deckTabStart.x;

        for (int i = 0; i < tabs.Length; i++)
        {
            var tabRect = new Rect(tabsStartX + i * (_deckTabW + _deckTabGap), tabsY, _deckTabW, _deckTabH);
            bool active = _deckViewerSortMode == i;
            bool tabHover = tabRect.Contains(ev.mousePosition);

            var tabTex = active ? _deckTabSelectedTex : _deckTabUnselectedTex;
            if (tabTex != null)
            {
                GUI.DrawTexture(tabRect, tabTex, ScaleMode.StretchToFill, alphaBlend: true);
                // 비선택 탭 hover 시 selected 텍스처를 30% 알파로 합성.
                if (!active && tabHover && _deckTabSelectedTex != null)
                {
                    var prev = GUI.color;
                    GUI.color = new Color(1f, 1f, 1f, 0.3f);
                    GUI.DrawTexture(tabRect, _deckTabSelectedTex, ScaleMode.StretchToFill, alphaBlend: true);
                    GUI.color = prev;
                }
            }

            int prevTabFS = _centerStyle.fontSize;
            var prevTabC = _centerStyle.normal.textColor;
            _centerStyle.fontSize = _deckTabFontSize;
            _centerStyle.normal.textColor = active
                ? new Color(1f, 0.95f, 0.70f)
                : new Color(0.85f, 0.80f, 0.70f);
            GUI.Label(tabRect, tabs[i], _centerStyle);
            _centerStyle.fontSize = prevTabFS;
            _centerStyle.normal.textColor = prevTabC;

            if (tabHover && ev.type == EventType.MouseDown && ev.button == 0)
            {
                _deckViewerSortMode = i;
                _deckViewerScroll = Vector2.zero;
                ev.Use();
            }
        }

        // 6) 카드 그룹핑 — id 기준 중복 묶음 + 정렬. source에 따라 run.deck / state.deck / state.discard.
        var grouped = new List<(CardData data, int count, int firstIndex)>();
        var indexMap = new Dictionary<string, int>();
        for (int i = 0; i < sourceCards.Count; i++)
        {
            var c = sourceCards[i];
            if (indexMap.TryGetValue(c.id, out int gi))
            {
                var g = grouped[gi];
                grouped[gi] = (g.data, g.count + 1, g.firstIndex);
            }
            else
            {
                indexMap[c.id] = grouped.Count;
                grouped.Add((c, 1, i));
            }
        }

        switch (_deckViewerSortMode)
        {
            case 1:  // 유형 (타입 → 비용 → 이름)
                grouped.Sort((a, b) =>
                {
                    int t = ((int)a.data.cardType).CompareTo((int)b.data.cardType);
                    if (t != 0) return t;
                    int co = a.data.cost.CompareTo(b.data.cost);
                    if (co != 0) return co;
                    return string.Compare(a.data.nameKr, b.data.nameKr, StringComparison.CurrentCulture);
                });
                break;
            case 2:  // 비용 (비용 → 이름)
                grouped.Sort((a, b) =>
                {
                    int co = a.data.cost.CompareTo(b.data.cost);
                    if (co != 0) return co;
                    return string.Compare(a.data.nameKr, b.data.nameKr, StringComparison.CurrentCulture);
                });
                break;
            default:  // 획득순 — run.deck 등장 순서 유지
                grouped.Sort((a, b) => a.firstIndex.CompareTo(b.firstIndex));
                break;
        }

        // 7) 카드 그리드 (스크롤)
        int cols = Mathf.Max(1, _deckGridCols);
        float gridPadX = _deckGridPadX;
        float cellGap = _deckCellGap;
        float gridTop = tabsY + _deckTabH + _deckGridTopGap;
        float gridBottom = panelRect.yMax - _deckGridBottomPad;
        float viewH = Mathf.Max(0f, gridBottom - gridTop);
        float gridW = panelRect.width - gridPadX * 2f;
        float cardW = (gridW - cellGap * (cols - 1)) / cols;
        float cardH = cardW * _deckCardAspect;

        int rows = (grouped.Count + cols - 1) / cols;
        float contentH = Mathf.Max(viewH, rows * (cardH + cellGap) - cellGap + 4f);

        var viewportRect = new Rect(panelRect.x + gridPadX, gridTop, gridW, viewH);
        // 스크롤바 숨기므로 16px 게터 빼지 않음 — content는 viewport 폭과 동일하게.
        var contentRect = new Rect(0f, 0f, gridW, contentH);

        // 이전 버전의 inner FillRect (이중 패널처럼 보이던 거) 제거. 패널 프레임 안쪽이 그대로 보이도록.
        // 스크롤바 비표시 — GUIStyle.none 두 개로 양 축 모두 제거. 휠 스크롤은 정상 동작.
        _deckViewerScroll = GUI.BeginScrollView(viewportRect, _deckViewerScroll, contentRect,
            GUIStyle.none, GUIStyle.none);
        float innerW = contentRect.width;
        float innerCardW = (innerW - cellGap * (cols - 1)) / cols;
        float innerCardH = innerCardW * _deckCardAspect;
        for (int i = 0; i < grouped.Count; i++)
        {
            int row = i / cols;
            int col = i % cols;
            var cardRect = new Rect(
                col * (innerCardW + cellGap),
                row * (innerCardH + cellGap),
                innerCardW,
                innerCardH);

            DrawCardFrame(cardRect, grouped[i].data, canPlay: true, drawCost: true);

            // 중복 카운트 뱃지 — 우상단. DeckUi/duplicate_tag_blank 메달 위에 숫자 정중앙.
            if (grouped[i].count > 1)
            {
                float badgeW = innerCardW * _deckBadgeWidthRatio;
                float badgeH = badgeW * _deckBadgeAspect;
                var badgeRect = new Rect(
                    cardRect.xMax - badgeW - _deckBadgeOffset.x,
                    cardRect.y + _deckBadgeOffset.y,
                    badgeW, badgeH);

                if (_deckBadgeTex != null)
                    GUI.DrawTexture(badgeRect, _deckBadgeTex, ScaleMode.StretchToFill, alphaBlend: true);

                // 새 메달은 V-notch 없이 대칭 — 텍스트 정중앙. text offset은 메달 중앙에서 미세조정.
                int prevBadgeFS = _cardCostStyle.fontSize;
                _cardCostStyle.fontSize = Mathf.RoundToInt(badgeRect.height * _deckBadgeFontRatio);
                var textRect = new Rect(
                    badgeRect.x + _deckBadgeTextOffset.x,
                    badgeRect.y + _deckBadgeTextOffset.y,
                    badgeRect.width, badgeRect.height);
                DrawTextWithOutline(textRect, $"×{grouped[i].count}", _cardCostStyle,
                    _deckBadgeTextColor, _deckBadgeOutlineColor, _deckBadgeOutlinePx);
                _cardCostStyle.fontSize = prevBadgeFS;
            }
        }
        GUI.EndScrollView();

        // 8) 패널 밖 클릭 → 닫기 / ESC → 닫기
        if (ev.type == EventType.MouseDown && ev.button == 0
            && !panelRect.Contains(ev.mousePosition))
        {
            _deckViewerOpen = false;
            ev.Use();
        }
        else if (ev.type == EventType.KeyDown && ev.keyCode == KeyCode.Escape)
        {
            _deckViewerOpen = false;
            ev.Use();
        }
    }

    // =========================================================
    // 증원(C156) 픽커 오버레이 — 보유 공룡(run.deck) T0 SUMMON 그리드 모달
    // =========================================================

    /// <summary>증원 카드 클릭 시 활성. run.deck의 T0 SUMMON을 id별로 묶어 그리드로 보여주고,
    /// 카드 클릭 → BattleManager.PlayCard에 reinforcementCardId 전달. 외부 클릭/ESC/우클릭으로 취소.
    /// 덱 뷰어와 동일한 9-slice 패널/탭 자리/그리드/×N 메달 스킨을 재사용.</summary>
    private void DrawReinforcePickerOverlay(GameStateManager gsm)
    {
        if (_reinforcePickerCardIndex < 0) return;
        var run = gsm?.CurrentRun;
        var state = _battle?.state;
        if (run == null || state == null)
        {
            _reinforcePickerCardIndex = -1;
            return;
        }

        var ev = Event.current;

        // 9-slice 패널 — 덱 뷰어와 동일 border / 동일 frame 텍스처.
        int pBorderX = Mathf.Min(_deckPanelBorder.x, Mathf.FloorToInt(_deckPanelW * 0.45f));
        int pBorderY = Mathf.Min(_deckPanelBorder.y, Mathf.FloorToInt(_deckPanelH * 0.45f));
        var panelStyle = BuildSlicedStyle(_deckPanelFrameTex,
            new RectOffset(pBorderX, pBorderX, pBorderY, pBorderY));

        // 1) 화면 전체 어둡게
        FillRect(new Rect(0f, 0f, RefW, RefH), new Color(0f, 0f, 0f, 0.72f));

        // 2) 패널 — 가운데 배치 (덱 뷰어와 동일 사이즈)
        var panelRect = new Rect((RefW - _deckPanelW) * 0.5f, (RefH - _deckPanelH) * 0.5f, _deckPanelW, _deckPanelH);
        if (panelStyle != null)
            GUI.Box(panelRect, GUIContent.none, panelStyle);

        // 3) 제목 — 덱 뷰어와 동일 오프셋/폰트
        int prevLabelFS = _labelStyle.fontSize;
        _labelStyle.fontSize = _deckTitleFontSize;
        var titleRect = new Rect(
            panelRect.x + _deckTitleOffset.x,
            panelRect.y + _deckTitleOffset.y,
            panelRect.width - _deckTitleOffset.x - (_deckCloseOffset.x + _deckCloseSize.x + 8f),
            _deckTitleFontSize + 10f);
        GUI.Label(titleRect, L("Reinforcement — call 1 owned dinosaur", "증원 소집 — 보유 공룡 1마리 호출"), _labelStyle);
        _labelStyle.fontSize = prevLabelFS;

        // 3.5) 타이틀 아래 구분선
        if (_deckDividerThickness > 0.01f)
        {
            var divRect = new Rect(
                panelRect.x + _deckDividerSidePadding,
                panelRect.y + _deckDividerY,
                Mathf.Max(0f, panelRect.width - _deckDividerSidePadding * 2f),
                _deckDividerThickness);
            FillRect(divRect, _deckDividerColor);
        }

        // 4) Close 버튼 (우상단) — 덱 뷰어와 동일
        var closeRect = new Rect(
            panelRect.xMax - _deckCloseOffset.x,
            panelRect.y + _deckCloseOffset.y,
            _deckCloseSize.x, _deckCloseSize.y);
        bool closeHover = closeRect.Contains(ev.mousePosition);
        int prevCenterFS = _centerStyle.fontSize;
        var prevCenterC = _centerStyle.normal.textColor;
        _centerStyle.fontSize = _deckCloseFontSize;
        _centerStyle.normal.textColor = closeHover ? Color.white : new Color(0.92f, 0.85f, 0.70f);
        GUI.Label(closeRect, "×", _centerStyle);
        _centerStyle.fontSize = prevCenterFS;
        _centerStyle.normal.textColor = prevCenterC;

        if (closeHover && ev.type == EventType.MouseDown && ev.button == 0)
        {
            _reinforcePickerCardIndex = -1;
            ev.Use();
            return;
        }

        // 5) 부제 — 덱 뷰어 탭 자리에 안내 문구 (정렬 탭 없음)
        var hintRect = new Rect(
            panelRect.x + _deckTabStart.x,
            panelRect.y + _deckTabStart.y,
            panelRect.width - _deckTabStart.x * 2f,
            _deckTabH);
        int prevHintFS = _labelStyle.fontSize;
        var prevHintC = _labelStyle.normal.textColor;
        _labelStyle.fontSize = 14;
        _labelStyle.normal.textColor = new Color(0.85f, 0.78f, 0.62f, 1f);
        GUI.Label(hintRect, L("Add 1 owned dinosaur to your hand. (Works from deck · discard · hand · this combat only)", "보유 공룡 1마리를 손패에 추가합니다. (덱·버린 더미·손패 어디든 보유 중이면 가능 · 이번 전투 한정)"), _labelStyle);
        _labelStyle.fontSize = prevHintFS;
        _labelStyle.normal.textColor = prevHintC;

        // 6) 후보 풀 — run.deck(보유 전체) T0 SUMMON, id 중복 제거 1장씩
        var seen = new HashSet<string>();
        var candidates = new List<CardData>();
        foreach (var c in run.deck)
        {
            if (c == null) continue;
            if (c.cardType != CardType.SUMMON) continue;
            if (c.id.EndsWith("_T1") || c.id.EndsWith("_T2")) continue;
            if (!seen.Add(c.id)) continue;
            candidates.Add(c);
        }

        // 7) 카드 그리드 (스크롤) — 덱 뷰어와 동일 cols/padding/aspect
        int cols = Mathf.Max(1, _deckGridCols);
        float gridPadX = _deckGridPadX;
        float cellGap = _deckCellGap;
        float gridTop = panelRect.y + _deckTabStart.y + _deckTabH + _deckGridTopGap;
        float gridBottom = panelRect.yMax - _deckGridBottomPad;
        float viewH = Mathf.Max(0f, gridBottom - gridTop);
        float gridW = panelRect.width - gridPadX * 2f;
        float cardW = (gridW - cellGap * (cols - 1)) / cols;
        float cardH = cardW * _deckCardAspect;

        int rows = Mathf.Max(1, (candidates.Count + cols - 1) / cols);
        float contentH = Mathf.Max(viewH, rows * (cardH + cellGap) - cellGap + 4f);

        var viewportRect = new Rect(panelRect.x + gridPadX, gridTop, gridW, viewH);
        var contentRect = new Rect(0f, 0f, gridW, contentH);

        if (candidates.Count == 0)
        {
            int prevFS = _centerStyle.fontSize;
            _centerStyle.fontSize = 16;
            GUI.Label(viewportRect, L("No T0 dinosaur cards owned.", "보유한 T0 공룡 카드가 없습니다."), _centerStyle);
            _centerStyle.fontSize = prevFS;
        }
        else
        {
            _reinforcePickerScroll = GUI.BeginScrollView(viewportRect, _reinforcePickerScroll, contentRect,
                GUIStyle.none, GUIStyle.none);
            float innerW = contentRect.width;
            float innerCardW = (innerW - cellGap * (cols - 1)) / cols;
            float innerCardH = innerCardW * _deckCardAspect;
            int? clickedIndex = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                int row = i / cols;
                int col = i % cols;
                var cardRect = new Rect(
                    col * (innerCardW + cellGap),
                    row * (innerCardH + cellGap),
                    innerCardW,
                    innerCardH);

                bool hover = cardRect.Contains(ev.mousePosition);
                DrawCardFrame(cardRect, candidates[i], canPlay: true, drawCost: true);

                if (hover)
                {
                    DrawBorder(cardRect, 2f, new Color(1f, 0.85f, 0.40f, 1f));
                    if (ev.type == EventType.MouseDown && ev.button == 0)
                    {
                        clickedIndex = i;
                        ev.Use();
                    }
                }
            }
            GUI.EndScrollView();

            if (clickedIndex.HasValue)
            {
                int catalystIdx = _reinforcePickerCardIndex;
                string pickedId = candidates[clickedIndex.Value].id;
                _reinforcePickerCardIndex = -1;
                _pending.Add(() => { _battle.PlayCard(catalystIdx, -1, -1, -1, null, pickedId); });
                return;
            }
        }

        // 8) 패널 밖 클릭 → 취소 / ESC → 취소
        if (ev.type == EventType.MouseDown && ev.button == 0
            && !panelRect.Contains(ev.mousePosition))
        {
            _reinforcePickerCardIndex = -1;
            ev.Use();
        }
        else if (ev.type == EventType.KeyDown && ev.keyCode == KeyCode.Escape)
        {
            _reinforcePickerCardIndex = -1;
            ev.Use();
        }
    }

    // =========================================================
    // 저수준 사각형 그리기 유틸
    // =========================================================

    private static void FillRect(Rect rect, Color color)
    {
        var prev = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = prev;
    }

    // 수직 그라디언트 — top→bottom으로 색/알파 Lerp. 다단 스트립으로 IMGUI에서 부드러운 페이드 구현.
    private static void FillVerticalGradient(Rect rect, Color top, Color bottom, int steps = 8)
    {
        if (rect.height <= 0f || rect.width <= 0f) return;
        if (steps < 2) steps = 2;
        float stripH = rect.height / steps;
        for (int i = 0; i < steps; i++)
        {
            float t = (i + 0.5f) / steps;
            var c = Color.Lerp(top, bottom, t);
            // 스트립 사이 시각적 seam 방지 — 0.5px 겹치게.
            FillRect(new Rect(rect.x, rect.y + stripH * i, rect.width, stripH + 0.6f), c);
        }
    }

    // (x, y, w, h) 비율 Vector4를 주어진 rect 안의 실제 Rect로 변환.
    private static Rect RectFromPct(Rect rect, Vector4 pct)
    {
        return new Rect(
            rect.x + rect.width  * pct.x,
            rect.y + rect.height * pct.y,
            rect.width  * pct.z,
            rect.height * pct.w);
    }

    private static void DrawBorder(Rect rect, float thickness, Color color)
    {
        FillRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
        FillRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
        FillRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
        FillRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
    }
}
