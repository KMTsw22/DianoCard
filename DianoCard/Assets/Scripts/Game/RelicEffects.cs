using System;
using System.Collections.Generic;
using DianoCard.Battle;
using DianoCard.Data;
using UnityEngine;

namespace DianoCard.Game
{
    /// <summary>
    /// 유물(Relic) 효과 디스패처 — relic.csv의 effect_type 문자열을 게임 상태에 적용한다.
    ///
    /// 트리거별 진입점:
    ///   - OnAcquired   — 유물 획득 즉시 (영구 스탯 버프 적용용. RunState 변경)
    ///   - OnBattleStart — 새 전투 시작 직후 (드로우/마나/힐/디버프/소환 등)
    ///   - OnTurnStart  — 매 플레이어 턴 시작 (방어도/마나/HP 임계 체크)
    ///   - OnTurnEnd    — 매 턴 종료 (현재 사용처 없음, 확장용)
    ///   - OnSummon     — 공룡 소환 직후 (소환된 공룡 ATK 버프)
    ///   - OnEnemyKilled — 적 처치 시 (KILL 트리거: GOLD/HEAL_KILLER)
    ///   - OnNodeEnter  — 새 맵 노드 진입 (NODE_ENTER 트리거: GOLD)
    ///   - OnBattleEnd  — 전투 승리 시 (BATTLE_END 트리거: 임시 카드 추가)
    ///
    /// effect_type 문자열은 relic.csv 컬럼 값을 그대로 사용. 신규 유물 추가 시 이 파일의 switch 케이스만 늘리면 됨.
    ///
    /// PASSIVE 처리 정책:
    ///   - MAX_HP / POTION_SLOT — OnAcquired에서 RunState 영구 변경. 매번 적용하면 스택됨.
    ///   - 매 턴 자동 효과 (R014 MANA+1) — trigger=PASSIVE인데 OnTurnStart에서도 처리.
    ///   - ATTACK +1 (R005) — OnSummon 시점에 추가. 이미 필드에 있던 공룡엔 영향 없음.
    /// </summary>
    public static class RelicEffects
    {
        // =========================================================
        // 공통 헬퍼
        // =========================================================

        /// <summary>유물 보유 여부 단순 체크.</summary>
        public static bool Has(RunState run, string relicId)
        {
            if (run == null) return false;
            foreach (var r in run.relics)
                if (r != null && r.id == relicId) return true;
            return false;
        }

        /// <summary>HP 회복량 스케일 — R022 타락한 성유물 보유 시 25% 감소.</summary>
        public static float GetHealMultiplier(RunState run)
        {
            if (run == null) return 1f;
            // R022는 effect_type=MAX_HP이지만 description의 "회복량 -25%"는 코드 하드코딩 (CSV 단일컬럼 한계).
            return Has(run, "R022") ? 0.75f : 1f;
        }

        /// <summary>유물이 부여하는 소환수 영구 ATK 보너스 (R005 전체+1, R007 소환시+2).</summary>
        public static int GetSummonAttackBonus(RunState run)
        {
            if (run == null) return 0;
            int bonus = 0;
            foreach (var r in run.relics)
            {
                if (r == null) continue;
                if (r.id == "R005" && r.effectType == "ATTACK") bonus += r.value; // 전체 영구 +1
                if (r.id == "R007" && r.effectType == "ATTACK_BUFF") bonus += r.value; // 소환 시 +2 (영구)
            }
            return bonus;
        }

        // =========================================================
        // 트리거: 유물 획득 (영구 스탯 버프 적용)
        // =========================================================

        /// <summary>
        /// 유물 획득 시 1회 호출. PASSIVE/MAX_HP 같은 영구 스탯 변경을 RunState에 반영.
        /// 매 전투/매 턴 적용되면 스택되는 것들은 여기에 있음.
        /// </summary>
        public static void OnAcquired(RunState run, RelicData relic)
        {
            if (run == null || relic == null) return;
            switch (relic.effectType)
            {
                case "MAX_HP":
                    run.playerMaxHp += relic.value;
                    run.playerCurrentHp = Math.Min(run.playerCurrentHp + relic.value, run.playerMaxHp);
                    Debug.Log($"[Relic] {relic.id} {relic.nameKr}: max HP +{relic.value} → HP {run.playerCurrentHp}/{run.playerMaxHp}");
                    break;

                case "POTION_SLOT":
                    // RunState.MaxPotionSlots 프로퍼티가 자동으로 relics 스캔 → 별도 처리 불필요.
                    Debug.Log($"[Relic] {relic.id} {relic.nameKr}: potion slot +{relic.value} (now {run.MaxPotionSlots})");
                    break;
            }
        }

