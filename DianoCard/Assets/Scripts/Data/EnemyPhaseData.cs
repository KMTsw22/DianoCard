using System.Collections.Generic;

namespace DianoCard.Data
{
    /// <summary>
    /// enemy_phase.csv 한 행 = 보스 페이즈 한 단계.
    /// on_enter_actions는 파이프 구분 문자열 ("LOCK_SLOT:1:4|GRACE_TURN:1|SUMMON:1").
    /// </summary>
    [System.Serializable]
    public class EnemyPhaseData
    {
        public string phaseSetId;
        public int phase;
        public float enterHpRatio;
        public string patternSetId;
        public string onEnterActions; // 파이프 구분, 런타임에서 파싱
        public string triggerText;

        public static EnemyPhaseData FromRow(Dictionary<string, string> row)
        {
            return new EnemyPhaseData
            {
                phaseSetId      = CSVUtil.GetString(row, "phase_set_id"),
                phase           = CSVUtil.GetInt(row, "phase", 1),
                enterHpRatio    = CSVUtil.GetFloat(row, "enter_hp_ratio", 1f),
                patternSetId    = CSVUtil.GetString(row, "pattern_set_id"),
                onEnterActions  = CSVUtil.GetString(row, "on_enter_actions"),
                triggerText     = CSVUtil.GetString(row, "trigger_text"),
            };
        }
    }
}
