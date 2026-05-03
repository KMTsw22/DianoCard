using System.Collections.Generic;

namespace DianoCard.Data
{
    /// <summary>
    /// card.csv 한 행에 대응하는 런타임 데이터.
    /// </summary>
    [System.Serializable]
    public class CardData
    {
        public string id;
        public string nameKr;
        public string nameEn;
        public CardType cardType;
        public CardSubType subType;
        public Rarity rarity;
        public int cost;
        public int attack;
        public int hp;
        public int value;
        public TargetType target;
        // CSV는 description_kr / description_en 두 컬럼을 갖는다.
        // 외부에서 c.description으로 읽으면 LocaleSettings 활성언어에 따라 자동 폴백한다.
        public string descriptionKr;
        public string descriptionEn;
        public string image;
        public int chapter;
        // 필드 그릴 때 dinoSize에 곱하는 배율. SUMMON 카드만 의미 있음. CSV 비어있거나 0이면 1.0으로 처리.
        public float fieldScale;

        // 활성언어 기준 본문/이름. 외부에서 c.description, c.name으로 읽으면 LocaleSettings 기준 폴백.
        public string description => LocaleSettings.Pick(descriptionKr, descriptionEn);
        public string name => LocaleSettings.Pick(nameKr, nameEn);

        public static CardData FromRow(Dictionary<string, string> row)
        {
            // 구버전 CSV(description 단일 컬럼) 호환: description_en이 없고 description만 있으면 그쪽으로 폴백.
            string descKr = CSVUtil.GetString(row, "description_kr");
            string descEn = CSVUtil.GetString(row, "description_en");
            if (string.IsNullOrEmpty(descEn))
                descEn = CSVUtil.GetString(row, "description");

            return new CardData
            {
                id = CSVUtil.GetString(row, "id"),
                nameKr = CSVUtil.GetString(row, "name_kr"),
                nameEn = CSVUtil.GetString(row, "name_en"),
                cardType = CSVUtil.GetEnum(row, "card_type", CardType.NONE),
                subType = CSVUtil.GetEnum(row, "sub_type", CardSubType.NONE),
                rarity = CSVUtil.GetEnum(row, "rarity", Rarity.COMMON),
                cost = CSVUtil.GetInt(row, "cost"),
                attack = CSVUtil.GetInt(row, "attack"),
                hp = CSVUtil.GetInt(row, "hp"),
                value = CSVUtil.GetInt(row, "value"),
                target = CSVUtil.GetEnum(row, "target", TargetType.NONE),
                descriptionKr = descKr,
                descriptionEn = descEn,
                image = CSVUtil.GetString(row, "image"),
                chapter = CSVUtil.GetInt(row, "chapter"),
                fieldScale = CSVUtil.GetFloat(row, "field_scale", 1f),
            };
        }

        // CSV의 field_scale이 비어있거나 0이면 1.0으로 보정 — 그릴 때 안전하게 사용.
        public float SafeFieldScale => fieldScale > 0.01f ? fieldScale : 1f;
    }
}
