using System;
using System.Collections.Generic;
using DianoCard.Data;
using DianoCard.Game;
using UnityEngine;

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
    private static readonly string[] AvailableCharacterIds = new[] { "CH001", "CH002" };
    private string _selectedCharacterId = "CH001";

    private readonly List<Action> _pending = new();

    private CharacterData _selectedCharacter;

    private Texture2D _backgroundTexture;
    private Texture2D _cardSlotSelected;
    private Texture2D _cardSlotLocked;
    private Texture2D _buttonBack;
    private Texture2D _buttonConfirm;
    private Texture2D _archaeologistCardPortrait;
    private Texture2D _iconHeart;
    private Texture2D _iconCoin;
    private Texture2D _iconPassive;
    private Texture2D _selectedCardBaked;  // 프레임+초상화를 한 장으로 구운 합성 텍스처

    private Font _displayFont;  // 영문 디스플레이 (Cinzel)
    private Font _bodyFont;     // 한글 본문 (Noto Sans KR)

    // ----- 레이아웃 (1280×720 가상 좌표) -----
    private static readonly Rect InfoPanelRect = new Rect(60, 60, 600, 340);

    // 5개 카드 (150×210), 가로 중앙 정렬, 카드 간 간격 30
    private static readonly Rect[] CardRects = new Rect[]
    {
        new Rect(205, 440, 150, 210),
        new Rect(385, 440, 150, 210),
        new Rect(565, 440, 150, 210),
        new Rect(745, 440, 150, 210),
        new Rect(925, 440, 150, 210),
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
    private bool _stylesReady;
    private bool _assetsLoaded;

    private float _comingSoonTimer;

    void Start()
    {
        LoadAssets();
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
    }

    private void LoadAssets()
    {
        // 테이블에서 캐릭터 정보 로드
        DataManager.Instance.Load();
        _selectedCharacter = DataManager.Instance.GetCharacter(_selectedCharacterId);
        if (_selectedCharacter == null)
            Debug.LogError($"[CharacterSelectUI] Missing character data: {_selectedCharacterId}");

        // UI 요소 — CharSelect/UI/
        _cardSlotSelected  = Resources.Load<Texture2D>("CharSelect/UI/CardSlot_Selected");
        _cardSlotLocked    = Resources.Load<Texture2D>("CharSelect/UI/CardSlot_Locked");
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

        // 배경 — CharSelect/Background/ 우선, 없으면 로비 배경 폴백
        _backgroundTexture = Resources.Load<Texture2D>("CharSelect/Background/CharSelect_Background")
                          ?? Resources.Load<Texture2D>("Lobby/Main_Background");

        if (_cardSlotSelected == null) Debug.LogWarning("[CharacterSelectUI] Missing CharSelect/UI/CardSlot_Selected");
        if (_cardSlotLocked == null)   Debug.LogWarning("[CharacterSelectUI] Missing CharSelect/UI/CardSlot_Locked");
        if (_buttonBack == null)       Debug.LogWarning("[CharacterSelectUI] Missing CharSelect/UI/Button_Back");
        if (_buttonConfirm == null)    Debug.LogWarning("[CharacterSelectUI] Missing CharSelect/UI/Button_Confirm");
        if (_archaeologistCardPortrait == null) Debug.LogWarning("[CharacterSelectUI] Missing Character_select/Char_Archaeologist_Card");
        if (_iconHeart == null)   Debug.LogWarning("[CharacterSelectUI] Missing CharSelect/Icon/ico_heart");
        if (_iconCoin == null)    Debug.LogWarning("[CharacterSelectUI] Missing CharSelect/Icon/ico_coin");
        if (_iconPassive == null) Debug.LogWarning("[CharacterSelectUI] Missing CharSelect/Icon/passive");
        if (_backgroundTexture == null) Debug.LogWarning("[CharacterSelectUI] Missing CharSelect/Background/CharSelect_Background");
        if (_displayFont == null) Debug.LogWarning("[CharacterSelectUI] Missing Fonts/Cinzel-VariableFont_wght");
        if (_bodyFont == null) Debug.LogWarning("[CharacterSelectUI] Missing Fonts/NotoSansKR-VariableFont_wght");

        BuildSelectedCardComposite();

        _assetsLoaded = true;
    }

    /// <summary>
    /// 선택된 카드 프레임 + 캐릭터 초상화를 RenderTexture에 합성해서
    /// 한 장의 Texture2D로 굽는다. 펄스 스케일링 시 두 레이어가 따로
    /// 픽셀 스냅되어 흔들리는 문제를 원천 차단.
    /// </summary>
    private void BuildSelectedCardComposite()
    {
        if (_selectedCardBaked != null) return;
        if (_cardSlotSelected == null || _archaeologistCardPortrait == null) return;

        // 펄스 시 부드러운 sub-pixel 샘플링을 위해 4배 해상도로 베이크.
        // 해상도가 충분히 높으면 bilinear filter shimmer가 거의 인지되지 않음.
        int w = Mathf.RoundToInt(CardRects[0].width  * 4f);
        int h = Mathf.RoundToInt(CardRects[0].height * 4f);

        var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
        rt.filterMode = FilterMode.Bilinear;

        var prevActive = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(true, true, Color.clear);
        GL.PushMatrix();
        GL.LoadPixelMatrix(0, w, h, 0);

        // 1) 프레임 — 카드 전체 영역
        Graphics.DrawTexture(new Rect(0, 0, w, h), _cardSlotSelected);

        // 2) 초상화 — 안쪽 패딩 (베이크 스케일에 맞춰 56px = 14 * 4)
        const int pad = 56;
        var portraitRect = new Rect(pad, pad, w - pad * 2, h - pad * 2);
        float texAspect  = _archaeologistCardPortrait.width / (float)_archaeologistCardPortrait.height;
        float rectAspect = portraitRect.width / portraitRect.height;
        Rect uvRect;
        if (texAspect > rectAspect)
        {
            // 텍스처가 더 가로로 넓음 → 좌우 잘라냄 (ScaleAndCrop)
            float u = rectAspect / texAspect;
            uvRect = new Rect((1f - u) * 0.5f, 0f, u, 1f);
        }
        else
        {
            // 텍스처가 더 세로로 김 → 상하 잘라냄
            float v = texAspect / rectAspect;
            uvRect = new Rect(0f, (1f - v) * 0.5f, 1f, v);
        }
        Graphics.DrawTexture(portraitRect, _archaeologistCardPortrait, uvRect, 0, 0, 0, 0);

        GL.PopMatrix();

        // mipChain = true: 여러 해상도의 미피맵을 자동 생성
        // Trilinear: 미p 레벨 사이를 보간 → 스케일 변화 시 부드러움
        // anisoLevel: 비스듬한 샘플링 시 선명도 ↑
        _selectedCardBaked = new Texture2D(w, h, TextureFormat.RGBA32, true)
        {
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Trilinear,
            wrapMode = TextureWrapMode.Clamp,
            anisoLevel = 4,
        };
        _selectedCardBaked.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        _selectedCardBaked.Apply(updateMipmaps: true);

        RenderTexture.active = prevActive;
        RenderTexture.ReleaseTemporary(rt);
    }

    void OnDestroy()
    {
        if (_selectedCardBaked != null)
        {
            Destroy(_selectedCardBaked);
            _selectedCardBaked = null;
        }
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

        // 1) 배경 — 화면 전체
        GUI.matrix = Matrix4x4.identity;
        DrawBackground();

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
            GUI.DrawTexture(
                new Rect(0, 0, Screen.width, Screen.height),
                _backgroundTexture,
                ScaleMode.ScaleAndCrop,
                alphaBlend: true);

            // 좀 더 어둡게 dim 처리해서 UI 요소들이 잘 보이도록
            // (CharSelect_Background에 캐릭터가 중앙에 있어서 UI 영역과 겹침)
            var prev = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.42f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = prev;
        }
        else
        {
            var prev = GUI.color;
            GUI.color = new Color(0.08f, 0.06f, 0.05f, 1f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = prev;
        }
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
        var ev = Event.current;

        // 슬롯별 렌더 — AvailableCharacterIds[i]에 매핑. 선택 중이면 펄스 + 베이크 포트레이트, 아니면 플레인 선택 프레임 + 이름 라벨.
        for (int i = 0; i < CardRects.Length; i++)
        {
            bool isAvailable = i < AvailableCharacterIds.Length;
            if (!isAvailable)
            {
                if (_cardSlotLocked != null)
                    DrawScalableTexture(CardRects[i], _cardSlotLocked, ScaleMode.StretchToFill);
                continue;
            }

            string slotCharId = AvailableCharacterIds[i];
            bool isSelected = slotCharId == _selectedCharacterId;
            Rect baseRect = CardRects[i];

            if (isSelected)
            {
                // 선택된 슬롯: 6초 사이클 펄스 + 베이크된 프레임+초상화
                float sinNorm = (Mathf.Sin(Time.time * (Mathf.PI * 2f / 6f)) + 1f) * 0.5f;
                float pulse = Mathf.SmoothStep(0f, 1f, sinNorm);
                float pulseScale = Mathf.Lerp(1.00f, 1.10f, pulse);
                float w = baseRect.width  * pulseScale;
                float h = baseRect.height * pulseScale;
                var pulsedRect = new Rect(baseRect.center.x - w * 0.5f, baseRect.center.y - h * 0.5f, w, h);
                Texture2D selectedTex = _selectedCardBaked ?? _cardSlotSelected;
                if (selectedTex != null)
                    GUI.DrawTexture(pulsedRect, selectedTex, ScaleMode.StretchToFill, alphaBlend: true);
            }
            else
            {
                // 비선택 사용 가능 슬롯: 프레임만 + 이름 라벨, 살짝 dim
                var prev = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.55f);
                if (_cardSlotSelected != null)
                    DrawScalableTexture(baseRect, _cardSlotSelected, ScaleMode.StretchToFill);
                GUI.color = prev;

                var ch = DataManager.Instance.GetCharacter(slotCharId);
                if (ch != null)
                {
                    var labelRect = new Rect(baseRect.x, baseRect.center.y - 12f, baseRect.width, 24f);
                    GUI.Label(labelRect, ch.nameKr, _cardNameStyle);
                }
            }
        }

        // 클릭 처리: 사용 가능 슬롯 → 선택 변경, 잠긴 슬롯 → Coming Soon 표시.
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

    /// <summary>선택 캐릭터 변경 — 데이터 + 포트레이트 재로드 + 베이크 재빌드.</summary>
    private void SwitchSelection(string characterId)
    {
        _selectedCharacterId = characterId;
        _selectedCharacter = DataManager.Instance.GetCharacter(characterId);
        string portraitName = _selectedCharacter != null ? _selectedCharacter.cardPortrait : null;
        if (!string.IsNullOrEmpty(portraitName))
            _archaeologistCardPortrait = Resources.Load<Texture2D>("Character_select/" + portraitName);
        // 기존 베이크 파기 → 다음 OnGUI 사이클에서 새 초상화로 다시 구움.
        if (_selectedCardBaked != null)
        {
            Destroy(_selectedCardBaked);
            _selectedCardBaked = null;
        }
        BuildSelectedCardComposite();
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

        float alpha = Mathf.Clamp01(_comingSoonTimer / 1.5f);
        var rect = new Rect(RefW / 2 - 160, RefH / 2 - 35, 320, 70);

        var prevColor = GUI.color;
        GUI.color = new Color(0, 0, 0, 0.75f * alpha);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = new Color(1f, 0.9f, 0.5f, alpha);
        GUI.Label(rect, "Coming Soon", _comingSoonStyle);
        GUI.color = prevColor;
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

        _stylesReady = true;
    }
}
