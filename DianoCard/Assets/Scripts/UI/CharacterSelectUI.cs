using System;
using System.Collections.Generic;
using System.Linq;
using DianoCard.Data;
using DianoCard.Game;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 캐릭터 선택 화면. GameState == CharacterSelect 일 때만 그려짐.
///
/// 에셋 폴더 구조:
///   Assets/Resources/
///     CharSelect/
///       Background/   ← 선택 화면 배경 이미지 (CharSelect_Background.png)
///       UI/           ← 프레임 / 버튼 / 카드 슬롯 등 UI 요소
///         Frame_InfoPanel.png
///         CardSlot_Selected.png
///         CardSlot_Locked.png
///         Button_Back.png
///         Button_Confirm.png
///     Character_select/   ← 캐릭터 선택창의 카드에 표시될 초상
///       Char_Archaeologist_Card.png
///     Character_infield/  ← 인게임 배틀 필드에 서있는 전신 일러스트
///       Char_Archaeologist_Field.png
///
/// 배경은 CharSelect/Background/CharSelect_Background.png 가 우선이고,
/// 없으면 Lobby/Main_Background.png 로 폴백.
///
/// 카드 클릭은 무시(선택은 이미 고정), 확정은 우하단 ✓ 버튼으로만.
/// </summary>
public class CharacterSelectUI : MonoBehaviour
{
    private const float RefW = 1280f;
    private const float RefH = 720f;

    // 선택 가능한 캐릭터 ID 목록 — 슬롯 순서대로. 나머지 슬롯은 "Coming Soon" 잠김 상태.
    private static readonly string[] AvailableCharacterIds = new[] { "CH002" };
    private string _selectedCharacterId = "CH002";

    // ----- Inspector 노출 파라미터 -----
    [Header("Card Frame")]
    [SerializeField, Range(0.5f, 1.6f), Tooltip("카드 프레임(CardFrameBase) 크기 배율. 1.0 = 슬롯과 동일.")]
    private float _frameScale = 1.06f;

    [SerializeField, Tooltip("프레임 황금 틴트 색상.")]
    private Color _frameTint = new Color(0.62f, 0.52f, 0.32f, 1f);

    [SerializeField, Tooltip("프레임 외곽 볼더(검정 아웃라인) 색상.")]
    private Color _frameOutlineColor = new Color(0f, 0f, 0f, 1f);

    [SerializeField, Range(0f, 8f), Tooltip("프레임 외곽 볼더 두께 (px). 0이면 비활성.")]
    private float _frameOutlineThickness = 2.5f;

    [SerializeField, Tooltip("비어있는 슬롯 내부 채움 색상.")]
    private Color _emptySlotFill = new Color(0.35f, 0.35f, 0.38f, 1f);

    [Header("Character Overlay")]
    [SerializeField, Range(0.4f, 1.2f), Tooltip("캐릭터 높이 (화면 높이 대비 비율). 1.0 = 화면 높이 가득.")]
    private float _characterHeightRatio = 0.75f;

    [SerializeField, Range(0f, 1f), Tooltip("캐릭터 가로 중심 위치 (0=왼쪽, 0.5=중앙, 1=오른쪽).")]
    private float _characterAnchorX = 0.78f;

    [SerializeField, Range(0f, 1f), Tooltip("캐릭터 세로 하단 위치 (0=화면 하단, 1=화면 상단). 발이 닿는 지점 기준.")]
    private float _characterAnchorBottom = 0.1f;

    [SerializeField, Range(0f, 0.4f), Tooltip("발밑 그림자 높이 (캐릭터 높이 대비 비율). 0이면 그림자 숨김.")]
    private float _shadowHeightRatio = 0.07f;

    [SerializeField, Range(0.3f, 2.5f), Tooltip("그림자 폭 배수 (텍스처 원본 종횡비 기준).")]
    private float _shadowWidthScale = 1f;

    [SerializeField, Tooltip("그림자 Y 오프셋 (px). 양수 = 발 아래쪽.")]
    private float _shadowYOffset = -4f;

    [SerializeField, Range(0f, 1f), Tooltip("그림자 알파.")]
    private float _shadowAlpha = 0.75f;

    [Header("Stage (본 애니메이션용)")]
    [SerializeField, Tooltip("배경+캐릭터 SpriteRenderer를 묶은 GameObject. 지정하면 OnGUI 대신 씬의 Sprite를 사용 (본 리깅 가능). 비워두면 OnGUI 폴백.")]
    private GameObject _stage;

    [Header("Cloud Scroll (구름 애니)")]
    [SerializeField, Tooltip("구름 레이어 켜기.")]
    private bool _cloudEnabled = true;

    [SerializeField, Tooltip("구름 텍스처 override. 설정하면 Resources/CharSelect/Background/akane_select_clouds 대신 이 텍스처 사용.\nJMO Assets/Cartoon FX Remaster/CFXR Assets/Graphics/cfxr cloud blur.png 또는 cfxr noise clouds big.png 추천.")]
    private Texture2D _cloudTextureOverride;

    [SerializeField, Range(1, 8), Tooltip("스프라이트시트 가로 프레임 수. x4 텍스처면 4, 단일 이미지면 1.")] private int _cloudColumns = 1;
    [SerializeField, Range(1, 8), Tooltip("스프라이트시트 세로 프레임 수.")] private int _cloudRows = 1;
    [SerializeField, Range(0.5f, 30f), Tooltip("스프라이트시트 프레임 재생 속도 (fps).")] private float _cloudFps = 6f;

    [SerializeField, Range(0f, 30f), Tooltip("구름 가로 스크롤 속도 (px/sec). 0이면 정지.")]
    private float _cloudScrollSpeed = 6f;

    [SerializeField, Range(0f, 1f), Tooltip("구름 레이어가 화면 높이의 몇 %를 차지할지 (위에서부터). 0.6 = 위쪽 60%.")]
    private float _cloudHeightRatio = 0.55f;

    [SerializeField, Range(0f, 1f), Tooltip("구름 알파 (0=투명, 1=불투명).")]
    private float _cloudAlpha = 0.6f;

    [SerializeField, Tooltip("구름 색상 틴트 (어두운 다크판타지 톤용).")]
    private Color _cloudTint = new Color(0.85f, 0.88f, 0.95f, 1f);

    [Header("FX • Fireflies (반딧불이)")]
    [SerializeField] private bool _enableFireflies = false;
    [SerializeField, Range(0, 50), Tooltip("입자 개수.")] private int _fireflyCount = 22;
    [SerializeField, Tooltip("크기 범위 (최소~최대 px).")] private Vector2 _fireflySize = new Vector2(14f, 26f);
    [SerializeField, Tooltip("알파 범위 (0~1).")] private Vector2 _fireflyAlpha = new Vector2(0.55f, 0.95f);
    [SerializeField, Tooltip("가로 드리프트 속도 범위 (px/sec).")] private Vector2 _fireflyDriftX = new Vector2(-8f, 8f);
    [SerializeField, Tooltip("위아래 흔들림 진폭 범위 (px).")] private Vector2 _fireflyBobAmp = new Vector2(10f, 24f);
    [SerializeField, Tooltip("위아래 흔들림 주파수 범위 (Hz·rad).")] private Vector2 _fireflyBobFreq = new Vector2(0.4f, 1.0f);
    [SerializeField, Range(0f, 1f), Tooltip("등장 영역 상단 (0=화면 상단). 하늘까지 올라가면 별처럼 보이니 0.5 이상 권장.")] private float _fireflyAreaTop = 0.62f;
    [SerializeField, Range(0f, 1f), Tooltip("등장 영역 하단 (1=화면 하단).")] private float _fireflyAreaBottom = 0.95f;
    [SerializeField, Tooltip("주 색상 (70%).")] private Color _fireflyTint = new Color(1f, 0.95f, 0.6f);
    [SerializeField, Tooltip("강조 색상 (30%).")] private Color _fireflyTintAccent = new Color(1f, 0.75f, 0.4f);
    [SerializeField, Range(0f, 1f), Tooltip("강조 색상이 나올 확률.")] private float _fireflyAccentChance = 0.3f;
    [SerializeField, Range(1f, 4f), Tooltip("외곽 글로우 크기 배수.")] private float _fireflyGlowSize = 3.0f;

    [Header("FX • Mist (중앙 안개)")]
    [SerializeField] private bool _enableMist = false;
    [SerializeField, Range(0, 20), Tooltip("안개 블롭 개수.")] private int _mistBlobCount = 5;
    [SerializeField, Tooltip("Y 위치 범위 (0=상단, 1=하단). 화면 중앙대 영역.")] private Vector2 _mistYRange = new Vector2(0.4f, 0.7f);
    [SerializeField, Tooltip("폭 범위 (화면폭 비율).")] private Vector2 _mistWidthRatio = new Vector2(0.35f, 0.7f);
    [SerializeField, Tooltip("높이 범위 (px).")] private Vector2 _mistHeight = new Vector2(80f, 150f);
    [SerializeField, Tooltip("알파 범위.")] private Vector2 _mistAlpha = new Vector2(0.1f, 0.22f);
    [SerializeField, Tooltip("가로 이동 속도 범위 (절대값, px/sec). 방향은 50% 확률로 랜덤.")] private Vector2 _mistSpeed = new Vector2(25f, 45f);
    [SerializeField, Tooltip("안개 색상.")] private Color _mistTint = new Color(0.88f, 0.92f, 1f);
    [SerializeField, Tooltip("캐릭터보다 앞에 그려서 발 주변에 퍼지게. 끄면 캐릭터 뒤에만.")] private bool _mistInForeground = true;

