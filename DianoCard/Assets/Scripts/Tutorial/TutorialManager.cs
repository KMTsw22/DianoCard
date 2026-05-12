using System.Collections.Generic;
using UnityEngine;

namespace DianoCard.Tutorial
{
    /// <summary>
    /// 튜토리얼 진행 싱글톤. GSM과 같은 GameObject에 attach (AutoAttachUI 패턴).
    /// GSM.EnterTutorial이 Begin() 호출 → 단계 진행 → 마지막 BattleWon 단계 후 EndBattle이 자동으로 EndTutorial 호출.
    /// 도중 스킵하면 GSM.EndTutorial을 강제 호출 (sandbox 전투 폐기 + 진짜 런 복원).
    /// </summary>
    [DefaultExecutionOrder(900)] // GSM(기본 0)보다 늦게, BattleUI보다 늦게
    public class TutorialManager : MonoBehaviour
    {
        public static TutorialManager Instance { get; private set; }

        private List<TutorialStep> _steps;
        private int _stepIndex = -1;
        public bool IsActive => _stepIndex >= 0 && _steps != null && _stepIndex < _steps.Count;
        public TutorialStep CurrentStep => IsActive ? _steps[_stepIndex] : null;

        // 스킵 확인 모달 표시 플래그. TutorialOverlay가 읽어 모달 그림.
        public bool SkipPromptOpen { get; private set; }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnEnable()
        {
            TutorialEvents.OnCardPlayed += HandleCardPlayed;
            TutorialEvents.OnSummonPlaced += HandleSummonPlaced;
            TutorialEvents.OnFusionResolved += HandleFusionResolved;
            TutorialEvents.OnTurnEnded += HandleTurnEnded;
            TutorialEvents.OnBattleWon += HandleBattleWon;
        }

        void OnDisable()
        {
            TutorialEvents.OnCardPlayed -= HandleCardPlayed;
            TutorialEvents.OnSummonPlaced -= HandleSummonPlaced;
            TutorialEvents.OnFusionResolved -= HandleFusionResolved;
            TutorialEvents.OnTurnEnded -= HandleTurnEnded;
            TutorialEvents.OnBattleWon -= HandleBattleWon;
        }

        /// <summary>GSM.EnterTutorial이 호출. 첫 단계는 즉시 표시(인트로),
        /// 카드/공룡 단계는 sandbox 전투가 시작된 뒤에 자연스럽게 흘러간다.</summary>
        public void Begin()
        {
            _steps = TutorialSteps.BuildCH01();
            _stepIndex = 0;
            SkipPromptOpen = false;
            Debug.Log($"[Tutorial] Begin — {_steps.Count} steps");
        }

        public void NextByUserClick()
        {
            if (!IsActive) return;
            if (CurrentStep.trigger == TutorialAdvanceTrigger.UserClick) AdvanceInternal();
        }

        public void OpenSkipPrompt() => SkipPromptOpen = true;
        public void CloseSkipPrompt() => SkipPromptOpen = false;

        /// <summary>스킵 확정 — 진행도 무관하게 EndTutorial 호출, sandbox 폐기.
        /// PlayerPrefs 완료 플래그는 EndTutorial이 세팅하므로 두 번 다시 안 뜸.</summary>
        public void ConfirmSkip()
        {
            Debug.Log("[Tutorial] Skip confirmed");
            SkipPromptOpen = false;
            _stepIndex = -1;
            _steps = null;
            // GSM.EndTutorial을 직접 호출. sandbox 전투 도중이라 BattleUI가 갑작스럽게
            // 상태 바뀌면 일부 코루틴이 한 프레임 꼬일 수 있지만, 다음 OnGUI에서 자기 정리.
            var gsm = DianoCard.Game.GameStateManager.Instance;
            if (gsm != null && gsm.IsTutorialMode) gsm.EndTutorial();
        }

        private void HandleCardPlayed(int _) => Advance(TutorialAdvanceTrigger.CardPlayed);
        private void HandleSummonPlaced() => Advance(TutorialAdvanceTrigger.SummonPlaced);
        private void HandleFusionResolved() => Advance(TutorialAdvanceTrigger.FusionResolved);
        private void HandleTurnEnded() => Advance(TutorialAdvanceTrigger.TurnEnded);
        private void HandleBattleWon() => Advance(TutorialAdvanceTrigger.BattleWon);

        private void Advance(TutorialAdvanceTrigger trigger)
        {
            if (!IsActive) return;
            // 현재 단계의 트리거와 일치할 때만 진행 — 다른 단계의 알림은 무시.
            if (CurrentStep.trigger == trigger) AdvanceInternal();
        }

        private void AdvanceInternal()
        {
            _stepIndex++;
            if (_stepIndex >= _steps.Count)
            {
                Debug.Log("[Tutorial] All steps consumed");
                _stepIndex = -1;
                _steps = null;
                // 마지막 단계가 BattleWon이라 GSM.EndBattle이 곧 EndTutorial을 호출.
                // 여기선 별도 처리 안 함.
            }
            else
            {
                Debug.Log($"[Tutorial] Advance → step {_stepIndex} ({CurrentStep.id})");
            }
        }
    }
}
