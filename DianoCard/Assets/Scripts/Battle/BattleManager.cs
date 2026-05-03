using System;
using System.Collections.Generic;
using System.Text;
using DianoCard.Data;
using UnityEngine;

namespace DianoCard.Battle
{
    /// <summary>
    /// 전투 한 판의 흐름을 제어하는 순수 로직 매니저 (MonoBehaviour 아님).
    /// 주요 API: StartBattle, PlayCard, EndTurn.
    /// state는 public으로 노출 — 추후 UI가 읽어서 화면 갱신 가능.
    /// </summary>
    public class BattleManager
    {
        public BattleState state;

        private readonly System.Random _rng = new();
        private const int DrawPerTurn = 5;

        // =========================================================
        // 전투 시작 / 턴 시작
        // =========================================================

        public void StartBattle(List<CardData> startingDeck, List<EnemyData> enemyPool, int maxMana = 3, int playerHp = 70, int maxFieldSize = 2, float normalHpScale = 1f, float normalDamageScale = 1f)
        {
            state = new BattleState();
            state.maxFieldSize = maxFieldSize;
            state.player = new Player
            {
                maxHp = playerHp,
                hp = playerHp,
                maxMana = maxMana,
            };

            // 같은 id가 연속 등장하면 패턴 스텝을 오프셋해 행동이 동기화되지 않도록 한다.
            // (예: 쌍둥이 E103은 PS_E103의 step 1/2가 엇갈려서 한 쪽이 공격, 한 쪽이 방어)
            // floor 스케일링은 NORMAL 적에만 적용 — 엘리트/보스는 디자인된 값 그대로.
            var idCount = new Dictionary<string, int>();
            foreach (var e in enemyPool)
            {
                if (e == null) continue;
                int dupIdx = idCount.TryGetValue(e.id, out var n) ? n : 0;
                idCount[e.id] = dupIdx + 1;
                float hpScale = e.enemyType == EnemyType.NORMAL ? normalHpScale : 1f;
                float dmgScale = e.enemyType == EnemyType.NORMAL ? normalDamageScale : 1f;
                var inst = new EnemyInstance(e, hpScale, dmgScale);
                inst.patternStepCursor = dupIdx; // 첫 인스턴스 0, 두 번째 1, ...
                state.enemies.Add(inst);
            }

            foreach (var c in startingDeck)
            {
                if (c == null) continue;
                state.deck.Add(new CardInstance(c));
            }

            Shuffle(state.deck);

            Log("=== Battle Start ===");
            StartTurn();
        }

        public void StartTurn()
        {
            state.turn++;
            state.player.mana = state.player.maxMana;
            state.player.block = 0;
            state.player.summonCostReduction = 0;

            // 예약된 다음 턴 방어도 적용 (C104 이중 룬돔 등). 적용 후 소진.
            if (state.player.pendingBlockNextTurn > 0)
            {
                state.player.block += state.player.pendingBlockNextTurn;
                Log($"  >> Pending block {state.player.pendingBlockNextTurn} applied (block now {state.player.block})");
                state.player.pendingBlockNextTurn = 0;
            }

            // 소환수 상태 리셋 — 한 턴 버프 / 공격 가능 플래그 / 방어도 / 스킬 쿨다운 틱
            foreach (var s in state.field)
            {
                s.tempAttackBonus = 0;
                s.hasAttackedThisTurn = false;
                s.block = 0;
                if (s.skillCooldownRemaining > 0) s.skillCooldownRemaining--;
            }

            // 적 인텐트(이번 턴 행동) 결정 + 보스 페이즈 체크 + 적 방어도 리셋
            // CheckBossPhaseTransition의 on_enter SUMMON이 state.enemies를 변경할 수 있어 스냅샷 순회.
            var startTurnSnapshot = new List<EnemyInstance>(state.enemies);
            foreach (var e in startTurnSnapshot)
            {
                if (e.IsDead) continue;
                e.block = e.extraBlockPerTurn;  // 영구 장갑이 있으면 매 턴 그만큼으로 리프레시, 없으면 0.
                CheckBossPhaseTransition(e);
                RollIntent(e);
            }

            Draw(DrawPerTurn);

            Log($"\n--- Turn {state.turn} ---");
            LogState();
        }

        // =========================================================
        // 카드 사용
        // =========================================================

        /// <param name="targetEnemyIndex">단일 적 타겟 카드의 적 인덱스. -1이면 자동(첫 적).</param>
        /// <param name="swapFieldIndex">SUMMON 카드를 필드 꽉 찬 상태로 플레이할 때 교체 대상 공룡 인덱스. 슬롯 여유 있으면 무시.</param>
        /// <param name="allyTargetIndex">ALLY 타겟 카드(수호 마법 등)의 대상 공룡 인덱스. ALL_ALLY는 무시.</param>
        /// <param name="fusion">융합 카드(UTILITY/FUSION) 플레이 시 재료 2개. 다른 카드는 null.</param>
        /// <returns>성공적으로 사용했으면 true</returns>
        public bool PlayCard(int handIndex, int targetEnemyIndex = -1, int swapFieldIndex = -1, int allyTargetIndex = -1, FusionTargets? fusion = null)
        {
            if (handIndex < 0 || handIndex >= state.hand.Count) return false;

            var inst = state.hand[handIndex];
            var card = inst.data;

            // 융합 카드는 별도 경로 — 코스트/재료/필드 변형 로직이 전혀 다르다.
            if (card.cardType == CardType.UTILITY && card.subType == CardSubType.FUSION)
            {
                if (!fusion.HasValue)
                {
                    Log($"  ! Cannot play {card.nameKr}: fusion targets required");
                    return false;
                }
                return TryPlayFusion(handIndex, inst, card, fusion.Value);
            }

            // SUMMON 카드는 summonCostReduction 적용 (C132 동족 소환 등)
            int effectiveCost = card.cardType == CardType.SUMMON
                ? Math.Max(0, card.cost - state.player.summonCostReduction)
                : card.cost;
            if (state.player.mana < effectiveCost)
            {
                Log($"  ! Cannot play {card.nameKr}: need {effectiveCost} mana (have {state.player.mana})");
                return false;
            }

            // 덮어쓰기 사전 판정 — 초식만 해당. 같은 id 초식 SUMMON이 필드에 있으면 슬롯 체크 건너뛰고
            // ResolveCard에서 스택 증가(같은 몸 강화)로 처리. 육식은 융합의 각인(C152)으로만 티어업.
            bool isOverwrite = card.cardType == CardType.SUMMON
                && card.subType == CardSubType.HERBIVORE
                && FindOverwriteTarget(card.id) != null;

            // 필드 슬롯 제한 — 꽉 차면 swapFieldIndex로 교체 가능. 미지정이면 블록. (덮어쓰기는 슬롯 불필요)
            bool needsSwap = card.cardType == CardType.SUMMON && !isOverwrite && state.field.Count >= state.maxFieldSize;
            if (needsSwap)
            {
                if (swapFieldIndex < 0 || swapFieldIndex >= state.field.Count)
                {
                    Log($"  ! Cannot play {card.nameKr}: field full ({state.field.Count}/{state.maxFieldSize}) — swap target required");
                    return false;
                }
                var swapped = state.field[swapFieldIndex];
                // 이미 이번 턴 공격한 공룡은 교체 금지 — 새 공룡이 들어와 또 공격하면 사실상 한 턴 2회 공격이 됨.
                if (swapped.hasAttackedThisTurn)
                {
                    Log($"  ! Cannot swap out {swapped.data.nameKr}: already attacked this turn");
                    return false;
                }
                Log($"  >> Swapped out {swapped.data.nameKr}");
                ReturnBoundCard(swapped);
                state.field.RemoveAt(swapFieldIndex);
            }

            // 수호 마법 단일 타겟(MAGIC/DEFENSE + ALLY): 대상 필요
            if (card.target == TargetType.ALLY
                && card.cardType == CardType.MAGIC
                && card.subType == CardSubType.DEFENSE)
            {
                if (allyTargetIndex < 0 || allyTargetIndex >= state.field.Count)
                {
                    Log($"  ! Cannot play {card.nameKr}: ally target required");
                    return false;
                }
            }

            // 명시된 타겟이 있으면 EnemyInstance로 변환
            EnemyInstance explicitTarget = null;
            if (targetEnemyIndex >= 0 && targetEnemyIndex < state.enemies.Count)
            {
                var candidate = state.enemies[targetEnemyIndex];
                if (!candidate.IsDead) explicitTarget = candidate;
            }

            // 비용 지불 & 손에서 제거
            state.player.mana -= effectiveCost;
            state.hand.RemoveAt(handIndex);
            // 카드 위치 결정:
            //  - SUMMON 신규 소환: 필드 공룡과 바인딩 → state.bound (공룡 제거 시 discard로 복귀)
            //  - SUMMON 덮어쓰기/합성: 기존 공룡에 투입되는 "재료" → discard (재사용 가능, 최종체 T2 도달 후엔 새 소환으로 플레이됨)
            //  - STATUS: 사용 시 소진(exhaust) — 아무 데도 안 감
            //  - 그 외: 일반 버림더미
            if (card.cardType == CardType.SUMMON)
            {
                if (isOverwrite) state.discard.Add(inst);
                else             state.bound.Add(inst);
            }
            else if (card.subType != CardSubType.STATUS)
                state.discard.Add(inst);

            Log($"  [Play] {card.nameKr} (cost {card.cost})");
            ResolveCard(inst, explicitTarget, allyTargetIndex, isOverwrite);

            return true;
        }

        /// <summary>필드에서 같은 base 카드 id(덮어쓰기/합성 대상)의 SummonInstance를 찾음. 없으면 null.
        /// originCardId로 비교하므로 이미 T1/T2로 진화한 공룡도 base 카드가 같으면 매칭됨.</summary>
        private SummonInstance FindOverwriteTarget(string cardId)
        {
            foreach (var s in state.field)
            {
                if (s.IsDead) continue;
                if (s.originCardId == cardId) return s;
            }
            return null;
        }

