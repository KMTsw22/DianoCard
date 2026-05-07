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
            // 상점 카드 슬롯: 공룡(SUMMON) 2장 + 스펠(MAGIC) 3장
            var summonCommon = new List<CardData>();
            var summonUncommon = new List<CardData>();
            var magicCommon = new List<CardData>();
            var magicUncommon = new List<CardData>();
            var magicRare = new List<CardData>();

            // RewardGenerator와 동일한 풀 규칙: T1/T2 공룡은 융합으로만 획득 — 상점에도 절대 노출 금지.
            // EvolutionResultIds는 dino_evolution.csv + ID suffix(`_T1`/`_T2`) 양쪽으로 채워진 안전망.
            // archetype별 SUMMON 분리.
            var evoResults = dm.EvolutionResultIds;
            var character = dm.GetCharacter(run.characterId);
            string archetype = character?.archetype ?? "HERB";
            bool isHerb = archetype == "HERB";

            foreach (var c in dm.Cards.Values)
            {
                if (c.cardType != CardType.SUMMON && c.cardType != CardType.MAGIC) continue;
                // STATUS(잡초 등) 저주 카드는 상점에 노출되면 안 됨 — 적 강제 추가 전용.
                if (c.subType == CardSubType.STATUS) continue;
                if (evoResults.Contains(c.id)) continue;

                if (c.cardType == CardType.SUMMON)
                {
                    bool matchHerb = c.subType == CardSubType.HERBIVORE || c.subType == CardSubType.OMNIVORE;
                    bool matchCarn = c.subType == CardSubType.CARNIVORE || c.subType == CardSubType.OMNIVORE;
                    if (isHerb && !matchHerb) continue;
                    if (!isHerb && !matchCarn) continue;

                    if (c.rarity == Rarity.COMMON) summonCommon.Add(c);
                    else if (c.rarity == Rarity.UNCOMMON) summonUncommon.Add(c);
                }
                else // MAGIC
                {
                    if (c.rarity == Rarity.COMMON) magicCommon.Add(c);
                    else if (c.rarity == Rarity.UNCOMMON) magicUncommon.Add(c);
                    else if (c.rarity == Rarity.RARE) magicRare.Add(c);
                }
            }

            // SUMMON 2장: COMMON 1 + UNCOMMON 1
            PickCards(summonCommon,   1, shop.cards);
            PickCards(summonUncommon, 1, shop.cards);
            // MAGIC 3장: COMMON 1 + UNCOMMON 1 + RARE 1
            PickCards(magicCommon,   1, shop.cards);
            PickCards(magicUncommon, 1, shop.cards);
            PickCards(magicRare,     1, shop.cards);
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
            const int TARGET = 3;

            var buyable = new List<PotionData>();
            foreach (var p in dm.Potions.Values)
            {
                if (!p.buyable) continue;
                buyable.Add(p);
            }
            if (buyable.Count == 0) return;

            var picked = new HashSet<string>();
            // Phase 1: unique picks
            int attempts = 0;
            while (shop.potions.Count < TARGET && attempts < 50 && picked.Count < buyable.Count)
            {
                attempts++;
                var p = buyable[Random.Range(0, buyable.Count)];
                if (!picked.Add(p.id)) continue;
                int price = p.price > 0 ? p.price : PotionFallbackPrice(p.rarity);
                shop.potions.Add(new ShopPotionEntry { potion = p, price = price });
            }
            // Phase 2: pool too small → allow duplicates to fill remaining
            while (shop.potions.Count < TARGET)
            {
                var p = buyable[Random.Range(0, buyable.Count)];
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
            const int TARGET = 6;

            var pool = new List<RelicData>();
            foreach (var r in dm.Relics.Values)
            {
                if (r.source != RelicSource.SHOP) continue;
                if (run.relics.Contains(r)) continue;
                pool.Add(r);
            }
            if (pool.Count == 0) return;

            var picked = new HashSet<RelicData>();
            // Phase 1: unique picks
            int attempts = 0;
            while (shop.relics.Count < TARGET && attempts < 50 && picked.Count < pool.Count)
            {
                attempts++;
                var r = pool[Random.Range(0, pool.Count)];
                if (!picked.Add(r)) continue;
                shop.relics.Add(new ShopRelicEntry { relic = r, price = RelicPrice(r.rarity) });
            }
            // Phase 2: pool too small → allow duplicates to fill remaining
            while (shop.relics.Count < TARGET)
            {
                var r = pool[Random.Range(0, pool.Count)];
                shop.relics.Add(new ShopRelicEntry { relic = r, price = RelicPrice(r.rarity) });
            }
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