    [Header("FX • Lightning (하늘 번개)")]
    [SerializeField] private bool _enableLightning = false;
    [SerializeField, Tooltip("번개 사이 간격 범위 (sec).")] private Vector2 _lightningInterval = new Vector2(1.5f, 3.5f);
    [SerializeField, Range(0.1f, 3f), Tooltip("한 번 번개 총 지속 시간 (sec). 클수록 번개가 더 느리게 사라짐.")] private float _lightningDuration = 1.2f;
    [SerializeField, Tooltip("한 번 칠 때 번개 줄기 개수 (X=최소, Y=최대). 동시 X, 순차적으로 등장.")] private Vector2 _lightningBoltsPerStrike = new Vector2(1f, 2f);
    [SerializeField, Range(0f, 1f), Tooltip("하늘 플래시 최대 알파 (0=끄기).")] private float _lightningPeakAlpha = 0f;
    [SerializeField, Range(0.1f, 1f), Tooltip("화면 상단에서 몇 %까지 빛이 내려오는지.")] private float _lightningSkyHeight = 0.6f;
    [SerializeField, Tooltip("번개 색상.")] private Color _lightningColor = new Color(0.85f, 0.92f, 1f);
    [SerializeField, Tooltip("번개 줄기(번쩍이는 가지) 그리기.")] private bool _lightningDrawBolt = true;
    [SerializeField, Range(1, 12), Tooltip("절차적 번개 줄기 두께 (px).")] private float _lightningBoltThickness = 5f;
    [SerializeField, Range(1, 4), Tooltip("줄기에서 갈라지는 가지 수.")] private int _lightningBranchCount = 2;
    [SerializeField, Tooltip("줄기 길이 비율 (화면 높이 대비). X=최소, Y=최대. 작을수록 하늘 위쪽에서만 짧게 번쩍.")] private Vector2 _lightningBoltLength = new Vector2(0.2f, 0.35f);
    [SerializeField, Tooltip("번개 텍스처 (스프라이트시트). 비워두면 절차적 지그재그 사용.\nJMO Assets/Cartoon FX Remaster/CFXR Assets/Graphics/cfxr electric arc anim x4.png 추천.")]
    private Texture2D _lightningBoltTexture;
    [SerializeField, Range(1, 16), Tooltip("스프라이트시트 가로 프레임 수.")] private int _lightningBoltColumns = 4;
    [SerializeField, Range(1, 16), Tooltip("스프라이트시트 세로 프레임 수.")] private int _lightningBoltRows = 1;
    [SerializeField, Range(5f, 60f), Tooltip("프레임 재생 속도 (fps).")] private float _lightningBoltFps = 24f;
    [SerializeField, Range(0.05f, 1f), Tooltip("텍스처 사용 시 번개 폭 비율 (화면 높이 대비).")] private float _lightningBoltWidth = 0.35f;
    [SerializeField, Range(0f, 1f), Tooltip("번개 줄기 전체 최대 밝기. 낮추면 덜 진함.")] private float _lightningBoltMaxAlpha = 0.55f;
    [SerializeField, Range(0.2f, 0.9f), Tooltip("전체 duration 중 페이드 차지 비율 (클수록 천천히 사라짐).")] private float _lightningBoltFadeRatio = 0.6f;

    [Header("FX • Ash (잿가루)")]
    [SerializeField] private bool _enableAsh = false;
    [SerializeField, Range(0, 100), Tooltip("입자 개수.")] private int _ashCount = 40;
    [SerializeField, Tooltip("낙하 속도 범위 (px/sec).")] private Vector2 _ashFallSpeed = new Vector2(20f, 45f);
    [SerializeField, Tooltip("좌우 흔들림 강도 범위.")] private Vector2 _ashDrift = new Vector2(-12f, 12f);
    [SerializeField, Tooltip("크기 범위 (px).")] private Vector2 _ashSize = new Vector2(3.5f, 7f);
    [SerializeField, Tooltip("알파 범위.")] private Vector2 _ashAlpha = new Vector2(0.4f, 0.75f);
    [SerializeField, Tooltip("잿가루 색상.")] private Color _ashTint = new Color(0.85f, 0.85f, 0.88f);

    [Header("FX • Parallax (마우스 패럴럭스)")]
    [SerializeField, Tooltip("마우스 움직임에 따라 BG/캐릭터가 반대로 살짝 이동 (3D 깊이감).")]
    private bool _enableParallax = false;
    [SerializeField, Range(0f, 60f), Tooltip("BG 이동 강도 (px 단위 최대 이동).")] private float _bgParallaxStrength = 14f;
    [SerializeField, Range(0f, 80f), Tooltip("캐릭터 이동 강도 (BG보다 크게 해야 자연스러움).")] private float _charParallaxStrength = 28f;
    [SerializeField, Range(1f, 15f), Tooltip("부드러움 (클수록 즉각 반응, 작을수록 관성).")] private float _parallaxSmoothing = 6f;

    [Header("FX • BG Breathing (배경 호흡 줌)")]
    [SerializeField] private bool _enableBgBreathing = false;
    [SerializeField, Range(0f, 0.03f), Tooltip("맥동 진폭 (비율). 0.005=0.5% 변화.")] private float _bgBreathingAmp = 0.006f;
    [SerializeField, Range(0.05f, 1f), Tooltip("맥동 주파수 (Hz). 0.15=약 6.7초 주기.")] private float _bgBreathingFreq = 0.15f;

    [Header("FX • Character Breathing (캐릭터 호흡)")]
    [SerializeField] private bool _enableCharBreathing = true;
    [SerializeField, Range(0f, 0.03f), Tooltip("캐릭터 Y축 호흡 진폭. 0.009=0.9% 늘림. 0.012 넘으면 머리가 너무 들썩임.")] private float _charBreathingAmp = 0.009f;
    [SerializeField, Range(0.05f, 1f), Tooltip("캐릭터 맥동 주파수 (Hz). 0.15=약 6.7초 주기.")] private float _charBreathingFreq = 0.15f;
    [SerializeField, Range(0f, 6.28f), Tooltip("배경 호흡과 박자 어긋나게 하려면 0.5~3 정도.")] private float _charBreathingPhase = 1.5f;

    [Header("FX • Dust (바닥에서 올라오는 먼지 — LobbyUI Bottom Smoke와 동일 공식)")]
    [SerializeField] private bool _enableDust = true;
    [SerializeField, Range(0, 40), Tooltip("먼지 입자 개수.")] private int _dustCount = 14;
    [SerializeField, Tooltip("스폰 Y 위치 범위 (0=상단, 1=하단). 화면 하단 얇은 띠.")] private Vector2 _dustYRange = new Vector2(0.965f, 1.0f);
    [SerializeField, Range(20f, 600f), Tooltip("올라가는 높이 (px).")] private float _dustRiseHeight = 70f;
    [SerializeField, Range(0.02f, 1f), Tooltip("상승 속도 (life/sec). 작을수록 천천히.")] private float _dustRiseSpeed = 0.15f;
    [SerializeField, Range(0f, 80f), Tooltip("좌우 흔들림 폭 (px).")] private float _dustSwayAmount = 25f;
    [SerializeField, Range(0.1f, 3f), Tooltip("흔들림 주파수.")] private float _dustSwayFrequency = 0.4f;
    [SerializeField, Tooltip("크기 범위 (px).")] private Vector2 _dustSize = new Vector2(30f, 55f);
    [SerializeField, Range(0f, 1f), Tooltip("전체 알파 배수.")] private float _dustAlphaMul = 0.2f;
    [SerializeField, Range(0f, 30f), Tooltip("플리커(반짝임) 속도.")] private float _dustFlickerSpeed = 2f;
    [SerializeField, Range(0f, 1f), Tooltip("플리커 깊이.")] private float _dustFlickerDepth = 0.2f;
    [SerializeField, Tooltip("코어 색상 (안쪽 — 시멘트 톤 중립 회색).")] private Color _dustTint = new Color(0.72f, 0.73f, 0.76f);
    [SerializeField, Tooltip("바깥 색상 (외곽 블룸 — 시멘트 톤 중립 회색).")] private Color _dustOuterTint = new Color(0.4f, 0.41f, 0.44f);
    [SerializeField, Range(1f, 6f), Tooltip("바깥 블룸 크기 (코어 대비 배수).")] private float _dustBloomScale = 4.5f;
    [SerializeField, Range(0f, 1f), Tooltip("바깥 블룸 알파 배수.")] private float _dustBloomAlphaMul = 0.55f;
    [SerializeField, Tooltip("스폰 X 위치 범위 (화면 폭 비율). (-0.1, 1.1)=살짝 화면 밖까지.")] private Vector2 _dustXRange = new Vector2(-0.1f, 1.1f);

    [Header("Card Hover (카드 호버)")]
    [SerializeField, Range(1f, 1.3f), Tooltip("카드 마우스 오버 시 확대 배율 (1=동일).")]
    private float _cardHoverScale = 1.10f;

    private readonly List<Action> _pending = new();

    private CharacterData _selectedCharacter;

    private Texture2D _backgroundTexture;
    private Texture2D _characterTexture;
    private Texture2D[] _characterFrames;      // 캐릭터 애니메이션 프레임 시퀀스 (있으면 정적 _characterTexture 대신 사용)
    private float _characterFps = 12f;         // KLING 원본의 절반 속도 (122프레임 ÷ 12fps ≈ 10.2s 루프)
    private Texture2D _characterShadowTexture;
    private Texture2D _cloudsTexture;
    private float _cloudOffset;

    // 절차 생성 텍스처 — Start에서 1회 생성
    private Texture2D _glowDot;      // 반딧불이/잿가루용 부드러운 원형 글로우
    private Texture2D _mistBlob;     // 안개용 가로로 늘어진 부드러운 블롭
    private Texture2D _dustBlob;     // 먼지용 — LobbyUI MakeRadialGlow와 동일 smoothstep falloff
    private Texture2D _skyGradient;  // 번개용 세로 그라데이션 (위 불투명 → 아래 투명)
    private Texture2D _emptySlotGradient; // 비어있는 카드 슬롯 배경 (위 살짝 밝음 → 아래 어두움)

    // 파티클 상태
    private struct Firefly { public Vector2 pos; public float driftX; public float bobAmp; public float bobFreq; public float phase; public float size; public float baseAlpha; public Color tint; }
    private struct Ash { public Vector2 pos; public float fallSpeed; public float driftX; public float driftFreq; public float phase; public float size; public float alpha; }
    private struct MistBlob { public float x; public float y; public float baseY; public float w; public float h; public float speed; public float alpha; public float bobFreq; public float bobPhase; public float bobAmp; }
    private Firefly[] _fireflies;
    private Ash[] _ashes;
    private MistBlob[] _mistBlobs;
    private bool _dustReady;

    // Parallax smoothed offset (-1 ~ 1 정규화)
    private Vector2 _parallaxOffset;
    private bool _fxInitialized;

    // 번개 상태 — 한 번 트리거되면 여러 줄기가 순차적으로 생겨남
    private float _nextLightningTime;
    private float _lightningElapsed = -1f;  // -1이면 비활성
    private float _lightningTotalDuration;  // 마지막 줄기까지 포함한 전체 이벤트 시간

    private struct BoltInstance
    {
        public Vector2[] path;
        public Vector2[][] branches;
        public float startOffset;  // _lightningElapsed가 이 값을 넘어야 보이기 시작
        public float randomSeed;   // frame 계산 시 번개별로 다른 phase
    }
    private readonly List<BoltInstance> _bolts = new List<BoltInstance>();
    private Texture2D _cardSlotLocked;
    private Texture2D _cardFrameBase;
    private Texture2D _buttonBack;
    private Texture2D _buttonConfirm;
    private Texture2D _archaeologistCardPortrait;
    private Texture2D _iconHeart;
    private Texture2D _iconCoin;
    private Texture2D _iconPassive;

