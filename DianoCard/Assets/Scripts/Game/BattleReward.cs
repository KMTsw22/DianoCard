using System.Collections.Generic;
using DianoCard.Data;

namespace DianoCard.Game
{
    /// <summary>
    /// 한 전투 승리 시 생성되는 보상 번들.
    /// RewardUI가 이걸 읽어서 화면에 뿌리고, 플레이어 선택에 따라 일부만 RunState에 반영.
    /// </summary>
    public class BattleReward
    {
        public int gold;                          // 골드는 항상 자동 획득
        public List<CardData> cardChoices = new(); // 3장 중 택1 (스킵 가능)
        public PotionData potion;                  // null이면 물약 드랍 없음
        public RelicData relic;                    // null이면 유물 없음 (엘리트/보스만 생성)
    }
}
