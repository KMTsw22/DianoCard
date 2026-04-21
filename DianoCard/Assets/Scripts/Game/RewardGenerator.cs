using System.Collections.Generic;
using DianoCard.Data;
using UnityEngine;

namespace DianoCard.Game
{
    /// <summary>
    /// 전투 종료 시 BattleReward를 만드는 순수 정적 로직.
    /// 무작위 보상은 DataManager의 카드/유물/물약 풀과 현재 RunState를 기반으로 결정.
    /// </summary>
    public static class RewardGenerator
    {
        public static BattleReward Generate(EnemyData enemy, RunState run)
        {
            var reward = new BattleReward();

            // === 골드 ===
            float multiplier = enemy.enemyType switch
            {
                EnemyType.BOSS => 2f,
                EnemyType.ELITE => 1.5f,
                _ => 1f,
            };
            int baseGold = Random.Range(enemy.goldMin, enemy.goldMax + 1);
            reward.gold = Mathf.RoundToInt(baseGold * multiplier);

            // === 카드 3장 (중복 없음) ===
            reward.cardChoices = PickCards(enemy.enemyType, run, 3);

            // === 물약 ===
            float potionChance = enemy.enemyType switch
            {
                EnemyType.BOSS => 0f,      // 보스는 유물 주므로 물약 없음
                EnemyType.ELITE => 0.5f,
                _ => 0.3f,
            };
            if (!run.PotionSlotFull && Random.value < potionChance)
            {
                reward.potion = PickCommonPotion();
            }

            // === 유물 (엘리트/보스 확정) ===
            if (enemy.enemyType == EnemyType.ELITE)
            {
                reward.relic = PickRelic(RelicSource.ELITE, run);
            }
            else if (enemy.enemyType == EnemyType.BOSS)
            {
                reward.relic = PickRelic(RelicSource.BOSS, run);
            }

            return reward;
        }

        /// <summary>
        /// 마을 보물상자 무료 개봉 보상.
        /// ELITE 풀에서 유물 1개 + 약간의 보너스 골드만 — 카드/물약은 없음.
        /// 유물 풀이 비면(이미 다 보유) 골드만 더 얹어 보너스를 보강한다.
        /// </summary>
        public static BattleReward GenerateTreasureChest(RunState run)
        {
            var reward = new BattleReward
            {
                gold = Random.Range(15, 31),
                relic = PickRelic(RelicSource.ELITE, run),
            };
            if (reward.relic == null)
            {
                // 풀이 비면 BOSS 풀에서 보충 시도
                reward.relic = PickRelic(RelicSource.BOSS, run);
            }
            if (reward.relic == null)
            {
                // 그래도 없으면 보너스 골드로 대체
                reward.gold += Random.Range(40, 71);
            }
            return reward;
        }

        // ---------------------------------------------------------
        // Card pool
        // ---------------------------------------------------------

        private static List<CardData> PickCards(EnemyType type, RunState run, int count)
        {
            // 챕터 인덱스: CH01 -> 1, CH02 -> 2 등
            int chapterIdx = ParseChapterIndex(run.chapterId);

            // 현재 챕터 이하 카드만 (RITUAL은 MVP에서 제외 — 제물 의식 같은 특수 효과)
            // 시작 덱에 이미 들어있는 카드 + 진화 결과체는 보상 풀에서 제외 (중복 수집 / 진화 결과 직접 획득 방지).
            // 캐릭터 archetype에 따라 공룡 종류 분리: HERB는 초식만, CARN은 육식만.
            var evoResults = DataManager.Instance.EvolutionResultIds;
            var character = DataManager.Instance.GetCharacter(run.characterId);
            string archetype = character?.archetype ?? "HERB";
            var starterIds = GameStateManager.GetStarterCardIdsFor(archetype);
            bool isHerb = archetype == "HERB";

            var allEligible = new List<CardData>();
            foreach (var c in DataManager.Instance.Cards.Values)
            {
                if (c.chapter > chapterIdx) continue;
                if (c.cardType == CardType.RITUAL) continue;
                if (starterIds.Contains(c.id)) continue;
                if (evoResults.Contains(c.id)) continue;
                // SUMMON 공룡은 캐릭터 archetype과 sub_type 일치 필요 — 다른 공룡 획득 불가.
                if (c.cardType == CardType.SUMMON)
                {
                    bool matchHerb = c.subType == CardSubType.HERBIVORE;
                    bool matchCarn = c.subType == CardSubType.CARNIVORE;
                    if (isHerb && !matchHerb) continue;
                    if (!isHerb && !matchCarn) continue;
                }
                allEligible.Add(c);
            }

            // 등급 가중치 (type별)
            var weights = type switch
            {
                EnemyType.BOSS => (common: 40, uncommon: 45, rare: 15),
                EnemyType.ELITE => (common: 50, uncommon: 42, rare: 8),
                _ => (common: 60, uncommon: 37, rare: 3),
            };
            int total = weights.common + weights.uncommon + weights.rare;

            var picked = new List<CardData>();
            for (int i = 0; i < count; i++)
            {
                Rarity rarity = RollRarity(weights, total);

                // 해당 rarity 풀에서 중복 없이 뽑기
                var pool = new List<CardData>();
                foreach (var c in allEligible)
                {
                    if (c.rarity == rarity && !picked.Contains(c)) pool.Add(c);
                }

                // 해당 rarity에 카드가 없으면 중복 제외 전체 풀로 fallback
                if (pool.Count == 0)
                {
                    foreach (var c in allEligible)
                        if (!picked.Contains(c)) pool.Add(c);
                }
                if (pool.Count == 0) break;

                picked.Add(pool[Random.Range(0, pool.Count)]);
            }

            return picked;
        }

        private static Rarity RollRarity((int common, int uncommon, int rare) w, int total)
        {
            int roll = Random.Range(0, total);
            if (roll < w.common) return Rarity.COMMON;
            if (roll < w.common + w.uncommon) return Rarity.UNCOMMON;
            return Rarity.RARE;
        }

        private static int ParseChapterIndex(string chapterId)
        {
            // "CH01" -> 1
            if (!string.IsNullOrEmpty(chapterId) && chapterId.Length >= 4)
            {
                if (int.TryParse(chapterId.Substring(2), out int idx)) return idx;
            }
            return 1;
        }

        // ---------------------------------------------------------
        // Potion pool
        // ---------------------------------------------------------

        private static PotionData PickCommonPotion()
        {
            var pool = new List<PotionData>();
            foreach (var p in DataManager.Instance.Potions.Values)
            {
                if (p.rarity == Rarity.COMMON) pool.Add(p);
            }
            return pool.Count > 0 ? pool[Random.Range(0, pool.Count)] : null;
        }

        // ---------------------------------------------------------
        // Relic pool
        // ---------------------------------------------------------

        private static RelicData PickRelic(RelicSource source, RunState run)
        {
            var pool = new List<RelicData>();
            foreach (var r in DataManager.Instance.Relics.Values)
            {
                if (r.source != source) continue;
                if (run.relics.Contains(r)) continue; // 중복 방지
                pool.Add(r);
            }
            return pool.Count > 0 ? pool[Random.Range(0, pool.Count)] : null;
        }
    }
}
