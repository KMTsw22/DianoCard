using System;

namespace DianoCard.Tutorial
{
    /// <summary>
    /// 튜토리얼 단계 진행을 위한 static 이벤트 허브. 게임 측에서 의미있는 액션
    /// (카드 사용/턴 종료/공룡 소환/융합 발동/전투 승리)이 일어나면 호출.
    /// TutorialManager가 OnEnable에서 구독 — 활성화 안 됐을 땐 invocation list가 비어 비용 0.
    /// </summary>
    public static class TutorialEvents
    {
        public static event Action<int> OnCardPlayed;     // arg: handIndex
        public static event Action OnSummonPlaced;        // 공룡이 필드에 새로 들어옴
        public static event Action OnFusionResolved;      // 융합 발동 완료
        public static event Action OnTurnEnded;
        public static event Action OnBattleWon;

        public static void NotifyCardPlayed(int handIndex) => OnCardPlayed?.Invoke(handIndex);
        public static void NotifySummonPlaced() => OnSummonPlaced?.Invoke();
        public static void NotifyFusionResolved() => OnFusionResolved?.Invoke();
        public static void NotifyTurnEnded() => OnTurnEnded?.Invoke();
        public static void NotifyBattleWon() => OnBattleWon?.Invoke();
    }
}
