using System;
using System.Collections.Generic;
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
    private const float RefW = 1280f;
    private const float RefH = 720f;

    private const float NodeSize = 76f;
    private const float BossSize = 110f;
    private const float StartSize = 80f;
    private const float HighlightPad = 20f;

    private const float RopeWidth = 6f;       // 얇게 — 시선이 노드에 가게
    private const float RopeNodeInset = 4f;
    private const float RopeAlpha = 0.55f;    // 살짝 흐리게

    // 스크롤 가능한 맵 컨텐츠 영역 (스크린 가상 좌표)
    // 상단바(~66) 아래부터 hint footer(~660) 위까지 클리핑 그룹으로 묶는다
    private const float MapAreaY = 72f;
    private const float MapAreaH = 588f;

    // 그룹 좌표계 기준 — floor 1이 그룹 하단 근처, 최상층이 그룹 상단 방향
    // 층 간격을 넉넉히 잡아 노드가 시원하게 흩뿌려진 인상. 화면을 넘으면 휠로 스크롤.
    private const float Floor1Y = 500f;
    private const float FloorSpacing = 200f;
    private const float StartDecoBaseY = 660f;  // start 데코를 1층보다 더 아래로 (gap ~160)

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

    private GUIStyle _smallStyle;
    private GUIStyle _backButtonStyle;
    private bool _stylesReady;
    private bool _assetsLoaded;

    // 절차적 데코 (스크롤과 함께 움직이는 화석/먼지/등고선)
    private struct DecorMark { public float x, y, size; public Color color; }
    private List<DecorMark> _decorMarks;
    private List<float> _decorContourYs;

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
        _nodeBossTex     = Resources.Load<Texture2D>("Map/Node_Boss");
        _nodeCampTex     = Resources.Load<Texture2D>("Map/Node_Camp");
        _nodeEventTex    = Resources.Load<Texture2D>("Map/Node_Event");
        _nodeMerchantTex = Resources.Load<Texture2D>("Map/Node_Merchant");
        _nodeStartTex    = Resources.Load<Texture2D>("Map/Node_Start");

        _ropeTex = Resources.Load<Texture2D>("Map/Rope");
        if (_ropeTex != null) _ropeTex.wrapMode = TextureWrapMode.Repeat;
        else Debug.LogWarning("[MapUI] Missing: Resources/Map/Rope");

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
    }

    private void DrawMapDecor()
    {
        if (_decorMarks == null) return;

        var prev = GUI.color;

        // 1) 등고선 (옅은 가로선) — 스크롤에 따라 같이 흐름
        GUI.color = new Color(0.32f, 0.20f, 0.07f, 0.10f);
        foreach (var cy in _decorContourYs)
        {
            float y = cy + _scrollY;
            if (y < -2f || y > MapAreaH + 2f) continue;
            GUI.DrawTexture(new Rect(0, y, RefW, 1.4f), Texture2D.whiteTexture);
        }

        // 2) 화석/먼지 점
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
        if (gsm == null || gsm.State != GameState.Map) return;
        if (gsm.CurrentMap == null) return;

        if (!_assetsLoaded) LoadAssets();
        EnsureStyles();

        var map = gsm.CurrentMap;

        // 1) 배경 맵 이미지 — 스크린 원본 좌표로 꽉 채움 (스크롤 영향 없음)
        GUI.matrix = Matrix4x4.identity;
        DrawMapBackground();

        // 2) 이후는 1280x720 가상 좌표
        float scale = Mathf.Min(Screen.width / RefW, Screen.height / RefH);
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));

        // 3) 현재 층이 바뀌면 보이는 영역 안으로 자동 정렬
        HandleScrollAutoSnap(map);

        // 4) 휠 스크롤 입력
        HandleScrollInput(map);

        // 5) 맵 컨텐츠 — 클리핑 그룹 안에서 그리기 (헤더/푸터 영역 침범 방지)
        GUI.BeginGroup(new Rect(0f, MapAreaY, RefW, MapAreaH));
        DrawMapDecor();
        DrawRopes(map);
        DrawNodes(gsm);
        GUI.EndGroup();

        // 6) 헤더/UI는 스크롤과 무관 (스크린 가상 좌표)
        //    상단 HUD는 전투와 동일한 BattleUI.DrawMapTopBar를 재사용.
        var battleUI = gsm.GetComponent<BattleUI>();
        if (battleUI != null)
            battleUI.DrawMapTopBar(gsm.CurrentRun, map.currentFloor, map.totalFloors);

        DrawBackButton(gsm);
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
        float contentTop = Floor1Y - (totalFloors - 1) * FloorSpacing - BossSize * 0.5f;
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

    private void HandleScrollInput(MapState map)
    {
        var ev = Event.current;
        if (ev.type != EventType.ScrollWheel) return;

        // 휠 위로(delta.y < 0) → 상위 층(보스 방향) 보기 → _scrollY 증가
        _scrollY -= ev.delta.y * 30f;
        GetScrollBounds(map.totalFloors, out float lo, out float hi);
        _scrollY = Mathf.Clamp(_scrollY, lo, hi);
        ev.Use();
    }

    private void HandleScrollAutoSnap(MapState map)
    {
        if (_lastSnappedFloor == map.currentFloor) return;
        _lastSnappedFloor = map.currentFloor;

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
    // 노드
    // ---------------------------------------------------------

    private void DrawStartDeco()
    {
        // 바닥 중앙의 START 장식 (클릭 불가, 플레이어 시작 지점 표시)
        if (_nodeStartTex == null) return;
        var rect = new Rect(
            StartDecoPos.x - StartSize / 2f,
            StartDecoPos.y - StartSize / 2f,
            StartSize, StartSize);
        GUI.DrawTexture(rect, _nodeStartTex, ScaleMode.ScaleToFit, alphaBlend: true);
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

    private void DrawRopes(MapState map)
    {
        // 시작층(floor 0) → 1층 모든 노드 (단일 점에서 퍼지는 fan은 교차 없음)
        var floor0 = map.NodesOnFloor(0);
        var floor1 = map.NodesOnFloor(1);
        foreach (var f0 in floor0)
            foreach (var f1 in floor1)
                DrawRope(GetNodeCenter(f0, map), GetNodeCenter(f1, map));

        // 층 사이 연결
        for (int f = 1; f < map.totalFloors; f++)
        {
            var fromNodes = map.NodesOnFloor(f);
            var toNodes = map.NodesOnFloor(f + 1);
            bool nextIsBoss = toNodes.Count == 1 && toNodes[0].kind == NodeKind.Boss;

            if (nextIsBoss)
            {
                // 모든 노드 → 보스 (fan in, 교차 없음)
                var boss = toNodes[0];
                foreach (var from in fromNodes)
                    DrawRope(GetNodeCenter(from, map), GetNodeCenter(boss, map));
                continue;
            }

            // 일반 층 → 일반 층: 교차 없는 엣지만 선택해서 그리기
            foreach (var (fromCol, toCol) in ComputeFloorEdges(f, fromNodes.Count, toNodes.Count))
            {
                MapNode from = null, to = null;
                foreach (var n in fromNodes) if (n.column == fromCol) { from = n; break; }
                foreach (var n in toNodes)   if (n.column == toCol)   { to = n; break; }
                if (from != null && to != null)
                    DrawRope(GetNodeCenter(from, map), GetNodeCenter(to, map));
            }
        }
    }

    // 한 층(fromCount) → 다음 층(toCount) 사이 엣지를 교차 없이 계산.
    // 두 번의 단조(monotone) 패스로 모든 from / to 노드가 최소 1개 엣지를 갖도록 보장:
    //   1) 각 to-node는 비율상 가장 가까운 from-node로부터 하나 받는다.
    //   2) 각 from-node는 비율상 가장 가까운 to-node로 하나 보낸다.
    // 두 패스 모두 인덱스 순으로 단조 증가하므로 결합해도 선분이 교차하지 않음.
    private static IEnumerable<(int fromCol, int toCol)> ComputeFloorEdges(int floor, int fromCount, int toCount)
    {
        var result = new List<(int, int)>(fromCount + toCount);
        var seen = new HashSet<(int, int)>();

        void Add(int a, int b)
        {
            if (seen.Add((a, b))) result.Add((a, b));
        }

        // to 커버 — 모든 to-node에 최소 1개 in-edge
        for (int t = 0; t < toCount; t++)
        {
            int fi;
            if (fromCount <= 1) fi = 0;
            else if (toCount <= 1) fi = 0;
            else
            {
                float pos = (float)t * (fromCount - 1) / (toCount - 1);
                fi = Mathf.Clamp((int)Mathf.Floor(pos + 0.5f), 0, fromCount - 1);
            }
            Add(fi, t);
        }

        // from 커버 — 모든 from-node에 최소 1개 out-edge
        for (int fi = 0; fi < fromCount; fi++)
        {
            int t;
            if (toCount <= 1) t = 0;
            else if (fromCount <= 1) t = 0;
            else
            {
                float pos = (float)fi * (toCount - 1) / (fromCount - 1);
                t = Mathf.Clamp((int)Mathf.Floor(pos + 0.5f), 0, toCount - 1);
            }
            Add(fi, t);
        }

        // 결정적 해시로 선택적 대각선 추가 — 교차 방지를 위해 오른쪽 한 칸만 확장.
        // 단조성 보존: (fi, ti) → (fi, ti+1). ti+1 이 다음 fi 의 primary ti' 이상이면 교차 가능 → 건너뜀.
        for (int fi = 0; fi < fromCount - 1; fi++)
        {
            uint h = (uint)(floor * 374761393) ^ (uint)((fi + 1) * 668265263);
            h ^= h >> 13; h *= 0x85ebca6b; h ^= h >> 16;
            if ((h % 3) != 1) continue;

            int tiPrimary = toCount <= 1 ? 0
                : Mathf.Clamp((int)Mathf.Floor((float)fi * (toCount - 1) / Mathf.Max(1, fromCount - 1) + 0.5f), 0, toCount - 1);
            int tiNextPrimary = toCount <= 1 ? 0
                : Mathf.Clamp((int)Mathf.Floor((float)(fi + 1) * (toCount - 1) / Mathf.Max(1, fromCount - 1) + 0.5f), 0, toCount - 1);

            int extra = tiPrimary + 1;
            if (extra >= toCount) continue;
            if (extra > tiNextPrimary) continue;
            Add(fi, extra);
        }

        return result;
    }

    // 노드 사이를 작은 점들로 이은 dashed 트레일.
    // 회전을 쓰지 않기 때문에 BeginGroup 클리핑/스크롤과 충돌하지 않음.
    private void DrawRope(Vector2 a, Vector2 b)
    {
        if (_circleTexture == null) return;

        Vector2 d = b - a;
        float length = d.magnitude;
        if (length < 1f) return;

        Vector2 dir = d / length;

        // 노드 안쪽으로 살짝 들어가서 시작 — 점들이 노드 가장자리 바로 옆에서 시작
        const float inset = 30f;
        if (length <= inset * 2f + 4f) return;

        Vector2 start = a + dir * inset;
        Vector2 end   = b - dir * inset;
        float trailLen = length - inset * 2f;

        // 점 간격 ~32px, 점 크기 ~5px
        const float dotSpacing = 32f;
        const float dotSize = 5f;
        int dotCount = Mathf.Max(2, Mathf.RoundToInt(trailLen / dotSpacing) + 1);

        var prevColor = GUI.color;
        GUI.color = new Color(0.30f, 0.18f, 0.06f, 0.62f);

        for (int i = 0; i < dotCount; i++)
        {
            float t = (float)i / (dotCount - 1);
            float px = Mathf.Lerp(start.x, end.x, t);
            float py = Mathf.Lerp(start.y, end.y, t);
            var rect = new Rect(px - dotSize * 0.5f, py - dotSize * 0.5f, dotSize, dotSize);
            GUI.DrawTexture(rect, _circleTexture);
        }

        GUI.color = prevColor;
    }

    private Vector2 GetNodeCenter(MapNode node, MapState map)
    {
        float y = GetFloorY(node.floor);
        if (node.kind == NodeKind.Boss) return new Vector2(BossX, y);
        if (node.floor == 0) return new Vector2(MapCenterX, y); // 시작 노드는 중앙 고정, jitter 없음
        int count = NodeCountOnFloor(map, node.floor);
        float cx = GetColumnX(count, node.column);
        Vector2 jitter = NodeJitter(node.floor, node.column, count);
        return new Vector2(cx + jitter.x, y + jitter.y);
    }

    private static int NodeCountOnFloor(MapState map, int floor)
    {
        int c = 0;
        foreach (var n in map.nodes) if (n.floor == floor) c++;
        return c;
    }

    // 층 노드 수(nodeCount)에 따라 컬럼 중심 x를 반환. 모든 층 중심은 640.
    private static float GetColumnX(int nodeCount, int column)
    {
        if (nodeCount <= 1) return MapCenterX;
        float totalSpan = nodeCount == 3 ? 520f : (nodeCount == 4 ? 660f : 800f);
        float leftX = MapCenterX - totalSpan * 0.5f;
        float spacing = totalSpan / (nodeCount - 1);
        int idx = Mathf.Clamp(column, 0, nodeCount - 1);
        return leftX + idx * spacing;
    }

    // (floor, column) → 결정적 ±오프셋. 같은 노드는 항상 같은 위치 → 로프 끝점이 자동으로 일치.
    // 층의 노드 수가 많아질수록 좌우 여유가 줄어들어 jitter 폭도 함께 줄인다.
    private static Vector2 NodeJitter(int floor, int column, int nodeCount)
    {
        unchecked
        {
            uint h = (uint)(floor * 73856093) ^ (uint)(column * 19349663);
            h ^= h >> 13; h *= 0x27d4eb2d; h ^= h >> 15;
            float fx = ((h & 0xFFFF) / 65535f - 0.5f) * 2f;          // -1..1
            float fy = (((h >> 16) & 0xFFFF) / 65535f - 0.5f) * 2f;  // -1..1
            float jitterX = nodeCount <= 3 ? 75f : (nodeCount == 4 ? 58f : 46f);
            return new Vector2(fx * jitterX, fy * 55f);
        }
    }

    private void DrawNode(MapNode node, Vector2 center, MapState map, GameStateManager gsm)
    {
        bool isCurrent = node.floor == map.currentFloor && !node.cleared;
        bool isPast = node.cleared;
        bool isBoss = node.kind == NodeKind.Boss;
        bool isStart = node.floor == 0;

        float baseSize = isBoss ? BossSize : (isStart ? StartSize : NodeSize);

        // 클릭 가능한 노드만 호버 시 살짝 커짐 — 버튼 느낌
        var hitRect = new Rect(center.x - baseSize / 2f, center.y - baseSize / 2f, baseSize, baseSize);
        bool isHovered = isCurrent && hitRect.Contains(Event.current.mousePosition);

        float size = isHovered ? baseSize * 1.12f : baseSize;
        var rect = new Rect(center.x - size / 2f, center.y - size / 2f, size, size);

        // 현재 층 노드 — 하이라이트
        if (isCurrent)
        {
            var prev = GUI.color;

            if (isStart)
            {
                // 시작 노드: 크기·알파가 함께 숨쉬는 녹색 다중 원 halo
                float pulse01 = 0.5f + 0.5f * Mathf.Sin(Time.time * 2.2f); // 0..1
                float pulseExtra = Mathf.Lerp(10f, 36f, pulse01);
                float pulseAlpha = Mathf.Lerp(0.6f, 1f, pulse01);
                for (int i = 0; i < 5; i++)
                {
                    float r = size + pulseExtra + i * 12f;
                    var hRect = new Rect(center.x - r / 2f, center.y - r / 2f, r, r);
                    float a = (0.34f - i * 0.058f) * pulseAlpha;
                    GUI.color = new Color(0.45f, 1f, 0.35f, Mathf.Max(0f, a));
                    GUI.DrawTexture(hRect, _circleTexture);
                }
            }
            else
            {
                // 일반/엘리트 등: 크기·알파가 함께 숨쉬는 노란 다중 원 halo
                float pulse01 = 0.5f + 0.5f * Mathf.Sin(Time.time * 2.6f); // 0..1
                float pulseExtra = Mathf.Lerp(14f, 44f, pulse01);
                float pulseAlpha = Mathf.Lerp(0.55f, 1f, pulse01);
                for (int i = 0; i < 5; i++)
                {
                    float r = size + pulseExtra + i * 14f;
                    var hRect = new Rect(center.x - r / 2f, center.y - r / 2f, r, r);
                    float a = (0.36f - i * 0.062f) * pulseAlpha;
                    GUI.color = new Color(1f, 0.85f, 0.30f, Mathf.Max(0f, a));
                    GUI.DrawTexture(hRect, _circleTexture);
                }
            }

            GUI.color = prev;
        }

        // 본체 스프라이트 — 시작층은 start 아이콘으로
        Texture2D tex = isStart ? _nodeStartTex : GetNodeTexture(node.kind);
        if (tex != null)
        {
            var prevColor = GUI.color;
            if (isPast)         GUI.color = new Color(0.55f, 0.55f, 0.55f, 0.85f); // 클리어 — 회색
            else if (!isCurrent) GUI.color = new Color(0.75f, 0.75f, 0.75f, 0.85f); // 미래 — 살짝 어둡게
            else                 GUI.color = Color.white;
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

        // 클릭 처리 (현재 층 노드만)
        if (isCurrent)
        {
            var ev = Event.current;
            if (ev.type == EventType.MouseDown && ev.button == 0 && rect.Contains(ev.mousePosition))
            {
                Debug.Log($"[MapUI] Clicked node Floor {node.floor} Col {node.column} enemies=[{string.Join(",", node.enemyIds)}]");
                ev.Use();
                var capturedNode = node;
                _pending.Add(() => gsm.SelectMapNode(capturedNode));
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
        NodeKind.Merchant => _nodeMerchantTex,
        _ => null,
    };

    private Color GetFallbackColor(NodeKind kind) => kind switch
    {
        NodeKind.Combat   => new Color(0.85f, 0.45f, 0.15f, 0.95f),
        NodeKind.Elite    => new Color(0.75f, 0.15f, 0.15f, 0.95f),
        NodeKind.Boss     => new Color(0.55f, 0.1f, 0.6f, 0.95f),
        NodeKind.Camp     => new Color(0.25f, 0.6f, 0.35f, 0.95f),
        NodeKind.Event    => new Color(0.9f, 0.85f, 0.2f, 0.95f),
        NodeKind.Merchant => new Color(0.25f, 0.45f, 0.75f, 0.95f),
        _ => Color.gray,
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

        _stylesReady = true;
    }
}
