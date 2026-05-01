using System.Collections.Generic;
using DianoCard.Game;
using UnityEngine;

/// <summary>
/// 테크트리(메타 진행) 화면 — Last Epoch 어빌리티 트리 톤.
/// 중앙 글로우 헥스 루트 + 4방위(우=공격 / 좌=방어 / 상=경제 / 하=운영) 노드 배치.
/// 단일 포인트 통화, 노드별 랭크(0..maxRank), 헥사곤 노드 + 점선/실선 연결선.
///
/// 임시 UI(MVP) — 16노드 + 캡스톤 4. 효과 적용 로직은 별도(런 시작 시 RunState에 반영하는 작업은 미구현).
/// </summary>
public class TechTreeUI : MonoBehaviour
{
    private const float RefW = 1280f;
    private const float RefH = 720f;

    // ── 색 팔레트 ──────────────────────────────────────────
    private static readonly Color BgDeep        = new Color(0.04f, 0.03f, 0.06f, 0.96f);
    private static readonly Color OrnamentTint  = new Color(0.45f, 0.35f, 0.18f, 0.18f);
    private static readonly Color TextWarm      = new Color(0.93f, 0.86f, 0.66f, 1f);
    private static readonly Color TextMuted     = new Color(0.70f, 0.66f, 0.55f, 1f);
    private static readonly Color TextDim       = new Color(0.55f, 0.50f, 0.45f, 1f);

    // 노드 상태별 색
    private static readonly Color HexLockedFill    = new Color(0.10f, 0.09f, 0.11f, 0.95f);
    private static readonly Color HexLockedBorder  = new Color(0.32f, 0.28f, 0.25f, 0.85f);
    private static readonly Color HexAvailFill     = new Color(0.18f, 0.14f, 0.10f, 0.95f);
    private static readonly Color HexAvailBorder   = new Color(0.95f, 0.78f, 0.40f, 1f);
    private static readonly Color HexFilledFill    = new Color(0.22f, 0.16f, 0.10f, 0.98f);
    private static readonly Color HexFilledBorder  = new Color(1f,    0.85f, 0.45f, 1f);
    private static readonly Color HexMaxedFill     = new Color(0.32f, 0.20f, 0.10f, 1f);
    private static readonly Color HexMaxedBorder   = new Color(1f,    0.60f, 0.18f, 1f);

    private static readonly Color LineLocked   = new Color(0.40f, 0.36f, 0.30f, 0.55f);
    private static readonly Color LineUnlocked = new Color(1f,    0.78f, 0.40f, 0.85f);

    // 방향별 액센트 (라벨 / 캡스톤 글로우 / 노드 아이콘)
    private static readonly Color AccentRight = new Color(0.95f, 0.45f, 0.30f); // 공격(불·적)
    private static readonly Color AccentLeft  = new Color(0.45f, 0.70f, 1.00f); // 방어(청)
    private static readonly Color AccentUp    = new Color(1.00f, 0.85f, 0.40f); // 경제(금)
    private static readonly Color AccentDown  = new Color(0.65f, 0.50f, 0.95f); // 운영(보라)
    private static readonly Color AccentRoot  = new Color(1f,    0.55f, 0.20f); // 루트(주황)

    // ── 절차 텍스처 ─────────────────────────────────────────
    private Texture2D _whiteTex;
    private Texture2D _hexFillTex;     // 알파 마스크: 헥스 내부=1, 밖=0 (스무드 AA 엣지)
    private Texture2D _hexOuterTex;    // 외곽 글로우용 (반지름 1.4×, soft falloff)
    private Texture2D _radialGlowTex;  // 일반 글로우 원
    private Font _displayFont;

    private GUIStyle _titleStyle;
    private GUIStyle _pointsStyle;
    private GUIStyle _branchLabelStyle;
    private GUIStyle _nodeNameStyle;
    private GUIStyle _rankCounterStyle;
    private GUIStyle _smallBtnStyle;
    private GUIStyle _backBtnStyle;
    private bool _stylesReady;

    private string _hoverNodeId;
    private string _flashMessage;
    private float _flashUntil;

    void Start()
    {
        _whiteTex = Texture2D.whiteTexture;
        _displayFont = Resources.Load<Font>("Fonts/IMFellEnglish-Regular");
        _hexFillTex    = MakeFlatTopHexTex(192, 0.985f, smoothEdge: 0.025f);
        _hexOuterTex   = MakeFlatTopHexTex(192, 1.20f,  smoothEdge: 0.40f);
        _radialGlowTex = MakeRadialGlow(96);
    }

