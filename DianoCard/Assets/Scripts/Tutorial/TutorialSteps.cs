using System.Collections.Generic;
using static DianoCard.Data.LocaleSettings;

namespace DianoCard.Tutorial
{
    /// <summary>
    /// CH01 튜토리얼. 슬라임 ×2 sandbox 전투 흐름.
    /// sandbox 덱 5장 (공격×1 + 방어×1 + 랩터×2 + 융합×1) + 포션 P004 + 유물 R001.
    /// 마지막 potion_use 단계 후 가이드 종료 — 잔여 턴은 자유 플레이.
    /// 전투 승리 시 GSM.EndBattle이 자동으로 EndTutorial 호출 → RelicPick 진입.
    /// </summary>
    public static class TutorialSteps
    {
        public static List<TutorialStep> BuildCH01()
        {
            return new List<TutorialStep>
            {
                new("intro",
                    L("I'm Arkane. Let's walk through combat.",
                      "Arkane이다. 짧게 전투의 흐름을 보자."),
                    TutorialAdvanceTrigger.UserClick),

                new("relic_view",
                    L("Top-bar shows your Relics — passive effects, always on. Hover to read.",
                      "상단 유물은 항상 발동되는 패시브. 마우스를 올려 효과를 본다."),
                    TutorialAdvanceTrigger.UserClick,
                    TutorialHighlight.RelicIcon),

                new("attack_card",
                    L("Click Searing Fang, then click a slime.",
                      "작열 송곳니를 누른 뒤 슬라임을 누른다."),
                    TutorialAdvanceTrigger.CardPlayed,
                    TutorialHighlight.HandCard, cardId: "C101"),

                new("defense_dot_rule",
                    L("Use Runic Orb. Block absorbs damage and Poison · Burn · Bleed.",
                      "룬 보주 사용. 방어막은 피해와 독·화상·출혈도 막는다."),
                    TutorialAdvanceTrigger.CardPlayed,
                    TutorialHighlight.HandCard, cardId: "C102"),

                new("summon_dino",
                    L("Summon a Raptor. Field dinos auto-attack each turn.",
                      "랩터를 소환. 필드 공룡은 매 턴 자동 공격."),
                    TutorialAdvanceTrigger.SummonPlaced,
                    TutorialHighlight.HandCard, cardId: "C004"),

                new("end_turn_first",
                    L("Mana out. END TURN. Enemy intent shows above their head.",
                      "마나 끝. END TURN. 적 머리 위 아이콘이 다음 행동."),
                    TutorialAdvanceTrigger.TurnEnded,
                    TutorialHighlight.EndTurnButton),

                new("second_dino",
                    L("Summon another Raptor — Fusion needs two of the same species.",
                      "랩터 한 마리 더. 융합은 같은 종 둘이 필요하다."),
                    TutorialAdvanceTrigger.SummonPlaced,
                    TutorialHighlight.HandCard, cardId: "C004"),

                new("fusion",
                    L("Click Fusion Sigil, then pick two Raptors (hand or field).",
                      "융합의 각인을 누르고 랩터 두 마리(손/필드)를 고른다."),
                    TutorialAdvanceTrigger.FusionResolved,
                    TutorialHighlight.HandCard, cardId: "C152"),

                new("evolved",
                    L("Alpha Raptor — your evolved core.",
                      "알파 랩터 — 진화체가 코어다."),
                    TutorialAdvanceTrigger.UserClick),

                new("potion_use",
                    L("Top-bar potion → Drink. Block carries to next turn.",
                      "상단의 포션 → 마시기. 방어막은 다음 턴까지."),
                    TutorialAdvanceTrigger.PotionUsed,
                    TutorialHighlight.PotionIcon),

                // farewell — 5초 후 자동 사라짐. 이 단계 동안 모든 게이트 해제(자유 플레이).
                // 사라진 뒤 잔여 턴은 평소처럼 진행, 슬라임 처치 시 EndTutorial → RelicPick.
                new("farewell",
                    L("Now — finish this battle.",
                      "이제 — 전투를 이겨내라."),
                    TutorialAdvanceTrigger.Timer,
                    TutorialHighlight.None,
                    cardId: null,
                    autoAdvanceSeconds: 5f),
            };
        }
    }
}
