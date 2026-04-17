using System.Collections.Generic;
using DianoCard.Data;

namespace DianoCard.Game
{
    public class ShopCardEntry
    {
        public CardData card;
        public int price;
        public bool sold;
    }

    public class ShopPotionEntry
    {
        public PotionData potion;
        public int price;
        public bool sold;
    }

    public class ShopRelicEntry
    {
        public RelicData relic;
        public int price;
        public bool sold;
    }

    /// <summary>
    /// 상인 노드 진입 시 생성되는 1회성 재고.
    /// Shop 상태를 빠져나가면 폐기된다 (다시 들르면 새 재고).
    /// </summary>
    public class ShopState
    {
        public List<ShopCardEntry> cards = new();
        public List<ShopPotionEntry> potions = new();
        public List<ShopRelicEntry> relics = new();

        public int cardRemovePrice = 75;
        public bool cardRemoveUsed;
    }
}