    private Font _displayFont;  // 영문 디스플레이 (Cinzel)
    private Font _bodyFont;     // 한글 본문 (Noto Sans KR)

    // ----- 레이아웃 (1280×720 가상 좌표) -----
    private static readonly Rect InfoPanelRect = new Rect(60, 60, 600, 340);

    // 5개 카드 (90×126), 가로 중앙 정렬, 카드 간 간격 20, 화면 하단 배치
    private static readonly Rect[] CardRects = new Rect[]
    {
        new Rect(375, 580, 90, 126),
        new Rect(485, 580, 90, 126),
        new Rect(595, 580, 90, 126),
        new Rect(705, 580, 90, 126),
        new Rect(815, 580, 90, 126),
    };

    private static readonly Rect BackButtonRect    = new Rect(  60, 560, 90, 90);
    private static readonly Rect ConfirmButtonRect = new Rect(1130, 560, 90, 90);

    // 스타일
    private GUIStyle _titleStyle;
    private GUIStyle _statStyle;
    private GUIStyle _hpStyleMid;
    private GUIStyle _goldStyleMid;
    private GUIStyle _descStyle;
    private GUIStyle _abilityNameStyle;
    private GUIStyle _abilityDescStyle;
    private GUIStyle _cardNameStyle;
    private GUIStyle _slotLabelStyle;
    private GUIStyle _comingSoonStyle;
    private GUIStyle _questionStyle;
    private bool _stylesReady;
    private bool _assetsLoaded;

    private float _comingSoonTimer;

    void Start()
    {
        LoadAssets();
        InitAtmosphereFX();
    }

    void OnDestroy()
    {
        if (_glowDot != null) Destroy(_glowDot);
        if (_mistBlob != null) Destroy(_mistBlob);
        if (_dustBlob != null) Destroy(_dustBlob);
        if (_skyGradient != null) Destroy(_skyGradient);
        if (_emptySlotGradient != null) Destroy(_emptySlotGradient);
    }

    private void InitAtmosphereFX()
    {
        if (_fxInitialized) return;

        _glowDot = GenerateGlowTexture(64, isElongated: false);
        _mistBlob = GenerateGlowTexture(96, isElongated: true);
        _dustBlob = GenerateDustBlob(64);
        _skyGradient = GenerateVerticalGradient(256);

        // 반딧불이 — 화면 전역에 분산, 따뜻한 황색 글로우
        _fireflies = new Firefly[_fireflyCount];
        for (int i = 0; i < _fireflies.Length; i++) _fireflies[i] = SpawnFirefly(initial: true);

        // 잿가루 — 위에서 천천히 내려옴
        _ashes = new Ash[_ashCount];
        for (int i = 0; i < _ashes.Length; i++) _ashes[i] = SpawnAsh(initial: true);

        // 안개 — 화면 하단에 가로로 길쭉한 블롭
        _mistBlobs = new MistBlob[_mistBlobCount];
        for (int i = 0; i < _mistBlobs.Length; i++) _mistBlobs[i] = SpawnMistBlob();

        // 바닥에서 올라오는 먼지 — Hash01 기반 인덱스 공식 (LobbyUI Bottom Smoke 동일)
        _dustReady = true;

        _nextLightningTime = Time.time + UnityEngine.Random.Range(_lightningInterval.x, _lightningInterval.y);
        _fxInitialized = true;
    }

