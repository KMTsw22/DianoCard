using DianoCard.Battle;
using DianoCard.Data;
using UnityEngine;

namespace DianoCard.Game
{
    /// <summary>
    /// 포션(Potion) 효과 디스패처 — potion.csv의 id별로 게임 상태에 효과를 적용한다.
    ///
    /// 사용 흐름:
    ///   1. 플레이어가 상단 슬롯의 포션을 클릭 → BattleUI가 RequiresTarget로 타겟 필요 여부 확인.
    ///   2. 필요하면 타겟 픽커 모드로 진입(적/아군 클릭 대기).
    ///   3. 타겟 결정 후 BattleManager.UsePotion(slotIndex, targetIndex) 호출 → 내부에서 PotionEffects.Use 호출.
    ///   4. 슬롯에서 포션 제거 + 디버그 로그.
    ///
    /// 타겟팅:
    ///   - ENEMY     → 적 1체 클릭 (potion.target == ENEMY)
    ///   - ALLY      → 아군 공룡 1체 클릭 (potion.target == ALLY)
    ///   - SELF      → 즉시 사용 (플레이어 자신)
    ///   - ALL_ENEMY → 즉시 사용 (전체 적)
    ///   - ALL_ALLY  → 즉시 사용 (전체 아군)
    ///
    /// 효과는 id별 switch — 신규 포션 추가 시 케이스만 늘리면 됨.
    /// </summary>
    public static class PotionEffects
    {
        // =========================================================
        // 타겟팅 메타데이터
        // =========================================================

        /// <summary>이 포션이 타겟 클릭을 필요로 하는가? (UI가 픽커 모드를 띄울지 결정).</summary>
        public static bool RequiresTarget(PotionData potion)
        {
            if (potion == null) return false;
            return potion.target == TargetType.ENEMY || potion.target == TargetType.ALLY;
        }

        // =========================================================
        // 효과 적용
        // =========================================================

