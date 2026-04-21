using System.Collections.Generic;

namespace DianoCard.Data
{
    [System.Serializable]
    public class EnemyData
    {
        public string id;
        public string nameKr;
        public string nameEn;
        public EnemyType enemyType;
        public int chapter;
        public int hp;
        public int attack;
        public int defense;
        public string patternSetId;   // enemy_pattern.csv 참조 (필수). 빈 값이면 폴백.
        public string phaseSetId;     // enemy_phase.csv 참조 (보스만, 없으면 빈 값).
        public int goldMin;
        public int goldMax;
        public string description;
        public string image;
        public List<string> passiveIds = new(); // enemy_passive.csv 참조. 여러 개면 "|" 구분.

        public static EnemyData FromRow(Dictionary<string, string> row)
        {
            var d = new EnemyData
            {
                id = CSVUtil.GetString(row, "id"),
                nameKr = CSVUtil.GetString(row, "name_kr"),
                nameEn = CSVUtil.GetString(row, "name_en"),
                enemyType = CSVUtil.GetEnum(row, "enemy_type", EnemyType.NORMAL),
                chapter = CSVUtil.GetInt(row, "chapter"),
                hp = CSVUtil.GetInt(row, "hp"),
                attack = CSVUtil.GetInt(row, "attack"),
                defense = CSVUtil.GetInt(row, "defense"),
                patternSetId = CSVUtil.GetString(row, "pattern_set_id"),
                phaseSetId = CSVUtil.GetString(row, "phase_set_id"),
                goldMin = CSVUtil.GetInt(row, "gold_min"),
                goldMax = CSVUtil.GetInt(row, "gold_max"),
                description = CSVUtil.GetString(row, "description"),
                image = CSVUtil.GetString(row, "image"),
            };

            // passive_ids 는 "|" 구분 문자열 (콤마가 리스트 구분자로 이미 사용 중이라 분리)
            var raw = CSVUtil.GetString(row, "passive_ids");
            if (!string.IsNullOrEmpty(raw))
            {
                foreach (var p in raw.Split('|'))
                {
                    var pid = p.Trim();
                    if (!string.IsNullOrEmpty(pid)) d.passiveIds.Add(pid);
                }
            }
            return d;
        }
    }
}
