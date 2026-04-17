using System.Collections.Generic;
using DianoCard.Data;
using UnityEngine;

namespace DianoCard.Game
{
    /// <summary>
    /// 상인 재고 생성. 현재는 챕터 무관 — 전체 카드/포션/유물 풀에서 희귀도별로 뽑음.
    /// 가격은 희귀도 기반 + 소량 지터. 추후 챕터별 가격 조정 가능.
    /// </summary>
    public static class ShopGenerator
    {
        public static ShopState Generate(RunState run)
        {
            var shop = new ShopState();
            var dm = DataManager.Instance;

            FillCards(shop, dm, run);
            FillPotions(shop, dm);
            FillRelics(shop, dm, run);

            return shop;
        }

        private static void FillCards(ShopState shop, DataManager dm, RunState run)
        {
            var common = new List<CardData>();
            var uncommon = new List<CardData>();
            var rare = new List<CardData>();

            foreach (var c in dm.Cards.Values)
            {
                if (c.cardType == CardType.RITUAL) continue;
                switch (c.rarity)
                {
                    case Rarity.COMMON:   common.Add(c);   break;
                    case Rarity.UNCOMMON: uncommon.Add(c); break;
                    case Rarity.RARE:     rare.Add(c);     break;
                }
            }

            PickCards(common,   3, shop.cards);
            PickCards(uncommon, 1, shop.cards);
            PickCards(rare,     1, shop.cards);
        }

        private static void PickCards(List<CardData> pool, int count, List<ShopCardEntry> outList)
        {
            if (pool.Count == 0) return;

            var picked = new HashSet<string>();
            int attempts = 0;
            while (picked.Count < count && attempts < 50)
            {
                attempts++;
                var c = pool[Random.Range(0, pool.Count)];
                if (!picked.Add(c.id)) continue;

                outList.Add(new ShopCardEntry
                {
                    card = c,
                    price = CardPrice(c.rarity),
                });
            }
        }

        private static int CardPrice(Rarity r)
        {
            int basePrice = r switch
            {
                Rarity.COMMON   => 50,
                Rarity.UNCOMMON => 75,
                Rarity.RARE     => 150,
                _               => 50,
            };
            return basePrice + Random.Range(-5, 6); // ±5 지터
        }

        private static void FillPotions(ShopState shop, DataManager dm)
        {
            var buyable = new List<PotionData>();
            foreach (var p in dm.Potions.Values)
            {
                if (!p.buyable) continue;
                buyable.Add(p);
            }
            if (buyable.Count == 0) return;

            var picked = new HashSet<string>();
            int attempts = 0;
            while (shop.potions.Count < 2 && attempts < 50)
            {
                attempts++;
                var p = buyable[Random.Range(0, buyable.Count)];
                if (!picked.Add(p.id)) continue;

                int price = p.price > 0 ? p.price : PotionFallbackPrice(p.rarity);
                shop.potions.Add(new ShopPotionEntry { potion = p, price = price });
            }
        }

        private static int PotionFallbackPrice(Rarity r) => r switch
        {
            Rarity.COMMON   => 50,
            Rarity.UNCOMMON => 75,
            Rarity.RARE     => 100,
            _               => 50,
        };

        private static void FillRelics(ShopState shop, DataManager dm, RunState run)
        {
            var pool = new List<RelicData>();
            foreach (var r in dm.Relics.Values)
            {
                if (r.source != RelicSource.SHOP) continue;
                if (run.relics.Contains(r)) continue;
                pool.Add(r);
            }
            if (pool.Count == 0) return;

            var r1 = pool[Random.Range(0, pool.Count)];
            shop.relics.Add(new ShopRelicEntry
            {
                relic = r1,
                price = RelicPrice(r1.rarity),
            });
        }

        private static int RelicPrice(Rarity r) => r switch
        {
            Rarity.COMMON   => 150,
            Rarity.UNCOMMON => 200,
            Rarity.RARE     => 300,
            Rarity.SHOP     => 180,
            _               => 180,
        };
    }
}
