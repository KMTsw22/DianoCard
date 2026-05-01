using System.Collections.Generic;

namespace DianoCard.Data
{
    /// <summary>
    /// character.csv 한 행에 대응하는 런타임 데이터.
    /// </summary>
    [System.Serializable]
    public class CharacterData
    {
        public string id;
        public string nameKr;
        public string nameEn;
        public int maxHp;
        public int startGold;
        public string description;
        public string passiveName;
        public string passiveDescription;
        public string cardPortrait;
        public string fieldPortrait;
        public string selectChar;  // 캐릭터 선택 화면 우측 전신 일러 — Resources/CharSelect/Background/<selectChar>.png + <selectChar>_anim/ 폴더
        public bool unlocked;
        public string archetype;  // "HERB" or "CARN" — 덱/보상 풀 분기에 사용
        public string linkedForm; // 같은 인물의 다른 모드(아케네↔린네). 비어있으면 모드 토글 없음.

        public static CharacterData FromRow(Dictionary<string, string> row)
        {
            return new CharacterData
            {
                id = CSVUtil.GetString(row, "id"),
                nameKr = CSVUtil.GetString(row, "name_kr"),
                nameEn = CSVUtil.GetString(row, "name_en"),
                maxHp = CSVUtil.GetInt(row, "max_hp"),
                startGold = CSVUtil.GetInt(row, "start_gold"),
                description = CSVUtil.GetString(row, "description"),
                passiveName = CSVUtil.GetString(row, "passive_name"),
                passiveDescription = CSVUtil.GetString(row, "passive_description"),
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
