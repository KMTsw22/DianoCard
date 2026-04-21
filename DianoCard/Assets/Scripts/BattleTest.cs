using System.Collections.Generic;
using DianoCard.Battle;
using DianoCard.Data;
using UnityEngine;

/// <summary>
/// 헤드리스 전투 프로토타입 테스트.
/// 빈 GameObject에 붙이고 Play 누르면 Console에 한 판 전체 진행 로그가 찍힘.
///
/// - autoPlay ON: 마나가 닿는 카드를 자동으로 사용하고 턴 종료까지 알아서 진행
/// - autoPlay OFF: BattleStart만 하고 수동으로 호출 (추후 UI 연결용)
/// </summary>
public class BattleTest : MonoBehaviour
{
    [Header("Enemy to fight")]
    public string enemyId = "E001"; // Goblin Hunter

    [Header("Battle rules")]
    public int playerHp = 70;
    public int maxMana = 3;

    [Header("Auto play")]
    public bool autoPlay = true;
    public int maxTurnsSafeguard = 30;

    private BattleManager _battle;

    void Start()
    {
        DataManager.Instance.Load();

        var deck = BuildStarterDeck();
        var enemies = new List<EnemyData>();
        var enemy = DataManager.Instance.GetEnemy(enemyId);
        if (enemy == null)
        {
            Debug.LogError($"[BattleTest] Enemy not found: {enemyId}");
            return;
        }
        enemies.Add(enemy);

        _battle = new BattleManager();
        _battle.StartBattle(deck, enemies, maxMana, playerHp);

        if (autoPlay) RunAutoPlay();
    }

    /// <summary>
    /// MVP 스타터 덱: 초식 공룡 위주 20장.
    /// 육식 공룡은 제물 필요해서 auto-play 테스트에 복잡하므로 제외.
    /// </summary>
    private List<CardData> BuildStarterDeck()
    {
        var deck = new List<CardData>();
        void Add(string id, int count)
        {
            var c = DataManager.Instance.GetCard(id);
            if (c == null) { Debug.LogError($"[BattleTest] Card missing: {id}"); return; }
            for (int i = 0; i < count; i++) deck.Add(c);
        }

        Add("C001", 3); // 트리케라톱스 (4/20)
        Add("C002", 2); // 스테고사우루스 (5/17)
        Add("C101", 6); // 공격 마법 (데미지 5)
        Add("C102", 5); // 방어 마법 (방어 5)
        Add("C201", 2); // 공격 강화 버프
        Add("C202", 2); // 전체 힐 버프

        return deck;
    }

    private void RunAutoPlay()
    {
        int safety = 0;
        while (!_battle.state.IsOver)
        {
            if (++safety > maxTurnsSafeguard * 20)
            {
                Debug.LogWarning("[BattleTest] Safety stop — loop too long.");
                break;
            }

            // 1) 마나 남아 있으면 사용 가능한 카드 탐색
            int playIndex = FindPlayableCardIndex();
            if (playIndex >= 0)
            {
                _battle.PlayCard(playIndex);
                continue;
            }

            // 2) 더 사용할 카드 없으면 턴 종료
            _battle.EndTurn();
        }
    }

    private int FindPlayableCardIndex()
    {
        var s = _battle.state;
        for (int i = 0; i < s.hand.Count; i++)
        {
            var c = s.hand[i].data;
            if (s.player.mana < c.cost) continue;

            // 필드 꽉 차면 소환 카드 건너뜀
            if (c.cardType == CardType.SUMMON && s.field.Count >= s.maxFieldSize)
                continue;

            return i;
        }
        return -1;
    }
}
