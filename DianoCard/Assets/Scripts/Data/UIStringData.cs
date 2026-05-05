using System.Collections.Generic;

namespace DianoCard.Data
{
    [System.Serializable]
    public class UIStringData
    {
        public string id;
        public string valueKr;
        public string valueEn;

        public string value => LocaleSettings.Pick(valueKr, valueEn);

        public static UIStringData FromRow(Dictionary<string, string> row)
        {
            return new UIStringData
            {
                id = CSVUtil.GetString(row, "id"),
                valueKr = CSVUtil.GetString(row, "value_kr"),
                valueEn = CSVUtil.GetString(row, "value_en"),
            };
        }
    }
}
