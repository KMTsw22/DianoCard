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

        public void StartBattle(List<CardData> startingDeck, List<EnemyData> enemyPool, int maxMana = 3, int playerHp = 70, int maxFieldSize = 2)
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
            var idCount = new Dictionary<string, int>();
            foreach (var e in enemyPool)
            {
                if (e == null) continue;
                int dupIdx = idCount.TryGetValue(e.id, out var n) ? n : 0;
                idCount[e.id] = dupIdx + 1;
                var inst = new EnemyInstance(e);
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

            // 소환수 상태 리셋 — 한 턴 버프 / 공격 가능 플래그 / 방어도
            foreach (var s in state.field)
            {
                s.tempAttackBonus = 0;
                s.hasAttackedThisTurn = false;
                s.block = 0;
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
        /// <param name="allyTargetIndex">ALLY 타겟 카드(수호 마법, 먹이 등)의 대상 공룡 인덱스. ALL_ALLY는 무시.</param>
        /// <returns>성공적으로 사용했으면 true</returns>
        public bool PlayCard(int handIndex, int targetEnemyIndex = -1, int swapFieldIndex = -1, int allyTargetIndex = -1)
        {
            if (handIndex < 0 || handIndex >= state.hand.Count) return false;

            var inst = state.hand[handIndex];
            var card = inst.data;

            // SUMMON 카드는 summonCostReduction 적용 (C132 동족 소환 등)
            int effectiveCost = card.cardType == CardType.SUMMON
                ? Math.Max(0, card.cost - state.player.summonCostReduction)
                : card.cost;
            if (state.player.mana < effectiveCost)
            {
                Log($"  ! Cannot play {card.nameKr}: need {effectiveCost} mana (have {state.player.mana})");
                return false;
            }

            // 덮어쓰기/합성 사전 판정 — 같은 id SUMMON이 이미 필드에 있으면 슬롯 체크를 건너뛰고
            // ResolveCard 단계에서 스택 증가 + 진화로 처리. (초식=덮어쓰기, 육식=합성)
            bool isOverwrite = card.cardType == CardType.SUMMON && FindOverwriteTarget(card.id) != null;

            // 필드 슬롯 제한 — 꽉 차면 swapFieldIndex로 교체 가능. 미지정이면 블록. (덮어쓰기/합성은 슬롯 불필요)
            bool needsSwap = card.cardType == CardType.SUMMON && !isOverwrite && state.field.Count >= state.maxFieldSize;
            if (needsSwap)
            {
                if (swapFieldIndex < 0 || swapFieldIndex >= state.field.Count)
                {
                    Log($"  ! Cannot play {card.nameKr}: field full ({state.field.Count}/{state.maxFieldSize}) — swap target required");
                    return false;
                }
                var swapped = state.field[swapFieldIndex];
                Log($"  >> Swapped out {swapped.data.nameKr}");
                ReturnBoundCard(swapped);
                state.field.RemoveAt(swapFieldIndex);
            }

            // 수호 마법 단일 타겟(MAGIC/DEFENSE + ALLY): 대상 필요
            if (card.target == TargetType.ALLY
                && card.cardType == CardType.MAGIC && card.subType == CardSubType.DEFENSE)
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
                        var existing = FindOverwriteTarget(c.id);
                        if (existing != null)
                        {
                            existing.stacks++;
                            if (c.subType == CardSubType.HERBIVORE)
                            {
                                // 초식: 같은 공룡이 그대로 강해짐 — 카드 형태 불변.
                                // 스탯 영구 상승. HP 보너스는 현재 HP에도 더해짐(풀 회복 아님 — 기존 상처는 유지).
                                const int atkGain = 1;
                                const int hpGain = 3;
                                existing.attack += atkGain;
                                existing.maxHp += hpGain;
                                existing.hp += hpGain;
                                Log($"    🌿 덮어쓰기! {c.nameKr} +{atkGain} ATK / +{hpGain} HP (now ATK {existing.attack} / HP {existing.hp}/{existing.maxHp})");
                            }
                            else // CARNIVORE
                            {
                                // 육식: 각 합성마다 작은 스탯 상승(진화 사이사이 성장감) + 진화 체크.
                                // HP 보너스는 현재 HP에도 더해짐 + 최소 50% HP 보장 (합성 보상 — "두 몸의 생명력 재조합" 플레이버).
                                const int carnAtkPerStack = 1;
                                const int carnHpPerStack = 1;
                                existing.attack += carnAtkPerStack;
                                existing.maxHp += carnHpPerStack;
                                existing.hp = Math.Max(existing.hp + carnHpPerStack, existing.maxHp / 2);
                                Log($"    🦖 합성! {c.nameKr} +{carnAtkPerStack}/+{carnHpPerStack}, 스택 {existing.stacks} (ATK {existing.attack} / HP {existing.hp}/{existing.maxHp})");
                                CheckEvolution(existing);
                            }
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

        /// <summary>현재 카드 id에서 스택 임계치 충족 시 즉시 진화. 여러 티어 동시 충족 가능(연속 진화).</summary>
        private void CheckEvolution(SummonInstance s)
        {
            while (true)
            {
                var evo = DataManager.Instance.GetEvolution(s.data.id);
                if (evo == null) return; // 더 이상 진화 경로 없음 (최종 형태)
                if (s.stacks < evo.stacksRequired) return;
                var next = DataManager.Instance.GetCard(evo.resultCardId);
                if (next == null)
                {
                    Log($"    [WARN] Evolution target card missing: {evo.resultCardId}");
                    return;
                }
                EvolveSummon(s, next);
            }
        }

        private void EvolveSummon(SummonInstance s, CardData next)
        {
            // 진화 전 스택 누적으로 쌓인 보너스(현재 스탯 - 현재 data 기준)를 보존해서 새 형태 위에 얹음.
            int attackBonus = s.attack - s.data.attack;
            int maxHpBonus = s.maxHp - s.data.hp;
            float hpRatio = s.maxHp > 0 ? (float)s.hp / s.maxHp : 1f;
            var oldName = s.data.nameKr;
            s.data = next;
            s.attack = next.attack + attackBonus;
            s.maxHp = next.hp + maxHpBonus;
            // 진화 후 HP는 기존 비율 유지하되 최소 maxHp/3은 보장 (티어 넘을 때 한방킬 방지)
            int minHp = Math.Max(1, s.maxHp / 3);
            s.hp = Math.Max(minHp, Mathf.RoundToInt(s.maxHp * hpRatio));
            Log($"    🌟 {oldName} → {next.nameKr} 진화! (ATK {s.attack} / HP {s.hp}/{s.maxHp})");
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
            }
            else if (c.subType == CardSubType.PURIFY)
            {
                ResolvePurify(c);
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

        /// <summary>보스 페이즈 전환 체크 — HP 비율이 다음 페이즈 임계 아래로 내려가면 패턴셋 교체 + on_enter 액션.</summary>
        private void CheckBossPhaseTransition(EnemyInstance e)
        {
            if (e == null || e.IsDead) return;
            if (string.IsNullOrEmpty(e.data.phaseSetId)) return;

            var phases = DianoCard.Data.DataManager.Instance.GetPhaseSet(e.data.phaseSetId);
            if (phases == null) return;

            float hpRatio = (float)e.hp / Math.Max(1, e.data.hp);
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
                    state.player.TakeDamage(e.intentValue);
                    int healed = Math.Min(e.intentValue, e.data.hp - e.hp);
                    e.hp += healed;
                    Log($"  {e.data.nameKr} drains {e.intentValue} from PLAYER, heals self +{healed}");
                    break;

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
                    Log($"  ⚡ {e.data.nameKr} 카운트다운 광역 발동! ({e.intentValue})");
                    state.player.TakeDamage(e.intentValue);
                    foreach (var s in state.field) s.TakeDamage(e.intentValue);
                    break;

                case EnemyAction.IDLE:
                    // 이끼 수호 상태 등 — 행동하지 않음
                    break;

                default:
                    Log($"  {e.data.nameKr}: unknown action {e.intentAction}");
                    break;
            }
        }

        /// <summary>
        /// 적 단발 공격 — 인텐트 롤 시점(RollIntent)에 확정된 attacker.intentTargetDino를 그대로 때림.
        /// null이면 플레이어. 확정된 공룡이 이미 죽었거나 필드에서 빠졌으면 플레이어로 폴백.
        /// </summary>
        private void DealAttack(EnemyInstance attacker, int rawDmg)
        {
            // 약화 상태면 피해 25% 감소 (C129 포효 등의 효과)
            int dmg = attacker.weakTurns > 0 ? Math.Max(1, (int)(rawDmg * 0.75f)) : rawDmg;

            var target = attacker.intentTargetDino;
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

        /// <summary>이끼 쫄 1체 소환. summoner.isMossAggressive에 따라 공격/수호 패턴으로.</summary>
        private void SpawnMoss(EnemyInstance summoner)
        {
            bool aggressive = summoner.isMossAggressive;
            var mossData = new EnemyData
            {
                id = "MOSS_" + summoner.data.id,
                nameKr = "이끼",
                nameEn = "Moss",
                enemyType = EnemyType.NORMAL,
                chapter = summoner.data.chapter,
                hp = 5,
                attack = aggressive ? 2 : 0,
                defense = 0,
                patternSetId = aggressive ? "PS_MOSS_ATTACK" : "PS_MOSS_PASSIVE",
                phaseSetId = "",
            };
            var moss = new EnemyInstance(mossData);
            moss.isMoss = true;
            state.enemies.Add(moss);
            RollIntent(moss);
            Log($"  {summoner.data.nameKr} 이끼 소환 (HP 5, {(aggressive ? "공격" : "수호")})");
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

            // condition 충족 안 되면 다음 스텝으로 (최대 cycleCount번 시도해 무한루프 방지)
            EnemyPatternData chosen = null;
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
            // 3) 가중 랜덤 — 30% 플레이어 / 70% 랜덤 공룡
            if (_rng.NextDouble() < 0.30) return null;
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
            int newHp = Mathf.Max(1, Mathf.RoundToInt(boss.data.hp * Mathf.Clamp01(ratio)));
            boss.hp = newHp;
            Log($"[CHEAT] {boss.data.nameKr} HP → {newHp}/{boss.data.hp} (≈ {ratio * 100f:F0}%) — 페이즈는 다음 턴에 갱신");
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
