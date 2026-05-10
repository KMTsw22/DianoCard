using System.Collections.Generic;
using DianoCard.Game;
using UnityEngine;

/// <summary>
/// 테크트리 화면 — 실제 헥스 스프라이트(Resources/테크트리UI/) 사용.
/// 배경: 별자리 이미지 / 노드: 방향별 컬러 헥스 스프라이트 / 잠금: 회색 헥스.
/// </summary>
public class TechTreeUI : MonoBehaviour
{
    private const float RefW = 1280f;
    private const float RefH = 720f;

    // ── Inspector 조절 파라미터 ────────────────────────────
    [Header("노드 크기 — 일반")]
    [SerializeField] private float nodeWidthNormal    = 80f;
    [SerializeField] private float nodeHeightNormal   = 65f;
    [Header("노드 크기 — 캡스톤")]
    [SerializeField] private float nodeWidthCapstone  = 100f;
    [SerializeField] private float nodeHeightCapstone = 81.25f;
    [SerializeField] private float nodeIconRatio         = 0.60f;
    [SerializeField] private float nodeIconRatioCapstone = 0.65f;
    [SerializeField] private float nodeIconPadding       = 7f;   // 헥스 안쪽 여백 (px)

    [Header("루트 노드")]
    [SerializeField] private float rootWidth  = 132f;
    [SerializeField] private float rootHeight = 112f;

    [Header("노드 간격")]
    [SerializeField] private float nodeSpacingScale = 2.0f;   // 중앙 기준 거리 배율 (1=기본)

    [Header("연결선")]
    [SerializeField] private float lineThickUnlocked = 2.2f;
    [SerializeField] private float lineThickDotted   = 1.8f;
    [SerializeField] private float lineThickLocked   = 1.5f;

    [Header("글로우")]
    [SerializeField] private float glowExpandNormal   = 20f;
    [SerializeField] private float glowExpandCapstone = 30f;

    [Header("줌")]
    [SerializeField] private float zoomSpeed = 0.10f;   // 휠 1틱당 줌 변화량
    [SerializeField] private float zoomMin   = 0.6f;
    [SerializeField] private float zoomMax   = 1.8f;

    [Header("패닝")]
    [SerializeField] private float panMargin = 80f;     // 트리 끝에서 추가 여유 (가상 단위)


    [Header("UI — 타이틀 아이콘")]
    [SerializeField] private float titleX = 0f;
    [SerializeField] private float titleY = 12f;
    [SerializeField] private float titleW = 299f;
    [SerializeField] private float titleH = 59f;

    [Header("UI — 포인트 아이콘")]
    [SerializeField] private float pointsIconX    = 1160f;
    [SerializeField] private float pointsIconY    = 20f;
    [SerializeField] private float pointsIconSize = 50f;
    [SerializeField] private float pointsFontSize = 35;

    [Header("UI — 돌아가기 버튼")]
    [SerializeField] private float backBtnX       = 20f;
    [SerializeField] private float backBtnY       = 643f;
    [SerializeField] private float backBtnSize    = 57f;
    [SerializeField] private float backBtnIconPad = 6f;

    // ── 색 팔레트 ──────────────────────────────────────────
    private static readonly Color BgDeep       = new Color(0.04f, 0.03f, 0.06f, 1f);
    private static readonly Color OrnamentTint = new Color(0.45f, 0.35f, 0.18f, 0.18f);
    private static readonly Color TextWarm     = new Color(0.93f, 0.86f, 0.66f, 1f);
    private static readonly Color TextMuted    = new Color(0.70f, 0.66f, 0.55f, 1f);
    private static readonly Color TextDim      = new Color(0.55f, 0.50f, 0.45f, 1f);

    private static readonly Color LineLocked   = new Color(0.40f, 0.36f, 0.30f, 0.55f);
    private static readonly Color LineUnlocked = new Color(1f,    0.78f, 0.40f, 0.85f);

    // 방향별 글로우 액센트 색
    private static readonly Color AccentRight = new Color(0.95f, 0.30f, 0.40f); // 공격 — 핑크빨강
    private static readonly Color AccentLeft  = new Color(0.45f, 0.70f, 1.00f); // 방어 — 파랑
    private static readonly Color AccentUp    = new Color(1.00f, 0.90f, 0.30f); // 공룡 — 노랑
    private static readonly Color AccentDown  = new Color(0.95f, 0.55f, 0.15f); // 유틸 — 주황
    private static readonly Color AccentRoot  = new Color(0.65f, 0.40f, 0.95f); // 루트 — 보라

    // ── 절차 텍스처 (글로우/선) ────────────────────────────
    private Texture2D _whiteTex;
    private Texture2D _radialGlowTex;