        private void ResolveCard(CardInstance inst, EnemyInstance explicitTarget, int allyTargetIndex, bool isOverwrite)
        {
            var c = inst.data;
            switch (c.cardType)
            {
                case CardType.SUMMON:
                    if (isOverwrite)
                    {
                        // 초식만 덮어쓰기 — 같은 공룡이 그대로 강해짐(카드 형태 불변, 스탯 영구 상승).
                        var existing = FindOverwriteTarget(c.id);
                        if (existing != null)
                        {
                            existing.stacks++;
                            const int atkGain = 1;
                            const int hpGain = 3;
                            existing.attack += atkGain;
                            existing.maxHp += hpGain;
                            existing.hp += hpGain;
                            Log($"    🌿 덮어쓰기! {c.nameKr} +{atkGain} ATK / +{hpGain} HP (now ATK {existing.attack} / HP {existing.hp}/{existing.maxHp})");
                        }
                    }
                    else
                    {
                        var summon = new SummonInstance(c) { sourceCardInstance = inst };
                        state.field.Add(summon);
                        Log($"    Summoned {c.nameKr} (ATK {c.attack} / HP {c.hp})");
                    }
                    break;

                case CardType.MAGIC:
                    ResolveMagic(c, explicitTarget, allyTargetIndex);
                    break;

                case CardType.BUFF:
                    ResolveBuff(c);
                    break;

                case CardType.UTILITY:
                    if (c.subType == CardSubType.DRAW)
                    {
                        Draw(c.value);
                        Log($"    Drew {c.value} cards");
                    }
                    break;

                case CardType.RITUAL:
                    // MVP: 구현 안 함
                    Log($"    (RITUAL not yet implemented)");
                    break;
            }
        }

        // =========================================================
        // 융합의 각인 (C152) — 같은 종·같은 티어 육식 2마리 합성
        // =========================================================

        /// <summary>융합 시도 — 검증 → 코스트 체크 → 재료 소비 → 다음 티어로 변형.
        /// 실패 시 어떤 상태도 변경하지 않는다 (얼리 리턴으로 원자성 보장).</summary>
        private bool TryPlayFusion(int catalystHandIndex, CardInstance catalystInst, CardData catalyst, FusionTargets targets)
        {
            var a = targets.a;
            var b = targets.b;

            // 1) 중복 지정 방지
            if (a.isHand == b.isHand && a.index == b.index)
            {
                Log($"  ! {catalyst.nameKr}: 같은 재료 중복 지정 불가");
                return false;
            }
            // 1-b) 촉매 카드 자체를 재료로 지정 방지
            if ((a.isHand && a.index == catalystHandIndex) || (b.isHand && b.index == catalystHandIndex))
            {
                Log($"  ! {catalyst.nameKr}: 촉매 카드 자체는 재료로 쓸 수 없음");
                return false;
            }

            // 2) 재료 해석 — 손은 CardData, 필드는 SummonInstance
            SummonInstance aField = a.isHand ? null : GetFieldSafe(a.index);
            SummonInstance bField = b.isHand ? null : GetFieldSafe(b.index);
            CardData aData = a.isHand ? GetHandCardSafe(a.index) : aField?.data;
            CardData bData = b.isHand ? GetHandCardSafe(b.index) : bField?.data;
            if (aData == null || bData == null)
            {
                Log($"  ! {catalyst.nameKr}: 재료 인덱스 범위 오류");
                return false;
            }

            // 3) 둘 다 육식 SUMMON 이어야 함
            bool aIsCarnivore = aData.cardType == CardType.SUMMON && aData.subType == CardSubType.CARNIVORE;
            bool bIsCarnivore = bData.cardType == CardType.SUMMON && bData.subType == CardSubType.CARNIVORE;
            if (!aIsCarnivore || !bIsCarnivore)
            {
                Log($"  ! {catalyst.nameKr}: 재료는 육식 SUMMON 카드여야 함");
                return false;
            }

            // 4) 종 일치 — 필드는 originCardId(진화 전 베이스), 손은 data.id(애초 T0)
            string aBase = aField != null ? aField.originCardId : aData.id;
            string bBase = bField != null ? bField.originCardId : bData.id;
            if (aBase != bBase)
            {
                Log($"  ! {catalyst.nameKr}: 서로 다른 종({aBase} vs {bBase})은 융합 불가");
                return false;
            }

            // 5) 티어 일치 — 필드는 현재 data.id 접미사, 손 카드는 항상 T0
            int aTier = aField != null ? GetCarnivoreRank(aField.data.id) : 0;
            int bTier = bField != null ? GetCarnivoreRank(bField.data.id) : 0;
            if (aTier != bTier)
            {
                Log($"  ! {catalyst.nameKr}: 혼합 티어(T{aTier}+T{bTier}) 불가");
                return false;
            }
            if (aTier >= 2)
            {
                Log($"  ! {catalyst.nameKr}: 최종체(T2)는 더 이상 융합 불가");
                return false;
            }

            // 6) 다음 티어 조회
            string currentId = aField != null ? aField.data.id : aData.id;
            var evo = DataManager.Instance.GetEvolution(currentId);
            var nextData = evo != null ? DataManager.Instance.GetCard(evo.resultCardId) : null;
            if (nextData == null)
            {
                Log($"  ! {catalyst.nameKr}: {currentId}의 다음 진화 경로 없음");
                return false;
            }

            // 7) 코스트 계산 — 기본(각인) + 손 재료 소환 코스트
            int aHandCost = a.isHand ? aData.cost : 0;
            int bHandCost = b.isHand ? bData.cost : 0;
            int totalCost = catalyst.cost + aHandCost + bHandCost;
            if (state.player.mana < totalCost)
            {
                Log($"  ! {catalyst.nameKr}: 필요 마나 {totalCost} (보유 {state.player.mana})");
                return false;
            }

            // 8) 슬롯 체크 — hand+hand는 필드 +1. field+field는 -1, field+hand는 ±0.
            bool bothHand = a.isHand && b.isHand;
            if (bothHand && state.field.Count >= state.maxFieldSize)
            {
                Log($"  ! {catalyst.nameKr}: 필드 꽉 참 — 손+손 융합은 빈 슬롯 필요");
                return false;
            }

            // === 여기서부터 상태 변경 — 이 지점 이전에 모든 검증 완료 ===

            state.player.mana -= totalCost;

            // 각인 카드 제거 → discard
            state.hand.RemoveAt(catalystHandIndex);
            state.discard.Add(catalystInst);

            // 손 재료 인덱스 보정 (catalyst 제거로 밀려난 만큼)
            int ah = a.isHand ? (a.index > catalystHandIndex ? a.index - 1 : a.index) : -1;
            int bh = b.isHand ? (b.index > catalystHandIndex ? b.index - 1 : b.index) : -1;

            // 손 재료 제거 — 인덱스 큰 쪽부터
            var handIdxToRemove = new List<int>();
            if (ah >= 0) handIdxToRemove.Add(ah);
            if (bh >= 0) handIdxToRemove.Add(bh);
            handIdxToRemove.Sort((x, y) => y.CompareTo(x));
            foreach (int hi in handIdxToRemove)
            {
                var hc = state.hand[hi];
                state.hand.RemoveAt(hi);
                state.discard.Add(hc); // 손 재료는 discard로 (재사용 가능)
            }

            // 버프 승계 — 더 높은 쪽 기준
            int maxAtkBonus = 0;
            int maxHpBonus = 0;
            int preservedHp = 0;
            if (aField != null)
            {
                maxAtkBonus = Math.Max(maxAtkBonus, aField.attack - aField.data.attack);
                maxHpBonus  = Math.Max(maxHpBonus,  aField.maxHp - aField.data.hp);
                preservedHp = Math.Max(preservedHp, aField.hp);
            }
            if (bField != null)
            {
                maxAtkBonus = Math.Max(maxAtkBonus, bField.attack - bField.data.attack);
                maxHpBonus  = Math.Max(maxHpBonus,  bField.maxHp - bField.data.hp);
                preservedHp = Math.Max(preservedHp, bField.hp);
            }

            // 결과 SummonInstance 결정 — 필드 재료가 하나라도 있으면 그걸 "기본"으로 변형
            SummonInstance result;
            if (aField != null)
            {
                result = aField;
                if (bField != null)
                {
                    // field+field — 두 번째 재료를 필드에서 제거
                    ReturnBoundCard(bField);
                    state.field.Remove(bField);
                }
                ApplyFusionResult(result, nextData, aBase, maxAtkBonus, maxHpBonus, preservedHp);
            }
            else if (bField != null)
            {
                result = bField;
                ApplyFusionResult(result, nextData, aBase, maxAtkBonus, maxHpBonus, preservedHp);
            }
            else
            {
                // hand+hand — 신규 SummonInstance 생성, 필드 진입. sourceCardInstance 없음(재료 카드는 discard로 감).
                result = new SummonInstance(nextData) { originCardId = aBase };
                // 손 재료는 다치지 않은 상태라 hp=maxHp. 보너스는 0일 수밖에 없지만 정합성 유지.
                result.attack = nextData.attack + maxAtkBonus;
                result.maxHp  = nextData.hp + maxHpBonus;
                result.hp     = result.maxHp;
                state.field.Add(result);
            }

            Log($"    🦖 융합! {aBase} T{aTier}+T{bTier} → {nextData.nameKr} (ATK {result.attack} / HP {result.hp}/{result.maxHp}, cost {totalCost})");
            return true;
        }

        /// <summary>필드 재료 하나를 다음 티어로 변형 — HP 비율 유지 + minHp/3 보장 + 승계 버프 적용.</summary>
        private void ApplyFusionResult(SummonInstance s, CardData next, string baseId, int maxAtkBonus, int maxHpBonus, int preservedCurrentHp)
        {
            float hpRatio = s.maxHp > 0 ? (float)preservedCurrentHp / s.maxHp : 1f;
            s.data = next;
            s.originCardId = baseId;
            s.attack = next.attack + maxAtkBonus;
            s.maxHp  = next.hp + maxHpBonus;
            int minHp = Math.Max(1, s.maxHp / 3);
            int scaledHp = Mathf.RoundToInt(s.maxHp * hpRatio);
            s.hp = Math.Clamp(Math.Max(scaledHp, preservedCurrentHp), minHp, s.maxHp);
            s.hp = Math.Min(s.hp, s.maxHp);
        }

        private SummonInstance GetFieldSafe(int idx)
        {
            if (idx < 0 || idx >= state.field.Count) return null;
            return state.field[idx];
        }

        private CardData GetHandCardSafe(int idx)
        {
            if (idx < 0 || idx >= state.hand.Count) return null;
            return state.hand[idx].data;
        }

        /// <summary>융합 카드의 실제 코스트 계산 — UI에서 카드 playability 표시에 사용.</summary>
        public int ComputeFusionCost(CardData catalyst, FusionTargets targets)
        {
            int aHandCost = 0;
            int bHandCost = 0;
            if (targets.a.isHand)
            {
                var ad = GetHandCardSafe(targets.a.index);
                if (ad != null) aHandCost = ad.cost;
            }
            if (targets.b.isHand)
            {
                var bd = GetHandCardSafe(targets.b.index);
                if (bd != null) bHandCost = bd.cost;
            }
            return catalyst.cost + aHandCost + bHandCost;
        }

