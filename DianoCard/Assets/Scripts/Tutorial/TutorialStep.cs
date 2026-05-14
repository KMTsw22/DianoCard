namespace DianoCard.Tutorial
{
    /// <summary>단계 → 다음 단계로 넘어가는 트리거 종류.</summary>
    public enum TutorialAdvanceTrigger
    {
        UserClick,        // 다이얼로그 클릭으로 진행
        CardPlayed,       // 아무 카드든 사용하면 진행
        SummonPlaced,     // 공룡 필드 소환
        FusionResolved,   // 융합 완료
        TurnEnded,        // 턴 종료 버튼
        BattleWon,        // 전투 승리
        PotionUsed,       // 포션 사용 — 어떤 포션이든 OK
        SummonAttacked,   // 공룡 평타(수동) 완료 — CommandSummonAttack 성공
        SkillUsed,        // 진화 공룡 시그니처 스킬 발동 완료
        RelicHovered,     // 상단 유물 아이콘 위에 마우스 — 패시브 패널 본 것으로 간주
        ManaHovered,      // 좌하단 마나 오브 위에 마우스 — 마나 툴팁 본 것으로 간주
        Timer,            // autoAdvanceSeconds 후 자동 진행. 그동안 모든 게이트 해제(자유 플레이).
    }

    /// <summary>화면에서 펄스 글로우로 강조할 대상.</summary>
    public enum TutorialHighlight
    {
        None,
        HandCard,        // 손패 특정 카드 (highlightCardId 필요). 못 찾으면 강조 생략.
        FieldArea,       // 플레이어 필드
        EnemyArea,       // 적 영역
        EndTurnButton,   // 우하단 END TURN
        PotionIcon,      // 상단 바 포션 아이콘 (드로어 열렸으면 자동으로 첫 드로어 슬롯으로 글로우 이전)
        RelicIcon,       // 상단 바 유물 아이콘
        ManaOrb,         // 좌하단 마나 오브
        SwordBadge,      // 공격 가능한 첫 공룡 머리 위 검 뱃지 (좁은 영역)
        SkillBadge,      // 진화 공룡의 시그니처 스킬 아이콘 (좁은 영역)
    }

    public class TutorialStep
    {
        public string id;
        public string message;
        public TutorialAdvanceTrigger trigger;
        public TutorialHighlight highlight;
        public string highlightCardId;   // HandCard일 때 강조할 카드 ID
        public float autoAdvanceSeconds; // Timer 트리거에서만 사용 — 단계 진입 후 N초 뒤 자동 진행

        public TutorialStep(string id, string message, TutorialAdvanceTrigger trigger,
            TutorialHighlight highlight = TutorialHighlight.None, string cardId = null,
            float autoAdvanceSeconds = 0f)
        {
            this.id = id;
            this.message = message;
            this.trigger = trigger;
            this.highlight = highlight;
            this.highlightCardId = cardId;
            this.autoAdvanceSeconds = autoAdvanceSeconds;
        }
    }
}
