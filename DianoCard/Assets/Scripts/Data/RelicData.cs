using System.Collections.Generic;

namespace DianoCard.Data
{
    [System.Serializable]
    public class RelicData
    {
        public string id;
        public string nameKr;
        public string nameEn;
        public Rarity rarity;
        public RelicSource source;
        public RelicTrigger trigger;
        public string effectType;
        public int value;
        public string description;

        public static RelicData FromRow(Dictionary<string, string> row)
        {
            return new RelicData
            {
                id = CSVUtil.GetString(row, "id"),
                nameKr = CSVUtil.GetString(row, "name_kr"),
                nameEn = CSVUtil.GetString(row, "name_en"),
                rarity = CSVUtil.GetEnum(row, "rarity", Rarity.COMMON),
                source = CSVUtil.GetEnum(row, "source", RelicSource.START),
                trigger = CSVUtil.GetEnum(row, "trigger", RelicTrigger.PASSIVE),
                effectType = CSVUtil.GetString(row, "effect_type"),
                value = CSVUtil.GetInt(row, "value"),
                description = CSVUtil.GetString(row, "description"),
            };
        }
    }
}