        private void ResolveMagic(CardData c, EnemyInstance explicitTarget, int allyTargetIndex)
        {
            if (c.subType == CardSubType.ATTACK)
            {
                // 카드별 특수 피해 계산
                int baseDmg = c.value;
                switch (c.id)
                {
                    case "C124":  // 분노의 일격: HP 절반 이하면 10
                        if (state.player.hp * 2 <= state.player.maxHp) baseDmg = 10;
                        break;
                    case "C131":  // 발톱 투척: 필드 공룡 ATK 합
                        int sum = 0;
                        foreach (var s in state.field) if (!s.IsDead) sum += s.TotalAttack;
                        baseDmg = sum;
                        if (baseDmg <= 0) { Log($"    ! {c.nameKr}: 필드에 공룡이 없어 피해 0"); return; }
                        break;
                }
                int dmg = ApplyPlayerWeak(baseDmg);
                switch (c.target)
                {
                    case TargetType.ALL_ENEMY:
                        // CheckBossPhaseTransition의 on_enter SUMMON이 state.enemies를 변경할 수 있음.
                        var allSnapshot = new List<EnemyInstance>(state.enemies);
                        foreach (var e in allSnapshot)
                        {
                            if (e.IsDead) continue;
                            // 이끼 보호 중인 본체는 데미지 무시 (AOE로도 뚫을 수 없음).
                            if (IsProtectedByMoss(e))
                            {
                                Log($"    -> {e.data.nameKr}: 이끼가 보호 중 (피해 무시)");
                                continue;
                            }
                            e.TakeDamage(dmg);
                            Log($"    -> {e.data.nameKr} takes {dmg} (HP {e.hp})");
                            CheckBossPhaseTransition(e);
                            CheckPartnerDeathTrigger(e);
                        }
                        break;
                    case TargetType.RANDOM:
                        var randTarget = RandomTargetableEnemy();
                        if (randTarget != null)
                        {
                            randTarget.TakeDamage(dmg);
                            Log($"    -> {randTarget.data.nameKr} takes {dmg} (HP {randTarget.hp})");
                            CheckBossPhaseTransition(randTarget);
                            CheckPartnerDeathTrigger(randTarget);
                        }
                        break;
                    default:
                        // ENEMY 또는 미지정 — 명시 타겟 우선, 없으면 첫 적
                        var t = explicitTarget ?? FirstTargetableEnemy();
                        // 명시 타겟이 이끼 보호 중이면 이끼로 리다이렉트
                        if (t != null && IsProtectedByMoss(t))
                        {
                            var moss = FirstTargetableEnemy(); // 이끼 우선 반환
                            if (moss != null && moss != t)
                            {
                                Log($"    -> {t.data.nameKr}는 이끼 보호 중 — {moss.data.nameKr}로 타겟 전환");
                                t = moss;
                            }
                            else t = null;
                        }
                        if (t != null && !t.IsDead)
                        {
                            t.TakeDamage(dmg);
                            Log($"    -> {t.data.nameKr} takes {dmg} (HP {t.hp})");
                            CheckBossPhaseTransition(t);
                            CheckPartnerDeathTrigger(t);
                        }
                        break;
                }

                // 카드별 사후 효과 — 공격 후 추가 효과
                if (c.id == "C121")  // 돌진: 소환사 방어도 +5
                {
                    state.player.block += 5;
                    Log($"    -> +5 block (now {state.player.block})");
                }
                if (c.id == "C129")  // 포효: 모든 적 1턴 약화
                {
                    int weakApplied = 0;
                    foreach (var e in state.enemies)
                    {
                        if (e.IsDead) continue;
                        e.weakTurns += 1;
                        weakApplied++;
                    }
                    Log($"    -> {weakApplied}체 적 약화 1턴");
                }
                if (c.id == "C139")  // 자전 섬광: 1장 드로우
                {
                    Draw(1);
                    Log($"    -> Drew 1 card");
                }
            }
            else if (c.subType == CardSubType.DEFENSE)
            {
                if (c.target == TargetType.ALLY
                    && allyTargetIndex >= 0 && allyTargetIndex < state.field.Count)
                {
                    var s = state.field[allyTargetIndex];
                    s.block += c.value;
                    Log($"    -> {s.data.nameKr} +{c.value} block (now {s.block})");
                }
                else if (c.target == TargetType.ALL_ALLY)
                {
                    int affected = 0;
                    foreach (var s in state.field)
                    {
                        if (s.IsDead) continue;
                        s.block += c.value;
                        affected++;
                    }
                    Log($"    -> {affected}체의 공룡 +{c.value} block");
                }
                else
                {
                    state.player.block += c.value;
                    Log($"    -> +{c.value} block (now {state.player.block})");
                }

                if (c.id == "C140")  // 수목의 매듭: 1장 드로우
                {
                    Draw(1);
                    Log($"    -> Drew 1 card");
                }
                if (c.id == "C104")  // 이중 룬돔: 다음 턴 +5 방어 예약
                {
                    state.player.pendingBlockNextTurn += 5;
                    Log($"    -> +5 block scheduled for next turn (pending {state.player.pendingBlockNextTurn})");
                }
            }
            else if (c.subType == CardSubType.PURIFY)
            {
                ResolvePurify(c);
            }
            else if (c.subType == CardSubType.DEBUFF)
            {
                ResolveDebuff(c, explicitTarget);
            }
            // 융합(FUSION)은 TryPlayFusion에서 전체 처리 — 여기로 오지 않음.
        }

        /// <summary>육식공룡 티어 rank 판정 — 베이스=0, _T1=1, _T2=2. 진화 CSV와 카드 id 규칙에 의존.</summary>
        private static int GetCarnivoreRank(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return 0;
            if (cardId.EndsWith("_T2")) return 2;
            if (cardId.EndsWith("_T1")) return 1;
            return 0;
        }

        /// <summary>DEBUFF 마법 — 카드 id별로 독/취약/속박 등 상태를 적에게 부여.
        /// target이 ALL_ENEMY면 광역, 아니면 explicitTarget(없으면 첫 적)에 단일 적용.</summary>
        private void ResolveDebuff(CardData c, EnemyInstance explicitTarget)
        {
            // 단일 타겟 결정 — 명시 타겟 우선, 없으면 첫 적. 이끼 보호 중이면 이끼로 리다이렉트.
            EnemyInstance ResolveSingle()
            {
                var t = explicitTarget ?? FirstTargetableEnemy();
                if (t != null && IsProtectedByMoss(t))
                {
                    var moss = FirstTargetableEnemy();
                    if (moss != null && moss != t) t = moss;
                    else t = null;
                }
                return (t != null && !t.IsDead) ? t : null;
            }

            switch (c.id)
            {
                case "C133":  // 속박의 덩굴: 속박(=stun) value턴
                {
                    var t = ResolveSingle();
                    if (t == null) return;
                    t.stunTurns += c.value;
                    Log($"    -> {t.data.nameKr} 속박 +{c.value}T (총 {t.stunTurns}T)");
                    break;
                }
                case "C135":  // 부식의 선풍: value 피해 + 취약 1턴
                {
                    var t = ResolveSingle();
                    if (t == null) return;
                    int dmg = ApplyPlayerWeak(c.value);
                    t.TakeDamage(dmg);
                    t.vulnerableTurns += 1;
                    Log($"    -> {t.data.nameKr} takes {dmg} (HP {t.hp}), 취약 +1T (총 {t.vulnerableTurns}T)");
                    CheckBossPhaseTransition(t);
                    CheckPartnerDeathTrigger(t);
                    break;
                }
                case "C136":  // 맹독 가시: 독 +value
                {
                    var t = ResolveSingle();
                    if (t == null) return;
                    t.poisonStacks += c.value;
                    Log($"    -> {t.data.nameKr} 독 +{c.value} (총 {t.poisonStacks})");
                    break;
                }
                case "C142":  // 태고의 비명: 전체 적 취약 1턴
                {
                    int n = 0;
                    foreach (var e in state.enemies)
                    {
                        if (e.IsDead) continue;
                        e.vulnerableTurns += 1;
                        n++;
                    }
                    Log($"    -> {n}체 적 취약 +1T");
                    break;
                }
                default:
                    Log($"    ? unhandled DEBUFF card '{c.id}'");
                    break;
            }
        }

        private void ResolveBuff(CardData c)
        {
            switch (c.subType)
            {
                case CardSubType.ATTACK_BUFF:
                    // "초식 공룡 공격력 +3" 류는 value만큼 한 턴 공격력 증가 (타겟 전체 아군 단순화)
                    foreach (var s in state.field)
                    {
                        s.tempAttackBonus += c.value;
                    }
                    Log($"    -> All summons +{c.value} ATK (this turn)");
                    break;

                case CardSubType.HEAL:
                    // 힐은 공룡 전용 — 소환사는 포션/이벤트로만 회복.
                    foreach (var s in state.field) s.Heal(c.value);
                    Log($"    -> All dinosaurs heal +{c.value}");
                    break;

                case CardSubType.TAUNT:
                    int applied = 0;
                    foreach (var s in state.field)
                    {
                        if (s.IsDead) continue;
                        s.tauntTurns += Math.Max(1, c.value);
                        applied++;
                    }
                    Log($"    -> 도발: {applied}체의 공룡 {c.value}턴 도발 상태");
                    // 이미 롤된 적 인텐트 타겟을 도발 공룡으로 갱신 — UI 화살표/툴팁이 즉시 반영되도록.
                    SummonInstance tauntTarget = null;
                    foreach (var s in state.field) if (s.IsTaunting) { tauntTarget = s; break; }
                    if (tauntTarget != null)
                    {
                        foreach (var en in state.enemies)
                        {
                            if (en.IsDead) continue;
                            if (IsSingleTargetAttackAction(en.intentAction))
                                en.intentTargetDino = tauntTarget;
                        }
                    }
                    break;

                case CardSubType.SPECIAL:
                    // 카드별 특수 효과
                    if (c.id == "C132")  // 동족 소환: 이번 턴 SUMMON 비용 감면
                    {
                        int reduction = Math.Max(1, c.value);
                        state.player.summonCostReduction += reduction;
                        Log($"    -> 이번 턴 SUMMON 비용 -{reduction} (누적 {state.player.summonCostReduction})");
                    }
                    else
                    {
                        Log($"    (SPECIAL buff not yet implemented: {c.nameKr})");
                    }
                    break;
            }
        }

        // =========================================================
        // 턴 종료 → 소환수 공격 → 적 턴 → 다음 턴
        // =========================================================

