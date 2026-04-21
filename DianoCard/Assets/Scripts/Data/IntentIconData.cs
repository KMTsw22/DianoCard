using System.Collections.Generic;

namespace DianoCard.Data
{
    /// <summary>intent_icon.csv 한 행 = 인텐트 아이콘 룩업.</summary>
    [System.Serializable]
    public class IntentIconData
    {
        public string id;
        public string iconImage;
        public string colorHint; // "#RRGGBB"
        public string descriptionKr;

        public static IntentIconData FromRow(Dictionary<string, string> row)
        {
            return new IntentIconData
            {
                id            = CSVUtil.GetString(row, "icon_id"),
                iconImage     = CSVUtil.GetString(row, "icon_image"),
                colorHint     = CSVUtil.GetString(row, "color_hint"),
                descriptionKr = CSVUtil.GetString(row, "description_kr"),
            };
        }
    }
}