        // =========================================================
        // 트리거: 전투 시작
        // =========================================================

        /// <summary>전투 시작 직후 호출 (StartBattle 끝부분, 첫 StartTurn 호출 전).</summary>
        public static void OnBattleStart(BattleManager mgr, RunState run)
        {
            if (mgr == null || run == null) return;
            int extraDraw = 0;
            int extraMana = 0;
            int healAmount = 0;
            int burnToAll = 0;
            bool summonCompy = false;

            foreach (var relic in run.relics)
            {
                if (relic == null) continue;
                if (relic.trigger != RelicTrigger.BATTLE_START) continue;
                switch (relic.effectType)
                {
                    case "DRAW":
                        // R002 +1, R015 +3
                        extraDraw += relic.value;
                        break;
                    case "MANA":
                        // R003 첫 턴 마나 +1
                        extraMana += relic.value;
                        break;
                    case "HEAL":
                        // R008 비늘 망토 +5
                        healAmount += relic.value;
                        break;
                    case "BURN":
                        // R010 화산재: 모든 적 화상 N
                        burnToAll += relic.value;
                        break;
                    case "SUMMON_COMPY":
                        // R018 어린 무리: 콤프 1마리 무료 소환
                        summonCompy = true;
                        break;
                }
            }

            if (extraDraw > 0)
            {
                mgr.Draw(extraDraw);
                Debug.Log($"[Relic] BATTLE_START: 추가 드로우 +{extraDraw}");
            }
            if (extraMana > 0)
            {
                mgr.state.player.maxMana += extraMana;
                mgr.state.player.mana += extraMana;
                Debug.Log($"[Relic] BATTLE_START: 마나 +{extraMana} (now {mgr.state.player.mana}/{mgr.state.player.maxMana})");
            }
            if (healAmount > 0)
            {
                int scaled = Mathf.RoundToInt(healAmount * GetHealMultiplier(run));
                if (scaled > 0)
                {
                    mgr.state.player.Heal(scaled);
                    Debug.Log($"[Relic] BATTLE_START: HP +{scaled}");
                }
            }
            if (burnToAll > 0)
            {
                foreach (var e in mgr.state.enemies)
                {
                    if (e == null || e.IsDead) continue;
                    e.burnStacks += burnToAll;
                }
                Debug.Log($"[Relic] BATTLE_START: 모든 적 화상 +{burnToAll}");
            }
            if (summonCompy)
            {
                TrySummonCompy(mgr, run);
            }
        }

        /// <summary>R018 어린 무리 — C010 콤프소그나투스를 무료 소환. 슬롯 가득이면 스킵.</summary>
        private static void TrySummonCompy(BattleManager mgr, RunState run)
        {
            if (mgr == null || mgr.state == null) return;
            if (mgr.state.field.Count >= mgr.state.maxFieldSize) return; // 슬롯 가득
            var compy = DataManager.Instance != null ? DataManager.Instance.GetCard("C010") : null;
            if (compy == null)
            {
                Debug.LogWarning("[Relic] R018 어린 무리: C010 (콤프소그나투스) 카드 데이터 없음");
                return;
            }
            var summon = new SummonInstance(compy);
            // 유물 소환 보너스 적용.
            int bonus = GetSummonAttackBonus(run);
            if (bonus > 0) summon.attack += bonus;
            mgr.state.field.Add(summon);
            Debug.Log($"[Relic] R018 어린 무리: {compy.nameKr} 무료 소환 (ATK {summon.attack})");
        }

        // =========================================================
        // 트리거: 턴 시작
        // =========================================================

