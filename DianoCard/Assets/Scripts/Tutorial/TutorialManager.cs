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
        private float _stepEnterTime;
        public bool IsActive => _stepIndex >= 0 && _steps != null && _stepIndex < _steps.Count;
        public TutorialStep CurrentStep => IsActive ? _steps[_stepIndex] : null;
        public int StepIndex => _stepIndex;
        // Timer 단계는 자유 플레이 — 모든 게이트 해제. 마지막 "전투 마무리하기" 안내용.
        public bool IsFreePlay => IsActive && CurrentStep.trigger == TutorialAdvanceTrigger.Timer;

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
            TutorialEvents.OnPotionUsed += HandlePotionUsed;
            TutorialEvents.OnSummonAttacked += HandleSummonAttacked;
            TutorialEvents.OnSkillUsed += HandleSkillUsed;
            TutorialEvents.OnRelicHovered += HandleRelicHovered;
            TutorialEvents.OnManaHovered += HandleManaHovered;
        }

        void OnDisable()
        {
            TutorialEvents.OnCardPlayed -= HandleCardPlayed;
            TutorialEvents.OnSummonPlaced -= HandleSummonPlaced;
            TutorialEvents.OnFusionResolved -= HandleFusionResolved;
            TutorialEvents.OnTurnEnded -= HandleTurnEnded;
            TutorialEvents.OnBattleWon -= HandleBattleWon;
            TutorialEvents.OnPotionUsed -= HandlePotionUsed;
            TutorialEvents.OnSummonAttacked -= HandleSummonAttacked;
            TutorialEvents.OnSkillUsed -= HandleSkillUsed;
            TutorialEvents.OnRelicHovered -= HandleRelicHovered;
            TutorialEvents.OnManaHovered -= HandleManaHovered;
        }

        /// <summary>GSM.EnterTutorial이 호출. 첫 단계는 즉시 표시(인트로),
        /// 카드/공룡 단계는 sandbox 전투가 시작된 뒤에 자연스럽게 흘러간다.</summary>
        public void Begin()
        {
            _steps = TutorialSteps.BuildCH01();
            _stepIndex = 0;
            _stepEnterTime = Time.unscaledTime;
            SkipPromptOpen = false;
            Debug.Log($"[Tutorial] Begin — {_steps.Count} steps");
        }

        void Update()
        {
            if (!IsActive) return;
            var step = CurrentStep;
            if (step.trigger == TutorialAdvanceTrigger.Timer
                && step.autoAdvanceSeconds > 0f
                && Time.unscaledTime - _stepEnterTime >= step.autoAdvanceSeconds)
            {
                AdvanceInternal();
            }
        }

        public void NextByUserClick()
        {
            if (!IsActive) return;
            if (CurrentStep.trigger == TutorialAdvanceTrigger.UserClick) AdvanceInternal();
        }

        public void OpenSkipPrompt() => SkipPromptOpen = true;
        public void CloseSkipPrompt() => SkipPromptOpen = false;

        // 안내되지 않은 손패 카드 클릭은 무시. HandCard 강조 단계는 그 카드 ID만 허용,
        // 그 외 단계(UserClick / EndTurnButton / PotionIcon / RelicIcon / None)는 모든 카드 차단.
        // Timer(farewell) 단계는 자유 플레이라 모두 허용.
        public bool IsHandCardAllowed(string cardId)
        {
            if (!IsActive) return true;
            if (IsFreePlay) return true;
            var step = CurrentStep;
            if (step.highlight == TutorialHighlight.HandCard && !string.IsNullOrEmpty(step.highlightCardId))
                return cardId == step.highlightCardId;
            return false;
        }

        // 포션 아이콘/뷰어/마시기 — PotionUsed 트리거 단계, PotionIcon 강조, Timer 자유 플레이에서 허용.
        public bool IsPotionAllowed()
        {
            if (!IsActive) return true;
            if (IsFreePlay) return true;
            var step = CurrentStep;
            return step.trigger == TutorialAdvanceTrigger.PotionUsed
                || step.highlight == TutorialHighlight.PotionIcon;
        }

        // 유물 아이콘 토글 — RelicIcon 강조 단계나 비활성 / Timer 자유 플레이.
        public bool IsRelicViewerAllowed()
        {
            if (!IsActive) return true;
            if (IsFreePlay) return true;
            return CurrentStep.highlight == TutorialHighlight.RelicIcon;
        }

        // 공룡 수동 공격(검 뱃지) — SummonAttacked 실습 단계 + Timer 자유 플레이.
        public bool IsManualSummonAllowed()
        {
            if (!IsActive) return true;
            if (IsFreePlay) return true;
            return CurrentStep.trigger == TutorialAdvanceTrigger.SummonAttacked;
        }

        // 시그니처 스킬 — Manual 공격과 동일 조건 + SkillUsed 트리거 단계(실습 단계)에서도 허용.
        public bool IsSkillUseAllowed()
        {
            if (!IsActive) return true;
            if (IsFreePlay) return true;
            return CurrentStep.trigger == TutorialAdvanceTrigger.SkillUsed;
        }

        // END TURN은 TurnEnded / BattleWon / Timer 자유 플레이에서 허용.
        // 락업 안전망은 BattleUI 쪽에서 "강조된 카드가 손패에 없으면" 별도 허용 처리.
        public bool IsEndTurnAllowed()
        {
            if (!IsActive) return true;
            if (IsFreePlay) return true;
            var trig = CurrentStep.trigger;
            return trig == TutorialAdvanceTrigger.TurnEnded
                || trig == TutorialAdvanceTrigger.BattleWon;
        }

        // 차단 시 BattleUI가 시각/소리 피드백을 띄울 수 있도록 신호. TutorialEvents 경유.
        public void NotifyBlocked() => TutorialEvents.NotifyActionBlocked();

        // GSM.EndTutorial 등 외부에서 강제 종료 — farewell Timer 중 전투가 빨리 끝나는 케이스 대비.
        public void ForceEnd()
        {
            if (_stepIndex < 0 && _steps == null) return;
            _stepIndex = -1;
            _steps = null;
            SkipPromptOpen = false;
        }

        /// <summary>스킵 확정 — 진행도 무관하게 EndTutorial 호출, sandbox 폐기.
        /// 완료 플래그는 EndTutorial이 SaveSystem에 세팅하므로 두 번 다시 안 뜸.</summary>
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
        private void HandlePotionUsed() => Advance(TutorialAdvanceTrigger.PotionUsed);
        private void HandleSummonAttacked() => Advance(TutorialAdvanceTrigger.SummonAttacked);
        private void HandleSkillUsed() => Advance(TutorialAdvanceTrigger.SkillUsed);
        private void HandleRelicHovered() => Advance(TutorialAdvanceTrigger.RelicHovered);
        private void HandleManaHovered() => Advance(TutorialAdvanceTrigger.ManaHovered);

        private void Advance(TutorialAdvanceTrigger trigger)
        {
            if (!IsActive) return;
            // 현재 단계의 트리거와 일치할 때만 진행 — 다른 단계의 알림은 무시.
            if (CurrentStep.trigger == trigger) AdvanceInternal();
        }

        private void AdvanceInternal()
        {
            _stepIndex++;
            _stepEnterTime = Time.unscaledTime;
            if (_stepIndex >= _steps.Count)
            {
                Debug.Log("[Tutorial] All steps consumed");
                _stepIndex = -1;
                _steps = null;
                // 마지막 farewell(Timer) 단계 후 자연스럽게 종료. 전투 자체는 계속.
                // 슬라임 처치 시 GSM.EndBattle → EndTutorial → RelicPick.
            }
            else
            {
                Debug.Log($"[Tutorial] Advance → step {_stepIndex} ({CurrentStep.id})");
            }
        }
    }
}