    // ── 스프라이트 (Resources/테크트리UI/) ─────────────────
    private Texture2D _bgTex;        // 배경 별자리
    private Texture2D _hexRootTex;   // 보라 — 중앙 센터
    private Texture2D _hexAttackTex; // 핑크빨강 — 공격(R)
    private Texture2D _hexBlueTex;   // 파랑 — 방어(L)
    private Texture2D _hexGoldTex;   // 노랑 — 공룡(U)
    private Texture2D _hexOrangeTex; // 주황 — 유틸(D)
    private Texture2D _hexGrayTex;   // 그레이 — 잠금

    // ── 노드 아이콘 (Resources/TechTreeUI/Icon/) ──────────
    private Dictionary<string, Texture2D> _nodeIcons;

    // ── UI 아이콘 ──────────────────────────────────────────
    private Texture2D _titleTex;
    private Texture2D _pointsIconTex;
    private Texture2D _backIconTex;

    private Font _displayFont;
    private Font _displayFontKR;

    private GUIStyle _titleStyle;
    private GUIStyle _pointsStyle;
    private GUIStyle _branchLabelStyle;
    private GUIStyle _rankCounterStyle;
    private GUIStyle _smallBtnStyle;
    private GUIStyle _backBtnStyle;
    private bool _stylesReady;

    private string _hoverNodeId;
    private string _flashMessage;
    private float  _flashUntil;

    // ── 패닝 / 줌 상태 ─────────────────────────────────────
    private Vector2 _pan  = Vector2.zero;
    private float   _zoom = 1f;
    private bool    _wasInTechTree;
    private Vector2 _dragStart;
    private bool    _isDragging;        // 우클릭 드래그
    private bool    _isLeftDragging;    // 좌클릭 드래그 (노드 외부에서 시작)

    void Start()
    {
        _whiteTex      = Texture2D.whiteTexture;
        _radialGlowTex = MakeRadialGlow(96);
        _displayFont   = Resources.Load<Font>("Fonts/IMFellEnglish-Regular");
        _displayFontKR = Resources.Load<Font>("Fonts/Hahmlet-VariableFont_wght");

        _bgTex        = Resources.Load<Texture2D>("TechTreeUI/TechTree_BG");
        _hexRootTex   = Resources.Load<Texture2D>("TechTreeUI/TechTree_Hex_Center");
        _hexAttackTex = Resources.Load<Texture2D>("TechTreeUI/TechTree_Hex_Attack");
        _hexBlueTex   = Resources.Load<Texture2D>("TechTreeUI/TechTree_Hex_Defense");
        _hexGoldTex   = Resources.Load<Texture2D>("TechTreeUI/TechTree_Hex_Dino");
        _hexOrangeTex = Resources.Load<Texture2D>("TechTreeUI/TechTree_Hex_Util");
        _hexGrayTex   = Resources.Load<Texture2D>("TechTreeUI/TechTree_Hex_Locked");

        _titleTex      = Resources.Load<Texture2D>("TechTreeUI/TechTree_Title");
        _pointsIconTex = Resources.Load<Texture2D>("TechTreeUI/TechTree_Icon_Points");
        _backIconTex   = Resources.Load<Texture2D>("TechTreeUI/TechTree_Icon_Back");

        _nodeIcons = new Dictionary<string, Texture2D>();
        foreach (var id in new[]{ "A1","A2","A3","A4","D1","D2","D3","D4","N1","N2","N3","N4","U1","U2","U3","U4" })
        {
            var tex = Resources.Load<Texture2D>($"TechTreeUI/Icon/{id}");
            if (tex != null) _nodeIcons[id] = tex;
        }
    }

    void OnDestroy()
    {
        if (_radialGlowTex != null) Destroy(_radialGlowTex);
    }

    void OnGUI()
    {
        if (PauseMenuUI.IsOpen) return;
        var gsm = GameStateManager.Instance;
        bool inTechTree = gsm != null && gsm.State == GameState.TechTree && gsm.TechTree != null;

        if (inTechTree && !_wasInTechTree)
        {
            _zoom = 1f;
            _pan  = Vector2.zero;
        }
        _wasInTechTree = inTechTree;

        if (!inTechTree) return;

        EnsureStyles();

        float scale      = Mathf.Min(Screen.width / RefW, Screen.height / RefH);
        float totalScale = scale * _zoom;

        HandlePanInput(scale);

        // 화면 밖 영역이 빈 칸으로 보이지 않도록 기본 어두운 색으로 채움
        DrawRect(new Rect(0, 0, Screen.width, Screen.height), BgDeep);

        var prevMatrix = GUI.matrix;

        // ── 트리 콘텐츠: 화면 중앙 정렬 + zoom + pan ─────
        // 가상 중심(CenterX, CenterY)이 항상 화면 중앙에 오도록 오프셋 적용
        float cx = Screen.width  * 0.5f - TechTreeCatalog.CenterX * totalScale;
        float cy = Screen.height * 0.5f - TechTreeCatalog.CenterY * totalScale;
        GUI.matrix = Matrix4x4.TRS(
            new Vector3(cx + _pan.x * totalScale, cy + _pan.y * totalScale, 0f),
            Quaternion.identity,
            new Vector3(totalScale, totalScale, 1f));

        // 배경 — 가상 좌표계 안에서 그림 (패닝·줌과 함께 움직임)
        DrawWorldBackground();
        DrawCenterGlow();
        DrawConnectors(gsm);
        DrawCenterRoot();

        if (Event.current.type == EventType.Repaint) _hoverNodeId = null;
        DrawNodes(gsm);

        // ── 고정 UI: scale 전용 (패닝 없음) ──────────────
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

        DrawHeader(gsm);
        DrawBottomBar(gsm);
        DrawTooltip(gsm);
        DrawFlash();

        GUI.matrix = prevMatrix;
    }

