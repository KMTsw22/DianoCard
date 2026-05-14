using System.Collections.Generic;
using static DianoCard.Data.LocaleSettings;

namespace DianoCard.Tutorial
{
    /// <summary>
    /// CH01 튜토리얼. 슬라임 ×2 sandbox 전투 흐름.
    /// sandbox 덱 5장 (공격×1 + 방어×1 + 랩터×2 + 융합×1) + 포션 P004 + 유물 R001.
    /// 마지막 potion_use 단계 후 가이드 종료 — 잔여 턴은 자유 플레이.
    /// 전투 승리 시 GSM.EndBattle이 자동으로 EndTutorial 호출 → RelicPick 진입.
    /// 메시지 톤: 단계 번호(1)/2)) + &lt;b&gt;핵심 단어&lt;/b&gt; richText. 사용자가 어디를 눌러야 할지 즉시 알도록.
    /// </summary>
    public static class TutorialSteps
    {
        /// <summary>메시지 본문의 &lt;b&gt;…&lt;/b&gt; 마커를 제거 — 색 분리/굵게 강조 없이 평문 출력.
        /// 메시지 본문은 &lt;b&gt;로 핵심 단어를 표시해 두되, 시각적 강조는 입히지 않는다.</summary>
        private static string E(string s) =>
            s.Replace("<b>", "").Replace("</b>", "");

        public static List<TutorialStep> BuildCH01()
        {
            var steps = new List<TutorialStep>
            {
                new("intro",
                    L("I'm <b>Arkane</b>. Let's walk through combat together.",
                      "<b>Arkane</b>이다. 전투의 흐름을 같이 보자."),
                    TutorialAdvanceTrigger.UserClick),

                new("relic_view",
                    L("Top-bar icons are your <b>Relics</b> — passive effects.\nHover the glowing icon to read it.",
                      "상단 아이콘은 <b>유물</b> — 항상 켜진 패시브.\n빛나는 아이콘에 <b>마우스를 올려</b> 본다."),
                    TutorialAdvanceTrigger.RelicHovered,
                    TutorialHighlight.RelicIcon),

                new("mana_intro",
                    L("Bottom-left orb is your <b>Mana</b> — <b>3 per turn</b>.\nHover the orb to see the panel.",
                      "좌하단 오브가 <b>마나</b> — <b>매 턴 3</b>.\n오브 위에 <b>마우스를 올려</b> 패널을 본다."),
                    TutorialAdvanceTrigger.ManaHovered,
                    TutorialHighlight.ManaOrb),

                new("attack_card",
                    L("<b>1)</b> Click <b>Searing Fang</b> in your hand.\n<b>2)</b> Then click a <b>slime</b> to deal damage.",
                      "<b>1)</b> 손패의 <b>작열 송곳니</b>를 클릭.\n<b>2)</b> 그 다음 <b>슬라임</b>을 클릭해 피해를 준다."),
                    TutorialAdvanceTrigger.CardPlayed,
                    TutorialHighlight.HandCard, cardId: "C101"),

                new("defense_dot_rule",
                    L("Play <b>Runic Orb</b> to gain Block.\nBlock soaks damage <b>and</b> Poison · Burn · Bleed.",
                      "<b>룬 보주</b>를 사용해 방어막을 얻는다.\n방어막은 피해 <b>그리고</b> 독·화상·출혈도 막아준다."),
                    TutorialAdvanceTrigger.CardPlayed,
                    TutorialHighlight.HandCard, cardId: "C102"),

                new("summon_dino",
                    L("Play <b>Raptor</b> to summon it onto your field.\nSword badge = ATK. They <b>auto-attack at turn end</b>.",
                      "<b>랩터</b>를 사용해 필드에 소환한다.\n검 뱃지가 공격력 — <b>턴 종료마다 자동 공격</b>한다."),
                    TutorialAdvanceTrigger.SummonPlaced,
                    TutorialHighlight.HandCard, cardId: "C004"),

                new("manual_attack",
                    L("Pick the target yourself —\n<b>1)</b> click the <b>sword badge</b> → <b>2)</b> a <b>slime</b>.",
                      "타겟을 직접 고르려면 —\n<b>1)</b> <b>검 뱃지</b> 클릭 → <b>2)</b> <b>슬라임</b> 클릭."),
                    TutorialAdvanceTrigger.SummonAttacked,
                    TutorialHighlight.SwordBadge),

                new("end_turn_first",
                    L("Out of mana? Press <b>END TURN</b> (bottom-right).\nThe icon above an enemy is its <b>next move</b>.",
                      "마나가 없으면 우하단 <b>END TURN</b>을 누른다.\n적 머리 위 아이콘이 <b>다음 행동</b>이다."),
                    TutorialAdvanceTrigger.TurnEnded,
                    TutorialHighlight.EndTurnButton),

                new("second_dino",
                    L("Summon <b>another Raptor</b>.\nFusion needs <b>two of the same species</b>.",
                      "<b>랩터 한 마리 더</b>.\n융합은 <b>같은 종 두 마리</b>가 필요하다."),
                    TutorialAdvanceTrigger.SummonPlaced,
                    TutorialHighlight.HandCard, cardId: "C004"),

                new("fusion",
                    L("<b>1)</b> Click <b>Fusion Sigil</b> in your hand.\n<b>2)</b> Pick <b>two Raptors</b> (from hand or field).",
                      "<b>1)</b> 손패의 <b>융합의 각인</b>을 클릭.\n<b>2)</b> <b>랩터 두 마리</b>를 고른다(손패/필드 어느 쪽이든)."),
                    TutorialAdvanceTrigger.FusionResolved,
                    TutorialHighlight.HandCard, cardId: "C152"),

                new("skill_use",
                    L("<b>Alpha Raptor</b>! Signature <b>Pack Slash</b> ready.\n<b>1)</b> click the <b>skill icon</b> → <b>2)</b> a <b>slime</b>.",
                      "<b>알파 랩터</b>! 시그니처 <b>연격</b> 준비.\n<b>1)</b> <b>스킬 아이콘</b> 클릭 → <b>2)</b> <b>슬라임</b> 클릭."),
                    TutorialAdvanceTrigger.SkillUsed,
                    TutorialHighlight.SkillBadge),

                new("potion_use",
                    L("Top-bar <b>Potion</b> → <b>1)</b> click it → <b>2)</b> press <b>Drink</b>.\nBlock carries to next turn.",
                      "상단 <b>포션</b> → <b>1)</b> 클릭 → <b>2)</b> <b>마시기</b>.\n방어막은 다음 턴까지 유지된다."),
                    TutorialAdvanceTrigger.PotionUsed,
                    TutorialHighlight.PotionIcon),

                // farewell — 5초 후 자동 사라짐. 이 단계 동안 모든 게이트 해제(자유 플레이).
                // 사라진 뒤 잔여 턴은 평소처럼 진행, 슬라임 처치 시 EndTutorial → RelicPick.
                new("farewell",
                    L("Now — <b>finish this battle</b>.",
                      "이제 — <b>전투를 이겨내라</b>."),
                    TutorialAdvanceTrigger.Timer,
                    TutorialHighlight.None,
                    cardId: null,
                    autoAdvanceSeconds: 5f),
            };

            // 강조 단어 색 적용 — 메시지 본문엔 <b>...</b>만 박고, 여기서 일괄로 색 입힌 richText 태그로 변환.
            foreach (var s in steps) s.message = E(s.message);
            return steps;
        }
    }
}