        /// <summary>
        /// 포션 효과 적용. 호출자(BattleManager)는 이미 슬롯에서 제거했거나 직후 제거할 책임을 갖는다.
        /// targetIndex 의미:
        ///   - target=ENEMY → state.enemies 인덱스
        ///   - target=ALLY  → state.field 인덱스
        ///   - 그 외 → 무시
        /// 반환: 성공 적용 시 true. 타겟 무효 등으로 적용 안 한 경우 false (호출자가 슬롯 복구할 수 있음).
        /// </summary>
        public static bool Use(BattleManager mgr, RunState run, PotionData potion, int targetIndex)
        {
            if (mgr == null || mgr.state == null || potion == null) return false;
            var state = mgr.state;
            var player = state.player;
            if (player == null) return false;

            switch (potion.id)
            {
                // === ATTACK ===
                case "P001": // 공격 물약 — 적 1체 데미지 20
                case "P003": // 최상급 공격 물약 — 적 1체 데미지 50
                {
                    var target = ResolveEnemyTarget(state, targetIndex);
                    if (target == null) return false;
                    bool wasAlive = !target.IsDead;
                    target.TakeDamage(potion.value);
                    Debug.Log($"[Potion] {potion.id} {potion.nameKr}: {target.data.nameKr} -{potion.value} (HP {target.hp})");
                    if (wasAlive && target.IsDead)
                        RelicEffects.OnEnemyKilled(mgr, run, null, target);
                    return true;
                }

                case "P002": // 상급 공격 물약 — 모든 적 데미지 30
                {
                    foreach (var e in state.enemies)
                    {
                        if (e == null || e.IsDead) continue;
                        bool wasAlive = !e.IsDead;
                        e.TakeDamage(potion.value);
                        if (wasAlive && e.IsDead)
                            RelicEffects.OnEnemyKilled(mgr, run, null, e);
                    }
                    Debug.Log($"[Potion] {potion.id} {potion.nameKr}: 모든 적 -{potion.value}");
                    return true;
                }

                case "P014": // 빙결 물약 — 적 1체 1턴 stun
                {
                    var target = ResolveEnemyTarget(state, targetIndex);
                    if (target == null) return false;
                    target.stunTurns += Mathf.Max(1, potion.value);
                    Debug.Log($"[Potion] {potion.id} {potion.nameKr}: {target.data.nameKr} stun {potion.value}T");
                    return true;
                }

                // === DEFENSE ===
                case "P004": // 방어 물약 +10
                case "P005": // 상급 방어 물약 +20
                case "P006": // 최상급 방어 물약 +30
                {
                    player.block += potion.value;
                    Debug.Log($"[Potion] {potion.id} {potion.nameKr}: 방어도 +{potion.value} (now {player.block})");
                    return true;
                }

                // === UTILITY ===
                case "P007": // 드로우 물약 — 카드 3장 드로우
                {
                    mgr.Draw(potion.value);
                    Debug.Log($"[Potion] {potion.id} {potion.nameKr}: 카드 {potion.value}장 드로우");
                    return true;
                }

                case "P008": // 마나 물약 — 이번 턴 마나 +2
                {
                    player.mana += potion.value;
                    Debug.Log($"[Potion] {potion.id} {potion.nameKr}: 마나 +{potion.value} (now {player.mana})");
                    return true;
                }

                case "P009": // 축복 물약 — 모든 아군 ATK+2 / HP+5 (value 무시, 하드코딩)
                {
                    int atkBonus = 2;
                    int hpBonus = 5;
                    int hpScaled = Mathf.RoundToInt(hpBonus * RelicEffects.GetHealMultiplier(run));
                    int affected = 0;
                    foreach (var s in state.field)
                    {
                        if (s == null || s.IsDead) continue;
                        s.attack += atkBonus;
                        if (hpScaled > 0)
                        {
                            s.maxHp += hpScaled;
                            s.hp = Mathf.Min(s.maxHp, s.hp + hpScaled);
                        }
                        affected++;
                    }
                    Debug.Log($"[Potion] {potion.id} {potion.nameKr}: 모든 아군({affected}체) +{atkBonus} ATK / +{hpScaled} HP");
                    return true;
                }

                case "P010": // 회복 물약 — HP value 회복 (R022 보유 시 -25%)
                {
                    int scaled = Mathf.RoundToInt(potion.value * RelicEffects.GetHealMultiplier(run));
                    if (scaled > 0) player.Heal(scaled);
                    Debug.Log($"[Potion] {potion.id} {potion.nameKr}: HP +{scaled} (now {player.hp}/{player.maxHp})");
                    return true;
                }

                case "P011": // 소환 물약 — 손패의 랜덤 공룡 1장을 필드로 즉시 소환 (마나/타겟 무시)
                {
                    if (!mgr.TryPotionSummonRandomFromHand())
                    {
                        Debug.LogWarning($"[Potion] {potion.id} {potion.nameKr}: 손패에 공룡 없음 또는 필드 만석 — 포션 보존");
                        return false;
                    }
                    Debug.Log($"[Potion] {potion.id} {potion.nameKr}: 손패의 무작위 공룡 소환 완료");
                    return true;
                }

                case "P012": // 정화 물약 — 모든 디버프 제거 + 카드 1장 드로우
                {
                    player.poisonStacks = 0;
                    player.weakTurns = 0;
                    player.vulnerableTurns = 0;
                    mgr.Draw(1);
                    Debug.Log($"[Potion] {potion.id} {potion.nameKr}: 디버프 제거 + 드로우 1");
                    return true;
                }

                case "P013": // 강화 물약 — 아군 공룡 1체 이번 전투 ATK +3
                {
                    var ally = ResolveAllyTarget(state, targetIndex);
                    if (ally == null) return false;
                    int bonus = Mathf.Max(1, potion.value);
                    ally.attack += bonus;
                    Debug.Log($"[Potion] {potion.id} {potion.nameKr}: {ally.data.nameKr} ATK +{bonus} (이번 전투, now {ally.attack})");
                    return true;
                }

                case "P015": // 각성 물약 — 1티어 임시 진화 (MVP 미구현)
                {
                    // 진화 시스템이 MVP에 없어 1티어 진화는 적용 불가.
                    // 대체 효과: 대상 공룡 ATK +5 / 최대 HP +10 (이번 전투, 진화 위력에 준하는 강력 버프).
                    var ally = ResolveAllyTarget(state, targetIndex);
                    if (ally == null) return false;
                    int hpBonus = Mathf.RoundToInt(10 * RelicEffects.GetHealMultiplier(run));
                    ally.attack += 5;
                    ally.maxHp += hpBonus;
                    ally.hp = Mathf.Min(ally.maxHp, ally.hp + hpBonus);
                    Debug.Log($"[Potion] {potion.id} {potion.nameKr}: {ally.data.nameKr} 각성 (ATK +5, HP +{hpBonus}) — 진화 시스템 부재 대체 효과");
                    return true;
                }

                case "P016": // 균열 물약 — 손패 베이스 공룡 2장 융합 (MVP 미구현 — 융합 카드 CSV 누락)
                {
                    // 융합 시스템이 데이터 미완성이라 적용 불가.
                    // 대체 효과: 손패에서 베이스 공룡 카드를 임의로 1장 골라 다음 티어 공룡으로 즉시 변환 시도. 데이터 없으면 노op.
                    Debug.LogWarning($"[Potion] {potion.id} {potion.nameKr}: 융합 시스템 미완성 — 효과 적용 안 됨 (포션은 그대로 소비)");
                    return true; // 슬롯은 비움 (false 반환하면 포션이 안 사라짐)
                }

                default:
                    Debug.LogWarning($"[Potion] 알 수 없는 포션 id: {potion.id}");
                    return false;
            }
        }

        // =========================================================
        // 헬퍼
        // =========================================================

        private static EnemyInstance ResolveEnemyTarget(BattleState state, int targetIndex)
        {
            if (targetIndex < 0 || targetIndex >= state.enemies.Count) return null;
            var t = state.enemies[targetIndex];
            return (t == null || t.IsDead) ? null : t;
        }

        private static SummonInstance ResolveAllyTarget(BattleState state, int targetIndex)
        {
            if (targetIndex < 0 || targetIndex >= state.field.Count) return null;
            var t = state.field[targetIndex];
            return (t == null || t.IsDead) ? null : t;
        }
    }
}
