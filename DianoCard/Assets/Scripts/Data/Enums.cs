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
