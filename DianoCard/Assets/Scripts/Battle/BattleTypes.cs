using System;
using DianoCard.Data;

namespace DianoCard.Battle
{
    /// <summary>
    /// 인텐트 시각 분류 — 적 머리 위 아이콘 결정. 실제 액션은 EnemyInstance.intentAction(EnemyAction).
    /// </summary>
    public enum EnemyIntentType
    {
        ATTACK,
        DEFEND,
        BUFF,
        DEBUFF,
        SUMMON,
        COUNTDOWN,
        UNKNOWN,
    }

    /// <summary>
    /// 전투 중 플레이어(소환사) 런타임 상태.
    /// </summary>
    public class Player
    {
        public int maxHp = 70;
        public int hp = 70;
        public int block;
        public int mana;
        public int maxMana = 3;

        // === 디버프 (rough MVP) ===
        public int poisonStacks;     // 매 턴 종료 시 poisonStacks만큼 피해, 1 감소
        public int weakTurns;        // >0이면 플레이어가 가하는 피해 25% 감소
        public int vulnerableTurns;  // >0이면 플레이어가 받는 피해 +50%

        // === 이번 턴 버프 ===
        // C132 동족 소환: 이번 턴 SUMMON 카드 비용 감면. StartTurn에 0으로 리셋.
        public int summonCostReduction;

        // === 치트 ===
        public bool cheatInvincible; // true면 모든 피해 무시 (블록도 안 깎임)

        public bool IsDead => hp <= 0;

        public void TakeDamage(int dmg)
        {
            if (cheatInvincible) return;
            // 취약 시 raw dmg가 먼저 증폭된 뒤 block으로 흡수 (STS 방식).
            int adjusted = vulnerableTurns > 0 ? (int)System.Math.Round(dmg * 1.5f) : dmg;
            int absorbed = Math.Min(block, adjusted);
            block -= absorbed;
            int remaining = adjusted - absorbed;
            hp = Math.Max(0, hp - remaining);
        }

        public void Heal(int amount)
        {
            hp = Math.Min(maxHp, hp + amount);
        }

        /// <summary>턴 종료 시 호출 — 독 데미지 + 약화/취약 카운트다운.</summary>
        public void TickStatuses()
        {
            if (poisonStacks > 0)
            {
                if (!cheatInvincible) hp = Math.Max(0, hp - poisonStacks);
                poisonStacks--;
            }
            if (weakTurns > 0) weakTurns--;
            if (vulnerableTurns > 0) vulnerableTurns--;
        }
    }

    /// <summary>
    /// 덱/패/버림 더미에 존재하는 카드 한 장 인스턴스.
    /// CardData는 설계 원본(불변), CardInstance는 런타임 복사본.
    /// </summary>
    public class CardInstance
    {
        public CardData data;

        public CardInstance(CardData data)
        {
            this.data = data;
        }
    }

    /// <summary>
    /// 필드에 소환된 공룡 인스턴스.
    /// </summary>
    public class SummonInstance
    {
        public CardData data;
        // 최초 소환 시점의 base 카드 id — 진화 후에도 불변. 덮어쓰기/합성 매칭 기준.
        // (data.id는 육식 진화 시 바뀌므로 같은 종 추가 플레이가 안 붙는 문제를 이 필드로 해결)
        public string originCardId;
        // 이 소환수를 만든 카드 인스턴스. state.bound에 있음. 소환수 제거 시 discard로 반환.
        // PURIFY 등으로 바인딩 없이 복원된 경우 null.
        public CardInstance sourceCardInstance;
        public int hp;
        // 인스턴스 최대 HP — 초식 덮어쓰기로 상승 가능. 최초 생성 시 data.hp로 초기화.
        public int maxHp;
        public int attack;
        public int tempAttackBonus; // 한 턴 버프 (턴 시작 시 0으로 리셋)
        // 방어도 — 피해를 먼저 흡수. StartTurn에 0으로 리셋 (수호 결계 스펠로 다시 부여).
        public int block;
        // 누적 스택 — 같은 종 SUMMON 카드가 덮어쓰기/합성될 때마다 +1.
        // 초식: 스탯 영구 상승 (카드 변경 없음). 육식: 진화 임계 도달 시 다음 형태로 변형.
        public int stacks;

        // 수동 공격 시스템: 턴당 1회 명령 가능. StartTurn에 false로 리셋.
        public bool hasAttackedThisTurn;
        // 침묵: >0이면 공격 명령 불가. 매 턴 종료 시 1 감소.
        public int silencedTurns;
        // 도발: >0이면 적의 모든 공격이 이 공룡으로 집중됨. 매 턴 종료 시 1 감소.
        public int tauntTurns;

        public bool CanAttack => !IsDead && !hasAttackedThisTurn && silencedTurns <= 0;
        public bool IsTaunting => !IsDead && tauntTurns > 0;
        public int TotalAttack => attack + tempAttackBonus;
        public bool IsDead => hp <= 0;
        public bool IsHerbivore => data.subType == CardSubType.HERBIVORE;
        public bool IsCarnivore => data.subType == CardSubType.CARNIVORE;

        public SummonInstance(CardData data)
        {
            this.data = data;
            this.originCardId = data.id;
            this.hp = data.hp;
            this.maxHp = data.hp;
            this.attack = data.attack;
        }

        public void TakeDamage(int dmg)
        {
            int absorbed = Math.Min(block, dmg);
            block -= absorbed;
            int remaining = dmg - absorbed;
            hp = Math.Max(0, hp - remaining);
        }

