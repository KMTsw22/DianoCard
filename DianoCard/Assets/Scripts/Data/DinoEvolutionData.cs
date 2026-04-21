using System.Collections.Generic;

namespace DianoCard.Data
{
    /// <summary>
    /// dino_evolution.csv 한 행 — 현재 카드에서 다음 진화체로 가는 조건.
    /// base_card_id는 현재 필드 공룡의 카드 id. 같은 id의 SUMMON 카드가 다시 플레이되면 스택이 누적되고
    /// stacks_required 도달 시 result_card_id 로 변형됨(초식=덮어쓰기, 육식=합성).
    /// 예: C001 → C001_T1 (1 스택), C001_T1 → C001_T2 (2 스택, 누적)
    /// </summary>
    [System.Serializable]
    public class DinoEvolutionData
    {
        public string baseCardId;    // 현재 카드 id (이 카드를 진화시키면...)
        public string resultCardId;  // ...이 카드로 바뀐다
        public int stacksRequired;   // 누적 스택 임계치 (base 기준 절대값, 진화 후에도 스택은 유지됨)

        public static DinoEvolutionData FromRow(Dictionary<string, string> row)
        {
            return new DinoEvolutionData
            {
                baseCardId = CSVUtil.GetString(row, "base_card_id"),
                resultCardId = CSVUtil.GetString(row, "result_card_id"),
                stacksRequired = CSVUtil.GetInt(row, "stacks_required"),
            };
        }
    }
}