        /// <summary>매 플레이어 턴 시작 시 호출 (BattleManager.StartTurn 끝부분).</summary>
        public static void OnTurnStart(BattleManager mgr, RunState run)
        {
            if (mgr == null || run == null) return;
            int blockBonus = 0;
            int manaBonus = 0;
            int hpLowAtkBonus = 0; // R016: HP<50%일 때 모든 소환수 임시 ATK +N

            float hpRatio = mgr.state.player.maxHp > 0
                ? (float)mgr.state.player.hp / mgr.state.player.maxHp
                : 0f;

            foreach (var relic in run.relics)
            {
                if (relic == null) continue;

                // TURN_START 트리거 (R006 DEFENSE 등)
                if (relic.trigger == RelicTrigger.TURN_START)
                {
                    if (relic.effectType == "DEFENSE") blockBonus += relic.value;
                }

                // PASSIVE이지만 매 턴 자동 효과
                if (relic.trigger == RelicTrigger.PASSIVE)
                {
                    // R014 왕의 왕관 — 매 턴 마나 +1
                    if (relic.id == "R014" && relic.effectType == "MANA") manaBonus += relic.value;
                }

                // HP_CRITICAL: HP ≤ 30% (R020 분노의 핏줄: 매 턴 마나 +1)
                if (relic.trigger == RelicTrigger.HP_CRITICAL && hpRatio <= 0.30f && relic.effectType == "MANA")
                {
                    manaBonus += relic.value;
                }

                // HP_LOW: HP ≤ 50% (R016 광폭의 부적: 모든 소환수 ATK +3)
                if (relic.trigger == RelicTrigger.HP_LOW && hpRatio <= 0.50f && relic.effectType == "ATTACK_BUFF")
                {
                    hpLowAtkBonus += relic.value;
                }
            }

            if (blockBonus > 0)
            {
                mgr.state.player.block += blockBonus;
                Debug.Log($"[Relic] TURN_START: 방어도 +{blockBonus} (now {mgr.state.player.block})");
            }
            if (manaBonus > 0)
            {
                mgr.state.player.mana += manaBonus;
                Debug.Log($"[Relic] TURN_START: 마나 +{manaBonus} (now {mgr.state.player.mana})");
            }
            if (hpLowAtkBonus > 0)
            {
                // 임시 ATK 버프 — 이번 턴만 (StartTurn에서 tempAttackBonus가 0으로 리셋되므로 매번 다시 부여).
                foreach (var s in mgr.state.field)
                {
                    if (s == null || s.IsDead) continue;
                    s.tempAttackBonus += hpLowAtkBonus;
                }
                Debug.Log($"[Relic] HP_LOW 격발: 모든 소환수 임시 ATK +{hpLowAtkBonus}");
            }
        }

        // =========================================================
        // 트리거: 턴 종료
        // =========================================================

        /// <summary>매 턴 종료 후 호출 (현재 사용처 없음, 향후 확장).</summary>
        public static void OnTurnEnd(BattleManager mgr, RunState run)
        {
            // 향후 TURN_END 트리거 유물 추가 시 여기.
        }

        // =========================================================
        // 트리거: 공룡 소환
        // =========================================================

        /// <summary>
        /// 새 SummonInstance가 필드에 추가된 직후 호출.
        /// R005 (전체 +1), R007 (소환 시 +2) 적용.
        /// </summary>
        public static void OnSummon(BattleManager mgr, RunState run, SummonInstance summon)
        {
            if (run == null || summon == null) return;
            int bonus = GetSummonAttackBonus(run);
            if (bonus > 0)
            {
                summon.attack += bonus;
                Debug.Log($"[Relic] OnSummon {summon.data.nameKr}: ATK +{bonus} (now {summon.attack})");
            }
        }

        // =========================================================
        // 트리거: 적 처치
        // =========================================================

