using System.Collections.Generic;

namespace DianoCard.Data
{
    /// <summary>
    /// 카드 종류 라벨. id 규칙:
    /// - SUMMON은 "SUMMON_HERBIVORE" / "SUMMON_CARNIVORE" 처럼 subType 포함
    /// - MAGIC은 "MAGIC_ATTACK" / "MAGIC_DEFENSE"
    /// - 나머지는 cardType 이름 그대로 ("BUFF", "UTILITY", "RITUAL")
    /// </summary>
    [System.Serializable]
    public class CardTypeLabelData
    {
        public string id;
        public string label;

        public static CardTypeLabelData FromRow(Dictionary<string, string> row)
        {
            return new CardTypeLabelData
            {
                id = CSVUtil.GetString(row, "id"),
                label = CSVUtil.GetString(row, "label"),
            };
        }

        /// <summary>cardType + subType 조합으로 테이블 조회용 키 생성.</summary>
        public static string BuildKey(CardType cardType, CardSubType subType)
        {
            switch (cardType)
            {
                case CardType.SUMMON:
                    return subType == CardSubType.CARNIVORE ? "SUMMON_CARNIVORE" : "SUMMON_HERBIVORE";
                case CardType.MAGIC:
                    return subType == CardSubType.ATTACK ? "MAGIC_ATTACK" : "MAGIC_DEFENSE";
                default:
                    return cardType.ToString();
            }
        }
    }
}