    private void HandlePanInput(float baseScale)
    {
        var e = Event.current;
        if (e == null) return;

        float totalScale = baseScale * _zoom;
        GetPanBounds(out float bx0, out float bx1, out float by0, out float by1);

        // 스크롤 휠 → 줌 (마우스 위치 기준)
        if (e.type == EventType.ScrollWheel)
        {
            float oldZoom = _zoom;
            _zoom = Mathf.Clamp(_zoom - e.delta.y * zoomSpeed, zoomMin, zoomMax);

            // 마우스 위치 고정 줌 — 화면 중앙 기준 상대 좌표 사용
            float factor  = 1f / _zoom - 1f / oldZoom;
            float screenCx = Screen.width  * 0.5f;
            float screenCy = Screen.height * 0.5f;
            _pan.x += (e.mousePosition.x - screenCx) / baseScale * factor;
            _pan.y += (e.mousePosition.y - screenCy) / baseScale * factor;

            GetPanBounds(out bx0, out bx1, out by0, out by1);
            _pan.x = Mathf.Clamp(_pan.x, bx0, bx1);
            _pan.y = Mathf.Clamp(_pan.y, by0, by1);
            e.Use();
        }

        // 우클릭 드래그: 자유 패닝
        if (e.type == EventType.MouseDown && e.button == 1)
        {
            _dragStart  = e.mousePosition;
            _isDragging = true;
            e.Use();
        }
        else if (e.type == EventType.MouseDrag && e.button == 1 && _isDragging)
        {
            ApplyDragDelta(e, totalScale, bx0, bx1, by0, by1);
        }
        else if (e.type == EventType.MouseUp && e.button == 1)
        {
            _isDragging = false;
        }

        // 좌클릭 드래그: 노드 위가 아닐 때 패닝 (뒤로가기 버튼 영역은 제외)
        if (e.type == EventType.MouseDown && e.button == 0 && string.IsNullOrEmpty(_hoverNodeId))
        {
            var scaledBackRect = new Rect(backBtnX * baseScale, backBtnY * baseScale,
                backBtnSize * baseScale, backBtnSize * baseScale);
            if (!scaledBackRect.Contains(e.mousePosition))
            {
                _dragStart      = e.mousePosition;
                _isLeftDragging = true;
                e.Use();
            }
        }
        else if (e.type == EventType.MouseDrag && e.button == 0 && _isLeftDragging)
        {
            ApplyDragDelta(e, totalScale, bx0, bx1, by0, by1);
        }
        else if (e.type == EventType.MouseUp && e.button == 0)
        {
            _isLeftDragging = false;
        }
    }

    private void ApplyDragDelta(Event e, float s, float bx0, float bx1, float by0, float by1)
    {
        Vector2 delta = (e.mousePosition - _dragStart) / s;
        _pan      += delta;
        _pan.x     = Mathf.Clamp(_pan.x, bx0, bx1);
        _pan.y     = Mathf.Clamp(_pan.y, by0, by1);
        _dragStart = e.mousePosition;
        e.Use();
    }

    private void GetPanBounds(out float bx0, out float bx1, out float by0, out float by1)
    {
        float extX = 0f, extY = 0f;
        var center = new Vector2(TechTreeCatalog.CenterX, TechTreeCatalog.CenterY);
        foreach (var n in TechTreeCatalog.Nodes)
        {
            var sp = ScaledPos(n.pos);
            extX = Mathf.Max(extX, Mathf.Abs(sp.x - center.x));
            extY = Mathf.Max(extY, Mathf.Abs(sp.y - center.y));
        }
        float visHalfW = RefW * 0.5f / _zoom;
        float visHalfH = RefH * 0.5f / _zoom;
        float rx = Mathf.Max(panMargin, extX - visHalfW + panMargin);
        float ry = Mathf.Max(panMargin, extY - visHalfH + panMargin);
        bx0 = -rx; bx1 = rx; by0 = -ry; by1 = ry;
    }

    // ── 헤더 / 바닥 ─────────────────────────────────────────