        /// <summary>
        /// 적이 죽은 직후 호출. killer는 처치한 아군 공룡 (스펠/포션 등 비공룡 처치는 null).
        /// R009 GOLD +3, R019 HEAL_KILLER +2.
        /// </summary>
        public static void OnEnemyKilled(BattleManager mgr, RunState run, SummonInstance killer, EnemyInstance victim)
        {
            if (run == null || victim == null) return;

            // (CANNIBAL 패시브는 적 처치가 아니라 아군 동족포식으로 발동 — BattleManager.FeedCannibal 참고)

            foreach (var relic in run.relics)
            {
                if (relic == null || relic.trigger != RelicTrigger.KILL) continue;
                switch (relic.effectType)
                {
                    case "GOLD":
                        run.gold += relic.value;
                        Debug.Log($"[Relic] {relic.id}: 처치 보상 골드 +{relic.value} (now {run.gold})");
                        break;
                    case "HEAL_KILLER":
                        if (killer != null && !killer.IsDead)
                        {
                            killer.Heal(relic.value);
                            Debug.Log($"[Relic] {relic.id}: {killer.data.nameKr} 처치 회복 +{relic.value}");
                        }
                        break;
                }
            }
        }

        // =========================================================
        // 트리거: 노드 진입
        // =========================================================

        /// <summary>
        /// 새 맵 노드 진입 시 호출 (전투/엘리트/보스/캠프/이벤트/상점 모두).
        /// R017 고고학자의 일지: GOLD +5.
        /// </summary>
        public static void OnNodeEnter(RunState run)
        {
            if (run == null) return;
            foreach (var relic in run.relics)
            {
                if (relic == null || relic.trigger != RelicTrigger.NODE_ENTER) continue;
                switch (relic.effectType)
                {
                    case "GOLD":
                        run.gold += relic.value;
                        Debug.Log($"[Relic] {relic.id}: 노드 진입 골드 +{relic.value} (now {run.gold})");
                        break;
                }
            }
        }

        // =========================================================
        // 트리거: 전투 종료 (승리 시)
        // =========================================================

        /// <summary>
        /// 전투 승리 직후 호출. R012 태초의 알: 랜덤 공룡 카드 1장을 RunState.pendingTemporaryCards에 추가.
        /// 다음 전투 시작 시 시작 덱에 합류하고 즉시 비워진다 (= 일시 카드).
        /// 패배 시(won=false)에는 아무 효과도 없음.
        /// </summary>
        public static void OnBattleEnd(RunState run, bool won)
        {
            if (run == null || !won) return;
            foreach (var relic in run.relics)
            {
                if (relic == null || relic.trigger != RelicTrigger.BATTLE_END) continue;
                if (relic.effectType == "CARD")
                {
                    var card = PickRandomDinoCard(run);
                    if (card != null)
                    {
                        run.pendingTemporaryCards.Add(card);
                        Debug.Log($"[Relic] {relic.id}: 일시 공룡 카드 +1 ({card.nameKr})");
                    }
                }
            }
        }

        /// <summary>현재 챕터의 SUMMON 카드 중 1장 랜덤 선택. 실패 시 null.</summary>
        private static CardData PickRandomDinoCard(RunState run)
        {
            if (DataManager.Instance == null) return null;
            int chapterNum = ParseChapterNumber(run?.chapterId);
            // T1/T2 진화 결과체는 융합 전용 — 일시 카드 풀에서도 제외(보상·상점과 동일 안전망).
            var evoResults = DataManager.Instance.EvolutionResultIds;
            var pool = new List<CardData>();
            foreach (var kv in DataManager.Instance.Cards)
            {
                var c = kv.Value;
                if (c == null) continue;
                if (c.cardType != CardType.SUMMON) continue;
                if (evoResults != null && evoResults.Contains(c.id)) continue;
                if (chapterNum > 0 && c.chapter != chapterNum) continue;
                pool.Add(c);
            }
            if (pool.Count == 0) return null;
            return pool[UnityEngine.Random.Range(0, pool.Count)];
        }

        /// <summary>"CH01" → 1, "CH02" → 2. 0 반환 시 챕터 필터링 안 함.</summary>
        private static int ParseChapterNumber(string chapterId)
        {
            if (string.IsNullOrEmpty(chapterId)) return 0;
            int n = 0;
            foreach (char ch in chapterId)
            {
                if (ch >= '0' && ch <= '9') n = n * 10 + (ch - '0');
            }
            return n;
        }
    }
}
