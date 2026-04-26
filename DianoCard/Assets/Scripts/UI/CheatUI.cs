using System.Collections.Generic;
using System.Linq;
using DianoCard.Battle;
using DianoCard.Data;
using DianoCard.Game;
using UnityEngine;

[DefaultExecutionOrder(2000)]
public class CheatUI : MonoBehaviour
{
    private bool _open;
    private Rect _windowRect = new(20f, 20f, 260f, 720f);
    private Vector2 _windowScroll;
    private GUIStyle _btnStyle;
    private GUIStyle _titleStyle;
    private GUIStyle _stateStyle;

    // 배경 리스트 캐시 — 전투 진입 시 1회만 Resources.LoadAll 수행
    private string[] _bgNames;
    private Vector2 _bgScroll;

    // 슬롯 강제 소환용 캐시 — SUMMON 카드 전체 목록.
    private List<CardData> _summonCards;
    private Vector2 _summonScroll;

    // 카드 프리뷰 (프레임 디자인 확인용)
    private bool _cardPreviewOpen;
    private int _cardPreviewIndex;
    private Rarity _cardPreviewRarity = Rarity.COMMON;
    private List<CardData> _cardPreviewList;
    private GUIStyle _previewLabelStyle;
    private float _cardPreviewHeight = 540f;   // 카드 세로 픽셀 (1280x720 가상 좌표). 슬라이더로 300~560 확대.
    private bool  _cardPreviewSlotOnly;        // true = 프레임 레이어만, 카드 데이터 숨김

