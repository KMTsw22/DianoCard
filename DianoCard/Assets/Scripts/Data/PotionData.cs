using System.Collections.Generic;

namespace DianoCard.Data
{
    [System.Serializable]
    public class PotionData
    {
        public string id;
        public string nameKr;
        public string nameEn;
        public PotionType potionType;
        public Rarity rarity;
        public TargetType target;
        public int value;
        public int price;
        public string descriptionKr;
        public string descriptionEn;
        public bool buyable;

        public string name => LocaleSettings.Pick(nameKr, nameEn);
        public string description => LocaleSettings.Pick(descriptionKr, descriptionEn);

        public static PotionData FromRow(Dictionary<string, string> row)
        {
            return new PotionData
            {
                id = CSVUtil.GetString(row, "id"),
                nameKr = CSVUtil.GetString(row, "name_kr"),
                nameEn = CSVUtil.GetString(row, "name_en"),
                potionType = CSVUtil.GetEnum(row, "potion_type", PotionType.ATTACK),
                rarity = CSVUtil.GetEnum(row, "rarity", Rarity.COMMON),
                target = CSVUtil.GetEnum(row, "target", TargetType.NONE),
                value = CSVUtil.GetInt(row, "value"),
                price = CSVUtil.GetInt(row, "price"),
                descriptionKr = CSVUtil.GetString(row, "description_kr"),
                descriptionEn = CSVUtil.GetString(row, "description_en"),
                buyable = CSVUtil.GetBool(row, "buyable"),
            };
        }
    }
}