    void OnDestroy()
    {
        if (_hexFillTex != null) Destroy(_hexFillTex);
        if (_hexOuterTex != null) Destroy(_hexOuterTex);
        if (_radialGlowTex != null) Destroy(_radialGlowTex);
    }

    void OnGUI()
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null || gsm.State != GameState.TechTree) return;
        if (gsm.TechTree == null) return;

        EnsureStyles();

        DrawRect(new Rect(0, 0, Screen.width, Screen.height), BgDeep);

        float scale = Mathf.Min(Screen.width / RefW, Screen.height / RefH);
        var prevMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));

        DrawCenterOrnament();
        DrawConnectors(gsm);
        DrawCenterRoot(gsm);
        DrawDirectionLabels();

        // 호버 갱신을 위해 hoverId는 매 프레임 비움 (Repaint 단계에서만 검사)
        _hoverNodeId = null;

        DrawNodes(gsm);

        DrawHeader(gsm);
        DrawBottomBar(gsm);
        DrawTooltip(gsm);
        DrawFlash();

        GUI.matrix = prevMatrix;
    }

    // ── 헤더 + 바닥 ─────────────────────────────────────────

    private void DrawHeader(GameStateManager gsm)
    {
        var titleRect = new Rect(0, 14f, RefW, 36f);
        DrawShadowedLabel(titleRect, "TECH TREE", _titleStyle);

        // 포인트 — 우상단 큰 글씨
        int pts = gsm.TechTree.points;
        var ptsRect = new Rect(RefW - 250f, 14f, 230f, 36f);
        var prev = GUI.color;
        GUI.color = pts > 0 ? new Color(1f, 0.85f, 0.45f) : TextDim;
        GUI.Label(ptsRect, $"포인트: {pts}", _pointsStyle);
        GUI.color = prev;

        var subRect = new Rect(0, 50f, RefW, 18f);
        var sub = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = TextMuted },
        };
        GUI.Label(subRect, "노드를 클릭해 랭크업 — 같은 노드를 여러 번 찍을 수 있음", sub);
    }

    private void DrawBottomBar(GameStateManager gsm)
    {
        // Back
        var backRect = new Rect(40f, RefH - 70f, 160f, 44f);
        DrawRect(backRect, new Color(0.10f, 0.08f, 0.13f, 0.9f));
        DrawHollowRect(backRect, new Color(0.85f, 0.75f, 0.45f, 0.7f), 1.5f);
        if (GUI.Button(backRect, "← 돌아가기", _backBtnStyle))
        {
            gsm.ExitTechTree();
        }

        // 디버그 — 조건 시스템 미구현이라 임시 포인트 부여 버튼
        var devLabelRect = new Rect(RefW - 580f, RefH - 75f, 200f, 18f);
        GUI.Label(devLabelRect, "[ DEV — 임시 포인트 부여 ]", new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = TextDim },
        });

        var p1 = new Rect(RefW - 580f, RefH - 56f, 110f, 32f);
        DrawDevButton(p1, "+1 포인트", () => { gsm.TechTree.GrantPoints(1); Flash("+1 포인트"); });
        var p5 = new Rect(RefW - 460f, RefH - 56f, 110f, 32f);
        DrawDevButton(p5, "+5 포인트", () => { gsm.TechTree.GrantPoints(5); Flash("+5 포인트"); });

        // 리셋
        var resetRect = new Rect(RefW - 180f, RefH - 56f, 140f, 32f);
        DrawRect(resetRect, new Color(0.08f, 0.07f, 0.10f, 0.9f));
        DrawHollowRect(resetRect, new Color(0.55f, 0.45f, 0.45f, 0.8f), 1.5f);
        if (GUI.Button(resetRect, "전체 리셋", _smallBtnStyle))
        {
            gsm.TechTree.ResetAll();
            Flash("테크트리 진척이 모두 초기화되었습니다");
        }
    }

    private void DrawDevButton(Rect r, string label, System.Action onClick)
    {
        DrawRect(r, new Color(0.08f, 0.07f, 0.10f, 0.9f));
        DrawHollowRect(r, new Color(0.85f, 0.75f, 0.45f, 0.7f), 1.5f);
        if (GUI.Button(r, label, _smallBtnStyle)) onClick?.Invoke();
    }

    // ── 중앙 오너먼트 + 루트 ────────────────────────────────

    private void DrawCenterOrnament()
    {
        // 중앙 주변 옅은 황금 발광 — 루트가 빛나는 느낌만 살짝
        Vector2 c = new Vector2(TechTreeCatalog.CenterX, TechTreeCatalog.CenterY);
        float s = 360f;
        var prev = GUI.color;
        GUI.color = OrnamentTint;
        GUI.DrawTexture(new Rect(c.x - s * 0.5f, c.y - s * 0.5f, s, s), _radialGlowTex, ScaleMode.StretchToFill, alphaBlend: true);
        GUI.color = prev;
    }

    private void DrawCenterRoot(GameStateManager gsm)
    {
        Vector2 c = new Vector2(TechTreeCatalog.CenterX, TechTreeCatalog.CenterY);
        float r = 56f;
        var hexRect = new Rect(c.x - r, c.y - r * 0.866f, r * 2f, r * 2f * 0.866f);

        // 외곽 글로우 (펄스)
        float pulse = 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 1.4f);
        var glowR = new Rect(hexRect.x - 22f, hexRect.y - 22f, hexRect.width + 44f, hexRect.height + 44f);
        var prev = GUI.color;
        GUI.color = new Color(AccentRoot.r, AccentRoot.g, AccentRoot.b, 0.45f * pulse);
        GUI.DrawTexture(glowR, _hexOuterTex, ScaleMode.StretchToFill, alphaBlend: true);

        // 본체
        GUI.color = new Color(0.20f, 0.10f, 0.06f, 1f);
        GUI.DrawTexture(hexRect, _hexFillTex, ScaleMode.StretchToFill, alphaBlend: true);

        // 외곽선 (별도 생성 비용 안 들이려고 살짝 큰 hex를 어두운 위에 밝게 두 번 그림)
        GUI.color = new Color(AccentRoot.r, AccentRoot.g, AccentRoot.b, 0.95f);
        var outline = new Rect(hexRect.x - 2f, hexRect.y - 2f * 0.866f, hexRect.width + 4f, hexRect.height + 4f * 0.866f);
        GUI.DrawTexture(outline, _hexFillTex, ScaleMode.StretchToFill, alphaBlend: true);
        GUI.color = new Color(0.20f, 0.10f, 0.06f, 1f);
        GUI.DrawTexture(hexRect, _hexFillTex, ScaleMode.StretchToFill, alphaBlend: true);

        // 코어 — 작은 주황 원
        float coreSize = 36f;
        GUI.color = new Color(AccentRoot.r, AccentRoot.g, AccentRoot.b, 0.85f);
        GUI.DrawTexture(new Rect(c.x - coreSize * 0.5f, c.y - coreSize * 0.5f, coreSize, coreSize),
            _radialGlowTex, ScaleMode.StretchToFill, alphaBlend: true);
        float hot = 16f;
        GUI.color = new Color(1f, 0.95f, 0.75f, 0.85f);
        GUI.DrawTexture(new Rect(c.x - hot * 0.5f, c.y - hot * 0.5f, hot, hot),
            _radialGlowTex, ScaleMode.StretchToFill, alphaBlend: true);

        GUI.color = prev;
    }

    // ── 연결선 ──────────────────────────────────────────────

    private void DrawConnectors(GameStateManager gsm)
    {
        foreach (var n in TechTreeCatalog.Nodes)
        {
            if (string.IsNullOrEmpty(n.prereqId)) continue;

            Vector2 from;
            if (n.prereqId == TechTreeCatalog.RootId)
            {
                from = new Vector2(TechTreeCatalog.CenterX, TechTreeCatalog.CenterY);
            }
            else
            {
                var pre = TechTreeCatalog.GetNode(n.prereqId);
                if (pre == null) continue;
                from = pre.pos;
            }
            Vector2 to = n.pos;

            // 노드 가장자리에서 시작/끝 — from에서 to 방향으로 노드 반경만큼 안쪽으로 당김
            float fromR = (n.prereqId == TechTreeCatalog.RootId) ? 56f : NodeRadius(false);
            float toR   = NodeRadius(n.isCapstone);
            Vector2 dir = (to - from).normalized;
            from += dir * fromR;
            to   -= dir * toR;

            bool prereqUnlocked = (n.prereqId == TechTreeCatalog.RootId) ||
                                   gsm.TechTree.IsUnlocked(n.prereqId);
            bool nodeUnlocked   = gsm.TechTree.IsUnlocked(n.id);

            Color c;
            float thick;
            bool dotted;
            if (nodeUnlocked) { c = LineUnlocked; thick = 2.2f; dotted = false; }
            else if (prereqUnlocked) { c = LineUnlocked; thick = 1.8f; dotted = true; }
            else { c = LineLocked; thick = 1.5f; dotted = true; }

            DrawLine(from, to, c, thick, dotted);
        }
    }

    // ── 노드 그리기 ─────────────────────────────────────────

    private void DrawNodes(GameStateManager gsm)
    {
        foreach (var n in TechTreeCatalog.Nodes)
        {
            DrawNode(n, gsm);
        }
    }

    private void DrawNode(TechNode node, GameStateManager gsm)
    {
        int rank = gsm.TechTree.GetRank(node.id);
        bool maxed     = rank >= node.maxRank;
        bool hasRank   = rank > 0;
        bool canRankUp = gsm.TechTree.CanRankUp(node);

        float r = NodeRadius(node.isCapstone);
        var hexRect = new Rect(node.pos.x - r, node.pos.y - r * 0.866f, r * 2f, r * 2f * 0.866f);

        // 호버 검사
        bool hovered = false;
        if (Event.current != null && Event.current.type == EventType.Repaint)
        {
            // 가까운 점-거리 기반 (헥스 정확 검사 대신 외접원 근사 — 충분)
            Vector2 mp = Event.current.mousePosition;
            if ((mp - node.pos).sqrMagnitude <= r * r)
            {
                hovered = true;
                _hoverNodeId = node.id;
            }
        }

        // 외곽 글로우 — 캡스톤은 항상, 일반은 hover/maxed/canRankUp 시
        Color accent = AccentOf(node.direction);
        bool drawGlow = node.isCapstone || maxed || hovered || canRankUp;
        if (drawGlow)
        {
            float glowExpand = node.isCapstone ? 28f : 18f;
            var glowR = new Rect(hexRect.x - glowExpand, hexRect.y - glowExpand * 0.866f,
                                 hexRect.width + glowExpand * 2f, hexRect.height + glowExpand * 2f * 0.866f);
            float pulse = node.isCapstone
                ? (0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 1.6f))
                : (hovered ? 1f : (canRankUp ? 0.85f : (maxed ? 0.7f : 0.4f)));
            float a = node.isCapstone ? 0.55f : (canRankUp || maxed ? 0.50f : 0.35f);
            DrawHexAt(glowR, new Color(accent.r, accent.g, accent.b, a * pulse), _hexOuterTex);
        }

        // 본체 — 상태별 색
        Color fill, border;
        if (maxed)         { fill = HexMaxedFill;  border = HexMaxedBorder; }
        else if (hasRank)  { fill = HexFilledFill; border = HexFilledBorder; }
        else if (canRankUp){ fill = HexAvailFill;  border = HexAvailBorder; }
        else               { fill = HexLockedFill; border = HexLockedBorder; }
        if (hovered) { fill = Brighten(fill, 0.12f); border = Brighten(border, 0.18f); }

        // 헥스 채우기: 외곽선 hex(=border 색) → 안쪽 hex(=fill 색) 2단 합성으로 외곽선 흉내
        DrawHexAt(hexRect, border, _hexFillTex);
        float inset = node.isCapstone ? 4f : 3f;
        var inner = new Rect(hexRect.x + inset, hexRect.y + inset * 0.866f,
                             hexRect.width - inset * 2f, hexRect.height - inset * 2f * 0.866f);
        DrawHexAt(inner, fill, _hexFillTex);

        // 아이콘 — 방향별 절차 심볼
        DrawNodeIcon(node, hexRect, accent, hasRank, maxed);

        // 랭크 카운터 (헥스 아래 작은 박스)
        DrawRankBadge(node, rank, hasRank, maxed, canRankUp);

        // 노드 이름 (캡스톤만 라벨 노출 — 일반 노드는 툴팁으로 봄)
        if (node.isCapstone)
        {
            float yOff = node.direction == TechDirection.Up   ? -38f
                       : node.direction == TechDirection.Down ?  38f
                       : 0f;
            float xOff = node.direction == TechDirection.Right ?  r + 24f
                       : node.direction == TechDirection.Left  ? -r - 24f
                       : 0f;
            TextAnchor anchor = node.direction == TechDirection.Right ? TextAnchor.MiddleLeft
                              : node.direction == TechDirection.Left  ? TextAnchor.MiddleRight
                              : TextAnchor.MiddleCenter;
            float w = 180f;
            float labelX = node.direction == TechDirection.Left ? node.pos.x - r - 24f - w : node.pos.x + xOff;
            var labelRect = new Rect(labelX, node.pos.y + yOff - 10f, w, 20f);
            var s = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                alignment = anchor,
                fontStyle = FontStyle.Bold,
                normal = { textColor = hasRank ? TextWarm : new Color(0.85f, 0.78f, 0.55f, 0.85f) },
            };
            GUI.Label(labelRect, node.name, s);
        }

        // 클릭 — 헥스 외접원 영역
        var hitRect = new Rect(node.pos.x - r, node.pos.y - r, r * 2f, r * 2f);
        if (Event.current != null && Event.current.type == EventType.MouseDown && Event.current.button == 0)
        {
            if ((Event.current.mousePosition - node.pos).sqrMagnitude <= r * r)
            {
                if (maxed) Flash($"{node.name} — 이미 최대 랭크 ({rank}/{node.maxRank})");
                else if (canRankUp)
                {
                    if (gsm.TechTree.TryRankUp(node))
                    {
                        int newRank = gsm.TechTree.GetRank(node.id);
                        Flash($"✓ {node.name} 랭크업 ({newRank}/{node.maxRank}) - {node.perRankCost}pt");
                    }
                }
                else if (!string.IsNullOrEmpty(node.prereqId) && node.prereqId != TechTreeCatalog.RootId
                         && !gsm.TechTree.IsUnlocked(node.prereqId))
                {
                    var pre = TechTreeCatalog.GetNode(node.prereqId);
                    Flash($"전제 노드 먼저: {(pre != null ? pre.name : node.prereqId)}");
                }
                else
                {
                    Flash($"포인트 부족 ({node.perRankCost}pt 필요, 보유 {gsm.TechTree.points})");
                }
                Event.current.Use();
            }
        }
    }

    private void DrawNodeIcon(TechNode node, Rect hexRect, Color accent, bool hasRank, bool maxed)
    {
        Vector2 c = new Vector2(hexRect.center.x, hexRect.center.y);
        float strength = hasRank ? 1f : 0.55f;
        Color tint = new Color(accent.r, accent.g, accent.b, strength);

        // 일관된 외광 — 작은 디스크
        float diskSize = node.isCapstone ? 30f : 22f;
        var prev = GUI.color;
        GUI.color = new Color(tint.r, tint.g, tint.b, hasRank ? 0.85f : 0.45f);
        GUI.DrawTexture(new Rect(c.x - diskSize * 0.5f, c.y - diskSize * 0.5f, diskSize, diskSize),
            _radialGlowTex, ScaleMode.StretchToFill, alphaBlend: true);

        // 방향별 상징 — 단순 도형 조합
        Color symColor = hasRank
            ? new Color(1f, 0.95f, 0.75f, 1f)
            : new Color(0.85f, 0.80f, 0.65f, 0.55f);

        switch (node.direction)
        {
            case TechDirection.Right: DrawSwordSymbol(c, node.isCapstone, symColor); break;
            case TechDirection.Left:  DrawShieldSymbol(c, node.isCapstone, symColor); break;
            case TechDirection.Up:    DrawCoinSymbol(c, node.isCapstone, symColor); break;
            case TechDirection.Down:  DrawCardSymbol(c, node.isCapstone, symColor); break;
        }

        GUI.color = prev;

        // 캡스톤은 안쪽에 추가 하이라이트
        if (node.isCapstone && (hasRank || maxed))
        {
            float hot = 8f;
            var hr = new Rect(c.x - hot * 0.5f, c.y - hot * 0.5f, hot, hot);
            var p2 = GUI.color;
            GUI.color = new Color(1f, 0.98f, 0.85f, 0.95f);
            GUI.DrawTexture(hr, _radialGlowTex, ScaleMode.StretchToFill, alphaBlend: true);
            GUI.color = p2;
        }
    }

    private void DrawSwordSymbol(Vector2 c, bool large, Color color)
    {
        float h = large ? 28f : 20f;
        float w = 2.5f;
        DrawRect(new Rect(c.x - w * 0.5f, c.y - h * 0.5f, w, h), color);
        // 가드
        float gw = large ? 12f : 9f;
        DrawRect(new Rect(c.x - gw * 0.5f, c.y - h * 0.18f, gw, 2f), color);
        // 폼멜
        var prev = GUI.color;
        GUI.color = color;
        float pSize = 4f;
        GUI.DrawTexture(new Rect(c.x - pSize * 0.5f, c.y + h * 0.5f - 1f, pSize, pSize), _radialGlowTex);
        GUI.color = prev;
    }

    private void DrawShieldSymbol(Vector2 c, bool large, Color color)
    {
        float w = large ? 18f : 14f;
        float h = large ? 22f : 17f;
        // 위쪽 직사각형 + 아래쪽 V 구현 대신 사다리꼴 근사
        DrawRect(new Rect(c.x - w * 0.5f, c.y - h * 0.5f, w, h * 0.55f), color);
        DrawRect(new Rect(c.x - w * 0.42f, c.y - h * 0.05f, w * 0.84f, h * 0.30f), color);
        DrawRect(new Rect(c.x - w * 0.30f, c.y + h * 0.25f, w * 0.60f, h * 0.20f), color);
        DrawRect(new Rect(c.x - w * 0.12f, c.y + h * 0.42f, w * 0.24f, h * 0.10f), color);
    }

    private void DrawCoinSymbol(Vector2 c, bool large, Color color)
    {
        float size = large ? 18f : 14f;
        var prev = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(new Rect(c.x - size * 0.5f, c.y - size * 0.5f, size, size),
            _radialGlowTex, ScaleMode.StretchToFill, alphaBlend: true);
        // 코인 가운데 작은 점
        float dot = 4f;
        GUI.color = new Color(0.10f, 0.08f, 0.04f, 0.95f);
        GUI.DrawTexture(new Rect(c.x - dot * 0.5f, c.y - dot * 0.5f, dot, dot),
            _radialGlowTex, ScaleMode.StretchToFill, alphaBlend: true);
        GUI.color = prev;
    }

    private void DrawCardSymbol(Vector2 c, bool large, Color color)
    {
        float w = large ? 12f : 9f;
        float h = large ? 18f : 14f;
        // 두 장 카드 겹친 모양
        DrawRect(new Rect(c.x - w * 0.6f - 3f, c.y - h * 0.5f + 2f, w, h),
            new Color(color.r, color.g, color.b, color.a * 0.55f));
        DrawRect(new Rect(c.x - w * 0.4f + 1f, c.y - h * 0.5f, w, h), color);
    }

    private void DrawRankBadge(TechNode node, int rank, bool hasRank, bool maxed, bool canRankUp)
    {
        float r = NodeRadius(node.isCapstone);
        // 헥스 아래 약간 떨어진 위치
        float w = 38f, h = 16f;
        var br = new Rect(node.pos.x - w * 0.5f, node.pos.y + r * 0.866f + 2f, w, h);

        Color bg = maxed ? new Color(0.40f, 0.22f, 0.08f, 0.95f)
                : hasRank ? new Color(0.18f, 0.13f, 0.08f, 0.92f)
                : canRankUp ? new Color(0.12f, 0.10f, 0.06f, 0.92f)
                : new Color(0.08f, 0.07f, 0.09f, 0.88f);
        DrawRect(br, bg);

        Color borderC = maxed ? HexMaxedBorder
                       : hasRank ? HexFilledBorder
                       : canRankUp ? HexAvailBorder
                       : new Color(0.30f, 0.27f, 0.25f, 0.85f);
        DrawHollowRect(br, borderC, 1f);

        var s = new GUIStyle(_rankCounterStyle);
        if (maxed) s.normal.textColor = new Color(1f, 0.92f, 0.65f, 1f);
        else if (canRankUp) s.normal.textColor = new Color(1f, 0.85f, 0.45f, 1f);
        else if (hasRank) s.normal.textColor = TextWarm;
        else s.normal.textColor = TextDim;
        GUI.Label(br, $"{rank}/{node.maxRank}", s);
    }

    // ── 방향 라벨 ───────────────────────────────────────────

    private void DrawDirectionLabels()
    {
        // 4방위 끝(캡스톤 너머)에 큼지막한 라벨
        DrawDirLabel(new Vector2(TechTreeCatalog.CenterX + 460f, TechTreeCatalog.CenterY), "공격", AccentRight);
        DrawDirLabel(new Vector2(TechTreeCatalog.CenterX - 460f, TechTreeCatalog.CenterY), "방어", AccentLeft);
        DrawDirLabel(new Vector2(TechTreeCatalog.CenterX, TechTreeCatalog.CenterY - 408f), "경제", AccentUp);
        DrawDirLabel(new Vector2(TechTreeCatalog.CenterX, TechTreeCatalog.CenterY + 405f), "운영", AccentDown);
    }

    private void DrawDirLabel(Vector2 pos, string text, Color accent)
    {
        const float w = 120f, h = 28f;
        var r = new Rect(pos.x - w * 0.5f, pos.y - h * 0.5f, w, h);

        var prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.85f);
        GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), text, _branchLabelStyle);
        GUI.color = accent;
        GUI.Label(r, text, _branchLabelStyle);
        GUI.color = prev;
    }

    // ── 툴팁 ────────────────────────────────────────────────

    private void DrawTooltip(GameStateManager gsm)
    {
        if (string.IsNullOrEmpty(_hoverNodeId)) return;
        if (Event.current == null || Event.current.type != EventType.Repaint) return;

        var node = TechTreeCatalog.GetNode(_hoverNodeId);
        if (node == null) return;

        Vector2 mp = Event.current.mousePosition;
        var tipRect = new Rect(mp.x + 18f, mp.y + 18f, 280f, 86f);
        if (tipRect.xMax > RefW - 8f) tipRect.x = mp.x - tipRect.width - 18f;
        if (tipRect.yMax > RefH - 8f) tipRect.y = RefH - tipRect.height - 8f;

        DrawRect(tipRect, new Color(0.05f, 0.04f, 0.07f, 0.96f));
        DrawHollowRect(tipRect, AccentOf(node.direction), 1.5f);

        var titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            normal = { textColor = TextWarm },
        };
        GUI.Label(new Rect(tipRect.x + 8f, tipRect.y + 4f, tipRect.width - 16f, 20f),
                  $"{node.name}{(node.isCapstone ? "  ★" : "")}", titleStyle);

        var descStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12, wordWrap = true,
            normal = { textColor = TextMuted },
        };
        GUI.Label(new Rect(tipRect.x + 8f, tipRect.y + 26f, tipRect.width - 16f, 32f), node.description, descStyle);

        int curRank = gsm.TechTree.GetRank(node.id);
        var costStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            normal = { textColor = curRank >= node.maxRank ? new Color(1f, 0.78f, 0.30f) : new Color(1f, 0.88f, 0.45f) },
        };
        string costLine = curRank >= node.maxRank
            ? $"최대 랭크 ({curRank}/{node.maxRank})"
            : $"랭크 {curRank}/{node.maxRank}  ·  다음: {node.perRankCost} pt";
        GUI.Label(new Rect(tipRect.x + 8f, tipRect.yMax - 22f, tipRect.width - 16f, 16f), costLine, costStyle);
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
        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(TextWarm.r, TextWarm.g, TextWarm.b, alpha) },
        };
        GUI.color = Color.white;
        GUI.Label(rect, _flashMessage, style);
        GUI.color = prev;
    }

    private void Flash(string msg)
    {
        _flashMessage = msg;
        _flashUntil = Time.unscaledTime + 1.6f;
    }

    // ── 헬퍼 ────────────────────────────────────────────────

    private static float NodeRadius(bool isCapstone) => isCapstone ? 38f : 30f;

    private static Color AccentOf(TechDirection d) => d switch
    {
        TechDirection.Right => AccentRight,
        TechDirection.Left  => AccentLeft,
        TechDirection.Up    => AccentUp,
        TechDirection.Down  => AccentDown,
        _ => AccentRoot,
    };

    private static Color Brighten(Color c, float amt)
    {
        return new Color(Mathf.Min(1f, c.r + amt), Mathf.Min(1f, c.g + amt), Mathf.Min(1f, c.b + amt), c.a);
    }

    private void DrawRect(Rect r, Color c)
    {
        var prev = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, _whiteTex);
        GUI.color = prev;
    }

    private void DrawHollowRect(Rect r, Color c, float thickness)
    {
        DrawRect(new Rect(r.x, r.y, r.width, thickness), c);
        DrawRect(new Rect(r.x, r.yMax - thickness, r.width, thickness), c);
        DrawRect(new Rect(r.x, r.y, thickness, r.height), c);
        DrawRect(new Rect(r.xMax - thickness, r.y, thickness, r.height), c);
    }

    private void DrawHexAt(Rect rect, Color c, Texture2D tex)
    {
        var prev = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill, alphaBlend: true);
        GUI.color = prev;
    }

    private void DrawShadowedLabel(Rect r, string text, GUIStyle style)
    {
        var prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.85f);
        GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), text, style);
        GUI.color = prev;
        GUI.Label(r, text, style);
    }

    /// <summary>두 점 사이를 회전된 1px 사각형으로 근사. dotted=true면 8px 단위로 끊어 그림.</summary>
    private void DrawLine(Vector2 a, Vector2 b, Color color, float thickness, bool dotted)
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
        if (!dotted)
        {
            GUI.DrawTexture(new Rect(a.x, a.y - thickness * 0.5f, len, thickness), _whiteTex);
        }
        else
        {
            const float dash = 6f;
            const float gap = 5f;
            float step = dash + gap;
            float traveled = 0f;
            while (traveled < len)
            {
                float seg = Mathf.Min(dash, len - traveled);
                GUI.DrawTexture(new Rect(a.x + traveled, a.y - thickness * 0.5f, seg, thickness), _whiteTex);
                traveled += step;
            }
        }
        GUI.matrix = prevMatrix;
        GUI.color = prevColor;
    }

    // ── 절차 텍스처 베이크 ─────────────────────────────────

    /// <summary>
    /// flat-top hexagon alpha mask. radiusFraction 1.0 = 텍스처 한 변 절반에 정확히 외접하는 헥스.
    /// smoothEdge: 0~1, 가장자리 페이드 폭 비율(0.025=날카로운 AA, 0.4=부드러운 외곽).
    /// </summary>
    private static Texture2D MakeFlatTopHexTex(int size, float radiusFraction, float smoothEdge)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave,
        };
        var px = new Color[size * size];
        float c = (size - 1) * 0.5f;
        // 외접 반지름 = c * radiusFraction (픽셀 단위)
        // flat-top: |y| <= 0.866*r,  1.732*|x| + |y| <= 1.732*r
        float r = c * radiusFraction;
        float fadeIn  = r * Mathf.Max(0.001f, 1f - smoothEdge);
        float fadeOut = r;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float ax = Mathf.Abs(x - c);
                float ay = Mathf.Abs(y - c);
                // hex "거리" = max(ay/0.866, (1.732*ax + ay)/1.732)
                float d1 = ay / 0.866f;
                float d2 = (1.732f * ax + ay) / 1.732f;
                float d = Mathf.Max(d1, d2);

                float alpha;
                if (d <= fadeIn) alpha = 1f;
                else if (d >= fadeOut) alpha = 0f;
                else alpha = 1f - (d - fadeIn) / (fadeOut - fadeIn);
                alpha = Mathf.Clamp01(alpha);
                alpha = alpha * alpha * (3f - 2f * alpha); // smoothstep

                px[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

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

    // ── 스타일 ──────────────────────────────────────────────

    private void EnsureStyles()
    {
        if (_stylesReady) return;

        _titleStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFont,
            fontSize = 28,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = TextWarm },
        };

        _pointsStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFont,
            fontSize = 22,
            alignment = TextAnchor.MiddleRight,
            fontStyle = FontStyle.Bold,
            normal = { textColor = TextWarm },
        };

        _branchLabelStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFont,
            fontSize = 26,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = TextWarm },
        };

        _nodeNameStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = TextWarm },
        };

        _rankCounterStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = TextWarm },
        };

        _smallBtnStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 13,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = TextWarm, background = null },
            hover = { textColor = Color.white, background = null },
            active = { textColor = new Color(1f, 0.95f, 0.7f), background = null },
            border = new RectOffset(0, 0, 0, 0),
            padding = new RectOffset(4, 4, 2, 2),
        };

        _backBtnStyle = new GUIStyle(_smallBtnStyle) { fontSize = 16 };

        _stylesReady = true;
    }
}