    /// <summary>
    /// 세로 그라데이션 — 텍스처 상단(y=0 in draw)이 불투명, 하단이 투명.
    /// Unity 텍스처 좌표는 y=0이 아래라 픽셀 배치 시 반전. OnGUI는 상단이 y=0.
    /// 결과: DrawTexture로 그리면 Rect의 위쪽이 진하고 아래로 내려갈수록 투명.
    /// </summary>
    private Texture2D GenerateVerticalGradient(int height)
    {
        var tex = new Texture2D(2, height, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        tex.hideFlags = HideFlags.HideAndDontSave;

        var pixels = new Color[2 * height];
        for (int y = 0; y < height; y++)
        {
            // y=0 (텍스처 하단 = 화면 하단) → 알파 0
            // y=height-1 (텍스처 상단 = 화면 상단) → 알파 1
            float t = (float)y / (height - 1);
            t = t * t; // 상단에 집중 (제곱)
            for (int x = 0; x < 2; x++)
                pixels[y * 2 + x] = new Color(1f, 1f, 1f, t);
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    // 비어있는 카드 슬롯 — 위쪽이 살짝 밝고 아래로 갈수록 어두워지는 부드러운 세로 그라데이션.
    // base 색을 중심으로 ±소량 명도차만 줘서 깊이감만 살짝 더한다.
    private Texture2D GenerateEmptySlotGradient(Color baseColor, int height)
    {
        var tex = new Texture2D(2, height, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave,
        };
        Color top = new Color(
            Mathf.Clamp01(baseColor.r + 0.10f),
            Mathf.Clamp01(baseColor.g + 0.10f),
            Mathf.Clamp01(baseColor.b + 0.11f),
            baseColor.a);
        Color bottom = new Color(
            Mathf.Clamp01(baseColor.r - 0.12f),
            Mathf.Clamp01(baseColor.g - 0.12f),
            Mathf.Clamp01(baseColor.b - 0.11f),
            baseColor.a);
        var pixels = new Color[2 * height];
        for (int y = 0; y < height; y++)
        {
            float t = (float)y / (height - 1); // y=0 (텍스처 하단 = 화면 하단) → bottom
            float k = Mathf.SmoothStep(0f, 1f, t);
            Color c = Color.Lerp(bottom, top, k);
            pixels[y * 2] = c;
            pixels[y * 2 + 1] = c;
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    private Texture2D GenerateGlowTexture(int size, bool isElongated)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        tex.hideFlags = HideFlags.HideAndDontSave;

        var pixels = new Color[size * size];
        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float maxRadius = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - center.x);
                float dy = (y + 0.5f - center.y);
                if (isElongated) dx *= 0.45f; // 가로로 늘어진 블롭 (안개)
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float t = Mathf.Clamp01(1f - dist / maxRadius);
                t = t * t; // 부드러운 falloff
                pixels[y * size + x] = new Color(1f, 1f, 1f, t);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    private Firefly SpawnFirefly(bool initial)
    {
        float topY = Screen.height * Mathf.Min(_fireflyAreaTop, _fireflyAreaBottom);
        float botY = Screen.height * Mathf.Max(_fireflyAreaTop, _fireflyAreaBottom);
        return new Firefly
        {
            pos = new Vector2(
                UnityEngine.Random.Range(0f, Screen.width),
                initial ? UnityEngine.Random.Range(topY, botY)
                        : UnityEngine.Random.Range(topY, botY)),
            driftX = UnityEngine.Random.Range(_fireflyDriftX.x, _fireflyDriftX.y),
            bobAmp = UnityEngine.Random.Range(_fireflyBobAmp.x, _fireflyBobAmp.y),
            bobFreq = UnityEngine.Random.Range(_fireflyBobFreq.x, _fireflyBobFreq.y),
            phase = UnityEngine.Random.Range(0f, Mathf.PI * 2f),
            size = UnityEngine.Random.Range(_fireflySize.x, _fireflySize.y),
            baseAlpha = UnityEngine.Random.Range(_fireflyAlpha.x, _fireflyAlpha.y),
            tint = UnityEngine.Random.value < _fireflyAccentChance ? _fireflyTintAccent : _fireflyTint,
        };
    }

    private Ash SpawnAsh(bool initial)
    {
        return new Ash
        {
            pos = new Vector2(
                UnityEngine.Random.Range(0f, Screen.width),
                initial ? UnityEngine.Random.Range(0f, Screen.height) : -10f),
            fallSpeed = UnityEngine.Random.Range(_ashFallSpeed.x, _ashFallSpeed.y),
            driftX = UnityEngine.Random.Range(_ashDrift.x, _ashDrift.y),
            driftFreq = UnityEngine.Random.Range(0.3f, 0.8f),
            phase = UnityEngine.Random.Range(0f, Mathf.PI * 2f),
            size = UnityEngine.Random.Range(_ashSize.x, _ashSize.y),
            alpha = UnityEngine.Random.Range(_ashAlpha.x, _ashAlpha.y),
        };
    }

    private static float DustHash01(float x)
    {
        float s = Mathf.Sin(x) * 43758.5453f;
        s -= Mathf.Floor(s);
        return s;
    }

    // LobbyUI.MakeRadialGlow와 동일한 smoothstep 방사형 falloff — 부드러운 스모크용.
    private static Texture2D GenerateDustBlob(int size)
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
                a = a * a * (3f - 2f * a); // smoothstep
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    private MistBlob SpawnMistBlob()
    {
        float baseY = Screen.height * UnityEngine.Random.Range(_mistYRange.x, _mistYRange.y);
        return new MistBlob
        {
            x = UnityEngine.Random.Range(-200f, Screen.width),
            y = baseY,
            baseY = baseY,
            w = Screen.width * UnityEngine.Random.Range(_mistWidthRatio.x, _mistWidthRatio.y),
            h = UnityEngine.Random.Range(_mistHeight.x, _mistHeight.y),
            speed = UnityEngine.Random.Range(_mistSpeed.x, _mistSpeed.y) * (UnityEngine.Random.value < 0.5f ? -1f : 1f),
            alpha = UnityEngine.Random.Range(_mistAlpha.x, _mistAlpha.y),
            bobFreq = UnityEngine.Random.Range(0.2f, 0.5f),
            bobPhase = UnityEngine.Random.Range(0f, Mathf.PI * 2f),
            bobAmp = UnityEngine.Random.Range(6f, 14f),
        };
    }

    void Update()
    {
        if (_pending.Count > 0)
        {
            var snapshot = new List<Action>(_pending);
            _pending.Clear();
            foreach (var a in snapshot) a?.Invoke();
        }

        if (_comingSoonTimer > 0) _comingSoonTimer -= Time.deltaTime;

        // 캐릭터 선택 화면에서만 모든 FX 갱신
        var gsmFx = GameStateManager.Instance;
        bool inSelect = gsmFx != null && gsmFx.State == GameState.CharacterSelect;

        if (inSelect)
        {
            float dt = Time.deltaTime;

            // 구름 가로 스크롤
            if (_cloudEnabled && _cloudsTexture != null && _cloudScrollSpeed > 0f)
            {
                _cloudOffset += _cloudScrollSpeed * dt;
                float texW = _cloudsTexture.width;
                if (_cloudOffset >= texW) _cloudOffset -= texW;
            }

            // Parallax — 마우스 위치를 화면 중심 기준 -1~1로 정규화, 스무딩 적용.
            // 신 Input System 기반: Mouse.current 사용. 연결된 마우스 없으면 중심.
            if (_enableParallax)
            {
                Vector2 target = Vector2.zero;
                if (Mouse.current != null)
                {
                    Vector2 mp = Mouse.current.position.ReadValue(); // Y-up, 픽셀 단위
                    target = new Vector2(
                        mp.x / Screen.width * 2f - 1f,
                        mp.y / Screen.height * 2f - 1f);
                    if (mp.x < 0 || mp.x > Screen.width || mp.y < 0 || mp.y > Screen.height)
                        target = Vector2.zero;
                }
                _parallaxOffset = Vector2.Lerp(_parallaxOffset, target, Mathf.Clamp01(_parallaxSmoothing * dt));
            }
            else
            {
                _parallaxOffset = Vector2.zero;
            }

            UpdateAtmosphereFX(dt);
        }

        // Stage(씬의 배경+캐릭터)가 지정돼 있으면 캐릭터 선택 상태일 때만 활성화
        if (_stage != null)
        {
            var gsm = GameStateManager.Instance;
            bool show = gsm != null && gsm.State == GameState.CharacterSelect;
            if (_stage.activeSelf != show) _stage.SetActive(show);
        }
    }

    private void LoadAssets()
    {
        // 테이블에서 캐릭터 정보 로드
        DataManager.Instance.Load();
        _selectedCharacter = DataManager.Instance.GetCharacter(_selectedCharacterId);
        if (_selectedCharacter == null)
            Debug.LogError($"[CharacterSelectUI] Missing character data: {_selectedCharacterId}");

        // UI 요소 — CharSelect/UI/
        _cardSlotLocked    = Resources.Load<Texture2D>("CharSelect/UI/CardSlot_Locked");
        _cardFrameBase     = Resources.Load<Texture2D>("CharSelect/UI/CardFrameBase");
        _buttonBack        = Resources.Load<Texture2D>("CharSelect/UI/Button_Back");
        _buttonConfirm     = Resources.Load<Texture2D>("CharSelect/UI/Button_Confirm");

        // 카드 초상 — Character_select/ (경로는 character.csv 의 card_portrait)
        string portraitName = _selectedCharacter != null
            ? _selectedCharacter.cardPortrait
            : "Char_Archaeologist_Card";
        _archaeologistCardPortrait = Resources.Load<Texture2D>("Character_select/" + portraitName);

        // HP/Gold/패시브 아이콘 — CharSelect/Icon/
        _iconHeart   = Resources.Load<Texture2D>("CharSelect/Icon/ico_heart");
        _iconCoin    = Resources.Load<Texture2D>("CharSelect/Icon/ico_coin");
        _iconPassive = Resources.Load<Texture2D>("CharSelect/Icon/passive");

        // 폰트 — Fonts/
        _displayFont = Resources.Load<Font>("Fonts/Cinzel-VariableFont_wght");
        _bodyFont    = Resources.Load<Font>("Fonts/NotoSansKR-VariableFont_wght");

        // 배경 — CharSelect/Background/akane_select_bg.png (캐릭터 빠진 빈 배경).
        _backgroundTexture = Resources.Load<Texture2D>("CharSelect/Background/akane_select_bg")
                          ?? Resources.Load<Texture2D>("CharSelect/Background/akane_select")
                          ?? Resources.Load<Texture2D>("CharSelect/Background/CharSelect_Background")
                          ?? Resources.Load<Texture2D>("Lobby/Main_Background");

        // 캐릭터 — 애니메이션 시퀀스(akane_select_char_anim/) 우선, 없으면 정적 PNG 폴백.
        // 추후 캐릭터별 분기 필요 시 character.csv에 select_character_anim 컬럼 추가.
        var charFrames = Resources.LoadAll<Texture2D>("CharSelect/Background/akane_select_char_anim");
        _characterFrames = charFrames != null && charFrames.Length > 0
            ? charFrames.OrderBy(t => t.name, StringComparer.Ordinal).ToArray()
            : null;
        _characterTexture = Resources.Load<Texture2D>("CharSelect/Background/akane_select_char");
        _characterShadowTexture = Resources.Load<Texture2D>("Character_infield/character_basic/shadow/character_shadow");
        _cloudsTexture = Resources.Load<Texture2D>("CharSelect/Background/akane_select_clouds");

        if (_cardSlotLocked == null)   Debug.LogWarning("[CharacterSelectUI] Missing CharSelect/UI/CardSlot_Locked");
        if (_cardFrameBase == null)    Debug.LogWarning("[CharacterSelectUI] Missing CharSelect/UI/CardFrameBase");
        if (_buttonBack == null)       Debug.LogWarning("[CharacterSelectUI] Missing CharSelect/UI/Button_Back");
        if (_buttonConfirm == null)    Debug.LogWarning("[CharacterSelectUI] Missing CharSelect/UI/Button_Confirm");
        if (_archaeologistCardPortrait == null) Debug.LogWarning($"[CharacterSelectUI] Missing Character_select/{portraitName}");
        if (_iconHeart == null)   Debug.LogWarning("[CharacterSelectUI] Missing CharSelect/Icon/ico_heart");
        if (_iconCoin == null)    Debug.LogWarning("[CharacterSelectUI] Missing CharSelect/Icon/ico_coin");
        if (_iconPassive == null) Debug.LogWarning("[CharacterSelectUI] Missing CharSelect/Icon/passive");
        if (_backgroundTexture == null) Debug.LogWarning("[CharacterSelectUI] Missing CharSelect/Background/akane_select_bg");
        if (_characterTexture == null) Debug.LogWarning("[CharacterSelectUI] Missing CharSelect/Background/akane_select_char");
        if (_cloudsTexture == null) Debug.LogWarning("[CharacterSelectUI] Missing CharSelect/Background/akane_select_clouds");
        if (_displayFont == null) Debug.LogWarning("[CharacterSelectUI] Missing Fonts/Cinzel-VariableFont_wght");
        if (_bodyFont == null) Debug.LogWarning("[CharacterSelectUI] Missing Fonts/NotoSansKR-VariableFont_wght");

        _assetsLoaded = true;
    }

    /// <summary>주어진 사각형을 중심 기준으로 scale 배만큼 확대/축소.</summary>
    private static Rect ScaleRectFromCenter(Rect r, float scale)
    {
        float w = r.width * scale;
        float h = r.height * scale;
        return new Rect(r.center.x - w * 0.5f, r.center.y - h * 0.5f, w, h);
    }

    // =========================================================
    // OnGUI
    // =========================================================

    void OnGUI()
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null || gsm.State != GameState.CharacterSelect) return;

        if (!_assetsLoaded) LoadAssets();
        EnsureStyles();

        // 1) 배경 + 캐릭터
        // Stage GameObject가 연결돼 있으면 씬의 SpriteRenderer가 그리니까 OnGUI 폴백 스킵.
        // 미연결 시(또는 본 리깅 셋업 전)에는 OnGUI로 평면 텍스처 폴백 — 화면이 비지 않게.
        GUI.matrix = Matrix4x4.identity;
        if (_stage == null)
        {
            DrawBackground();
            if (_cloudEnabled) DrawCloudsOverlay();
            DrawAtmosphereFX();        // 안개 — 캐릭터 뒤
            DrawCharacterOverlay();
            DrawAtmosphereFXForeground(); // 반딧불/재/번개 — 캐릭터 앞
        }
        else
        {
            // Stage(씬 Sprite) 사용 시에도 atmosphere는 OnGUI로 계속 그림
            DrawAtmosphereFX();
            DrawAtmosphereFXForeground();
        }

        // 2) 가상 좌표계로 전환
        float scale = Mathf.Min(Screen.width / RefW, Screen.height / RefH);
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));

        DrawInfoPanel();
        DrawCardRow();
        DrawButtons(gsm);
        DrawComingSoonOverlay();
    }

