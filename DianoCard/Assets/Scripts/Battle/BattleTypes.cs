using System;
using DianoCard.Data;

namespace DianoCard.Battle
{
    public enum EnemyIntentType
    {
        ATTACK,
        DEFEND,
        BUFF,
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

        public bool IsDead => hp <= 0;

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
        public int hp;
        public int attack;
        public int tempAttackBonus; // 한 턴 버프 (턴 시작 시 0으로 리셋)

        public int TotalAttack => attack + tempAttackBonus;
        public bool IsDead => hp <= 0;
        public bool IsHerbivore => data.subType == CardSubType.HERBIVORE;
        public bool IsCarnivore => data.subType == CardSubType.CARNIVORE;

        public SummonInstance(CardData data)
        {
            this.data = data;
            this.hp = data.hp;
            this.attack = data.attack;
        }

        public void TakeDamage(int dmg)
        {
            hp = Math.Max(0, hp - dmg);
        }

        public void Heal(int amount)
        {
            hp = Math.Min(data.hp, hp + amount);
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

        // 이번 턴에 할 행동 (턴 시작 시 미리 공개)
        public EnemyIntentType intentType;
        public int intentValue;

        public bool IsDead => hp <= 0;
        public string IntentLabel => intentType switch
        {
            EnemyIntentType.ATTACK => $"ATK {intentValue}",
            EnemyIntentType.DEFEND => $"DEF {intentValue}",
            EnemyIntentType.BUFF => $"BUFF {intentValue}",
            _ => "?",
        };

        public EnemyInstance(EnemyData data)
        {
            this.data = data;
            this.hp = data.hp;
            this.block = data.defense;
        }

        public void TakeDamage(int dmg)
        {
            int absorbed = Math.Min(block, dmg);
            block -= absorbed;
            int remaining = dmg - absorbed;
            hp = Math.Max(0, hp - remaining);
        }
    }
}
