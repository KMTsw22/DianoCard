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
        public AIPattern aiPattern;
        public int goldMin;
        public int goldMax;
        public string description;
        public string image;

        public static EnemyData FromRow(Dictionary<string, string> row)
        {
            return new EnemyData
            {
                id = CSVUtil.GetString(row, "id"),
                nameKr = CSVUtil.GetString(row, "name_kr"),
                nameEn = CSVUtil.GetString(row, "name_en"),
                enemyType = CSVUtil.GetEnum(row, "enemy_type", EnemyType.NORMAL),
                chapter = CSVUtil.GetInt(row, "chapter"),
                hp = CSVUtil.GetInt(row, "hp"),
                attack = CSVUtil.GetInt(row, "attack"),
                defense = CSVUtil.GetInt(row, "defense"),
                aiPattern = CSVUtil.GetEnum(row, "ai_pattern", AIPattern.ATTACK),
                goldMin = CSVUtil.GetInt(row, "gold_min"),
                goldMax = CSVUtil.GetInt(row, "gold_max"),
                description = CSVUtil.GetString(row, "description"),
                image = CSVUtil.GetString(row, "image"),
            };
        }
    }
}
