using System;
using System.Collections.Generic;
using DianoCard.Data;
using DianoCard.Game;
using UnityEngine;

/// <summary>
/// 전투 승리 후 보상 선택 화면.
/// GameState == Reward 일 때만 그려짐.
///
/// 화면 구성:
/// - 상단: 클리어 층 + 획득 골드
/// - 중앙: 카드 3장 중 택1 (또는 스킵)
/// - 하단: 물약(옵션) + Continue 버튼
/// - 유물(엘리트/보스)은 Continue 시 자동 획득
/// </summary>
public class RewardUI : MonoBehaviour
{
    private const float RefW = 1280f;
    private const float RefH = 720f;

    private bool _cardTaken;
    private bool _potionTaken;

    // 이전 상태가 뭐였는지 추적해서 Reward 상태 진입 시 선택 플래그 리셋
    private GameState _prevState = GameState.Lobby;

    private readonly List<Action> _pending = new();

    private GUIStyle _titleStyle;
    private GUIStyle _labelStyle;
    private GUIStyle _cardStyle;
    private GUIStyle _buttonStyle;
    private GUIStyle _smallLabelStyle;
    private bool _stylesReady;

    void Update()
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null) return;

        // Reward 상태로 새로 들어왔을 때 선택 플래그 리셋
        if (_prevState != GameState.Reward && gsm.State == GameState.Reward)
        {
            _cardTaken = false;
            _potionTaken = false;
        }
        _prevState = gsm.State;

        // 지연 실행 액션
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
        if (gsm == null || gsm.State != GameState.Reward) return;

        var run = gsm.CurrentRun;
        var reward = run?.pendingReward;
        if (reward == null) return;

        EnsureStyles();

        float scale = Mathf.Min(Screen.width / RefW, Screen.height / RefH);
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));

        // 반투명 검정 배경 (전투 화면 위에 오버레이)
        var prev = GUI.color;
        GUI.color = new Color(0.05f, 0.03f, 0.1f, 0.92f);
        GUI.DrawTexture(new Rect(0, 0, RefW, RefH), Texture2D.whiteTexture);
        GUI.color = prev;

        DrawHeader(run, reward);
        DrawCardChoices(reward);
        DrawPotion(reward, run);
        DrawRelic(reward);
        DrawContinue(gsm, reward);
    }

    // ---------------------------------------------------------

    private void DrawHeader(RunState run, BattleReward reward)
    {
        string title = $"FLOOR {run.currentFloor} CLEARED";
        GUI.Label(new Rect(0, 40, RefW, 60), title, _titleStyle);

        string sub = $"+{reward.gold} Gold    Total Gold: {run.gold}    " +
                     $"HP: {run.playerCurrentHp}/{run.playerMaxHp}    Deck: {run.deck.Count}";
        GUI.Label(new Rect(0, 105, RefW, 26), sub, _labelStyle);
    }

    private void DrawCardChoices(BattleReward reward)
    {
        GUI.Label(new Rect(0, 150, RefW, 24), "카드 선택 (1장 또는 스킵)", _labelStyle);

        int n = reward.cardChoices.Count;
        if (n == 0) return;

        const float cardW = 180;
        const float cardH = 230;
        const float spacing = 30;
        float totalW = n * cardW + (n - 1) * spacing;
        float startX = (RefW - totalW) / 2f;

        GUI.enabled = !_cardTaken;
        for (int i = 0; i < n; i++)
        {
            var card = reward.cardChoices[i];
            var rect = new Rect(startX + i * (cardW + spacing), 185, cardW, cardH);
            if (GUI.Button(rect, BuildCardLabel(card), _cardStyle))
            {
                int captured = i;
                _pending.Add(() =>
                {
                    var gsm = GameStateManager.Instance;
                    gsm?.TakeCardReward(reward.cardChoices[captured]);
                    _cardTaken = true;
                });
            }
        }
        GUI.enabled = true;

        // 스킵 버튼
        float skipW = 160, skipH = 36;
        string skipLabel = _cardTaken ? "✓ Card Taken" : "Skip Card";
        GUI.enabled = !_cardTaken;
        if (GUI.Button(new Rect((RefW - skipW) / 2f, 440, skipW, skipH), skipLabel, _buttonStyle))
        {
            _pending.Add(() => _cardTaken = true);
        }
        GUI.enabled = true;
    }

    private void DrawPotion(BattleReward reward, RunState run)
    {
        if (reward.potion == null) return;

        const float w = 360, h = 44;
        var rect = new Rect((RefW - w) / 2f, 495, w, h);

        if (_potionTaken)
        {
            GUI.Label(rect, $"✓ {reward.potion.nameKr} 획득됨", _labelStyle);
            return;
        }

        bool canTake = !run.PotionSlotFull;
        string label = canTake
            ? $"+ 물약 획득: {reward.potion.nameKr}"
            : $"물약 슬롯 가득 참 ({reward.potion.nameKr} 포기)";

        GUI.enabled = canTake;
        if (GUI.Button(rect, label, _buttonStyle))
        {
            _pending.Add(() =>
            {
                GameStateManager.Instance?.TakePotionReward(reward.potion);
                _potionTaken = true;
            });
        }
        GUI.enabled = true;
    }

    private void DrawRelic(BattleReward reward)
    {
        if (reward.relic == null) return;

        string text = $"★ 유물 획득: {reward.relic.nameKr}  —  {reward.relic.description}";
        GUI.Label(new Rect(20, 560, RefW - 40, 28), text, _smallLabelStyle);
    }

    private void DrawContinue(GameStateManager gsm, BattleReward reward)
    {
        const float w = 260, h = 64;
        if (GUI.Button(new Rect((RefW - w) / 2f, RefH - 100, w, h), "CONTINUE →", _buttonStyle))
        {
            _pending.Add(() =>
            {
                // 유물은 continue 시 자동 획득
                if (reward.relic != null) gsm.TakeRelicReward(reward.relic);
                gsm.ProceedAfterReward();
            });
        }
    }

    // ---------------------------------------------------------

    private string BuildCardLabel(CardData c)
    {
        string body;
        switch (c.cardType)
        {
            case CardType.SUMMON:
                string tag = c.subType == CardSubType.CARNIVORE ? "육식 (제물)" : "초식";
                body = $"{tag}\nATK: {c.attack}\nHP: {c.hp}";
                break;
            case CardType.MAGIC:
                body = c.subType == CardSubType.ATTACK
                    ? $"마법 (공격)\nDMG: {c.value}"
                    : $"마법 (방어)\nBlock: {c.value}";
                break;
            case CardType.BUFF:
                body = $"버프\n{Short(c.description)}";
                break;
            case CardType.UTILITY:
                body = $"유틸\n{Short(c.description)}";
                break;
            default:
                body = Short(c.description);
                break;
        }
        return $"{c.nameKr}\n[{c.rarity}]  Cost: {c.cost}\n\n{body}";
    }

    private string Short(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length > 32 ? s.Substring(0, 32) + "..." : s;
    }

    private void EnsureStyles()
    {
        if (_stylesReady) return;
        _titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 44,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.9f, 0.5f) },
        };
        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white },
        };
        _smallLabelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.95f, 0.8f, 0.4f) },
        };
        _cardStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 13,
            alignment = TextAnchor.UpperLeft,
            padding = new RectOffset(12, 12, 12, 12),
            wordWrap = true,
            fontStyle = FontStyle.Bold,
        };
        _buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 18,
            fontStyle = FontStyle.Bold,
        };
        _stylesReady = true;
    }
}
