namespace DianoCard.Data
{
    public enum CardType
    {
        NONE,
        SUMMON,
        MAGIC,
        BUFF,
        RITUAL,
        UTILITY,
    }

    public enum CardSubType
    {
        NONE,
        HERBIVORE,
        CARNIVORE,
        ATTACK,
        DEFENSE,
        HEAL,
        DRAW,
        SACRIFICE,
        ATTACK_BUFF,
        SPECIAL,
        PURIFY,   // 정화 — 빼앗긴 소환수 회수 + 디버프 해제
        STATUS,   // 오염 카드 (잡초 등) — 효과 없음, 사용 시 exhaust, 덱 순환 방해 목적
        TAUNT,    // 도발 — 대상 공룡에게 도발 상태 부여 (적 공격 이 공룡 집중)
        FEED,     // 먹이 — 대상(또는 전체) 공룡 EXP 증가. 진화 조건 충족 시 진화 가능.
        FUSION,   // 융합 촉매 — 같은 종·같은 티어 육식공룡 2마리(필드/손 조합)를 합성해 한 단계 위 티어로 업그레이드.
        DEBUFF,   // 디버프 마법 — 적에게 독/약화/취약/속박 등 상태 부여 (단일/광역).
    }

    public enum Rarity
    {
        COMMON,
        UNCOMMON,
        RARE,
        SHOP,
    }

    public enum TargetType
    {
        NONE,
        SELF,
        ENEMY,
        ALL_ENEMY,
        ALLY,
        ALL_ALLY,
        FIELD,
        RANDOM,
    }

    public enum EnemyType
    {
        NORMAL,
        ELITE,
        BOSS,
    }

    public enum AIPattern
    {
        ATTACK,
        DEFEND_ATTACK,
        DEBUFF_ATTACK,
        POISON_ATTACK,
        BURN_ATTACK,
        SUMMON_ATTACK,
        FLY_ATTACK,
        PHASE_ATTACK,
    }

    /// <summary>enemy_pattern.csv의 action 컬럼 값.</summary>
    public enum EnemyAction
    {
        UNKNOWN,
        ATTACK,
        MULTI_ATTACK,
        DEFEND,
        POISON,
        WEAK,
        DRAIN,
        SUMMON,
        BUFF_SELF,
        COUNTDOWN_ATTACK,
        COUNTDOWN_AOE,
        REFILL_MOSS,   // 이끼 쫄을 target 수까지 보충 (E901 보스)
        IDLE,          // 행동하지 않음 (이끼 수호 상태 등)
        STEAL_SUMMON,  // 플레이어 필드의 공룡 1체를 빼앗아 적 편에 편입 (정화로 해제 가능)
        VULNERABLE,    // 플레이어에게 취약 N턴 부여 (받는 피해 +50%)
        ARMOR_UP,      // 자가 영구 장갑 +N (extraBlockPerTurn 증가, 매 턴 자동 리프레시)
        CLOG_DECK,     // 플레이어 버림더미에 잡초(STATUS 카드) N장 강제 추가
        SILENCE,       // 플레이어 필드의 모든 공룡을 N턴 침묵 — 이 기간엔 공격 명령 불가
        HEAL_BOSS,     // 자신을 소환한 보스(summoner)의 HP를 N 회복 (E901 정령 효과)
        EMPOWER_BOSS,  // 자신을 소환한 보스의 다음 어택 데미지 +N (extraAttack 누적)
        BLOCK_BOSS,    // 자신을 소환한 보스에게 블록 +N 부여
    }

    /// <summary>enemy_pattern.csv의 target 컬럼 값.</summary>
    public enum IntentTarget
    {
        SUMMONER,
        SELF,
        ALL,
        FIELD,
        RANDOM,
        ALLY_FIELD,
    }

    public enum PotionType
    {
        ATTACK,
        DEFENSE,
        UTILITY,
    }

    public enum RelicSource
    {
        START,
        ELITE,
        BOSS,
        SHOP,
        EVENT,
    }

    public enum RelicTrigger
    {
        PASSIVE,
        BATTLE_START,
        TURN_START,
        BATTLE_END,
        SUMMON,
        KILL,
        HP_LOW,
        HP_CRITICAL,
        NODE_ENTER,
    }

    public enum NodeType
    {
        START,
        NORMAL_BATTLE,
        ELITE_BATTLE,
        SHOP,
        TOWN,
        EVENT,
        TREASURE,
        UNKNOWN,
        BOSS,
    }
}
