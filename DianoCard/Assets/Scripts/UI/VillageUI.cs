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

    private readonly List<Action> _pending = new();

    // 아트 (Reward / Map 폴더에서 재사용)
    private Texture2D _panelTex;
    private Texture2D _rowTex;
    private Texture2D _medallionTex;
    private Texture2D _continueTex;
    private Texture2D _glowTex;
    private Texture2D _campIconTex;     // 헤더 — Map/Node_Camp
    private Texture2D _treasureIconTex; // 좌측 옵션 — Reward/RelicIcon
    private Texture2D _restIconTex;     // 우측 옵션 — Reward/Potion_Bottle

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

        GUI.depth = 0;
        float scale = Mathf.Min(Screen.width / RefW, Screen.height / RefH);
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));

        DrawBackdrop();
        DrawHeader(run);
        DrawOptions(gsm, run);
    }

    // =========================================================
    // Drawing
    // =========================================================

    private void DrawBackdrop()
    {
        var prev = GUI.color;
        GUI.color = new Color(0.03f, 0.02f, 0.04f, 0.88f);
        GUI.DrawTexture(new Rect(0, 0, RefW, RefH), Texture2D.whiteTexture);
        GUI.color = prev;

        // 모닥불 분위기 — 따뜻한 주황 글로우
        if (_glowTex != null)
        {
            GUI.color = new Color(1f, 0.55f, 0.22f, 0.22f);
            GUI.DrawTexture(new Rect(RefW * 0.5f - 700f, RefH * 0.5f - 450f, 1400f, 900f), _glowTex, ScaleMode.StretchToFill);
            GUI.color = prev;
        }
    }

    private void DrawHeader(RunState run)
    {
        // 좌상단 — 마을 아이콘
        if (_campIconTex != null)
        {
            GUI.DrawTexture(new Rect(40f, 18f, 68f, 68f), _campIconTex, ScaleMode.ScaleToFit);
        }

        // 가운데 — 타이틀 / 부제
        GUI.Label(new Rect(0, 24f, RefW, 48f), "VILLAGE", _titleStyle);
        GUI.Label(new Rect(0, 70f, RefW, 22f), "Choose your gift", _subStyle);

        // 우상단 — HP 인디케이터 (회복량 가늠용)
        var hpRect = new Rect(RefW - 240f, 22f, 220f, 44f);
        var prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(hpRect, Texture2D.whiteTexture);
        GUI.color = prev;
        GUI.Label(hpRect, $"HP  {run.playerCurrentHp} / {run.playerMaxHp}", _hpStyle);
    }

    private void DrawOptions(GameStateManager gsm, RunState run)
    {
        const float cardW = 380f;
        const float cardH = 440f;
        const float gap = 60f;
        float totalW = cardW * 2 + gap;
        float startX = (RefW - totalW) * 0.5f;
        float startY = 150f;

        var leftRect = new Rect(startX, startY, cardW, cardH);
        var rightRect = new Rect(startX + cardW + gap, startY, cardW, cardH);

        int healAmount = Mathf.Max(1, Mathf.RoundToInt(run.playerMaxHp * 0.25f));
        int afterHp = Mathf.Min(run.playerCurrentHp + healAmount, run.playerMaxHp);
        bool restMeaningful = run.playerCurrentHp < run.playerMaxHp;

        var ev = Event.current;
        bool leftHover = leftRect.Contains(ev.mousePosition);
        bool rightHover = rightRect.Contains(ev.mousePosition);

        DrawOptionCard(
            leftHover ? Scale(leftRect, 1.04f) : leftRect,
            _treasureIconTex,
            "TREASURE CHEST",
            "Open a free treasure chest.\nReceive a relic instantly.",
            new Color(1f, 0.82f, 0.42f),
            leftHover);

        DrawOptionCard(
            rightHover ? Scale(rightRect, 1.04f) : rightRect,
            _restIconTex,
            "REST",
            restMeaningful
                ? $"Recover 25% of max HP.\n+{healAmount} HP  ({run.playerCurrentHp} → {afterHp})"
                : "Recover 25% of max HP.\nAlready at full health.",
            new Color(0.55f, 0.95f, 0.55f),
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

    private void DrawOptionCard(Rect rect, Texture2D icon, string title, string description, Color glowColor, bool hover)
    {
        // 글로우 (호버 시 강해짐)
        if (_glowTex != null)
        {
            float pad = hover ? 60f : 36f;
            var glowRect = new Rect(rect.x - pad, rect.y - pad, rect.width + pad * 2, rect.height + pad * 2);
            var prev = GUI.color;
            GUI.color = new Color(glowColor.r, glowColor.g, glowColor.b, hover ? 0.55f : 0.30f);
            GUI.DrawTexture(glowRect, _glowTex, ScaleMode.StretchToFill);
            GUI.color = prev;
        }

        // 패널
        if (_panelTex != null)
            GUI.DrawTexture(rect, _panelTex, ScaleMode.StretchToFill);
        else
            DrawFilledRect(rect, new Color(0.10f, 0.14f, 0.20f, 0.96f));

        // 메달리온 + 아이콘 (상단 중앙)
        float medSize = 180f;
        float medCx = rect.center.x;
        float medCy = rect.y + 130f;
        if (_medallionTex != null)
        {
            var medRect = new Rect(medCx - medSize * 0.5f, medCy - medSize * 0.5f, medSize, medSize);
            GUI.DrawTexture(medRect, _medallionTex, ScaleMode.ScaleToFit);
        }
        if (icon != null)
        {
            // 둥실둥실 — 호버 시 더 강하게
            float bobAmp = hover ? 5f : 2.5f;
            float bob = Mathf.Sin(Time.time * Mathf.PI * 2f * 1.0f) * bobAmp;
            float iconSize = medSize * 0.55f;
            var iconRect = new Rect(medCx - iconSize * 0.5f, medCy - iconSize * 0.5f + bob, iconSize, iconSize);
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
        }

        // 타이틀 (중간)
        var titleRect = new Rect(rect.x, rect.y + 250f, rect.width, 40f);
        GUI.Label(titleRect, title, _optionTitleStyle);

        // 설명 (하단)
        var descRect = new Rect(rect.x + 24f, rect.y + 300f, rect.width - 48f, rect.height - 320f);
        GUI.Label(descRect, description, _optionDescStyle);
    }

    // =========================================================
    // 리소스 / 스타일
    // =========================================================

    private void EnsureStyles()
    {
        if (_stylesReady) return;

        _panelTex        = Resources.Load<Texture2D>("Reward/Panel");
        _rowTex          = Resources.Load<Texture2D>("Reward/RowButton");
        _medallionTex    = Resources.Load<Texture2D>("Reward/MedallionRing");
        _continueTex     = Resources.Load<Texture2D>("Reward/ContinueButton");
        _campIconTex     = Resources.Load<Texture2D>("Map/Node_Camp");
        _treasureIconTex = Resources.Load<Texture2D>("Reward/RelicIcon");
        _restIconTex     = Resources.Load<Texture2D>("Reward/Potion_Bottle");

        _displayFont = Resources.Load<Font>("Fonts/Cinzel-VariableFont_wght");

        _glowTex = CreateRadialGlowTexture(64);

        var cream = new Color(0.99f, 0.95f, 0.78f);

        _titleStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFont, fontSize = 44, alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.98f, 0.88f, 0.52f) },
        };
        _subStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFont, fontSize = 16, alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold, normal = { textColor = cream },
        };
        _optionTitleStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFont, fontSize = 26, alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.98f, 0.88f, 0.52f) },
        };
        _optionDescStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFont, fontSize = 16, alignment = TextAnchor.UpperCenter,
            fontStyle = FontStyle.Bold, wordWrap = true, normal = { textColor = cream },
        };
        _hpStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFont, fontSize = 22, alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.95f, 0.55f, 0.55f) },
        };

        _stylesReady = true;
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