    void Update()
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.backquoteKey.wasPressedThisFrame)
        {
            _open = !_open;
            _bgNames = null; // 다음 열 때 배경 목록 재로드
            _summonCards = null;
        }
    }

    void OnGUI()
    {
        // 카드 프리뷰는 런타임 치트 패널이 닫혀 있어도 동작해야 함 — 에디터 CheatWindow에서도 열 수 있음.
        if (!_open && !_cardPreviewOpen) return;

        var matrix = GUI.matrix;
        float scale = Screen.width / 1280f;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

        // 프리뷰는 BattleUI보다 위 (depth 낮을수록 앞).
        if (_cardPreviewOpen)
        {
            EnsureCheatStyles();          // DrawWindow 호출 안 거쳐도 스타일 확보
            GUI.depth = -100;
            DrawCardPreviewOverlay();
        }

        if (_open)
            _windowRect = GUI.Window(9999, _windowRect, DrawWindow, "");

        GUI.matrix = matrix;
    }

    // 스타일 생성을 DrawWindow 밖으로 뺀다 — DrawCardPreviewOverlay 도 _btnStyle 쓰기 때문.
    // GUI.skin 은 OnGUI 안에서만 안전하게 접근 가능하므로, 스타일 초기화도 OnGUI 경로에서만 호출.
    private void EnsureCheatStyles()
    {
        if (_btnStyle != null) return;
        _btnStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            fixedHeight = 30f,
        };
        _titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(1f, 0.85f, 0.4f) },
        };
        _stateStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.7f, 0.9f, 1f) },
        };
    }

    private void DrawWindow(int id)
    {
        EnsureCheatStyles();

        var gsm = GameStateManager.Instance;
        string state = gsm != null ? gsm.State.ToString() : "N/A";

        GUILayout.Space(4f);
        GUILayout.Label("CHEAT PANEL", _titleStyle);
        GUILayout.Label($"Current: {state}", _stateStyle);
        GUILayout.Space(8f);

        if (gsm == null)
        {
            GUILayout.Label("GameStateManager not found");
            GUI.DragWindow();
            return;
        }

        // 창 본체는 헤더(약 60px) 아래로 스크롤. 항목 늘어나도 화면 밖으로 잘리지 않도록.
        _windowScroll = GUILayout.BeginScrollView(_windowScroll, GUILayout.Height(_windowRect.height - 70f));

        if (GUILayout.Button("Lobby", _btnStyle))
            gsm.ReturnToLobby();

        if (GUILayout.Button("Character Select", _btnStyle))
            gsm.StartNewRun();

        if (GUILayout.Button("Reward", _btnStyle))
            gsm.Cheat_TriggerReward();

        if (GUILayout.Button("Shop", _btnStyle))
            gsm.Cheat_EnterShop();

        if (GUILayout.Button("Village", _btnStyle))
            gsm.Cheat_EnterVillage();

        GUILayout.Space(8f);
        GUILayout.Label("— 훈련장 입장 —", _stateStyle);

        if (GUILayout.Button("E901 이끼 수호석상 (보스)", _btnStyle))
            gsm.Cheat_StartBossBattle();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("E001", _btnStyle)) gsm.Cheat_StartBattleWith("E001");
        if (GUILayout.Button("E008", _btnStyle)) gsm.Cheat_StartBattleWith("E008");
        if (GUILayout.Button("E101", _btnStyle)) gsm.Cheat_StartBattleWith("E101");
        GUILayout.EndHorizontal();

        // ===== 전투 중에만 보이는 제어 =====
        var battle = GetActiveBattle();
        if (gsm.State == GameState.Battle && battle != null && battle.state != null)
        {
            GUILayout.Space(10f);
            GUILayout.Label("— 전투 중 제어 —", _stateStyle);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("전체 즉사", _btnStyle)) battle.Cheat_KillAllEnemies();
            if (GUILayout.Button("쫄류만 즉사", _btnStyle)) battle.Cheat_ClearAddsOnly();
            GUILayout.EndHorizontal();

            GUILayout.Label("보스 HP 설정", _stateStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("70%", _btnStyle)) battle.Cheat_SetPrimaryHpRatio(0.70f);
            if (GUILayout.Button("50%", _btnStyle)) battle.Cheat_SetPrimaryHpRatio(0.50f);
            if (GUILayout.Button("30%", _btnStyle)) battle.Cheat_SetPrimaryHpRatio(0.30f);
            if (GUILayout.Button("5%",  _btnStyle)) battle.Cheat_SetPrimaryHpRatio(0.05f);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("풀 회복", _btnStyle)) battle.Cheat_FullHeal();
            string invLabel = battle.state.player.cheatInvincible ? "무적 OFF" : "무적 ON";
            if (GUILayout.Button(invLabel, _btnStyle)) battle.Cheat_ToggleInvincible();
            GUILayout.EndHorizontal();

            // ===== 배경 전환 =====
            GUILayout.Space(8f);
            GUILayout.Label("— 배경 전환 —", _stateStyle);
            if (_bgNames == null)
            {
                var all = Resources.LoadAll<Texture2D>("Backgrounds");
                var list = new System.Collections.Generic.List<string>();
                foreach (var t in all)
                {
                    if (t != null) list.Add(t.name);
                }
                list.Sort();
                _bgNames = list.ToArray();
            }

            var battleUi = Object.FindFirstObjectByType<BattleUI>();
            _bgScroll = GUILayout.BeginScrollView(_bgScroll, GUILayout.Height(180f));
            foreach (var name in _bgNames)
            {
                if (GUILayout.Button(name, _btnStyle) && battleUi != null)
                    battleUi.Cheat_SetBackground($"Backgrounds/{name}");
            }
            GUILayout.EndScrollView();

            // ===== 공룡 슬롯 강제 지정 =====
            GUILayout.Space(8f);
            GUILayout.Label("— 공룡 슬롯 강제 지정 —", _stateStyle);

            // 현재 슬롯 상태 표시 + 비우기 버튼.
            string slot1 = battle.state.field.Count > 0 ? battle.state.field[0].data.nameKr : "(비어있음)";
            string slot2 = battle.state.field.Count > 1 ? battle.state.field[1].data.nameKr : "(비어있음)";
            GUILayout.BeginHorizontal();
            GUILayout.Label($"1번: {slot1}", _stateStyle);
            if (GUILayout.Button("X", _btnStyle, GUILayout.Width(30f))) battle.Cheat_ClearFieldSlot(0);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"2번: {slot2}", _stateStyle);
            if (GUILayout.Button("X", _btnStyle, GUILayout.Width(30f))) battle.Cheat_ClearFieldSlot(1);
            GUILayout.EndHorizontal();

            // SUMMON 카드 전체 캐시 — 카드 데이터는 전투 중 바뀌지 않으므로 한 번만 로드.
            if (_summonCards == null)
            {
                if (!DataManager.Instance.IsLoaded) DataManager.Instance.Load();
                _summonCards = DataManager.Instance.Cards.Values
                    .Where(c => c.cardType == CardType.SUMMON)
                    .OrderBy(c => c.subType)
                    .ThenBy(c => c.id)
                    .ToList();
            }

            _summonScroll = GUILayout.BeginScrollView(_summonScroll, GUILayout.Height(220f));
            foreach (var c in _summonCards)
            {
                GUILayout.BeginHorizontal();
                string label = $"{c.nameKr}";
                GUILayout.Label(label, GUILayout.Width(140f));
                if (GUILayout.Button("→1", _btnStyle, GUILayout.Width(40f))) battle.Cheat_SetFieldSlot(0, c.id);
                if (GUILayout.Button("→2", _btnStyle, GUILayout.Width(40f))) battle.Cheat_SetFieldSlot(1, c.id);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            // ===== 페어 자동 패킹 슬라이더 — 2마리 배치일 때 가로 겹침·세로 스태거 라이브 튜닝.
            if (battleUi != null)
            {
                GUILayout.Space(8f);
                GUILayout.Label("— 페어 패킹 (2마리 배치) —", _stateStyle);

                GUILayout.Label($"가로 겹침: {battleUi.PairOverlapPct:F2}");
                battleUi.PairOverlapPct = GUILayout.HorizontalSlider(battleUi.PairOverlapPct, 0f, 0.7f);

                GUILayout.Label($"세로 스태거: {battleUi.PairStaggerYPct:F2}");
                battleUi.PairStaggerYPct = GUILayout.HorizontalSlider(battleUi.PairStaggerYPct, 0f, 0.5f);

                GUILayout.Label($"가로 최소 간격: {battleUi.PairMinSpacingPct:F2}");
                battleUi.PairMinSpacingPct = GUILayout.HorizontalSlider(battleUi.PairMinSpacingPct, 0f, 0.6f);

                GUILayout.Label($"사이즈차 Y 부스트: {battleUi.PairSizeStaggerBoost:F2}");
                battleUi.PairSizeStaggerBoost = GUILayout.HorizontalSlider(battleUi.PairSizeStaggerBoost, 0f, 1.5f);
            }
        }

        GUILayout.Space(12f);
        GUILayout.Label("— Gold —", _stateStyle);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("+100G", _btnStyle) && gsm.CurrentRun != null)
            gsm.CurrentRun.gold += 100;
        if (GUILayout.Button("+500G", _btnStyle) && gsm.CurrentRun != null)
            gsm.CurrentRun.gold += 500;
        GUILayout.EndHorizontal();

        GUILayout.Space(12f);
        GUILayout.Label("— 카드 프리뷰 —", _stateStyle);
        string previewLabel = _cardPreviewOpen ? "프리뷰 닫기" : "카드 프리뷰 열기";
        if (GUILayout.Button(previewLabel, _btnStyle))
        {
            _cardPreviewOpen = !_cardPreviewOpen;
            if (_cardPreviewOpen) EnsureCardPreviewList();
        }
        // 슬롯 프리뷰 — 카드 데이터 없이 프레임 레이어만 큰 화면으로. Inspector rect 튜닝용.
        string slotLabel = _cardPreviewOpen && _cardPreviewSlotOnly ? "슬롯 모드 ON" : "카드 슬롯 프리뷰 (빈 프레임)";
        if (GUILayout.Button(slotLabel, _btnStyle))
        {
            _cardPreviewSlotOnly = !_cardPreviewSlotOnly;
            if (!_cardPreviewOpen)
            {
                _cardPreviewOpen = true;
                EnsureCardPreviewList();
            }
        }

        GUILayout.EndScrollView();

        GUI.DragWindow();
    }

    // Editor CheatWindow에서 호출하는 공개 API — 플레이 모드에서 오버레이 토글.
    public bool IsCardPreviewOpen => _cardPreviewOpen;

    public void OpenCardPreview(bool slotOnly)
    {
        _cardPreviewOpen = true;
        _cardPreviewSlotOnly = slotOnly;
        EnsureCardPreviewList();
    }

    public void CloseCardPreview() => _cardPreviewOpen = false;

    // 모든 카드 한 번 캐시 (id 정렬).
    private void EnsureCardPreviewList()
    {
        if (_cardPreviewList != null) return;
        if (!DataManager.Instance.IsLoaded) DataManager.Instance.Load();
        _cardPreviewList = DataManager.Instance.Cards.Values
            .OrderBy(c => c.id)
            .ToList();
        _cardPreviewIndex = Mathf.Clamp(_cardPreviewIndex, 0, Mathf.Max(0, _cardPreviewList.Count - 1));
    }

    private void DrawCardPreviewOverlay()
    {
        if (_cardPreviewList == null || _cardPreviewList.Count == 0) return;

        if (_previewLabelStyle == null)
        {
            _previewLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white },
            };
        }

        // 화면 어둡게 깔기.
        var fullRect = new Rect(0, 0, 1280, 720);
        var prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.85f);
        GUI.DrawTexture(fullRect, Texture2D.whiteTexture);
        GUI.color = prev;

        // 카드 크기 — 슬라이더로 조절. 상단 정보 바(56px) + 하단 버튼 바(~96px) 공간을 남긴다.
        float cardH = Mathf.Clamp(_cardPreviewHeight, 300f, 560f);
        float cardW = cardH * (3f / 4f);
        // 세로 중심을 약간 위로 — 하단 버튼 자리 확보.
        var cardRect = new Rect(640f - cardW * 0.5f, 340f - cardH * 0.5f, cardW, cardH);

        var ui = Object.FindFirstObjectByType<BattleUI>();
        if (ui == null)
        {
            GUI.Label(cardRect, "BattleUI not in scene.\n전투 한 번 들어갔다 나오면 OK", _previewLabelStyle);
        }
        else if (_cardPreviewSlotOnly)
        {
            // 빈 슬롯 프리뷰 — 카드 데이터 없음. BattleUI의 Inspector에서 rect/tint 조정하며 실시간 확인.
            ui.DrawCardPreview(cardRect, null, _cardPreviewRarity, slotOnly: true);

            // 정보 라벨은 화면 최상단 고정 — 카드가 커져도 안 가려짐.
            GUI.Label(new Rect(0, 8f, 1280f, 24f), "[ 카드 슬롯 프리뷰 — 빈 프레임 ]", _previewLabelStyle);
            GUI.Label(new Rect(0, 32f, 1280f, 20f),
                "BattleUI Inspector · Card Layers v2 / cardBgTint / cardBaseTint 실시간 반영", _previewLabelStyle);
        }
        else
        {
            var card = _cardPreviewList[_cardPreviewIndex];
            ui.DrawCardPreview(cardRect, card, _cardPreviewRarity);

            GUI.Label(new Rect(0, 8f, 1280f, 24f), $"{card.id}  {card.nameKr}", _previewLabelStyle);
            GUI.Label(new Rect(0, 32f, 1280f, 20f),
                $"{card.cardType} / {card.subType}  · 원본 {card.rarity} · 표시 {_cardPreviewRarity}",
                _previewLabelStyle);
        }

        // 확대 슬라이더 (상단 우측) — 슬롯/카드 모드 공통.
        GUI.Label(new Rect(1000f, 8f, 240f, 20f), $"확대: {_cardPreviewHeight:0}px", _previewLabelStyle);
        _cardPreviewHeight = GUI.HorizontalSlider(new Rect(1000f, 32f, 240f, 24f), _cardPreviewHeight, 300f, 560f);

        // 컨트롤 — 캔버스 하단 고정 바. 카드 크기와 무관하게 항상 같은 자리.
        const float bottomRow = 620f;
        const float bottomRow2 = 668f;

        if (!_cardPreviewSlotOnly)
        {
            // 카드 모드: 이전/다음 버튼은 카드 옆에.
            var prevBtn = new Rect(cardRect.x - 80f, cardRect.center.y - 30f, 70f, 60f);
            var nextBtn = new Rect(cardRect.xMax + 10f, cardRect.center.y - 30f, 70f, 60f);
            if (GUI.Button(prevBtn, "◀\nPrev", _btnStyle))
                _cardPreviewIndex = (_cardPreviewIndex - 1 + _cardPreviewList.Count) % _cardPreviewList.Count;
            if (GUI.Button(nextBtn, "Next\n▶", _btnStyle))
                _cardPreviewIndex = (_cardPreviewIndex + 1) % _cardPreviewList.Count;

            // 등급 토글 — 하단 좌측.
            if (GUI.Button(new Rect(320f, bottomRow, 100f, 36f), "COMMON",   _btnStyle)) _cardPreviewRarity = Rarity.COMMON;
            if (GUI.Button(new Rect(430f, bottomRow, 100f, 36f), "UNCOMMON", _btnStyle)) _cardPreviewRarity = Rarity.UNCOMMON;
            if (GUI.Button(new Rect(540f, bottomRow, 100f, 36f), "RARE",     _btnStyle)) _cardPreviewRarity = Rarity.RARE;
        }

        // 슬롯/카드 모드 토글 — 하단 우측.
        string modeLabel = _cardPreviewSlotOnly ? "▶ 카드 모드로" : "▶ 슬롯 모드로";
        if (GUI.Button(new Rect(680f, bottomRow, 180f, 36f), modeLabel, _btnStyle))
            _cardPreviewSlotOnly = !_cardPreviewSlotOnly;

        // 닫기 — 가장 하단 중앙.
        if (GUI.Button(new Rect(580f, bottomRow2, 120f, 36f), "닫기", _btnStyle))
            _cardPreviewOpen = false;
    }

    /// <summary>현재 씬에 떠있는 BattleUI에서 BattleManager 인스턴스를 획득.</summary>
    private DianoCard.Battle.BattleManager GetActiveBattle()
    {
        var ui = Object.FindFirstObjectByType<BattleUI>();
        return ui != null ? ui.Battle : null;
    }
}
