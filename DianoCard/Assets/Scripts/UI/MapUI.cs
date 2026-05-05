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
    [Header("Legend Panel")]
    [Tooltip("범례 패널 전체 불투명도 — 배경 양피지 + 아이콘 + 라벨 모두에 곱해진다. 0=완전 투명, 1=원본 그대로.")]
    [SerializeField, Range(0f, 1f)] private float legendPanelAlpha = 1f;
    [Tooltip("패널 우측 가장자리에서 화면 우측까지 여백")]
    [SerializeField] private float legendRightMargin = 18f;
    [Tooltip("패널 상단 Y (1280x720 가상 좌표 기준). HUD 스트립(~74) 아래로 내려야 가려지지 않음.")]
    [SerializeField] private float legendTopY = 95f;

    [Header("Legend Panel — Padding")]
    [Tooltip("좌우 내부 여백 — 찢긴 양피지 가장자리 피하기")]
    [SerializeField] private float legendPadX = 28f;
    [Tooltip("상단 내부 여백 — 핀 + 상단 찢긴 가장자리 피하기")]
    [SerializeField] private float legendPadTop = 52f;
    [Tooltip("하단 내부 여백 — 하단 찢긴 가장자리 피하기")]
    [SerializeField] private float legendPadBottom = 34f;

    [Header("Legend Panel — Row")]
    [Tooltip("노드 아이콘 크기 (정사각형)")]
    [SerializeField] private float legendIconSize = 36f;
    [Tooltip("한 행 높이")]
    [SerializeField] private float legendRowH = 44f;
    [Tooltip("라벨 영역 너비")]
    [SerializeField] private float legendLabelW = 82f;
    [Tooltip("아이콘과 라벨 사이 간격")]
    [SerializeField] private float legendIconLabelGap = 10f;

    [Header("Legend Panel — Text")]
    [SerializeField] private int legendFontSize = 20;
    [SerializeField] private Color legendTextColor = new Color(0.25f, 0.15f, 0.07f);

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

    [Header("Node Layout — StS-style 7컬럼 격자 (양피지 영역에 맞춰 조정)")]
    [Tooltip("7컬럼 좌↔우 총 폭 (px). 화면 폭 1280에서 양옆 패딩을 뺀 값 이하로 설정.")]
    [SerializeField, Range(600f, 1200f)] private float gridSpan7 = 780f;

    [Tooltip("노드 가로 jitter 배율. 1=기본, 0=jitter 없음 (컬럼 정렬 깔끔). 7컬럼은 간격이 좁아 기본값 작게.")]
    [SerializeField, Range(0f, 1.5f)] private float nodeJitterXScale = 0.18f;
    [Tooltip("노드 세로 jitter 배율.")]
    [SerializeField, Range(0f, 1.5f)] private float nodeJitterYScale = 1f;

    private const int MapWidth = 7;

    // =========================================================
    private const float RefW = 1280f;
    private const float RefH = 720f;

    private const float NodeSize = 50f;
    private const float BossSize = 98.01f;
    private const float StartSize = 71.28f;
    private const float HighlightPad = 16f;

    private const float RopeWidth = 6f;
    private const float RopeNodeInset = 3f;
    private const float RopeAlpha = 0.60f;

    // 스크롤 가능한 맵 컨텐츠 영역 (스크린 가상 좌표)
    // 화면 전체를 차지 — 상단 HUD / 하단 버튼은 그 위에 오버레이로 그린다
    private const float MapAreaY = 0f;
    private const float MapAreaH = RefH;

    // 층 간격 250px — 15층 맵이 화면을 넘어가므로 스크롤로 탐색.
    private const float Floor1Y = 500f;
    private const float FloorSpacing = 250f;
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
    private const float IntroDuration = 2.4f;

    // 이전 프레임 GameState — Map 진입 감지용
    private GameState _prevGuiState = (GameState)(-1);

    // 인트로를 재생한 MapState — 같은 맵 재진입(전투 후 복귀 등)에서는 인트로 생략
    private MapState _lastIntroMap;

    // 시작 유물 선택 패널 — 시작 데코 클릭 시 열림, 유물 선택 후 닫힘.
    private bool _starterRelicPanelOpen;

    // 미리보기: 미래 노드 클릭 시 현재 층 → 해당 노드까지의 가능 경로 강조
    private MapNode _previewTarget;
    private readonly HashSet<(int fromFloor, int fromCol, int toCol)> _previewEdges = new();
    // 현재 층에서 실제 도달 가능한 컬럼 집합 — 매 OnGUI마다 재계산. 직전 층 클리어 노드에서 연결된 컬럼만 포함.
    private readonly HashSet<int> _reachableColumns = new();

    private Texture2D _bgTexture;
    private Texture2D _circleTexture;

    private Texture2D _nodeCombatTex;
    private Texture2D _nodeEliteTex;
    private Texture2D _nodeBossTex;
    private Texture2D _nodeCampTex;
    private Texture2D _nodeEventTex;
    private Texture2D _nodeMerchantTex;
    private Texture2D _nodeStartTex;
    private Texture2D _ropeTex;
    private Texture2D _legendPanelTex;
    private Texture2D _relicPickIcon;  // 유물 선택 패널 아이콘 (ShopUI/Relics 재활용)

    private GUIStyle _smallStyle;
    private GUIStyle _backButtonStyle;
    private GUIStyle _legendStyle;
    private bool _stylesReady;
    private bool _assetsLoaded;

    // 절차적 데코 (스크롤과 함께 움직이는 화석/먼지/등고선)
    private struct DecorMark { public float x, y, size; public Color color; }
    private List<DecorMark> _decorMarks;
    private List<float> _decorContourYs;

    // 하늘의 별 — 화면 좌표 고정, 스크롤 영향 없음. 각자 위상차로 반짝임.
    private struct StarMark { public float x, y, size, phase, freq, baseAlpha; public Color tint; }
    private List<StarMark> _stars;

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
        _nodeStartTex    = Resources.Load<Texture2D>("Map/Node_Start_V2");
        _relicPickIcon   = Resources.Load<Texture2D>("ShopUI/Relics");

        _ropeTex = Resources.Load<Texture2D>("Map/Rope");
        if (_ropeTex != null) _ropeTex.wrapMode = TextureWrapMode.Repeat;
        else Debug.LogWarning("[MapUI] Missing: Resources/Map/Rope");

        _legendPanelTex = Resources.Load<Texture2D>("Map/Legend_Panel");
        if (_legendPanelTex == null) Debug.LogWarning("[MapUI] Missing: Resources/Map/Legend_Panel");

        if (_circleTexture == null) _circleTexture = CreateCircleTexture(128);

        GenerateDecor();

        _assetsLoaded = true;
    }

    private void GenerateDecor()
    {
        // 시드 고정 — 매 프레임 같은 패턴
        var rng = new System.Random(7341);
        _decorMarks = new List<DecorMark>(120);

        // 데코 y 범위: 위로는 보스(많이 잡아 -2400), 아래로는 시작 데코 아래 800
        // 그룹 좌표계 기준 (그룹 안에서 _scrollY 더해서 그림)
        const float yMin = -2400f;
        const float yMax = 900f;

        for (int i = 0; i < 120; i++)
        {
            float gray = 0.18f + (float)rng.NextDouble() * 0.18f;
            _decorMarks.Add(new DecorMark
            {
                x = (float)rng.NextDouble() * RefW,
                y = yMin + (float)rng.NextDouble() * (yMax - yMin),
                size = 3f + (float)rng.NextDouble() * 9f,
                color = new Color(gray, gray * 0.7f, gray * 0.4f, 0.35f),
            });
        }

        _decorContourYs = new List<float>();
        for (float y = yMin; y < yMax; y += 68f)
            _decorContourYs.Add(y + (float)rng.NextDouble() * 12f);

        GenerateStars();
    }

    private void GenerateStars()
    {
        // 별은 배경 아트의 하늘(상단 ~45%)에 모이게. 아래로 내려올수록 개수·밝기 감소.
        var rng = new System.Random(5118);
        _stars = new List<StarMark>(70);

        const int count = 64;
        for (int i = 0; i < count; i++)
        {
            float u = (float)rng.NextDouble();
            float v = (float)rng.NextDouble();
            // y 분포: 상단에 몰리게 제곱 바이어스
            float ny = v * v;
            float y = ny * (RefH * 0.52f);
            float x = u * RefW;

            // 크기: 대부분 작고 가끔 큰 별
            float r = (float)rng.NextDouble();
            float size = r < 0.82f ? 1.6f + r * 1.8f : 3.2f + (float)rng.NextDouble() * 2.4f;

            // 색: 흰색 ↔ 살짝 따뜻한 크림 ↔ 아주 옅은 블루 — 차콜 하늘에 녹는 톤
            float warm = (float)rng.NextDouble();
            Color tint = warm < 0.55f
                ? new Color(1f, 0.97f, 0.88f)
                : warm < 0.85f
                    ? new Color(1f, 1f, 1f)
                    : new Color(0.82f, 0.88f, 1f);

            // 상단은 진하게, 아래는 흐리게 — 지평선으로 자연스럽게 녹아듬
            float heightFactor = 1f - ny;            // 1 at top, 0 at horizon
            float baseAlpha = Mathf.Lerp(0.18f, 0.85f, heightFactor);

            _stars.Add(new StarMark
            {
                x = x,
                y = y,
                size = size,
                phase = (float)rng.NextDouble() * Mathf.PI * 2f,
                freq = 1.2f + (float)rng.NextDouble() * 2.2f,
                baseAlpha = baseAlpha,
                tint = tint,
            });
        }
    }

    private void DrawSky()
    {
        if (_stars == null || _circleTexture == null) return;

        var prev = GUI.color;
        float t = Time.time;

        foreach (var s in _stars)
        {
            // 숨쉬는 알파 (0.55..1.0) — 너무 깜빡이지 않게 저 진폭
            float tw = 0.775f + 0.225f * Mathf.Sin(t * s.freq + s.phase);
            float a = s.baseAlpha * tw;

            // 본체
            GUI.color = new Color(s.tint.r, s.tint.g, s.tint.b, a);
            GUI.DrawTexture(
                new Rect(s.x - s.size * 0.5f, s.y - s.size * 0.5f, s.size, s.size),
                _circleTexture);

            // 큰 별에만 은은한 glow
            if (s.size > 3f)
            {
                float g = s.size * 2.6f;
                GUI.color = new Color(s.tint.r, s.tint.g, s.tint.b, a * 0.28f);
                GUI.DrawTexture(
                    new Rect(s.x - g * 0.5f, s.y - g * 0.5f, g, g),
                    _circleTexture);
            }
        }

        GUI.color = prev;
    }

    private void DrawMapDecor()
    {
        if (_decorMarks == null) return;

        var prev = GUI.color;

        // 등고선(갈색 가로선)은 양피지 맵 시절 데코 — 현 하늘/폐허 배경과 안 어울려 제거.

        // 화석/먼지 점
        foreach (var m in _decorMarks)
        {
            float y = m.y + _scrollY;
            if (y < -10f || y > MapAreaH + 10f) continue;
            GUI.color = m.color;
            GUI.DrawTexture(
                new Rect(m.x - m.size * 0.5f, y - m.size * 0.5f, m.size, m.size),
                _circleTexture);
        }

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
        float scale = Mathf.Min(Screen.width / RefW, Screen.height / RefH);
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));

        // 2.5) 하늘의 별 — 스크롤과 무관. 맵 컨텐츠 뒤에 깔려서 노드/로프를 방해하지 않음.
        DrawSky();

        // 3) 현재 층이 바뀌면 보이는 영역 안으로 자동 정렬
        HandleScrollAutoSnap(map);

        // 3.5) 인트로 패닝 업데이트 — AutoSnap 이후에 _scrollY 오버라이드
        UpdateIntroPan();

        // 4) 휠 스크롤 입력
        HandleScrollInput(map);

        // 5) 맵 컨텐츠 — 클리핑 그룹 안에서 그리기 (헤더/푸터 영역 침범 방지)
        RecomputeReachableColumns(map);
        GUI.BeginGroup(new Rect(0f, MapAreaY, RefW, MapAreaH));
        DrawMapDecor();
        DrawRopes(map);
        DrawStartDeco();
        DrawNodes(gsm);
        GUI.EndGroup();

        // 6) 헤더/UI는 스크롤과 무관 (스크린 가상 좌표)
        //    상단 HUD는 배틀/맵/마을 공용 BattleUI.DrawTopBar 사용.
        var battleUI = gsm.GetComponent<BattleUI>();
        if (battleUI != null)
            battleUI.DrawTopBar(BattleUI.HudContext.Map, gsm.CurrentRun, map.currentFloor, map.totalFloors);

        DrawBackButton(gsm);
        DrawScrollbar(map);

        // 7) 시작 유물 선택 패널 — 시작 데코 클릭으로 열림. 모든 UI 위에 오버레이.
        if (_starterRelicPanelOpen && !map.starterRelicChosen)
            DrawStarterRelicPanel(gsm);

        // 덱 뷰어 오버레이 — 상단 덱 버튼 클릭 시 열림. 모든 UI 위에 그려져야 해서 맨 마지막.
        if (battleUI != null)
            battleUI.DrawDeckViewerOverlay(gsm);

        // 유물 뷰어 오버레이 — 상단 유물 슬롯 클릭 시 열림.
        if (battleUI != null)
            battleUI.DrawRelicViewerOverlay(gsm);

        // 포션 뷰어 오버레이 — 상단 포션 슬롯 클릭 시 열림.
        if (battleUI != null)
            battleUI.DrawPotionViewerOverlay(gsm);
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
        if (GUI.Button(new Rect(RefW - 170, RefH - 54, 150, 40), "← BACK TO LOBBY", _backButtonStyle))
        {
            _pending.Add(() => gsm.ReturnToLobby());
        }
    }

    // ---------------------------------------------------------
    // 범례 — 각 노드 아이콘의 의미를 알려주는 반투명 패널
    // ---------------------------------------------------------
    private static readonly (NodeKind kind, string label)[] LegendEntries = new[]
    {
        (NodeKind.Combat,   "적"),
        (NodeKind.Elite,    "엘리트"),
        (NodeKind.Boss,     "보스"),
        (NodeKind.Camp,     "휴식"),
        (NodeKind.Merchant, "상인"),
        (NodeKind.Treasure, "보물"),
        (NodeKind.Unknown,  "미지"),
    };

    private void DrawLegend()
    {
        // 인스펙터 값 → 로컬 변수로 캡처
        float iconSize = legendIconSize;
        float rowH = legendRowH;
        float labelW = legendLabelW;
        float iconLabelGap = legendIconLabelGap;
        float padX = legendPadX;
        float padTop = legendPadTop;
        float padBottom = legendPadBottom;

        float a = Mathf.Clamp01(legendPanelAlpha);
        if (a <= 0.001f) return; // 완전 투명이면 스킵

        // 폰트/색도 인스펙터에서 바꾸면 즉시 반영. 라벨 색에도 패널 알파 곱해줌.
        if (_legendStyle != null)
        {
            _legendStyle.fontSize = legendFontSize;
            var tc = legendTextColor;
            _legendStyle.normal.textColor = new Color(tc.r, tc.g, tc.b, tc.a * a);
        }

        float contentW = iconSize + iconLabelGap + labelW;
        float contentH = rowH * LegendEntries.Length;
        float panelW = padX * 2f + contentW;
        float panelH = padTop + padBottom + contentH;
        float panelX = RefW - panelW - legendRightMargin;
        float panelY = legendTopY;

        var panelRect = new Rect(panelX, panelY, panelW, panelH);

        var savedColor = GUI.color;

        // 배경 — 양피지 텍스처. 없으면 fallback으로 반투명 다크 패널.
        if (_legendPanelTex != null)
        {
            GUI.color = new Color(1f, 1f, 1f, a);
            GUI.DrawTexture(panelRect, _legendPanelTex, ScaleMode.StretchToFill, alphaBlend: true);
        }
        else
        {
            GUI.color = new Color(0.08f, 0.06f, 0.04f, 0.72f * a);
            GUI.DrawTexture(panelRect, Texture2D.whiteTexture);
        }

        float cy = panelY + padTop;
        foreach (var (kind, label) in LegendEntries)
        {
            var tex = GetNodeTexture(kind);
            var iconRect = new Rect(panelX + padX, cy + (rowH - iconSize) * 0.5f, iconSize, iconSize);
            if (tex != null)
            {
                GUI.color = new Color(1f, 1f, 1f, a);
                GUI.DrawTexture(iconRect, tex, ScaleMode.ScaleToFit, alphaBlend: true);
            }
            else
            {
                var fc = GetFallbackColor(kind);
                GUI.color = new Color(fc.r, fc.g, fc.b, fc.a * a);
                GUI.DrawTexture(iconRect, _circleTexture);
            }

            var labelRect = new Rect(
                iconRect.xMax + iconLabelGap,
                cy,
                labelW,
                rowH);
            GUI.color = Color.white; // 라벨은 스타일의 textColor 사용
            GUI.Label(labelRect, label, _legendStyle);

            cy += rowH;
        }

        GUI.color = savedColor;
    }

    // ---------------------------------------------------------
    // 노드
    // ---------------------------------------------------------

    private void DrawStartDeco()
    {
        if (_nodeStartTex == null) return;
        var map = GameStateManager.Instance?.CurrentMap;

        float size = StartSize;
        var rect = new Rect(
            StartDecoPos.x - size * 0.5f,
            StartDecoPos.y - size * 0.5f,
            size, size);

        var prev = GUI.color;

        // 유물 미선택 상태 → 황금 박동 글로우 + 클릭 가능
        bool needsRelic = map != null && !map.starterRelicChosen;
        if (needsRelic)
        {
            float pulse = 0.7f + 0.3f * Mathf.Sin(Time.time * 3f);
            float haloSize = size + 28f;
            GUI.color = new Color(1f, 0.85f, 0.2f, 0.55f * pulse);
            GUI.DrawTexture(new Rect(
                StartDecoPos.x - haloSize * 0.5f,
                StartDecoPos.y - haloSize * 0.5f,
                haloSize, haloSize), _circleTexture);
            GUI.color = prev;

            bool hover = rect.Contains(Event.current.mousePosition);
            if (hover && Event.current.type == EventType.MouseDown)
            {
                _starterRelicPanelOpen = true;
                Event.current.Use();
            }
        }

        GUI.color = needsRelic ? new Color(1f, 1f, 1f, 1f) : new Color(0.5f, 0.5f, 0.5f, 0.7f);
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
    // 시작 유물 선택 패널 (스크린 좌표, 스크롤 무관)
    // ---------------------------------------------------------

    private void DrawStarterRelicPanel(GameStateManager gsm)
    {
        EnsureStyles();
        var map = gsm.CurrentMap;
        if (map == null || map.starterRelicCandidates.Count == 0) return;

        const float PanelW = 780f;
        const float PanelH = 180f;
        const float CardW   = 220f;
        const float CardH   = 140f;
        const float Gap     = 20f;

        float panelX = (RefW - PanelW) * 0.5f;
        float panelY = RefH - PanelH - 50f;

        // 반투명 배경
        var prevColor = GUI.color;
        GUI.color = new Color(0.05f, 0.02f, 0.1f, 0.88f);
        GUI.DrawTexture(new Rect(panelX - 10f, panelY - 40f, PanelW + 20f, PanelH + 50f), Texture2D.whiteTexture);
        GUI.color = prevColor;

        // 제목
        GUI.Label(new Rect(panelX, panelY - 32f, PanelW, 30f),
            "시작 유물을 선택하세요",
            new GUIStyle(GUI.skin.label)
            {
                fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 0.88f, 0.55f) }
            });

        // 후보 카드 3개
        float totalW = map.starterRelicCandidates.Count * CardW + (map.starterRelicCandidates.Count - 1) * Gap;
        float startX = panelX + (PanelW - totalW) * 0.5f;

        for (int i = 0; i < map.starterRelicCandidates.Count; i++)
        {
            var relic = DataManager.Instance.GetRelic(map.starterRelicCandidates[i]);
            if (relic == null) continue;

            float cx = startX + i * (CardW + Gap);
            var cardRect = new Rect(cx, panelY, CardW, CardH);
            bool hover = cardRect.Contains(Event.current.mousePosition);

            // 카드 배경
            GUI.color = hover ? new Color(0.9f, 0.8f, 0.3f, 0.85f) : new Color(0.2f, 0.15f, 0.35f, 0.85f);
            GUI.DrawTexture(cardRect, Texture2D.whiteTexture);
            GUI.color = prevColor;

            // 아이콘 (ShopUI의 Relics.jpg 재활용)
            if (_relicPickIcon != null)
            {
                float iconS = 48f;
                GUI.DrawTexture(new Rect(cx + (CardW - iconS) * 0.5f, panelY + 8f, iconS, iconS),
                    _relicPickIcon, ScaleMode.ScaleToFit);
            }

            // 이름
            GUI.Label(new Rect(cx + 4f, panelY + 58f, CardW - 8f, 24f), relic.nameKr,
                new GUIStyle(GUI.skin.label)
                {
                    fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = Color.white }
                });

            // 설명
            GUI.Label(new Rect(cx + 4f, panelY + 80f, CardW - 8f, 48f), relic.description,
                new GUIStyle(GUI.skin.label)
                {
                    fontSize = 11, wordWrap = true, alignment = TextAnchor.UpperCenter,
                    normal = { textColor = new Color(0.85f, 0.85f, 0.85f) }
                });

            // 클릭
            if (hover && Event.current.type == EventType.MouseDown)
            {
                gsm.PickStarterRelic(relic.id);
                _starterRelicPanelOpen = false;
                Event.current.Use();
            }
        }
    }

    // ---------------------------------------------------------
    // 로프 (노드 연결선)
    // ---------------------------------------------------------

    // 현재 층(cf)에서 클릭 가능한 컬럼 집합. cf==1이고 1층 cleared가 없으면 1층 전부가 시작 선택지.
    // 그 외에는 직전 층(cf-1)에서 cleared된 노드의 nextColumns만 허용.
    // starterRelicChosen이 false이면 1층 진입 자체를 막는다(유물 선택 강제).
    private void RecomputeReachableColumns(MapState map)
    {
        _reachableColumns.Clear();
        int cf = map.currentFloor;

        // 유물 미선택 → 어떤 노드도 클릭 불가 (유물 패널로 유도).
        if (!map.starterRelicChosen) return;

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

        float dotSpacing = highlighted ? 22f : 32f;
        float dotSize    = highlighted ? 9f  : 5f;
        int dotCount = Mathf.Max(2, Mathf.RoundToInt(trailLen / dotSpacing) + 1);

        var prevColor = GUI.color;

        if (highlighted)
        {
            // 경로를 따라 흐르는 듯한 숨쉬는 금빛 트레일
            float flow = Time.time * 2.4f;
            for (int i = 0; i < dotCount; i++)
            {
                float t = (float)i / (dotCount - 1);
                float px = Mathf.Lerp(start.x, end.x, t);
                float py = Mathf.Lerp(start.y, end.y, t);

                // 점별 위상차로 물결처럼 반짝이게
                float phase = 0.5f + 0.5f * Mathf.Sin(flow - t * 5f);
                float size = dotSize * Mathf.Lerp(0.85f, 1.25f, phase);
                var color = Color.Lerp(
                    new Color(1f, 0.72f, 0.22f, 0.85f),
                    new Color(1f, 0.97f, 0.65f, 1f),
                    phase);

                // 바깥 glow 원 (크고 투명)
                GUI.color = new Color(color.r, color.g, color.b, color.a * 0.35f);
                float glow = size * 2.2f;
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
    // 7컬럼 spacing(~180px)에 비해 jitter는 작게 잡아 컬럼이 명확히 구분되도록 한다.
    private static Vector2 NodeJitter(int floor, int column)
    {
        unchecked
        {
            uint h = (uint)(floor * 73856093) ^ (uint)(column * 19349663);
            h ^= h >> 13; h *= 0x27d4eb2d; h ^= h >> 15;
            float fx = ((h & 0xFFFF) / 65535f - 0.5f) * 2f;          // -1..1
            float fy = (((h >> 16) & 0xFFFF) / 65535f - 0.5f) * 2f;  // -1..1
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

        // 클릭 처리
        if (isClickable)
        {
            var ev = Event.current;
            if (ev.type == EventType.MouseDown && ev.button == 0 && rect.Contains(ev.mousePosition))
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
        NodeKind.Treasure => _nodeEventTex, // 전용 보물상자 아이콘 추가 전까진 Event 아이콘 재활용
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

        _backButtonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
        };

        _legendStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = legendFontSize,
            alignment = TextAnchor.MiddleLeft,
            fontStyle = FontStyle.Bold,
            normal = { textColor = legendTextColor }, // 양피지 위 진한 잉크
        };

        _stylesReady = true;
    }
}
