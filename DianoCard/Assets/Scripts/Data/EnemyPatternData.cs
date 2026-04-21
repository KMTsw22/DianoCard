using System.Collections.Generic;

namespace DianoCard.Data
{
    /// <summary>
    /// enemy_pattern.csv 한 행 = 적 패턴 한 스텝.
    /// 같은 patternSetId의 행들이 모여 패턴 셋을 이룸 (DataManager가 그룹화).
    /// </summary>
    [System.Serializable]
    public class EnemyPatternData
    {
        public string patternSetId;
        public int stepOrder;
        public int phase;
        public EnemyAction action;
        public int value;
        public int count;          // MULTI_ATTACK 분할 횟수, SUMMON 마릿수 등
        public IntentTarget target;
        public int telegraphTurns; // 0=즉발, 1=1턴 예고, 2=2턴 예고...
        public string intentIcon;
        public int weight;         // step_order=0(랜덤) 시 가중치 — MVP에선 미사용
        public string condition;   // ADD_DEAD, ADD_ALIVE, ON_PARTNER_DEATH 등
        public string description;

        public static EnemyPatternData FromRow(Dictionary<string, string> row)
        {
            return new EnemyPatternData
            {
                patternSetId   = CSVUtil.GetString(row, "pattern_set_id"),
                stepOrder      = CSVUtil.GetInt(row, "step_order"),
                phase          = CSVUtil.GetInt(row, "phase", 1),
                action         = CSVUtil.GetEnum(row, "action", EnemyAction.UNKNOWN),
                value          = CSVUtil.GetInt(row, "value"),
                count          = CSVUtil.GetInt(row, "count", 1),
                target         = CSVUtil.GetEnum(row, "target", IntentTarget.SUMMONER),
                telegraphTurns = CSVUtil.GetInt(row, "telegraph_turns", 1),
                intentIcon     = CSVUtil.GetString(row, "intent_icon", "UNKNOWN"),
                weight         = CSVUtil.GetInt(row, "weight"),
                condition      = CSVUtil.GetString(row, "condition"),
                description    = CSVUtil.GetString(row, "description"),
            };
        }
    }
}