        public void Heal(int amount)
        {
            hp = Math.Min(maxHp, hp + amount);
        }
    }

    /// <summary>
    /// 전투 중 적 런타임 인스턴스.
    /// </summary>
    public class EnemyInstance
    {
        public EnemyData data;
        public int hp;
        public int block;
        public int extraAttack; // BUFF_SELF로 누적된 공격력 보너스 (영구)

        // === 인텐트 (이번 턴에 할 행동, 턴 시작 시 미리 공개) ===
        public EnemyIntentType intentType;
        public int intentValue;
        public int intentCount = 1;          // MULTI_ATTACK 분할 횟수 등
        public EnemyAction intentAction;     // 실제 액션 (실행 시 분기)
        public IntentTarget intentTarget;
        public string intentIcon;            // intent_icon.csv 키
        public int telegraphRemaining;       // >0이면 예고 중 (시각 표시), 0 도달 시 발동
        // 공격 인텐트가 RollIntent 시점에 미리 확정한 타겟. null이면 플레이어 대상.
        // DealAttack은 이 필드를 읽어 실행 시 재롤하지 않음 — 표시와 실제 일치.
        public SummonInstance intentTargetDino;

        // === 패턴 진행 ===
        public string currentPatternSetId;   // 현재 사용 중인 패턴셋 (페이즈 전환 시 변경)
        public int patternStepCursor;        // 현재 step_order 인덱스 (순환)
        public int currentPhase = 1;

        // === 디버프 (rough MVP) ===
        public int poisonStacks;
        public int weakTurns;

        // === 페이즈 전환 추적 (보스용) ===
        // 0이면 phase 1 on_enter도 배틀 시작 시 1회 실행됨.
        public int phasesEntered = 0;        // 이미 진입한 페이즈 수 (다음 임계 비교용)

        // === E901 이끼 수호석상 기믹 ===
        public bool isMoss;                  // 이끼 쫄 (보호막 계산에 사용)
        public bool isBossProtected;         // true + 이끼 생존 시 본체 타겟 불가
        public bool isMossAggressive;        // true면 소환되는 이끼가 공격 패턴으로 스폰
        public int graceTurnsRemaining;      // >0이면 본체 행동을 스킵 (페이즈 전환 직후 각성 턴)
        public int extraActionsPerTurn;      // >0이면 1턴에 (1+N)회 행동. P3 거대화 시 1로 세팅.
        public int extraBlockPerTurn;        // >0이면 매 턴 시작 시 block이 이 값으로 자동 세팅 (영구 장갑). P3 거대화 시 6으로 세팅.

        // === 빼앗긴 공룡 (STEAL_SUMMON) ===
        public CardData stolenFromCard;      // null이 아니면 플레이어에게서 빼앗은 소환수.
                                             // 정화(PURIFY) 시 이 CardData로 SummonInstance를 복원해 필드로 되돌림.
        public bool IsStolen => stolenFromCard != null;

        public int TotalAttack => data.attack + extraAttack;
        public bool IsDead => hp <= 0;
        public string IntentLabel
        {
            get
            {
                if (graceTurnsRemaining > 0) return "각성 중…";
                if (telegraphRemaining > 0)
                    return $"⚠ {intentAction} {intentValue}×{intentCount} (T-{telegraphRemaining})";
                return intentAction switch
                {
                    EnemyAction.ATTACK            => $"ATK {intentValue}",
                    EnemyAction.MULTI_ATTACK      => $"ATK {intentValue}×{intentCount}",
                    EnemyAction.DEFEND            => $"DEF {intentValue}",
                    EnemyAction.POISON            => $"독 {intentValue}",
                    EnemyAction.WEAK              => $"약화 {intentValue}",
                    EnemyAction.DRAIN             => $"흡혈 {intentValue}",
                    EnemyAction.SUMMON            => $"소환 {intentValue}",
                    EnemyAction.BUFF_SELF         => $"강화 +{intentValue}",
                    EnemyAction.COUNTDOWN_ATTACK  => $"⚠ 강타 {intentValue}",
                    EnemyAction.COUNTDOWN_AOE     => $"⚠ 광역 {intentValue}",
                    EnemyAction.REFILL_MOSS       => $"이끼 보충 {intentValue}",
                    EnemyAction.STEAL_SUMMON      => $"⚠ 공룡 강탈",
                    EnemyAction.VULNERABLE        => $"취약 {intentValue}T",
                    EnemyAction.ARMOR_UP          => $"장갑 +{intentValue}",
                    EnemyAction.CLOG_DECK         => $"⚠ 잡초 +{intentValue}",
                    EnemyAction.SILENCE           => $"⚠ 침묵 {intentValue}T",
                    EnemyAction.IDLE              => "—",
                    _                             => "?",
                };
            }
        }

        public EnemyInstance(EnemyData data)
        {
            this.data = data;
            this.hp = data.hp;
            this.block = data.defense;
            this.currentPatternSetId = data.patternSetId;
        }

        public void TakeDamage(int dmg)
        {
            int absorbed = Math.Min(block, dmg);
            block -= absorbed;
            int remaining = dmg - absorbed;
            hp = Math.Max(0, hp - remaining);
        }

        public void TickStatuses()
        {
            if (poisonStacks > 0)
            {
                hp = Math.Max(0, hp - poisonStacks);
                poisonStacks--;
            }
            if (weakTurns > 0) weakTurns--;
        }
    }
}
