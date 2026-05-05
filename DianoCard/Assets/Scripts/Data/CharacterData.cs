using System.Collections.Generic;

namespace DianoCard.Data
{
    [System.Serializable]
    public class CharacterData
    {
        public string id;
        public string nameKr;
        public string nameEn;
        public int maxHp;
        public int startGold;
        public string descriptionEn;
        public string descriptionKr;
        public string passiveNameEn;
        public string passiveNameKr;
        public string passiveDescriptionEn;
        public string passiveDescriptionKr;
        public string cardPortrait;
        public string fieldPortrait;
        public string selectChar;
        public bool unlocked;
        public string archetype;
        public string linkedForm;

        public string name => LocaleSettings.Pick(nameKr, nameEn);
        public string description => LocaleSettings.Pick(descriptionKr, descriptionEn);
        public string passiveName => LocaleSettings.Pick(passiveNameKr, passiveNameEn);
        public string passiveDescription => LocaleSettings.Pick(passiveDescriptionKr, passiveDescriptionEn);

        public static CharacterData FromRow(Dictionary<string, string> row)
        {
            return new CharacterData
            {
                id = CSVUtil.GetString(row, "id"),
                nameKr = CSVUtil.GetString(row, "name_kr"),
                nameEn = CSVUtil.GetString(row, "name_en"),
                maxHp = CSVUtil.GetInt(row, "max_hp"),
                startGold = CSVUtil.GetInt(row, "start_gold"),
                descriptionEn = CSVUtil.GetString(row, "description_en"),
                descriptionKr = CSVUtil.GetString(row, "description_kr"),
                passiveNameEn = CSVUtil.GetString(row, "passive_name_en"),
                passiveNameKr = CSVUtil.GetString(row, "passive_name_kr"),
                passiveDescriptionEn = CSVUtil.GetString(row, "passive_description_en"),
                passiveDescriptionKr = CSVUtil.GetString(row, "passive_description_kr"),
                cardPortrait = CSVUtil.GetString(row, "card_portrait"),
                fieldPortrait = CSVUtil.GetString(row, "field_portrait"),
                selectChar = CSVUtil.GetString(row, "select_char"),
                unlocked = CSVUtil.GetBool(row, "unlocked"),
                archetype = CSVUtil.GetString(row, "archetype"),
                linkedForm = CSVUtil.GetString(row, "linked_form"),
            };
        }
    }
}
