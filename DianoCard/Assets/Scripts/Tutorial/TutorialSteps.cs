using System.Collections.Generic;
using static DianoCard.Data.LocaleSettings;

namespace DianoCard.Tutorial
{
    /// <summary>
    /// CH01 튜토리얼 9단계. 슬라임 ×2 sandbox 전투 흐름.
    /// sandbox 덱 5장 (공격×1 + 방어×1 + 랩터×2 + 융합×1)이 첫 드로우 5장에 전부 손으로 들어옴.
    /// 마나 3 고정이라 한 턴에 3장만 사용 가능 → 중간 END TURN 단계가 학습 흐름에 자연스럽게 포함.
    ///
    /// 이상적 진행:
    ///   턴 1: 작열 송곳니(1코) + 룬 보주(1코) + 랩터(1코) → 마나 0 → END TURN
    ///   턴 2: 랩터 한 마리 더(1코) + 융합의 각인(1코) → 알파 랩터 등장
    ///   턴 3+: 알파 랩터가 자동 공격, 남은 슬라임 정리
    /// </summary>
    public static class TutorialSteps
    {
        public static List<TutorialStep> BuildCH01()
        {
            return new List<TutorialStep>
            {
                new("intro",
                    L("I'm Arkane. Tame the dinosaurs that fell upon the Anvil of Extinction.\nLet's walk through combat quickly.",
                      "Arkane이다. 멸종의 모루에 떨어진 공룡들을 다시 길들여라.\n짧게 전투의 흐름만 짚고 가자."),
                    TutorialAdvanceTrigger.UserClick),

                new("attack_card",
                    L("Drag Searing Fang onto the slime.\nAttack spells strike enemies directly from your hand.",
                      "작열 송곳니를 슬라임에게 끌어다 놔라.\n공격 마법은 손에서 적을 직접 타격한다."),
                    TutorialAdvanceTrigger.CardPlayed,
                    TutorialHighlight.HandCard, cardId: "C101"),

                new("defense_dot_rule",
                    L("Now defend yourself with Runic Orb.\nBlock absorbs direct damage and Poison · Burn · Bleed too.",
                      "이번엔 룬 보주로 자신을 방어해라.\n방어막은 직접 피해뿐 아니라 독·화상·출혈도 함께 막아준다."),
                    TutorialAdvanceTrigger.CardPlayed,
                    TutorialHighlight.HandCard, cardId: "C102"),

                new("summon_dino",
                    L("Summon a Raptor to the field.\nField dinosaurs auto-attack each turn.",
                      "랩터 카드를 필드로 소환해라.\n필드 공룡은 매 턴 자동으로 적을 친다."),
                    TutorialAdvanceTrigger.SummonPlaced,
                    TutorialHighlight.HandCard, cardId: "C004"),

                new("end_turn_first",
                    L("Out of Mana. Press END TURN to start the next turn.\nThe icon above each enemy shows their next-turn intent — read attack/block ahead.",
                      "마나가 떨어졌다. END TURN으로 다음 턴을 시작하자.\n적 머리 위 아이콘이 다음 턴 의도다 — 공격/방어를 미리 읽어라."),
                    TutorialAdvanceTrigger.TurnEnded,
                    TutorialHighlight.EndTurnButton),

                new("second_dino",
                    L("Summon another Raptor.\nTwo of the same species unlock a stronger card.",
                      "랩터를 한 마리 더 부른다.\n같은 종 둘이 모이면 더 강한 카드가 열린다."),
                    TutorialAdvanceTrigger.SummonPlaced,
                    TutorialHighlight.HandCard, cardId: "C004"),

                new("fusion",
                    L("Use Fusion Sigil.\nFuse two same-species, same-tier carnivores to evolve them one tier up.",
                      "융합의 각인을 사용해라.\n같은 종·같은 티어 육식 둘을 합쳐 한 단계 위로 진화시킨다."),
                    TutorialAdvanceTrigger.FusionResolved,
                    TutorialHighlight.HandCard, cardId: "C152"),

                new("evolved",
                    L("Alpha Raptor — faster, stronger.\nThis evolution is your core for this combat.",
                      "알파 랩터다. 더 빠르고 더 강하다.\n이 진화체가 이번 전투의 코어가 된다."),
                    TutorialAdvanceTrigger.UserClick),

                new("finish_battle",
                    L("Press END TURN and let Alpha Raptor clean up the remaining slimes.\nWin the combat to start the real run.",
                      "END TURN을 눌러 알파 랩터가 남은 슬라임을 정리하게 하자.\n전투를 끝내면 본 게임이 시작된다."),
                    TutorialAdvanceTrigger.BattleWon,
                    TutorialHighlight.EndTurnButton),
            };
        }
    }
}
