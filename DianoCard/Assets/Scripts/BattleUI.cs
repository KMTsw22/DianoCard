using System;
using System.Collections.Generic;
using DianoCard.Battle;
using DianoCard.Data;
using UnityEngine;

/// <summary>
/// BattleManager를 조작하는 즉석 IMGUI 프로토타입 UI.
/// 빈 GameObject에 붙이고 Play 누르면 Game 뷰에 즉시 전투 화면이 뜸.
///
/// 주의:
/// - OnGUI는 정식 UI 프레임워크가 아님 → 프로토타입 전용.
/// - 추후 uGUI / UI Toolkit으로 갈아끼울 때 BattleManager는 그대로 재사용 가능.
/// </summary>
public class BattleUI : MonoBehaviour
{
    [Header("Battle Settings")]
    public string enemyId = "E001";
    public int playerHp = 70;
    public int maxMana = 3;

    // 가상 해상도 — 실제 화면 크기에 맞춰 스케일링됨
    private const float RefW = 1280f;
    private const float RefH = 720f;

    private BattleManager _battle;
    // OnGUI에서 state를 즉시 변경하면 Layout/Repaint 이벤트 간 불일치로
    // ArgumentException이 뜨므로, 버튼 클릭 시에는 액션을 지연시켜 Update에서 실행.
    private readonly List<Action> _pending = new();

    private GUIStyle _boxStyle;
    private GUIStyle _buttonStyle;
    private GUIStyle _labelStyle;
    private GUIStyle _bigStyle;
    private bool _stylesReady;

    void Start()
    {
        DataManager.Instance.Load();
        StartNewBattle();
    }

    void Update()
    {
        if (_pending.Count == 0) return;
        var snapshot = new List<Action>(_pending);
        _pending.Clear();
        foreach (var a in snapshot) a?.Invoke();
    }

    private void StartNewBattle()
    {
        var enemy = DataManager.Instance.GetEnemy(enemyId);
        if (enemy == null)
        {
            Debug.LogError($"[BattleUI] Enemy not found: {enemyId}");
            return;
        }

        _battle = new BattleManager();
        _battle.StartBattle(BuildStarterDeck(), new List<EnemyData> { enemy }, maxMana, playerHp);
    }

    private List<CardData> BuildStarterDeck()
    {
        var deck = new List<CardData>();
        void Add(string id, int count)
        {
            var c = DataManager.Instance.GetCard(id);
            if (c == null) { Debug.LogError($"[BattleUI] Missing card: {id}"); return; }
            for (int i = 0; i < count; i++) deck.Add(c);
        }
        Add("C001", 3); // 트리케라톱스
        Add("C002", 2); // 스테고사우루스
        Add("C101", 6); // 공격 마법
        Add("C102", 5); // 방어 마법
        Add("C201", 2); // 공격 강화
        Add("C202", 2); // 전체 힐
        return deck;
    }

    // =========================================================
    // OnGUI
    // =========================================================