    private void DrawHeader(GameStateManager gsm)
    {
        var e = Event.current;

        // 타이틀 아이콘 — 좌상단
        var titleRect = new Rect(titleX, titleY, titleW, titleH);
        if (_titleTex != null)
            GUI.DrawTexture(titleRect, _titleTex, ScaleMode.ScaleToFit, alphaBlend: true);
        else
            DrawShadowedLabel(titleRect, "TECH TREE", _titleStyle);

        // 타이틀 위에 마우스 호버 → 안내 툴팁
        bool titleHovered = e != null && e.type == EventType.Repaint && titleRect.Contains(e.mousePosition);
        if (titleHovered)
        {
            const string hint = "노드를 클릭해 랭크업 — 같은 노드를 여러 번 찍을 수 있음";
            var tipStyle = new GUIStyle(GUI.skin.label)
            {
                font = _displayFontKR,
                fontSize = 12, wordWrap = false,
                normal = { textColor = TextMuted },
            };
            float tw = 400f, th = 24f;
            var tipRect = new Rect(titleX, titleY + titleH + 4f, tw, th);
            DrawRect(new Rect(tipRect.x - 4f, tipRect.y - 2f, tw + 8f, th + 4f),
                new Color(0.04f, 0.03f, 0.06f, 0.88f));
            GUI.Label(tipRect, hint, tipStyle);
        }

        // 포인트 — 우상단 (항상 밝게)
        int pts = gsm.TechTree.points;
        if (_pointsIconTex != null)
        {
            var prev = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(pointsIconX, pointsIconY, pointsIconSize, pointsIconSize),
                _pointsIconTex, ScaleMode.ScaleToFit, alphaBlend: true);
            GUI.color = prev;

            var numStyle = new GUIStyle(GUI.skin.label)
            {
                font = _displayFont,
                fontSize = (int)pointsFontSize,
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.92f, 0.60f, 1f) },
            };
            GUI.Label(new Rect(pointsIconX + pointsIconSize + 6f, pointsIconY,
                120f, pointsIconSize), $"{pts}", numStyle);
        }
        else
        {
            var numStyle = new GUIStyle(GUI.skin.label)
            {
                font = _displayFontKR, fontSize = (int)pointsFontSize,
                alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.92f, 0.60f, 1f) },
            };
            GUI.Label(new Rect(RefW - 250f, 14f, 230f, 36f), $"포인트: {pts}", numStyle);
        }
    }

    private void DrawBottomBar(GameStateManager gsm)
    {
        // 돌아가기 — 아이콘만, 패널 없음
        var backRect = new Rect(backBtnX, backBtnY, backBtnSize, backBtnSize);
        var prevColor = GUI.color;
        GUI.color = Color.white;
        if (_backIconTex != null)
        {
            GUI.DrawTexture(
                new Rect(backRect.x + backBtnIconPad, backRect.y + backBtnIconPad,
                    backRect.width - backBtnIconPad * 2f, backRect.height - backBtnIconPad * 2f),
                _backIconTex, ScaleMode.ScaleToFit, alphaBlend: true);
        }
        else
        {
            DrawShadowedLabel(backRect, "←", _backBtnStyle);
        }
        GUI.color = prevColor;

        // 투명 버튼으로 클릭 감지
        if (GUI.Button(backRect, GUIContent.none, GUIStyle.none))
            gsm.ExitTechTree();

    }

    // ── 배경 ────────────────────────────────────────────────

    private void DrawWorldBackground()
    {
        // 가상 좌표계 기준으로 배경 이미지 배치 — 줌·패닝과 함께 움직임
        // RefW의 3배 크기로 잡아 최소 줌(0.6×)에서도 빈 틈이 생기지 않음
        float bgW = RefW * 3f;
        float bgH = RefH * 3f;
        float bx  = TechTreeCatalog.CenterX - bgW * 0.5f;
        float by  = TechTreeCatalog.CenterY - bgH * 0.5f;

        var prev = GUI.color;
        GUI.color = Color.white;
        if (_bgTex != null)
            GUI.DrawTexture(new Rect(bx, by, bgW, bgH), _bgTex, ScaleMode.ScaleAndCrop);
        else
            DrawRect(new Rect(bx, by, bgW, bgH), BgDeep);
        GUI.color = prev;
    }

    // ── 중앙 글로우 + 루트 노드 ─────────────────────────────

    private void DrawCenterGlow()
    {
        Vector2 c = new Vector2(TechTreeCatalog.CenterX, TechTreeCatalog.CenterY);
        float s = 360f;
        var prev = GUI.color;
        GUI.color = OrnamentTint;
        GUI.DrawTexture(new Rect(c.x - s * 0.5f, c.y - s * 0.5f, s, s),
            _radialGlowTex, ScaleMode.StretchToFill, alphaBlend: true);
        GUI.color = prev;
    }

    private void DrawCenterRoot()
    {
        Vector2 c = new Vector2(TechTreeCatalog.CenterX, TechTreeCatalog.CenterY);
        var hexRect = new Rect(c.x - rootWidth * 0.5f, c.y - rootHeight * 0.5f, rootWidth, rootHeight);

        float pulse = 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 1.4f);

        // 외곽 글로우
        float ge = glowExpandCapstone;
        var glowR = new Rect(hexRect.x - ge, hexRect.y - ge, hexRect.width + ge * 2f, hexRect.height + ge * 2f);
        var prev = GUI.color;
        GUI.color = new Color(AccentRoot.r, AccentRoot.g, AccentRoot.b, 0.50f * pulse);
        GUI.DrawTexture(glowR, _radialGlowTex, ScaleMode.StretchToFill, alphaBlend: true);

        // 루트 헥스 스프라이트
        Texture2D rootTex = _hexRootTex ?? _hexGoldTex;
        if (rootTex != null)
        {
            GUI.color = Color.white;
            GUI.DrawTexture(hexRect, rootTex, ScaleMode.StretchToFill, alphaBlend: true);
        }

        // 코어 광점
        float hot = 18f;
        GUI.color = new Color(1f, 0.92f, 0.80f, 0.90f * pulse);
        GUI.DrawTexture(new Rect(c.x - hot * 0.5f, c.y - hot * 0.5f, hot, hot),
            _radialGlowTex, ScaleMode.StretchToFill, alphaBlend: true);

        GUI.color = prev;
    }

    // ── 연결선 ──────────────────────────────────────────────

    private void DrawConnectors(GameStateManager gsm)
    {
        var center = new Vector2(TechTreeCatalog.CenterX, TechTreeCatalog.CenterY);
        foreach (var n in TechTreeCatalog.Nodes)
        {
            if (string.IsNullOrEmpty(n.prereqId)) continue;

            Vector2 from = (n.prereqId == TechTreeCatalog.RootId)
                ? center
                : ScaledPos(TechTreeCatalog.GetNode(n.prereqId)?.pos ?? Vector2.zero);

            if (from == Vector2.zero && n.prereqId != TechTreeCatalog.RootId) continue;

            Vector2 to    = ScaledPos(n.pos);
            float fromR   = (n.prereqId == TechTreeCatalog.RootId)
                ? Mathf.Min(rootWidth, rootHeight) * 0.5f
                : NodeHalfMin(false);
            float toR     = NodeHalfMin(n.isCapstone);
            Vector2 dir   = (to - from).normalized;
            from += dir * fromR;
            to   -= dir * toR;

            bool prereqUnlocked = (n.prereqId == TechTreeCatalog.RootId) || gsm.TechTree.IsUnlocked(n.prereqId);
            bool nodeUnlocked   = gsm.TechTree.IsUnlocked(n.id);

            Color c; float thick; bool dotted;
            if (nodeUnlocked)        { c = LineUnlocked; thick = lineThickUnlocked; dotted = false; }
            else if (prereqUnlocked) { c = LineUnlocked; thick = lineThickDotted;   dotted = true; }
            else                     { c = LineLocked;   thick = lineThickLocked;   dotted = true; }

            DrawLine(from, to, c, thick, dotted);
        }
    }

    // ── 노드 ────────────────────────────────────────────────

    private void DrawNodes(GameStateManager gsm)
    {
        foreach (var n in TechTreeCatalog.Nodes)
            DrawNode(n, gsm);
    }

    private void DrawNode(TechNode node, GameStateManager gsm)
    {
        int  rank      = gsm.TechTree.GetRank(node.id);
        bool maxed     = rank >= node.maxRank;
        bool hasRank   = rank > 0;
        bool canRankUp = gsm.TechTree.CanRankUp(node);
        bool isLocked  = !hasRank && !canRankUp;

        Vector2 pos = ScaledPos(node.pos);
        Vector2 sz  = NodeSize(node.isCapstone);
        var hexRect = new Rect(pos.x - sz.x * 0.5f, pos.y - sz.y * 0.5f, sz.x, sz.y);

        // 호버 검사
        bool hovered = false;
        if (Event.current != null && Event.current.type == EventType.Repaint)
        {
            if (hexRect.Contains(Event.current.mousePosition))
            {
                hovered = true;
                _hoverNodeId = node.id;
            }
        }

        // 방향 액센트 색
        Color accent = AccentOf(node.direction);

        // 외곽 글로우 — 잠금 상태엔 항상 끔
        bool drawGlow = !isLocked && (node.isCapstone || maxed || hovered || canRankUp);
        if (drawGlow)
        {
            float ge = node.isCapstone ? glowExpandCapstone : glowExpandNormal;
            var glowR = new Rect(hexRect.x - ge, hexRect.y - ge, hexRect.width + ge * 2f, hexRect.height + ge * 2f);
            float pulse = node.isCapstone
                ? (0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 1.6f))
                : (hovered ? 1f : (canRankUp ? 0.85f : (maxed ? 0.70f : 0.40f)));
            float a = node.isCapstone ? 0.55f : (canRankUp || maxed ? 0.50f : 0.35f);
            var prev = GUI.color;
            GUI.color = new Color(accent.r, accent.g, accent.b, a * pulse);
            GUI.DrawTexture(glowR, _radialGlowTex, ScaleMode.StretchToFill, alphaBlend: true);
            GUI.color = prev;
        }

        // 헥스 스프라이트
        Texture2D hexTex = GetHexSprite(node.direction, isLocked);
        float hexAlpha = maxed ? 1f : hasRank ? 0.95f : canRankUp ? 0.88f : 0.55f;
        if (hovered) hexAlpha = Mathf.Min(1f, hexAlpha + 0.10f);

        var prev2 = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, hexAlpha);
        GUI.DrawTexture(hexRect, hexTex, ScaleMode.StretchToFill, alphaBlend: true);
        GUI.color = prev2;

        // 아이콘 (방향별 심볼)
        DrawNodeIcon(node, hexRect, accent, hasRank, maxed);

        // 랭크 배지
        DrawRankBadge(node, rank, hasRank, maxed, canRankUp, pos, sz.y * 0.5f);


        // 클릭 판정
        if (Event.current != null && Event.current.type == EventType.MouseDown && Event.current.button == 0)
        {
            if (hexRect.Contains(Event.current.mousePosition))
            {
                if (maxed)
                    Flash($"{node.name} — 이미 최대 랭크 ({rank}/{node.maxRank})");
                else if (canRankUp)
                {
                    if (gsm.TechTree.TryRankUp(node))
                        Flash($"✓ {node.name} 랭크업 ({gsm.TechTree.GetRank(node.id)}/{node.maxRank}) - {node.perRankCost}pt");
                }
                else if (!string.IsNullOrEmpty(node.prereqId)
                         && node.prereqId != TechTreeCatalog.RootId
                         && !gsm.TechTree.IsUnlocked(node.prereqId))
                {
                    var pre = TechTreeCatalog.GetNode(node.prereqId);
                    Flash($"전제 노드 먼저: {(pre != null ? pre.name : node.prereqId)}");
                }
                else
                    Flash($"포인트 부족 ({node.perRankCost}pt 필요, 보유 {gsm.TechTree.points})");

                Event.current.Use();
            }
        }
    }

    // ── 스프라이트 선택 ─────────────────────────────────────

    private Texture2D GetHexSprite(TechDirection dir, bool isLocked)
    {
        if (isLocked) return _hexGrayTex ?? Texture2D.whiteTexture;
        return GetDirHex(dir);
    }

    private Texture2D GetDirHex(TechDirection dir) => dir switch
    {
        TechDirection.Right  => _hexAttackTex ?? Texture2D.whiteTexture, // 핑크빨강 — 공격
        TechDirection.Left   => _hexBlueTex   ?? Texture2D.whiteTexture, // 파랑 — 방어
        TechDirection.Up     => _hexGoldTex   ?? Texture2D.whiteTexture, // 노랑 — 공룡
        TechDirection.Down   => _hexOrangeTex ?? Texture2D.whiteTexture, // 주황 — 유틸
        _                    => _hexRootTex   ?? Texture2D.whiteTexture, // 보라 — 센터
    };

    // ── 아이콘 (노드 ID 기반 실제 텍스처) ─────────────────────

    private void DrawNodeIcon(TechNode node, Rect hexRect, Color accent, bool hasRank, bool maxed)
    {
        Vector2 c = hexRect.center;
        var prev = GUI.color;

        _nodeIcons.TryGetValue(node.id, out Texture2D icon);

        if (icon != null)
        {
            float available = Mathf.Min(hexRect.width, hexRect.height) - nodeIconPadding * 2f;
            float size = available * (node.isCapstone ? nodeIconRatioCapstone : nodeIconRatio);
            var iconRect = new Rect(c.x - size * 0.5f, c.y - size * 0.5f, size, size);
            float alpha = maxed ? 1f : hasRank ? 0.95f : 0.55f;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, alphaBlend: true);
        }
        else
        {
            // 폴백 — 액센트 글로우 디스크
            float diskSize = node.isCapstone ? 28f : 20f;
            GUI.color = new Color(accent.r, accent.g, accent.b, hasRank ? 0.80f : 0.40f);
            GUI.DrawTexture(new Rect(c.x - diskSize * 0.5f, c.y - diskSize * 0.5f, diskSize, diskSize),
                _radialGlowTex, ScaleMode.StretchToFill, alphaBlend: true);
        }

        // 캡스톤 코어 광점
        if (node.isCapstone && (hasRank || maxed))
        {
            float hot = 8f;
            GUI.color = new Color(1f, 0.98f, 0.85f, 0.85f);
            GUI.DrawTexture(new Rect(c.x - hot * 0.5f, c.y - hot * 0.5f, hot, hot),
                _radialGlowTex, ScaleMode.StretchToFill, alphaBlend: true);
        }

        GUI.color = prev;
    }

    // ── 랭크 배지 ───────────────────────────────────────────

    private void DrawRankBadge(TechNode node, int rank, bool hasRank, bool maxed, bool canRankUp, Vector2 pos, float halfH)
    {
        float w = 38f, h = 16f;
        var br = new Rect(pos.x - w * 0.5f, pos.y + halfH + 2f, w, h);

        Color bg = maxed    ? new Color(0.40f, 0.22f, 0.08f, 0.95f)
                 : hasRank  ? new Color(0.18f, 0.13f, 0.08f, 0.92f)
                 : canRankUp? new Color(0.12f, 0.10f, 0.06f, 0.92f)
                 :            new Color(0.08f, 0.07f, 0.09f, 0.88f);
        DrawRect(br, bg);

        Color borderC = maxed    ? new Color(1f, 0.60f, 0.18f)
                      : hasRank  ? new Color(1f, 0.85f, 0.45f)
                      : canRankUp? new Color(0.95f, 0.78f, 0.40f)
                      :            new Color(0.30f, 0.27f, 0.25f, 0.85f);
        DrawHollowRect(br, borderC, 1f);

        Color txtC = maxed    ? new Color(1f, 0.92f, 0.65f)
                   : canRankUp? new Color(1f, 0.85f, 0.45f)
                   : hasRank  ? TextWarm
                   :            TextDim;

        var s = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11, alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = txtC },
        };
        GUI.Label(br, $"{rank}/{node.maxRank}", s);
    }

    // ── 툴팁 ────────────────────────────────────────────────

    private void DrawTooltip(GameStateManager gsm)
    {
        if (string.IsNullOrEmpty(_hoverNodeId)) return;
        if (Event.current == null || Event.current.type != EventType.Repaint) return;

        var node = TechTreeCatalog.GetNode(_hoverNodeId);
        if (node == null) return;

        // BattleUI 유물/포션 툴팁과 동일 톤: 다크 바이올렛 배경 + 브론즈 1px 트림.
        const float TipW = 210f;
        bool hasDesc = !string.IsNullOrEmpty(node.description);
        float descH  = hasDesc ? 32f : 0f;
        float tipH   = 6f + 20f + (hasDesc ? 6f + descH : 0f) + 4f + 16f + 6f;

        Vector2 mp = Event.current.mousePosition;
        float tx = Mathf.Round(Mathf.Clamp(mp.x + 18f, 10f, RefW - TipW - 10f));
        float ty = Mathf.Round(mp.y + 18f);
        if (ty + tipH > RefH - 8f) ty = RefH - tipH - 8f;
        var tipRect = new Rect(tx, ty, TipW, tipH);

        DrawRect(tipRect, new Color(0.059f, 0.043f, 0.137f, 1f));
        var trimCol = new Color(0.82f, 0.68f, 0.38f, 0.7f);
        DrawRect(new Rect(tipRect.x, tipRect.y, tipRect.width, 1f), trimCol);
        DrawRect(new Rect(tipRect.x, tipRect.yMax - 1f, tipRect.width, 1f), trimCol);
        DrawRect(new Rect(tipRect.x, tipRect.y + 1f, 1f, tipRect.height - 2f), trimCol);
        DrawRect(new Rect(tipRect.xMax - 1f, tipRect.y + 1f, 1f, tipRect.height - 2f), trimCol);

        GUI.Label(new Rect(tipRect.x + 8f, tipRect.y + 6f, TipW - 16f, 20f),
            $"{node.name}{(node.isCapstone ? "  ★" : "")}",
            new GUIStyle(GUI.skin.label)
            {
                font = _displayFontKR,
                fontSize = 13, fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
            });

        float cursorY = tipRect.y + 26f;
        if (hasDesc)
        {
            GUI.Label(new Rect(tipRect.x + 8f, cursorY, TipW - 16f, descH),
                node.description,
                new GUIStyle(GUI.skin.label)
                {
                    font = _displayFontKR,
                    fontSize = 11, wordWrap = true,
                    normal = { textColor = new Color(0.80f, 0.76f, 0.88f) },
                });
            cursorY += descH + 4f;
        }

        int curRank = gsm.TechTree.GetRank(node.id);
        string costLine = curRank >= node.maxRank
            ? $"최대 랭크 ({curRank}/{node.maxRank})"
            : $"랭크 {curRank}/{node.maxRank}  ·  다음: {node.perRankCost} pt";
        GUI.Label(new Rect(tipRect.x + 8f, cursorY, TipW - 16f, 16f),
            costLine,
            new GUIStyle(GUI.skin.label)
            {
                font = _displayFontKR,
                fontSize = 11,
                normal = { textColor = curRank >= node.maxRank ? new Color(1f, 0.78f, 0.30f) : new Color(1f, 0.88f, 0.45f) },
            });
    }

    // ── 플래시 메시지 ───────────────────────────────────────

    private void DrawFlash()
    {
        if (string.IsNullOrEmpty(_flashMessage)) return;
        if (Time.unscaledTime > _flashUntil) { _flashMessage = null; return; }

        float alpha = Mathf.Clamp01(_flashUntil - Time.unscaledTime);
        var rect = new Rect(RefW * 0.5f - 280f, 70f, 560f, 28f);
        var prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.7f * alpha);
        GUI.DrawTexture(rect, _whiteTex);
        GUI.color = Color.white;
        GUI.Label(rect, _flashMessage, new GUIStyle(GUI.skin.label)
        {
            font = _displayFontKR,
            fontSize = 14, alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(TextWarm.r, TextWarm.g, TextWarm.b, alpha) },
        });
        GUI.color = prev;
    }

    private void Flash(string msg) { _flashMessage = msg; _flashUntil = Time.unscaledTime + 1.6f; }

    // ── 헬퍼 ────────────────────────────────────────────────

    private Vector2 NodeSize(bool isCapstone) => isCapstone
        ? new Vector2(nodeWidthCapstone, nodeHeightCapstone)
        : new Vector2(nodeWidthNormal, nodeHeightNormal);

    private float NodeHalfMin(bool isCapstone)
    {
        var s = NodeSize(isCapstone);
        return Mathf.Min(s.x, s.y) * 0.5f;
    }

    private Vector2 ScaledPos(Vector2 nodePos)
    {
        var c = new Vector2(TechTreeCatalog.CenterX, TechTreeCatalog.CenterY);
        return c + (nodePos - c) * nodeSpacingScale;
    }

    private static Color AccentOf(TechDirection d) => d switch
    {
        TechDirection.Right => AccentRight,
        TechDirection.Left  => AccentLeft,
        TechDirection.Up    => AccentUp,
        TechDirection.Down  => AccentDown,
        _                   => AccentRoot,
    };

    private void DrawRect(Rect r, Color c)
    {
        var prev = GUI.color; GUI.color = c;
        GUI.DrawTexture(r, _whiteTex);
        GUI.color = prev;
    }

    private void DrawHollowRect(Rect r, Color c, float t)
    {
        DrawRect(new Rect(r.x, r.y, r.width, t), c);
        DrawRect(new Rect(r.x, r.yMax - t, r.width, t), c);
        DrawRect(new Rect(r.x, r.y, t, r.height), c);
        DrawRect(new Rect(r.xMax - t, r.y, t, r.height), c);
    }

    private void DrawShadowedLabel(Rect r, string text, GUIStyle style)
    {
        var prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.85f);
        GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), text, style);
        GUI.color = prev;
        GUI.Label(r, text, style);
    }

    /// <summary>두 점 사이를 회전된 사각형으로 근사. dotted=true 시 점선.</summary>
    private void DrawLine(Vector2 a, Vector2 b, Color color, float thickness, bool dotted)
    {
        float dx = b.x - a.x; float dy = b.y - a.y;
        float len = Mathf.Sqrt(dx * dx + dy * dy);
        if (len < 0.01f) return;

        var prevMatrix = GUI.matrix;
        var prevColor  = GUI.color;
        GUI.matrix = prevMatrix
            * Matrix4x4.Translate(new Vector3(a.x, a.y, 0))
            * Matrix4x4.Rotate(Quaternion.Euler(0, 0, Mathf.Atan2(dy, dx) * Mathf.Rad2Deg))
            * Matrix4x4.Translate(new Vector3(-a.x, -a.y, 0));

        GUI.color = color;
        if (!dotted)
        {
            GUI.DrawTexture(new Rect(a.x, a.y - thickness * 0.5f, len, thickness), _whiteTex);
        }
        else
        {
            const float dash = 6f, gap = 5f;
            float t = 0f;
            while (t < len)
            {
                float seg = Mathf.Min(dash, len - t);
                GUI.DrawTexture(new Rect(a.x + t, a.y - thickness * 0.5f, seg, thickness), _whiteTex);
                t += dash + gap;
            }
        }
        GUI.matrix = prevMatrix;
        GUI.color  = prevColor;
    }

    // ── 절차 텍스처 ─────────────────────────────────────────

    private static Texture2D MakeRadialGlow(int size)
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
                float dx = (x - c) / maxR; float dy = (y - c) / maxR;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                a = a * a * (3f - 2f * a);
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    // ── 스타일 ──────────────────────────────────────────────

    private void EnsureStyles()
    {
        if (_stylesReady) return;

        _titleStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFont, fontSize = 28,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = TextWarm },
        };
        _pointsStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFont, fontSize = 22,
            alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold,
            normal = { textColor = TextWarm },
        };
        _branchLabelStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFont, fontSize = 26,
            alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold,
            normal = { textColor = TextWarm },
        };
        _rankCounterStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11, alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = TextWarm },
        };
        _smallBtnStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 13, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold,
            normal  = { textColor = TextWarm, background = null },
            border  = new RectOffset(0, 0, 0, 0),
            padding = new RectOffset(4, 4, 2, 2),
        };
        _backBtnStyle = new GUIStyle(_smallBtnStyle) { fontSize = 16 };

        // GUI.skin 기본 hover/active state의 색·배경 swap을 차단해
        // 호버 시 텍스트가 미세하게 흔들리는 느낌을 없앤다.
        LockHoverState(_titleStyle);
        LockHoverState(_pointsStyle);
        LockHoverState(_branchLabelStyle);
        LockHoverState(_rankCounterStyle);
        LockHoverState(_smallBtnStyle);
        LockHoverState(_backBtnStyle);

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
