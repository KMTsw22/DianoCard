using System;
using System.Collections.Generic;
using DianoCard.Data;
using DianoCard.Game;
using UnityEngine;

/// <summary>
/// 노드 선택 맵 화면. GameState == Map일 때만 그려짐.
///
/// Map_Background.png를 배경으로 깔고 그 위에 Node_*.png 스프라이트를
/// 층별 3~5개의 가변 격자로 배치한다. 층 사이는 곡선이 아닌 직선 로프로 연결.
/// </summary>
public class MapUI : MonoBehaviour
{
    // =========================================================
    // Inspector — 범례 패널 튜닝
    // =========================================================
    [Header("Legend Panel — 패널 텍스처만 사용 (라벨 베이크된 이미지)")]
    [Tooltip("범례 패널 표시 여부. 최종 베이크된 패널 이미지 준비되면 켜기.")]
    [SerializeField] private bool legendPanelEnabled = false;
    [Tooltip("범례 패널 전체 불투명도. 0=완전 투명, 1=원본 그대로.")]
    [SerializeField, Range(0f, 1f)] private float legendPanelAlpha = 1f;
    [Tooltip("패널 우측 가장자리에서 화면 우측까지 여백.")]
    [SerializeField] private float legendRightMargin = 18f;
    [Tooltip("패널 상단 Y (1280x720 가상 좌표 기준). HUD 스트립(~74) 아래로 내려야 가려지지 않음.")]
    [SerializeField] private float legendTopY = 95f;
    [Tooltip("패널 폭 (px). 0 이하면 텍스처 원본 비율로 자동 계산.")]
    [SerializeField] private float legendPanelWidth = 220f;
    [Tooltip("패널 높이 (px). 0 이하면 텍스처 원본 비율로 자동 계산.")]
    [SerializeField] private float legendPanelHeight = 380f;

    [Header("Scrollbar (맵 오른쪽/왼쪽 스크롤 위치 표시 — 기본 OFF)")]
    [Tooltip("스크롤바 표시 여부. 기본 OFF — 필요할 때만 켜기.")]
    [SerializeField] private bool scrollbarEnabled = false;
    [Tooltip("true=좌측 배치, false=우측 배치.")]
    [SerializeField] private bool scrollbarOnLeft = true;
    [Tooltip("화면 가장자리에서 스크롤바까지 여백 (px).")]
    [SerializeField, Range(0f, 200f)] private float scrollbarMargin = 40f;
    [Tooltip("스크롤바 상단 Y 위치 (px). HUD(~74) 아래로.")]
    [SerializeField, Range(40f, 400f)] private float scrollbarTopY = 130f;
    [Tooltip("스크롤바 하단까지 내려올 여백 (px). 화면 하단에서 이 값만큼 위에서 끝남.")]
    [SerializeField, Range(40f, 300f)] private float scrollbarBottomMargin = 90f;
    [Tooltip("트랙(배경) 두께 (px).")]
    [SerializeField, Range(4f, 40f)] private float scrollbarTrackWidth = 12f;
    [Tooltip("썸(현재 위치 핸들) 두께 (px). 트랙보다 크게 하면 밖으로 튀어나옴.")]
    [SerializeField, Range(6f, 60f)] private float scrollbarThumbWidth = 18f;
    [Tooltip("썸 최소 높이 (px). 컨텐츠가 짧을 때도 너무 작아지지 않게.")]
    [SerializeField, Range(20f, 200f)] private float scrollbarThumbMinH = 60f;
    [Tooltip("트랙 색 + 알파.")]
    [SerializeField] private Color scrollbarTrackColor = new(0.15f, 0.10f, 0.06f, 0.55f);
    [Tooltip("썸 색 + 알파.")]
    [SerializeField] private Color scrollbarThumbColor = new(0.72f, 0.52f, 0.24f, 0.95f);
    [Tooltip("썸/트랙 테두리(보더) 색 + 알파.")]
    [SerializeField] private Color scrollbarBorderColor = new(0.10f, 0.06f, 0.03f, 1f);
    [Tooltip("테두리 두께 (0=없음).")]
    [SerializeField, Range(0f, 4f)] private float scrollbarBorderThickness = 1.5f;
    [Tooltip("현재 층 정보 라벨 표시 여부 (스크롤바 상단).")]
    [SerializeField] private bool scrollbarFloorLabelEnabled = true;
    [Tooltip("층 라벨 폰트 크기.")]
    [SerializeField, Range(10, 32)] private int scrollbarFloorLabelFontSize = 16;
    [Tooltip("층 라벨 색.")]
    [SerializeField] private Color scrollbarFloorLabelColor = new(0.92f, 0.85f, 0.70f, 1f);

    [Header("Vignette (화면 가장자리 어둡게)")]
    [Tooltip("비네팅 사용 여부. 가장자리를 어둡게 해 시선을 맵 중앙으로 모음.")]
    [SerializeField] private bool vignetteEnabled = true;
    [Tooltip("비네팅 최대 강도 (코너 알파). 0=없음, 1=완전 검정.")]
    [SerializeField, Range(0f, 1f)] private float vignetteStrength = 0.9f;
    [Tooltip("비네팅 시작 반경 (0=중앙부터, 1=가장자리에서만). 작을수록 중앙까지 어두워짐.")]
    [SerializeField, Range(0f, 1f)] private float vignetteInnerRadius = 0.35f;
    [Tooltip("비네팅 색. 검정 외에 짙은 보라 등도 가능.")]
    [SerializeField] private Color vignetteColor = new Color(0f, 0f, 0f, 1f);

    [Header("Chapter Intro — 맵 진입 ACT/챕터명 패널 (런타임 Inspector 조절)")]
    [Tooltip("Play 중 값 바꾸면 즉시 반영. 단, Play 모드 종료 시 값은 리셋되니 마음에 들면 값을 적어놓고 Play 끄고 다시 입력할 것.")]
    [SerializeField] private bool _chapterIntroLiveTuneNote = true;

    [Tooltip("ACT 라벨 폰트 크기.")]
    [SerializeField, Range(14, 60)] private int chapterIntroActFontSize = 28;
    [Tooltip("ACT 라벨 Y 위치 비율 (0=상단, 1=하단). RefH(720) 기준.")]
    [SerializeField, Range(0f, 1f)] private float chapterIntroActYRatio = 0.18f;
    [Tooltip("ACT 라벨 자간(letter-spacing) 시뮬 — 글자 사이 공백 추가. 0=원본.")]
    [SerializeField, Range(0, 8)] private int chapterIntroActSpacing = 4;
    [Tooltip("ACT 라벨 색.")]
    [SerializeField] private Color chapterIntroActColor = new(0.69f, 0.49f, 0.31f, 1f);

    [Tooltip("챕터 제목 폰트 크기. (큰 폰트일수록 chapterIntroTitleHeight도 키워야 잘림 방지.)")]
    [SerializeField, Range(28, 160)] private int chapterIntroTitleFontSize = 84;
    [Tooltip("챕터 제목 Y 위치 비율.")]
    [SerializeField, Range(0f, 1f)] private float chapterIntroTitleYRatio = 0.24f;
    [Tooltip("챕터 제목 컨테이너 높이 (글자 잘림 방지).")]
    [SerializeField, Range(40f, 240f)] private float chapterIntroTitleHeight = 130f;
    [Tooltip("챕터 제목 굵기.")]
    [SerializeField] private FontStyle chapterIntroTitleStyle = FontStyle.Normal;
    [Tooltip("챕터 제목 색.")]
    [SerializeField] private Color chapterIntroTitleColor = new(0.93f, 0.88f, 0.78f, 1f);
    [Tooltip("제목 뒤 그림자 두께(px). 가독성용. 0=없음.")]
    [SerializeField, Range(0f, 8f)] private float chapterIntroTitleShadow = 3f;
    [Tooltip("제목 그림자 색 + 알파.")]
    [SerializeField] private Color chapterIntroTitleShadowColor = new(0f, 0f, 0f, 0.55f);