    void OnGUI()
    {
        if (_battle == null || _battle.state == null) return;

        EnsureStyles();

        // 해상도 스케일링: 1280x720 기준으로 그린 뒤 현재 화면에 맞춰 확대/축소
        float scale = Mathf.Min(Screen.width / RefW, Screen.height / RefH);
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));

        var state = _battle.state;

        DrawPlayer(state);
        DrawEnemies(state);
        DrawField(state);
        DrawHand(state);
        DrawEndTurn(state);
        DrawGameOver(state);
    }

    private void EnsureStyles()
    {
        if (_stylesReady) return;
        _boxStyle = new GUIStyle(GUI.skin.box)
        {
            fontSize = 14,
            alignment = TextAnchor.UpperLeft,
            padding = new RectOffset(10, 10, 8, 8),
            wordWrap = true,
        };
        _buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 13,
            alignment = TextAnchor.UpperLeft,
            padding = new RectOffset(8, 8, 8, 8),
            wordWrap = true,
        };
        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
        };
        _bigStyle = new GUIStyle(GUI.skin.box)
        {
            fontSize = 36,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
        };
        _stylesReady = true;
    }

    // =========================================================
    // 섹션별 그리기
    // =========================================================

    private void DrawPlayer(BattleState state)
    {
        var p = state.player;
        string text =
            $"[Player · 고고학자]\n" +
            $"HP: {p.hp} / {p.maxHp}\n" +
            $"Block: {p.block}    Mana: {p.mana} / {p.maxMana}\n" +
            $"Turn: {state.turn}";
        GUI.Box(new Rect(10, 10, 260, 100), text, _boxStyle);
    }

    private void DrawEnemies(BattleState state)
    {
        float x = 290;
        foreach (var e in state.enemies)
        {
            if (e.IsDead) continue;
            string blockLine = e.block > 0 ? $"\nBlock: {e.block}" : "";
            string text =
                $"[{e.data.nameKr}]\n" +
                $"HP: {e.hp} / {e.data.hp}{blockLine}\n" +
                $"Intent: {e.IntentLabel}";
            GUI.Box(new Rect(x, 10, 200, 100), text, _boxStyle);
            x += 210;
            if (x > RefW - 200) break;
        }
    }

    private void DrawField(BattleState state)
    {
        GUI.Label(new Rect(10, 125, 300, 22), "[Field · 소환수]", _labelStyle);

        if (state.field.Count == 0)
        {
            GUI.Label(new Rect(10, 150, 300, 22), "(없음)");
            return;
        }

        float x = 10;
        foreach (var s in state.field)
        {
            string atkLine = s.tempAttackBonus > 0
                ? $"ATK: {s.TotalAttack} (+{s.tempAttackBonus})"
                : $"ATK: {s.TotalAttack}";
            string text =
                $"{s.data.nameKr}\n" +
                $"{atkLine}\n" +
                $"HP: {s.hp} / {s.data.hp}";
            GUI.Box(new Rect(x, 150, 150, 95), text, _boxStyle);
            x += 160;
            if (x > RefW - 160) break;
        }
    }

    private void DrawHand(BattleState state)
    {
        float handY = RefH - 210;

        GUI.Label(
            new Rect(10, handY - 24, 600, 22),
            $"[Hand]  Deck: {state.deck.Count}   Discard: {state.discard.Count}",
            _labelStyle);

        float x = 10;
        for (int i = 0; i < state.hand.Count; i++)
        {
            var c = state.hand[i].data;
            bool canPlay = !state.IsOver && state.player.mana >= c.cost;
            if (c.cardType == CardType.SUMMON
                && c.subType == CardSubType.CARNIVORE
                && state.field.Count == 0)
            {
                canPlay = false;
            }

            string label = BuildCardLabel(c);

            GUI.enabled = canPlay;
            if (GUI.Button(new Rect(x, handY, 150, 190), label, _buttonStyle))
            {
                int captured = i;
                _pending.Add(() => _battle.PlayCard(captured));
            }
            GUI.enabled = true;

            x += 160;
            if (x > RefW - 180) break;
        }
    }

    private string BuildCardLabel(CardData c)
    {
        string body;
        switch (c.cardType)
        {
            case CardType.SUMMON:
                string tag = c.subType == CardSubType.CARNIVORE ? "육식 (제물1)" : "초식";
                body = $"{tag}\nATK: {c.attack}\nHP: {c.hp}";
                break;
            case CardType.MAGIC:
                body = c.subType == CardSubType.ATTACK
                    ? $"마법 (공격)\nDMG: {c.value}"
                    : $"마법 (방어)\nBlock: {c.value}";
                break;
            case CardType.BUFF:
                body = $"버프\n{ShortDesc(c)}";
                break;
            case CardType.UTILITY:
                body = $"유틸\n{ShortDesc(c)}";
                break;
            default:
                body = ShortDesc(c);
                break;
        }

        return $"{c.nameKr}\nCost: {c.cost}\n\n{body}";
    }

    private string ShortDesc(CardData c)
    {
        if (string.IsNullOrEmpty(c.description)) return "";
        return c.description.Length > 30
            ? c.description.Substring(0, 30) + "..."
            : c.description;
    }

    private void DrawEndTurn(BattleState state)
    {
        GUI.enabled = !state.IsOver;
        if (GUI.Button(new Rect(RefW - 170, 10, 160, 100), "END\nTURN", _buttonStyle))
        {
            _pending.Add(() => _battle.EndTurn());
        }
        GUI.enabled = true;
    }

    private void DrawGameOver(BattleState state)
    {
        if (!state.IsOver) return;

        string result = state.PlayerWon ? "VICTORY" : "DEFEAT";
        GUI.Box(new Rect(RefW / 2 - 180, RefH / 2 - 90, 360, 110), result, _bigStyle);

        if (GUI.Button(new Rect(RefW / 2 - 100, RefH / 2 + 40, 200, 55), "Restart", _buttonStyle))
        {
            _pending.Add(StartNewBattle);
        }
    }
}
