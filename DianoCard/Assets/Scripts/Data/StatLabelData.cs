using System.Collections.Generic;

namespace DianoCard.Data
{
    [System.Serializable]
    public class StatLabelData
    {
        public string id;        // ATK, HP, DMG, BLOCK, COST
        public string labelKr;
        public string labelEn;
        public string fullNameKr;
        public string fullNameEn;

        public string label => LocaleSettings.Pick(labelKr, labelEn);
        public string fullName => LocaleSettings.Pick(fullNameKr, fullNameEn);

        public static StatLabelData FromRow(Dictionary<string, string> row)
        {
            return new StatLabelData
            {
                id = CSVUtil.GetString(row, "id"),
                labelKr = CSVUtil.GetString(row, "label_kr"),
                labelEn = CSVUtil.GetString(row, "label_en"),
                fullNameKr = CSVUtil.GetString(row, "full_name_kr"),
                fullNameEn = CSVUtil.GetString(row, "full_name_en"),
            };
        }
    }
}