        /// <summary>
        /// 한 번에 모든 단계를 실행하는 동기 EndTurn (BattleTest 등 헤드리스용).
        /// UI에서는 애니메이션을 위해 아래 granular 메서드들을 직접 호출하는 것을 권장.
        /// </summary>
        public void EndTurn()
        {
            Log($"  [End Turn {state.turn}]");

            // 0. 플레이어 디버프 틱 (독/약화/취약)
            state.player.TickStatuses();
            if (state.PlayerLost) { Log("=== DEFEAT (poison) ==="); return; }

            // 1. 공룡 수동 공격 시스템 — 플레이어가 명령하지 않은 공룡은 이번 턴 공격 없음.
            //    침묵/도발 카운트다운은 턴 종료 시 1 감소.
            foreach (var s in state.field)
            {
                if (s.IsDead) continue;
                if (s.silencedTurns > 0) s.silencedTurns--;
                if (s.tauntTurns > 0) s.tauntTurns--;
            }

            if (state.PlayerWon) { Log("=== VICTORY ==="); return; }

            // 2. 적 턴 (한 번에) — SUMMON 액션이 state.enemies를 변경하므로 스냅샷 순회.
            //    이번 턴 새로 소환된 쫄은 다음 턴부터 행동.
            var endTurnSnapshot = new List<EnemyInstance>(state.enemies);
            foreach (var e in endTurnSnapshot)
            {
                if (e.IsDead) continue;
                DoEnemyAction(e);
                // Dual action 등 extraActionsPerTurn — 첫 행동 후 인텐트 재Roll하고 추가 실행.
                for (int k = 0; k < e.extraActionsPerTurn; k++)
                {
                    if (e.IsDead || state.PlayerLost) break;
                    RollIntent(e);
                    DoEnemyAction(e);
                }
                e.TickStatuses();
                if (state.PlayerLost) { Log("=== DEFEAT ==="); return; }
            }

            EndTurnCleanup();
            StartNextTurnIfAlive();
        }

        // =========================================================
        // 애니메이션용 granular 단계 메서드 (BattleUI 코루틴이 사용)
        // =========================================================

        /// <summary>
        /// (레거시/자동) 한 마리 소환수 공격 실행. 현재는 수동 시스템으로 전환되어 외부에서 사용하지 않지만,
        /// 치트/디버그용으로 남겨둠. hasAttackedThisTurn 갱신 포함.
        /// </summary>
        public void DoSummonAttack(SummonInstance summon)
        {
            if (summon == null || !summon.CanAttack) return;
            var target = FirstTargetableEnemy();
            if (target == null) return;
            int dmg = ApplyPlayerWeak(summon.TotalAttack);
            target.TakeDamage(dmg);
            summon.hasAttackedThisTurn = true;
            Log($"  {summon.data.nameKr} attacks {target.data.nameKr} for {dmg} (HP {target.hp})");
            if (target.IsDead) Log($"    x {target.data.nameKr} defeated");
            CheckBossPhaseTransition(target);
            CheckPartnerDeathTrigger(target);
        }

        /// <summary>
        /// 플레이어가 소환수에게 공격 명령을 내린다 (수동 공격 시스템).
        /// 침묵 중 / 이미 공격함 / 죽음 / 보호막 중 → 실패(false).
        /// 타겟이 보호 중인 본체면 이끼로 자동 리다이렉트.
        /// </summary>
        public bool CommandSummonAttack(int summonIndex, int targetEnemyIndex)
        {
            if (summonIndex < 0 || summonIndex >= state.field.Count) return false;
            var summon = state.field[summonIndex];
            if (!summon.CanAttack)
            {
                Log($"  ! {summon.data.nameKr}: 공격 불가 (" +
                    $"{(summon.hasAttackedThisTurn ? "이미 공격" : summon.silencedTurns > 0 ? $"침묵 {summon.silencedTurns}T" : "죽음")})");
                return false;
            }
            if (targetEnemyIndex < 0 || targetEnemyIndex >= state.enemies.Count) return false;
            var target = state.enemies[targetEnemyIndex];
            if (target == null || target.IsDead) return false;
            // 이끼 보호 중인 본체는 이끼로 리다이렉트
            if (IsProtectedByMoss(target))
            {
                var moss = FirstTargetableEnemy(); // 이끼 우선
                if (moss != null && moss != target)
                {
                    Log($"    -> {target.data.nameKr}는 이끼 보호 중 — {moss.data.nameKr}로 타겟 전환");
                    target = moss;
                }
                else return false;
            }

            int dmg = ApplyPlayerWeak(summon.TotalAttack);
            target.TakeDamage(dmg);
            summon.hasAttackedThisTurn = true;
            Log($"  [Command] {summon.data.nameKr} attacks {target.data.nameKr} for {dmg} (HP {target.hp})");
            if (target.IsDead) Log($"    x {target.data.nameKr} defeated");
            CheckBossPhaseTransition(target);
            CheckPartnerDeathTrigger(target);
            return true;
        }

        /// <summary>플레이어가 약화 상태면 데미지 25% 감소 (rough).</summary>
        private int ApplyPlayerWeak(int dmg)
        {
            if (state.player.weakTurns > 0) return Math.Max(1, (int)(dmg * 0.75f));
            return dmg;
        }

        // =========================================================
        // 시그니처 스킬 (T1+ 진화 공룡)
        // =========================================================

        /// <summary>해당 인덱스의 소환수가 갖고 있는 시그니처 스킬 정보. 스킬 없거나 인덱스 invalid면 null.</summary>
        public DinoSkillData GetSkillForSummon(int summonIndex)
        {
            if (summonIndex < 0 || summonIndex >= state.field.Count) return null;
            return DataManager.Instance.GetSkill(state.field[summonIndex].data.id);
        }

        /// <summary>UI가 스킬 버튼 활성/비활성 결정에 사용. 스킬 없거나, 사망/침묵/쿨다운 중이면 false.</summary>
        public bool CanUseSkill(int summonIndex)
        {
            if (summonIndex < 0 || summonIndex >= state.field.Count) return false;
            var s = state.field[summonIndex];
            if (s.IsDead || s.silencedTurns > 0) return false;
            var skill = DataManager.Instance.GetSkill(s.data.id);
            if (skill == null) return false;
            if (skill.isOnceBattle) return !s.skillUsedThisBattle;
            return s.skillCooldownRemaining <= 0;
        }

        /// <summary>
        /// 소환수에게 시그니처 스킬 사용 명령. 일반 공격(CommandSummonAttack)과 별개 자원이라 같은 턴에 둘 다 가능.
        /// targetEnemyIndex는 ENEMY 타겟에서만 의미; -1이면 자동 선정(첫 타게터블 적).
        /// </summary>
        public bool CommandSummonSkill(int summonIndex, int targetEnemyIndex = -1)
        {
            if (summonIndex < 0 || summonIndex >= state.field.Count) return false;
            var summon = state.field[summonIndex];
            var skill = DataManager.Instance.GetSkill(summon.data.id);
            if (skill == null)
            {
                Log($"  ! {summon.data.nameKr}: 스킬 없음 (T0 또는 비진화 공룡)");
                return false;
            }
            if (summon.IsDead || summon.silencedTurns > 0)
            {
                Log($"  ! {summon.data.nameKr}: 스킬 사용 불가 (사망/침묵)");
                return false;
            }
            if (skill.isOnceBattle)
            {
                if (summon.skillUsedThisBattle)
                {
                    Log($"  ! {summon.data.nameKr}: 이미 이번 전투에 {skill.nameKr} 사용함");
                    return false;
                }
            }
            else if (summon.skillCooldownRemaining > 0)
            {
                Log($"  ! {summon.data.nameKr}: {skill.nameKr} 쿨다운 {summon.skillCooldownRemaining}T 남음");
                return false;
            }

            Log($"  [Skill] {summon.data.nameKr} → {skill.nameKr}");

            // 데미지 타겟 결정
            var damageTargets = ResolveSkillDamageTargets(skill, targetEnemyIndex);

            // 데미지 적용 (히트 수만큼 반복)
            if (skill.damage > 0 && damageTargets.Count > 0)
            {
                for (int h = 0; h < skill.hits; h++)
                {
                    foreach (var t in damageTargets)
                    {
                        if (t.IsDead) continue;
                        int dmg = ApplyPlayerWeak(skill.damage);
                        t.TakeDamage(dmg);
                        Log($"    -> {t.data.nameKr} -{dmg} (HP {t.hp})");
                        if (t.IsDead) Log($"    x {t.data.nameKr} defeated");
                        CheckBossPhaseTransition(t);
                        CheckPartnerDeathTrigger(t);
                    }
                }
            }

            // 부가 효과 적용
            ApplySkillEffects(summon, skill, damageTargets);

            // 쿨다운 갱신
            if (skill.isOnceBattle) summon.skillUsedThisBattle = true;
            else summon.skillCooldownRemaining = skill.cooldownTurns;

            return true;
        }

        private List<EnemyInstance> ResolveSkillDamageTargets(DinoSkillData skill, int targetEnemyIndex)
        {
            var list = new List<EnemyInstance>();
            switch (skill.target)
            {
                case TargetType.ENEMY:
                {
                    EnemyInstance target = null;
                    if (targetEnemyIndex >= 0 && targetEnemyIndex < state.enemies.Count)
                    {
                        var t = state.enemies[targetEnemyIndex];
                        if (t != null && !t.IsDead) target = t;
                    }
                    if (target == null) target = FirstTargetableEnemy();
                    // 이끼 보호 중이면 이끼로 리다이렉트
                    if (target != null && IsProtectedByMoss(target))
                    {
                        var moss = FirstTargetableEnemy();
                        if (moss != null) target = moss;
                    }
                    if (target != null) list.Add(target);
                    break;
                }
                case TargetType.ALL_ENEMY:
                {
                    foreach (var e in state.enemies)
                    {
                        if (e == null || e.IsDead) continue;
                        if (IsProtectedByMoss(e)) continue;
                        list.Add(e);
                    }
                    break;
                }
                case TargetType.SELF:
                    // damage 적용 대상 없음 — effects만 동작
                    break;
            }
            return list;
        }

