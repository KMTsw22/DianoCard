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
        // 필드에 소환된 공룡과 1:1 바인딩된 카드 — 덱 순환에서 빠져있다가 공룡이 죽거나 제거되면 discard로 복귀.
        public List<CardInstance> bound = new();
        public int turn;
        // 플레이어 필드(소환수) 동시 수용 가능 수. 기본 2 — 테크트리/유물로 증가 가능.
        public int maxFieldSize = 2;

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
