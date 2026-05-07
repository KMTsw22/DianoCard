using System.Collections.Generic;

namespace DianoCard.Data
{
    [System.Serializable]
    public class ChapterData
    {
        public string id;
        public string nameKr;
        public string nameEn;
        public int mana;
        public int maxFieldSize;
        public int nodeCount;
        public int normalCount;
        public int eliteCount;
        public int shopCount;
        public int townCount;
        public int eventCount;
        public string bossId;
        public List<string> normalEnemyPool;
        public List<string> normalPoolEarly;
        public List<string> normalPoolMid;
        public List<string> normalPoolLate;
        public List<string> eliteEnemyPool;
        public string descriptionKr;
        public string descriptionEn;

        public string name => LocaleSettings.Pick(nameKr, nameEn);
        public string description => LocaleSettings.Pick(descriptionKr, descriptionEn);

        // 층 기반 티어 풀. 각 티어가 비어있으면 normalEnemyPool로 폴백.
        // CH01 기준: 1~5 early, 6~10 mid, 11+ late.
        public List<string> GetNormalPoolForFloor(int floor)
        {
            List<string> tier;
            if (floor <= 5) tier = normalPoolEarly;
            else if (floor <= 10) tier = normalPoolMid;
            else tier = normalPoolLate;

            if (tier != null && tier.Count > 0) return tier;
            return normalEnemyPool;
        }

        public static ChapterData FromRow(Dictionary<string, string> row)
        {
            // description_kr 우선, 없으면 구버전 'description' 컬럼 폴백
            string descKr = CSVUtil.GetString(row, "description_kr");
            if (string.IsNullOrEmpty(descKr))
                descKr = CSVUtil.GetString(row, "description");

            return new ChapterData
            {
                id = CSVUtil.GetString(row, "id"),
                nameKr = CSVUtil.GetString(row, "name_kr"),
                nameEn = CSVUtil.GetString(row, "name_en"),
                mana = CSVUtil.GetInt(row, "mana"),
                maxFieldSize = CSVUtil.GetInt(row, "max_field_size", 2),
                nodeCount = CSVUtil.GetInt(row, "node_count"),
                normalCount = CSVUtil.GetInt(row, "normal_count"),
                eliteCount = CSVUtil.GetInt(row, "elite_count"),
                shopCount = CSVUtil.GetInt(row, "shop_count"),
                townCount = CSVUtil.GetInt(row, "town_count"),
                eventCount = CSVUtil.GetInt(row, "event_count"),
                bossId = CSVUtil.GetString(row, "boss_id"),
                normalEnemyPool = CSVUtil.GetStringList(row, "normal_enemy_pool"),
                normalPoolEarly = CSVUtil.GetStringList(row, "normal_pool_early"),
                normalPoolMid = CSVUtil.GetStringList(row, "normal_pool_mid"),
                normalPoolLate = CSVUtil.GetStringList(row, "normal_pool_late"),
                eliteEnemyPool = CSVUtil.GetStringList(row, "elite_enemy_pool"),
                descriptionKr = descKr,
                descriptionEn = CSVUtil.GetString(row, "description_en"),
            };
        }
    }
}