    private void DrawBackground()
    {
        if (_backgroundTexture != null)
        {
            // Parallax 이동 + Breathing 줌 + 엣지 가림 방지용 3% 오버사이즈
            float breath = _enableBgBreathing
                ? 1f + Mathf.Sin(Time.time * Mathf.PI * 2f * _bgBreathingFreq) * _bgBreathingAmp
                : 1f;
            // 패럴럭스로 잘리는 엣지 + 호흡 확대치를 합쳐서 여유 오버스캔
            float oversize = 1.03f * breath;
            float w = Screen.width * oversize;
            float h = Screen.height * oversize;
            float px = -_parallaxOffset.x * _bgParallaxStrength;
            float py = _parallaxOffset.y * _bgParallaxStrength;  // 마우스는 Y-up, OnGUI는 Y-down이라 부호 조정
            float x = (Screen.width - w) * 0.5f + px;
            float y = (Screen.height - h) * 0.5f + py;

            GUI.DrawTexture(
                new Rect(x, y, w, h),
                _backgroundTexture,
                ScaleMode.ScaleAndCrop,
                alphaBlend: true);
        }
        else
        {
            var prev = GUI.color;
            GUI.color = new Color(0.08f, 0.06f, 0.05f, 1f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = prev;
        }
    }

    private void UpdateAtmosphereFX(float dt)
    {
        if (!_fxInitialized) return;

        // 반딧불이 — bobbing 위치, 떠다니다 화면 밖으로 나가면 반대편에서 재생성
        if (_enableFireflies && _fireflies != null)
        {
            for (int i = 0; i < _fireflies.Length; i++)
            {
                var f = _fireflies[i];
                f.phase += f.bobFreq * dt;
                f.pos.x += f.driftX * dt;
                if (f.pos.x < -20f) f.pos.x = Screen.width + 20f;
                else if (f.pos.x > Screen.width + 20f) f.pos.x = -20f;
                _fireflies[i] = f;
            }
        }

        // 잿가루 — 떨어지면서 좌우 살짝 흔들. 화면 아래로 나가면 재생성
        if (_enableAsh && _ashes != null)
        {
            for (int i = 0; i < _ashes.Length; i++)
            {
                var a = _ashes[i];
                a.phase += a.driftFreq * dt;
                a.pos.y += a.fallSpeed * dt;
                a.pos.x += a.driftX * dt * Mathf.Sin(a.phase);
                if (a.pos.y > Screen.height + 10f || a.pos.x < -20f || a.pos.x > Screen.width + 20f)
                    _ashes[i] = SpawnAsh(initial: false);
                else
                    _ashes[i] = a;
            }
        }

        // 안개 — 가로 드리프트 + 세로 bobbing. 화면 밖 나가면 반대편에서 다시.
        if (_enableMist && _mistBlobs != null)
        {
            for (int i = 0; i < _mistBlobs.Length; i++)
            {
                var b = _mistBlobs[i];
                b.x += b.speed * dt;
                b.bobPhase += b.bobFreq * dt;
                b.y = b.baseY + Mathf.Sin(b.bobPhase) * b.bobAmp;
                if (b.speed > 0 && b.x - b.w * 0.5f > Screen.width) b.x = -b.w * 0.5f;
                else if (b.speed < 0 && b.x + b.w * 0.5f < 0) b.x = Screen.width + b.w * 0.5f;
                _mistBlobs[i] = b;
            }
        }

        // 번개 — 트리거되면 elapsed가 0부터 duration까지 진행
        if (_enableLightning)
        {
            if (_lightningElapsed >= 0f)
            {
                _lightningElapsed += dt;
                // 순차 번개일 때 마지막 줄기가 완전히 페이드될 때까지 이벤트 유지
                float totalDur = _lightningTotalDuration > 0f ? _lightningTotalDuration : _lightningDuration;
                if (_lightningElapsed >= totalDur) _lightningElapsed = -1f;
            }
            else if (Time.time >= _nextLightningTime)
            {
                _lightningElapsed = 0f;
                _nextLightningTime = Time.time + UnityEngine.Random.Range(_lightningInterval.x, _lightningInterval.y);
                if (_lightningDrawBolt) GenerateLightningBolts();
            }
        }
    }

    /// <summary>
    /// 번개 한 번 트리거 시 여러 줄기를 약간의 시차로 생성.
    /// _lightningBoltsPerStrike에 따라 2~4개가 동시/연쇄로 등장.
    /// </summary>
    private void GenerateLightningBolts()
    {
        _bolts.Clear();
        int count = Mathf.Max(1, Mathf.RoundToInt(UnityEngine.Random.Range(_lightningBoltsPerStrike.x, _lightningBoltsPerStrike.y)));

        // 순차 배치 — 각 줄기가 앞 줄기의 fade가 거의 끝날 때쯤 등장해서 동시 출현 X.
        float nextOffset = 0f;
        for (int i = 0; i < count; i++)
        {
            var inst = new BoltInstance
            {
                startOffset = nextOffset,
                randomSeed = UnityEngine.Random.value * 100f,
            };
            GenerateBoltPath(ref inst);
            _bolts.Add(inst);
            // 앞 줄기 duration의 70~95% 지나서 다음이 등장 → 거의 안 겹침
            nextOffset += _lightningDuration * UnityEngine.Random.Range(0.7f, 0.95f);
        }

        // 마지막 줄기까지 완전히 보여지고 끝나려면 최종 시간은 lastOffset + duration
        _lightningTotalDuration = _bolts[_bolts.Count - 1].startOffset + _lightningDuration;
    }

    private void GenerateBoltPath(ref BoltInstance inst)
    {
        // 메인 줄기
        int segments = UnityEngine.Random.Range(9, 14);
        inst.path = new Vector2[segments + 1];
        float startX = UnityEngine.Random.Range(Screen.width * 0.1f, Screen.width * 0.9f);
        float lengthRatio = UnityEngine.Random.Range(_lightningBoltLength.x, _lightningBoltLength.y);
        float endY = Screen.height * lengthRatio;
        float segLen = endY / segments;

        Vector2 cur = new Vector2(startX, 0f);
        inst.path[0] = cur;
        for (int i = 1; i <= segments; i++)
        {
            cur.y += segLen;
            cur.x += UnityEngine.Random.Range(-30f, 30f);
            inst.path[i] = cur;
        }

        // 가지
        int branches = Mathf.Max(0, _lightningBranchCount);
        inst.branches = new Vector2[branches][];
        for (int b = 0; b < branches; b++)
        {
            int splitIdx = UnityEngine.Random.Range(2, segments - 1);
            int branchSegs = UnityEngine.Random.Range(3, 6);
            var branch = new Vector2[branchSegs + 1];
            Vector2 bcur = inst.path[splitIdx];
            branch[0] = bcur;
            float bdir = UnityEngine.Random.value < 0.5f ? -1f : 1f;
            for (int j = 1; j <= branchSegs; j++)
            {
                bcur.x += bdir * UnityEngine.Random.Range(15f, 35f) + UnityEngine.Random.Range(-10f, 10f);
                bcur.y += UnityEngine.Random.Range(20f, 40f);
                branch[j] = bcur;
            }
            inst.branches[b] = branch;
        }
    }

    /// <summary>
    /// 단일 번개 인스턴스를 reveal 진행도에 따라 그림.
    /// 텍스처 지정 시 스프라이트시트 애니, 아니면 절차적 지그재그.
    /// </summary>
    private void DrawLightningBoltProgressive(BoltInstance inst, float boltElapsed, float alpha, float revealT)
    {
        if (_lightningBoltTexture != null)
        {
            DrawLightningBoltFromTexture(inst, boltElapsed, alpha, revealT);
            return;
        }

        if (inst.path == null || inst.path.Length < 2) return;

        var prev = GUI.color;

        int mainSegs = inst.path.Length - 1;
        float revealSegsF = mainSegs * Mathf.Clamp01(revealT);
        int fullSegs = Mathf.FloorToInt(revealSegsF);
        float partialFrac = revealSegsF - fullSegs;

        float glowThick = _lightningBoltThickness * 4f;

        // Pass 1: 외곽 글로우
        GUI.color = new Color(_lightningColor.r, _lightningColor.g, _lightningColor.b, alpha * 0.35f);
        DrawPartialJaggedLine(inst.path, fullSegs, partialFrac, glowThick);

        // Pass 2: 코어
        GUI.color = new Color(1f, 1f, 1f, alpha);
        DrawPartialJaggedLine(inst.path, fullSegs, partialFrac, _lightningBoltThickness);

        // 가지
        if (revealT > 0.85f && inst.branches != null)
        {
            float branchAlpha = alpha * Mathf.Clamp01((revealT - 0.85f) / 0.15f);
            GUI.color = new Color(_lightningColor.r, _lightningColor.g, _lightningColor.b, branchAlpha * 0.35f);
            for (int b = 0; b < inst.branches.Length; b++)
                DrawJaggedLine(inst.branches[b], glowThick * 0.7f);
            GUI.color = new Color(1f, 1f, 1f, branchAlpha);
            for (int b = 0; b < inst.branches.Length; b++)
                DrawJaggedLine(inst.branches[b], _lightningBoltThickness * 0.7f);
        }

        GUI.color = prev;
    }

    /// <summary>
    /// 스프라이트시트 번개 텍스처로 번개 그리기.
    /// 텍스처는 원래 가로 아크(예: 1024x256, 4프레임)라 -90° 회전으로 세로로 떨어지는 번개처럼 보이게.
    /// revealT에 따라 상단부터 내려가며 길이가 늘어나고, 스프라이트시트 프레임은 독립적으로 순환.
    /// </summary>
    private void DrawLightningBoltFromTexture(BoltInstance inst, float boltElapsed, float alpha, float revealT)
    {
        if (_lightningBoltTexture == null) return;

        // 프레임 선택 — 인스턴스별 seed로 시작 phase 달라지게
        int cols = Mathf.Max(1, _lightningBoltColumns);
        int rows = Mathf.Max(1, _lightningBoltRows);
        int totalFrames = cols * rows;
        int frameIdx = Mathf.FloorToInt(boltElapsed * _lightningBoltFps + inst.randomSeed) % totalFrames;
        if (frameIdx < 0) frameIdx += totalFrames;
        int col = frameIdx % cols;
        int row = frameIdx / cols;
        float uvW = 1f / cols;
        float uvH = 1f / rows;
        float uvX = col * uvW;
        float uvY = 1f - (row + 1) * uvH;
        Rect uvRect = new Rect(uvX, uvY, uvW, uvH);

        // 번개 시작 지점 — 이 인스턴스의 path 첫 점
        float startX = (inst.path != null && inst.path.Length > 0) ? inst.path[0].x : Screen.width * 0.5f;
        float maxLen = (inst.path != null && inst.path.Length > 0)
            ? inst.path[inst.path.Length - 1].y
            : Screen.height * _lightningBoltLength.y;
        float boltLen = maxLen * Mathf.Clamp01(revealT);
        float boltThickness = Screen.height * _lightningBoltWidth;

        if (boltLen < 1f) return;

        var prev = GUI.color;
        var prevMatrix = GUI.matrix;
        GUI.color = new Color(1f, 1f, 1f, alpha);

        // 가로로 긴 사각형을 (startX, 0) 기준 90° CW 회전 → 아래로 뻗어나감
        GUIUtility.RotateAroundPivot(90f, new Vector2(startX, 0f));
        GUI.DrawTextureWithTexCoords(
            new Rect(startX, -boltThickness * 0.5f, boltLen, boltThickness),
            _lightningBoltTexture,
            uvRect);

        GUI.matrix = prevMatrix;
        GUI.color = prev;
    }

    private void DrawPartialJaggedLine(Vector2[] path, int fullSegs, float partialFrac, float thickness)
    {
        // 완전한 세그먼트
        for (int i = 1; i <= fullSegs && i < path.Length; i++)
            DrawLineGUI(path[i - 1], path[i], thickness);
        // 마지막 부분 세그먼트 (보간)
        int nextIdx = fullSegs + 1;
        if (nextIdx < path.Length && partialFrac > 0.001f)
        {
            Vector2 a = path[fullSegs];
            Vector2 b = path[nextIdx];
            Vector2 partial = Vector2.Lerp(a, b, partialFrac);
            DrawLineGUI(a, partial, thickness);
        }
    }

    private void DrawJaggedLine(Vector2[] path, float thickness)
    {
        for (int i = 1; i < path.Length; i++)
            DrawLineGUI(path[i - 1], path[i], thickness);
    }

    /// <summary>
    /// OnGUI에서 두 점 사이에 두께 있는 선 그리기. RotateAroundPivot로 사각형을 회전.
    /// </summary>
    private static void DrawLineGUI(Vector2 a, Vector2 b, float thickness)
    {
        Vector2 dir = b - a;
        float len = dir.magnitude;
        if (len < 0.5f) return;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        Matrix4x4 prevMatrix = GUI.matrix;
        GUIUtility.RotateAroundPivot(angle, a);
        GUI.DrawTexture(new Rect(a.x, a.y - thickness * 0.5f, len, thickness), Texture2D.whiteTexture);
        GUI.matrix = prevMatrix;
    }

    /// <summary>
    /// 번개 깜빡임 강도 (0~1). elapsed = 0~1 정규화 시간.
    /// 진짜 번개처럼 초반에 가장 강한 피크 → 빠른 깜빡임 → 느린 페이드.
    /// 3개의 피크 (1.0 → 0.7 → 0.4) 이후 0으로 수렴.
    /// </summary>
    private static float ComputeLightningIntensity(float elapsed)
    {
        // elapsed [0..1]
        if (elapsed < 0f || elapsed > 1f) return 0f;

        // 첫 피크: 0~0.06에서 0→1, 0.06~0.12에서 1→0.15
        if (elapsed < 0.06f) return Mathf.Lerp(0f, 1f, elapsed / 0.06f);
        if (elapsed < 0.12f) return Mathf.Lerp(1f, 0.15f, (elapsed - 0.06f) / 0.06f);
        // 짧은 휴지
        if (elapsed < 0.16f) return 0.15f;
        // 두 번째 피크: 0.16~0.20에서 0.15→0.7, 0.20~0.28에서 0.7→0.1
        if (elapsed < 0.20f) return Mathf.Lerp(0.15f, 0.7f, (elapsed - 0.16f) / 0.04f);
        if (elapsed < 0.28f) return Mathf.Lerp(0.7f, 0.1f, (elapsed - 0.20f) / 0.08f);
        // 세 번째 피크 (작음): 0.32~0.36 0.1→0.4, 0.36~0.50 0.4→0
        if (elapsed < 0.32f) return 0.1f;
        if (elapsed < 0.36f) return Mathf.Lerp(0.1f, 0.4f, (elapsed - 0.32f) / 0.04f);
        if (elapsed < 0.50f) return Mathf.Lerp(0.4f, 0f, (elapsed - 0.36f) / 0.14f);
        // 잔향 페이드
        if (elapsed < 1.0f) return 0f;
        return 0f;
    }

    private void DrawAtmosphereFX()
    {
        if (!_fxInitialized) return;
        if (_glowDot == null) return;

        // 1) 안개 — 기본은 캐릭터 뒤에. _mistInForeground가 true면 여기선 스킵하고 Foreground에서 그림.
        if (!_mistInForeground) DrawMistBlobs();

        // 2) 번개 — 캐릭터 뒤에 그려야 자연스러움
        DrawLightning();
    }

    private void DrawMistBlobs()
    {
        if (!_enableMist || _mistBlobs == null || _mistBlob == null) return;
        var prev = GUI.color;
        for (int i = 0; i < _mistBlobs.Length; i++)
        {
            var b = _mistBlobs[i];
            GUI.color = new Color(_mistTint.r, _mistTint.g, _mistTint.b, b.alpha);
            GUI.DrawTexture(
                new Rect(b.x - b.w * 0.5f, b.y - b.h * 0.5f, b.w, b.h),
                _mistBlob, ScaleMode.StretchToFill, alphaBlend: true);
        }
        GUI.color = prev;
    }

    private void DrawAtmosphereFXForeground()
    {
        if (!_fxInitialized) return;
        if (_glowDot == null) return;

        // 0) 안개 — 캐릭터 앞에 그릴 옵션이면 여기서
        if (_mistInForeground) DrawMistBlobs();

        // 2) 반딧불이 — 캐릭터 앞쪽에
        if (_enableFireflies && _fireflies != null)
        {
            var prev = GUI.color;
            for (int i = 0; i < _fireflies.Length; i++)
            {
                var f = _fireflies[i];
                float pulse = 0.6f + 0.4f * Mathf.Sin(f.phase);
                float y = f.pos.y + f.bobAmp * Mathf.Sin(f.phase);
                float a = f.baseAlpha * pulse;

                // 외곽 글로우 (큰 사이즈, 낮은 알파)
                GUI.color = new Color(f.tint.r, f.tint.g, f.tint.b, a * 0.35f);
                float gs = f.size * _fireflyGlowSize;
                GUI.DrawTexture(new Rect(f.pos.x - gs * 0.5f, y - gs * 0.5f, gs, gs),
                    _glowDot, ScaleMode.StretchToFill, alphaBlend: true);

                // 중심 코어
                GUI.color = new Color(f.tint.r, f.tint.g, f.tint.b, a);
                GUI.DrawTexture(new Rect(f.pos.x - f.size * 0.5f, y - f.size * 0.5f, f.size, f.size),
                    _glowDot, ScaleMode.StretchToFill, alphaBlend: true);
            }
            GUI.color = prev;
        }

        // 3) 잿가루
        if (_enableAsh && _ashes != null)
        {
            var prev = GUI.color;
            for (int i = 0; i < _ashes.Length; i++)
            {
                var a = _ashes[i];
                GUI.color = new Color(_ashTint.r, _ashTint.g, _ashTint.b, a.alpha);
                GUI.DrawTexture(new Rect(a.pos.x - a.size * 0.5f, a.pos.y - a.size * 0.5f, a.size, a.size),
                    _glowDot, ScaleMode.StretchToFill, alphaBlend: true);
            }
            GUI.color = prev;
        }

        // 3-b) 바닥에서 올라오는 먼지 — LobbyUI Bottom Smoke와 동일 공식 (Hash01 기반 인덱스, 3단 레이어)
        // LobbyUI는 1280x720 가상 좌표계에서 그리고 matrix로 스케일하므로, 동일 비율을 위해 scale 팩터를 곱함.
        if (_enableDust && _dustReady && _dustCount > 0)
        {
            var prev = GUI.color;
            float t = Time.unscaledTime;
            float dustScale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            int seedOffset = 500; // LobbyUI Bottom Smoke seedOffset
            float spawnX = Screen.width * _dustXRange.x;
            float spawnW = Screen.width * (_dustXRange.y - _dustXRange.x);
            float spawnY = Screen.height * _dustYRange.x;
            float spawnH = Screen.height * (_dustYRange.y - _dustYRange.x);
            for (int i = 0; i < _dustCount; i++)
            {
                int idx = i + seedOffset;
                float seed = DustHash01(idx * 0.6180339f + 0.13f);
                float speed = _dustRiseSpeed * (0.75f + seed * 0.6f);
                float phase = seed * 7.13f;
                float life = ((t * speed) + phase) % 1f;
                if (life < 0f) life += 1f;

                float spawnU = DustHash01(idx * 12.9898f);
                float spawnV = DustHash01(idx * 78.233f);
                float sway = Mathf.Sin(life * Mathf.PI * 2f * _dustSwayFrequency + seed * 6f) * _dustSwayAmount * dustScale;

                float centerX = spawnX + spawnW * 0.5f;
                float px = centerX + (spawnU - 0.5f) * spawnW + sway;
                float py = spawnY + spawnV * spawnH - life * _dustRiseHeight * dustScale;

                float sizeT = Mathf.Sin(life * Mathf.PI);
                float baseSize = Mathf.Lerp(_dustSize.x, _dustSize.y, DustHash01(idx * 37.719f));
                float size = baseSize * (0.45f + 0.55f * sizeT) * dustScale;

                float fade = Mathf.Sin(life * Mathf.PI);
                float flicker = (1f - _dustFlickerDepth) + _dustFlickerDepth * Mathf.Sin(t * _dustFlickerSpeed + seed * 17f);
                float a = Mathf.Clamp01(fade * flicker) * _dustAlphaMul;

                // 1) 바깥 블룸 — 크고 흐리게
                float bloomSize = size * _dustBloomScale;
                GUI.color = new Color(_dustOuterTint.r, _dustOuterTint.g, _dustOuterTint.b,
                    _dustOuterTint.a * a * _dustBloomAlphaMul);
                GUI.DrawTexture(new Rect(px - bloomSize * 0.5f, py - bloomSize * 0.5f, bloomSize, bloomSize),
                    _dustBlob, ScaleMode.StretchToFill, alphaBlend: true);

                // 2) 미드 글로우 (아웃터 컬러)
                float glowSize = size * 1.6f;
                GUI.color = new Color(_dustOuterTint.r, _dustOuterTint.g, _dustOuterTint.b,
                    _dustOuterTint.a * a * 0.7f);
                GUI.DrawTexture(new Rect(px - glowSize * 0.5f, py - glowSize * 0.5f, glowSize, glowSize),
                    _dustBlob, ScaleMode.StretchToFill, alphaBlend: true);

                // 3) 안쪽 코어 (이너 컬러)
                GUI.color = new Color(_dustTint.r, _dustTint.g, _dustTint.b, _dustTint.a * a);
                GUI.DrawTexture(new Rect(px - size * 0.5f, py - size * 0.5f, size, size),
                    _dustBlob, ScaleMode.StretchToFill, alphaBlend: true);
            }
            GUI.color = prev;
        }

        // 번개는 이제 DrawLightning()으로 분리 — 캐릭터 뒤로 그리기 위해 DrawAtmosphereFX에서 호출.
    }

    /// <summary>
    /// 번개 플래시 + 줄기 그리기. 캐릭터 뒤에 와야 자연스러우므로 DrawAtmosphereFX에서 호출.
    /// </summary>
    private void DrawLightning()
    {
        if (!_enableLightning || _lightningElapsed < 0f) return;

        float t = _lightningElapsed / _lightningDuration;
        float intensity = ComputeLightningIntensity(t);

        // 1. 하늘 그라데이션 플래시 — 알파가 0이면 그리지 않음
        if (intensity > 0.001f && _lightningPeakAlpha > 0.001f)
        {
            float skyH = Screen.height * _lightningSkyHeight;
            float a = intensity * _lightningPeakAlpha;
            var prev = GUI.color;
            GUI.color = new Color(_lightningColor.r, _lightningColor.g, _lightningColor.b, a);
            if (_skyGradient != null)
            {
                GUI.DrawTexture(
                    new Rect(0, 0, Screen.width, skyH),
                    _skyGradient,
                    ScaleMode.StretchToFill,
                    alphaBlend: true);
            }
            GUI.color = prev;
        }

        // 2. 번개 줄기 — 여러 인스턴스가 각자 startOffset만큼 지연된 후 등장.
        if (_lightningDrawBolt && _bolts.Count > 0)
        {
            const float revealDur = 0.10f;
            float fadeDur = _lightningDuration * _lightningBoltFadeRatio;
            float holdEnd = _lightningDuration - fadeDur;

            for (int i = 0; i < _bolts.Count; i++)
            {
                var inst = _bolts[i];
                float boltElapsed = _lightningElapsed - inst.startOffset;
                if (boltElapsed < 0f) continue;
                if (boltElapsed > _lightningDuration) continue;

                float revealT = Mathf.Clamp01(boltElapsed / revealDur);
                float boltAlpha;
                if (boltElapsed < holdEnd)
                {
                    boltAlpha = 1f;
                }
                else
                {
                    // SmoothStep 페이드 — S-곡선으로 부드럽게 (양 끝이 완만)
                    float fadeT = (boltElapsed - holdEnd) / fadeDur;
                    boltAlpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(fadeT));
                }

                // hold 구간에서만 미묘한 shimmer (너무 강하면 끊기는 느낌)
                if (boltElapsed < holdEnd)
                {
                    float shimmer = 0.92f + 0.08f * Mathf.Sin((boltElapsed + inst.randomSeed) * 18f);
                    boltAlpha *= shimmer;
                }

                // 전체 최대 밝기 제한
                boltAlpha *= _lightningBoltMaxAlpha;

                if (boltAlpha > 0.01f)
                    DrawLightningBoltProgressive(inst, boltElapsed, boltAlpha, revealT);
            }
        }
    }

