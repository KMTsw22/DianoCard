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
    [SerializeField] private Color campfireGlowColor = new(1f, 0.55f, 0.22f, 0f);
    [SerializeField] private Vector2 campfireGlowSize = new(1400f, 900f);

    [Header("NPC (상반신 화자)")]
    [Tooltip("NPC 표시 여부.")]
    [SerializeField] private bool npcEnabled = true;
    [Tooltip("NPC 크기 (px). ScaleToFit이라 비율 유지됨.")]
    [SerializeField] private Vector2 npcSize = new(540f, 740f);
    [Tooltip("NPC 좌상단 Y 위치 (px). 값 크게 → 아래로 내려감.")]
    [SerializeField, Range(-100f, 500f)] private float npcY = 196f;
    [Tooltip("NPC X 위치 모드 (true=화면 왼쪽 기준, false=오른쪽 기준).")]
    [SerializeField] private bool npcAlignLeft = true;
    [Tooltip("앵커로부터의 X 거리 (px). 왼쪽 정렬이면 화면 왼쪽에서, 오른쪽이면 화면 오른쪽에서.")]
    [SerializeField, Range(-200f, 400f)] private float npcXOffset = 0f;

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

    [Header("Option Cards (좌/우 2장)")]
    [SerializeField] private Vector2 optionCardSize = new(340f, 400f);
    [SerializeField] private float optionCardGap = 50f;
    [SerializeField] private float optionCardYOffset = -35f;
    [Tooltip("카드 2장 묶음의 X 오프셋 (양수 = 오른쪽으로 이동). 기본 0 = 화면 중앙.")]
    [SerializeField, Range(-300f, 300f)] private float optionCardXOffset = 164f;
    [SerializeField, Range(1f, 1.2f)] private float optionHoverScale = 1.04f;

    [Header("Option Content (왼쪽: Treasure / 오른쪽: Rest)")]
    [SerializeField] private string treasureTitle = "";
    [TextArea(2, 4)]
    [SerializeField] private string treasureDesc = "Open a free treasure chest.\nReceive a relic instantly.";
    [SerializeField] private Color treasureGlowColor = new(1f, 0.82f, 0.42f);
    [SerializeField] private string restTitle = "";
    [SerializeField, Range(0f, 1f)] private float restHealPct = 0.25f;
    [SerializeField] private Color restGlowColor = new(0.55f, 0.95f, 0.55f);

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
    [SerializeField, Range(0.2f, 0.6f)] private float medallionCenterYFactor = 0.377f;
    [Tooltip("아이콘 크기 배율 — 메달리온 없으면 큰 값(0.9~1.1) 권장.")]
    [SerializeField, Range(0.3f, 1.4f)] private float iconSizeFactor = 0.8f;
    [Tooltip("아이콘 뒤 글로우 표시 여부.")]
    [SerializeField] private bool iconGlowEnabled = true;
    [Tooltip("기본(기타) 아이콘 글로우 색.")]
    [SerializeField] private Color iconGlowColor = new(1f, 0.78f, 0.40f, 0.55f);
    [Tooltip("글로우 크기 = 아이콘 크기 × 이 값.")]
    [SerializeField, Range(1f, 3f)] private float iconGlowScale = 2.0f;
    [Tooltip("TREASURE 아이콘 전용 글로우 색 — 보물상자 어울리는 진한 골드.")]
    [SerializeField] private Color treasureIconGlowColor = new(1f, 0.65f, 0.18f, 0.75f);
    [Tooltip("TREASURE 아이콘 글로우 크기 배율.")]
    [SerializeField, Range(0.8f, 2.5f)] private float treasureIconGlowScaleMultiplier = 1.2f;
    [Tooltip("REST 아이콘 전용 글로우 색 (빨간 하트 어울림).")]
    [SerializeField] private Color restIconGlowColor = new(1f, 0.25f, 0.20f, 0.75f);
    [Tooltip("REST 아이콘 글로우 크기 배율.")]
    [SerializeField, Range(0.8f, 2.5f)] private float restIconGlowScaleMultiplier = 1.2f;
    [SerializeField, Range(0f, 10f)] private float iconBobAmpNormal = 2.5f;
    [SerializeField, Range(0f, 20f)] private float iconBobAmpHover = 5f;
    [SerializeField, Range(0.2f, 3f)] private float iconBobFrequency = 1.0f;
    [SerializeField] private float optionTitleTopGap = 14f;
    [SerializeField] private float optionTitleHeight = 36f;
    [SerializeField] private float optionDescTopGap = 10f;
    [SerializeField] private float optionDescXPad = 30f;
    [SerializeField] private float optionDescBottomPad = 16f;

    [Header("Option Card Glow")]
    [SerializeField, Range(0f, 120f)] private float cardGlowPadNormal = 36f;
    [SerializeField, Range(0f, 120f)] private float cardGlowPadHover = 60f;
    [SerializeField, Range(0f, 1f)] private float cardGlowAlphaNormal = 0.30f;
    [SerializeField, Range(0f, 1f)] private float cardGlowAlphaHover = 0.55f;

    [Header("Font Sizes")]
    [SerializeField, Range(20, 60)] private int titleFontSize = 44;
    [SerializeField, Range(10, 24)] private int subtitleFontSize = 16;
    [SerializeField, Range(16, 40)] private int optionTitleFontSize = 26;
    [SerializeField, Range(10, 24)] private int optionDescFontSize = 14;
    [SerializeField, Range(14, 32)] private int hpFontSize = 22;

    [Header("Colors")]
    [SerializeField] private Color titleColor = new(0.98f, 0.88f, 0.52f);
    [SerializeField] private Color creamColor = new(0.99f, 0.95f, 0.78f);
    [SerializeField] private Color hpTextColor = new(0.95f, 0.55f, 0.55f);
    [Tooltip("옵션 타이틀(TREASURE/REST) 글자 색 — 진한 블랙 계열.")]
    [SerializeField] private Color optionTitleColor = new(0.08f, 0.05f, 0.03f);
    [Tooltip("외곽선 위쪽 색 (밝은 실버).")]
    [SerializeField] private Color optionTitleOutlineTop = new(0.92f, 0.92f, 0.94f, 1f);
    [Tooltip("외곽선 아래쪽 색 (어두운 차콜) — 상하 그라데이션용.")]
    [SerializeField] private Color optionTitleOutlineBottom = new(0.35f, 0.35f, 0.38f, 1f);
    [Tooltip("외곽선 두께 (0=외곽선 없음).")]
    [SerializeField, Range(0f, 3f)] private float optionTitleOutlineThickness = 0f;

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
    private Texture2D _hpIconTex;       // HP 하트 아이콘 — InGame/Icon/HP
    private Texture2D _bgTex;           // 전체 화면 배경 — VillageUI/BackGround
    private Texture2D _npcTex;          // NPC 상반신 — VillageUI/NPC
    private Texture2D _optionCardTex;   // 선택지 카드 패널 — VillageUI/OptionCardPanel

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
        ApplyStyleValues();  // Inspector 값 실시간 반영

        GUI.depth = 0;
        float scale = Mathf.Min(Screen.width / RefW, Screen.height / RefH);
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));

        DrawBackdrop();
        DrawNPC();
        DrawHeader(run);
        DrawOptions(gsm, run);
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

        GUI.color = prev;
    }

    private void DrawNPC()
    {
        if (!npcEnabled || _npcTex == null) return;
        float x = npcAlignLeft ? npcXOffset : (RefW - npcSize.x - npcXOffset);
        GUI.DrawTexture(new Rect(x, npcY, npcSize.x, npcSize.y), _npcTex, ScaleMode.ScaleToFit);
    }

    private void DrawHeader(RunState run)
    {
        if (_campIconTex != null)
        {
            GUI.DrawTexture(campIconRect, _campIconTex, ScaleMode.ScaleToFit);
        }

        GUI.Label(new Rect(0, titleY, RefW, titleHeight), titleText, _titleStyle);
        GUI.Label(new Rect(0, subtitleY, RefW, subtitleHeight), subtitleText, _subStyle);

        var hpRect = new Rect(RefW - hpPanelRightMargin, hpPanelTopY, hpPanelSize.x, hpPanelSize.y);
        // ShopUI 섹션 패널과 동일한 느낌 — 어두운 네이비 fill + 얇은 골드 테두리
        DrawFilledRect(hpRect, hpPanelBgColor);
        DrawBorderRect(hpRect, hpPanelBorderThickness, hpPanelBorderColor);

        // 하트 아이콘 (왼쪽) + 텍스트 오른쪽 시프트
        if (_hpIconTex != null)
        {
            var iconRect = new Rect(
                hpRect.x + hpIconLeftPad,
                hpRect.y + (hpRect.height - hpIconSize) * 0.5f,
                hpIconSize, hpIconSize);
            GUI.DrawTexture(iconRect, _hpIconTex, ScaleMode.ScaleToFit);
        }
        var hpTextRect = new Rect(hpRect.x + hpTextXShift, hpRect.y, hpRect.width - hpTextXShift, hpRect.height);
        GUI.Label(hpTextRect, $"{run.playerCurrentHp} / {run.playerMaxHp}", _hpStyle);
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
        bool restMeaningful = run.playerCurrentHp < run.playerMaxHp;
        int pctLabel = Mathf.RoundToInt(restHealPct * 100f);

        var ev = Event.current;
        bool leftHover = leftRect.Contains(ev.mousePosition);
        bool rightHover = rightRect.Contains(ev.mousePosition);

        DrawOptionCard(
            leftHover ? Scale(leftRect, optionHoverScale) : leftRect,
            _treasureIconTex,
            treasureTitle,
            treasureDesc,
            treasureGlowColor,
            leftHover);

        DrawOptionCard(
            rightHover ? Scale(rightRect, optionHoverScale) : rightRect,
            _restIconTex,
            restTitle,
            restMeaningful
                ? $"Recover {pctLabel}% of max HP.\n+{healAmount} HP  ({run.playerCurrentHp} → {afterHp})"
                : $"Recover {pctLabel}% of max HP.\nAlready at full health.",
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

    private void DrawOptionCard(Rect rect, Texture2D icon, string title, string description, Color glowColor, bool hover)
    {
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
            float bobAmp = hover ? iconBobAmpHover : iconBobAmpNormal;
            float bob = Mathf.Sin(Time.time * Mathf.PI * 2f * iconBobFrequency) * bobAmp;
            float iconSize = medSize * iconSizeFactor;
            float cy = medCy + bob;

            // 글로우 (아이콘 뒤쪽) — TREASURE는 진한 골드, REST는 빨강
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

            var iconRect = new Rect(medCx - iconSize * 0.5f, cy - iconSize * 0.5f, iconSize, iconSize);
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
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
        GUI.Label(descRect, description, _optionDescStyle);
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
            font = _displayFont, alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold, wordWrap = true,
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
        _optionDescStyle.normal.textColor = creamColor;
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
