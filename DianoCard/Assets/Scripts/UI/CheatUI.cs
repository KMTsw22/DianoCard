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
    private GUIStyle _btnStyle;
    private GUIStyle _titleStyle;
    private GUIStyle _stateStyle;

    // 배경 리스트 캐시 — 전투 진입 시 1회만 Resources.LoadAll 수행
    private string[] _bgNames;
    private Vector2 _bgScroll;

    // 카드 프리뷰 (프레임 디자인 확인용)
    private bool _cardPreviewOpen;
    private int _cardPreviewIndex;
    private Rarity _cardPreviewRarity = Rarity.COMMON;
    private List<CardData> _cardPreviewList;
    private GUIStyle _previewLabelStyle;

    void Update()
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.backquoteKey.wasPressedThisFrame)
        {
            _open = !_open;
            _bgNames = null; // 다음 열 때 배경 목록 재로드
        }
    }

    void OnGUI()
    {
        if (!_open) return;

        var matrix = GUI.matrix;
        float scale = Screen.width / 1280f;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

        // 프리뷰는 BattleUI보다 위 (depth 낮을수록 앞).
        if (_cardPreviewOpen)
        {
            GUI.depth = -100;
            DrawCardPreviewOverlay();
        }

        _windowRect = GUI.Window(9999, _windowRect, DrawWindow, "");
        GUI.matrix = matrix;
    }

    private void DrawWindow(int id)
    {
        if (_btnStyle == null)
        {
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

        GUI.DragWindow();
    }

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

        // 큰 카드 한 장 (3:4 비율).
        const float cardW = 360f;
        const float cardH = 504f;
        var cardRect = new Rect(640f - cardW * 0.5f, 360f - cardH * 0.5f, cardW, cardH);

        var ui = Object.FindFirstObjectByType<BattleUI>();
        if (ui == null)
        {
            GUI.Label(cardRect, "BattleUI not in scene.\n전투 한 번 들어갔다 나오면 OK", _previewLabelStyle);
        }
        else
        {
            var card = _cardPreviewList[_cardPreviewIndex];
            ui.DrawCardPreview(cardRect, card, _cardPreviewRarity);

            // 카드 정보 라벨 (카드 위쪽).
            var infoRect = new Rect(cardRect.x, cardRect.y - 56f, cardRect.width, 24f);
            GUI.Label(infoRect, $"{card.id}  {card.nameKr}", _previewLabelStyle);
            var subRect = new Rect(cardRect.x, cardRect.y - 32f, cardRect.width, 24f);
            GUI.Label(subRect, $"{card.cardType} / {card.subType}  · 원본 {card.rarity} · 표시 {_cardPreviewRarity}", _previewLabelStyle);
        }

        // 컨트롤 패널 (카드 아래).
        float btnY = cardRect.yMax + 24f;
        var prevBtn = new Rect(cardRect.x - 80f, cardRect.center.y - 30f, 70f, 60f);
        var nextBtn = new Rect(cardRect.xMax + 10f, cardRect.center.y - 30f, 70f, 60f);
        if (GUI.Button(prevBtn, "◀\nPrev", _btnStyle))
        {
            _cardPreviewIndex = (_cardPreviewIndex - 1 + _cardPreviewList.Count) % _cardPreviewList.Count;
        }
        if (GUI.Button(nextBtn, "Next\n▶", _btnStyle))
        {
            _cardPreviewIndex = (_cardPreviewIndex + 1) % _cardPreviewList.Count;
        }

        // 등급 토글.
        float rx = cardRect.x;
        if (GUI.Button(new Rect(rx,        btnY, 100f, 36f), "COMMON",   _btnStyle)) _cardPreviewRarity = Rarity.COMMON;
        if (GUI.Button(new Rect(rx + 110f, btnY, 100f, 36f), "UNCOMMON", _btnStyle)) _cardPreviewRarity = Rarity.UNCOMMON;
        if (GUI.Button(new Rect(rx + 220f, btnY, 100f, 36f), "RARE",     _btnStyle)) _cardPreviewRarity = Rarity.RARE;

        // 닫기.
        if (GUI.Button(new Rect(640f - 60f, btnY + 48f, 120f, 36f), "닫기", _btnStyle))
        {
            _cardPreviewOpen = false;
        }
    }

    /// <summary>현재 씬에 떠있는 BattleUI에서 BattleManager 인스턴스를 획득.</summary>
    private DianoCard.Battle.BattleManager GetActiveBattle()
    {
        var ui = Object.FindFirstObjectByType<BattleUI>();
        return ui != null ? ui.Battle : null;
    }
}