        private void ApplySkillEffects(SummonInstance summon, DinoSkillData skill, List<EnemyInstance> damageTargets)
        {
            foreach (var (key, value) in skill.effects)
            {
                switch (key)
                {
                    case "bleed":
                        foreach (var t in damageTargets)
                        {
                            if (t.IsDead) continue;
                            t.bleedStacks += value;
                            Log($"    -> {t.data.nameKr} 출혈 +{value} (총 {t.bleedStacks})");
                        }
                        break;
                    case "vulnerable":
                        foreach (var t in damageTargets)
                        {
                            if (t.IsDead) continue;
                            t.vulnerableTurns += value;
                            Log($"    -> {t.data.nameKr} 취약 +{value}T (총 {t.vulnerableTurns}T)");
                        }
                        break;
                    case "weak":
                        foreach (var t in damageTargets)
                        {
                            if (t.IsDead) continue;
                            t.weakTurns += value;
                            Log($"    -> {t.data.nameKr} 약화 +{value}T (총 {t.weakTurns}T)");
                        }
                        break;
                    case "stun":
                        foreach (var t in damageTargets)
                        {
                            if (t.IsDead) continue;
                            t.stunTurns += value;
                            Log($"    -> {t.data.nameKr} 기절 +{value}T (총 {t.stunTurns}T)");
                        }
                        break;
                    case "draw":
                        Draw(value);
                        Log($"    -> 카드 {value}장 드로우");
                        break;
                    case "self_block":
                        summon.block += value;
                        Log($"    -> {summon.data.nameKr} 보호막 +{value} (총 {summon.block})");
                        break;
                    default:
                        Log($"    ? unknown skill effect '{key}:{value}'");
                        break;
                }
            }
        }

        /// <summary>보스 페이즈 전환 체크 — HP 비율이 다음 페이즈 임계 아래로 내려가면 패턴셋 교체 + on_enter 액션.</summary>
        private void CheckBossPhaseTransition(EnemyInstance e)
        {
            if (e == null || e.IsDead) return;
            if (string.IsNullOrEmpty(e.data.phaseSetId)) return;

            var phases = DianoCard.Data.DataManager.Instance.GetPhaseSet(e.data.phaseSetId);
            if (phases == null) return;

            float hpRatio = (float)e.hp / Math.Max(1, e.maxHp);
            for (int i = e.phasesEntered; i < phases.Count; i++)
            {
                var ph = phases[i];
                if (hpRatio <= ph.enterHpRatio)
                {
                    Log($"  ★ {e.data.nameKr} 페이즈 전환 → P{ph.phase} ({ph.triggerText})");
                    e.currentPatternSetId = ph.patternSetId;
                    e.currentPhase = ph.phase;
                    e.patternStepCursor = 0;
                    e.phasesEntered = i + 1;
                    ExecuteOnEnterActions(e, ph.onEnterActions);
                }
                else break;
            }
        }

        /// <summary>"SUMMON_MOSS:4|SET_PROTECTED:1|GRACE_TURN:1" 식 액션 리스트 실행.</summary>
        private void ExecuteOnEnterActions(EnemyInstance e, string actionsStr)
        {
            if (string.IsNullOrEmpty(actionsStr)) return;
            foreach (var token in actionsStr.Split('|'))
            {
                var parts = token.Split(':');
                if (parts.Length == 0) continue;
                int arg1 = parts.Length > 1 && int.TryParse(parts[1], out var a1) ? a1 : 1;
                switch (parts[0])
                {
                    case "SUMMON":
                        for (int k = 0; k < arg1; k++) SpawnAdd(e);
                        break;
                    case "SUMMON_MOSS":
                        for (int k = 0; k < arg1; k++) SpawnMoss(e);
                        break;
                    case "REFILL_MOSS":
                        RefillMoss(e, arg1);
                        break;
                    case "SET_PROTECTED":
                        e.isBossProtected = arg1 != 0;
                        Log($"  ☆ {e.data.nameKr} 보호막 {(e.isBossProtected ? "ON" : "OFF")}");
                        break;
                    case "SET_MOSS_AGGRESSIVE":
                        e.isMossAggressive = arg1 != 0;
                        SwitchMossAggression(e.isMossAggressive);
                        break;
                    case "ABSORB_MOSS":
                        AbsorbAllMoss(e);
                        break;
                    case "ENABLE_DUAL_ACTION":
                        e.extraActionsPerTurn = arg1;
                        Log($"  ☆ {e.data.nameKr}: 이중 행동 활성 (턴당 {1 + arg1}회 행동)");
                        break;
                    case "GRACE_TURN":
                        e.graceTurnsRemaining = arg1;
                        Log($"  ☆ {e.data.nameKr} 각성 중 ({arg1}턴)");
                        break;
                    case "LOCK_SLOT":
                        // 현재 시스템 미구현 (덩굴 결박) — 로그만.
                        Log($"  ☆ on_enter: {token} (LOCK_SLOT 시스템 미구현)");
                        break;
                    default:
                        Log($"  ☆ on_enter: {token} (unknown)");
                        break;
                }
            }
        }

        /// <summary>E103 이끼 쌍둥이류 — 한 체 사망 시 같은 패턴셋의 다른 인스턴스에 ON_PARTNER_DEATH 트리거 발동.</summary>
        private void CheckPartnerDeathTrigger(EnemyInstance justDamaged)
        {
            if (justDamaged == null || !justDamaged.IsDead) return;
            // 같은 patternSetId를 쓰는 다른 살아있는 인스턴스 찾기
            foreach (var partner in state.enemies)
            {
                if (partner == justDamaged) continue;
                if (partner.IsDead) continue;
                if (partner.data.patternSetId != justDamaged.data.patternSetId) continue;

                var steps = DianoCard.Data.DataManager.Instance.GetPatternSet(partner.currentPatternSetId);
                if (steps == null) continue;
                foreach (var s in steps)
                {
                    if (s.condition == "ON_PARTNER_DEATH")
                    {
                        // 즉시 발동 (인텐트 갱신 없이 효과만 적용)
                        if (s.action == EnemyAction.BUFF_SELF)
                        {
                            partner.extraAttack += s.value;
                            Log($"  ⚡ {partner.data.nameKr} 격노! (+{s.value} ATK, now {partner.TotalAttack})");
                        }
                    }
                }
            }
        }

        /// <summary>한 마리 적의 인텐트 실행 (공격/방어 등).</summary>
        public void DoEnemyAction(EnemyInstance enemy)
        {
            if (enemy == null || enemy.IsDead) return;
            ExecuteIntent(enemy);
        }

        /// <summary>MULTI_ATTACK 분할 hit 1회 실행 — UI가 hit별로 lunge 애니메이션을 재생하기 위한 분할 진입점.
        /// 호출자가 카운트만큼 반복하고 각 hit 사이에 애니메이션을 끼워넣는다.
        /// 그레이스/기절/예고 진행 중이면 false 반환(아무것도 안 함).</summary>
        public bool DealEnemyMultiAttackHit(EnemyInstance enemy)
        {
            if (enemy == null || enemy.IsDead) return false;
            if (enemy.graceTurnsRemaining > 0) return false;
            if (enemy.stunTurns > 0) return false;
            if (enemy.telegraphRemaining > 0) return false;
            DealAttack(enemy, enemy.intentValue);
            return !state.PlayerLost;
        }

        /// <summary>턴 종료 정리: 죽은 소환수 제거(바인딩 카드 복귀), 패 버림더미로.</summary>
        public void EndTurnCleanup()
        {
            for (int i = state.field.Count - 1; i >= 0; i--)
            {
                var s = state.field[i];
                if (!s.IsDead) continue;
                ReturnBoundCard(s);
                state.field.RemoveAt(i);
            }
            foreach (var c in state.hand) state.discard.Add(c);
            state.hand.Clear();
        }

        /// <summary>필드에서 제거되는 소환수의 sourceCardInstance를 bound→discard로 이동.</summary>
        private void ReturnBoundCard(SummonInstance s)
        {
            var src = s.sourceCardInstance;
            if (src == null) return; // PURIFY 등 바인딩 없는 소환은 스킵
            if (state.bound.Remove(src))
                state.discard.Add(src);
            s.sourceCardInstance = null;
        }

        /// <summary>다음 턴 시작 (전투가 끝나지 않았을 때만).</summary>
        public void StartNextTurnIfAlive()
        {
            if (!state.IsOver) StartTurn();
        }

