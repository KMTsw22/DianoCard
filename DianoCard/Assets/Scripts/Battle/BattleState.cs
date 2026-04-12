using System.Collections.Generic;

namespace DianoCard.Battle
{
    /// <summary>
    /// 전투 한 판의 모든 런타임 상태 집합.
    /// BattleManager는 이 state를 변경함.
    /// </summary>
    public class BattleState
    {
        public Player player;
        public List<EnemyInstance> enemies = new();
        public List<SummonInstance> field = new();
        public List<CardInstance> deck = new();
        public List<CardInstance> hand = new();
        public List<CardInstance> discard = new();
        public int turn;

        public bool AllEnemiesDead
        {
            get
            {
                foreach (var e in enemies) if (!e.IsDead) return false;
                return true;
            }
        }

        public bool PlayerWon => AllEnemiesDead;
        public bool PlayerLost => player != null && player.IsDead;
        public bool IsOver => PlayerWon || PlayerLost;
    }
}