    /// <summary>
    /// 구름 레이어를 배경 위에 가로 스크롤로 그림.
    /// 텍스처를 화면 폭에 맞춰 두 장 나란히 배치하고 _cloudOffset만큼 좌측으로 밀어
    /// 무한 wrap-around. 좌우 끝이 완벽히 이어지지 않아도 느린 속도면 거의 안 보임.
    /// </summary>
    private void DrawCloudsOverlay()
    {
        // override가 우선, 없으면 기존 Resources 텍스처
        Texture2D tex = _cloudTextureOverride != null ? _cloudTextureOverride : _cloudsTexture;
        if (tex == null) return;

        // 스프라이트시트 프레임 — _cloudColumns/_cloudRows > 1이면 애니메이션
        int cols = Mathf.Max(1, _cloudColumns);
        int rows = Mathf.Max(1, _cloudRows);
        int totalFrames = cols * rows;
        int frameIdx = totalFrames > 1
            ? Mathf.FloorToInt(Time.time * _cloudFps) % totalFrames
            : 0;
        int col = frameIdx % cols;
        int row = frameIdx / cols;
        float uvW = 1f / cols;
        float uvH = 1f / rows;
        float uvX = col * uvW;
        float uvY = 1f - (row + 1) * uvH;
        Rect uvRect = new Rect(uvX, uvY, uvW, uvH);

        // 한 프레임의 비율로 가로폭 계산 (sprite sheet 고려)
        float frameW = tex.width / (float)cols;
        float frameH = tex.height / (float)rows;
        float frameAspect = frameW / frameH;

        float drawH = Screen.height * _cloudHeightRatio;
        float drawW = drawH * frameAspect;

        // 한 장이 화면보다 좁으면 화면 폭에 맞춰 늘림 (퍼프 텍스처는 보통 정사각형이라 늘어남)
        if (drawW < Screen.width) drawW = Screen.width;

        float scrollPx = _cloudOffset * (drawW / Mathf.Max(1f, frameW));
        scrollPx %= drawW;

        var prev = GUI.color;
        GUI.color = new Color(_cloudTint.r, _cloudTint.g, _cloudTint.b, _cloudAlpha);

        // 좌측 본체 + 우측 보조 (wrap-around)
        GUI.DrawTextureWithTexCoords(new Rect(-scrollPx, 0, drawW, drawH), tex, uvRect);
        GUI.DrawTextureWithTexCoords(new Rect(drawW - scrollPx, 0, drawW, drawH), tex, uvRect);

        GUI.color = prev;
    }