        private void ExecuteIntent(EnemyInstance e)
        {
            // 그레이스 턴: 본체가 각성 중 — 행동 스킵. 한 턴 소모.
            if (e.graceTurnsRemaining > 0)
            {
                e.graceTurnsRemaining--;
                Log($"  {e.data.nameKr}: 각성 중… (grace {e.graceTurnsRemaining} 남음)");
                return;
            }

            // 기절: 행동 스킵. 감소는 TickStatuses에서.
            if (e.stunTurns > 0)
            {
                Log($"  {e.data.nameKr}: 기절 ({e.stunTurns}T 남음)");
                return;
            }

            // 카운트다운 예고 중이면 발동 안 함. 예고 카운트 감소는 RollIntent에서 처리됨.
            if (e.telegraphRemaining > 0)
            {
                Log($"  {e.data.nameKr}: 예고 진행 중 (T-{e.telegraphRemaining})");
                return;
            }

            switch (e.intentAction)
            {
                case EnemyAction.ATTACK:
                    DealAttack(e, e.intentValue);
                    break;

                case EnemyAction.MULTI_ATTACK:
                    for (int i = 0; i < e.intentCount; i++)
                    {
                        DealAttack(e, e.intentValue);
                        if (state.PlayerLost) return;
                    }
                    break;

                case EnemyAction.DEFEND:
                    e.block += e.intentValue;
                    Log($"  {e.data.nameKr} defends (+{e.intentValue} block)");
                    break;

                case EnemyAction.POISON:
                    state.player.poisonStacks += e.intentValue;
                    Log($"  {e.data.nameKr} poisons player (+{e.intentValue}, total {state.player.poisonStacks})");
                    break;

                case EnemyAction.WEAK:
                    state.player.weakTurns += e.intentValue;
                    Log($"  {e.data.nameKr} weakens player (+{e.intentValue}T, total {state.player.weakTurns}T)");
                    break;

                case EnemyAction.DRAIN:
                {
                    state.player.TakeDamage(e.intentValue);
                    int healed = Math.Min(e.intentValue, e.maxHp - e.hp);
                    e.hp += healed;
                    Log($"  {e.data.nameKr} drains {e.intentValue} from PLAYER, heals self +{healed}");
                    break;
                }

                case EnemyAction.SUMMON:
                    for (int i = 0; i < e.intentValue; i++) SpawnAdd(e);
                    break;

                case EnemyAction.REFILL_MOSS:
                    RefillMoss(e, e.intentValue);
                    break;

                case EnemyAction.STEAL_SUMMON:
                    for (int i = 0; i < Math.Max(1, e.intentValue); i++)
                    {
                        if (!StealPlayerSummon(e)) break;
                    }
                    break;

                case EnemyAction.VULNERABLE:
                    state.player.vulnerableTurns += e.intentValue;
                    Log($"  {e.data.nameKr} inflicts VULNERABLE (+{e.intentValue}T, total {state.player.vulnerableTurns}T)");
                    break;

                case EnemyAction.ARMOR_UP:
                {
                    // "set-to-max" 시맨틱 — 같은 값으로 반복 사용해도 스택 누적되지 않음.
                    // 더 높은 값으로 업그레이드는 가능 (ARMOR_UP 2 → ARMOR_UP 4).
                    int newArmor = Math.Max(e.extraBlockPerTurn, e.intentValue);
                    if (newArmor > e.extraBlockPerTurn)
                    {
                        e.extraBlockPerTurn = newArmor;
                        e.block = Math.Max(e.block, e.extraBlockPerTurn);
                        Log($"  {e.data.nameKr} 장갑 강화 (매 턴 +{e.extraBlockPerTurn})");
                    }
                    break;
                }

                case EnemyAction.CLOG_DECK:
                    AddClogCards(Math.Max(1, e.intentValue));
                    break;

                case EnemyAction.SILENCE:
                {
                    int n = Math.Max(1, e.intentValue);
                    int affected = 0;
                    foreach (var s in state.field)
                    {
                        if (s.IsDead) continue;
                        s.silencedTurns += n;
                        affected++;
                    }
                    Log($"  ⚠ {e.data.nameKr}가 모든 공룡을 침묵시킴 (+{n}T, {affected}체)");
                    break;
                }

                case EnemyAction.BUFF_SELF:
                    e.extraAttack += e.intentValue;
                    Log($"  {e.data.nameKr} buffs self (+{e.intentValue} ATK, now {e.TotalAttack})");
                    break;

                case EnemyAction.COUNTDOWN_ATTACK:
                    DealAttack(e, e.intentValue);
                    Log($"  ⚡ {e.data.nameKr} 카운트다운 강타 발동! ({e.intentValue})");
                    break;

                case EnemyAction.COUNTDOWN_AOE:
                {
                    int aoeDmg = Math.Max(1, (int)Math.Round(e.intentValue * e.damageScale));
                    Log($"  ⚡ {e.data.nameKr} 카운트다운 광역 발동! ({aoeDmg})");
                    state.player.TakeDamage(aoeDmg);
                    foreach (var s in state.field) s.TakeDamage(aoeDmg);
                    break;
                }

                case EnemyAction.IDLE:
                    // 이끼 수호 상태 등 — 행동하지 않음
                    break;

                case EnemyAction.HEAL_BOSS:
                {
                    var boss = GetMossSummoner(e);
                    if (boss != null && !boss.IsDead)
                    {
                        int healed = Math.Min(e.intentValue, boss.maxHp - boss.hp);
                        boss.hp += healed;
                        Log($"  🟢 {e.data.nameKr} → {boss.data.nameKr} HP +{healed} (이제 {boss.hp}/{boss.maxHp})");
                    }
                    break;
                }

                case EnemyAction.EMPOWER_BOSS:
                {
                    var boss = GetMossSummoner(e);
                    if (boss != null && !boss.IsDead)
                    {
                        boss.extraAttack += e.intentValue;
                        Log($"  🟣 {e.data.nameKr} → {boss.data.nameKr} 다음 어택 +{e.intentValue} (총 +{boss.extraAttack})");
                    }
                    break;
                }

                case EnemyAction.BLOCK_BOSS:
                {
                    var boss = GetMossSummoner(e);
                    if (boss != null && !boss.IsDead)
                    {
                        boss.block += e.intentValue;
                        Log($"  🟡 {e.data.nameKr} → {boss.data.nameKr} 블록 +{e.intentValue} (총 {boss.block})");
                    }
                    break;
                }

                default:
                    Log($"  {e.data.nameKr}: unknown action {e.intentAction}");
                    break;
            }
        }

        /// <summary>
        /// 적 단발 공격 — 인텐트 롤 시점(RollIntent)에 확정된 attacker.intentTargetDino를 그대로 때림.
        /// null이면 플레이어. 확정된 공룡이 이미 죽었거나 필드에서 빠졌으면 플레이어로 폴백.
        /// 단, 도발 활성 공룡이 있으면 실행 시점에 그쪽으로 강제 리다이렉트(C108 도발이
        /// 인텐트 롤 이후에 발동돼도 같은 턴에 효과가 적용되도록).
        /// </summary>
        private void DealAttack(EnemyInstance attacker, int rawDmg)
        {
            // floor 스케일링 — 일반 적은 layer 깊어질수록 데미지 증가. 엘리트/보스는 1.0.
            int scaled = Math.Max(1, (int)Math.Round(rawDmg * attacker.damageScale));
            // 약화 상태면 피해 25% 감소 (C129 포효 등의 효과)
            int dmg = attacker.weakTurns > 0 ? Math.Max(1, (int)(scaled * 0.75f)) : scaled;

            // 도발 우선 — 살아있는 첫 도발 공룡이 있으면 인텐트 타겟을 무시하고 그쪽으로 끌어감.
            SummonInstance taunting = null;
            foreach (var s in state.field)
            {
                if (s.IsTaunting) { taunting = s; break; }
            }

            var target = taunting ?? attacker.intentTargetDino;
            // 타겟 유효성 재확인 — 사망 / 필드 이탈 시 플레이어로 폴백.
            if (target != null && (target.IsDead || !state.field.Contains(target)))
                target = null;

            if (target == null)
            {
                state.player.TakeDamage(dmg);
                Log($"  {attacker.data.nameKr} → PLAYER {dmg} (HP {state.player.hp}, block {state.player.block})");
            }
            else
            {
                target.TakeDamage(dmg);
                string flavor = target.IsTaunting ? "[도발] " : "";
                Log($"  {attacker.data.nameKr} → {target.data.nameKr} {flavor}{dmg} (HP {target.hp})");
                if (target.IsDead)
                {
                    Log($"    x {target.data.nameKr} defeated");
                    ReturnBoundCard(target);
                    state.field.Remove(target);
                }
            }
        }

        private void SpawnAdd(EnemyInstance summoner)
        {
            var addData = new EnemyData
            {
                id = "ADD_" + summoner.data.id,
                nameKr = "쫄",
                nameEn = "Add",
                enemyType = EnemyType.NORMAL,
                chapter = summoner.data.chapter,
                hp = 8,
                attack = 3,
                defense = 0,
                patternSetId = "PS_ADD",
                phaseSetId = "",
            };
            var add = new EnemyInstance(addData);
            state.enemies.Add(add);
            // 쫄도 인텐트를 즉시 결정해서 Intent UI에 노출
            RollIntent(add);
            Log($"  {summoner.data.nameKr} summons {addData.nameKr} (HP {addData.hp}/ATK {addData.attack})");
        }

        /// <summary>이끼 쫄 1체 소환. PS_MOSS_E901 5종 행동(공격/힐/엠파워/블록/화상) 매 턴 랜덤.</summary>
        private void SpawnMoss(EnemyInstance summoner)
        {
            var mossData = new EnemyData
            {
                id = "MOSS_" + summoner.data.id,
                nameKr = "이끼",
                nameEn = "Moss",
                enemyType = EnemyType.NORMAL,
                chapter = summoner.data.chapter,
                hp = 5,
                attack = 2,
                defense = 0,
                patternSetId = "PS_MOSS_E901",  // 5종 행동(공격/힐/엠파워/블록/화상) 매 턴 랜덤
                phaseSetId = "",
                image = "E901_Moss_left_up.png", // 기본값 — 실제 스프라이트는 BattleUI.ComputeSlotPositions에서 코너별로 스왑

            };
            var moss = new EnemyInstance(mossData);
            moss.isMoss = true;
            state.enemies.Add(moss);
            RollIntent(moss);
            Log($"  {summoner.data.nameKr} 이끼 소환 (HP 5, 5종 행동 랜덤)");
        }

        /// <summary>모스 정령의 소환자(보스) 인스턴스 반환. data.id "MOSS_E901" → "E901" 검색.</summary>
        private EnemyInstance GetMossSummoner(EnemyInstance moss)
        {
            if (!moss.isMoss) return null;
            string summonerID = moss.data.id.StartsWith("MOSS_") ? moss.data.id.Substring(5) : null;
            if (string.IsNullOrEmpty(summonerID)) return null;
            foreach (var x in state.enemies)
                if (!x.IsDead && x.data.id == summonerID) return x;
            return null;
        }

        /// <summary>살아있는 이끼 수를 target까지 채움 (부족한 만큼만 소환).</summary>
        private void RefillMoss(EnemyInstance summoner, int target)
        {
            int alive = 0;
            foreach (var x in state.enemies) if (!x.IsDead && x.isMoss) alive++;
            int need = target - alive;
            if (need <= 0)
            {
                Log($"  {summoner.data.nameKr} 이끼 리필: 이미 {alive}체 (보충 없음)");
                return;
            }
            Log($"  {summoner.data.nameKr} 이끼 {need}체 보충 ({alive} → {target})");
            for (int k = 0; k < need; k++) SpawnMoss(summoner);
        }

        /// <summary>살아있는 이끼들의 패턴셋을 공격/수호 중 하나로 전환.</summary>
        private void SwitchMossAggression(bool aggressive)
        {
            string newPattern = aggressive ? "PS_MOSS_ATTACK" : "PS_MOSS_PASSIVE";
            foreach (var m in state.enemies)
            {
                if (m.IsDead || !m.isMoss) continue;
                m.currentPatternSetId = newPattern;
                m.patternStepCursor = 0;
                m.telegraphRemaining = 0;
                // 공격 활성화 시 데미지 부여 (data.attack은 불변이라 extraAttack으로 보정)
                if (aggressive && m.data.attack == 0) m.extraAttack = 2;
                RollIntent(m);
            }
            Log($"  ★ 이끼 전원 {(aggressive ? "공격 개시" : "수호 복귀")}");
        }

        /// <summary>CLOG_DECK: 플레이어 버림더미에 "잡초"(C901) N장 추가. 다음 셔플에 섞여 드로우 낭비.</summary>
        private void AddClogCards(int count)
        {
            var clog = DianoCard.Data.DataManager.Instance.GetCard("C901");
            if (clog == null)
            {
                Log($"  [WARN] CLOG_DECK: C901(잡초) 카드를 찾을 수 없음 — card.csv 확인 필요");
                return;
            }
            for (int i = 0; i < count; i++)
                state.discard.Add(new CardInstance(clog));
            Log($"  ⚠ 플레이어 버림더미에 잡초 {count}장 추가 (총 {state.discard.Count}장)");
        }

