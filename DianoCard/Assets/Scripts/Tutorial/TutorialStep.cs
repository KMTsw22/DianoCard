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
        PotionIcon,      // 상단 바 포션 아이콘
        RelicIcon,       // 상단 바 유물 아이콘
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