    /// <summary>
    /// 캐릭터(투명 배경)를 배경 위에 오버레이.
    /// 화면 우측 하단 기준으로 배치하며 Inspector에서 위치/크기 튜닝 가능.
    /// 추후 본 리깅이 들어가면 여기서 그리는 대신 Sprite/Animator로 교체.
    /// </summary>
    private void DrawCharacterOverlay()
    {
        // 시퀀스가 있으면 시간 기반으로 프레임 선택, 없으면 정적 텍스처
        Texture2D charTex = null;
        if (_characterFrames != null && _characterFrames.Length > 0)
        {
            int idx = Mathf.FloorToInt(Time.time * _characterFps) % _characterFrames.Length;
            if (idx < 0) idx += _characterFrames.Length;
            charTex = _characterFrames[idx];
        }
        if (charTex == null) charTex = _characterTexture;
        if (charTex == null) return;

        float texAspect = (float)charTex.width / charTex.height;

        // 호흡 — Y만 살짝 늘림(가슴이 위로 부푸는 느낌). 가로는 고정해야 균일 스케일의 "부풀어오름"이 안 생김.
        // ease curve로 들숨/날숨이 천천히 머무르게 → 펌핑이 아닌 호흡 리듬.
        float charBreath = 1f;
        if (_enableCharBreathing)
        {
            float t = Time.time * Mathf.PI * 2f * _charBreathingFreq + _charBreathingPhase;
            float rawSin = Mathf.Sin(t);
            // smoothstep 같은 곡선으로 부드럽게 (피크/저점에서 잠깐 머무름)
            float eased = rawSin * rawSin * Mathf.Sign(rawSin);
            charBreath = 1f + eased * _charBreathingAmp;
        }
        float baseH = Screen.height * _characterHeightRatio;
        float drawH = baseH * charBreath;   // Y 스케일
        float drawW = baseH * texAspect;    // X 고정 (호흡 영향 X)

        // 발 끝 위치 = 화면 하단에서 anchorBottom 비율만큼 위
        float footY = Screen.height * (1f - _characterAnchorBottom);
        float topY = footY - drawH;

        // 가로 중심 위치
        float centerX = Screen.width * _characterAnchorX;
        float leftX = centerX - drawW * 0.5f;

        // Parallax — 캐릭터는 BG보다 더 크게 움직여 깊이감.
        float parX = -_parallaxOffset.x * _charParallaxStrength;
        float parY = _parallaxOffset.y * _charParallaxStrength; // Y-up → Y-down 부호 조정
        leftX += parX;
        topY += parY;
        centerX += parX;
        footY += parY;

        // 발밑 그림자 — 캐릭터 뒤에 먼저 그려 그라운딩.
        if (_characterShadowTexture != null && _shadowHeightRatio > 0.0001f)
        {
            float shadowAspect = (float)_characterShadowTexture.width / _characterShadowTexture.height;
            float shadowH = drawH * _shadowHeightRatio;
            float shadowW = shadowH * shadowAspect * _shadowWidthScale;
            var shadowRect = new Rect(
                centerX - shadowW * 0.5f,
                footY - shadowH * 0.5f + _shadowYOffset,
                shadowW, shadowH);

            var prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, _shadowAlpha);
            GUI.DrawTexture(shadowRect, _characterShadowTexture, ScaleMode.StretchToFill, alphaBlend: true);
            GUI.color = prev;
        }

