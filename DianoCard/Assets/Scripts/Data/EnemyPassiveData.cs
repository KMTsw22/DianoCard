using System.Collections.Generic;

namespace DianoCard.Data
{
    [System.Serializable]
    public class EnemyPassiveData
    {
        public string id;
        public string nameKr;
        public string nameEn;
        public string descriptionKr;
        public string descriptionEn;
        public string icon;

        public string name => LocaleSettings.Pick(nameKr, nameEn);
        public string description => LocaleSettings.Pick(descriptionKr, descriptionEn);

        public static EnemyPassiveData FromRow(Dictionary<string, string> row)
        {
            return new EnemyPassiveData
            {
                id = CSVUtil.GetString(row, "id"),
                nameKr = CSVUtil.GetString(row, "name_kr"),
                nameEn = CSVUtil.GetString(row, "name_en"),
                descriptionKr = CSVUtil.GetString(row, "description_kr"),
                descriptionEn = CSVUtil.GetString(row, "description_en"),
                icon = CSVUtil.GetString(row, "icon"),
            };
        }
    }
}
