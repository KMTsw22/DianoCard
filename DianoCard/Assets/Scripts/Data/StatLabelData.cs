using System.Collections.Generic;

namespace DianoCard.Data
{
    [System.Serializable]
    public class StatLabelData
    {
        public string id;        // ATK, HP, DMG, BLOCK, COST
        public string label;     // 짧은 표기 (UI 카드 본문용)
        public string fullName;  // 툴팁·상세용 풀 네임

        public static StatLabelData FromRow(Dictionary<string, string> row)
        {
            return new StatLabelData
            {
                id = CSVUtil.GetString(row, "id"),
                label = CSVUtil.GetString(row, "label"),
                fullName = CSVUtil.GetString(row, "full_name"),
            };
        }
    }
}