        GUI.DrawTexture(
            new Rect(leftX, topY, drawW, drawH),
            charTex,
            ScaleMode.StretchToFill,
            alphaBlend: true);
    }

    // =========================================================
    // Info panel (좌상단)
    // =========================================================

    private void DrawInfoPanel()
    {
        DrawRoundedRect(InfoPanelRect, 32f, new Color(0f, 0f, 0f, 0.55f));

        // 프레임 안쪽 패딩 (테두리 두께 고려)
        const float padX = 50;
        const float padTop = 38;
        var inner = new Rect(
            InfoPanelRect.x + padX,
            InfoPanelRect.y + padTop,
            InfoPanelRect.width - padX * 2,
            InfoPanelRect.height - padTop - 30);

        float y = inner.y;

        var ch = _selectedCharacter;

        // 타이틀 — character.name_en
        string titleText = ch != null ? ch.nameEn : "";
        GUI.Label(new Rect(inner.x, y, inner.width, 44), titleText, _titleStyle);
        y += 50;

        // HP / Gold — 아이콘 + 숫자 (세로 중앙 정렬)
        const float iconSize = 36f;
        const float iconTextGap = 10f;
        const float groupGap = 32f;
        float rowY = y;

        // 하트 아이콘 + HP (연한 빨강)
        var heartRect = new Rect(inner.x, rowY, iconSize, iconSize);
        if (_iconHeart != null)
            GUI.DrawTexture(heartRect, _iconHeart, ScaleMode.ScaleToFit, alphaBlend: true);

        string hpText = ch != null ? $"{ch.maxHp}/{ch.maxHp}" : "";
        float hpTextW = _hpStyleMid.CalcSize(new GUIContent(hpText)).x;
        var hpTextRect = new Rect(heartRect.xMax + iconTextGap, rowY, hpTextW, iconSize);
        GUI.Label(hpTextRect, hpText, _hpStyleMid);

        // 코인 아이콘 + Gold (연한 노랑)
        var coinRect = new Rect(hpTextRect.xMax + groupGap, rowY, iconSize, iconSize);
        if (_iconCoin != null)
            GUI.DrawTexture(coinRect, _iconCoin, ScaleMode.ScaleToFit, alphaBlend: true);

        string goldText = ch != null ? ch.startGold.ToString() : "";
        float goldTextW = _goldStyleMid.CalcSize(new GUIContent(goldText)).x;
        var goldTextRect = new Rect(coinRect.xMax + iconTextGap, rowY, goldTextW, iconSize);
        GUI.Label(goldTextRect, goldText, _goldStyleMid);

        y += iconSize + 16f;

        // 설명 — character.description (CSV 줄바꿈은 \n 이스케이프로 저장됨)
        string descText = ch != null ? ch.description.Replace("\\n", "\n") : "";
        float descH = _descStyle.CalcHeight(new GUIContent(descText), inner.width);
        GUI.Label(new Rect(inner.x, y, inner.width, descH), descText, _descStyle);
        y += descH + 10;

        // 패시브 — 아이콘 + (이름 / 설명) 가로 배치
        const float passiveIconSize = 52f;
        const float passiveTextGap = 12f;

        var passiveIconRect = new Rect(inner.x, y, passiveIconSize, passiveIconSize);
        if (_iconPassive != null)
            GUI.DrawTexture(passiveIconRect, _iconPassive, ScaleMode.ScaleToFit, alphaBlend: true);

        float textX = passiveIconRect.xMax + passiveTextGap;
        float textW = inner.xMax - textX;

        var abilityNameContent = new GUIContent(ch != null ? ch.passiveName : "");
        float abilityNameH = _abilityNameStyle.CalcHeight(abilityNameContent, textW);
        GUI.Label(new Rect(textX, y, textW, abilityNameH), abilityNameContent, _abilityNameStyle);

        string abilityDescText = ch != null ? ch.passiveDescription.Replace("\\n", "\n") : "";
        float abilityDescH = _abilityDescStyle.CalcHeight(new GUIContent(abilityDescText), textW);
        GUI.Label(new Rect(textX, y + abilityNameH + 2, textW, abilityDescH), abilityDescText, _abilityDescStyle);
    }

    // =========================================================
    // Card row (하단)
    // =========================================================

    private void DrawCardRow()
    {
        // 모든 슬롯에 동일 CardFrameBase(황금 틴트) 오버레이. 펄스 애니메이션 없음 (정적).
        // AvailableCharacterIds[i]에 해당하는 슬롯은 초상화, 나머지는 단색 패널.
        // 마우스 호버 시 _cardHoverScale 배율로 살짝 커짐 (클릭 영역은 원본 유지).
        Vector2 mousePos = Event.current.mousePosition;
        for (int i = 0; i < CardRects.Length; i++)
        {
            Rect baseRect = CardRects[i];
            bool hovered = baseRect.Contains(mousePos);
            Rect slotRect = hovered ? ScaleRectFromCenter(baseRect, _cardHoverScale) : baseRect;
            bool isAvailable = i < AvailableCharacterIds.Length;

            // 1) 내부 채우기 — 있으면 초상화, 없으면 회색 패널 + "?"
            if (isAvailable && _archaeologistCardPortrait != null)
            {
                GUI.DrawTexture(
                    slotRect, _archaeologistCardPortrait,
                    ScaleMode.ScaleAndCrop, alphaBlend: true);
            }
            else
            {
                if (_emptySlotGradient == null)
                    _emptySlotGradient = GenerateEmptySlotGradient(_emptySlotFill, 64);
                GUI.DrawTexture(slotRect, _emptySlotGradient, ScaleMode.StretchToFill, alphaBlend: true);
                GUI.Label(slotRect, "?", _questionStyle);
            }

            // 2) CardFrameBase 오버레이 — _frameScale 배율 적용, 황금 틴트
            if (_cardFrameBase != null)
            {
                Rect frameRect = ScaleRectFromCenter(slotRect, _frameScale);
                var prev = GUI.color;

                // 2-a) 8방향 오프셋으로 검정 아웃라인(볼더) 먼저 깔기
                if (_frameOutlineThickness > 0f)
                {
                    GUI.color = _frameOutlineColor;
                    float t = _frameOutlineThickness;
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        for (int oy = -1; oy <= 1; oy++)
                        {
                            if (ox == 0 && oy == 0) continue;
                            var offsetRect = new Rect(frameRect.x + ox * t, frameRect.y + oy * t, frameRect.width, frameRect.height);
                            GUI.DrawTexture(offsetRect, _cardFrameBase, ScaleMode.StretchToFill, alphaBlend: true);
                        }
                    }
                }

                // 2-b) 노란 프레임을 위에 얹기
                GUI.color = _frameTint;
                GUI.DrawTexture(frameRect, _cardFrameBase, ScaleMode.StretchToFill, alphaBlend: true);
                GUI.color = prev;
            }
        }

        // 클릭 처리 — 잠긴 슬롯은 Coming Soon, 해금된 슬롯은 이미 선택 상태라 무동작.
        var ev = Event.current;
        if (ev.type == EventType.MouseDown && ev.button == 0)
        {
            for (int i = 0; i < CardRects.Length; i++)
            {
                if (!CardRects[i].Contains(ev.mousePosition)) continue;
                ev.Use();
                if (i >= AvailableCharacterIds.Length)
                {
                    _comingSoonTimer = 1.5f;
                    return;
                }
                string slotCharId = AvailableCharacterIds[i];
                if (slotCharId != _selectedCharacterId)
                    SwitchSelection(slotCharId);
                return;
            }
        }
    }

    /// <summary>선택 캐릭터 변경 — 데이터 + 포트레이트 재로드.</summary>
    private void SwitchSelection(string characterId)
    {
        _selectedCharacterId = characterId;
        _selectedCharacter = DataManager.Instance.GetCharacter(characterId);
        string portraitName = _selectedCharacter != null ? _selectedCharacter.cardPortrait : null;
        if (!string.IsNullOrEmpty(portraitName))
            _archaeologistCardPortrait = Resources.Load<Texture2D>("Character_select/" + portraitName);
    }

    // =========================================================
    // Buttons (좌하/우하)
    // =========================================================

    private void DrawButtons(GameStateManager gsm)
    {
        // Back / Confirm 버튼 — 호버 시 살짝 커지는 효과
        if (_buttonBack != null)
        {
            DrawScalableTexture(BackButtonRect, _buttonBack, ScaleMode.ScaleToFit);
        }

        if (_buttonConfirm != null)
        {
            DrawScalableTexture(ConfirmButtonRect, _buttonConfirm, ScaleMode.ScaleToFit);
        }

        var ev = Event.current;
        if (ev.type != EventType.MouseDown || ev.button != 0) return;

        if (BackButtonRect.Contains(ev.mousePosition))
        {
            ev.Use();
            _pending.Add(() => gsm.ReturnToLobby());
            return;
        }

        if (ConfirmButtonRect.Contains(ev.mousePosition))
        {
            ev.Use();
            string chosen = _selectedCharacterId;
            _pending.Add(() => gsm.ConfirmCharacterSelection(chosen));
        }
    }

    /// <summary>
    /// 마우스가 rect 위에 있으면 약간 커진 영역에 텍스처를 그림.
    /// 클릭 감지는 호출 측에서 원래 rect 기준으로 별도 처리.
    /// </summary>
    private void DrawScalableTexture(Rect rect, Texture2D tex, ScaleMode scaleMode)
    {
        const float HoverScale = 1.08f;

        bool hovered = rect.Contains(Event.current.mousePosition);
        Rect drawRect = rect;

        if (hovered)
        {
            float dw = rect.width * (HoverScale - 1f);
            float dh = rect.height * (HoverScale - 1f);
            drawRect = new Rect(
                rect.x - dw / 2f,
                rect.y - dh / 2f,
                rect.width + dw,
                rect.height + dh);
        }

        GUI.DrawTexture(drawRect, tex, scaleMode, alphaBlend: true);
    }

    private void DrawComingSoonOverlay()
    {
        if (_comingSoonTimer <= 0) return;

        // 다크판타지 톤: 박스 없이 가운데 비문 같은 텍스트만, 위아래 가는 황금 라인.
        // 페이드 인 빠르고 페이드 아웃 천천히.
        const float TotalLife = 1.5f;
        float t = _comingSoonTimer / TotalLife;
        float alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t < 0.85f ? t / 0.85f : 1f));

        // 텍스트가 위로 살짝 떠오르는 모션 (0~6px)
        float floatOffset = (1f - alpha) * -6f;

        var labelRect = new Rect(RefW / 2 - 240, RefH / 2 - 24 + floatOffset, 480, 48);
        var prev = GUI.color;

        // 그림자 (검정, 1px 아래)
        GUI.color = new Color(0f, 0f, 0f, 0.7f * alpha);
        GUI.Label(new Rect(labelRect.x + 2, labelRect.y + 2, labelRect.width, labelRect.height),
                  "ARCANA  SEALED", _comingSoonStyle);

        // 본문 — 흐릿한 황금
        GUI.color = new Color(0.95f, 0.78f, 0.42f, alpha);
        GUI.Label(labelRect, "ARCANA  SEALED", _comingSoonStyle);

        // 위아래 가는 황금 구분선 (텍스트 폭의 절반 정도)
        float lineW = 200f;
        float lineX = RefW / 2 - lineW / 2f;
        GUI.color = new Color(0.85f, 0.7f, 0.35f, alpha * 0.55f);
        GUI.DrawTexture(new Rect(lineX, labelRect.y - 8 + floatOffset, lineW, 1f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(lineX, labelRect.y + labelRect.height + 6 + floatOffset, lineW, 1f), Texture2D.whiteTexture);

        // 부제
        var subRect = new Rect(RefW / 2 - 240, labelRect.y + labelRect.height + 14 + floatOffset, 480, 22);
        GUI.color = new Color(0.78f, 0.78f, 0.78f, alpha * 0.75f);
        GUI.Label(subRect, "The seal has yet to be broken", _slotLabelStyle);

        GUI.color = prev;
    }

    // =========================================================
    // Selected card pulse + glow
    // =========================================================


    // =========================================================
    // Primitive draw helpers
    // =========================================================

    private static void DrawSolidRect(Rect rect, Color color)
    {
        var prev = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = prev;
    }

    // Unity 내장 borderRadiuses 파라미터로 둥근 코너 그리기
    private static void DrawRoundedRect(Rect rect, float radius, Color color)
    {
        GUI.DrawTexture(
            rect,
            Texture2D.whiteTexture,
            ScaleMode.StretchToFill,
            alphaBlend: true,
            imageAspect: 0f,
            color: color,
            borderWidths: Vector4.zero,
            borderRadiuses: new Vector4(radius, radius, radius, radius));
    }

    // =========================================================
    // Styles
    // =========================================================

    private void EnsureStyles()
    {
        if (_stylesReady) return;

        _titleStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFont,
            fontSize = 34,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.85f, 0.45f) },
        };
        _statStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFont,
            fontSize = 24,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white },
        };
        _hpStyleMid = new GUIStyle(_statStyle)
        {
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = new Color(1f, 0.55f, 0.55f) }, // 연한 빨강
        };
        _goldStyleMid = new GUIStyle(_statStyle)
        {
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = new Color(1f, 0.92f, 0.55f) }, // 연한 노랑
        };
        _descStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFont,
            fontSize = 16,
            wordWrap = true,
            normal = { textColor = new Color(1f, 1f, 1f) },
        };
        _abilityNameStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFont,
            fontSize = 20,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.85f, 0.45f) },
        };
        _abilityDescStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFont,
            fontSize = 15,
            wordWrap = true,
            normal = { textColor = new Color(0.96f, 0.96f, 0.96f) },
        };
        _cardNameStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFont,
            fontSize = 16,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.9f, 0.55f) },
        };
        _slotLabelStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFont,
            fontSize = 14,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.85f, 0.85f, 0.85f) },
        };
        _comingSoonStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFont,
            fontSize = 30,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.9f, 0.5f) },
        };
        _questionStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFont,
            fontSize = 48,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.85f, 0.85f, 0.9f, 0.85f) },
        };

        _stylesReady = true;
    }
}