    [Tooltip("영문 제목용 디스플레이 폰트 경로 (Resources/Fonts/...). 비우면 기본 Cinzel.\n추천: Fonts/Metamorphous-Regular (다크판타지), Fonts/MedievalSharp-Regular (고딕), Fonts/IMFellEnglish-Regular (고서).")]
    [SerializeField] private string chapterIntroDisplayFontPath = "Fonts/Metamorphous-Regular";
    [Tooltip("한글 제목용 폰트 경로. 비우면 기본 Hahmlet.")]
    [SerializeField] private string chapterIntroBodyFontPath = "";

    [Header("Node Layout — StS-style 7컬럼 격자 (양피지 영역에 맞춰 조정)")]
    [Tooltip("7컬럼 좌↔우 총 폭 (px). 화면 폭 1280에서 양옆 패딩을 뺀 값 이하로 설정.")]
    [SerializeField, Range(600f, 1200f)] private float gridSpan7 = 780f;

    [Tooltip("노드 가로 jitter 배율. 1=기본, 0=jitter 없음 (컬럼 정렬 깔끔). 7컬럼은 간격이 좁아 기본값 작게.")]
    [SerializeField, Range(0f, 1.5f)] private float nodeJitterXScale = 0.18f;
    [Tooltip("노드 세로 jitter 배율. NodeJitter 내부에서 50% 노드만 활성, 나머지는 floor Y에 그대로. 0이면 전부 정렬.")]
    [SerializeField, Range(0f, 1.5f)] private float nodeJitterYScale = 0.4f;

    private const int MapWidth = 7;

    // =========================================================
    // 반응형 (AspectScaler) — 실제 화면 비율에 맞춰 자동 확장.
    private static float RefW => DianoCard.UI.AspectScaler.ScreenW;
    private static float RefH => DianoCard.UI.AspectScaler.ScreenH;

    private const float NodeSize = 46f;
    private const float BossSize = 90.18f;
    private const float StartSize = 65.58f;
    private const float HighlightPad = 16f;

    private const float RopeWidth = 6f;
    private const float RopeNodeInset = 3f;
    private const float RopeAlpha = 0.60f;

    // 스크롤 가능한 맵 컨텐츠 영역 (스크린 가상 좌표)
    // 화면 전체를 차지 — 상단 HUD / 하단 버튼은 그 위에 오버레이로 그린다
    private const float MapAreaY = 0f;
    private static float MapAreaH => RefH;  // RefH가 동적이라 const 불가

    // 층 간격 235px — 15층 맵이 화면을 넘어가므로 스크롤로 탐색.
    private const float Floor1Y = 500f;
    private const float FloorSpacing = 235f;
    private const float StartDecoBaseY = 660f;  // floor 1 바로 아래

    // 층별 노드 수에 따른 컬럼 x 좌표를 GetColumnX로 계산. 중심은 항상 640.
    // 3개 → spacing 260, 4개 → 220, 5개 → 200 (화면 가장자리까지 여유 확보).
    private const float MapCenterX = 640f;

    // 보스는 항상 중앙 컬럼
    private const float BossX = 640f;

    // START 장식 노드 위치 — floor 1 바로 아래 중앙 (스크롤에 따라 이동)
    private Vector2 StartDecoPos => new Vector2(640f, StartDecoBaseY + _scrollY);

    private readonly List<Action> _pending = new();

    // 세로 스크롤 (그룹 좌표계 안에서 컨텐츠를 +y 방향으로 밀어내는 양)
    private float _scrollY;
    private int _lastSnappedFloor = -1;

    // 맵 입장 인트로 패닝 — 보스에서 시작지점까지 카메라가 내려오는 연출
    private bool _introPlaying;
    private float _introFrom;
    private float _introTo;
    private float _introStartTime;
    private const float IntroDuration = 2f;

    // 이전 프레임 GameState — Map 진입 감지용
    private GameState _prevGuiState = (GameState)(-1);

    // 인트로를 재생한 MapState — 같은 맵 재진입(전투 후 복귀 등)에서는 인트로 생략
    private MapState _lastIntroMap;

    // 챕터 인트로 패널 — 카메라 팬과 동시 진행 (오버랩). _lastIntroMap 가드를 공유.
    private bool _chapterIntroPlaying;
    private float _chapterIntroStart;
    private const float ChapterIntroFadeIn  = 0.2f;   // 짧게 + ease-out 곡선 → 맵 등장과 동시에 즉시 뿌려짐
    private const float ChapterIntroHold    = 0.9f;
    private const float ChapterIntroFadeOut = 1.4f;
    private const float ChapterIntroTotal   = ChapterIntroFadeIn + ChapterIntroHold + ChapterIntroFadeOut; // 2.5s

    // ember 파티클 — 결정론적 시드, 인트로 첫 시작 시 한 번만 초기화
    private struct EmberParticle
    {
        public float xRef;
        public float yStart;
        public float ySpeed;
        public float xDrift;
        public float size;
        public float phase;
    }
    private EmberParticle[] _emberParticles;

    // 미리보기: 미래 노드 클릭 시 현재 층 → 해당 노드까지의 가능 경로 강조
    private MapNode _previewTarget;
    private readonly HashSet<(int fromFloor, int fromCol, int toCol)> _previewEdges = new();
    // 현재 층에서 실제 도달 가능한 컬럼 집합 — 매 OnGUI마다 재계산. 직전 층 클리어 노드에서 연결된 컬럼만 포함.
    private readonly HashSet<int> _reachableColumns = new();

    private Texture2D _bgTexture;
    private Texture2D _circleTexture;
    private Texture2D _vignetteTex;
    private float _vignetteBakedInner = -1f;

    private Texture2D _nodeCombatTex;
    private Texture2D _nodeEliteTex;
    private Texture2D _nodeBossTex;
    private Texture2D _nodeCampTex;
    private Texture2D _nodeEventTex;
    private Texture2D _nodeMerchantTex;
    private Texture2D _nodeTreasureTex;
    private Texture2D _nodeStartTex;
    private Texture2D _ropeTex;
    private Texture2D _legendPanelTex;
    private Texture2D _backIconTex;

    // 챕터 인트로 — Cinzel(영문 디스플레이) + Hahmlet(한글 본문 명조). 다른 UI와 동일 페어.
    private Font _displayFont;
    private Font _bodyFont;

    // 챕터 인트로 폰트 오버라이드 캐시 — Inspector path 변경 시 lazy 재로드.
    private Font _chapterIntroDisplayFont;
    private Font _chapterIntroBodyFont;
    private string _chapterIntroDisplayFontLoadedPath = "__unloaded__";
    private string _chapterIntroBodyFontLoadedPath = "__unloaded__";

    private GUIStyle _smallStyle;
    private bool _stylesReady;
    private bool _assetsLoaded;

    void Start()
    {
        LoadAssets();
    }

    void Update()
    {
        if (_pending.Count == 0) return;
        var snapshot = new List<Action>(_pending);
        _pending.Clear();
        foreach (var a in snapshot) a?.Invoke();
    }

    private void LoadAssets()
    {
        _bgTexture = Resources.Load<Texture2D>("Map/Map_Background");
        if (_bgTexture == null) Debug.LogWarning("[MapUI] Missing: Resources/Map/Map_Background");

        _nodeCombatTex   = Resources.Load<Texture2D>("Map/Node_Combat");
        _nodeEliteTex    = Resources.Load<Texture2D>("Map/Node_Elite");
        _nodeBossTex     = Resources.Load<Texture2D>("Map/Node_Boss_V2");
        _nodeCampTex     = Resources.Load<Texture2D>("Map/Node_Camp");
        _nodeEventTex    = Resources.Load<Texture2D>("Map/Node_Event");
        _nodeMerchantTex = Resources.Load<Texture2D>("Map/Node_Merchant");
        _nodeTreasureTex = Resources.Load<Texture2D>("Map/Node_Treasure");
        _nodeStartTex    = Resources.Load<Texture2D>("Map/Node_Start_V2");

        _ropeTex = Resources.Load<Texture2D>("Map/Rope");
        if (_ropeTex != null) _ropeTex.wrapMode = TextureWrapMode.Repeat;
        else Debug.LogWarning("[MapUI] Missing: Resources/Map/Rope");

        _legendPanelTex = Resources.Load<Texture2D>("Map/Legend_Panel");
        if (_legendPanelTex == null) Debug.LogWarning("[MapUI] Missing: Resources/Map/Legend_Panel");

        _backIconTex = Resources.Load<Texture2D>("Map/MapBackbutton");
        if (_backIconTex == null) Debug.LogWarning("[MapUI] Missing: Resources/Map/MapBackbutton");

        _displayFont = Resources.Load<Font>("Fonts/Cinzel-VariableFont_wght");
        _bodyFont    = Resources.Load<Font>("Fonts/Hahmlet-VariableFont_wght");
        if (_displayFont == null) Debug.LogWarning("[MapUI] Missing Fonts/Cinzel-VariableFont_wght");
        if (_bodyFont == null)    Debug.LogWarning("[MapUI] Missing Fonts/Hahmlet-VariableFont_wght");

        if (_circleTexture == null) _circleTexture = CreateCircleTexture(128);
        EnsureVignetteTex();

        _assetsLoaded = true;
    }

