using System.Collections.Generic;
using DianoCard.Data;

namespace DianoCard.Game
{
    /// <summary>
    /// 한 run(전체 게임 한 판) 동안 유지되는 플레이어 상태.
    /// Lobby에서 NEW RUN 시 생성, Defeat/Victory 시 Lobby로 돌아가면서 폐기.
    /// </summary>
    public class RunState
    {
        public int playerMaxHp = 70;
        public int playerCurrentHp = 70;
        public int gold = 0;

        public List<CardData> deck = new();
        public List<RelicData> relics = new();
        public List<PotionData> potions = new();
        public const int MaxPotionSlots = 3;

        public int currentFloor = 1;
        public string chapterId = "CH01";
        public string characterId = "CH001";  // 선택된 캐릭터 id — 시작 덱/보상 풀 분기에 사용

        // 직전 전투 클리어 시 생성된 보상 (RewardUI가 읽음)
        public BattleReward pendingReward;

        public bool PotionSlotFull => potions.Count >= MaxPotionSlots;
    }
}
