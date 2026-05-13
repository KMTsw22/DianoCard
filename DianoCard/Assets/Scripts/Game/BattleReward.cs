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
        // R012 태초의 알이 있을 때 채워지는 "다음 라운드 카드 보상" 큐.
        // 카드 한 번 고르거나 스킵하면 RewardUI가 여기서 다음 set을 cardChoices로 끌어와 picker를 다시 띄운다.
        public List<List<CardData>> extraCardChoiceSets = new();
        // 카드 보상 총 라운드 수 (1 + 추가 picks). UI에 "1/2" 같은 카운터를 그릴 때 분모로 사용.
        // 0이면 카드 보상 자체가 없음 — 행/피커 모두 안 그림.
        public int totalCardPickRounds;
        public PotionData potion;                  // null이면 물약 드랍 없음
        public RelicData relic;                    // null이면 유물 없음 (엘리트/보스만 생성)
        public bool cardRemoveOffer;               // true면 카드 1장 무료 제거 기회 (일반 전투 확률 지급)
    }
}
