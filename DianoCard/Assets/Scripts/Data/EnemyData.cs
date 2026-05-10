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
        public string patternSetId;
        public string phaseSetId;
        public int goldMin;
        public int goldMax;
        public string descriptionKr;
        public string descriptionEn;
        public string image;
        public List<string> passiveIds = new();
        public float fieldScale;
        public bool isDummy;
        // HP 바 X 앵커 (0~1, default 0.5). 스프라이트 캔버스는 중앙이지만 시각 무게중심이 좌/우로 쏠린 몬스터용.
        // 예: 0.55 → 슬롯 너비의 55% 지점에 HP 바 중심 배치.
        public float hpBarAnchorX;

        public string name => LocaleSettings.Pick(nameKr, nameEn);
        public string description => LocaleSettings.Pick(descriptionKr, descriptionEn);
        public float SafeFieldScale => fieldScale > 0.01f ? fieldScale : 1f;

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
                descriptionKr = CSVUtil.GetString(row, "description_kr"),
                descriptionEn = CSVUtil.GetString(row, "description_en"),
                image = CSVUtil.GetString(row, "image"),
                fieldScale = CSVUtil.GetFloat(row, "field_scale", 1f),
                isDummy = CSVUtil.GetInt(row, "is_dummy") != 0,
                hpBarAnchorX = CSVUtil.GetFloat(row, "hp_bar_anchor_x", 0.5f),
            };

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
