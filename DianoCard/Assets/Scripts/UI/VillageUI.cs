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

    [Header("Backdrop (배경 이미지 + 딤) — 위치/크기 Inspector 튜닝")]
    [Tooltip("배경 이미지 위에 덮는 딤 색. 알파 0=이미지 그대로, 높일수록 어두움.")]
    [SerializeField] private Color backdropColor = new(0.02f, 0.04f, 0.06f, 0.25f);
    [Tooltip("배경 이미지 X 오프셋 (px). 양수=오른쪽으로 밀림. ScaleAndCrop 후 위치 보정용.")]
    [SerializeField, Range(-400f, 400f)] private float bgOffsetX = 0f;
    [Tooltip("배경 이미지 Y 오프셋 (px). 양수=아래로 밀림.")]
    [SerializeField, Range(-400f, 400f)] private float bgOffsetY = 0f;
    [Tooltip("배경 이미지 스케일. 1=원본, >1=확대(가까이), <1=축소.")]
    [SerializeField, Range(0.5f, 2f)] private float bgScale = 1f;

    [Header("NPC (상반신 화자) — 위치/크기 Inspector 튜닝")]
    [Tooltip("NPC 표시 여부.")]
    [SerializeField] private bool npcEnabled = true;
    [Tooltip("NPC 크기 (px). ScaleToFit이라 비율 유지됨.")]
    [SerializeField] private Vector2 npcSize = new(594f, 740f);
    [Tooltip("NPC 좌상단 Y 위치 (px). 값 크게 → 아래로 내려감.")]
    [SerializeField, Range(-300f, 700f)] private float npcY = 110f;
    [Tooltip("NPC X 위치 모드 (true=화면 왼쪽 기준, false=오른쪽 기준).")]
    [SerializeField] private bool npcAlignLeft = true;
    [Tooltip("앵커로부터의 X 거리 (px). 왼쪽 정렬이면 화면 왼쪽에서, 오른쪽이면 화면 오른쪽에서.")]
    [SerializeField, Range(-400f, 800f)] private float npcXOffset = -20f;

    [Header("NPC Intro (Village 진입 시 1회 페이드 인)")]
    [Tooltip("진입 페이드인 재생 여부.")]
    [SerializeField] private bool introEnabled = true;
    [Tooltip("진입 연출 총 지속시간 (초).")]
    [SerializeField, Range(0.3f, 2f)] private float introDuration = 0.7f;
    [Tooltip("진입 시작 위치 오프셋 (px). 여기서 원위치로 슬라이드 인. Y 양수 = 아래에서 위로 올라옴.")]
    [SerializeField] private Vector2 introSlideFrom = new(-30f, 20f);
    [Tooltip("진입 시작 스케일. 1보다 작으면 커지며 등장.")]
    [SerializeField, Range(0.85f, 1f)] private float introScaleFrom = 0.96f;
    [Tooltip("NPC 등장 완료 후 옵션 카드가 페이드 인되는 시간 (초). 0=즉시 표시.")]
    [SerializeField, Range(0f, 1f)] private float optionsFadeInDuration = 0.35f;

    [Header("NPC Idle Breathing (지속 호흡)")]
    [Tooltip("호흡 루프 재생 여부.")]
    [SerializeField] private bool breathingEnabled = true;
    [Tooltip("호흡 진폭 (px). 상하로 이만큼 미세하게 흔들림.")]
    [SerializeField, Range(0f, 10f)] private float breathingAmplitudePx = 3f;
    [Tooltip("호흡 주기 (초). 한 호흡 = 들이쉬고 내쉬는 1사이클.")]
    [SerializeField, Range(1.5f, 8f)] private float breathingPeriod = 3.5f;

    [Header("Option Cards (좌/우 2장) — 위치/크기 Inspector 튜닝")]
    [Tooltip("카드 한 장 크기 (W, H, px).")]
    [SerializeField] private Vector2 optionCardSize = new(340f, 540f);
    [Tooltip("두 카드 사이 간격 (px).")]
    [SerializeField, Range(0f, 200f)] private float optionCardGap = 50f;
    [Tooltip("카드 2장 묶음의 Y 오프셋 (양수=아래로). 기본 0 = 화면 세로 중앙.")]
    [SerializeField, Range(-300f, 300f)] private float optionCardYOffset = 10f;
    [Tooltip("카드 2장 묶음의 X 오프셋 (양수 = 오른쪽으로 이동). 기본 0 = 화면 중앙.")]
    [SerializeField, Range(-500f, 500f)] private float optionCardXOffset = 200f;
    [SerializeField, Range(1f, 1.2f)] private float optionHoverScale = 1.04f;

    [Header("Option Content (왼쪽: Treasure / 오른쪽: Rest)")]
    [TextArea(2, 4)]
    [Tooltip("카드 1 보물 — 타이틀(큰 글씨). 본문은 아래 desc 필드.")]
    [SerializeField] private string treasureName = "Mystery";
    [Tooltip("카드 1 보물 — 본문. 이름 아래에 표시.")]
    [TextArea(2, 4)]
    [SerializeField] private string treasureDesc = "Free Relic Inside";
    [SerializeField] private Color treasureGlowColor = new(1f, 0.82f, 0.42f);
    [Tooltip("카드 2 휴식 — 타이틀(큰 글씨).")]
    [SerializeField] private string restName = "Heal";
    [SerializeField, Range(0f, 1f)] private float restHealPct = 0.25f;
    [SerializeField] private Color restGlowColor = new(1f, 0.35f, 0.30f);

    [Header("옵션 카드 아이콘 — 위치/크기")]
    [Tooltip("아이콘 기준 크기 (px). 글로우·그림자 크기 계산의 기준이 됨. 실제 아이콘 픽셀 크기는 이 값 × Icon Size Factor.")]
    [SerializeField, Range(60f, 280f)] private float medallionSize = 150f;
    [Tooltip("아이콘 크기 배율 (medallionSize 대비). 1=기준 크기 그대로, <1=줄임, >1=키움.")]
    [SerializeField, Range(0.3f, 1.4f)] private float iconSizeFactor = 1f;
    [Tooltip("아이콘 X 오프셋 (px). 0=카드 정중앙, 양수=오른쪽으로.")]
    [SerializeField, Range(-120f, 120f)] private float iconCenterXOffset = 0f;
    [Tooltip("아이콘 중심 Y = card.y + card.h * 이 값. (0.2~0.6 범위, 작을수록 위쪽)")]
    [SerializeField, Range(0.2f, 0.6f)] private float medallionCenterYFactor = 0.47f;
    [Tooltip("아이콘 뒤 글로우 표시 여부.")]
    [SerializeField] private bool iconGlowEnabled = true;
    [Tooltip("기본(기타) 아이콘 글로우 색.")]
    [SerializeField] private Color iconGlowColor = new(1f, 0.78f, 0.40f, 0.55f);
    [Tooltip("글로우 크기 = 아이콘 크기 × 이 값.")]
    [SerializeField, Range(1f, 3f)] private float iconGlowScale = 2.0f;
    [Tooltip("TREASURE 아이콘 전용 글로우 색 — 앰버/오렌지 계열. 채도·알파 높여 보물상자 톤과 차별화.")]
    [SerializeField] private Color treasureIconGlowColor = new(1f, 0.58f, 0.15f, 0.95f);
    [Tooltip("TREASURE 아이콘 글로우 크기 배율.")]
    [SerializeField, Range(0.8f, 3f)] private float treasureIconGlowScaleMultiplier = 1.7f;
    [Tooltip("REST 아이콘 전용 글로우 색 — 빨간 하트 주변 붉은 아우라.")]
    [SerializeField] private Color restIconGlowColor = new(1f, 0.25f, 0.20f, 0.85f);
    [Tooltip("REST 아이콘 글로우 크기 배율.")]
    [SerializeField, Range(0.8f, 2.5f)] private float restIconGlowScaleMultiplier = 1.5f;

    [Header("아이콘 통합 연출 (프레임과 자연스러운 조화)")]
    [Tooltip("아이콘 틴트 — 흰색(1,1,1,1)은 원본 그대로. 세피아/따뜻하게 밀면 프레임과 묶임.")]
    [SerializeField] private Color iconTint = new(1f, 0.95f, 0.88f, 1f);
    [Tooltip("아이콘 뒤 부드러운 그림자(그라운딩) on/off.")]
    [SerializeField] private bool iconShadowEnabled = true;
    [Tooltip("그림자 색 + 알파. 기본 따뜻한 갈색 반투명.")]
    [SerializeField] private Color iconShadowColor = new(0.15f, 0.08f, 0.04f, 0.45f);
    [Tooltip("그림자 크기 = 아이콘 크기 × 이 값.")]
    [SerializeField, Range(0.5f, 2f)] private float iconShadowScale = 1.15f;
    [Tooltip("그림자 세로 오프셋 (px). 양수=아이콘보다 아래로 깔림, 0=중앙 일치.")]
    [SerializeField, Range(-30f, 40f)] private float iconShadowYOffset = 6f;
    [Tooltip("그림자 세로 납작 비율. 1=원, 0.5=타원(바닥에 깔린 느낌).")]
    [SerializeField, Range(0.3f, 1f)] private float iconShadowVerticalSquish = 0.55f;

    [Header("옵션 카드 설명 영역 (텍스트 컨테이너) — 카드 안쪽 여백")]
    [Tooltip("아이콘 하단으로부터 설명 영역(본문)이 시작되는 간격 (px).")]
    [SerializeField, Range(-40f, 200f)] private float optionDescTopGap = 72f;
    [Tooltip("설명 영역의 좌우 패딩 (px). 카드 폭에서 양쪽으로 이만큼씩 안쪽 들여쓰기.")]
    [SerializeField, Range(0f, 80f)] private float optionDescXPad = 30f;
    [Tooltip("설명 영역의 하단 패딩 (px). 카드 하단에서 이만큼 위까지만 텍스트 영역으로 사용.")]
    [SerializeField, Range(0f, 60f)] private float optionDescBottomPad = 16f;

    [Header("Option Card Glow")]
    [SerializeField, Range(0f, 120f)] private float cardGlowPadNormal = 52f;
    [SerializeField, Range(0f, 120f)] private float cardGlowPadHover = 80f;
    [SerializeField, Range(0f, 1f)] private float cardGlowAlphaNormal = 0.48f;
    [SerializeField, Range(0f, 1f)] private float cardGlowAlphaHover = 0.72f;

    [Header("Option Card Planted Shadow (말뚝 끝 꽂힌 느낌 — 지면 접촉 그림자)")]
    [SerializeField] private bool plantedShadowEnabled = true;
    [Tooltip("그림자 색 + 알파. 숲 바닥 톤에 맞게 검정/짙은 갈색 추천.")]
    [SerializeField] private Color plantedShadowColor = new(0f, 0f, 0f, 0.55f);
    [Tooltip("그림자 타원 크기 (px). 말뚝 하나당.")]
    [SerializeField] private Vector2 plantedShadowSize = new(90f, 22f);
    [Tooltip("두 말뚝 중심 X 위치 (카드 폭 비율 0~1). 아트의 실제 말뚝 위치에 맞추세요.")]
    [SerializeField] private Vector2 plantedStakeXFactors = new(0.23f, 0.77f);
    [Tooltip("그림자 Y 오프셋 (카드 하단 기준, 양수=아래로 내려감). 말뚝 끝과 맞추기.")]
    [SerializeField, Range(-40f, 40f)] private float plantedShadowYOffset = -4f;

    [Header("Font Sizes")]
    [SerializeField, Range(10, 32)] private int optionDescFontSize = 20;

    [Header("옵션 카드 제목 (Mystery / Heal) — 아이콘 위쪽에 표시")]
    [Tooltip("제목 폰트 크기.")]
    [SerializeField, Range(16, 60)] private int nameFontSize = 26;
    [Tooltip("제목 위치 오프셋 (px). 기준점=아이콘 위쪽 끝. X 양수=오른쪽, Y 양수=아이콘에서 더 위로 멀어짐.")]
    [SerializeField] private Vector2 nameOffset = new(0f, 50f);
    [Tooltip("제목 영역 폭 오버라이드 (px). 0=설명 영역과 동일한 폭 (카드 폭 - 좌우 패딩).")]
    [SerializeField, Range(0f, 600f)] private float nameWidthOverride = 0f;
    [Tooltip("제목 영역 높이 오버라이드 (px). 0=폰트 크기에 맞춰 자동.")]
    [SerializeField, Range(0f, 200f)] private float nameHeightOverride = 0f;
    [Tooltip("제목 보물(Mystery) 색.")]
    [SerializeField] private Color treasureNameColor = new(1f, 0.83f, 0.29f); // #FFD54A
    [Tooltip("제목 휴식(Heal) 색.")]
    [SerializeField] private Color restNameColor = new(0.91f, 0.29f, 0.29f); // #E84A4A

    [Header("옵션 카드 본문 (Free Relic Inside / Recover ...) — 위치/크기")]
    [Tooltip("본문 위치 오프셋 (px). 기준점=설명 영역 좌상단(아이콘 아래). X 양수=오른쪽, Y 양수=아래.")]
    [SerializeField] private Vector2 bodyOffset = new(0f, 0f);
    [Tooltip("본문 폰트 크기 오버라이드 — 0=Option Desc Font Size 사용, 그 외는 이 값으로.")]
    [SerializeField, Range(0, 32)] private int bodyFontSizeOverride = 0;
    [Tooltip("본문 영역 폭 오버라이드 (px). 0=설명 영역 기본 폭(카드 폭 - 좌우 패딩).")]
    [SerializeField, Range(0f, 600f)] private float bodyWidthOverride = 0f;
    [Tooltip("본문 영역 높이 오버라이드 (px). 0=설명 영역 잔여 높이(자동, 카드 하단까지).")]
    [SerializeField, Range(0f, 400f)] private float bodyHeightOverride = 0f;

    [Header("옵션 카드 제목/본문 외곽선 (이름·본문 공통)")]
    [Tooltip("외곽선 on/off.")]
    [SerializeField] private bool nameOutlineEnabled = true;
    [Tooltip("외곽선 두께 (px). 0.3~0.6 이 아주 얇음. 값이 클수록 굵어짐.")]
    [SerializeField, Range(0f, 3f)] private float nameOutlineThickness = 0.4f;
    [Tooltip("외곽선 색. 알파로 진하기 조절 (0=안보임, 1=불투명).")]
    [SerializeField] private Color nameOutlineColor = new(0f, 0f, 0f, 0.55f);
    [Tooltip("4방향(상하좌우) 대신 8방향(대각선 포함)으로 렌더 — 더 굵고 둥근 외곽선.")]
    [SerializeField] private bool nameOutline8Dir = false;

    [Header("글씨 드롭 섀도우 (양피지에 박힌 느낌)")]
    [Tooltip("글씨 뒤 그림자 on/off. 이름+본문 모두 동일하게 적용.")]
    [SerializeField] private bool textShadowEnabled = true;
    [Tooltip("그림자 오프셋 (px). Y 양수=아래로, X 양수=오른쪽.")]
    [SerializeField] private Vector2 textShadowOffset = new(0.8f, 1.2f);
    [Tooltip("그림자 색 + 알파. 따뜻한 짙은 갈색 권장.")]
    [SerializeField] private Color textShadowColor = new(0.08f, 0.04f, 0.02f, 0.28f);

    [Header("Colors")]
    [Tooltip("옵션 본문 글자 색. 어두운 카드 위에서는 밝은 크림/오프화이트가 가독성 좋음.")]
    [SerializeField] private Color optionDescColor = new(0.96f, 0.91f, 0.78f);

    [Header("Font (Resources/Fonts/* — 확장자 제외)")]
    [Tooltip("폰트 경로. 추천 경로:\n• Fonts/NotoSansKR-VariableFont_wght — 모던 산세리프, 아크나이츠/그랑블루 톤 (UI 추천)\n• Fonts/IMFellEnglish-Regular — 빈티지 책체, 작은 본문 가독성↑\n• Fonts/Metamorphous-Regular — 고딕 캐피털 (장식적)\n• Fonts/MedievalSharp-Regular — 중세 LARP\n• Fonts/Cinzel-VariableFont_wght — 클래식 로마 세리프")]
    [SerializeField] private string fontResourcePath = "Fonts/NotoSansKR-VariableFont_wght";

    private readonly List<Action> _pending = new();

    // 아트 — VillageUI/ 폴더에 정리된 5종만 사용. 헤더(HP/Gold/Mana/Floor)는 BattleUI.DrawTopBar가 자체 자산 사용.
    private Texture2D _glowTex;
    private Texture2D _treasureIconTex; // 좌측 옵션 아이콘 — VillageUI/TreasureChest
    private Texture2D _restIconTex;     // 우측 옵션 아이콘 — VillageUI/RestHeart
    private Texture2D _bgTex;           // 전체 화면 배경 — VillageUI/BackGround
    private Texture2D _npcTex;          // NPC 상반신 — VillageUI/NPC
    private Texture2D _optionCardTex;   // 선택지 카드 프레임 — VillageUI/OptionCardPanel

    // 진입 엣지 디텍션 + 제스처 타이머. _gestureT < 0 = 재생 안 함.
    private bool _wasInVillage;
    private float _gestureT = -1f;

    private Font _displayFont;
    private string _loadedFontPath;

    private GUIStyle _optionDescStyle;
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

        // Village 진입 엣지에서 인트로 타이머 리셋. 진입 후에는 계속 누적 (호흡은 Time.time 사용).
        bool inVillage = gsm.State == GameState.Village;
        if (inVillage && !_wasInVillage && introEnabled) _gestureT = 0f;
        _wasInVillage = inVillage;
        if (_gestureT >= 0f && inVillage) _gestureT += Time.deltaTime;

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

        // 옵션 카드: NPC 인트로 완료 후 페이드 인. 완료 전에는 아예 안 그려서 클릭도 방지.
        float optionsAlpha = 1f;
        if (introEnabled && _gestureT >= 0f)
        {
            float elapsed = _gestureT - introDuration;
            if (elapsed < 0f) optionsAlpha = 0f;
            else if (elapsed < optionsFadeInDuration && optionsFadeInDuration > 0f)
                optionsAlpha = Mathf.SmoothStep(0f, 1f, elapsed / optionsFadeInDuration);
        }
        if (optionsAlpha > 0.001f)
        {
            var prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, optionsAlpha);
            DrawOptions(gsm, run);
            GUI.color = prev;
        }

    }

    // =========================================================
    // Drawing
    // =========================================================

    private void DrawBackdrop()
    {
        var prev = GUI.color;

        // 배경 이미지 — Inspector 의 offset/scale 적용. 없으면 단색 폴백.
        if (_bgTex != null)
        {
            GUI.color = Color.white;
            float bgW = RefW * bgScale;
            float bgH = RefH * bgScale;
            float bgX = (RefW - bgW) * 0.5f + bgOffsetX;
            float bgY = (RefH - bgH) * 0.5f + bgOffsetY;
            GUI.DrawTexture(new Rect(bgX, bgY, bgW, bgH), _bgTex, ScaleMode.ScaleAndCrop);
            // UI 가독성 확보용 딤 오버레이 — 항상 전체 화면.
            GUI.color = backdropColor;
            GUI.DrawTexture(new Rect(0, 0, RefW, RefH), Texture2D.whiteTexture);
        }
        else
        {
            GUI.color = new Color(0.03f, 0.02f, 0.04f, 0.88f);
            GUI.DrawTexture(new Rect(0, 0, RefW, RefH), Texture2D.whiteTexture);
        }

        GUI.color = prev;
    }

    private void DrawNPC()
    {
        if (!npcEnabled || _npcTex == null) return;

        float baseX = npcAlignLeft ? npcXOffset : (RefW - npcSize.x - npcXOffset);

        // 인트로 진행도 (0 = 막 등장, 1 = 완료). 진입 후엔 항상 1로 고정.
        float introT = 1f;
        if (introEnabled && _gestureT >= 0f && introDuration > 0f)
        {
            introT = Mathf.Clamp01(_gestureT / introDuration);
            introT = Mathf.SmoothStep(0f, 1f, introT);
        }

        float alpha = introT; // 알파: 페이드 인
        float scale = Mathf.Lerp(introScaleFrom, 1f, introT); // 스케일: 살짝 커지며 등장
        Vector2 slideOff = introSlideFrom * (1f - introT); // 슬라이드: 오프셋에서 원위치로

        // 호흡 루프: 상하로 미세하게 흔들림. 인트로 중엔 감쇠해서 등장과 충돌 안 나게.
        float breathOff = 0f;
        if (breathingEnabled && breathingPeriod > 0f)
        {
            float phase = Time.time * (Mathf.PI * 2f / breathingPeriod);
            breathOff = Mathf.Sin(phase) * breathingAmplitudePx * introT;
        }

        float w = npcSize.x * scale;
        float h = npcSize.y * scale;
        float cx = baseX + npcSize.x * 0.5f;
        float cy = npcY  + npcSize.y * 0.5f;
        var rect = new Rect(
            cx - w * 0.5f + slideOff.x,
            cy - h * 0.5f + slideOff.y + breathOff,
            w, h);

        var prev = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.DrawTexture(rect, _npcTex, ScaleMode.ScaleToFit);
        GUI.color = prev;
    }

    /// <summary>
    /// 배틀/맵/마을 공용 상단 HUD (HP/Gold/Potion/Relic + Floor/Total).
    /// 치트 진입 등으로 CurrentMap이 null이어도 HUD 는 여전히 그려짐 (Floor 정보만 RunState 값 사용).
    /// </summary>
    private void DrawHeader(RunState run)
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null) return;
        var battleUI = gsm.GetComponent<BattleUI>();
        if (battleUI == null)
        {
            Debug.LogWarning("[VillageUI] BattleUI 컴포넌트 없음 — 상단 HUD 생략.");
            return;
        }

        // HUD 스트립 + 구분선은 BattleUI.DrawTopBar가 공통 처리 (BattleUI Inspector의 Village 색 사용).
        var map = gsm.CurrentMap;
        int currentFloor = map != null ? map.currentFloor : run.currentFloor;
        int totalFloors = map != null ? map.totalFloors : 15; // fallback — 치트 진입 시 챕터 기본값
        battleUI.DrawTopBar(BattleUI.HudContext.Village, run, currentFloor, totalFloors);

        // 덱 뷰어 오버레이 — 상단 덱 버튼 클릭 시 열림.
        battleUI.DrawDeckViewerOverlay(gsm);
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
        int pctLabel = Mathf.RoundToInt(restHealPct * 100f);

        var ev = Event.current;
        bool leftHover = leftRect.Contains(ev.mousePosition);
        bool rightHover = rightRect.Contains(ev.mousePosition);

        DrawOptionCard(
            leftRect,
            _treasureIconTex,
            treasureName,
            treasureNameColor,
            treasureDesc,
            treasureGlowColor,
            leftHover);

        string restBody = $"Recover {pctLabel}% HP\n<color=#E84A4A>{run.playerCurrentHp} → {afterHp}</color>";
        DrawOptionCard(
            rightRect,
            _restIconTex,
            restName,
            restNameColor,
            restBody,
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

    private void DrawOptionCard(Rect rect, Texture2D icon, string name, Color nameColor, string description, Color glowColor, bool hover)
    {
        // 호버 시 카드 전체를 하나의 단위로 스케일 — GUI.matrix 로 중심 기준 확대.
        Matrix4x4 prevMatrix = GUI.matrix;
        if (hover && optionHoverScale > 1f)
        {
            GUIUtility.ScaleAroundPivot(new Vector2(optionHoverScale, optionHoverScale), rect.center);
        }

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

        // 말뚝 접촉 그림자 — 패널 아래에 깔아서 말뚝 끝이 땅에 박힌 느낌.
        if (plantedShadowEnabled && _glowTex != null)
        {
            var prevShadow = GUI.color;
            GUI.color = plantedShadowColor;
            float shadowCy = rect.yMax + plantedShadowYOffset;
            float[] xFactors = { plantedStakeXFactors.x, plantedStakeXFactors.y };
            foreach (var xf in xFactors)
            {
                float cx = rect.x + rect.width * xf;
                var sRect = new Rect(
                    cx - plantedShadowSize.x * 0.5f,
                    shadowCy - plantedShadowSize.y * 0.5f,
                    plantedShadowSize.x, plantedShadowSize.y);
                GUI.DrawTexture(sRect, _glowTex, ScaleMode.StretchToFill);
            }
            GUI.color = prevShadow;
        }

        // 카드 프레임 — VillageUI/OptionCardPanel (없으면 단색 fallback).
        if (_optionCardTex != null)
            GUI.DrawTexture(rect, _optionCardTex, ScaleMode.StretchToFill);
        else
            DrawFilledRect(rect, new Color(0.10f, 0.14f, 0.20f, 0.96f));

        // 아이콘 배치 기준 — 프레임 중앙(+ Inspector 의 X 오프셋) / Y 는 카드 높이의 비율.
        float medSize = medallionSize;
        float medCx = rect.center.x + iconCenterXOffset;
        float medCy = rect.y + rect.height * medallionCenterYFactor;
        if (icon != null)
        {
            float iconSize = medSize * iconSizeFactor;
            float cy = medCy;

            // 1) 그라운딩 그림자 — 아이콘이 양피지에 "앉아있는" 느낌 연출. 글로우보다 먼저 깔림.
            if (iconShadowEnabled && _glowTex != null)
            {
                float shadowW = iconSize * iconShadowScale;
                float shadowH = shadowW * iconShadowVerticalSquish;
                var shadowRect = new Rect(
                    medCx - shadowW * 0.5f,
                    cy - shadowH * 0.5f + iconShadowYOffset,
                    shadowW, shadowH);
                var prev = GUI.color;
                GUI.color = iconShadowColor;
                GUI.DrawTexture(shadowRect, _glowTex, ScaleMode.StretchToFill);
                GUI.color = prev;
            }

            // 2) 글로우 (아이콘 뒤쪽) — 기본 앰버 톤으로 프레임 보석과 조화.
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

            // 3) 아이콘 본체 — 따뜻한 틴트 적용해서 프레임 색감과 섞이게.
            var iconRect = new Rect(medCx - iconSize * 0.5f, cy - iconSize * 0.5f, iconSize, iconSize);
            var prevIcon = GUI.color;
            GUI.color = iconTint;
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
            GUI.color = prevIcon;
        }

        // 설명 영역 — 아이콘 아래(본문 전용 컨테이너).
        float descTop = medCy + medSize * 0.5f + optionDescTopGap;
        var descRect = new Rect(rect.x + optionDescXPad, descTop, rect.width - optionDescXPad * 2f, rect.yMax - descTop - optionDescBottomPad);

        // 제목(Mystery / Heal) — 아이콘 위쪽에 별도 배치. nameOffset.y 양수 = 아이콘에서 위로 멀어짐.
        // 호버 스케일은 GUI.matrix 가 처리하므로 폰트/오프셋은 원본 값 그대로 사용.
        int prevNameFS = _optionDescStyle.fontSize;
        var prevNameColor = _optionDescStyle.normal.textColor;
        _optionDescStyle.fontSize = nameFontSize;
        float nameAreaW = nameWidthOverride > 0f ? nameWidthOverride : rect.width - optionDescXPad * 2f;
        float nameAutoH = _optionDescStyle.CalcHeight(new GUIContent(name), nameAreaW);
        float nameH = nameHeightOverride > 0f ? nameHeightOverride : nameAutoH;
        float nameY = (medCy - medSize * 0.5f) - nameOffset.y - nameH;
        // 제목 폭이 오버라이드되면 카드 중앙 기준 정렬, 아니면 좌측 패딩 시작.
        float nameX = nameWidthOverride > 0f
            ? rect.center.x - nameAreaW * 0.5f + nameOffset.x
            : rect.x + optionDescXPad + nameOffset.x;
        var nameRect = new Rect(nameX, nameY, nameAreaW, nameH);

        // 이름 외곽선 — 4/8방향 오프셋.
        if (nameOutlineEnabled && nameOutlineThickness > 0f && !string.IsNullOrEmpty(name))
        {
            float t = nameOutlineThickness;
            var prevGUIColor = GUI.color;
            GUI.color = nameOutlineColor;
            _optionDescStyle.normal.textColor = Color.white; // GUI.color 곱셈 적용용
            LockStateColors(_optionDescStyle); // 호버 시 textColor 바뀌지 않게 모든 상태 통일
            GUI.Label(new Rect(nameRect.x + t, nameRect.y,     nameRect.width, nameRect.height), name, _optionDescStyle);
            GUI.Label(new Rect(nameRect.x - t, nameRect.y,     nameRect.width, nameRect.height), name, _optionDescStyle);
            GUI.Label(new Rect(nameRect.x,     nameRect.y + t, nameRect.width, nameRect.height), name, _optionDescStyle);
            GUI.Label(new Rect(nameRect.x,     nameRect.y - t, nameRect.width, nameRect.height), name, _optionDescStyle);
            if (nameOutline8Dir)
            {
                GUI.Label(new Rect(nameRect.x + t, nameRect.y + t, nameRect.width, nameRect.height), name, _optionDescStyle);
                GUI.Label(new Rect(nameRect.x - t, nameRect.y + t, nameRect.width, nameRect.height), name, _optionDescStyle);
                GUI.Label(new Rect(nameRect.x + t, nameRect.y - t, nameRect.width, nameRect.height), name, _optionDescStyle);
                GUI.Label(new Rect(nameRect.x - t, nameRect.y - t, nameRect.width, nameRect.height), name, _optionDescStyle);
            }
            GUI.color = prevGUIColor;
        }
        // 이름 드롭 섀도우 — 아래로 깔리는 그림자로 양피지에 박힌 느낌.
        if (textShadowEnabled && !string.IsNullOrEmpty(name))
        {
            var prevGUIColor = GUI.color;
            GUI.color = textShadowColor;
            _optionDescStyle.normal.textColor = Color.white;
            LockStateColors(_optionDescStyle);
            GUI.Label(new Rect(nameRect.x + textShadowOffset.x, nameRect.y + textShadowOffset.y, nameRect.width, nameRect.height), name, _optionDescStyle);
            GUI.color = prevGUIColor;
        }
        // 이름 본체 — 호버 색 변경 방지 위해 모든 상태 color 통일.
        _optionDescStyle.normal.textColor = nameColor;
        LockStateColors(_optionDescStyle);
        GUI.Label(nameRect, name, _optionDescStyle);

        // 이름 아래에 본문 렌더 — 원래 폰트/색 복구 후. 오프셋/폰트 오버라이드 인스펙터 반영.
        int baseBodyFS = bodyFontSizeOverride > 0 ? bodyFontSizeOverride : prevNameFS;
        _optionDescStyle.fontSize = baseBodyFS;
        _optionDescStyle.normal.textColor = prevNameColor;
        LockStateColors(_optionDescStyle);
        float bodyTop = descRect.y + bodyOffset.y;
        float bodyW = bodyWidthOverride > 0f ? bodyWidthOverride : descRect.width;
        float bodyMaxH = descRect.yMax - bodyTop;
        float bodyH = bodyHeightOverride > 0f ? Mathf.Min(bodyHeightOverride, bodyMaxH) : bodyMaxH;
        var bodyRect = new Rect(descRect.x + bodyOffset.x, bodyTop, bodyW, bodyH);

        // 본문도 외곽선 (이름과 같은 설정 재사용).
        if (nameOutlineEnabled && nameOutlineThickness > 0f && !string.IsNullOrEmpty(description))
        {
            float t = nameOutlineThickness;
            var prevGUIColor = GUI.color;
            GUI.color = nameOutlineColor;
            var prevTC = _optionDescStyle.normal.textColor;
            _optionDescStyle.normal.textColor = Color.white;
            LockStateColors(_optionDescStyle);
            GUI.Label(new Rect(bodyRect.x + t, bodyRect.y,     bodyRect.width, bodyRect.height), description, _optionDescStyle);
            GUI.Label(new Rect(bodyRect.x - t, bodyRect.y,     bodyRect.width, bodyRect.height), description, _optionDescStyle);
            GUI.Label(new Rect(bodyRect.x,     bodyRect.y + t, bodyRect.width, bodyRect.height), description, _optionDescStyle);
            GUI.Label(new Rect(bodyRect.x,     bodyRect.y - t, bodyRect.width, bodyRect.height), description, _optionDescStyle);
            if (nameOutline8Dir)
            {
                GUI.Label(new Rect(bodyRect.x + t, bodyRect.y + t, bodyRect.width, bodyRect.height), description, _optionDescStyle);
                GUI.Label(new Rect(bodyRect.x - t, bodyRect.y + t, bodyRect.width, bodyRect.height), description, _optionDescStyle);
                GUI.Label(new Rect(bodyRect.x + t, bodyRect.y - t, bodyRect.width, bodyRect.height), description, _optionDescStyle);
                GUI.Label(new Rect(bodyRect.x - t, bodyRect.y - t, bodyRect.width, bodyRect.height), description, _optionDescStyle);
            }
            GUI.color = prevGUIColor;
            _optionDescStyle.normal.textColor = prevTC;
            LockStateColors(_optionDescStyle);
        }

        // 본문 드롭 섀도우
        if (textShadowEnabled && !string.IsNullOrEmpty(description))
        {
            var prevGUIColor = GUI.color;
            GUI.color = textShadowColor;
            var prevTC = _optionDescStyle.normal.textColor;
            _optionDescStyle.normal.textColor = Color.white;
            LockStateColors(_optionDescStyle);
            GUI.Label(new Rect(bodyRect.x + textShadowOffset.x, bodyRect.y + textShadowOffset.y, bodyRect.width, bodyRect.height), description, _optionDescStyle);
            GUI.color = prevGUIColor;
            _optionDescStyle.normal.textColor = prevTC;
            LockStateColors(_optionDescStyle);
        }

        GUI.Label(bodyRect, description, _optionDescStyle);
        _optionDescStyle.fontSize = prevNameFS;

        // 호버 스케일 복구.
        GUI.matrix = prevMatrix;
    }

    // =========================================================
    // 리소스 / 스타일
    // =========================================================

    private void EnsureStyles()
    {
        if (_stylesReady) return;

        // VillageUI 전용 에셋 — 5종만. 헤더(상단 네비)는 BattleUI.DrawTopBar 가 InGame/ 자산을 그대로 사용.
        _treasureIconTex = Resources.Load<Texture2D>("VillageUI/TreasureChest");
        _restIconTex     = Resources.Load<Texture2D>("VillageUI/RestHeart");
        _bgTex           = Resources.Load<Texture2D>("VillageUI/BackGround");
        _npcTex          = Resources.Load<Texture2D>("VillageUI/NPC");
        _optionCardTex   = Resources.Load<Texture2D>("VillageUI/OptionCardPanel");

        _displayFont = Resources.Load<Font>(fontResourcePath)
                    ?? Resources.Load<Font>("Fonts/Cinzel-VariableFont_wght"); // 폰트 경로 오타 대비 폴백
        _loadedFontPath = fontResourcePath;

        _glowTex = CreateRadialGlowTexture(64);

        _optionDescStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFont, alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold, wordWrap = true, richText = true,
        };

        ApplyStyleValues();
        _stylesReady = true;
    }

    // Inspector 값이 바뀌면 매 프레임 스타일에 반영
    private void ApplyStyleValues()
    {
        if (_optionDescStyle == null) return;

        // 폰트 핫스왑 — 인스펙터에서 fontResourcePath 변경 시 즉시 반영.
        if (_loadedFontPath != fontResourcePath)
        {
            var newFont = Resources.Load<Font>(fontResourcePath);
            if (newFont != null)
            {
                _displayFont = newFont;
                _optionDescStyle.font = _displayFont;
            }
            _loadedFontPath = fontResourcePath;
        }

        _optionDescStyle.fontSize = optionDescFontSize;
        _optionDescStyle.normal.textColor = optionDescColor;

        // 호버/액티브 시 색 변경 방지 — 모든 state에 동일 색 복사
        LockStateColors(_optionDescStyle);
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
