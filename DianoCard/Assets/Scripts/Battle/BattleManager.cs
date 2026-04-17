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

        public void StartBattle(List<CardData> startingDeck, List<EnemyData> enemyPool, int maxMana = 3, int playerHp = 70, int maxFieldSize = 5)
        {
            state = new BattleState();
            state.maxFieldSize = maxFieldSize;
            state.player = new Player
            {
                maxHp = playerHp,
                hp = playerHp,
                maxMana = maxMana,
            };

            foreach (var e in enemyPool)
            {
                if (e == null) continue;
                state.enemies.Add(new EnemyInstance(e));
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

            // 소환수 한 턴 버프 리셋
            foreach (var s in state.field) s.tempAttackBonus = 0;

            // 적 인텐트(이번 턴 행동) 결정
            foreach (var e in state.enemies)
            {
                if (e.IsDead) continue;
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
        /// <returns>성공적으로 사용했으면 true</returns>
        public bool PlayCard(int handIndex, int targetEnemyIndex = -1)
        {
            if (handIndex < 0 || handIndex >= state.hand.Count) return false;

            var inst = state.hand[handIndex];
            var card = inst.data;

            if (state.player.mana < card.cost)
            {
                Log($"  ! Cannot play {card.nameKr}: need {card.cost} mana (have {state.player.mana})");
                return false;
            }

            // 필드 슬롯 제한 — 챕터별 (1챕터=2체). 육식공룡은 제물로 슬롯을 교체하므로 예외.
            if (card.cardType == CardType.SUMMON
                && card.subType != CardSubType.CARNIVORE
                && state.field.Count >= state.maxFieldSize)
            {
                Log($"  ! Cannot play {card.nameKr}: field full ({state.field.Count}/{state.maxFieldSize})");
                return false;
            }

            // 육식 공룡: 제물 1체 필요 (MVP: 아무 소환수 1체 자동 소모)
            if (card.cardType == CardType.SUMMON && card.subType == CardSubType.CARNIVORE)
            {
                if (state.field.Count == 0)
                {
                    Log($"  ! Cannot play {card.nameKr}: no sacrifice on field");
                    return false;
                }
                var sac = state.field[0];
                state.field.RemoveAt(0);
                Log($"  >> Sacrificed {sac.data.nameKr}");
            }

            // 명시된 타겟이 있으면 EnemyInstance로 변환
            EnemyInstance explicitTarget = null;
            if (targetEnemyIndex >= 0 && targetEnemyIndex < state.enemies.Count)
            {
                var candidate = state.enemies[targetEnemyIndex];
                if (!candidate.IsDead) explicitTarget = candidate;
            }

            // 비용 지불 & 손에서 제거
            state.player.mana -= card.cost;
            state.hand.RemoveAt(handIndex);
            state.discard.Add(inst);

            Log($"  [Play] {card.nameKr} (cost {card.cost})");
            ResolveCard(card, explicitTarget);

            return true;
        }

        private void ResolveCard(CardData c, EnemyInstance explicitTarget)
        {
            switch (c.cardType)
            {
                case CardType.SUMMON:
                    var summon = new SummonInstance(c);
                    state.field.Add(summon);
                    Log($"    Summoned {c.nameKr} (ATK {c.attack} / HP {c.hp})");
                    break;

                case CardType.MAGIC:
                    ResolveMagic(c, explicitTarget);
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

        private void ResolveMagic(CardData c, EnemyInstance explicitTarget)
        {
            if (c.subType == CardSubType.ATTACK)
            {
                switch (c.target)
                {
                    case TargetType.ALL_ENEMY:
                        foreach (var e in state.enemies)
                        {
                            if (e.IsDead) continue;
                            e.TakeDamage(c.value);
                            Log($"    -> {e.data.nameKr} takes {c.value} (HP {e.hp})");
                        }
                        break;
                    case TargetType.RANDOM:
                        var randTarget = RandomAliveEnemy();
                        if (randTarget != null)
                        {
                            randTarget.TakeDamage(c.value);
                            Log($"    -> {randTarget.data.nameKr} takes {c.value} (HP {randTarget.hp})");
                        }
                        break;
                    default:
                        // ENEMY 또는 미지정 — 명시 타겟 우선, 없으면 첫 적
                        var t = explicitTarget ?? FirstAliveEnemy();
                        if (t != null && !t.IsDead)
                        {
                            t.TakeDamage(c.value);
                            Log($"    -> {t.data.nameKr} takes {c.value} (HP {t.hp})");
                        }
                        break;
                }
            }
            else if (c.subType == CardSubType.DEFENSE)
            {
                state.player.block += c.value;
                Log($"    -> +{c.value} block (now {state.player.block})");
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
                    foreach (var s in state.field) s.Heal(c.value);
                    state.player.Heal(c.value);
                    Log($"    -> All allies heal +{c.value}");
                    break;

                case CardSubType.SPECIAL:
                    Log($"    (SPECIAL buff not yet implemented: {c.nameKr})");
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

            // 1. 소환수 공격 (한 번에)
            foreach (var s in state.field)
            {
                if (s.IsDead) continue;
                DoSummonAttack(s);
            }

            if (state.PlayerWon) { Log("=== VICTORY ==="); return; }

            // 2. 적 턴 (한 번에)
            foreach (var e in state.enemies)
            {
                if (e.IsDead) continue;
                DoEnemyAction(e);
                if (state.PlayerLost) { Log("=== DEFEAT ==="); return; }
            }

            EndTurnCleanup();
            StartNextTurnIfAlive();
        }

        // =========================================================
        // 애니메이션용 granular 단계 메서드 (BattleUI 코루틴이 사용)
        // =========================================================

        /// <summary>한 마리 소환수의 자동 공격을 실행. 죽었거나 타겟 없으면 no-op.</summary>
        public void DoSummonAttack(SummonInstance summon)
        {
            if (summon == null || summon.IsDead) return;
            var target = FirstAliveEnemy();
            if (target == null) return;
            target.TakeDamage(summon.TotalAttack);
            Log($"  {summon.data.nameKr} attacks {target.data.nameKr} for {summon.TotalAttack} (HP {target.hp})");
            if (target.IsDead) Log($"    x {target.data.nameKr} defeated");
        }

        /// <summary>한 마리 적의 인텐트 실행 (공격/방어 등).</summary>
        public void DoEnemyAction(EnemyInstance enemy)
        {
            if (enemy == null || enemy.IsDead) return;
            ExecuteIntent(enemy);
        }

        /// <summary>턴 종료 정리: 죽은 소환수 제거, 패 버림더미로.</summary>
        public void EndTurnCleanup()
        {
            state.field.RemoveAll(s => s.IsDead);
            foreach (var c in state.hand) state.discard.Add(c);
            state.hand.Clear();
        }

        /// <summary>다음 턴 시작 (전투가 끝나지 않았을 때만).</summary>
        public void StartNextTurnIfAlive()
        {
            if (!state.IsOver) StartTurn();
        }

        private void ExecuteIntent(EnemyInstance e)
        {
            switch (e.intentType)
            {
                case EnemyIntentType.ATTACK:
                    // 필드에 소환수가 있으면 소환수부터, 없으면 플레이어
                    if (state.field.Count > 0)
                    {
                        var target = state.field[0];
                        target.TakeDamage(e.intentValue);
                        Log($"  {e.data.nameKr} attacks {target.data.nameKr} for {e.intentValue} (HP {target.hp})");
                        if (target.IsDead)
                        {
                            Log($"    x {target.data.nameKr} defeated");
                            state.field.Remove(target);
                        }
                    }
                    else
                    {
                        state.player.TakeDamage(e.intentValue);
                        Log($"  {e.data.nameKr} attacks PLAYER for {e.intentValue} (HP {state.player.hp}, block {state.player.block})");
                    }
                    break;

                case EnemyIntentType.DEFEND:
                    e.block += e.intentValue;
                    Log($"  {e.data.nameKr} defends (+{e.intentValue} block)");
                    break;
            }
        }

        private void RollIntent(EnemyInstance e)
        {
            // MVP: 항상 공격 (고정 데이터 기반). 추후 ai_pattern 별 분기.
            e.intentType = EnemyIntentType.ATTACK;
            e.intentValue = e.data.attack;
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

        private EnemyInstance RandomAliveEnemy()
        {
            var alive = new List<EnemyInstance>();
            foreach (var e in state.enemies) if (!e.IsDead) alive.Add(e);
            if (alive.Count == 0) return null;
            return alive[_rng.Next(alive.Count)];
        }

        // =========================================================
        // 로그 출력
        // =========================================================

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
            sb.Append($"  Deck {state.deck.Count} / Discard {state.discard.Count}");
            Log(sb.ToString());
        }

        private void Log(string msg)
        {
            Debug.Log(msg);
        }
    }
}