        /// <summary>P3 거대화: 살아있는 이끼 전원 즉사 + 본체 고정 강화 (ATK +8, 매 턴 방어도 +6), 보호막 해제.</summary>
        private void AbsorbAllMoss(EnemyInstance boss)
        {
            const int atkBonus = 8;
            const int blockPerTurn = 6;

            int absorbed = 0;
            foreach (var m in state.enemies)
            {
                if (!m.IsDead && m.isMoss)
                {
                    m.hp = 0;   // 즉시 사망 처리 (필드에서 제거는 다음 cleanup에서)
                    absorbed++;
                }
            }
            boss.extraAttack += atkBonus;
            boss.extraBlockPerTurn += blockPerTurn;
            boss.block = Math.Max(boss.block, boss.extraBlockPerTurn);  // 즉시 장갑 적용
            boss.isBossProtected = false;   // 이끼 없어져 보호막 의미 없음
            Log($"  ⚡ {boss.data.nameKr} 이끼 {absorbed}체 흡수 — ATK +{atkBonus} (now {boss.TotalAttack}) / 매 턴 방어도 +{blockPerTurn} / 보호막 해제");
        }

        /// <summary>플레이어 필드에서 소환수 1체를 빼앗아 적 편에 편입. 필드가 비면 false 반환.</summary>
        private bool StealPlayerSummon(EnemyInstance thief)
        {
            if (state.field.Count == 0)
            {
                Log($"  {thief.data.nameKr} 공룡 강탈 실패 — 필드에 공룡이 없음");
                return false;
            }
            // 가장 앞(오래된) 소환수 1체 선택. 빼앗긴 공룡의 카드는 즉시 discard로 복귀(다시 드로우 가능).
            var victim = state.field[0];
            ReturnBoundCard(victim);
            state.field.RemoveAt(0);

            var stolenData = new EnemyData
            {
                id = "STOLEN_" + victim.data.id,
                nameKr = "홀린 " + victim.data.nameKr,
                nameEn = "Corrupted " + victim.data.nameEn,
                enemyType = EnemyType.NORMAL,
                chapter = thief.data.chapter,
                hp = Math.Max(1, victim.hp),
                attack = Math.Max(3, victim.TotalAttack),
                defense = 0,
                patternSetId = "PS_STOLEN_DINO",
                phaseSetId = "",
            };
            var stolen = new EnemyInstance(stolenData);
            stolen.stolenFromCard = victim.data;
            state.enemies.Add(stolen);
            RollIntent(stolen);
            Log($"  ⚡ {thief.data.nameKr}가 {victim.data.nameKr}을(를) 빼앗아갔다! (HP {stolen.hp} / ATK {stolen.data.attack})");
            return true;
        }

        /// <summary>정화(PURIFY) 카드 효과 — 빼앗긴 공룡을 모두 필드로 되돌리고 플레이어 독/약화 해제.</summary>
        private void ResolvePurify(CardData c)
        {
            int restored = 0;
            // 역순 순회 — 제거하면서 순회 안전.
            for (int i = state.enemies.Count - 1; i >= 0; i--)
            {
                var e = state.enemies[i];
                if (!e.IsStolen || e.IsDead) continue;
                if (state.field.Count >= state.maxFieldSize) break; // 필드 꽉 차면 추가 못 함

                var summon = new SummonInstance(e.stolenFromCard) { hp = Math.Max(1, e.hp) };
                state.field.Add(summon);
                state.enemies.RemoveAt(i);
                restored++;
                Log($"    -> 정화: {e.data.nameKr}({e.hp}HP) → 필드 복귀");
            }
            // 플레이어 디버프 해제
            int clearedPoison = state.player.poisonStacks;
            int clearedWeak = state.player.weakTurns;
            state.player.poisonStacks = 0;
            state.player.weakTurns = 0;
            if (restored == 0 && clearedPoison == 0 && clearedWeak == 0)
                Log($"    -> 정화: 해제할 대상 없음");
            else
                Log($"    -> 정화 완료: 공룡 {restored}체 복귀, 독 -{clearedPoison}, 약화 -{clearedWeak}T");
        }

        /// <summary>현재 보호막이 활성인지 — 본체가 protected이고 살아있는 이끼가 있을 때.</summary>
        private bool IsProtectedByMoss(EnemyInstance e)
        {
            if (e == null || !e.isBossProtected) return false;
            foreach (var m in state.enemies) if (!m.IsDead && m.isMoss) return true;
            return false;
        }

        /// <summary>패턴셋의 다음 스텝을 읽어 인텐트 세팅. condition을 만족하지 못하면 스텝을 건너뜀.</summary>
        private void RollIntent(EnemyInstance e)
        {
            // 각성(grace) 중: 인텐트는 IDLE 로 고정 노출, 스텝 cursor는 advance 하지 않음.
            if (e.graceTurnsRemaining > 0)
            {
                e.intentAction = EnemyAction.IDLE;
                e.intentType = EnemyIntentType.UNKNOWN;
                e.intentValue = 0;
                e.intentCount = 1;
                e.telegraphRemaining = 0;
                e.intentIcon = "GRACE";
                return;
            }

            // 카운트다운 예고 중: 기존 인텐트 유지, remaining을 1 감소.
            // (ExecuteIntent에서는 감소하지 않으므로 여기서만 감소 → fire 턴에 0에 도달해 발동.)
            if (e.telegraphRemaining > 0)
            {
                e.telegraphRemaining--;
                return;
            }

            var patternId = e.currentPatternSetId;
            if (string.IsNullOrEmpty(patternId)) patternId = e.data.patternSetId;

            var steps = DianoCard.Data.DataManager.Instance.GetPatternSet(patternId);
            if (steps == null || steps.Count == 0)
            {
                // 폴백 — 데이터 누락 시 그냥 ATTACK
                e.intentAction = EnemyAction.ATTACK;
                e.intentType = EnemyIntentType.ATTACK;
                e.intentValue = e.TotalAttack;
                e.intentCount = 1;
                e.telegraphRemaining = 0;
                e.intentIcon = "ATTACK";
                return;
            }

            // step_order=99 같은 트리거 전용 스텝(ON_PARTNER_DEATH 등)은 정상 사이클에서 제외
            // 사이클은 stepOrder 1..N 순환. 99 이상은 트리거로 분류.
            int cycleCount = 0;
            foreach (var s in steps) if (s.stepOrder < 90) cycleCount++;
            if (cycleCount == 0) cycleCount = steps.Count;

            EnemyPatternData chosen = null;

            // 이끼 정령(isMoss): cycle 대신 매 턴 랜덤 step 선택 — 4체 정령이 각자 독립적으로
            // 매 턴 5종 행동 중 무엇을 할지 모르는 우선순위 결정 게임플레이.
            if (e.isMoss)
            {
                var pool = new System.Collections.Generic.List<EnemyPatternData>();
                foreach (var s in steps)
                    if (s.stepOrder < 90 && CheckCondition(s.condition, e)) pool.Add(s);
                if (pool.Count > 0)
                    chosen = pool[UnityEngine.Random.Range(0, pool.Count)];
            }
            else
            {
                // condition 충족 안 되면 다음 스텝으로 (최대 cycleCount번 시도해 무한루프 방지)
                for (int tries = 0; tries < cycleCount; tries++)
                {
                    int idx = e.patternStepCursor % cycleCount;
                    e.patternStepCursor++;
                    var candidate = StepAt(steps, idx);
                    if (candidate == null) continue;
                    if (CheckCondition(candidate.condition, e))
                    {
                        chosen = candidate;
                        break;
                    }
                    Log($"  {e.data.nameKr}: skip step {candidate.action} (condition '{candidate.condition}' not met)");
                }
            }
            if (chosen == null) chosen = StepAt(steps, 0);

            e.intentAction = chosen.action;
            e.intentValue = chosen.value;
            e.intentCount = Math.Max(1, chosen.count);
            e.intentTarget = chosen.target;
            e.intentIcon = chosen.intentIcon;
            e.telegraphRemaining = chosen.telegraphTurns > 0 ? chosen.telegraphTurns - 1 : 0;
            e.intentType = MapIntentType(chosen.action, chosen.intentIcon);

            // 공격 계열 인텐트는 턴 시작 시 타겟 확정 — DealAttack에서 재롤 안 함.
            e.intentTargetDino = IsSingleTargetAttackAction(chosen.action) ? RollAttackTarget() : null;
        }

        private static bool IsSingleTargetAttackAction(EnemyAction a)
        {
            return a == EnemyAction.ATTACK
                || a == EnemyAction.MULTI_ATTACK
                || a == EnemyAction.COUNTDOWN_ATTACK;
        }

        /// <summary>단일 공격 타겟 롤 — 도발 > 필드 비었으면 플레이어 > 30% 플레이어 / 70% 랜덤 공룡.</summary>
        private SummonInstance RollAttackTarget()
        {
            // 1) 도발 우선
            foreach (var s in state.field)
            {
                if (s.IsTaunting) return s;
            }
            // 2) 필드 비어있으면 플레이어(null)
            var alive = new List<SummonInstance>();
            foreach (var s in state.field) if (!s.IsDead) alive.Add(s);
            if (alive.Count == 0) return null;
            // 3) 가중 랜덤 — 50% 플레이어 / 50% 랜덤 공룡
            if (_rng.NextDouble() < 0.50) return null;
            return alive[_rng.Next(alive.Count)];
        }

        /// <summary>cycleStep 인덱스 idx에 해당하는 stepOrder<90인 스텝 반환.</summary>
        private EnemyPatternData StepAt(System.Collections.Generic.List<EnemyPatternData> steps, int idx)
        {
            int seen = 0;
            foreach (var s in steps)
            {
                if (s.stepOrder >= 90) continue;
                if (seen == idx) return s;
                seen++;
            }
            return null;
        }

        private bool CheckCondition(string condition, EnemyInstance e)
        {
            if (string.IsNullOrEmpty(condition)) return true;
            switch (condition)
            {
                case "ADD_DEAD":
                    // 자기 외에 살아있는 쫄이 없을 때
                    foreach (var other in state.enemies)
                        if (other != e && !other.IsDead && other.data.id.StartsWith("ADD_")) return false;
                    return true;
                case "ADD_ALIVE":
                    foreach (var other in state.enemies)
                        if (other != e && !other.IsDead && other.data.id.StartsWith("ADD_")) return true;
                    return false;
                case "MOSS_FULL":
                {
                    int alive = 0;
                    foreach (var x in state.enemies) if (!x.IsDead && x.isMoss) alive++;
                    return alive >= 4;
                }
                case "MOSS_NOT_FULL":
                {
                    int alive = 0;
                    foreach (var x in state.enemies) if (!x.IsDead && x.isMoss) alive++;
                    return alive < 4;
                }
                case "MOSS_EMPTY":
                {
                    foreach (var x in state.enemies) if (!x.IsDead && x.isMoss) return false;
                    return true;
                }
                default:
                    // ON_PARTNER_DEATH 등 트리거성은 RollIntent에서 자동 처리되지 않음 (별도 훅 필요)
                    return true;
            }
        }