    // vignetteInnerRadius 값이 바뀌면 다시 베이킹. Inspector 튜닝용.
    private void EnsureVignetteTex()
    {
        if (_vignetteTex != null && Mathf.Approximately(_vignetteBakedInner, vignetteInnerRadius)) return;
        if (_vignetteTex != null) Destroy(_vignetteTex);
        _vignetteTex = CreateVignetteTexture(256, vignetteInnerRadius);
        _vignetteBakedInner = vignetteInnerRadius;
    }

    // 화면 가장자리를 부드럽게 어둡게 만드는 비네팅 텍스처. 알파만 거리 기반으로 굽고
    // 색/세기는 그릴 때 GUI.color로 곱해 사용. innerRadius 안쪽은 알파 0.
    private static Texture2D CreateVignetteTexture(int size, float innerRadius)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float center = (size - 1) * 0.5f;
        float maxR = Mathf.Sqrt(2f) * center; // 코너까지 거리
        float falloffSpan = Mathf.Max(0.0001f, 1f - innerRadius);
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - center) / maxR;
                float dy = (y - center) / maxR;
                float r = Mathf.Sqrt(dx * dx + dy * dy); // 0(중앙) ~ 1(코너)
                float t = Mathf.Clamp01((r - innerRadius) / falloffSpan);
                float a = Mathf.SmoothStep(0f, 1f, t);
                pixels[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        tex.filterMode = FilterMode.Bilinear;
        return tex;
    }

    // 비네팅 그리기 — 노드 위, HUD 아래 레이어에 풀스크린으로 깔린다.
    private void DrawVignette()
    {
        if (!vignetteEnabled || vignetteStrength <= 0f) return;
        EnsureVignetteTex();
        if (_vignetteTex == null) return;
        var prev = GUI.color;
        GUI.color = new Color(
            vignetteColor.r, vignetteColor.g, vignetteColor.b,
            vignetteColor.a * vignetteStrength);
        GUI.matrix = Matrix4x4.identity;
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _vignetteTex);
        GUI.color = prev;
    }

    private static Texture2D CreateCircleTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float center = (size - 1) / 2f;
        float radius = center - 1f;
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center;
                float dy = y - center;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float edge = Mathf.Clamp01(radius - dist + 0.5f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, edge);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        tex.filterMode = FilterMode.Bilinear;
        return tex;
    }

    // =========================================================
    // OnGUI
    // =========================================================

    void OnGUI()
    {
        if (PauseMenuUI.IsOpen) return;
        var gsm = GameStateManager.Instance;

        // 상태를 매 프레임 추적 — Map 바깥에서도 갱신해야 재진입 시 인트로가 올바르게 트리거됨
        GameState curState = gsm != null ? gsm.State : (GameState)(-1);
        bool justEnteredMap = _prevGuiState != GameState.Map && curState == GameState.Map;
        _prevGuiState = curState;

        if (gsm == null || curState != GameState.Map) return;
        if (gsm.CurrentMap == null) return;

        if (!_assetsLoaded) LoadAssets();
        EnsureStyles();

        var map = gsm.CurrentMap;

        // 맵 진입 감지 — 이전 상태가 Map이 아니었다가 Map이 된 첫 프레임에 인트로 시작
        if (justEnteredMap)
            TriggerIntroPan(map);

        // 1) 배경 맵 이미지 — 스크린 원본 좌표로 꽉 채움 (스크롤 영향 없음)
        GUI.matrix = Matrix4x4.identity;
        DrawMapBackground();

        // 2) 이후는 1280x720 가상 좌표
        GUI.matrix = DianoCard.UI.AspectScaler.GuiMatrix;

        // 3) 현재 층이 바뀌면 보이는 영역 안으로 자동 정렬
        HandleScrollAutoSnap(map);

        // 3.5) 인트로 패닝 업데이트 — AutoSnap 이후에 _scrollY 오버라이드
        UpdateIntroPan();

        // 4) 휠 스크롤 입력
        HandleScrollInput(map);

        // 5) 맵 컨텐츠 — 클리핑 그룹 안에서 그리기 (헤더/푸터 영역 침범 방지)
        RecomputeReachableColumns(map);
        GUI.BeginGroup(new Rect(0f, MapAreaY, RefW, MapAreaH));
        DrawRopes(map);
        DrawStartDeco();
        DrawNodes(gsm);
        GUI.EndGroup();

        // 5.5) 비네팅 — 노드 위, HUD 아래. 가장자리 시선 모음.
        DrawVignette();
        // DrawVignette가 GUI.matrix를 identity로 되돌렸으므로 가상좌표 매트릭스 복원
        GUI.matrix = DianoCard.UI.AspectScaler.GuiMatrix;

        // 6) 헤더/UI는 스크롤과 무관 (스크린 가상 좌표)
        //    상단 HUD는 배틀/맵/마을 공용 BattleUI.DrawTopBar 사용.
        var battleUI = gsm.GetComponent<BattleUI>();
        if (battleUI != null)
            battleUI.DrawTopBar(BattleUI.HudContext.Map, gsm.CurrentRun, map.currentFloor, map.totalFloors);

        DrawBackButton(gsm);
        DrawScrollbar(map);
        DrawLegend();

        // 덱 뷰어 오버레이 — 상단 덱 버튼 클릭 시 열림. 모든 UI 위에 그려져야 해서 맨 마지막.
        if (battleUI != null)
            battleUI.DrawDeckViewerOverlay(gsm);

        // 유물 뷰어 오버레이 — 상단 유물 슬롯 클릭 시 열림.
        if (battleUI != null)
            battleUI.DrawRelicViewerOverlay(gsm);

        // 포션 뷰어 오버레이 — 상단 포션 슬롯 클릭 시 열림.
        if (battleUI != null)
            battleUI.DrawPotionViewerOverlay(gsm);

        // 챕터 인트로 패널 — 모든 UI 위에 그려짐 (입력 흡수 포함)
        DrawChapterIntro();
    }

    private float GetFloorY(int floor)
    {
        // 그룹 좌표계 기준 y. _scrollY 가 양수면 컨텐츠가 아래로 밀려 상위 층이 보인다.
        if (floor == 0) return StartDecoBaseY + _scrollY; // 시작층은 start 데코 위치
        return Floor1Y - (floor - 1) * FloorSpacing + _scrollY;
    }

    private const float ScrollTopPad = 130f;    // 보스 위 여백 — 끝까지 올렸을 때 보스가 화면 중앙 근처에 오도록
    private const float ScrollBottomPad = 130f; // start deco 아래 여백 — 끝까지 내렸을 때 시작 노드가 화면 중앙 근처에 오도록

    private void GetScrollBounds(int totalFloors, out float minScroll, out float maxScroll)
    {
        // 컨텐츠의 절대 위·아래 가장자리 (그룹 좌표계, _scrollY 미적용)
        float contentTop = Floor1Y - totalFloors * FloorSpacing - BossSize * 0.5f;
        float contentBottom = StartDecoBaseY + StartSize * 0.5f;

        // contentBottom + scrollY <= MapAreaH - ScrollBottomPad  → 컨텐츠 하단이 그룹 안에 머무는 최소 스크롤
        // contentTop    + scrollY >= ScrollTopPad                → 컨텐츠 상단이 그룹 안에 머무는 최대 스크롤
        float scrollForBottom = MapAreaH - ScrollBottomPad - contentBottom;
        float scrollForTop = ScrollTopPad - contentTop;

        if (scrollForBottom > scrollForTop)
        {
            // 컨텐츠가 그룹보다 작아 다 들어감 — 중앙 정렬, 스크롤 잠금
            float center = (scrollForBottom + scrollForTop) * 0.5f;
            minScroll = center;
            maxScroll = center;
        }
        else
        {
            minScroll = scrollForBottom;
            maxScroll = scrollForTop;
        }
    }

    // 스크롤바 썸 드래그 상태
    private bool _scrollbarDragging;
    private float _scrollbarDragOffset; // 마우스 Y - 썸 상단 Y (드래그 시작 시 보정)

    private static void FillRect(Rect r, Color c)
    {
        var prev = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = prev;
    }

    private static void DrawBorder(Rect r, float t, Color c)
    {
        if (t <= 0f) return;
        FillRect(new Rect(r.x, r.y, r.width, t), c);
        FillRect(new Rect(r.x, r.yMax - t, r.width, t), c);
        FillRect(new Rect(r.x, r.y, t, r.height), c);
        FillRect(new Rect(r.xMax - t, r.y, t, r.height), c);
    }

    // 맵 우/좌측 스크롤바 렌더 + 드래그 처리. GetScrollBounds 기반으로 _scrollY를 0..1로 정규화.
    private void DrawScrollbar(MapState map)
    {
        if (!scrollbarEnabled) return;

        GetScrollBounds(map.totalFloors, out float lo, out float hi);
        float range = hi - lo;
        if (range <= 0.5f) return; // 컨텐츠가 뷰포트에 다 들어감 — 스크롤바 불필요

        // 트랙 Rect
        float trackX = scrollbarOnLeft
            ? scrollbarMargin
            : RefW - scrollbarMargin - scrollbarTrackWidth;
        float trackY = scrollbarTopY;
        float trackH = RefH - scrollbarTopY - scrollbarBottomMargin;
        var trackRect = new Rect(trackX, trackY, scrollbarTrackWidth, trackH);

        FillRect(trackRect, scrollbarTrackColor);
        DrawBorder(trackRect, scrollbarBorderThickness, scrollbarBorderColor);

        // 썸 크기 — 뷰포트/컨텐츠 비율로. 너무 작아지지 않게 최소값.
        float contentH = range + MapAreaH;
        float thumbH = Mathf.Max(scrollbarThumbMinH, trackH * (MapAreaH / contentH));
        thumbH = Mathf.Min(thumbH, trackH);

        // _scrollY 는 hi(상단)↔lo(하단). 썸은 상단=스크롤최대(hi)일 때 트랙 상단.
        float t = Mathf.InverseLerp(hi, lo, _scrollY); // 0=top 1=bottom
        float thumbY = trackY + (trackH - thumbH) * t;

        float thumbX = trackX + (scrollbarTrackWidth - scrollbarThumbWidth) * 0.5f;
        var thumbRect = new Rect(thumbX, thumbY, scrollbarThumbWidth, thumbH);
        FillRect(thumbRect, scrollbarThumbColor);
        DrawBorder(thumbRect, scrollbarBorderThickness, scrollbarBorderColor);

        // 드래그 입력
        var ev = Event.current;
        if (ev.type == EventType.MouseDown && ev.button == 0)
        {
            if (thumbRect.Contains(ev.mousePosition))
            {
                _scrollbarDragging = true;
                _scrollbarDragOffset = ev.mousePosition.y - thumbY;
                ev.Use();
            }
            else if (trackRect.Contains(ev.mousePosition))
            {
                // 트랙 클릭 시 해당 위치로 점프
                float newThumbY = Mathf.Clamp(ev.mousePosition.y - thumbH * 0.5f, trackY, trackY + trackH - thumbH);
                float nt = (newThumbY - trackY) / Mathf.Max(1f, trackH - thumbH);
                _scrollY = Mathf.Lerp(hi, lo, nt);
                ev.Use();
            }
        }
        else if (ev.type == EventType.MouseDrag && _scrollbarDragging)
        {
            float newThumbY = Mathf.Clamp(ev.mousePosition.y - _scrollbarDragOffset, trackY, trackY + trackH - thumbH);
            float nt = (newThumbY - trackY) / Mathf.Max(1f, trackH - thumbH);
            _scrollY = Mathf.Lerp(hi, lo, nt);
            ev.Use();
        }
        else if (ev.type == EventType.MouseUp && _scrollbarDragging)
        {
            _scrollbarDragging = false;
            ev.Use();
        }

        // 층 정보 라벨 — 스크롤바 상단 바로 위에
        if (scrollbarFloorLabelEnabled)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = scrollbarFloorLabelFontSize,
                alignment = scrollbarOnLeft ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight,
                fontStyle = FontStyle.Bold,
            };
            style.normal.textColor = scrollbarFloorLabelColor;
            LockHoverState(style);
            int floor = Mathf.Max(1, map.currentFloor);
            string label = $"{floor}/{map.totalFloors}";
            float labelW = 100f;
            float labelX = scrollbarOnLeft
                ? trackX - 4f
                : trackX + scrollbarTrackWidth - labelW + 4f;
            var labelRect = new Rect(labelX, trackY - 30f, labelW, 26f);
            GUI.Label(labelRect, label, style);
        }
    }

    private void HandleScrollInput(MapState map)
    {
        if (_introPlaying) return; // 인트로 패닝 중에는 스크롤 입력 차단

        var ev = Event.current;
        if (ev.type != EventType.ScrollWheel) return;

        // 휠 위로(delta.y < 0) → 상위 층(보스 방향) 보기 → _scrollY 증가
        _scrollY -= ev.delta.y * 30f;
        GetScrollBounds(map.totalFloors, out float lo, out float hi);
        _scrollY = Mathf.Clamp(_scrollY, lo, hi);
        ev.Use();
    }

    // 맵에 입장할 때 보스 위치(상단)에서 시작지점(하단)으로 카메라를 내리는 인트로 시작
    private void TriggerIntroPan(MapState map)
    {
        if (map == null) return;
        if (map == _lastIntroMap) return; // 같은 맵 재진입(전투 후 복귀 등) — 인트로 생략
        _lastIntroMap = map;
        GetScrollBounds(map.totalFloors, out float lo, out float hi);

        _introFrom = hi; // 맵 최상단 = 보스가 보이는 위치
        _introTo   = lo; // 맵 최하단 = 시작 노드가 보이는 끝

        _scrollY = _introFrom;
        _introStartTime = Time.time;
        _introPlaying = true;

        // AutoSnap이 인트로를 덮어쓰지 않도록 현재 층을 미리 기록
        _lastSnappedFloor = map.currentFloor;

        // 챕터 인트로 패널 동시 시작 (카메라 팬과 오버랩)
        StartChapterIntro();
    }

    private void StartChapterIntro()
    {
        _chapterIntroPlaying = true;
        _chapterIntroStart = Time.time;
        if (_emberParticles == null)
        {
            // 큰 불똥(near-foreground bokeh) + 작은 불씨(distant) 혼합 — Gemini 레퍼런스 톤
            const int Count = 60;
            _emberParticles = new EmberParticle[Count];
            var rng = new System.Random(42);
            for (int i = 0; i < Count; i++)
            {
                bool large = rng.NextDouble() < 0.30; // 30%는 큰 전경 불똥
                _emberParticles[i] = new EmberParticle
                {
                    xRef    = (float)rng.NextDouble() * RefW,
                    yStart  = (float)rng.NextDouble() * RefH * 1.4f, // 0~1008: 시간이 흐르며 위로 떠오름
                    ySpeed  = 40f + (float)rng.NextDouble() * 95f,
                    xDrift  = ((float)rng.NextDouble() - 0.5f) * 18f,
                    size    = large
                        ? 5f + (float)rng.NextDouble() * 5f          // 큰 불똥 5~10px
                        : 1.6f + (float)rng.NextDouble() * 2.6f,     // 작은 불씨 1.6~4.2px
                    phase   = (float)rng.NextDouble() * Mathf.PI * 2f,
                };
            }
        }
    }

    // 인트로 패닝 진행 — ease-out cubic으로 부드럽게 감속
    private void UpdateIntroPan()
    {
        if (!_introPlaying) return;
        float t = Mathf.Clamp01((Time.time - _introStartTime) / IntroDuration);
        float eased = t * t; // ease-in quadratic — 처음엔 천천히, 점점 가속
        _scrollY = Mathf.Lerp(_introFrom, _introTo, eased);
        if (t >= 1f) _introPlaying = false;
    }

    // ---------------------------------------------------------
    // 챕터 인트로 패널 — StS "Act I" 스타일 (영문 큰 제목 + 한글 부제)
    // 어두운 베일 + 떠오르는 ember 입자 + 페이드 인/홀드/아웃
    // ---------------------------------------------------------

    private float GetChapterIntroAlpha(float t)
    {
        if (t < ChapterIntroFadeIn)
        {
            // ease-out quadratic — 맵 등장과 동시에 빠르게 차오르고 끝부분만 부드럽게.
            // SmoothStep과 달리 시작이 느리지 않아 "지각 지연"이 없음.
            float p = t / ChapterIntroFadeIn;
            return 1f - (1f - p) * (1f - p);
        }
        float t2 = t - ChapterIntroFadeIn;
        if (t2 < ChapterIntroHold) return 1f;
        float t3 = t2 - ChapterIntroHold;
        if (t3 < ChapterIntroFadeOut)
            return Mathf.SmoothStep(1f, 0f, t3 / ChapterIntroFadeOut);
        return 0f;
    }

    private Font GetChapterIntroDisplayFont()
    {
        if (_chapterIntroDisplayFontLoadedPath != chapterIntroDisplayFontPath)
        {
            _chapterIntroDisplayFontLoadedPath = chapterIntroDisplayFontPath;
            _chapterIntroDisplayFont = string.IsNullOrEmpty(chapterIntroDisplayFontPath)
                ? null
                : Resources.Load<Font>(chapterIntroDisplayFontPath);
        }
        return _chapterIntroDisplayFont != null ? _chapterIntroDisplayFont : _displayFont;
    }

    private Font GetChapterIntroBodyFont()
    {
        if (_chapterIntroBodyFontLoadedPath != chapterIntroBodyFontPath)
        {
            _chapterIntroBodyFontLoadedPath = chapterIntroBodyFontPath;
            _chapterIntroBodyFont = string.IsNullOrEmpty(chapterIntroBodyFontPath)
                ? null
                : Resources.Load<Font>(chapterIntroBodyFontPath);
        }
        return _chapterIntroBodyFont != null ? _chapterIntroBodyFont : _bodyFont;
    }

    private void DrawChapterIntro()
    {
        if (!_chapterIntroPlaying) return;

        float t = Time.time - _chapterIntroStart;
        if (t >= ChapterIntroTotal)
        {
            _chapterIntroPlaying = false;
            return;
        }

        float a = GetChapterIntroAlpha(t);

        var gsm = GameStateManager.Instance;
        if (gsm == null || gsm.CurrentRun == null) return;
        var chapter = DianoCard.Data.DataManager.Instance.GetChapter(gsm.CurrentRun.chapterId);
        if (chapter == null) return;

        // ACT 라벨 — Inspector spacing 슬라이더로 자간 시뮬 (글자 사이 공백 삽입)
        string actLabel = "ACT" + new string(' ', chapterIntroActSpacing) + ChapterRoman(gsm.CurrentRun.chapterId);
        string title = chapter.name ?? "";

        // 한글이면 body 폰트(Hahmlet), 영문이면 display 폰트. 오버라이드 우선.
        bool titleIsKorean = ContainsHangul(title);
        Font actFont   = GetChapterIntroDisplayFont();
        Font titleFont = titleIsKorean ? GetChapterIntroBodyFont() : GetChapterIntroDisplayFont();

        var prevColor = GUI.color;

        // 1) 풀스크린 어두운 베일 — 맵이 살짝 비치게 0.55 max (잉크 차콜 #1A1814)
        GUI.color = new Color(0.10f, 0.09f, 0.08f, 0.55f * a);
        GUI.DrawTexture(new Rect(0f, 0f, RefW, RefH), Texture2D.whiteTexture);

        // 2) ember 파티클 — 화면 아래에서 위로 떠오름. 톤 일관 유지를 위해 flicker 제거.
        //    각 입자는 페이드인/아웃 알파(a)에만 동기화되고, 자체 깜빡임은 없음.
        if (_circleTexture != null && _emberParticles != null)
        {
            for (int i = 0; i < _emberParticles.Length; i++)
            {
                var p = _emberParticles[i];
                float py = p.yStart - t * p.ySpeed;
                if (py < -10f || py > RefH + 10f) continue;
                float px = p.xRef + Mathf.Sin(t * 0.6f + p.phase) * p.xDrift; // 잔잔한 가로 흐름만
                bool large = p.size >= 5f;

                // 외곽 글로우 — 항상 같은 강도, flicker 없음
                float glowMul = large ? 3.2f : 2.6f;
                float glowAlpha = (large ? 0.20f : 0.13f) * a;
                GUI.color = new Color(0.98f, 0.55f, 0.20f, glowAlpha);
                float sg = p.size * glowMul;
                GUI.DrawTexture(new Rect(px - sg * 0.5f, py - sg * 0.5f, sg, sg), _circleTexture);

                // 본체 ember — 항상 같은 강도
                float bodyAlpha = (large ? 0.70f : 0.50f) * a;
                GUI.color = new Color(1f, 0.78f, 0.42f, bodyAlpha);
                float s = p.size;
                GUI.DrawTexture(new Rect(px - s * 0.5f, py - s * 0.5f, s, s), _circleTexture);
            }
        }

        // 3) ACT 라벨
        var actColor = chapterIntroActColor;   actColor.a   *= a;
        var titleCol = chapterIntroTitleColor; titleCol.a   *= a;
        var shadowCol = chapterIntroTitleShadowColor; shadowCol.a *= a;

        var actStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = chapterIntroActFontSize,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Normal,
        };
        if (actFont != null) actStyle.font = actFont;
        actStyle.normal.textColor = actColor;
        LockHoverState(actStyle);
        float actContainerH = Mathf.Max(32f, chapterIntroActFontSize * 1.6f);
        GUI.Label(new Rect(0f, RefH * chapterIntroActYRatio, RefW, actContainerH), actLabel, actStyle);

        // 4) 챕터 제목 — 그림자(있으면) → 본체 순서로 그려 본체가 위에 오게
        // 한글은 Hahmlet 명조의 Regular weight가 큰 사이즈에서 톤 부족 → Bold 강제
        var titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = chapterIntroTitleFontSize,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = titleIsKorean ? FontStyle.Bold : chapterIntroTitleStyle,
        };
        if (titleFont != null) titleStyle.font = titleFont;
        var titleRect = new Rect(0f, RefH * chapterIntroTitleYRatio, RefW, chapterIntroTitleHeight);

        if (chapterIntroTitleShadow > 0.01f)
        {
            titleStyle.normal.textColor = shadowCol;
            LockHoverState(titleStyle);
            var shadowRect = new Rect(
                titleRect.x + chapterIntroTitleShadow,
                titleRect.y + chapterIntroTitleShadow,
                titleRect.width, titleRect.height);
            GUI.Label(shadowRect, title, titleStyle);
        }

        titleStyle.normal.textColor = titleCol;
        LockHoverState(titleStyle);
        GUI.Label(titleRect, title, titleStyle);

        GUI.color = prevColor;

        // 입력 흡수 — fadeIn + hold 동안만 차단. fadeOut 중에는 통과시켜
        // 패널이 천천히 사라지는 동안에도 노드 클릭/스크롤 가능.
        bool blocking = t < ChapterIntroFadeIn + ChapterIntroHold;
        if (blocking)
        {
            var ev = Event.current;
            if (ev.type == EventType.MouseDown || ev.type == EventType.MouseDrag)
                ev.Use();
        }
    }

    private static bool ContainsHangul(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c >= 0xAC00 && c <= 0xD7A3) return true;     // 한글 음절
            if (c >= 0x1100 && c <= 0x11FF) return true;     // 한글 자모
            if (c >= 0x3130 && c <= 0x318F) return true;     // 한글 호환 자모
        }
        return false;
    }

    private static string ChapterRoman(string chapterId)
    {
        if (string.IsNullOrEmpty(chapterId)) return "I";
        int n = 0;
        for (int i = 0; i < chapterId.Length; i++)
            if (char.IsDigit(chapterId[i])) n = n * 10 + (chapterId[i] - '0');
        return n switch
        {
            1 => "I",
            2 => "II",
            3 => "III",
            4 => "IV",
            5 => "V",
            _ => n.ToString(),
        };
    }

    private void HandleScrollAutoSnap(MapState map)
    {
        if (_lastSnappedFloor == map.currentFloor) return;
        _lastSnappedFloor = map.currentFloor;

        // 층이 바뀌면 이전에 표시하던 경로 미리보기는 무효
        ClearPreview();

        // 현재 층이 뷰포트 하단 쪽에 오도록 정렬 — 위쪽에 앞으로 진행할 층들이 더 많이 보이게.
        // bound 안에서 clamp.
        float targetOriginalY = map.currentFloor == 0
            ? StartDecoBaseY
            : Floor1Y - (map.currentFloor - 1) * FloorSpacing;
        float targetViewportY = MapAreaH * 0.78f; // 하단에서 약 22% 지점
        GetScrollBounds(map.totalFloors, out float lo, out float hi);
        _scrollY = Mathf.Clamp(targetViewportY - targetOriginalY, lo, hi);
    }

    private void DrawMapBackground()
    {
        if (_bgTexture != null)
        {
            // BG 본체는 풀스크린 고정 — 가장자리 프레임이 잘리지 않게.
            GUI.DrawTexture(
                new Rect(0, 0, Screen.width, Screen.height),
                _bgTexture,
                ScaleMode.ScaleAndCrop,
                alphaBlend: true);
        }
        else
        {
            var prev = GUI.color;
            GUI.color = new Color(0.08f, 0.07f, 0.05f, 1f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = prev;
        }

    }

    private void DrawBackButton(GameStateManager gsm)
    {
        // 돌아가기 — 아이콘만, 패널 없음 (TechTreeUI와 동일 패턴 + 동일 에셋)
        const float backBtnSize = 57f;
        const float backBtnIconPad = 6f;
        const float backBtnMargin = 20f;
        var backRect = new Rect(backBtnMargin, RefH - backBtnSize - backBtnMargin, backBtnSize, backBtnSize);

        var prevColor = GUI.color;
        GUI.color = Color.white;
        if (_backIconTex != null)
        {
            GUI.DrawTexture(
                new Rect(backRect.x + backBtnIconPad, backRect.y + backBtnIconPad,
                    backRect.width - backBtnIconPad * 2f, backRect.height - backBtnIconPad * 2f),
                _backIconTex, ScaleMode.ScaleToFit, alphaBlend: true);
        }
        GUI.color = prevColor;

        if (GUI.Button(backRect, GUIContent.none, GUIStyle.none))
        {
            _pending.Add(() => gsm.ReturnToLobby());
        }
    }

    // ---------------------------------------------------------
    // 범례 — Legend_Panel.png 텍스처만 화면 우측에 표시. 라벨/아이콘은 텍스처에 베이크되어 있음.
    // ---------------------------------------------------------
    private void DrawLegend()
    {
        if (!legendPanelEnabled) return;
        float a = Mathf.Clamp01(legendPanelAlpha);
        if (a <= 0.001f) return;
        if (_legendPanelTex == null) return;

        // 폭/높이가 0 이하면 텍스처 원본 비율로 자동 계산.
        float panelW = legendPanelWidth > 0f ? legendPanelWidth : _legendPanelTex.width;
        float panelH = legendPanelHeight > 0f ? legendPanelHeight : _legendPanelTex.height;
        float panelX = RefW - panelW - legendRightMargin;
        float panelY = legendTopY;

        var savedColor = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, a);
        GUI.DrawTexture(new Rect(panelX, panelY, panelW, panelH), _legendPanelTex, ScaleMode.StretchToFill, alphaBlend: true);
        GUI.color = savedColor;
    }

    // ---------------------------------------------------------
    // 노드
    // ---------------------------------------------------------

    private void DrawStartDeco()
    {
        if (_nodeStartTex == null) return;

        float size = StartSize;
        var rect = new Rect(
            StartDecoPos.x - size * 0.5f,
            StartDecoPos.y - size * 0.5f,
            size, size);

        // 시작 데코는 항상 깬 상태(회색 비활성)로 표시 — 시작 유물은 RelicPickerUI에서 받음.
        var prev = GUI.color;
        GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.7f);
        GUI.DrawTexture(rect, _nodeStartTex, ScaleMode.ScaleToFit, alphaBlend: true);
        GUI.color = prev;
    }

    private void DrawNodes(GameStateManager gsm)
    {
        var map = gsm.CurrentMap;
        foreach (var node in map.nodes)
        {
            Vector2 pos = GetNodeCenter(node, map);
            DrawNode(node, pos, map, gsm);
        }
    }

    // ---------------------------------------------------------
    // 로프 (노드 연결선)
    // ---------------------------------------------------------

    // 현재 층(cf)에서 클릭 가능한 컬럼 집합. cf==1이고 1층 cleared가 없으면 1층 전부가 시작 선택지.
    // 그 외에는 직전 층(cf-1)에서 cleared된 노드의 nextColumns만 허용.
    private void RecomputeReachableColumns(MapState map)
    {
        _reachableColumns.Clear();
        int cf = map.currentFloor;

        // cf=1 이고 아직 1층에서 어떤 노드도 클리어 안 함 → 1층 전체가 시작 선택지(StS의 "starting room" 행).
        if (cf == 1)
        {
            bool anyCleared = false;
            foreach (var n in map.NodesOnFloor(1)) if (n.cleared) { anyCleared = true; break; }
            if (!anyCleared)
            {
                foreach (var n in map.NodesOnFloor(1)) _reachableColumns.Add(n.column);
                return;
            }
        }

        // 일반 케이스: 직전 층의 cleared 노드들이 가리키는 nextColumns만 허용.
        foreach (var n in map.NodesOnFloor(cf - 1))
        {
            if (!n.cleared) continue;
            foreach (var nextCol in n.nextColumns) _reachableColumns.Add(nextCol);
        }

        // fail-open: 데이터 이상으로 도달 가능 컬럼이 비면 현재 층 전체 허용.
        if (_reachableColumns.Count == 0)
        {
            foreach (var n in map.NodesOnFloor(cf)) _reachableColumns.Add(n.column);
        }
    }

    private void DrawRopes(MapState map)
    {
        // 시작 데코(floor 0 가상 위치) → 1층 모든 노드 fan-out 로프
        var startPos = StartDecoPos;
        foreach (var n1 in map.NodesOnFloor(1))
            DrawRope(startPos, GetNodeCenter(n1, map));

        // 1층 이상 노드들의 nextColumns를 따라 엣지를 그린다 (15→16 보스 fan-in 포함).
        for (int f = 1; f < map.totalFloors + 1; f++)
        {
            foreach (var from in map.NodesOnFloor(f))
            {
                foreach (var toCol in from.nextColumns)
                {
                    var to = map.GetNode(f + 1, toCol);
                    if (to == null) continue;
                    bool highlighted = _previewEdges.Contains((f, from.column, toCol));
                    DrawRope(GetNodeCenter(from, map), GetNodeCenter(to, map), highlighted);
                }
            }
        }
    }

    // 미래 노드를 preview 대상으로 지정하고 현재 층에서 그 노드까지의 모든 가능 경로 엣지를 계산.
    // 역방향 BFS — target에서 시작해 부모(in-edge가 자신을 가리키는 노드)로 거슬러 올라간다.
    private void SetPreviewTarget(MapState map, MapNode target)
    {
        _previewTarget = target;
        _previewEdges.Clear();
        if (target == null) return;
        if (target.floor <= map.currentFloor) return;

        var reachable = new HashSet<int> { target.column };
        for (int f = target.floor - 1; f >= Mathf.Max(map.currentFloor, 1); f--)
        {
            var nextReachable = reachable;
            var cur = new HashSet<int>();
            foreach (var from in map.NodesOnFloor(f))
            {
                foreach (var toCol in from.nextColumns)
                {
                    if (!nextReachable.Contains(toCol)) continue;
                    cur.Add(from.column);
                    _previewEdges.Add((f, from.column, toCol));
                }
            }
            reachable = cur;
            if (reachable.Count == 0) break;
        }

        // currentFloor == 0 일 때 1층 노드들로의 진입은 시작 데코에서 fan-out. preview 강조는 1층까지만.
    }

    private void ClearPreview()
    {
        _previewTarget = null;
        _previewEdges.Clear();
    }

    // 노드 사이를 작은 점들로 이은 dashed 트레일.
    // 회전을 쓰지 않기 때문에 BeginGroup 클리핑/스크롤과 충돌하지 않음.
    // highlighted=true 면 preview 경로 강조용 굵은 금색 숨쉬는 점으로 그림.
    private void DrawRope(Vector2 a, Vector2 b, bool highlighted = false)
    {
        if (_circleTexture == null) return;

        Vector2 d = b - a;
        float length = d.magnitude;
        if (length < 1f) return;

        Vector2 dir = d / length;

        const float inset = 30f;
        if (length <= inset * 2f + 4f) return;

        Vector2 start = a + dir * inset;
        Vector2 end   = b - dir * inset;
        float trailLen = length - inset * 2f;

        float dotSpacing = highlighted ? 26f : 32f;
        float dotSize    = highlighted ? 7f  : 5f;
        int dotCount = Mathf.Max(2, Mathf.RoundToInt(trailLen / dotSpacing) + 1);

        var prevColor = GUI.color;

        if (highlighted)
        {
            // 경로를 따라 흐르는 듯한 숨쉬는 금빛 트레일 (잔잔한 펄스)
            float flow = Time.time * 1.5f;
            for (int i = 0; i < dotCount; i++)
            {
                float t = (float)i / (dotCount - 1);
                float px = Mathf.Lerp(start.x, end.x, t);
                float py = Mathf.Lerp(start.y, end.y, t);

                // 점별 위상차로 물결처럼 반짝이게 — 진폭 대폭 축소
                float phase = 0.5f + 0.5f * Mathf.Sin(flow - t * 5f);
                float size = dotSize * Mathf.Lerp(0.95f, 1.08f, phase);
                var color = Color.Lerp(
                    new Color(0.85f, 0.62f, 0.22f, 0.78f),
                    new Color(0.95f, 0.82f, 0.50f, 0.90f),
                    phase);

                // 바깥 glow 원 — 작고 더 투명하게
                GUI.color = new Color(color.r, color.g, color.b, color.a * 0.20f);
                float glow = size * 1.4f;
                GUI.DrawTexture(new Rect(px - glow * 0.5f, py - glow * 0.5f, glow, glow), _circleTexture);

                // 본체 점
                GUI.color = color;
                GUI.DrawTexture(new Rect(px - size * 0.5f, py - size * 0.5f, size, size), _circleTexture);
            }
        }
        else
        {
            // 밝은 베이지 본체 + 뒤에 어두운 halo로 배경 어디서든 읽히게
            Color haloColor = new Color(0f, 0f, 0f, 0.55f);
            Color dotColor  = new Color(0.94f, 0.86f, 0.68f, 0.85f);
            float haloSize  = dotSize + 3f;
            for (int i = 0; i < dotCount; i++)
            {
                float t = (float)i / (dotCount - 1);
                float px = Mathf.Lerp(start.x, end.x, t);
                float py = Mathf.Lerp(start.y, end.y, t);

                // 어두운 halo (아우트라인)
                GUI.color = haloColor;
                GUI.DrawTexture(new Rect(px - haloSize * 0.5f, py - haloSize * 0.5f, haloSize, haloSize), _circleTexture);

                // 밝은 본체
                GUI.color = dotColor;
                GUI.DrawTexture(new Rect(px - dotSize * 0.5f, py - dotSize * 0.5f, dotSize, dotSize), _circleTexture);
            }
        }

        GUI.color = prevColor;
    }

    private Vector2 GetNodeCenter(MapNode node, MapState map)
    {
        float y = GetFloorY(node.floor);
        if (node.kind == NodeKind.Boss) return new Vector2(BossX, y);
        if (node.floor == 0) return new Vector2(MapCenterX, y); // 시작 데코는 중앙 고정 (실제 노드는 floor>=1)
        float cx = GetColumnX(node.column);
        Vector2 jitter = NodeJitter(node.floor, node.column);
        return new Vector2(cx + jitter.x * nodeJitterXScale, y + jitter.y * nodeJitterYScale);
    }

    // 7컬럼 고정 격자 — column 0..6, 중심 = 640. 격자 폭은 gridSpan7로 조정.
    private float GetColumnX(int column)
    {
        float leftX = MapCenterX - gridSpan7 * 0.5f;
        float spacing = gridSpan7 / (MapWidth - 1);
        int idx = Mathf.Clamp(column, 0, MapWidth - 1);
        return leftX + idx * spacing;
    }

    // (floor, column) → 결정적 ±오프셋. 같은 노드는 항상 같은 위치 → 로프 끝점이 자동으로 일치.
    // 7컬럼 spacing(~180px)에 비해 X jitter는 작게 잡아 컬럼이 명확히 구분되도록 한다.
    // Y는 50% 노드만 활성화 — 나머지는 0으로 floor 라인에 정렬 → "딱딱한 격자" 느낌 완화.
    private static Vector2 NodeJitter(int floor, int column)
    {
        unchecked
        {
            uint h = (uint)(floor * 73856093) ^ (uint)(column * 19349663);
            h ^= h >> 13; h *= 0x27d4eb2d; h ^= h >> 15;
            float fx = ((h & 0xFFFF) / 65535f - 0.5f) * 2f;       // -1..1
            bool yEnabled = ((h >> 16) & 1u) != 0u;               // 50% on/off
            float fy = yEnabled
                ? (((h >> 17) & 0x7FFFu) / 32767f - 0.5f) * 2f    // -1..1
                : 0f;
            return new Vector2(fx * 28f, fy * 45f);
        }
    }

    private void DrawNode(MapNode node, Vector2 center, MapState map, GameStateManager gsm)
    {
        // 현재 층 노드라도 직전 층 클리어 노드와 연결된 컬럼이 아니면 진입 불가.
        bool onCurrentFloor = node.floor == map.currentFloor && !node.cleared;
        bool isCurrent = onCurrentFloor && _reachableColumns.Contains(node.column);
        bool isPast = node.cleared;
        bool isBoss = node.kind == NodeKind.Boss;
        bool isStart = node.floor == 0;
        bool isFuture = !isCurrent && !isPast && node.floor > map.currentFloor;
        bool isPreviewTarget = _previewTarget != null
            && node.floor == _previewTarget.floor
            && node.column == _previewTarget.column;

        float baseSize = isBoss ? BossSize : (isStart ? StartSize : NodeSize);
        if (node.kind == NodeKind.Elite) baseSize *= 1.10f;

        // 클릭 가능한 노드 — 현재 층 + 미래 노드 모두 호버 시 살짝 커짐
        bool isClickable = isCurrent || isFuture;
        var hitRect = new Rect(center.x - baseSize / 2f, center.y - baseSize / 2f, baseSize, baseSize);
        bool isHovered = isClickable && hitRect.Contains(Event.current.mousePosition);

        float size = isHovered ? baseSize * 1.12f : baseSize;
        var rect = new Rect(center.x - size / 2f, center.y - size / 2f, size, size);

        // 현재 층 노드 — 하이라이트
        if (isCurrent)
        {
            var prev = GUI.color;

            if (isStart)
            {
                // 시작 노드: 크기·알파가 함께 숨쉬는 녹색 다중 원 halo — 노드에 바짝 붙게
                float pulse01 = 0.5f + 0.5f * Mathf.Sin(Time.time * 2.2f); // 0..1
                float pulseExtra = Mathf.Lerp(3f, 12f, pulse01);
                float pulseAlpha = Mathf.Lerp(0.6f, 1f, pulse01);
                for (int i = 0; i < 3; i++)
                {
                    float r = size + pulseExtra + i * 6f;
                    var hRect = new Rect(center.x - r / 2f, center.y - r / 2f, r, r);
                    float a = (0.34f - i * 0.08f) * pulseAlpha;
                    GUI.color = new Color(0.45f, 1f, 0.35f, Mathf.Max(0f, a));
                    GUI.DrawTexture(hRect, _circleTexture);
                }
            }
            else
            {
                // 일반/엘리트 등: 크기·알파가 함께 숨쉬는 노란 다중 원 halo — 노드에 바짝 붙게
                float pulse01 = 0.5f + 0.5f * Mathf.Sin(Time.time * 2.6f); // 0..1
                float pulseExtra = Mathf.Lerp(4f, 14f, pulse01);
                float pulseAlpha = Mathf.Lerp(0.55f, 1f, pulse01);
                for (int i = 0; i < 3; i++)
                {
                    float r = size + pulseExtra + i * 7f;
                    var hRect = new Rect(center.x - r / 2f, center.y - r / 2f, r, r);
                    float a = (0.36f - i * 0.09f) * pulseAlpha;
                    GUI.color = new Color(1f, 0.85f, 0.30f, Mathf.Max(0f, a));
                    GUI.DrawTexture(hRect, _circleTexture);
                }
            }

            GUI.color = prev;
        }

        // Preview 타겟 노드 — 금빛 숨쉬는 다중 원 halo (현재 층 하이라이트와 색감으로 구분)
        if (isPreviewTarget)
        {
            var prev = GUI.color;
            float pulse01 = 0.5f + 0.5f * Mathf.Sin(Time.time * 3f);
            float pulseExtra = Mathf.Lerp(10f, 30f, pulse01);
            float pulseAlpha = Mathf.Lerp(0.55f, 1f, pulse01);
            for (int i = 0; i < 4; i++)
            {
                float r = size + pulseExtra + i * 12f;
                var hRect = new Rect(center.x - r / 2f, center.y - r / 2f, r, r);
                float a = (0.38f - i * 0.072f) * pulseAlpha;
                GUI.color = new Color(1f, 0.82f, 0.35f, Mathf.Max(0f, a));
                GUI.DrawTexture(hRect, _circleTexture);
            }
            GUI.color = prev;
        }

        // 앰비언트 글로우 — 미래 노드(아직 진입 불가)에 타입 색으로 아주 옅게 티만.
        // 현재 층 halo와 혼동되지 않도록 크기·알파 모두 최소화.
        if (isFuture && !isStart && !isPreviewTarget)
        {
            var prev = GUI.color;
            Color glow = GetNodeGlowColor(node.kind);
            float pulse01 = 0.5f + 0.5f * Mathf.Sin(Time.time * 1.2f + node.floor * 0.7f + node.column * 0.5f);
            float alphaMul = Mathf.Lerp(0.88f, 1f, pulse01);
            for (int i = 0; i < 2; i++)
            {
                float r = size + 2f + i * 4f;
                var hRect = new Rect(center.x - r / 2f, center.y - r / 2f, r, r);
                float a = (0.10f - i * 0.05f) * alphaMul;
                GUI.color = new Color(glow.r, glow.g, glow.b, Mathf.Max(0f, a));
                GUI.DrawTexture(hRect, _circleTexture);
            }
            GUI.color = prev;
        }

        // 본체 스프라이트 — 시작층은 start 아이콘으로
        Texture2D tex = isStart ? _nodeStartTex : GetNodeTexture(node.kind);
        if (tex != null)
        {
            var prevColor = GUI.color;
            if (isPreviewTarget)      GUI.color = Color.white;                        // preview — 밝게
            else if (isPast)          GUI.color = new Color(0.55f, 0.55f, 0.55f, 0.85f); // 클리어 — 회색
            else if (!isCurrent)      GUI.color = new Color(0.75f, 0.75f, 0.75f, 0.85f); // 미래 — 살짝 어둡게
            else                      GUI.color = Color.white;
            GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit, alphaBlend: true);
            GUI.color = prevColor;
        }
        else
        {
            // fallback: 색 원
            var prevColor = GUI.color;
            GUI.color = GetFallbackColor(node.kind);
            GUI.DrawTexture(rect, _circleTexture);
            GUI.color = prevColor;
        }

        // 클릭 처리 — 시각은 1.12배(rect)지만 hit-test는 hover판정과 동일한 baseSize(hitRect)로 통일.
        // hover→rect 확장→클릭→hover 해제→rect 축소가 한 프레임 안에 진동하면 클릭 손실됨.
        if (isClickable)
        {
            var ev = Event.current;
            if (ev.type == EventType.MouseDown && ev.button == 0 && hitRect.Contains(ev.mousePosition))
            {
                if (isCurrent)
                {
                    Debug.Log($"[MapUI] Clicked node Floor {node.floor} Col {node.column} enemies=[{string.Join(",", node.enemyIds)}]");
                    ev.Use();
                    var capturedNode = node;
                    _pending.Add(() => gsm.SelectMapNode(capturedNode));
                }
                else // isFuture — 경로 미리보기 토글
                {
                    if (isPreviewTarget) ClearPreview();
                    else                 SetPreviewTarget(map, node);
                    ev.Use();
                }
            }
        }
    }

    private Texture2D GetNodeTexture(NodeKind kind) => kind switch
    {
        NodeKind.Combat   => _nodeCombatTex,
        NodeKind.Elite    => _nodeEliteTex,
        NodeKind.Boss     => _nodeBossTex,
        NodeKind.Camp     => _nodeCampTex,
        NodeKind.Event    => _nodeEventTex,
        NodeKind.Unknown  => _nodeEventTex, // 전용 ? 아이콘이 추가될 때까진 Event 아이콘 재활용
        NodeKind.Merchant => _nodeMerchantTex,
        NodeKind.Treasure => _nodeTreasureTex,
        _ => null,
    };

    private Color GetFallbackColor(NodeKind kind) => kind switch
    {
        NodeKind.Combat   => new Color(0.85f, 0.45f, 0.15f, 0.95f),
        NodeKind.Elite    => new Color(0.75f, 0.15f, 0.15f, 0.95f),
        NodeKind.Boss     => new Color(0.55f, 0.1f, 0.6f, 0.95f),
        NodeKind.Camp     => new Color(0.25f, 0.6f, 0.35f, 0.95f),
        NodeKind.Event    => new Color(0.9f, 0.85f, 0.2f, 0.95f),
        NodeKind.Unknown  => new Color(0.9f, 0.85f, 0.2f, 0.95f),
        NodeKind.Merchant => new Color(0.25f, 0.45f, 0.75f, 0.95f),
        NodeKind.Treasure => new Color(0.95f, 0.75f, 0.25f, 0.95f), // 황금
        _ => Color.gray,
    };

    // 앰비언트 글로우 색상 — 실제 아이콘 색감에 맞춤
    private Color GetNodeGlowColor(NodeKind kind) => kind switch
    {
        NodeKind.Combat   => new Color(0.40f, 0.60f, 1.00f), // 파랑 (교차 검 아이콘)
        NodeKind.Elite    => new Color(1.00f, 0.30f, 0.35f), // 붉은 (해골)
        NodeKind.Boss     => new Color(1.00f, 0.30f, 0.35f), // 붉은 (해골)
        NodeKind.Camp     => new Color(1.00f, 0.65f, 0.30f), // 주황 (모닥불)
        NodeKind.Event    => new Color(0.80f, 0.40f, 1.00f), // 보라 (물음표)
        NodeKind.Unknown  => new Color(0.80f, 0.40f, 1.00f), // 보라 (물음표)
        NodeKind.Merchant => new Color(1.00f, 0.80f, 0.30f), // 금 (돈주머니)
        NodeKind.Treasure => new Color(1.00f, 0.85f, 0.25f), // 황금 (보물상자)
        _ => Color.white,
    };

    // ---------------------------------------------------------
    // 스타일
    // ---------------------------------------------------------

    private void EnsureStyles()
    {
        if (_stylesReady) return;

        _smallStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.2f, 0.12f, 0.05f) },
        };
        // GUI.skin.label 기본 hover state의 색 swap을 차단해 호버 시 텍스트가 깜빡이는 느낌을 없앤다.
        LockHoverState(_smallStyle);

        _stylesReady = true;
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
}
