using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using DianoCard.Battle;
using DianoCard.Data;
using DianoCard.Game;
using UnityEngine;

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

    // 융합 모드: MAGIC/FUSION 카드가 _targetingCardIndex로 지정된 상태에서, 첫 재료를 선택 → 두 번째 선택 → 실행.
    // _fusionMaterialAPicked == false면 첫 번째 재료 대기 중, true면 두 번째 대기 중.
    // _fusionMaterialA는 선택된 재료의 (필드/손, 인덱스) 기록.
    private bool _fusionMaterialAPicked;
    private DianoCard.Battle.FusionMaterial _fusionMaterialA;

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

    // 배경을 world-space로 렌더링해서 파티클이 배경 위에 나오게 한다.
    // (IMGUI는 world 렌더링 뒤에 그려지므로, OnGUI로 배경을 그리면 파티클이 가려짐)
    private SpriteRenderer _worldBgSr;

    // 카드 프레임 텍스처 (손패 공통)
    // 아래→위 순서로 레이어: CardBg → 아트 → CardArtFrame(육각 배너 포함) → CardDescPanel → CardBorder
    private Texture2D _cardFrameTexture;
    private Texture2D _cardBgTexture;
    private Texture2D _cardBorderTexture;
    private Texture2D _cardArtFrameTexture;
    private Texture2D _cardDescPanelTexture;
    private Texture2D _cardCountBadgeTexture;
    private Texture2D _manaFrameTexture;
    private Texture2D _manaOrbTexture; // 좌하단 마나 오브 본체 — 다크판타지 톤 디테일 에셋. 없으면 _manaFrameTexture로 폴백.
    private Texture2D _shieldFxTexture;

    // StS-style 카드 레이어 v3 (2026-04-23) — 흰색 PNG + 코드 tint 방식.
    // CardBg(빨강 풀사이즈) → CardFrameBase(동색 인셋) → CardArtPlate(상단 아트 패널) →
    //   아트 → CardDescPanel(하단 패널) → CardRibbon → CardTrim → CardTypeLabel → CostGem.
    private Texture2D _cardFrameBaseTexture;
    private Texture2D _cardArtPlateTexture;
    private Texture2D _cardRibbonTexture;
    private Texture2D _cardTrimTexture;
    private Texture2D _cardTypeLabelTexture;
    private Texture2D _cardCostGemTexture;
    private Texture2D _cardCostGemInnerTexture;

    // 상단 HUD 아이콘
    private Texture2D _iconHP;
    private Texture2D _iconGold;
    private Texture2D _iconMana;
    private Texture2D _iconPotion;
    private Texture2D _iconRelic;
    private Texture2D _iconDeck;
    private Texture2D _iconDiscard;
    private Texture2D _iconCardBack;  // 드로우 애니메이션의 뒷면 표시용
    private Texture2D _iconFloor;
    private Texture2D _iconShield;
    private Texture2D _iconShieldGreen;
    private Texture2D _iconAttack;
    private Texture2D _topBarBg;
    private Texture2D _endTurnButtonTex;
    private Texture2D _hudDividerTexMap;     // 맵 전용 구분선 — Map/divider_map
    private Texture2D _hudDividerTexVillage; // 마을 전용 구분선 — VillageUI/divider_village
    private Texture2D _hudDividerTexBattle;  // 전투 전용 구분선 — InGame/divider_battle (없으면 스킵)
    private float _endTurnHoverScale = 1f;

    // 카드 위에 표시되는 일러스트 (카드 id → 텍스처). 카테고리별 CardArt/{Spell|Summon|Utility}/.
    private readonly Dictionary<string, Texture2D> _cardSprites = new();
    // 필드 위에 그려지는 공룡 스프라이트 (투명 배경). Dinos/ 폴더.
    private readonly Dictionary<string, Texture2D> _fieldDinoSprites = new();

    // 적 스프라이트 (적 id → 텍스처). Start()에서 한 번만 로드.
    private readonly Dictionary<string, Texture2D> _enemySprites = new();

    // 플레이어 캐릭터 스프라이트 (필드 위에 서있는 모습)
    private Texture2D _playerSprite;
    // 애니메이션용 world-space 뷰 (Phase 1)
    private BattleEntityView _playerView;
    private bool _rewardDimmed;
    private SpriteRenderer _rewardDimOverlay;
    private static readonly Color RewardOverlayColor = new Color(0f, 0f, 0f, 0.4f);
    private Sprite _playerWorldSprite;

    // 적 애니메이션 뷰 (적 id → world Sprite, EnemyInstance → view)
    private readonly Dictionary<string, Sprite> _enemyWorldSprites = new();
    private readonly Dictionary<EnemyInstance, BattleEntityView> _enemyViews = new();

    // E901 이끼 잡몹 — 좌/우 코너용 변형 스프라이트 + 코너별 원근 스케일.
    // ComputeSlotPositions에서 코너 인덱스에 따라 L/R 스왑하고 스케일 dict에 기록 → GetEnemyDrawHeight가 읽음.
    private Sprite _mossWorldSpriteL;
    private Sprite _mossWorldSpriteR;
    private readonly Dictionary<EnemyInstance, float> _mossDepthScale = new();

    // 데미지 시 스폰되는 VFX 프리팹 (Inspector에서 할당)
    // 기본값으로 Resources 또는 AssetDatabase로는 못 불러오므로 SerializeField로 노출.
    [Header("HUD Strip & Divider (상단 네비바 공용 — Battle/Map/Village 전부)")]
    [Tooltip("HUD 스트립 배경 + 구분선 표시 여부.")]
    [SerializeField] private bool hudStripEnabled = true;
    [Tooltip("HUD 스트립 높이 (px).")]
    [SerializeField, Range(40f, 300f)] private float hudStripHeight = 74f;
    [Tooltip("배틀 화면용 HUD 스트립 배경색. 알파는 아래 Alpha Battle 슬라이더가 최종값을 결정.")]
    [SerializeField] private Color hudStripBgColorBattle = new(0.059f, 0.043f, 0.137f, 1f);
    [Tooltip("맵 화면용 HUD 스트립 배경색. 알파는 아래 Alpha Map 슬라이더가 최종값을 결정.")]
    [SerializeField] private Color hudStripBgColorMap = new(0.059f, 0.043f, 0.137f, 1f);
    [Tooltip("마을(캠프) 화면용 HUD 스트립 배경색. 알파는 아래 Alpha Village 슬라이더가 최종값을 결정.")]
    [SerializeField] private Color hudStripBgColorVillage = new(0.03f, 0.05f, 0.08f, 1f);
    [Tooltip("배틀 HUD 스트립 최종 알파. 0=완전 투명, 1=완전 불투명.")]
    [SerializeField, Range(0f, 1f)] private float hudStripAlphaBattle = 0.5f;
    [Tooltip("맵 HUD 스트립 최종 알파. 0=완전 투명, 1=완전 불투명.")]
    [SerializeField, Range(0f, 1f)] private float hudStripAlphaMap = 0.84f;
    [Tooltip("마을 HUD 스트립 최종 알파. 0=완전 투명, 1=완전 불투명.")]
    [SerializeField, Range(0f, 1f)] private float hudStripAlphaVillage = 0.88f;
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

    // ── 1마리일 때 ──────────────────────────────────────
    [Tooltip("1마리일 때 공룡의 X 중심. 캐릭터(x=230)에 붙는 정도. 작을수록 캐릭터 가까이.")]
    [Range(300f, 900f)]
    [SerializeField] private float dinoSingleX = 430f;

    [Tooltip("1마리일 때 공룡 발끝 Y. GroundY=560(캐릭터 발끝)을 기준으로 +면 캐릭터보다 아래(앞쪽).")]
    [Range(400f, 700f)]
    [SerializeField] private float dinoSingleFootY = 575f;

    // ── 2마리일 때 (각 슬롯 독립 컨트롤) ─────────────────────
    [Tooltip("2마리 시 슬롯 0 (앞쪽 공룡) X 중심. 뒤 공룡은 이 위치에서 자동 패킹됨.")]
    [Range(300f, 900f)]
    [SerializeField] private float dinoTwoSlot0X = 420f;

    [Tooltip("2마리 시 슬롯 0 (앞쪽 공룡) 발끝 Y. GroundY=560 기준. 뒤 공룡은 이 발끝에서 절대 픽셀(dinoSize×staggerPct)만큼 위로.")]
    [Range(400f, 700f)]
    [SerializeField] private float dinoTwoSlot0FootY = 590f;

    // ── 페어 자동 패킹 (공룡별 크기는 card.csv field_scale에서 로드) ───
    [Tooltip("2마리 페어의 가로 겹침 비율. 0.55가 기존 dinoTwoSlot1X=500 셋팅과 동일한 느낌. 0=떨어져, 0.7=많이 겹침.")]
    [Range(0f, 0.7f)]
    [SerializeField] private float pairOverlapPct = 0.55f;

    [Tooltip("뒤 공룡의 발이 앞 공룡 발보다 위로 올라가는 비율 (앞 공룡 키 기준). 0.28이 기존 dinoTwoSlot1FootY=530과 동일.")]
    [Range(0f, 0.5f)]
    [SerializeField] private float pairStaggerYPct = 0.28f;

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
    [Tooltip("손패 카드의 화면 하단 노출 오프셋. 값↑ = 카드가 더 아래로 가려짐. 기본 81")]
    [Range(0f, 200f)]
    [SerializeField] private float handBottomOffset = 81f;

    [Tooltip("카드 사이 각도(도). 값↑ = 부채꼴 더 펼쳐짐. 기본 6")]
    [Range(0f, 20f)]
    [SerializeField] private float handAnglePerCard = 6f;

    [Tooltip("부채꼴 가상 원 반지름. 값↑ = 곡률 줄어듦(평평해짐). 기본 1100")]
    [Range(400f, 2500f)]
    [SerializeField] private float handFanRadius = 1100f;

    // ───────── 카드 레이어 rect 튜닝 (Inspector 노출) ─────────
    // 손패/호버/날아가는 카드/덱 뷰어 — 모든 BattleUI 카드 렌더링에 적용.
    // (x, y, w, h) = 카드 rect 내부 비율.
    [Header("Card Layers (rect 비율)")]
    [Tooltip("CardBg 뒤 배경판 영역. 금색 테두리(CardBorder) 안쪽에 딱 맞도록 inset.")]
    [SerializeField] private Vector4 cardBgRectPct = new(0.045f, 0.03f, 0.91f, 0.94f);
    [Tooltip("아트(일러스트) 영역.")]
    [SerializeField] private Vector4 cardArtRectPct = new(0.06f, 0.045f, 0.88f, 0.63f);
    [Tooltip("CardArtFrame 오버레이 (골드 사각 + 육각 배너) 영역.")]
    [SerializeField] private Vector4 cardArtFrameRectPct = new(0.15f, 0.63f, 0.7f, 0.09f);
    [Tooltip("CardBorder (외곽 테두리) 영역. 기본은 전체.")]
    [SerializeField] private Vector4 cardBorderRectPct = new(0f, 0f, 1f, 1f);
    [Tooltip("카드 이름(카테고리 라벨) 영역 — 금테 안에 들어오도록 Y를 살짝 아래로.")]
    [SerializeField] private Vector4 cardNameRectPct = new(0.06f, 0.075f, 0.88f, 0.07f);
    [Tooltip("타입/이름 라벨 (Triceratops 등)이 얹힐 육각 배너 영역 — hex 중앙에 정렬.")]
    [SerializeField] private Vector4 cardTypeRectPct = new(0.15f, 0.645f, 0.7f, 0.07f);
    [Tooltip("본문 스탯/설명 영역. hex 배너 바로 아래 붙여서 공백 최소화.")]
    [SerializeField] private Vector4 cardBodyRectPct = new(0.15f, 0.73f, 0.7f, 0.22f);
    [Tooltip("골드 디바이더 rect (xPct, yPct, widthPct, 절대높이 px). hex 배너 영역 피해서 좌·우 두 조각으로 그려짐.")]
    [SerializeField] private Vector4 cardDividerRectPct = new(0.07f, 0.672f, 0.87f, 1.3f);
    [SerializeField] private Color cardDividerColor = new(0.50f, 0.42f, 0.26f, 0.65f);
    [Tooltip("좌상단 마나 코스트 오브 — (centerX, centerY, sizeFrac) 카드 폭 기준 비율.")]
    [SerializeField] private Vector3 cardCostOrbPct = new(0.127f, 0.086f, 0.18f);

    // ───────── StS 카드 레이어 v3 (2026-04-23) ─────────
    [Header("Card Layers v3 (StS-style)")]
    [Tooltip("CardFrameBase — 본체 프레임. 보통 전체 rect.")]
    [SerializeField] private Vector4 cardBaseRectPct = new(0f, 0f, 1f, 1f);
    [Tooltip("CardArtPlate — 상단 아트 패널 배경. 아트가 올라갈 윗칸.")]
    [SerializeField] private Vector4 cardArtPlateRectPct = new(0.05f, 0.02f, 0.9f, 0.6f);
    [Tooltip("CardRibbon — 상단 리본. 살짝 위로 튀어나오게.")]
    [SerializeField] private Vector4 cardRibbonRectPct = new(-0.05f, 0.03f, 1.1f, 0.18f);
    [Tooltip("카드 아트 영역 — CardArtPlate 위에 얹힘.")]
    [SerializeField] private Vector4 cardArtRectV2Pct = new(0.05f, 0.04f, 0.9f, 0.58f);
    [Tooltip("CardTrim — 메탈 트림. 보통 전체 rect.")]
    [SerializeField] private Vector4 cardTrimRectPct = new(0f, 0f, 1f, 1f);
    [Tooltip("CardDescPanel — 본문 텍스트 뒤 어두운 패널 위치/크기.")]
    [SerializeField] private Vector4 cardDescPanelRectPct = new(0.07f, 0.59f, 0.86f, 0.4f);
    [Tooltip("CardTypeLabel — 하단 육각 타입 라벨 위치/크기.")]
    [SerializeField] private Vector4 cardTypeLabelPillRectPct = new(0.30f, 0.89f, 0.40f, 0.06f);
    [Tooltip("리본 위 카드명 텍스트 영역.")]
    [SerializeField] private Vector4 cardNameOnRibbonRectPct = new(0.16f, 0.015f, 0.68f, 0.12f);
    [Tooltip("하단 패널 본문 영역 (ATK/HP 또는 설명).")]
    [SerializeField] private Vector4 cardBodyV2RectPct = new(0.11f, 0.62f, 0.78f, 0.24f);
    [Tooltip("CardFrameBase 안쪽 플레이트 색 — slotOnly 또는 frameUseTypeColor=false 일 때만 사용.")]
    [SerializeField] private Color cardBaseTint = new(0.34f, 0.33f, 0.36f, 1f);
    [Tooltip("CardBg 외곽 프레임 색 — 미드 스톤 그레이 (가장 밝은 레이어, 카드 실루엣이 배경과 분리되게).")]
    [SerializeField] private Color cardBgTint = new(0.42f, 0.41f, 0.44f, 1f);
    [Tooltip("CardArtPlate 상단 아트 패널 색 — 아트가 돋보이도록 가장 어두운 레이어.")]
    [SerializeField] private Color cardArtPlateTint = new(0.20f, 0.19f, 0.22f, 1f);
    [Tooltip("CardDescPanel 본문 패널 색 — 텍스트 가독성 위해 중간 밝기.")]
    [SerializeField] private Color cardDescPanelTint = new(0.36f, 0.35f, 0.38f, 1f);
    [Tooltip("코스트 젬 외곽 링 색 — 에이지드 브라스 (빛바랜 황동).")]
    [SerializeField] private Color cardCostGemTint = new(0.45f, 0.36f, 0.22f, 1f);
    [Tooltip("코스트 젬 안쪽 디스크 색 — 잉크 블랙.")]
    [SerializeField] private Color cardCostGemInnerTint = new(0.08f, 0.08f, 0.10f, 1f);
    [Tooltip("안쪽 디스크 축소 비율 (orb 기준). 0 = 링과 같은 크기, 양수 = 작게.")]
    [SerializeField, Range(0f, 0.5f)] private float cardCostGemInnerShrinkPct = 0.22f;
    [Tooltip("CardTypeLabel 하단 육각 라벨 색 (어두운 회색).")]
    [SerializeField] private Color cardTypeLabelTint = new(0.18f, 0.18f, 0.20f, 1f);

    // ───────── v3 추가 튜닝 (Inspector 전용 tint/color) ─────────
    [Header("Card Extra Tints (흰색 = 기본 적용)")]
    [Tooltip("아트 일러스트에 적용되는 tint 곱셈. 흰색 = 원본.")]
    [SerializeField] private Color cardArtTint = Color.white;
    [Tooltip("CardRibbon 추가 tint 곱셈 (타입 색 × 이 값).")]
    [SerializeField] private Color cardRibbonTintMul = Color.white;
    [Tooltip("CardTrim 추가 tint 곱셈 (등급 색 × 이 값).")]
    [SerializeField] private Color cardTrimTintMul = Color.white;

    [Header("Card Border (최외곽 오버레이 — 선택)")]
    [Tooltip("CardBorder — CardBg 위에 한 번 더 덮는 외곽 테두리. 없으면 비어둠.")]
    [SerializeField] private Color cardBorderTint = new(0f, 0f, 0f, 0f); // alpha 0 = 그리지 않음
    [Tooltip("CardBorder 그리기 활성화.")]
    [SerializeField] private bool cardBorderEnabled = false;

    [Header("Layer Borders (레이어별 볼더 — 기본: 검정 α128, 3pt)")]
    [Tooltip("CardBg (외곽 프레임) 볼더.")]
    [SerializeField] private LayerBorderConfig borderCardBg = new() { enabled = true, color = new Color(0f, 0f, 0f, 1f), widthPx = 2f };
    [Tooltip("CardFrameBase (안쪽 플레이트) 볼더.")]
    [SerializeField] private LayerBorderConfig borderFrameBase = new() { enabled = true, color = new Color(0f, 0f, 0f, 1f), widthPx = 2f };
    [Tooltip("CardRibbon (상단 리본) 볼더.")]
    [SerializeField] private LayerBorderConfig borderRibbon = new() { enabled = true, color = new Color(0f, 0f, 0f, 1f), widthPx = 2f };
    [Tooltip("CardTrim (등급 트림) 볼더.")]
    [SerializeField] private LayerBorderConfig borderTrim = new() { enabled = true, color = new Color(0f, 0f, 0f, 1f), widthPx = 2f };
    [Tooltip("CardTypeLabel (하단 타입 pill) 볼더.")]
    [SerializeField] private LayerBorderConfig borderTypeLabel = new() { enabled = true, color = new Color(0f, 0f, 0f, 1f), widthPx = 2f };
    [Tooltip("CostGem (좌상단 마나 외곽 링) 볼더.")]
    [SerializeField] private LayerBorderConfig borderCostGem = new() { enabled = true, color = new Color(0f, 0f, 0f, 1f), widthPx = 2f };
    [Tooltip("CostGemInner (마나 안쪽 디스크) 볼더.")]
    [SerializeField] private LayerBorderConfig borderCostGemInner = new() { enabled = false, color = new Color(0f, 0f, 0f, 1f), widthPx = 1f };

    [Header("Card Ribbon Override")]
    [Tooltip("리본 색을 타입 색 대신 고정 색으로 오버라이드.")]
    [SerializeField] private bool cardRibbonUseOverride = false;
    [Tooltip("오버라이드 시 사용할 리본 색 — 에이지드 파치먼트 (빛바랜 종이).")]
    [SerializeField] private Color cardRibbonOverrideColor = new(0.62f, 0.55f, 0.45f, 1f);

    [Header("Card Frame Color by Type (CardFrameBase 레이어)")]
    [Tooltip("안쪽 플레이트 색을 카드 타입에 따라 자동 변경. false 면 cardBaseTint 고정.")]
    [SerializeField] private bool frameUseTypeColor = false;
    [Tooltip("SUMMON — 일본 다크판타지 와인/버건디 (빨강 대신).")]
    [SerializeField] private Color frameColorSummon = new(0.40f, 0.18f, 0.22f, 1f);
    [Tooltip("MAGIC — 딥 인디고/사파이어 (보라 대신 잉크 네이비).")]
    [SerializeField] private Color frameColorMagic = new(0.22f, 0.24f, 0.46f, 1f);
    [Tooltip("BUFF — 에이지드 제이드/티어.")]
    [SerializeField] private Color frameColorBuff = new(0.18f, 0.36f, 0.32f, 1f);
    [Tooltip("UTILITY — 거넛메탈 슬레이트.")]
    [SerializeField] private Color frameColorUtility = new(0.26f, 0.26f, 0.32f, 1f);
    [Tooltip("RITUAL — 머티드 로즈/마브.")]
    [SerializeField] private Color frameColorRitual = new(0.50f, 0.30f, 0.38f, 1f);
    [Tooltip("매치 안 되는 타입 / null — 스톤 그레이.")]
    [SerializeField] private Color frameColorDefault = new(0.32f, 0.32f, 0.36f, 1f);

    [Header("Card Slot Defaults (카드 데이터 없을 때)")]
    [Tooltip("슬롯 프리뷰 리본 기본 색.")]
    [SerializeField] private Color slotDefaultRibbonTint = new(0.45f, 0.45f, 0.45f, 1f);
    [Tooltip("슬롯 프리뷰 트림 기본 색 (동색).")]
    [SerializeField] private Color slotDefaultTrimTint = new(0.71f, 0.43f, 0.20f, 1f);
    [Tooltip("아트 텍스처 없을 때 placeholder fill 색.")]
    [SerializeField] private Color cardArtPlaceholderTint = new(0.5f, 0.5f, 0.5f, 0.35f);

    [Header("Card State")]
    [Tooltip("플레이 불가 카드 dim 곱셈 색 (프레임 전체).")]
    [SerializeField] private Color cardDisabledDim = new(0.55f, 0.55f, 0.55f, 0.9f);
    [Tooltip("플레이 불가 카드 코스트 젬 dim.")]
    [SerializeField] private Color cardDisabledDimGem = new(0.7f, 0.7f, 0.7f, 0.95f);

    [Header("Card Text Tints")]
    [Tooltip("카드명 텍스트 tint 곱셈 (등급 색 × 이 값).")]
    [SerializeField] private Color cardNameTextTint = Color.white;
    [Tooltip("카드명 외곽선 색.")]
    [SerializeField] private Color cardNameOutline = new(0f, 0f, 0f, 0.9f);
    [Tooltip("카드명 외곽선 두께.")]
    [SerializeField, Range(0f, 3f)] private float cardNameOutlineThickness = 1.0f;
    [Tooltip("카테고리 라벨 (소환/마법/버프/유틸) 색.")]
    [SerializeField] private Color cardTypeTextColor = Color.white;
    [Tooltip("본문(ATK/HP, 설명) 텍스트 색.")]
    [SerializeField] private Color cardBodyTextColor = Color.white;
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
    [Tooltip("카드명 (리본 위) 폰트 크기 — 호버/프리뷰(큰 카드).")]
    [SerializeField, Range(6, 48)] private int cardNameFontSize = 16;
    [Tooltip("카드명 폰트 크기 — 손패 (작은 카드, drawCost=false 경로).")]
    [SerializeField, Range(6, 32)] private int cardNameFontSizeSmall = 11;
    [Tooltip("카테고리 라벨 (소환/마법 등, 하단 pill) 폰트 크기.")]
    [SerializeField, Range(6, 32)] private int cardTypeFontSize = 11;
    [Tooltip("본문 (ATK/HP, 설명) 폰트 크기.")]
    [SerializeField, Range(6, 32)] private int cardBodyFontSize = 11;
    [Tooltip("코스트 젬 숫자 크기 비율 (orb 지름 × 이 비율). 0.55 = 젬의 55%.")]
    [SerializeField, Range(0.2f, 1.0f)] private float cardCostFontSizeRatio = 0.72f;

    [Header("Card Text Rects (위치/크기 — 배경 레이어와 독립)")]
    [Tooltip("카테고리 라벨 텍스트 위치 (pill 배경과 독립). 카드 내부 비율.")]
    [SerializeField] private Vector4 cardTypeTextRectPct = new(0.30f, 0.89f, 0.40f, 0.06f);
    [Tooltip("코스트 숫자 위치 오프셋 (orb 중심 기준, 카드 폭 대비 비율). X=우측, Y=아래.")]
    [SerializeField] private Vector2 cardCostTextOffsetPct = new(0f, 0f);
    [Tooltip("코스트 숫자 크기 오프셋 (orb 크기 대비 비율 추가). 0 = orb 크기 그대로.")]
    [SerializeField, Range(-0.5f, 0.5f)] private float cardCostTextRectShrinkPct = 0f;

    [Header("Mana Orb (좌하단)")]
    [Tooltip("좌하단 마나 오브 지름 (RefH 좌표 기준 px).")]
    [SerializeField, Range(40f, 240f)] private float manaOrbSize = 105f;
    [Tooltip("좌하단 마나 오브 중심 X (RefW 좌표 기준 px, 좌측 0).")]
    [SerializeField, Range(40f, 400f)] private float manaOrbCenterX = 210f;
    [Tooltip("좌하단 마나 오브 중심이 화면 하단에서 떨어진 거리 (px). 클수록 위로 올라감.")]
    [SerializeField, Range(20f, 200f)] private float manaOrbBottomOffset = 70f;
    [Tooltip("마나 텍스트 크기 비율 (orb 지름 × 이 비율).")]
    [SerializeField, Range(0.10f, 0.50f)] private float manaOrbFontSizeRatio = 0.22f;

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
    [SerializeField, Range(0, 60)] private int _normal1FogCount = 24;
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
    // HP 바 위치별 '표시 fraction' — 실제 hp가 내려가면 이 값이 천천히 따라내려가며 pale trail을 만든다
    private readonly Dictionary<Vector2, float> _hpBarDisplayedFrac = new();
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
    private const float ShieldFxDuration = 1.2f;

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
    private GUIStyle _cardTypeStyle;
    private GUIStyle _cardDescStyle;
    private bool _stylesReady;

    // 덱 뷰어 — 상단 바 계단(Floor) 아이콘 왼쪽 버튼 클릭 시 오픈.
    // run.deck 전체를 id로 그룹핑해 카드 그리드로 보여주며, 정렬 탭과 스크롤 지원.
    private bool _deckViewerOpen;
    private int _deckViewerSortMode;  // 0=획득순, 1=유형, 2=비용, 3=이름순
    private Vector2 _deckViewerScroll;

    private class DamageFloater
    {
        public object anchor;
        public int amount;
        public float delay;
        public float age;
        public const float LifeTime = 1.2f;
    }

    // =========================================================
    // Lifecycle
    // =========================================================

    void Start()
    {
        if (!DataManager.Instance.IsLoaded) DataManager.Instance.Load();
        LoadCardSprites();
        LoadEnemySprites();
        _cardFrameTexture = Resources.Load<Texture2D>("CardSlot/CardFrame");
        if (_cardFrameTexture == null)
            Debug.LogWarning("[BattleUI] CardFrame texture not found: Resources/CardSlot/CardFrame");
        _cardBgTexture = Resources.Load<Texture2D>("CardSlot/CardBg");
        if (_cardBgTexture == null)
            Debug.LogWarning("[BattleUI] CardBg texture not found: Resources/CardSlot/CardBg");
        _cardBorderTexture = Resources.Load<Texture2D>("CardSlot/CardBorder");
        if (_cardBorderTexture == null)
            Debug.LogWarning("[BattleUI] CardBorder texture not found: Resources/CardSlot/CardBorder");

        _cardArtFrameTexture = Resources.Load<Texture2D>("CardSlot/CardArtFrame");
        if (_cardArtFrameTexture == null)
            Debug.LogWarning("[BattleUI] CardArtFrame texture not found: Resources/CardSlot/CardArtFrame");

        _cardDescPanelTexture = Resources.Load<Texture2D>("CardSlot/CardDescPanel");
        if (_cardDescPanelTexture == null)
            Debug.LogWarning("[BattleUI] CardDescPanel texture not found: Resources/CardSlot/CardDescPanel");

        _cardCountBadgeTexture = Resources.Load<Texture2D>("CardSlot/CardCountBadge");
        if (_cardCountBadgeTexture == null)
            Debug.LogWarning("[BattleUI] CardCountBadge texture not found: Resources/CardSlot/CardCountBadge");

        _manaFrameTexture = Resources.Load<Texture2D>("CardSlot/ManaFrame");
        if (_manaFrameTexture == null)
            Debug.LogWarning("[BattleUI] ManaFrame texture not found: Resources/CardSlot/ManaFrame");

        _manaOrbTexture = Resources.Load<Texture2D>("CardSlot/ManaOrb");
        if (_manaOrbTexture == null)
            Debug.LogWarning("[BattleUI] ManaOrb texture not found: Resources/CardSlot/ManaOrb");

        // StS-style v2 레이어 (흰색 PNG, 코드에서 tint).
        _cardFrameBaseTexture = Resources.Load<Texture2D>("CardSlot/CardFrameBase");
        if (_cardFrameBaseTexture == null)
            Debug.LogWarning("[BattleUI] CardFrameBase texture not found: Resources/CardSlot/CardFrameBase");
        _cardArtPlateTexture = Resources.Load<Texture2D>("CardSlot/CardArtPlate");
        if (_cardArtPlateTexture == null)
            Debug.LogWarning("[BattleUI] CardArtPlate texture not found: Resources/CardSlot/CardArtPlate");
        _cardRibbonTexture = Resources.Load<Texture2D>("CardSlot/CardRibbon");
        if (_cardRibbonTexture == null)
            Debug.LogWarning("[BattleUI] CardRibbon texture not found: Resources/CardSlot/CardRibbon");
        _cardTrimTexture = Resources.Load<Texture2D>("CardSlot/CardTrim");
        if (_cardTrimTexture == null)
            Debug.LogWarning("[BattleUI] CardTrim texture not found: Resources/CardSlot/CardTrim");
        _cardTypeLabelTexture = Resources.Load<Texture2D>("CardSlot/CardTypeLabel");
        if (_cardTypeLabelTexture == null)
            Debug.LogWarning("[BattleUI] CardTypeLabel texture not found: Resources/CardSlot/CardTypeLabel");
        _cardCostGemTexture = Resources.Load<Texture2D>("CardSlot/CostGem");
        if (_cardCostGemTexture == null)
            Debug.LogWarning("[BattleUI] CostGem texture not found: Resources/CardSlot/CostGem");
        _cardCostGemInnerTexture = Resources.Load<Texture2D>("CardSlot/CostGemInner");
        if (_cardCostGemInnerTexture == null)
            Debug.LogWarning("[BattleUI] CostGemInner texture not found: Resources/CardSlot/CostGemInner");

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
        _iconFloor   = Resources.Load<Texture2D>("InGame/Icon/Floor");
        _iconShield       = Resources.Load<Texture2D>("InGame/Icon/Shield");
        _iconShieldGreen  = Resources.Load<Texture2D>("InGame/Icon/ShieldGreen");
        _iconAttack       = Resources.Load<Texture2D>("InGame/Icon/Attack");
        _topBarBg   = Resources.Load<Texture2D>("InGame/TopBar");
        _hudDividerTexMap     = Resources.Load<Texture2D>("Map/divider_map");
        _hudDividerTexVillage = Resources.Load<Texture2D>("VillageUI/divider_village");
        _hudDividerTexBattle  = Resources.Load<Texture2D>("InGame/divider_battle"); // 유저가 넣을 예정 — 없으면 null
        _endTurnButtonTex = Resources.Load<Texture2D>("InGame/EndTurnButton");
        if (_endTurnButtonTex == null)
            Debug.LogWarning("[BattleUI] EndTurnButton texture not found: Resources/InGame/EndTurnButton");
        if (_iconHP     == null) Debug.LogWarning("[BattleUI] HP icon not found: Resources/InGame/Icon/HP");
        if (_iconGold   == null) Debug.LogWarning("[BattleUI] Gold icon not found: Resources/InGame/Icon/Gold");
        if (_iconMana   == null) Debug.LogWarning("[BattleUI] Mana icon not found: Resources/InGame/Icon/Mana");
        if (_iconPotion == null) Debug.LogWarning("[BattleUI] Potion icon not found: Resources/InGame/Icon/Potion_Bottle");
        if (_iconRelic  == null) Debug.LogWarning("[BattleUI] Relic icon not found: Resources/InGame/Icon/Relic");
        if (_iconDeck    == null) Debug.LogWarning("[BattleUI] Deck icon not found: Resources/InGame/Icon/Deck");
        if (_iconDiscard == null) Debug.LogWarning("[BattleUI] Discard icon not found: Resources/InGame/Icon/Discard");
        if (_iconCardBack == null) Debug.LogWarning("[BattleUI] CardBack icon not found: Resources/InGame/Icon/CardBack");
        if (_iconFloor   == null) Debug.LogWarning("[BattleUI] Floor icon not found: Resources/InGame/Icon/Floor");
        if (_iconShield       == null) Debug.LogWarning("[BattleUI] Shield icon not found: Resources/InGame/Icon/Shield");
        if (_iconShieldGreen  == null) Debug.LogWarning("[BattleUI] ShieldGreen icon not found: Resources/InGame/Icon/ShieldGreen");
        if (_iconAttack       == null) Debug.LogWarning("[BattleUI] Attack icon not found: Resources/InGame/Icon/Attack");
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
                _hpBarDisplayedFrac.Clear();
                _floaters.Clear();
                _targetingCardIndex = -1;
                _targetingSummonIndex = -1;
                _targetingSummonSkillIndex = -1;
                _swapFromCardIndex = -1;
                _endTurnAnimating = false;
                _attackingUnit = null;
                _attackProgress = 0;
                _prevPlayerBlock = 0;
                _playerShieldFxStartTime = -1f;
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
            _hpBarDisplayedFrac.Clear();
            _floaters.Clear();
            _targetingCardIndex = -1;
            _targetingSummonIndex = -1;
            _targetingSummonSkillIndex = -1;
            _swapFromCardIndex = -1;
            _endTurnAnimating = false;
            _attackingUnit = null;
            _attackProgress = 0;
            _prevPlayerBlock = 0;
            _playerShieldFxStartTime = -1f;
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
            CleanupDeadEnemyViews();

            // 플레이어 block 증가 감지 → 방패 이펙트 트리거
            int curBlock = _battle.state.player.block;
            if (curBlock > _prevPlayerBlock)
                _playerShieldFxStartTime = Time.time;
            _prevPlayerBlock = curBlock;
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

    private void LoadCardSprites()
    {
        foreach (var card in DataManager.Instance.Cards.Values)
        {
            if (string.IsNullOrEmpty(card.image)) continue;

            string filename = Path.GetFileNameWithoutExtension(card.image);

            // 카드 표시용 일러스트 — 타입별 서브폴더
            string subfolder = card.cardType switch
            {
                CardType.SUMMON => "Summon",
                CardType.MAGIC  => "Spell",
                _               => "Utility", // BUFF / UTILITY / RITUAL
            };
            var tex = Resources.Load<Texture2D>($"CardArt/{subfolder}/{filename}");
            if (tex != null) _cardSprites[card.id] = tex;
            else Debug.LogWarning($"[BattleUI] Card sprite not found: CardArt/{subfolder}/{filename}");

            // 필드용 공룡 스프라이트 (투명 배경) — SUMMON만
            if (card.cardType == CardType.SUMMON)
            {
                var fieldTex = Resources.Load<Texture2D>("Dinos/" + filename);
                if (fieldTex != null) _fieldDinoSprites[card.id] = fieldTex;
                else Debug.LogWarning($"[BattleUI] Field dino sprite not found: Dinos/{filename}");
            }
        }

        // 정적 폴백 스프라이트 — attack 시퀀스가 없을 때만 사용. 없어도 PlayerView는 시퀀스로 만들 수 있음.
        _playerSprite = Resources.Load<Texture2D>("Character_infield/Char_Archaeologist_Field");
        EnsurePlayerView();
    }

    private void EnsurePlayerView()
    {
        if (_playerView != null) return;

        // Character_infield/Archaeologist/ 에서 공격(attack_f##), 피격(hit_f##), 소환(summon_f##) 시퀀스를 순서대로 로드.
        // hit/summon은 없으면 attack 시퀀스로 폴백한다.
        var attackSeq = LoadFrameSequence("Character_infield/Archaeologist/attack_f");
        var hitSeq    = LoadFrameSequence("Character_infield/Archaeologist/hit_f");
        var summonSeq = LoadFrameSequence("Character_infield/Archaeologist/summon_f");
        if (hitSeq == null || hitSeq.Length == 0)       hitSeq = attackSeq;
        if (summonSeq == null || summonSeq.Length == 0) summonSeq = attackSeq;

        // Idle / 베이스 스프라이트 = character_basic/Idle.png. 없으면 공격 시퀀스 첫 프레임 → Char_Archaeologist_Field 폴백.
        var idleTex = Resources.Load<Texture2D>("Character_infield/character_basic/Idle");
        Sprite idleSprite = idleTex != null ? TexToSprite(idleTex) : null;
        Sprite baseSprite = idleSprite
            ?? (attackSeq != null && attackSeq.Length > 0 ? attackSeq[0] : null)
            ?? (_playerSprite != null ? TexToSprite(_playerSprite) : null);

        if (baseSprite == null)
        {
            Debug.LogWarning("[BattleUI] PlayerView init skipped — Character_infield/Archaeologist/attack_f## 없음 + Char_Archaeologist_Field 폴백도 없음");
            return;
        }

        _playerWorldSprite = baseSprite;

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
            Debug.Log($"[BattleUI] Archaeologist attack sequence loaded: {attackSeq.Length} frames");
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

        if (attackSeq == null || attackSeq.Length == 0)
            Debug.LogWarning("[BattleUI] Character_infield/Archaeologist/attack_f## 시퀀스 없음 — 정적 Char_Archaeologist_Field 폴백 사용");

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
    }

    /// <summary>Resources 경로 프리픽스 뒤에 01, 02… 를 붙여가며 연속적으로 로드한다 (끊기는 번호에서 중단, 최대 99).
    /// 예: LoadFrameSequence("Character_infield/Archaeologist/attack_f") → attack_f01, attack_f02, ... 를 순서대로.</summary>
    private static Sprite[] LoadFrameSequence(string pathPrefix)
    {
        var list = new System.Collections.Generic.List<Sprite>();
        for (int i = 1; i <= 99; i++)
        {
            var tex = Resources.Load<Texture2D>($"{pathPrefix}{i:D2}");
            if (tex == null) break;
            list.Add(TexToSprite(tex));
        }
        return list.Count > 0 ? list.ToArray() : null;
    }

    private static Sprite TexToSprite(Texture2D tex)
    {
        return Sprite.Create(
            tex,
            new Rect(0, 0, tex.width, tex.height),
            new Vector2(0.5f, 0f),
            100f);
    }

    private void OnDestroy()
    {
        if (_playerView != null && _playerView.gameObject != null)
            Destroy(_playerView.gameObject);
        DestroyAllEnemyViews();
        if (_normal1FogTex != null) Destroy(_normal1FogTex);
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
        // 좌/우 변형 두 종 — 보스 좌측 코너엔 L(불꽃이 좌상단으로 흩날림), 우측 코너엔 R(우상단으로 흩날림) 스프라이트를 ComputeSlotPositions에서 코너별로 스왑.
        // _enemySprites/_enemyWorldSprites["MOSS_E901"] 기본값은 L — 첫 프레임에 view 생성 시 폴백.
        var mossTexL = Resources.Load<Texture2D>("Monsters/E901_Moss_L");
        var mossTexR = Resources.Load<Texture2D>("Monsters/E901_Moss_R");
        if (mossTexL != null)
        {
            _mossWorldSpriteL = TexToSprite(mossTexL);
            _enemySprites["MOSS_E901"] = mossTexL;
            _enemyWorldSprites["MOSS_E901"] = _mossWorldSpriteL;
        }
        if (mossTexR != null) _mossWorldSpriteR = TexToSprite(mossTexR);
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

        // 런타임 소환된 쫄(EnemyData가 DataManager에 없음) 등은 캐시에 없을 수 있음 — placeholder 생성
        if (!_enemyWorldSprites.TryGetValue(e.data.id, out var sprite))
        {
            var tex = BuildEnemyPlaceholderTex(e.data);
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
        // 동시 박자 방지 — 개체별 해시로 주기(freq)와 위상(phase)을 모두 분산.
        // freq: 0.12 ~ 0.19Hz (~5.3s ~ 8.3s), phase: 0 ~ 2π
        int hash = e.GetHashCode();
        float freqNoise = ((hash >> 10) & 0x3FF) / 1024f;        // 0~1
        float phaseNoise = (hash & 0x3FF) / 1024f;               // 0~1
        view.breathingFreq = 0.12f + freqNoise * 0.07f;
        view.breathingPhase = phaseNoise * Mathf.PI * 2f;
        // 이끼 잡몹은 도깨비불처럼 살짝 투명하게 — 보스 BG 톤에 묻혀 들어가게.
        if (e.isMoss) view.SetBaseColor(new Color(1f, 1f, 1f, 0.7f));
        _enemyViews[e] = view;

        // 발밑 그림자 — 이미지 파일명 규칙(`Monsters/shadow/{이름}_shadow`)으로 로드.
        // 예: crow.png → Monsters/shadow/crow_shadow, E101_StoneGolem.png → E101_StoneGolem_shadow.
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
            if (kv.Key.IsDead)
            {
                if (kv.Value != null && kv.Value.gameObject != null)
                    Destroy(kv.Value.gameObject);
                (toRemove ??= new List<EnemyInstance>()).Add(kv.Key);
            }
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
        DestroyAllEnemyViews();
        _lastKnownHp.Clear();
        _hpBarDisplayedFrac.Clear();
        _floaters.Clear();
        _pending.Clear();
        _battleEndQueued = false;
        _targetingCardIndex = -1;
        _targetingSummonIndex = -1;
        _targetingSummonSkillIndex = -1;
        _swapFromCardIndex = -1;

        var chapter = DataManager.Instance.GetChapter(run.chapterId);
        int mana = chapter?.mana ?? 3;
        int maxFieldSize = chapter?.maxFieldSize ?? 2;

        // 이전 전투에서 남은 애니메이션 상태를 정리
        EndDiscardFlyAnimation();
        EndDrawFlyAnimation();
        EndReshuffleAnimation();

        _battle = new BattleManager();
        _battle.StartBattle(
            new List<CardData>(run.deck),
            new List<EnemyData>(enemies), // 복사본 전달
            mana,
            run.playerMaxHp,
            maxFieldSize);

        // 현재 run의 HP로 플레이어 초기화 (이전 전투 잔존 HP 반영)
        _battle.state.player.hp = Mathf.Clamp(run.playerCurrentHp, 1, run.playerMaxHp);

        PrepareEnemyViews();
        SpawnBackgroundFX();
        SpawnBackgroundVines();

        // 전투 시작 시 이미 Draw된 첫 손패를 드로우 애니메이션으로 등장시킨다.
        if (_battle.state.hand.Count > 0)
        {
            StartCoroutine(InitialDrawCoroutine());
        }
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

    /// <summary>플레이어 공격 시 ComputeAttackDir + 타겟 world 좌표 기반으로 FX 예약.</summary>
    private void TriggerPlayerAttackFx(int preferredEnemyIdx, float attackDuration = 0.75f)
    {
        if (_playerAttackFxSprite == null) return;
        var targetWorld = GetAttackTargetWorld(preferredEnemyIdx);
        if (targetWorld == Vector3.zero) return;
        // 공격 peak는 sequence routine에서 60% 지점. 그 타이밍에 FX 스폰.
        SpawnAttackFx(_playerAttackFxSprite, targetWorld, peakDelay: attackDuration * 0.55f, lifetime: 0.35f, size: 1.8f);
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

    private Texture2D LoadBackgroundFor(EnemyData enemy)
    {
        if (enemy.enemyType == EnemyType.BOSS)
        {
            var boss = Resources.Load<Texture2D>("Backgrounds/BG_Ch1_Boss_01");
            if (boss == null) Debug.LogWarning("[BattleUI] Background not found: Resources/Backgrounds/BG_Ch1_Boss_01");
            return boss;
        }
        if (enemy.enemyType == EnemyType.ELITE)
        {
            var elite = Resources.Load<Texture2D>("Backgrounds/BG_Ch1_Elite_01");
            if (elite == null) Debug.LogWarning("[BattleUI] Background not found: Resources/Backgrounds/BG_Ch1_Elite_01");
            return elite;
        }

        // Normal: BG_Ch1_Battle_NN 중 랜덤 선택
        var all = Resources.LoadAll<Texture2D>("Backgrounds");
        var variants = new List<Texture2D>();
        foreach (var t in all)
        {
            if (t != null && t.name.StartsWith("BG_Ch1_Battle_")) variants.Add(t);
        }
        if (variants.Count == 0)
        {
            Debug.LogWarning("[BattleUI] No BG_Ch1_Battle_NN backgrounds found in Resources/Backgrounds");
            return Resources.Load<Texture2D>("Backgrounds/BG_Ch1_Battle_02");
        }
        return variants[UnityEngine.Random.Range(0, variants.Count)];
    }

    // =========================================================
    // Damage detection & floaters
    // =========================================================

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
                _floaters.Add(new DamageFloater
                {
                    anchor = unit,
                    amount = delta,
                    delay = delay,
                    age = 0,
                });
                if (_slotPositions.TryGetValue(unit, out var guiPos))
                {
                    StartCoroutine(SpawnDamageVFXDelayed(guiPos, delay));
                }
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
                newFloatersThisFrame++;
            }
        }
        _lastKnownHp[unit] = currentHp;
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

    private void SpawnDamageVFX(Vector2 guiPos)
    {
        if (Camera.main == null) return;
        Vector3 world = GuiToWorld(guiPos);

        if (_vfxHitA   != null) Instantiate(_vfxHitA,   world, Quaternion.identity);
        if (_vfxHitD   != null) Instantiate(_vfxHitD,   world, Quaternion.identity);
        if (_vfxSmokeF != null) Instantiate(_vfxSmokeF, world, Quaternion.identity);
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
        var gsm = GameStateManager.Instance;
        if (gsm == null) return;
        // Reward 상태에서도 배경/전장은 계속 그려서 보상 화면 뒤로 비춰야 함
        if (gsm.State != GameState.Battle && gsm.State != GameState.Reward) return;
        if (_battle == null || _battle.state == null) return;

        // GUI.depth: 낮을수록 앞. BattleUI는 뒤에 깔리고 RewardUI(=0)가 위로 올라오도록
        GUI.depth = 10;

        // 매 프레임 호버 툴팁 상태 리셋 — 이번 프레임에 패시브 칩 위에 마우스 있으면 채워짐.
        _hoveredPassiveTitle = null;
        _hoveredPassiveBody = null;

        EnsureStyles();

        bool active = gsm.State == GameState.Battle;

        if (active)
        {
            // 우클릭으로 타겟팅 취소 (카드/공룡/공룡스킬/교체/융합 모두)
            if ((_targetingCardIndex >= 0 || _targetingSummonIndex >= 0 || _targetingSummonSkillIndex >= 0 || _swapFromCardIndex >= 0)
                && Event.current.type == EventType.MouseDown
                && Event.current.button == 1)
            {
                if (_targetingSummonIndex >= 0) ShowToast("공격을 취소합니다");
                else if (_targetingSummonSkillIndex >= 0) ShowToast("스킬을 취소합니다");
                _targetingCardIndex = -1;
                _targetingSummonIndex = -1;
                _targetingSummonSkillIndex = -1;
                _swapFromCardIndex = -1;
                _fusionMaterialAPicked = false;
                Event.current.Use();
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
            }
            // 공룡 공격 타겟팅과 스킬 타겟팅도 상호 배타
            if (_targetingSummonIndex >= 0) _targetingSummonSkillIndex = -1;
            if (_targetingSummonSkillIndex >= 0) _targetingSummonIndex = -1;
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
        DrawFloaters();
        var run = gsm.CurrentRun;
        if (run != null)
        {
            var map = gsm.CurrentMap;
            int totalFloors = map != null ? map.totalFloors : 5;
            DrawTopBar(HudContext.Battle, run, run.currentFloor, totalFloors,
                       hpCurrent: state.player.hp, hpMax: state.player.maxHp);
        }
        DrawTurnInfo(state);

        // Reward 상태에서는 상호작용 UI(손패/턴 종료/타겟팅 힌트) 숨김.
        // 덱 뷰어가 열려있을 때도 손패 상호작용을 막아 오버레이 아래 카드 클릭이 새지 않게 함.
        if (active && !_deckViewerOpen)
        {
            DrawHand(state);
            DrawHandHideToggle();
            DrawEndTurn(state);
            DrawTargetingHint(state);
            DrawSummonAttackHint(state);
            DrawSummonSkillHint(state);
        }
        DrawToast();

        // 버린 더미로 날아가는 카드 — reward 상태와 관계없이 위에 그려져야 자연스럽다.
        DrawDiscardFlyingCards();

        // 덱 리셔플 — 버림 더미 → 덱 더미 스트림
        DrawReshuffleFlyingCards();

        // 덱에서 뽑혀오는 카드 (뒷면 → 플립 → 앞면) — 최상단에 그려 손패/UI 위로 드러나게 함.
        DrawDrawFlyingCards();

        // 덱 뷰어 오버레이 — 모든 UI 위에 그려짐.
        DrawDeckViewerOverlay(gsm);

        // 패시브 호버 툴팁 — 최상단에 그려야 다른 UI 위로 나옴.
        DrawPassiveTooltip();
    }

    /// <summary>적의 패시브 리스트를 HP 바 아래 한 줄 칩으로 그리고, 호버 시 툴팁 정보 세팅.</summary>
    private void DrawEnemyPassives(Rect rowRect, EnemyInstance e)
    {
        if (e?.data?.passiveIds == null || e.data.passiveIds.Count == 0) return;

        EnsurePassiveStyles();

        const float chipH = 20f;
        const float chipPadX = 8f;
        const float chipGap = 4f;

        // 각 칩 가로폭을 내용에 맞춰 계산하고 왼쪽부터 배치. 공간 넘치면 잘림.
        float x = rowRect.x;
        float y = rowRect.y;

        foreach (var pid in e.data.passiveIds)
        {
            var p = DianoCard.Data.DataManager.Instance.GetPassive(pid);
            string label = p != null ? p.nameKr : pid;
            var content = new GUIContent(label);
            var size = _passiveChipStyle.CalcSize(content);
            float chipW = size.x + chipPadX * 2f;
            if (x + chipW > rowRect.xMax) break; // 넘치면 잘라냄

            var chipRect = new Rect(x, y, chipW, chipH);

            // 배경 — 둥근 느낌을 주는 반투명 칩. 호버 시 밝아짐.
            var ev = Event.current;
            bool hovered = ev != null && chipRect.Contains(ev.mousePosition);

            Color bg = hovered
                ? new Color(0.45f, 0.12f, 0.16f, 0.96f)
                : new Color(0.20f, 0.06f, 0.08f, 0.88f);
            Color border = new Color(1f, 0.8f, 0.4f, hovered ? 1f : 0.85f);

            FillRect(chipRect, bg);
            DrawBorder(chipRect, 1, border);

            // 라벨
            var labelRect = new Rect(chipRect.x + chipPadX, chipRect.y, size.x, chipH);
            GUI.Label(labelRect, content, _passiveChipStyle);

            if (hovered && p != null)
            {
                _hoveredPassiveTitle = p.nameKr;
                _hoveredPassiveBody = p.description;
            }

            x += chipW + chipGap;
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
        string text = $"▶ {s.data.nameKr} 공격 — 적을 클릭하세요  (우클릭: 취소)";
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
        string text = $"✦ {s.data.nameKr} {skill.nameKr} — 적을 클릭하세요  (우클릭: 취소)";
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 2.2f);
        float alpha = Mathf.Lerp(0.35f, 0.95f, pulse);
        var prev = GUI.color;
        GUI.color = new Color(0.85f, 1f, 0.95f, alpha);
        GUI.Label(new Rect(0, 115, RefW, 30), text, _targetHintStyle);
        GUI.color = prev;
    }

    private void DrawTargetingHint(BattleState state)
    {
        if (_targetingCardIndex < 0 || _targetingCardIndex >= state.hand.Count) return;
        var c = state.hand[_targetingCardIndex].data;
        string text;
        if (CardNeedsFusionTargets(c))
        {
            text = _fusionMaterialAPicked
                ? $"▶ {c.nameKr} — 두 번째 재료(같은 종·같은 티어)를 클릭  (우클릭: 취소)"
                : $"▶ {c.nameKr} — 융합할 육식공룡 두 마리 중 첫 재료를 클릭 (필드/손)  (우클릭: 취소)";
        }
        else if (CardNeedsAllyTarget(c))
        {
            text = $"▶ {c.nameKr} 사용 중 — 아군 공룡을 클릭하세요  (우클릭: 취소)";
        }
        else
        {
            text = $"▶ {c.nameKr} 사용 중 — 적을 클릭하세요  (우클릭: 취소)";
        }

        // 살짝 보였다 사라졌다 하는 알파 펄스 (sin 0~1 → 0.35~0.95)
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 2.2f);
        float alpha = Mathf.Lerp(0.35f, 0.95f, pulse);

        var prevColor = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, alpha);
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
            fontSize = 11,
            alignment = TextAnchor.MiddleRight,
            fontStyle = FontStyle.Bold,
            wordWrap = false,
            clipping = TextClipping.Overflow,
            normal = { textColor = Color.white },
        };
        _damageStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 32,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white },
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
            fontSize = 22,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.95f, 0.6f) },
        };
        _cardNameStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.92f, 0.75f) },
        };
        _cardTypeStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            // 어두운 회색 pill 위에 올라가므로 밝은 색으로 대비 확보 (v2 프레임).
            normal = { textColor = new Color(0.92f, 0.92f, 0.95f) },
        };
        _cardDescStyle = new GUIStyle(GUI.skin.label)
        {
            // 카드피커 body 스타일과 톤 맞춤
            fontSize = 11,
            alignment = TextAnchor.UpperCenter,
            fontStyle = FontStyle.Bold,
            wordWrap = true,
            padding = new RectOffset(6, 6, 4, 4),
            normal = { textColor = new Color(0.96f, 0.92f, 0.74f) },
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
        LockStateColors(_cardTypeStyle);
        LockStateColors(_cardDescStyle);

        _stylesReady = true;
    }

    // GUIStyle의 모든 인터랙션 state의 텍스트 색을 normal과 동일하게 고정.
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

    private static bool CardNeedsTarget(CardData c)
    {
        return CardNeedsEnemyTarget(c) || CardNeedsAllyTarget(c) || CardNeedsFusionTargets(c);
    }

    private static bool CardNeedsEnemyTarget(CardData c)
    {
        return c.cardType == CardType.MAGIC
            && c.subType == CardSubType.ATTACK
            && c.target == TargetType.ENEMY;
    }

    // ALLY 단일 타겟 카드 — 수호 마법(MAGIC/DEFENSE + ALLY)만 해당.
    // 융합(MAGIC/FUSION)은 2개 재료 지정이 필요해 별도 흐름으로 처리 (CardNeedsFusionTargets).
    private static bool CardNeedsAllyTarget(CardData c)
    {
        if (c.target != TargetType.ALLY) return false;
        if (c.cardType != CardType.MAGIC) return false;
        return c.subType == CardSubType.DEFENSE;
    }

    // 융합 카드 — 재료 2개(필드/손 자유 조합) 지정 필요.
    private static bool CardNeedsFusionTargets(CardData c)
    {
        return c.cardType == CardType.MAGIC && c.subType == CardSubType.FUSION;
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

        return candidateBaseId == aBaseId && candidateTier == aTier;
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
    private static bool IsAttackSpell(CardData c)
    {
        return c.cardType == CardType.MAGIC
            && c.subType == CardSubType.ATTACK;
    }

    // =========================================================
    // Battle field rendering
    // =========================================================

    // 지면 라인 — 플레이어 캐릭터 발끝이 닿는 GUI Y. 카드 상단(≈567) 약간 위로 잡아 HP 바 겹침 방지.
    // 공룡 발끝 위치는 모두 이 라인 기준으로 계산 (사람 기준).
    private const float GroundY = 560f;

    private void ComputeSlotPositions(BattleState state)
    {
        // DrawPlayerNPC의 h(=257)와 일치 — h/2여야 발끝이 GroundY에 정확히 닿음.
        const float PlayerHalfH = 128f;

        _slotPositions.Clear();
        _mossDepthScale.Clear();
        _slotPositions[state.player] = new Vector2(230, GroundY - PlayerHalfH - 10);

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
            if (e.IsDead) continue;
            if (e.isMoss) { mossAlive.Add(e); continue; }
            // 적 크기는 타입별로 다름 — 발끝이 GroundY에 닿도록 센터 Y를 h/2만큼 위로.
            // staggerY는 뒤쪽 적이 멀어 보이게 하되, 안개 지평선(40%)으로 밀려나지 않을 정도로만.
            float h = GetEnemyDrawHeight(e);
            // 보스는 검·갑옷 실루엣이 우측 끝을 벗어나지 않게 살짝 안쪽으로.
            float baseX = (e.data.enemyType == EnemyType.BOSS) ? 970f : 1070f;
            _slotPositions[e] = new Vector2(baseX - aliveIdx * 160, GroundY - h / 2f - aliveIdx * 22);
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
                new Vector2(-220f, -bossH * 0.30f),  // 0: 위-좌
                new Vector2(+170f, -bossH * 0.30f),  // 1: 위-우
                new Vector2(-220f, +bossH * 0.22f),  // 2: 아래-좌
                new Vector2(+170f, +bossH * 0.22f),  // 3: 아래-우
            };
            float[] cornerScale = { 0.85f, 0.85f, 1.05f, 1.05f }; // 위 작게, 아래 크게
            for (int i = 0; i < mossAlive.Count; i++)
            {
                int cornerIdx = i % 4;
                var m = mossAlive[i];
                _slotPositions[m] = bossPos + corners[cornerIdx];
                _mossDepthScale[m] = cornerScale[cornerIdx];

                // 좌측 코너(0,2) = L 스프라이트(불꽃 좌상단 흩날림), 우측(1,3) = R(우상단 흩날림).
                bool isLeft = (cornerIdx == 0 || cornerIdx == 2);
                Sprite target = isLeft ? _mossWorldSpriteL : _mossWorldSpriteR;
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
        float baseH = e.data.enemyType switch
        {
            EnemyType.BOSS  => 400f,
            EnemyType.ELITE => 320f,
            _               => 240f,
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
            if (_summonDisplayPositions.TryGetValue(s, out var pos)) DrawSummon(s, i, pos);
        }

        for (int i = 0; i < state.enemies.Count; i++)
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
            DrawHpBar(barRect, p.hp, p.maxHp, new Color(0.65f, 0.16f, 0.18f), p.block > 0, _playerShieldFxStartTime);

            if (p.block > 0)
            {
                // 방패 뱃지를 HP 바 왼쪽 끝에 살짝 겹치게 — 머리 위 대신 인라인
                DrawBlockBadge(new Vector2(barRect.x, barRect.center.y), p.block, 34f);
            }

            // 디버프 표시 (rough — HP 바 우측 끝)
            if (p.poisonStacks > 0 || p.weakTurns > 0)
            {
                var sb = new System.Text.StringBuilder();
                if (p.poisonStacks > 0) sb.Append($"☠{p.poisonStacks} ");
                if (p.weakTurns > 0) sb.Append($"↓{p.weakTurns}T");
                GUI.Label(new Rect(barRect.xMax - 80, barRect.yMax + 2, 80, 18),
                          sb.ToString().Trim(), _centerStyle);
            }
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
            DrawHpBar(fbHpRect, p.hp, p.maxHp, new Color(0.65f, 0.16f, 0.18f), p.block > 0, _playerShieldFxStartTime);

            if (p.block > 0)
            {
                DrawBlockBadge(new Vector2(fbHpRect.x, fbHpRect.center.y), p.block, 34f);
            }
        }
    }

    // 적 머리 위 intent 표시 — 숫자 + 아이콘을 좌우로 나란히. 공격은 검, 방어는 방패, 버프는 텍스트.
    private void DrawEnemyIntent(Vector2 center, EnemyInstance e)
    {
        if (e.intentType == EnemyIntentType.ATTACK)
        {
            DrawAttackIconBadge(center, e.intentValue, -45f, boosted: false);
            DrawTargetHint(center, e);
            return;
        }

        if (e.intentType == EnemyIntentType.DEFEND && _iconShield != null)
        {
            DrawSideBySideBadge(center, e.intentValue, _iconShield, 0f, Color.white);
            return;
        }

        // 폴백: 텍스트 라벨 (BUFF 또는 아이콘 미로드)
        GUI.Label(new Rect(center.x - 80f, center.y - 12f, 160f, 24f),
                  $"▲ {e.IntentLabel}", _intentStyle);
        // 카운트다운 공격·광역·강탈 등에도 타겟 힌트
        DrawTargetHint(center, e);
    }

    /// <summary>공격 인텐트 아래에 "→ 공룡 / → 플레이어 / → 전체" 타겟 힌트 표시. 반투명 배경 박스로 가독성 확보.</summary>
    private void DrawTargetHint(Vector2 center, EnemyInstance e)
    {
        if (_battle?.state == null) return;
        string hint = GetTargetHint(e);
        if (string.IsNullOrEmpty(hint)) return;

        // 텍스트 크기에 맞춰 배경 박스 동적 크기 결정.
        var content = new GUIContent(hint);
        var textSize = _intentStyle.CalcSize(content);
        float padX = 6f;
        float padY = 2f;
        float boxW = Mathf.Max(80f, textSize.x + padX * 2);
        float boxH = textSize.y + padY * 2;
        // 아이콘 아래에 붙임 (center.y + 18). 스프라이트 상단에 살짝 겹치나 배경 박스로 가독성 확보.
        var boxRect = new Rect(center.x - boxW * 0.5f, center.y + 18f, boxW, boxH);

        // 반투명 검정 배경
        FillRect(boxRect, new Color(0f, 0f, 0f, 0.72f));
        DrawBorder(boxRect, 1, new Color(0f, 0f, 0f, 0.9f));

        // 텍스트 (밝은 노랑)
        var labelRect = new Rect(boxRect.x, boxRect.y + padY, boxRect.width, textSize.y);
        var prev = _intentStyle.normal.textColor;
        _intentStyle.normal.textColor = new Color(1f, 0.88f, 0.5f);
        GUI.Label(labelRect, hint, _intentStyle);
        _intentStyle.normal.textColor = prev;
    }

    private string GetTargetHint(EnemyInstance e)
    {
        bool hasField = _battle.state.field.Count > 0;

        switch (e.intentAction)
        {
            // 단일 대상 공격 — RollIntent 시점에 확정된 intentTargetDino 그대로 표시.
            case DianoCard.Data.EnemyAction.ATTACK:
            case DianoCard.Data.EnemyAction.MULTI_ATTACK:
            case DianoCard.Data.EnemyAction.COUNTDOWN_ATTACK:
                if (e.intentTargetDino != null && !e.intentTargetDino.IsDead)
                    return $"→ {e.intentTargetDino.data.nameKr}";
                return "→ 플레이어";

            // 광역: 플레이어 + 필드 전체
            case DianoCard.Data.EnemyAction.COUNTDOWN_AOE:
                return "→ 전체";

            // 공룡 특정 겨냥
            case DianoCard.Data.EnemyAction.STEAL_SUMMON:
            case DianoCard.Data.EnemyAction.SILENCE:
                return hasField ? "→ 공룡" : "→ (공룡 없음)";

            // 플레이어 직접 디버프
            case DianoCard.Data.EnemyAction.POISON:
            case DianoCard.Data.EnemyAction.WEAK:
            case DianoCard.Data.EnemyAction.DRAIN:
            case DianoCard.Data.EnemyAction.VULNERABLE:
            case DianoCard.Data.EnemyAction.CLOG_DECK:
                return "→ 플레이어";

            default:
                return null; // DEFEND/BUFF_SELF/SUMMON/REFILL_MOSS/ARMOR_UP/IDLE 등은 자기 대상
        }
    }

    // 공격 아이콘(검) + 데미지 숫자 뱃지. 적은 -45°, 아군은 +45°. boosted면 숫자를 강조 색으로.
    private void DrawAttackIconBadge(Vector2 center, int value, float angleDeg, bool boosted)
    {
        if (_iconAttack == null) return;
        Color textCol = boosted ? new Color(1f, 0.85f, 0.3f) : Color.white;
        DrawSideBySideBadge(center, value, _iconAttack, angleDeg, textCol);
    }

    // 아이콘은 center에 정중앙으로 배치, 숫자는 아이콘 왼쪽에 치우쳐서 표시.
    private void DrawSideBySideBadge(Vector2 center, int value, Texture2D icon, float angleDeg, Color textCol)
    {
        const float iconSize = 56f;
        const float numberW = 22f;
        const float overlap = 5f; // 아이콘 가장자리 안으로 숫자 영역을 살짝만 겹쳐 적당한 간격 유지

        var iconRect = new Rect(center.x - iconSize / 2f, center.y - iconSize / 2f, iconSize, iconSize);
        var numRect = new Rect(iconRect.x + overlap - numberW, center.y - iconSize / 2f, numberW, iconSize);

        DrawTextWithOutline(numRect, value.ToString(), _intentNumberStyle,
                            textCol, new Color(0f, 0f, 0f, 0.95f), 1.2f);

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
    }

    // 방패 아이콘 + 숫자 뱃지. center를 중심으로 size 크기로 그림. icon으로 플레이어/적 텍스처 분리.
    private void DrawBlockBadge(Vector2 center, int block, float size = 40f, Texture2D icon = null)
    {
        var iconRect = new Rect(center.x - size / 2, center.y - size / 2, size, size);
        var tex = icon != null ? icon : _iconShield;
        if (tex != null)
        {
            GUI.DrawTexture(iconRect, tex, ScaleMode.ScaleToFit, alphaBlend: true);
        }

        int prevFontSize = _centerStyle.fontSize;
        Color prevColor = _centerStyle.normal.textColor;
        _centerStyle.fontSize = Mathf.RoundToInt(size * 0.42f);
        _centerStyle.normal.textColor = Color.white;

        var shadowRect = new Rect(iconRect.x + 1, iconRect.y + 2, iconRect.width, iconRect.height);
        var prevShadow = _centerStyle.normal.textColor;
        _centerStyle.normal.textColor = new Color(0f, 0f, 0f, 0.75f);
        GUI.Label(shadowRect, block.ToString(), _centerStyle);
        _centerStyle.normal.textColor = prevShadow;

        GUI.Label(iconRect, block.ToString(), _centerStyle);

        _centerStyle.fontSize = prevFontSize;
        _centerStyle.normal.textColor = prevColor;
    }

    // 플레이어 주위에 떠오르는 반투명 방패 버블. block이 증가한 프레임에 트리거되어
    // ShieldFxDuration 동안 페이드 인 → 유지(펄스) → 페이드 아웃.
    private void DrawPlayerShieldFx(Vector2 center, float targetW, float targetH)
    {
        if (_playerShieldFxStartTime < 0f) return;
        var tex = _shieldFxTexture != null ? _shieldFxTexture : _manaFrameTexture;
        if (tex == null) return;

        float t = Time.time - _playerShieldFxStartTime;
        if (t >= ShieldFxDuration)
        {
            _playerShieldFxStartTime = -1f;
            return;
        }

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
        // Lunge 오프셋: 공격 중인 소환수는 오른쪽으로 sin 곡선 이동
        if (ReferenceEquals(_attackingUnit, s))
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

        // Reward 상태면 공룡도 world-space overlay와 같은 톤으로 어둡게 tint
        bool inReward = GameStateManager.Instance != null && GameStateManager.Instance.State == GameState.Reward;
        // 공격 불가 상태(이미 공격 / 침묵)는 어둡게, 이번 턴 선택된 공룡은 살짝 밝게.
        bool selected = _targetingSummonIndex == summonIndex;
        bool dimmed = !s.CanAttack && !inReward;
        Color prevGuiColor = GUI.color;
        if (inReward) GUI.color = new Color(0.6f, 0.6f, 0.6f, 1f);
        else if (dimmed) GUI.color = new Color(0.55f, 0.55f, 0.55f, 1f);
        else if (selected) GUI.color = new Color(1.12f, 1.08f, 0.9f, 1f);

        if (_fieldDinoSprites.TryGetValue(s.data.id, out var tex) && tex.height > 0)
        {
            float aspect = tex.width / (float)tex.height;
            var drawRect = ComputeBottomAnchoredDrawRect(rect, aspect);
            GUI.DrawTexture(drawRect, tex, ScaleMode.StretchToFill, alphaBlend: true);
        }
        else
        {
            FillRect(rect, new Color(0.4f, 0.7f, 0.4f, 0.8f));
            GUI.Label(new Rect(rect.x, rect.y + h / 2 - 10, rect.width, 22),
                      s.data.nameKr, _centerStyle);
        }

        GUI.color = prevGuiColor;

        // HP 바 — 적과 동일 규칙: 스프라이트 발(rect.yMax) 바로 아래 통일 오프셋.
        float summonBarW = ComputeHpBarWidth(rect.width);
        var summonHpRect = new Rect(rect.center.x - summonBarW / 2, rect.yMax + 4f, summonBarW, hpBarHeight);
        DrawHpBar(summonHpRect, s.hp, s.maxHp, new Color(0.65f, 0.16f, 0.18f));

        // 방어도 뱃지 — HP 바 왼쪽에 겹치게 (플레이어와 동일 스타일)
        if (s.block > 0)
        {
            DrawBlockBadge(new Vector2(summonHpRect.x, summonHpRect.center.y), s.block, 30f, _iconShieldGreen);
        }

        // 티어/스택 인디케이터 — 육식: 현재 티어 (T0/T1/T2·MAX). 초식: 덮어쓰기 누적 스택.
        // 육식 진화는 스택이 아니라 "진화의 각인" 카드로 트리거되므로 "합성까지 N장" 표시 없음.
        string stackText = null;
        if (s.data.subType == CardSubType.CARNIVORE)
        {
            if (s.data.id.EndsWith("_T2"))      stackText = "T2 · MAX";
            else if (s.data.id.EndsWith("_T1")) stackText = "T1";
            else                                 stackText = "T0";
        }
        else if (s.stacks > 0)
        {
            stackText = $"스택 {s.stacks}";
        }
        if (!string.IsNullOrEmpty(stackText))
        {
            var stackRect = new Rect(rect.x, summonHpRect.yMax + 3f, rect.width, 16f);
            var prev = _centerStyle.normal.textColor;
            _centerStyle.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
            GUI.Label(new Rect(stackRect.x + 1, stackRect.y + 1, stackRect.width, stackRect.height), stackText, _centerStyle);
            _centerStyle.normal.textColor = new Color(1f, 0.88f, 0.55f);
            GUI.Label(stackRect, stackText, _centerStyle);
            _centerStyle.normal.textColor = prev;
        }

        // ATK 뱃지 — 머리 위 (적 intent와 미러 대칭). 아군은 검을 +45°로 회전.
        // 이 뱃지를 클릭하면 공격 타겟팅 시작 (예전엔 공룡 전체 클릭). 클릭 영역은 시인성보다 살짝 크게.
        Vector2 badgeCenter = new Vector2(rect.center.x, rect.y - 12f);
        DrawAttackIconBadge(badgeCenter, s.TotalAttack, +45f, s.tempAttackBonus > 0);
        var badgeHitRect = new Rect(badgeCenter.x - 36f, badgeCenter.y - 36f, 72f, 72f);
        bool badgeActive = !inReward && _battle?.state != null && !_battle.state.IsOver
            && _targetingCardIndex < 0 && _swapFromCardIndex < 0 && s.CanAttack;
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

        // 상태 라벨 — 우선순위: 도발 > 침묵 > 공격 완료. 스택 인디케이터 아래로 배치.
        string stateLabel = null;
        if (s.tauntTurns > 0)        stateLabel = $"🛡 도발 {s.tauntTurns}T";
        else if (s.silencedTurns > 0) stateLabel = $"침묵 {s.silencedTurns}T";
        else if (s.hasAttackedThisTurn) stateLabel = "공격 완료";
        if (stateLabel != null)
        {
            GUI.Label(new Rect(rect.x, summonHpRect.yMax + 22f, rect.width, 20f),
                      stateLabel, _centerStyle);
        }

        // 스킬 핀 — T1+ 진화 공룡만 (DinoSkillData 존재 시). 평타와 별개 자원.
        // 위치: HP 바 + 상태라벨 아래 (rect.x, summonHpRect.yMax + 42f).
        var skillData = DianoCard.Data.DataManager.Instance.GetSkill(s.data.id);
        if (skillData != null)
        {
            DrawSummonSkillBadge(s, summonIndex, skillData, rect, summonHpRect, inReward);
        }

        // 클릭 처리 우선순위:
        //   1) 교체 모드 (swap) — 필드 꽉 찬 상태에서 SUMMON 카드 플레이 시
        //   2) 아군 타겟 카드 모드 — 수호 마법/먹이 단일 타겟 카드
        //   3) 일반 summon-attack 선택 토글
        if (!inReward && _battle?.state != null && !_battle.state.IsOver)
        {
            var ev = Event.current;
            bool hovered = ev != null && rect.Contains(ev.mousePosition);

            bool allyTargetMode = _targetingCardIndex >= 0
                && _targetingCardIndex < _battle.state.hand.Count
                && CardNeedsAllyTarget(_battle.state.hand[_targetingCardIndex].data);
            bool fusionMode = _targetingCardIndex >= 0
                && _targetingCardIndex < _battle.state.hand.Count
                && CardNeedsFusionTargets(_battle.state.hand[_targetingCardIndex].data);
            bool fieldMaterialEligible = fusionMode && IsFusionMaterialEligible(s, summonIndex, isHand: false);

            if (_swapFromCardIndex >= 0)
            {
                DrawTargetFootGlow(rect, hovered);
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
            else if (allyTargetMode)
            {
                DrawTargetFootGlow(rect, hovered);
                if (ev != null && ev.type == EventType.MouseDown && ev.button == 0 && hovered)
                {
                    ev.Use();
                    int cardIdx = _targetingCardIndex;
                    int allyIdx = summonIndex;
                    _targetingCardIndex = -1;
                    _pending.Add(() => { _battle.PlayCard(cardIdx, -1, -1, allyIdx); });
                }
            }
            else if (fusionMode)
            {
                bool isFusionA = _fusionMaterialAPicked
                    && !_fusionMaterialA.isHand
                    && _fusionMaterialA.index == summonIndex;
                if (isFusionA)
                {
                    // 이미 선택된 재료 A — 글로우 유지, 재클릭으로 선택 해제.
                    DrawTargetFootGlow(rect, true);
                    if (ev != null && ev.type == EventType.MouseDown && ev.button == 0 && hovered)
                    {
                        ev.Use();
                        _fusionMaterialAPicked = false;
                    }
                }
                else if (fieldMaterialEligible)
                {
                    DrawTargetFootGlow(rect, hovered);
                    if (ev != null && ev.type == EventType.MouseDown && ev.button == 0 && hovered)
                    {
                        ev.Use();
                        HandleFusionMaterialClick(DianoCard.Battle.FusionMaterial.Field(summonIndex));
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
    /// 진화 공룡(T1+)의 스킬 핀 — 평타와 별개 자원, 턴 단위 쿨다운.
    /// 위치: HP 바 + 상태 라벨 아래. 가운데 정렬 알약 모양.
    /// 상태:
    ///  - READY: 청록 글로우 + 클릭 가능. ENEMY 타겟이면 _targetingSummonSkillIndex 세팅, AOE/SELF면 즉시 발동.
    ///  - 쿨다운 중: 어두운 회색 + "{n}T" 표시, 비활성.
    ///  - 전투당 1회 사용 후: 어두운 회색 + "✓" 표시, 비활성.
    /// </summary>
    private void DrawSummonSkillBadge(SummonInstance s, int summonIndex, DianoCard.Data.DinoSkillData skill,
                                       Rect summonRect, Rect summonHpRect, bool inReward)
    {
        if (_battle?.state == null) return;

        bool ready = _battle.CanUseSkill(summonIndex);
        bool onCooldown = !ready && skill.cooldownTurns > 0 && s.skillCooldownRemaining > 0;
        bool used = !ready && skill.isOnceBattle && s.skillUsedThisBattle;

        // 알약 — 폭은 공룡 박스 폭 - padding, 높이 22.
        float pillW = Mathf.Max(60f, summonRect.width - 8f);
        float pillH = 22f;
        var pill = new Rect(summonRect.center.x - pillW / 2f, summonHpRect.yMax + 42f, pillW, pillH);

        // 상태별 색
        Color bg, border, textCol;
        if (ready)
        {
            float pulse = 0.7f + 0.3f * Mathf.Sin(Time.time * 4f);
            bg = new Color(0.10f, 0.40f, 0.40f, 0.85f);
            border = new Color(0.35f, 0.95f, 0.85f, pulse);
            textCol = new Color(0.85f, 1f, 0.95f);
        }
        else
        {
            bg = new Color(0.10f, 0.10f, 0.12f, 0.78f);
            border = new Color(0.35f, 0.35f, 0.40f, 0.85f);
            textCol = new Color(0.62f, 0.62f, 0.66f);
        }

        FillRect(pill, bg);
        DrawBorder(pill, ready ? 2 : 1, border);

        // 라벨
        string label;
        if (ready)
        {
            label = $"✦ {skill.nameKr}";
        }
        else if (used)
        {
            label = $"✦ {skill.nameKr} ✓";
        }
        else if (onCooldown)
        {
            label = $"✦ {skill.nameKr} {s.skillCooldownRemaining}T";
        }
        else
        {
            label = $"✦ {skill.nameKr}";
        }

        var prevCol = _centerStyle.normal.textColor;
        int prevSize = _centerStyle.fontSize;
        _centerStyle.normal.textColor = new Color(0f, 0f, 0f, 0.75f);
        _centerStyle.fontSize = 12;
        GUI.Label(new Rect(pill.x + 1, pill.y + 1, pill.width, pill.height), label, _centerStyle);
        _centerStyle.normal.textColor = textCol;
        GUI.Label(pill, label, _centerStyle);
        _centerStyle.normal.textColor = prevCol;
        _centerStyle.fontSize = prevSize;

        // 클릭 — 다른 타겟팅이 진행 중이면 무시. 발동 분기:
        //   - 이미 _targetingSummonSkillIndex가 이 공룡: 토글로 해제
        //   - target=ENEMY: _targetingSummonSkillIndex 세팅 (공격 타겟팅 해제)
        //   - target=ALL_ENEMY / SELF: 즉시 발동 (-1 = AOE/SELF)
        if (inReward) return;
        if (_battle.state.IsOver) return;
        if (!ready) return;
        if (_targetingCardIndex >= 0 || _swapFromCardIndex >= 0) return;

        var ev = Event.current;
        if (ev == null) return;
        if (ev.type != EventType.MouseDown || ev.button != 0) return;
        if (!pill.Contains(ev.mousePosition)) return;
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
            int sIdx = summonIndex;
            var summon = s;
            _pending.Add(() => StartCoroutine(ManualSummonSkillCoroutine(summon, -1)));
        }
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
        DrawEnemyIntent(new Vector2(rect.center.x, rect.y - 44), e);

        // 아트 없는 placeholder 적은 가운데에 이름 라벨 (식별용)
        if (string.IsNullOrEmpty(e.data.image))
        {
            GUI.Label(new Rect(rect.x, rect.center.y - 11, rect.width, 22),
                      e.data.nameKr, _centerStyle);
        }

        // 디버프 스택 표시 (rough — 적 머리 위 우측)
        if (e.poisonStacks > 0 || e.weakTurns > 0)
        {
            var sb = new System.Text.StringBuilder();
            if (e.poisonStacks > 0) sb.Append($"☠{e.poisonStacks} ");
            if (e.weakTurns > 0) sb.Append($"↓{e.weakTurns}T");
            GUI.Label(new Rect(rect.xMax - 70, rect.y + 4, 70, 18), sb.ToString().Trim(), _centerStyle);
        }

        // 이끼 잡몹은 본체 적보다 작으니 HP바도 비례 축소 — min clamp 우회 + 두께도 얇게.
        float enemyBarW = e.isMoss ? rect.width * 0.65f : ComputeHpBarWidth(rect.width);
        float enemyBarH = e.isMoss ? 8f : hpBarHeight;
        var enemyHpRect = new Rect(rect.center.x - enemyBarW / 2, rect.yMax + 4f, enemyBarW, enemyBarH);
        DrawHpBar(enemyHpRect, e.hp, e.data.hp, new Color(0.65f, 0.16f, 0.18f));

        if (e.block > 0)
        {
            // HP 바 왼쪽 끝에 살짝 겹치게 — 플레이어 파란 방패와 미러 대칭
            DrawBlockBadge(new Vector2(enemyHpRect.x, enemyHpRect.center.y), e.block, 34f,
                           _iconShieldGreen);
        }

        // 패시브 칩 — HP 바 바로 아래 한 줄. 호버 시 툴팁.
        DrawEnemyPassives(new Rect(rect.x, enemyHpRect.yMax + 4f, rect.width, 22f), e);

        // 타겟팅 모드: 발치 둥근 글로우 + 클릭 처리 — 적을 대상으로 하는 카드일 때만
        if (_targetingCardIndex >= 0
            && _targetingCardIndex < _battle.state.hand.Count
            && CardNeedsEnemyTarget(_battle.state.hand[_targetingCardIndex].data))
        {
            var ev = Event.current;
            bool hovered = rect.Contains(ev.mousePosition);
            DrawTargetFootGlow(rect, hovered);

            if (ev.type == EventType.MouseDown && ev.button == 0 && hovered)
            {
                ev.Use();
                int cardIdx = _targetingCardIndex;
                int eIdx = enemyIndex;
                _targetingCardIndex = -1;
                _pending.Add(() => {
                    _battle.PlayCard(cardIdx, eIdx);
                    _playerView?.PlayAttack(ComputeAttackDir(eIdx));
                    TriggerPlayerAttackFx(eIdx);
                });
            }
        }
        // 소환수 타겟팅 모드: 선택된 공룡이 이 적을 공격
        else if (_targetingSummonIndex >= 0)
        {
            var ev = Event.current;
            bool hovered = rect.Contains(ev.mousePosition);
            DrawTargetEnemyRing(rect, hovered);

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
            DrawTargetEnemyRing(rect, hovered);

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
    }

    /// <summary>수동 소환수 공격 — lunge 애니메이션 후 데미지 적용.</summary>
    private IEnumerator ManualSummonAttackCoroutine(SummonInstance summon, int enemyIndex)
    {
        if (summon == null || _battle?.state == null) yield break;
        if (!summon.CanAttack) yield break;
        int currentIdx = _battle.state.field.IndexOf(summon);
        if (currentIdx < 0) yield break;
        yield return AnimateLunge(summon, isSummon: true);
        _battle.CommandSummonAttack(currentIdx, enemyIndex);
    }

    /// <summary>수동 소환수 스킬 — lunge 애니메이션 후 스킬 발동. enemyIndex는 ENEMY 타겟에서만 사용 (-1 = AOE/SELF).</summary>
    private IEnumerator ManualSummonSkillCoroutine(SummonInstance summon, int enemyIndex)
    {
        if (summon == null || _battle?.state == null) yield break;
        int currentIdx = _battle.state.field.IndexOf(summon);
        if (currentIdx < 0) yield break;
        if (!_battle.CanUseSkill(currentIdx)) yield break;
        yield return AnimateLunge(summon, isSummon: true);
        _battle.CommandSummonSkill(currentIdx, enemyIndex);
    }

    // 타겟팅 모드에서 선택된 카드 외곽에 부드럽게 빛나는 글로우.
    // 단단한 노란 외곽선 대신 여러 겹의 옅은 보더가 바깥으로 퍼지며 펄스.
    private void DrawSoftCardGlow(Rect cardRect)
    {
        float pulse = 0.6f + 0.4f * Mathf.Sin(Time.time * 3f); // 0.2~1.0
        Color tint = new Color(1f, 0.92f, 0.65f); // 따뜻한 옅은 노랑

        const int layers = 5;
        for (int i = 0; i < layers; i++)
        {
            float t = i / (float)(layers - 1);
            float expand = Mathf.Lerp(1f, 9f, t);
            float thickness = Mathf.Lerp(2f, 1f, t);
            float alpha = Mathf.Lerp(0.55f, 0.05f, t) * pulse;
            var r = new Rect(cardRect.x - expand, cardRect.y - expand,
                             cardRect.width + expand * 2f, cardRect.height + expand * 2f);
            DrawBorder(r, thickness, new Color(tint.r, tint.g, tint.b, alpha));
        }
    }

    // 타겟팅 가능한 적 발치에 떠 있는 납작한 타원형 글로우.
    // 호버되면 더 밝게 펄스, 아니면 옅게 깔려 있어 "여기 클릭 가능"만 알림.
    // 공룡 공격 타겟팅 중 적 전체를 감싸는 형광 시안 ring — "여기 클릭" 시그널.
    // 3겹 단단한 outline(밖→안: 가늘고 옅음→두껍고 진함) + soft inner halo. 펄스 애니메이션. hover 시 더 밝게.
    private void DrawTargetEnemyRing(Rect enemyRect, bool hovered)
    {
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 4f);

        // 형광 시안 — 백색에 가까울 정도로 밝게. hover 시 거의 화이트시안.
        Color baseCol = hovered
            ? new Color(0.65f, 1.00f, 0.95f)
            : new Color(0.30f, 0.95f, 0.85f);

        // 3겹 ripple outline — 외곽 → 안쪽으로 갈수록 두껍고 진해짐.
        float[] paddings = { 24f, 12f, 2f };
        float[] thicknesses = { 2f, 3f, 4f };
        float[] alphas = { 0.45f, 0.75f, 1.00f };

        for (int i = 0; i < paddings.Length; i++)
        {
            float pad = paddings[i];
            var r = new Rect(
                enemyRect.x - pad,
                enemyRect.y - pad,
                enemyRect.width + pad * 2f,
                enemyRect.height + pad * 2f);
            float a = alphas[i] * (0.7f + 0.3f * pulse);
            if (hovered) a = Mathf.Min(1f, a * 1.25f);
            DrawBorder(r, thicknesses[i], new Color(baseCol.r, baseCol.g, baseCol.b, a));
        }

        // 안쪽 soft fill — 적 스프라이트 전체에 옅은 시안 블룸. _manaFrameTexture 있을 때만.
        if (_manaFrameTexture != null)
        {
            var prev = GUI.color;
            float fillA = (hovered ? 0.20f : 0.13f) * (0.7f + 0.3f * pulse);
            GUI.color = new Color(baseCol.r, baseCol.g, baseCol.b, fillA);
            float w = enemyRect.width * 1.2f;
            float h = enemyRect.height * 1.2f;
            var r = new Rect(enemyRect.center.x - w * 0.5f, enemyRect.center.y - h * 0.5f, w, h);
            GUI.DrawTexture(r, _manaFrameTexture, ScaleMode.StretchToFill, alphaBlend: true);
            GUI.color = prev;
        }
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

    private void DrawHpBar(Rect rect, int curr, int max, Color fill, bool blueTint = false, float blueTintStart = -1f)
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

        // 위치 기반 키로 bar의 표시 fraction을 추적 — 데미지 받으면 pale trail이 따라 내려감
        var key = new Vector2(rect.x, rect.y);
        if (!_hpBarDisplayedFrac.TryGetValue(key, out float displayed))
            displayed = realFrac;

        if (Event.current.type == EventType.Repaint)
        {
            if (realFrac < displayed)
                displayed = Mathf.MoveTowards(displayed, realFrac, Time.unscaledDeltaTime * 0.85f);
            else
                displayed = realFrac; // 힐은 즉시
            _hpBarDisplayedFrac[key] = displayed;
        }

        // 1) 배경 인셋 — 잉크 차콜
        FillRect(rect, new Color(0.06f, 0.05f, 0.07f, 0.88f));

        // 2) 딜레이 트레일 — 실제 hp 구간 ~ displayed 구간 사이에만 머티드 잔상
        if (displayed > realFrac)
        {
            float trailStartX = rect.x + rect.width * realFrac;
            float trailWidth = rect.width * (displayed - realFrac);
            FillRect(new Rect(trailStartX, rect.y, trailWidth, rect.height),
                     new Color(0.78f, 0.62f, 0.30f, 0.72f));
        }

        // 3) 본 HP 채움 + 그라디언트 (상단 하이라이트, 하단 섀도)
        if (realFrac > 0f)
        {
            var fillRect = new Rect(rect.x, rect.y, rect.width * realFrac, rect.height);
            FillRect(fillRect, fill);

            float hiH = Mathf.Max(1f, fillRect.height * 0.38f);
            FillRect(new Rect(fillRect.x, fillRect.y, fillRect.width, hiH),
                     new Color(0.85f, 0.45f, 0.40f, 0.28f));

            float shH = Mathf.Max(1f, fillRect.height * 0.28f);
            FillRect(new Rect(fillRect.x, fillRect.yMax - shH, fillRect.width, shH),
                     new Color(0f, 0f, 0f, 0.38f));
        }

        // 4) 저체력 펄스 — 30% 이하일 때 빨간 발광이 숨쉬듯 박동
        if (realFrac > 0f && realFrac < 0.3f)
        {
            float pulse = (Mathf.Sin(Time.time * 4.5f) + 1f) * 0.5f;
            float alpha = Mathf.Lerp(0.14f, 0.36f, pulse) * (1f - realFrac / 0.3f);
            FillRect(rect, new Color(0.85f, 0.18f, 0.20f, alpha));
        }

        // 5) 머티드 차콜 외곽 프레임 + 내부 암색 인셋 라인 — 배경(보라+석조)에 묻히도록 톤 다운
        DrawBorder(rect, 1f, new Color(0.18f, 0.14f, 0.18f, 0.92f));
        var innerRect = new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f);
        DrawBorder(innerRect, 1f, new Color(0f, 0f, 0f, 0.45f));

        // 6) 외곽선 텍스트 — 흰 글자 + 검정 외곽. 바 높이에 맞춰 폰트 축소.
        int prevFs = _centerStyle.fontSize;
        _centerStyle.fontSize = Mathf.Clamp(Mathf.RoundToInt(rect.height * 0.95f), 9, 14);
        DrawTextWithOutline(rect, $"{curr}/{max}", _centerStyle,
                            Color.white, new Color(0f, 0f, 0f, 0.95f), 1f);
        _centerStyle.fontSize = prevFs;
    }

    private void DrawFloaters()
    {
        foreach (var f in _floaters)
        {
            if (f.delay > 0) continue;
            if (!_slotPositions.TryGetValue(f.anchor, out var basePos)) continue;

            float progress = Mathf.Clamp01(f.age / DamageFloater.LifeTime);
            float alpha = 1f - progress;
            float yOffset = -70f * progress;

            var rect = new Rect(basePos.x - 60, basePos.y - 110 + yOffset, 120, 46);
            GUI.color = new Color(1f, 0.25f, 0.25f, alpha);
            GUI.Label(rect, $"-{f.amount}", _damageStyle);
            GUI.color = Color.white;
        }
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

    // Battle/Map/Village 공통 상단 HUD 스트립 + 구분선 — 호출자가 컨텍스트를 넘겨주면 그 색 사용.
    public void DrawHudStripAndDivider(HudContext ctx = HudContext.Battle)
    {
        if (!hudStripEnabled) return;

        Color bg = ctx switch
        {
            HudContext.Map     => hudStripBgColorMap,
            HudContext.Village => hudStripBgColorVillage,
            _                  => hudStripBgColorBattle,
        };
        // 컨텍스트별 최종 알파 — 색 필드의 알파는 무시하고 슬라이더 값을 직접 사용.
        bg.a = Mathf.Clamp01(ctx switch
        {
            HudContext.Map     => hudStripAlphaMap,
            HudContext.Village => hudStripAlphaVillage,
            _                  => hudStripAlphaBattle,
        });
        Texture2D divTex = ctx switch
        {
            HudContext.Map     => null, // 맵은 노란 디바이더 제거 — 검은 바만 사용
            HudContext.Village => _hudDividerTexVillage,
            _                  => _hudDividerTexBattle,
        };

        // 1) 바 배경 채우기. 한 번만 — 이중 fill은 알파 반투명을 깨뜨림.
        FillRect(new Rect(0f, 0f, RefW, hudStripHeight), bg);

        // 2) 디바이더는 마지막에 그려서 바 위로 겹치도록. Width가 0이면 오버스캔 기반 자동, >0이면 그 값 직접 사용해 가운데 정렬.
        if (divTex != null)
        {
            float divW = hudDividerWidth > 0f ? hudDividerWidth : (RefW + hudDividerOverscan * 2f);
            float divX = hudDividerWidth > 0f ? (RefW - divW) * 0.5f : -hudDividerOverscan;
            var prev = GUI.color;
            GUI.color = hudDividerTint;
            GUI.DrawTexture(
                new Rect(divX,
                         hudDividerCenterY - hudDividerHeight * 0.5f,
                         divW,
                         hudDividerHeight),
                divTex, ScaleMode.StretchToFill, alphaBlend: true);
            GUI.color = prev;
        }
        // 텍스처 없으면 아예 선 생략 — 호출 측에서 나중에 따로 붙이도록.

        // 바 하단 골드 트림 — 전투/맵 공용 (마을은 모닥불 톤 충돌로 제외). 두께 0이거나 알파 0이면 스킵.
        if ((ctx == HudContext.Battle || ctx == HudContext.Map)
            && hudBattleBottomLineThickness > 0f && hudBattleBottomLineColor.a > 0f)
        {
            FillRect(new Rect(0f, hudStripHeight - hudBattleBottomLineThickness, RefW, hudBattleBottomLineThickness),
                     hudBattleBottomLineColor);
        }
    }

    // 우측 정렬 슬롯들 (DeckView + Floor). 우→좌 순서로 그려서 cursor 계산을 단순화.
    private void DrawRightSlots(
        Rect barRect, float barY, float barH,
        float iconY, float iconSize, float iconLabelGap,
        string floorLabel, int deckCount = -1)
    {
        const float rightPad = 23.94f;    // 화면 우측 가장자리 여백 (padX보다 살짝 크게)
        const float rightSlotGap = 47.88f;// 슬롯 사이 간격 (좌측 slotGap보다 넓게)

        float right = barRect.xMax - rightPad;
        bool anyDrawn = false;

        // Floor 슬롯 (가장 오른쪽) — 계단은 아주 미세하게 좌우로 기울음
        if (floorLabel != null)
        {
            right = DrawRightSlot(right, barY, barH, iconY, iconSize, iconLabelGap,
                _iconFloor, floorLabel, new Color(1f, 0.82f, 0.35f), wobblePhase: 2.4f);
            anyDrawn = true;
        }

        // Deck View 버튼 — 계단 왼쪽. 클릭하면 덱 전체 보기 오버레이 오픈.
        if (deckCount >= 0)
        {
            if (anyDrawn) right -= rightSlotGap;
            DrawDeckViewRightSlot(right, barY, barH, iconY, iconSize, iconLabelGap, deckCount);
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
        }

        if (_iconDeck != null)
        {
            Color glowTint = hover ? new Color(1f, 0.92f, 0.60f) : new Color(0.70f, 0.88f, 1f);
            DrawIconGlow(iconRect, glowTint, hover ? 1.35f : 1f);

            float angle = Mathf.Sin(Time.time * 0.7f + 1.2f) * 0.32f;
            var prevMatrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, iconRect.center);
            GUI.DrawTexture(iconRect, _iconDeck, ScaleMode.ScaleToFit);
            GUI.matrix = prevMatrix;
        }

        GUI.Label(labelRect, label, _labelStyle);

        if (hover && ev.type == EventType.MouseDown && ev.button == 0)
        {
            _deckViewerOpen = !_deckViewerOpen;
            _deckViewerScroll = Vector2.zero;
            ev.Use();
        }

        return iconX;
    }

    // =========================================================
    // 배틀 / 맵 / 마을 공용 상단 HUD — HP/Gold/Potion/Relic + (우측) Deck/Floor.
    // 전투 중 실시간 HP를 반영하려면 hpCurrent/hpMax 오버라이드를 넘긴다.
    // 맵·마을에서는 RunState 값을 그대로 쓴다.
    // =========================================================
    public void DrawTopBar(HudContext ctx, RunState run, int currentFloor, int totalFloors,
                           int? hpCurrent = null, int? hpMax = null)
    {
        if (run == null) return;
        EnsureStyles();

        DrawHudStripAndDivider(ctx);

        const float barX = 10f;
        const float barY = 8f;
        const float barW = RefW - 20f;
        const float barH = 58.14f;
        var barRect = new Rect(barX, barY, barW, barH);

        const float iconSize = 42.75f;
        const float iconLabelGap = 5.13f;
        const float slotGap = 23.94f;
        const float padX = 17.1f;
        float iconY = barY + (barH - iconSize) * 0.5f;
        float cursorX = barX + padX;

        void DrawSlot(Texture2D tex, string label, Color glowTint, float glowIntensity = 1f)
        {
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
        }

        int hpNow = hpCurrent ?? run.playerCurrentHp;
        int hpCap = hpMax ?? run.playerMaxHp;
        DrawSlot(_iconHP,     $"{hpNow}/{hpCap}",                          new Color(1f, 0.55f, 0.50f), 1.6f);
        DrawSlot(_iconGold,   $"{run.gold}",                               new Color(1f, 0.82f, 0.35f));
        DrawSlot(_iconPotion, $"{run.potions.Count}/{RunState.MaxPotionSlots}", new Color(0.55f, 1f, 0.65f));
        DrawSlot(_iconRelic,  $"{run.relics.Count}",                       new Color(0.85f, 0.55f, 1f));

        DrawRightSlots(barRect, barY, barH, iconY, iconSize, iconLabelGap,
            $"{currentFloor}/{totalFloors}", deckCount: run.deck.Count);
    }

    // 한 슬롯을 right 기준으로 우→좌로 그리고, 이 슬롯의 left x를 반환
    // wobblePhase가 >=0 이면 미세한 좌우 기울임 적용 (양옆으로 살짝 기우는 느낌)
    private float DrawRightSlot(float right, float barY, float barH,
        float iconY, float iconSize, float iconLabelGap,
        Texture2D icon, string label, Color glowTint, float wobblePhase)
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

            // 아주 미세한 좌우 기울임 — 더 천천히 부드럽게, 폭은 더 작게
            float angle = Mathf.Sin(Time.time * 0.7f + wobblePhase) * 0.32f;
            var prevMatrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, iconRect.center);
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
            GUI.matrix = prevMatrix;
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

        var orbBodyTex = _manaOrbTexture != null ? _manaOrbTexture : _manaFrameTexture;

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
        DrawTextWithOutline(orbRect, $"{p.mana}/{p.maxMana}", _cardCostStyle,
                            Color.white, new Color(0, 0, 0, 0.95f), 1.5f);
        _cardCostStyle.fontSize = prevFontSize;

        // 좌하단 덱 더미 — 화면 좌측 최하단 모서리에 작게. 하늘색 카운트 뱃지.
        var skyBlue = new Color(0.30f, 0.65f, 1f, 1f);
        int deckDisplay = GetDeckDisplayCount(state);
        float deckPulse = GetReshuffleDeckLandPulse();
        DrawCardPile(new Rect(22f, RefH - 88f, 78f, 78f), _iconDeck, deckDisplay, skyBlue, deckPulse);

        // 우하단 버린 카드 더미 — 좌측 덱과 동일한 하늘색 뱃지.
        // 손패가 버려지는 애니메이션 중에는 착지한 카드 수만큼 카운트가 틱틱 올라가며,
        // 카드가 착지할 때마다 뱃지가 잠깐 커졌다 돌아오는 펄스가 들어간다.
        int discardDisplay = GetDiscardDisplayCount(state);
        float discardPulse = GetDiscardLandPulse();
        DrawCardPile(new Rect(RefW - 90f, RefH - 88f, 78f, 78f), _iconDiscard, discardDisplay, skyBlue, discardPulse);
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

    private void DrawHand(BattleState state)
    {
        const float cardW = 150f;
        const float cardH = 209f;

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

        // 카드 간 각도 고정 (좌우 완전 대칭)
        float anglePerCard = handAnglePerCard;
        float totalAngle = (n - 1) * anglePerCard;
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
            for (int k = n - 1; k >= 0; k--)
            {
                int i = drawOrder[k];
                if (IsBeingDrawnInto(state.hand[i])) continue;
                float angle = startAngle + i * anglePerCard;
                Vector2 center = FanCardCenter(fanOriginX, fanOriginY, fanRadius, angle);
                center.y += CardIdleBob(i);
                if (PointInRotatedRect(mouse, center, cardW, cardH, angle))
                {
                    hoverIdx = i;
                    break;
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

            float angle = startAngle + i * anglePerCard;
            Vector2 center = FanCardCenter(fanOriginX, fanOriginY, fanRadius, angle);
            center.y += CardIdleBob(i);
            var rect = new Rect(center.x - cardW * 0.5f, center.y - cardH * 0.5f, cardW, cardH);

            GUI.matrix = baseMatrix * RotateAroundPivotMatrix(angle, center);

            bool isFusionFanA = _fusionMaterialAPicked
                && _fusionMaterialA.isHand
                && _fusionMaterialA.index == i;
            if (i == _targetingCardIndex || i == _swapFromCardIndex || isFusionFanA)
            {
                DrawSoftCardGlow(rect);
            }
            DrawCardFrame(rect, c, canPlay, drawCost: false);
        }
        GUI.matrix = baseMatrix;

        // 2-b) Cost 패스 — 카드 본체가 모두 그려진 뒤 cost 원만 위에 다시 그린다.
        // 이렇게 해야 좌→우 겹침 순서에 상관없이 cost가 항상 보임.
        foreach (int i in drawOrder)
        {
            if (i == hoverIdx) continue;
            if (IsBeingDrawnInto(state.hand[i])) continue;

            var c = state.hand[i].data;
            bool canPlay = IsCardPlayable(state, c);

            float angle = startAngle + i * anglePerCard;
            Vector2 center = FanCardCenter(fanOriginX, fanOriginY, fanRadius, angle);
            center.y += CardIdleBob(i);
            var rect = new Rect(center.x - cardW * 0.5f, center.y - cardH * 0.5f, cardW, cardH);

            GUI.matrix = baseMatrix * RotateAroundPivotMatrix(angle, center);
            DrawCardCost(rect, c, canPlay);
        }
        GUI.matrix = baseMatrix;

        // 3) 호버 카드 — 회전 없이, 크게, 위로 올라옴 (맨 위에 그려져야 하므로 마지막)
        if (hoverIdx >= 0)
        {
            int i = hoverIdx;
            var c = state.hand[i].data;
            bool canPlay = IsCardPlayable(state, c);

            float angle = startAngle + i * anglePerCard;
            Vector2 fanCenter = FanCardCenter(fanOriginX, fanOriginY, fanRadius, angle);

            const float hoverScale = 1.18f;
            const float hoverBottomPad = 20f;
            float hw = cardW * hoverScale;
            float hh = cardH * hoverScale;

            // 호버 카드는 부채꼴 위치와 무관하게 화면 하단에 고정 앵커해서 전체가 항상 보이게 함.
            // x는 부채꼴 위치 유지(손 위 어느 카드인지 직관적으로 보이게), y만 화면 하단 기준.
            // 숨김 진행도에 따라 함께 아래로 슬라이드.
            var hoverRect = new Rect(fanCenter.x - hw * 0.5f, RefH - hh - hoverBottomPad + hideOffset, hw, hh);

            bool isFusionHoverA = _fusionMaterialAPicked
                && _fusionMaterialA.isHand
                && _fusionMaterialA.index == i;
            if (i == _targetingCardIndex || i == _swapFromCardIndex || isFusionHoverA)
            {
                DrawSoftCardGlow(hoverRect);
            }
            DrawCardFrame(hoverRect, c, canPlay, drawCost: true);

            // 융합 모드에서 손 카드 클릭 — 재료 선택으로 가로챔 (canPlay 무관).
            bool fusionMode = _targetingCardIndex >= 0
                && _targetingCardIndex < state.hand.Count
                && CardNeedsFusionTargets(state.hand[_targetingCardIndex].data);
            if (fusionMode)
            {
                var ev2 = Event.current;
                if (ev2.type == EventType.MouseDown && ev2.button == 0 && hoverRect.Contains(ev2.mousePosition))
                {
                    ev2.Use();
                    if (i == _targetingCardIndex)
                    {
                        // 촉매 카드 재클릭 → 융합 모드 취소
                        _targetingCardIndex = -1;
                        _fusionMaterialAPicked = false;
                    }
                    else if (IsFusionMaterialEligible(null, i, isHand: true))
                    {
                        HandleFusionMaterialClick(DianoCard.Battle.FusionMaterial.Hand(i));
                    }
                    return; // 융합 모드에서는 일반 클릭 처리로 내려가지 않음
                }
            }

            // 클릭 처리: 호버된 카드에서만
            if (canPlay)
            {
                var ev = Event.current;
                if (ev.type == EventType.MouseDown && ev.button == 0 && hoverRect.Contains(ev.mousePosition))
                {
                    ev.Use();
                    int captured = i;
                    bool isSummon = c.cardType == CardType.SUMMON;
                    bool fieldFull = _battle.state.field.Count >= _battle.state.maxFieldSize;

                    if (CardNeedsTarget(c))
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
                        _pending.Add(() => {
                            _battle.PlayCard(captured, -1);
                            if (isAttack)
                            {
                                _playerView?.PlayAttack(ComputeAttackDir(-1));
                                TriggerPlayerAttackFx(-1);
                            }
                            else if (isSummon)
                                _playerView?.PlaySummon(ComputeAttackDir(-1));
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

    // 손패 숨김/표시 토글 버튼 — "서랍 손잡이" 스타일의 반투명 pill 탭.
    // 카드 상단 경계(Y≈555) 바로 위에 앉아 카드를 아래로 당겨 내리는 손잡이처럼 보이게.
    // 카드가 슬라이드해서 내려갈 때 같은 hideOffset만큼 함께 따라 내려감.
    // 어두운 반투명 fill + 금색 얇은 테두리 + 작은 쉐브론(▽/△). 호버 시 살짝 밝아짐.
    private void DrawHandHideToggle()
    {
        // 카드 드로우/리셔플 애니메이션 중엔 탭 숨김 — 손패가 재배치되는 중이라 탭이 떠있으면 어색함
        if (IsDrawFlyActive || IsReshuffleActive) return;

        const float w = 76f;
        const float h = 20f;
        // DrawHand와 동일한 ease 커브 적용 — 탭이 카드와 같은 속도·곡선으로 슬라이드
        float hideOffset = EaseInOutCubic(_handHideProgress) * HandHideDistance;
        var rect = new Rect(RefW * 0.5f - w * 0.5f, 540f + hideOffset, w, h);

        var ev = Event.current;
        bool hover = rect.Contains(ev.mousePosition);

        // 호버 시 탭이 살짝 위로 들리는 리프트 효과
        if (hover) rect.y -= 2f;

        // 부드러운 호흡 펄스 — 사라졌다 돌아오는 느낌이지만 완전히 사라지진 않음.
        // 1.3Hz sine으로 pulse(0..1) 계산, 알파를 baseMin ↔ baseMax 사이에서 왕복.
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 1.3f);

        // 배경 알파 — 기존보다 훨씬 옅게, 호흡으로 0.18 ↔ 0.42 사이 왕복. 호버 시 고정 0.55.
        float fillA = hover ? 0.55f : Mathf.Lerp(0.18f, 0.42f, pulse);
        FillRect(rect, new Color(0.08f, 0.05f, 0.05f, fillA));

        // 금색 얇은 테두리 — 호흡으로 0.25 ↔ 0.60 사이. 호버 시 밝게 고정.
        float borderA = hover ? 1f : Mathf.Lerp(0.25f, 0.60f, pulse);
        Color goldBorder = hover
            ? new Color(0.98f, 0.82f, 0.42f, 1f)
            : new Color(0.86f, 0.66f, 0.28f, borderA);
        DrawBorder(rect, 1f, goldBorder);

        // 호버 시 외곽 금색 글로우 (탭 자체가 옅어서 호버 피드백은 글로우로 보강)
        if (hover)
        {
            for (int i = 0; i < 3; i++)
            {
                float pad = (i + 1) * 2f;
                float ga = 0.10f * (1f - i / 3f);
                FillRect(new Rect(rect.x - pad, rect.y - pad, rect.width + pad * 2f, rect.height + pad * 2f),
                         new Color(0.86f, 0.66f, 0.28f, ga));
            }
        }

        // 쉐브론 — 숨김 상태면 위로(펼치기), 표시 상태면 아래로(숨기기). 텍스트도 함께 호흡.
        string label = _handHidden ? "▲" : "▼";
        int prevFontSize = _centerStyle.fontSize;
        Color prevColor = _centerStyle.normal.textColor;
        _centerStyle.fontSize = 13;
        float textA = hover ? 1f : Mathf.Lerp(0.40f, 0.80f, pulse);
        _centerStyle.normal.textColor = hover
            ? new Color(1f, 0.92f, 0.68f, 1f)
            : new Color(0.94f, 0.86f, 0.58f, textA);
        GUI.Label(rect, label, _centerStyle);
        _centerStyle.fontSize = prevFontSize;
        _centerStyle.normal.textColor = prevColor;

        // 클릭 처리
        if (hover && ev.type == EventType.MouseDown && ev.button == 0)
        {
            ev.Use();
            _handHidden = !_handHidden;
        }
    }

    private bool IsCardPlayable(BattleState state, CardData c)
    {
        if (state.IsOver || _endTurnAnimating || IsDrawFlyActive) return false;
        if (state.player.mana < c.cost) return false;
        // SUMMON은 슬롯 꽉 차도 교체 모드로 플레이 가능하므로 별도 필드 체크 없음.
        // ALLY 타겟 카드(수호 마법) / ALL_ALLY 방어는 필드에 공룡 없으면 플레이 불가.
        if (CardNeedsAllyTarget(c) && state.field.Count == 0) return false;
        if (c.cardType == CardType.MAGIC && c.subType == CardSubType.DEFENSE
            && c.target == TargetType.ALL_ALLY && state.field.Count == 0) return false;
        // 융합 카드: 필드 + 손 조합에 같은 종·같은 티어 육식이 최소 2마리 있어야 재료 확보 가능.
        if (CardNeedsFusionTargets(c) && !HasAnyFusionPair(state)) return false;
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
        foreach (var n in counts.Values) if (n >= 2) return true;
        return false;
    }

    private static Vector2 FanCardCenter(float originX, float originY, float radius, float angleDeg)
    {
        float rad = angleDeg * Mathf.Deg2Rad;
        return new Vector2(originX + Mathf.Sin(rad) * radius,
                           originY - Mathf.Cos(rad) * radius);
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
    /// StS-style 카드 프레임 v4 (2026-04-23).
    /// 흰색 PNG + 코드 tint 방식. 사용자 지정 레이어 순서:
    ///   1) CardDescPanel → 2) CardArtPlate → 3) Art → 4) CardFrameBase →
    ///   5) CardBg(동색 외곽 프레임) → 6) CardTrim(등급 색) → 7) CardTypeLabel →
    ///   8) CardRibbon → 9) CostGem (맨 위).
    /// 등급은 트림 색 + 카드명 텍스트 색으로만 표현 — RARE 글로우/파티클 없음.
    /// </summary>
    private void DrawCardFrame(Rect rect, CardData c, bool canPlay, bool drawCost, bool slotOnly = false)
    {
        var prevColor = GUI.color;
        Color dim = canPlay ? Color.white : cardDisabledDim;

        // 1) CardDescPanel — 하단 설명 패널 (맨 뒤, 볼더 없음).
        DrawLayerWithBorder(_cardDescPanelTexture, RectFromPct(rect, cardDescPanelRectPct), cardDescPanelTint, dim, border: null);

        // 2) CardArtPlate — 상단 아트 패널 배경 (볼더 없음).
        DrawLayerWithBorder(_cardArtPlateTexture, RectFromPct(rect, cardArtPlateRectPct), cardArtPlateTint, dim, border: null);

        // 3) 아트 — ArtPlate 위에 얹는다. slotOnly 모드에선 생략.
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
                FillRect(artRect, (c != null ? GetCardRibbonTint(c) : Color.white) * cardArtPlaceholderTint);
            }
        }
        else
        {
            FillRect(artRect, cardArtPlaceholderTint);
        }

        // 4) CardFrameBase — 안쪽 플레이트. 타입별 색 자동 적용 (toggle 로 끌 수 있음).
        Color baseTint = (frameUseTypeColor && c != null && !slotOnly) ? GetFrameColorByType(c) : cardBaseTint;
        DrawLayerWithBorder(_cardFrameBaseTexture, RectFromPct(rect, cardBaseRectPct), baseTint, dim, borderFrameBase);

        // 5) CardBg — 외곽 프레임. 카퍼/브론즈 고정 (전 카드 공통).
        DrawLayerWithBorder(_cardBgTexture, RectFromPct(rect, cardBgRectPct), cardBgTint, dim, borderCardBg);

        // 5b) CardBorder — 최외곽 보더 오버레이 (선택, Inspector에서 enable).
        if (cardBorderEnabled)
        {
            DrawLayerWithBorder(_cardBorderTexture, RectFromPct(rect, cardBorderRectPct), cardBorderTint, dim, border: null);
        }

        // 6) 트림 (CardTrim) — 등급별 색 tint × trimTintMul.
        if (_cardTrimTexture != null)
        {
            Color trimBase = (c != null && !slotOnly) ? GetRarityTrimColor(c.rarity) : slotDefaultTrimTint;
            Color trimTint = MultColor(trimBase, cardTrimTintMul);
            DrawLayerWithBorder(_cardTrimTexture, RectFromPct(rect, cardTrimRectPct), trimTint, dim, borderTrim);
        }

        // 7) 하단 타입 라벨 pill (CardTypeLabel).
        DrawLayerWithBorder(_cardTypeLabelTexture, RectFromPct(rect, cardTypeLabelPillRectPct), cardTypeLabelTint, dim, borderTypeLabel);

        // 8) 상단 리본 (CardRibbon) — 오버라이드 색 또는 타입 색.
        if (_cardRibbonTexture != null)
        {
            Color ribbonTint;
            if (cardRibbonUseOverride)
            {
                ribbonTint = cardRibbonOverrideColor;
            }
            else
            {
                Color ribbonBase = (c != null && !slotOnly) ? GetCardRibbonTint(c) : slotDefaultRibbonTint;
                ribbonTint = MultColor(ribbonBase, cardRibbonTintMul);
            }
            DrawLayerWithBorder(_cardRibbonTexture, RectFromPct(rect, cardRibbonRectPct), ribbonTint, dim, borderRibbon);
        }

        GUI.color = prevColor;

        if (slotOnly)
        {
            // 슬롯 프리뷰: 카드 데이터 텍스트/코스트는 생략. Inspector에서 rect/tint 만 관찰.
            return;
        }

        // 9) 코스트 젬 (맨 위) — 좌상단.
        if (drawCost && c != null)
        {
            DrawCardCost(rect, c, canPlay);
        }

        if (c == null) return;

        // 텍스트 — 리본 위 카드명 (등급 색 × cardNameTextTint)
        var nameRect = RectFromPct(rect, cardNameOnRibbonRectPct);
        int prevNameSize = _cardNameStyle.fontSize;
        Color prevNameCol = _cardNameStyle.normal.textColor;
        _cardNameStyle.fontSize = drawCost ? cardNameFontSize : cardNameFontSizeSmall;
        Color nameCol = canPlay
            ? MultColor(GetRarityTextColor(c.rarity), cardNameTextTint)
            : cardNameDisabledColor;
        DrawTextWithOutline(nameRect, GetCardTypeLabel(c), _cardNameStyle, nameCol, cardNameOutline, cardNameOutlineThickness);
        _cardNameStyle.fontSize = prevNameSize;
        _cardNameStyle.normal.textColor = prevNameCol;

        // 카테고리 라벨 — 하단 pill 안 (소환/마법/버프/유틸). 텍스트 rect 는 배경과 독립.
        var typeRect = RectFromPct(rect, cardTypeTextRectPct);
        int prevTypeSize = _cardTypeStyle.fontSize;
        Color prevTypeCol = _cardTypeStyle.normal.textColor;
        _cardTypeStyle.fontSize = cardTypeFontSize;
        _cardTypeStyle.normal.textColor = canPlay ? cardTypeTextColor : cardNameDisabledColor;
        GUI.Label(typeRect, GetCardCategoryLabelKr(c), _cardTypeStyle);
        _cardTypeStyle.fontSize = prevTypeSize;
        _cardTypeStyle.normal.textColor = prevTypeCol;

        // 본문 — 하단 패널 (ATK/HP 또는 짧은 설명)
        int prevBodySize = _cardDescStyle.fontSize;
        Color prevBodyCol = _cardDescStyle.normal.textColor;
        _cardDescStyle.fontSize = cardBodyFontSize;
        _cardDescStyle.normal.textColor = canPlay ? cardBodyTextColor : cardNameDisabledColor;
        GUI.Label(RectFromPct(rect, cardBodyV2RectPct), GetCardBody(c), _cardDescStyle);
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
    // 등급 인자는 c.rarity를 무시하고 강제 적용 (UI 토글로 등급 비교).
    // slotOnly=true 이면 카드 데이터 생략 — 빈 슬롯(프레임 레이어)만 그려서 rect 튜닝용.
    public void DrawCardPreview(Rect rect, CardData c, Rarity? rarityOverride = null, bool slotOnly = false)
    {
        EnsureStyles();
        if (slotOnly)
        {
            DrawCardFrame(rect, null, canPlay: true, drawCost: false, slotOnly: true);
            return;
        }
        if (rarityOverride.HasValue && c != null)
        {
            var clone = new CardData
            {
                id = c.id, nameKr = c.nameKr, nameEn = c.nameEn,
                cardType = c.cardType, subType = c.subType,
                rarity = rarityOverride.Value,
                cost = c.cost, attack = c.attack, hp = c.hp, value = c.value,
                target = c.target, description = c.description,
                image = c.image, chapter = c.chapter,
            };
            DrawCardFrame(rect, clone, canPlay: true, drawCost: true);
        }
        else
        {
            DrawCardFrame(rect, c, canPlay: true, drawCost: true);
        }
    }

    private void DrawCardCost(Rect rect, CardData c, bool canPlay)
    {
        var prevColor = GUI.color;

        // 코스트 젬 위치 — Inspector의 cardCostOrbPct (centerX, centerY, sizeFrac).
        float orbSize = rect.width * cardCostOrbPct.z;
        float orbCx = rect.x + rect.width  * cardCostOrbPct.x;
        float orbCy = rect.y + rect.height * cardCostOrbPct.y;
        var orbRect = new Rect(orbCx - orbSize * 0.5f, orbCy - orbSize * 0.5f, orbSize, orbSize);

        Color dim = canPlay ? Color.white : cardDisabledDimGem;

        // ManaOrb(완성형 디자인 에셋) 있으면 단일 레이어로 그려 좌하단 마나 오브와 톤 통일.
        // 없으면 기존 2레이어 CostGem 폴백.
        if (_manaOrbTexture != null)
        {
            var prev = GUI.color;
            GUI.color = dim;
            GUI.DrawTexture(orbRect, _manaOrbTexture, ScaleMode.StretchToFill, alphaBlend: true);
            GUI.color = prev;
        }
        else
        {
            // 1) 안쪽 디스크 먼저 (링 뒤에 깔림).
            if (_cardCostGemInnerTexture != null)
            {
                float shrink = orbSize * cardCostGemInnerShrinkPct;
                var innerRect = new Rect(
                    orbRect.x + shrink * 0.5f,
                    orbRect.y + shrink * 0.5f,
                    orbRect.width - shrink,
                    orbRect.height - shrink);
                DrawLayerWithBorder(_cardCostGemInnerTexture, innerRect, cardCostGemInnerTint, dim, borderCostGemInner);
            }

            // 2) 외곽 링을 위에 덮음.
            DrawLayerWithBorder(_cardCostGemTexture, orbRect, cardCostGemTint, dim, borderCostGem);
        }

        // 숫자: Inspector 색 + 외곽선. 텍스트 rect 는 orb 기준 오프셋/축소 반영.
        Color textCol = canPlay ? cardCostTextColor : cardCostDisabledColor;
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
        DrawTextWithOutline(costTextRect, c.cost.ToString(), _cardCostStyle, textCol, cardCostOutline, cardCostOutlineThickness);
        _cardCostStyle.fontSize = prevFontSize;

        GUI.color = prevColor;
    }

    private static void DrawTextWithOutline(Rect rect, string text, GUIStyle style,
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

    // 카드 타입별 프레임 색 — Inspector frameColor* 필드 사용.
    private Color GetFrameColorByType(CardData c)
    {
        if (c == null) return frameColorDefault;
        return c.cardType switch
        {
            CardType.SUMMON => frameColorSummon,
            CardType.MAGIC => frameColorMagic,
            CardType.BUFF => frameColorBuff,
            CardType.UTILITY => frameColorUtility,
            CardType.RITUAL => frameColorRitual,
            _ => frameColorDefault,
        };
    }

    // 리본 색 — 카드 타입별 (2026-04-24 제팬 다크판타지 팔레트).
    // 타입 색이 드러나는 유일한 존 = 리본. 프레임/아트플레이트는 다크 잉크 고정.
    private static Color GetCardRibbonTint(CardData c)
    {
        return c.cardType switch
        {
            CardType.SUMMON => new Color(0.40f, 0.18f, 0.22f),   // 와인/버건디
            CardType.MAGIC => new Color(0.22f, 0.24f, 0.46f),    // 딥 인디고
            CardType.BUFF => new Color(0.18f, 0.36f, 0.32f),     // 에이지드 제이드
            CardType.UTILITY => new Color(0.26f, 0.26f, 0.32f),  // 거넛메탈
            CardType.RITUAL => new Color(0.50f, 0.30f, 0.38f),   // 머티드 로즈
            _ => new Color(0.32f, 0.32f, 0.36f),
        };
    }

    // 등급 트림 색 — 동/은/금. SHOP은 RARE와 동일 처리.
    private static Color GetRarityTrimColor(Rarity r)
    {
        return r switch
        {
            Rarity.COMMON => new Color(0.71f, 0.43f, 0.20f),    // bronze
            Rarity.UNCOMMON => new Color(0.83f, 0.85f, 0.87f),  // silver
            Rarity.RARE => new Color(0.95f, 0.78f, 0.32f),      // gold
            Rarity.SHOP => new Color(0.95f, 0.78f, 0.32f),
            _ => Color.white,
        };
    }

    // 등급별 카드명 텍스트 색 — 트림 규칙 동기화.
    private static Color GetRarityTextColor(Rarity r)
    {
        return r switch
        {
            Rarity.COMMON => Color.white,
            Rarity.UNCOMMON => new Color(0.85f, 0.92f, 0.98f),  // pale silver-blue
            Rarity.RARE => new Color(1.0f, 0.86f, 0.42f),       // warm gold
            Rarity.SHOP => new Color(1.0f, 0.86f, 0.42f),
            _ => Color.white,
        };
    }

    // 한국어 카테고리 라벨 (하단 pill).
    private static string GetCardCategoryLabelKr(CardData c)
    {
        return c.cardType switch
        {
            CardType.SUMMON => "소환",
            CardType.MAGIC => "마법",
            CardType.BUFF => "버프",
            CardType.UTILITY => "유틸",
            CardType.RITUAL => "의식",
            _ => "",
        };
    }

    private static Color GetCardTypeTint(CardData c)
    {
        return c.cardType switch
        {
            CardType.SUMMON => new Color(0.5f, 0.8f, 0.4f),
            CardType.MAGIC when c.subType == CardSubType.ATTACK => new Color(0.9f, 0.4f, 0.3f),
            CardType.MAGIC => new Color(0.4f, 0.6f, 0.9f),
            CardType.BUFF => new Color(0.95f, 0.8f, 0.3f),
            CardType.UTILITY => new Color(0.7f, 0.7f, 0.7f),
            CardType.RITUAL => new Color(0.7f, 0.3f, 0.7f),
            _ => new Color(0.6f, 0.6f, 0.6f),
        };
    }

    // 카드 상단(이름 슬롯): 카테고리만
    private static string GetCardCategoryLabel(CardData c)
    {
        return c.cardType switch
        {
            CardType.SUMMON => "Summon",
            CardType.MAGIC => "Spell",
            CardType.BUFF => "Buff",
            CardType.UTILITY => "Utility",
            CardType.RITUAL => "Ritual",
            _ => "",
        };
    }

    // 카드 중앙(타입 라벨): 마법은 Attack/Defense, 그 외는 카드 고유 이름
    private static string GetCardTypeLabel(CardData c)
    {
        if (c.cardType == CardType.MAGIC)
            return c.subType == CardSubType.ATTACK ? "Attack" : "Defense";
        return c.nameEn;
    }

    private static string GetCardBody(CardData c)
    {
        if (c.cardType == CardType.SUMMON)
            return $"ATK {c.attack}\nHP {c.hp}";
        return ShortDesc(c);
    }

    private static string ShortDesc(CardData c)
    {
        if (string.IsNullOrEmpty(c.description)) return "";
        return c.description.Length > 60
            ? c.description.Substring(0, 60) + "…"
            : c.description;
    }

    private void DrawEndTurn(BattleState state)
    {
        GUI.enabled = !state.IsOver && !_endTurnAnimating && !IsDrawFlyActive;

        // 베이스 사이즈(살짝 작아짐) + 호버 시 확대
        var baseRect = new Rect(RefW - 280f, RefH - 80f, 150f, 72f);
        bool hovered = GUI.enabled && baseRect.Contains(Event.current.mousePosition);

        // 호버 스케일 — 즉각적인 펌프 느낌을 위해 약간 보간 (Repaint에서만 누적)
        float targetScale = hovered ? 1.12f : 1.0f;
        if (Event.current.type == EventType.Repaint)
            _endTurnHoverScale = Mathf.Lerp(_endTurnHoverScale, targetScale, Time.unscaledDeltaTime * 14f);

        float w = baseRect.width * _endTurnHoverScale;
        float h = baseRect.height * _endTurnHoverScale;
        var rect = new Rect(baseRect.center.x - w * 0.5f, baseRect.center.y - h * 0.5f, w, h);

        if (_endTurnButtonTex != null)
        {
            var prev = GUI.color;

            // 황금빛 외곽 글로우 — 버튼 텍스처를 확대해 깔고 골드 틴트로 펄스, 호버 시 더 강하게
            float slow = (Mathf.Sin(Time.time * 1.6f) + 1f) * 0.5f;
            float pulse = Mathf.Lerp(0.75f, 1.05f, slow);
            Color goldTint = new Color(1.0f, 0.82f, 0.35f);

            const int glowLayers = 6;
            const float glowMinScale = 1.04f;
            float glowMaxScale = hovered ? 1.46f : 1.32f;
            float glowBaseAlpha = hovered ? 0.48f : 0.32f;

            float cx = rect.center.x;
            float cy = rect.center.y;

            for (int i = 0; i < glowLayers; i++)
            {
                float t = i / (float)(glowLayers - 1);
                float scale = Mathf.Lerp(glowMinScale, glowMaxScale, t) + 0.025f * slow * t;
                float alpha = glowBaseAlpha * (1f - t) * (1f - t) * pulse;
                if (!GUI.enabled) alpha *= 0.35f;
                float gw = rect.width * scale;
                float gh = rect.height * scale;
                var gr = new Rect(cx - gw * 0.5f, cy - gh * 0.5f, gw, gh);
                GUI.color = new Color(goldTint.r, goldTint.g, goldTint.b, alpha);
                GUI.DrawTexture(gr, _endTurnButtonTex, ScaleMode.ScaleToFit, alphaBlend: true);
            }

            GUI.color = GUI.enabled ? Color.white : new Color(1f, 1f, 1f, 0.5f);
            GUI.DrawTexture(rect, _endTurnButtonTex, ScaleMode.ScaleToFit, alphaBlend: true);
            GUI.color = prev;

            if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
            {
                _targetingCardIndex = -1;
                _swapFromCardIndex = -1;
                _pending.Add(() => StartCoroutine(EndTurnCoroutine()));
            }
        }
        else if (GUI.Button(rect, "END\nTURN", _buttonStyle))
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

        // 적이 차례대로 행동 — 공격 계열만 lunge 애니메이션.
        var enemies = new List<EnemyInstance>(state.enemies);
        foreach (var e in enemies)
        {
            if (e.IsDead) continue;
            if (state.PlayerLost) break;

            if (IsAttackAction(e.intentAction))
                yield return AnimateEnemyAttack(e);
            _battle.DoEnemyAction(e);
            yield return new WaitForSeconds(BetweenAttacksPause);
        }

        if (state.PlayerLost)
        {
            _endTurnAnimating = false;
            _attackingUnit = null;
            yield break;
        }

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

    /// <summary>
    /// 적의 공격 애니메이션 — BattleEntityView가 있으면 world-space PlayAttack,
    /// 없으면 IMGUI lunge 폴백.
    /// </summary>
    private IEnumerator AnimateEnemyAttack(EnemyInstance e)
    {
        if (_enemyViews.TryGetValue(e, out var view) && view != null)
        {
            view.PlayAttack(Vector3.left);
            yield return new WaitForSeconds(0.55f);
        }
        else
        {
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

        const float cardW = 150f;
        const float cardH = 209f;

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

        const float cardW = 150f;
        const float cardH = 209f;

        // 버린 더미 중심 (DrawTopBar의 Rect와 일치)
        Vector2 pileTarget = new Vector2(RefW - 90f + 39f, RefH - 88f + 39f);

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

            DrawCardFrame(rect, fc.data, canPlay: true, drawCost: false);
        }
        GUI.matrix = baseMatrix;
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
        const float cardH = 209f;
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

        const float cardW = 150f;
        const float cardH = 209f;

        // 덱 더미 중심 (DrawTopBar의 Rect와 일치: (22, RefH-88, 78, 78))
        Vector2 deckCenter = new Vector2(22f + 39f, RefH - 88f + 39f);
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
                DrawCardFrame(rect, fc.data, canPlay: true, drawCost: false);
            }
            else if (_iconCardBack != null)
            {
                GUI.DrawTexture(rect, _iconCardBack, ScaleMode.StretchToFill, alphaBlend: true);
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
        if (_iconCardBack == null) return;  // 뒷면 텍스처 없으면 조용히 스킵

        // 양쪽 더미 중심 (DrawTopBar Rect와 일치)
        Vector2 discardCenter = new Vector2(RefW - 90f + 39f, RefH - 88f + 39f);  // (1229, 671)
        Vector2 deckCenter    = new Vector2(22f + 39f,        RefH - 88f + 39f);  // (61, 671)
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

            GUI.DrawTexture(rect, _iconCardBack, ScaleMode.StretchToFill, alphaBlend: true);
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
    // 덱 뷰어 오버레이 — 상단 바 계단 왼쪽 버튼을 누르면 뜨는 전체 덱 보기 팝업
    // =========================================================

    // Map/Village 화면에서도 덱 뷰어를 띄울 수 있도록 public. 내부적으로 _deckViewerOpen 체크.
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

        // 1) 화면 전체 어둡게 — 뒤 UI를 가리고 클릭 이벤트도 흡수
        FillRect(new Rect(0f, 0f, RefW, RefH), new Color(0f, 0f, 0f, 0.72f));

        // 2) 패널 — 가운데 배치
        const float panelW = 1060f;
        const float panelH = 600f;
        var panelRect = new Rect((RefW - panelW) * 0.5f, (RefH - panelH) * 0.5f, panelW, panelH);
        FillRect(panelRect, new Color(0.08f, 0.05f, 0.05f, 0.97f));
        DrawBorder(panelRect, 2f, new Color(0.70f, 0.55f, 0.28f, 1f));

        // 3) 제목
        int prevLabelFS = _labelStyle.fontSize;
        _labelStyle.fontSize = 24;
        var titleRect = new Rect(panelRect.x + 28f, panelRect.y + 12f, panelRect.width - 120f, 34f);
        GUI.Label(titleRect, $"덱 · {run.deck.Count}장", _labelStyle);
        _labelStyle.fontSize = prevLabelFS;

        // 4) Close 버튼 (우상단)
        var closeRect = new Rect(panelRect.xMax - 44f, panelRect.y + 10f, 34f, 34f);
        bool closeHover = closeRect.Contains(ev.mousePosition);
        FillRect(closeRect, closeHover
            ? new Color(0.55f, 0.18f, 0.18f, 1f)
            : new Color(0.18f, 0.12f, 0.10f, 0.90f));
        DrawBorder(closeRect, 1f, new Color(0.70f, 0.55f, 0.28f, 0.9f));
        int prevCenterFS = _centerStyle.fontSize;
        var prevCenterC = _centerStyle.normal.textColor;
        _centerStyle.fontSize = 20;
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

        // 5) 정렬 탭 — 획득순 / 유형 / 비용 / 이름순
        string[] tabs = { "획득순", "유형", "비용", "이름순" };
        const float tabW = 104f;
        const float tabH = 32f;
        const float tabGap = 6f;
        float tabsY = panelRect.y + 54f;
        float tabsStartX = panelRect.x + 28f;

        for (int i = 0; i < tabs.Length; i++)
        {
            var tabRect = new Rect(tabsStartX + i * (tabW + tabGap), tabsY, tabW, tabH);
            bool active = _deckViewerSortMode == i;
            bool tabHover = tabRect.Contains(ev.mousePosition);

            Color bg = active
                ? new Color(0.55f, 0.40f, 0.20f, 1f)
                : (tabHover ? new Color(0.26f, 0.20f, 0.14f, 1f) : new Color(0.15f, 0.12f, 0.09f, 0.9f));
            FillRect(tabRect, bg);
            DrawBorder(tabRect, 1f, active
                ? new Color(1f, 0.82f, 0.35f, 1f)
                : new Color(0.55f, 0.42f, 0.22f, 0.7f));

            int prevTabFS = _centerStyle.fontSize;
            var prevTabC = _centerStyle.normal.textColor;
            _centerStyle.fontSize = 14;
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

        // 6) 카드 그룹핑 — id 기준 중복 묶음 + 정렬
        var grouped = new List<(CardData data, int count, int firstIndex)>();
        var indexMap = new Dictionary<string, int>();
        for (int i = 0; i < run.deck.Count; i++)
        {
            var c = run.deck[i];
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
            case 3:  // 이름순
                grouped.Sort((a, b) =>
                    string.Compare(a.data.nameKr, b.data.nameKr, StringComparison.CurrentCulture));
                break;
            default:  // 획득순 — run.deck 등장 순서 유지
                grouped.Sort((a, b) => a.firstIndex.CompareTo(b.firstIndex));
                break;
        }

        // 7) 카드 그리드 (스크롤)
        const int cols = 6;
        const float gridPadX = 28f;
        const float cellGap = 12f;
        float gridTop = tabsY + tabH + 14f;
        float gridBottom = panelRect.yMax - 18f;
        float viewH = gridBottom - gridTop;
        float gridW = panelRect.width - gridPadX * 2f;
        float cardW = (gridW - cellGap * (cols - 1)) / cols;
        float cardH = cardW * 1.45f;

        int rows = (grouped.Count + cols - 1) / cols;
        float contentH = Mathf.Max(viewH, rows * (cardH + cellGap) - cellGap + 4f);

        var viewportRect = new Rect(panelRect.x + gridPadX, gridTop, gridW, viewH);
        var contentRect = new Rect(0f, 0f,
            gridW - (contentH > viewH ? 16f : 0f),
            contentH);

        // 스크롤 영역 밖 라이트 박스
        FillRect(viewportRect, new Color(0.04f, 0.03f, 0.03f, 0.55f));

        _deckViewerScroll = GUI.BeginScrollView(viewportRect, _deckViewerScroll, contentRect);
        float innerW = contentRect.width;
        float innerCardW = (innerW - cellGap * (cols - 1)) / cols;
        float innerCardH = innerCardW * 1.45f;
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

            // 중복 카운트 뱃지 — 우상단. CardCountBadge 텍스처 (왼쪽 V-notch) 위에 숫자 얹음.
            if (grouped[i].count > 1)
            {
                float badgeW = innerCardW * 0.34f;
                float badgeH = badgeW * 0.42f;  // 2.4:1 비율에 맞춤
                var badgeRect = new Rect(
                    cardRect.xMax - badgeW - 2f,
                    cardRect.y + 4f,
                    badgeW, badgeH);

                if (_cardCountBadgeTexture != null)
                {
                    GUI.DrawTexture(badgeRect, _cardCountBadgeTexture, ScaleMode.StretchToFill, alphaBlend: true);
                }
                else
                {
                    FillRect(badgeRect, new Color(0.10f, 0.07f, 0.05f, 0.92f));
                    DrawBorder(badgeRect, 1f, new Color(1f, 0.82f, 0.35f, 1f));
                }

                // 텍스트는 V-notch를 피해 오른쪽으로 살짝 밀어 채워진 영역 중앙에 위치.
                var textRect = new Rect(
                    badgeRect.x + badgeRect.width * 0.12f,
                    badgeRect.y,
                    badgeRect.width * 0.88f,
                    badgeRect.height);
                int prevBadgeFS = _cardCostStyle.fontSize;
                _cardCostStyle.fontSize = Mathf.RoundToInt(badgeRect.height * 0.68f);
                DrawTextWithOutline(textRect, $"×{grouped[i].count}", _cardCostStyle,
                    new Color(1f, 0.95f, 0.60f),
                    new Color(0f, 0f, 0f, 0.85f), 1f);
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
    // 저수준 사각형 그리기 유틸
    // =========================================================

    private static void FillRect(Rect rect, Color color)
    {
        var prev = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = prev;
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