        private EnemyIntentType MapIntentType(EnemyAction action, string iconHint)
        {
            return action switch
            {
                EnemyAction.ATTACK            => EnemyIntentType.ATTACK,
                EnemyAction.MULTI_ATTACK      => EnemyIntentType.ATTACK,
                EnemyAction.DEFEND            => EnemyIntentType.DEFEND,
                EnemyAction.POISON            => EnemyIntentType.DEBUFF,
                EnemyAction.WEAK              => EnemyIntentType.DEBUFF,
                EnemyAction.DRAIN             => EnemyIntentType.DEBUFF,
                EnemyAction.SUMMON            => EnemyIntentType.SUMMON,
                EnemyAction.REFILL_MOSS       => EnemyIntentType.SUMMON,
                EnemyAction.STEAL_SUMMON      => EnemyIntentType.DEBUFF,
                EnemyAction.VULNERABLE        => EnemyIntentType.DEBUFF,
                EnemyAction.ARMOR_UP          => EnemyIntentType.DEFEND,
                EnemyAction.CLOG_DECK         => EnemyIntentType.DEBUFF,
                EnemyAction.SILENCE           => EnemyIntentType.DEBUFF,
                EnemyAction.BUFF_SELF         => EnemyIntentType.BUFF,
                EnemyAction.COUNTDOWN_ATTACK  => EnemyIntentType.COUNTDOWN,
                EnemyAction.COUNTDOWN_AOE     => EnemyIntentType.COUNTDOWN,
                EnemyAction.IDLE              => EnemyIntentType.UNKNOWN,
                EnemyAction.HEAL_BOSS         => EnemyIntentType.BUFF,
                EnemyAction.EMPOWER_BOSS      => EnemyIntentType.BUFF,
                EnemyAction.BLOCK_BOSS        => EnemyIntentType.DEFEND,
                _                             => EnemyIntentType.UNKNOWN,
            };
        }

        // =========================================================
        // 덱 조작
        // =========================================================

        private void Draw(int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (state.deck.Count == 0)
                {
                    if (state.discard.Count == 0) return;
                    state.deck.AddRange(state.discard);
                    state.discard.Clear();
                    Shuffle(state.deck);
                    Log("  (reshuffled discard -> deck)");
                }
                var top = state.deck[0];
                state.deck.RemoveAt(0);
                state.hand.Add(top);
            }
        }

        private void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        // =========================================================
        // 유틸
        // =========================================================

        private EnemyInstance FirstAliveEnemy()
        {
            foreach (var e in state.enemies) if (!e.IsDead) return e;
            return null;
        }

        /// <summary>플레이어가 때릴 수 있는 첫 적 — 이끼 보호 중인 본체는 건너뜀.</summary>
        private EnemyInstance FirstTargetableEnemy()
        {
            // 1차: 이끼 우선 (WoW 방식 — 이끼가 살아있으면 이끼부터)
            foreach (var e in state.enemies) if (!e.IsDead && e.isMoss) return e;
            // 2차: 보호 안 받는 적
            foreach (var e in state.enemies) if (!e.IsDead && !IsProtectedByMoss(e)) return e;
            return null;
        }

        private EnemyInstance RandomAliveEnemy()
        {
            var alive = new List<EnemyInstance>();
            foreach (var e in state.enemies) if (!e.IsDead) alive.Add(e);
            if (alive.Count == 0) return null;
            return alive[_rng.Next(alive.Count)];
        }

        /// <summary>RANDOM 타겟용 — 보호 받는 본체 제외.</summary>
        private EnemyInstance RandomTargetableEnemy()
        {
            var alive = new List<EnemyInstance>();
            foreach (var e in state.enemies)
                if (!e.IsDead && !IsProtectedByMoss(e)) alive.Add(e);
            if (alive.Count == 0) return null;
            return alive[_rng.Next(alive.Count)];
        }

        /// <summary>자동 공격용 랜덤 타겟 인덱스. 이끼 보호 중엔 이끼 우선, 없으면 -1.</summary>
        public int PickRandomTargetIndex()
        {
            var t = RandomTargetableEnemy();
            if (t == null) return -1;
            return state.enemies.IndexOf(t);
        }

        // =========================================================
        // 로그 출력
        // =========================================================

        // =========================================================
        // 치트 / 훈련 도우미 — 전투 중에만 의미 있음.
        // =========================================================

        /// <summary>모든 적(보스·이끼·홀린 공룡·쫄 포함) HP=0 처리. 다음 프레임에 전투 승리 감지됨.</summary>
        public void Cheat_KillAllEnemies()
        {
            if (state == null) return;
            int n = 0;
            foreach (var e in state.enemies)
            {
                if (e.IsDead) continue;
                e.hp = 0;
                n++;
            }
            Log($"[CHEAT] 전체 처치: {n}체 즉사");
        }

        /// <summary>보스(첫 번째 살아있는 주 적)만 남기고 이끼/쫄/홀린 공룡 전부 처치.</summary>
        public void Cheat_ClearAddsOnly()
        {
            if (state == null) return;
            int n = 0;
            foreach (var e in state.enemies)
            {
                if (e.IsDead) continue;
                // "쫄류"로 간주: isMoss / IsStolen / id가 ADD_ / STOLEN_ / MOSS_ 로 시작
                bool isAdd = e.isMoss || e.IsStolen
                    || (e.data?.id != null && (e.data.id.StartsWith("ADD_") || e.data.id.StartsWith("MOSS_") || e.data.id.StartsWith("STOLEN_")));
                if (!isAdd) continue;
                e.hp = 0;
                n++;
            }
            Log($"[CHEAT] 쫄류 일소: {n}체 처치");
        }

        /// <summary>첫 번째 적(보스 가정)의 HP를 maxHp의 ratio 비율로 설정. 다음 StartTurn에서 페이즈 전환 감지.</summary>
        public void Cheat_SetPrimaryHpRatio(float ratio)
        {
            if (state == null) return;
            EnemyInstance boss = null;
            foreach (var e in state.enemies)
            {
                if (e.IsDead) continue;
                if (e.isMoss || e.IsStolen) continue;
                if (e.data?.id?.StartsWith("ADD_") == true) continue;
                boss = e; break;
            }
            if (boss == null) { Log("[CHEAT] 보스 대상 없음"); return; }
            int newHp = Mathf.Max(1, Mathf.RoundToInt(boss.maxHp * Mathf.Clamp01(ratio)));
            boss.hp = newHp;
            Log($"[CHEAT] {boss.data.nameKr} HP → {newHp}/{boss.maxHp} (≈ {ratio * 100f:F0}%) — 페이즈는 다음 턴에 갱신");
        }

        /// <summary>플레이어 HP/마나 풀 보충 + 독·약화·취약 해제 + 필드·패 유지. 훈련용.</summary>
        public void Cheat_FullHeal()
        {
            if (state?.player == null) return;
            state.player.hp = state.player.maxHp;
            state.player.block = 0;
            state.player.mana = state.player.maxMana;
            state.player.poisonStacks = 0;
            state.player.weakTurns = 0;
            state.player.vulnerableTurns = 0;
            Log("[CHEAT] 플레이어 풀 회복");
        }

        /// <summary>플레이어 무적 토글 — on일 때 모든 피해 무시.</summary>
        public void Cheat_ToggleInvincible()
        {
            if (state?.player == null) return;
            state.player.cheatInvincible = !state.player.cheatInvincible;
            Log($"[CHEAT] 무적 {(state.player.cheatInvincible ? "ON" : "OFF")}");
        }

        /// <summary>치트: 지정 슬롯(0=1번, 1=2번)에 cardId의 SummonInstance를 강제 배치.
        /// 이미 차 있으면 덮어쓰기(바인딩 카드 복귀), 비어있고 maxFieldSize 미만이면 추가.
        /// 슬롯이 field.Count보다 클 경우엔 다음 빈 자리에 추가.</summary>
        public void Cheat_SetFieldSlot(int slotIndex, string cardId)
        {
            if (state == null) return;
            var data = DataManager.Instance.GetCard(cardId);
            if (data == null) { Log($"[CHEAT] {cardId} 카드 없음"); return; }
            if (data.cardType != CardType.SUMMON) { Log($"[CHEAT] {cardId}는 SUMMON 카드가 아님"); return; }

            int target = Mathf.Clamp(slotIndex, 0, state.maxFieldSize - 1);
            var summon = new SummonInstance(data); // sourceCardInstance = null → 죽어도 카드 복귀 없음

            if (target < state.field.Count)
            {
                ReturnBoundCard(state.field[target]);
                state.field[target] = summon;
                Log($"[CHEAT] 슬롯 {target + 1} → {data.nameKr} (덮어쓰기)");
            }
            else if (state.field.Count < state.maxFieldSize)
            {
                state.field.Add(summon);
                Log($"[CHEAT] 슬롯 {state.field.Count} → {data.nameKr} (추가)");
            }
            else
            {
                Log($"[CHEAT] 필드 꽉 참 — 추가 불가");
            }
        }

        /// <summary>치트: 지정 슬롯의 소환수를 제거. 바인딩 카드는 discard로 복귀.</summary>
        public void Cheat_ClearFieldSlot(int slotIndex)
        {
            if (state == null) return;
            if (slotIndex < 0 || slotIndex >= state.field.Count) return;
            var s = state.field[slotIndex];
            ReturnBoundCard(s);
            state.field.RemoveAt(slotIndex);
            Log($"[CHEAT] 슬롯 {slotIndex + 1} 비움 ({s.data.nameKr})");
        }

        public void LogState()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"  [Player] HP {state.player.hp}/{state.player.maxHp}  Block {state.player.block}  Mana {state.player.mana}/{state.player.maxMana}");

            sb.Append("  [Enemies] ");
            bool any = false;
            foreach (var e in state.enemies)
            {
                if (e.IsDead) continue;
                sb.Append($"{e.data.nameKr}(HP {e.hp}, {e.IntentLabel}) ");
                any = true;
            }
            if (!any) sb.Append("(none)");
            sb.AppendLine();

            sb.Append("  [Field] ");
            if (state.field.Count == 0) sb.Append("(empty)");
            foreach (var s in state.field)
                sb.Append($"{s.data.nameKr}(ATK {s.TotalAttack}/HP {s.hp}) ");
            sb.AppendLine();

            sb.Append($"  [Hand {state.hand.Count}] ");
            for (int i = 0; i < state.hand.Count; i++)
            {
                var c = state.hand[i].data;
                sb.Append($"{i}:{c.nameKr}({c.cost}) ");
            }
            sb.AppendLine();
            sb.Append($"  Deck {state.deck.Count} / Discard {state.discard.Count} / Bound {state.bound.Count}");
            Log(sb.ToString());
        }

        private void Log(string msg)
        {
            Debug.Log(msg);
        }
    }
}
