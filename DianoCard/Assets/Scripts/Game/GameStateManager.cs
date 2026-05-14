using System.Collections.Generic;
using DianoCard.Data;
using UnityEngine;

namespace DianoCard.Game
{
    public enum GameState
    {
        Lobby,
        CharacterSelect, // 캐릭터 선택 화면
        RelicPick,       // 시작 유물 3종 중 1개 선택
        Map,             // 노드 선택 화면
        Battle,
        Reward,
        Shop,            // 상인 노드 상호작용 화면
        Village,         // 마을(캠프) 노드 상호작용 화면
        Defeat,
        Victory,
        Training,        // 훈련장 — 임의 적을 골라 자유롭게 전투 (승패 무관, 맵/보상 없음)
        AnimationTest,   // 애니메이션 테스트 — Resources/AnimationTest 폴더의 프레임 시퀀스 프리뷰 (에디터 전용 개발 툴)
        TechTree,        // 메타 진행 — 영구 해금 트리. 로비에서만 진입 가능, 임시 UI(MVP 3브랜치 9노드).
        Event,           // 미지(Unknown) 노드 → 이벤트 분기 화면. 선택지에 따라 HP/유물 변동.
        EventRelicOffer, // '룬을 만진다' 후 단일 유물 제안 + HP 더 지불해 리롤 화면.
    }

    /// <summary>
    /// 전역 게임 상태/플로우 관리 싱글톤.
    ///
    /// 챕터 구성 (StS-style 7×15 + 보스):
    /// - Floor 1~15: path-first random walk로 6개 경로를 그린 뒤 그 위에만 노드 배치
    /// - Floor 16: 보스 (15층 모든 노드가 fan-in)
    /// - 1층 = 전부 Combat / 9층 = Treasure / 15층 = Camp(Rest) 고정
    /// - 그 외 층은 저승천 분포(Combat 53 / Elite 8 / Event 22 / Rest 12 / Shop 5)
    ///
    /// UI 컴포넌트(LobbyUI/MapUI/BattleUI/RewardUI/GameOverUI)는 같은 GameObject에
    /// 자동으로 attach되며, 각자 State에 따라 자기 OnGUI를 on/off함.
    /// </summary>
    public class GameStateManager : MonoBehaviour
    {
        public static GameStateManager Instance { get; private set; }

        public GameState State { get; private set; } = GameState.Lobby;
        public RunState CurrentRun { get; private set; }
        public MapState CurrentMap { get; private set; }
        public List<RelicData> RelicPickChoices { get; private set; }
        public ShopState CurrentShop { get; private set; }
        public List<EnemyData> CurrentEnemies { get; private set; } = new();

        /// <summary>훈련장 모드 플래그 — true면 EndBattle이 Reward/Defeat 대신 Training으로 복귀.</summary>
        public bool IsTrainingMode { get; private set; }

        /// <summary>튜토리얼 모드 플래그 — 캐릭터 선택 직후 1회. true면 진짜 RunState는 백업되고
        /// sandbox RunState로 전투. EndBattle 시 EndTutorial() 거쳐 RelicPick으로 복귀.</summary>
        public bool IsTutorialMode { get; private set; }

        // 튜토리얼 진입 시 백업해두는 진짜 런 상태 — EndTutorial 시 그대로 복원.
        private RunState _tutorialSavedRun;
        private MapState _tutorialSavedMap;
        private List<RelicData> _tutorialSavedRelicChoices;

        public const string TutorialCompletedKey = "DianoCard.Tutorial.CH01.Completed";
        public static bool HasCompletedTutorial() => SaveSystem.GetInt(TutorialCompletedKey, 0) == 1;

        /// <summary>영구 메타 진행(테크트리) 상태. PlayerPrefs에서 로드, 노드 해금 시 자동 저장.</summary>
        public TechTreeState TechTree { get; private set; }

        // 이전 코드 호환용 — 리스트의 첫 적 (배경/보상 결정에 사용)
        public EnemyData PrimaryEnemy => CurrentEnemies.Count > 0 ? CurrentEnemies[0] : null;

        // 맵 격자: StS와 동일. 1..15층은 일반 + 16층은 보스(별도 단일 노드).
        // currentFloor=0 은 "아직 어떤 노드도 진입 안 함"이라는 의미로, 1층 노드들이 시작 선택지.
        private const int MapWidth = 7;
        private const int TotalFloors = 15;
        private const int BossFloor = 16;
        private const int PathCount = 6;
        private const int TreasureFloor = 9;
        private const int RestFloor = 15;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // 같은 GameObject에 UI 컴포넌트 자동 부착 (idempotent)
            AutoAttachUI();
        }

        void Start()
        {
            if (!DataManager.Instance.IsLoaded) DataManager.Instance.Load();
            if (TechTree == null) TechTree = new TechTreeState();
            State = GameState.Lobby;
        }

        private void AutoAttachUI()
        {
            if (GetComponent<LobbyUI>() == null) gameObject.AddComponent<LobbyUI>();
            if (GetComponent<CharacterSelectUI>() == null) gameObject.AddComponent<CharacterSelectUI>();
            if (GetComponent<RelicPickerUI>() == null) gameObject.AddComponent<RelicPickerUI>();
            if (GetComponent<MapUI>() == null) gameObject.AddComponent<MapUI>();
            if (GetComponent<BattleUI>() == null) gameObject.AddComponent<BattleUI>();
            if (GetComponent<RewardUI>() == null) gameObject.AddComponent<RewardUI>();
            if (GetComponent<ShopUI>() == null) gameObject.AddComponent<ShopUI>();
            if (GetComponent<VillageUI>() == null) gameObject.AddComponent<VillageUI>();
            if (GetComponent<GameOverUI>() == null) gameObject.AddComponent<GameOverUI>();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (GetComponent<AnimationTestUI>() == null) gameObject.AddComponent<AnimationTestUI>();
#endif
            if (GetComponent<TechTreeUI>() == null) gameObject.AddComponent<TechTreeUI>();
            if (GetComponent<PauseMenuUI>() == null) gameObject.AddComponent<PauseMenuUI>();
            if (GetComponent<EventUI>() == null) gameObject.AddComponent<EventUI>();
            if (GetComponent<EventRelicOfferUI>() == null) gameObject.AddComponent<EventRelicOfferUI>();
            if (GetComponent<DianoCard.Tutorial.TutorialManager>() == null) gameObject.AddComponent<DianoCard.Tutorial.TutorialManager>();
            if (GetComponent<DianoCard.Tutorial.TutorialOverlay>() == null) gameObject.AddComponent<DianoCard.Tutorial.TutorialOverlay>();
            if (GetComponent<DianoCard.Tutorial.TutorialTipModal>() == null) gameObject.AddComponent<DianoCard.Tutorial.TutorialTipModal>();
            if (GetComponent<DianoCard.UI.CursorManager>() == null) gameObject.AddComponent<DianoCard.UI.CursorManager>();
        }

        /// <summary>Lobby에서 애니메이션 테스트 화면 진입. Run 상태는 건드리지 않음.</summary>
        public void EnterAnimationTest()
        {
            State = GameState.AnimationTest;
            Debug.Log("[GSM] EnterAnimationTest");
        }

        /// <summary>애니메이션 테스트에서 로비로 복귀.</summary>
        public void ExitAnimationTest()
        {
            State = GameState.Lobby;
            Debug.Log("[GSM] ExitAnimationTest");
        }

        // 테크트리 진입 직전의 상태를 보관 — 어떤 화면(Battle/Map/Village/Lobby 등)에서 들어왔든
        // ExitTechTree에서 그 화면으로 정확히 복귀시키기 위함.
        private GameState _stateBeforeTechTree = GameState.Lobby;

        /// <summary>현재 화면에서 테크트리 화면으로 진입. 복귀 시 원래 화면으로 돌아감.</summary>
        public void EnterTechTree()
        {
            if (TechTree == null) TechTree = new TechTreeState();
            if (State != GameState.TechTree) _stateBeforeTechTree = State;
            TechTree.hasNewPoints = false;
            State = GameState.TechTree;
            Debug.Log($"[GSM] EnterTechTree (from {_stateBeforeTechTree})");
        }

        /// <summary>테크트리에서 진입했던 이전 화면으로 복귀.</summary>
        public void ExitTechTree()
        {
            State = _stateBeforeTechTree;
            Debug.Log($"[GSM] ExitTechTree → {State}");
        }

        // =========================================================
        // Run 시작
        // =========================================================

        public void StartNewRun()
        {
            CurrentRun = new RunState
            {
                playerMaxHp = 70,
                playerCurrentHp = 70,
                gold = 50,
                deck = new List<CardData>(), // 캐릭터 확정 시 채움
                relics = new List<RelicData>(),
                potions = new List<PotionData>(),
                currentFloor = 0,
                chapterId = "CH01",
                characterId = "CH002",  // 1차 출시: Arkane(아케네) 단일 캐릭터
            };

            // 캐릭터 선택 화면을 먼저 보여주고, 거기서 확인하면 맵을 생성한다
            State = GameState.CharacterSelect;
        }

        /// <summary>
        /// 캐릭터 선택을 확정 → 시작 덱 빌드 + 맵 생성 후 RelicPick 상태로 전환.
        /// </summary>
        public void ConfirmCharacterSelection(string characterId = null)
        {
            if (CurrentRun == null)
            {
                Debug.LogError("[GSM] ConfirmCharacterSelection: CurrentRun is null");
                return;
            }

            if (!string.IsNullOrEmpty(characterId)) CurrentRun.characterId = characterId;
            CurrentRun.deck = BuildStarterDeck(CurrentRun.characterId);
            CurrentMap = GenerateMap(CurrentRun.chapterId);
            RelicPickChoices = BuildRelicPickChoices(CurrentRun, 3);
            _relicPickReturnState = GameState.Map;

            // 튜토리얼 미완료면 진짜 RunState는 그대로 백업하고 sandbox 전투로 분기.
            // EndTutorial이 백업을 복원하면서 RelicPick 상태로 넘김.
            if (!HasCompletedTutorial())
            {
                EnterTutorial();
                return;
            }

            State = GameState.RelicPick;
        }

        // =========================================================
        // 튜토리얼 (캐릭터 선택 직후 1회, sandbox 전투)
        //
        // 진짜 RunState/MapState/RelicPickChoices를 백업해두고, 격리된 sandbox
        // RunState로 슬라임 2마리(E001×2)와 전투. sandbox 덱은 정확히 5장:
        //   - C101 작열 송곳니 ×1 (공격, 1코 5데미지)
        //   - C102 룬 보주 ×1 (방어, 1코 +6 블록)
        //   - C004 랩터 ×2 (육식 T0 — 융합 재료)
        //   - C152 융합의 각인 ×1 (융합 발동)
        // 마나 3 고정이라 한 턴에 최대 3장 사용 → END TURN 단계가 학습 흐름에 자연스럽게 들어감.
        // 슬라임 스케일은 BattleUI가 IsTutorialMode 보고 hp×0.35, dmg×0.5로 약화.
        // 시그니처 스킬은 작동하면 자연 학습, MVP에서 미작동이면 단계 텍스트가 톤다운.
        // 전투 종료(EndBattle) 시 IsTutorialMode 분기가 EndTutorial을 호출, 진짜 런 복원.
        // =========================================================

        private void EnterTutorial()
        {
            _tutorialSavedRun = CurrentRun;
            _tutorialSavedMap = CurrentMap;
            _tutorialSavedRelicChoices = RelicPickChoices;

            CurrentRun = BuildTutorialRun(_tutorialSavedRun.characterId);
            CurrentMap = null;
            RelicPickChoices = null;

            CurrentEnemies.Clear();
            var slime = DataManager.Instance.GetEnemy("E001");
            if (slime != null)
            {
                CurrentEnemies.Add(slime);
                CurrentEnemies.Add(slime);
            }

            IsTutorialMode = true;
            State = GameState.Battle;
            Debug.Log("[GSM] EnterTutorial — sandbox battle vs 슬라임 ×2");

            // TutorialManager는 GSM과 같은 GameObject에 AutoAttach됨.
            // Awake 순서상 같은 프레임에 이미 attach 완료 — Instance null 체크는 안전망.
            if (DianoCard.Tutorial.TutorialManager.Instance != null)
                DianoCard.Tutorial.TutorialManager.Instance.Begin();
        }

        /// <summary>튜토리얼 종료 — sandbox 폐기, 진짜 RunState/Map/유물선택지 복원, RelicPick 진입.</summary>
        public void EndTutorial()
        {
            SaveSystem.SetInt(TutorialCompletedKey, 1);
            SaveSystem.Save();

            CurrentRun = _tutorialSavedRun;
            CurrentMap = _tutorialSavedMap;
            RelicPickChoices = _tutorialSavedRelicChoices;
            _tutorialSavedRun = null;
            _tutorialSavedMap = null;
            _tutorialSavedRelicChoices = null;

            CurrentEnemies.Clear();
            IsTutorialMode = false;
            // farewell(Timer) 단계 도중 전투가 끝나도 오버레이가 RelicPick 위로 깜빡이지 않게
            // TutorialManager 상태도 함께 강제 리셋.
            DianoCard.Tutorial.TutorialManager.Instance?.ForceEnd();
            State = GameState.RelicPick;
            Debug.Log("[GSM] EndTutorial — restored real run, → RelicPick");
        }

        // 슬라임 2마리에 맞춰 깎은 sandbox RunState. HP 낮고 덱 작게.
        private RunState BuildTutorialRun(string characterId)
        {
            var deck = new List<CardData>();
            var dm = DataManager.Instance;
            void Add(string id, int n = 1)
            {
                var c = dm.GetCard(id);
                if (c == null) { Debug.LogWarning($"[GSM] Tutorial deck missing card '{id}'"); return; }
                for (int i = 0; i < n; i++) deck.Add(c);
            }
            Add("C101", 1); // 작열 송곳니 — 1코 5데미지 (공격)
            Add("C102", 1); // 룬 보주 — 1코 +6 블록 (방어)
            Add("C004", 2); // 랩터 ×2 — 융합 재료 (육식 T0)
            Add("C152", 1); // 융합의 각인 — 융합 발동

            // 튜토리얼 sandbox에 포션/유물 1개씩 시드. 사용·확인 단계에서 가르친다.
            var potions = new List<PotionData>();
            var defensePotion = dm.GetPotion("P004"); // 방어 물약 — SELF 타겟, 즉시 +10 블록
            if (defensePotion != null) potions.Add(defensePotion);
            else Debug.LogWarning("[GSM] Tutorial sandbox missing potion 'P004'");

            var relics = new List<RelicData>();
            var ancientBone = dm.GetRelic("R001"); // 고대의 뼈 — START PASSIVE +10 MAX HP
            if (ancientBone != null) relics.Add(ancientBone);
            else Debug.LogWarning("[GSM] Tutorial sandbox missing relic 'R001'");

            var run = new RunState
            {
                playerMaxHp = 40,
                playerCurrentHp = 40,
                gold = 0,
                deck = deck,
                relics = relics,
                potions = potions,
                currentFloor = 0,
                chapterId = "CH01",
                characterId = characterId ?? "CH002",
            };

            // 유물 PASSIVE 효과(MAX_HP 등)는 RunState 빌드 직후 OnAcquired로 단일 경로 적용.
            if (ancientBone != null) RelicEffects.OnAcquired(run, ancientBone);
            return run;
        }

        /// <summary>
        /// 유물 선택 화면에서 선택 완료 → 선택한 유물 획득 후 returnState 로 전환.
        /// chosen이 null이면 아무 유물도 주지 않고 넘어감(선택지가 없는 경우 폴백).
        /// </summary>
        public void ConfirmRelicPick(RelicData chosen)
        {
            if (chosen != null && CurrentRun != null)
            {
                if (!CurrentRun.relics.Contains(chosen))
                {
                    CurrentRun.relics.Add(chosen);
                    CurrentRun.hasNewRelic = true;
                    RelicEffects.OnAcquired(CurrentRun, chosen);
                }
            }
            RelicPickChoices = null;

            // 보물 노드에서 진입한 경우엔 returnState 대신 노드 클리어 + 다음 층 진행을 수행한다.
            if (_relicPickAdvancesMap)
            {
                _relicPickAdvancesMap = false;
                if (CurrentMap != null)
                {
                    var node = CurrentMap.nodes.Find(n =>
                        n.floor == CurrentMap.currentFloor && n.column == CurrentMap.currentColumn);
                    if (node != null) node.cleared = true;
                    AdvanceToNextFloorOrVictory();
                    return;
                }
            }

            State = _relicPickReturnState;
        }

        private GameState _relicPickReturnState = GameState.Map;
        private bool _relicPickAdvancesMap = false;

        /// <summary>
        /// 이벤트 노드 등 외부에서 직접 유물 선택 화면을 열 때 사용.
        /// returnState에는 선택 완료 후 돌아갈 상태를 지정 (기본값 Map).
        /// </summary>
        public void EnterRelicPick(List<RelicData> choices, GameState returnState = GameState.Map)
        {
            RelicPickChoices = choices ?? new List<RelicData>();
            _relicPickReturnState = returnState;
            _relicPickAdvancesMap = false;
            State = GameState.RelicPick;
        }

        // ─── 이벤트 노드 ────────────────────────────────────────────
        // MVP: 단일 하드코딩 이벤트 "부서진 룬 제단".
        //   choice 0 = 룬을 만진다 → HP -3, 단일 유물 제안 화면(EventRelicOffer)
        //                            → 받기 / HP 더 내고 리롤 (에스컬레이팅 -5/-7/-9 …)
        //   choice 1 = 그냥 지나간다 → 무효과 + 다음 층
        // HP 0 시 Defeat. 리롤은 다음 비용(EventRerollCost)을 못 내면 UI에서 비활성.

        private MapNode _pendingEventNode;
        public MapNode PendingEventNode => _pendingEventNode;

        // 단일 유물 제안 상태.
        public RelicData EventOfferedRelic { get; private set; }
        // 다음 리롤에 들 HP 비용. 진입 시 5, 리롤할 때마다 +2.
        public int EventRerollCost { get; private set; }
        // 한 이벤트 동안 이미 보여줬던 유물(리롤 시 같은 것 안 나오게).
        private readonly HashSet<string> _eventShownRelicIds = new();
        private const int EventInitialRerollCost = 5;
        private const int EventRerollCostStep = 2;

        public void ResolveEventChoice(int choiceIdx)
        {
            if (_pendingEventNode == null || State != GameState.Event)
            {
                Debug.LogWarning($"[GSM] ResolveEventChoice({choiceIdx}) ignored — no pending event / wrong state ({State})");
                return;
            }

            var node = _pendingEventNode;

            if (choiceIdx == 0)
            {
                int prevHp = CurrentRun.playerCurrentHp;
                CurrentRun.playerCurrentHp = Mathf.Max(0, prevHp - 3);
                Debug.Log($"[GSM] Event '부서진 룬 제단' → 룬을 만진다: HP {prevHp} → {CurrentRun.playerCurrentHp}");

                if (CurrentRun.playerCurrentHp <= 0)
                {
                    node.cleared = true;
                    _pendingEventNode = null;
                    State = GameState.Defeat;
                    return;
                }

                // 단일 유물 제안 화면으로 진입. 노드/이벤트 컨텍스트는 Accept/Reroll 까지 유지.
                _eventShownRelicIds.Clear();
                var first = RollSingleEventRelic();
                if (first == null)
                {
                    // 풀이 비면(이미 모든 풀 유물 보유) 디폴트 폴백 유물 — 안 비도록 보장.
                    Debug.LogWarning("[GSM] EventRelicOffer: ALL pools exhausted — using default relic as fallback.");
                    first = FindAnyUnownedRelic();
                    if (first == null)
                    {
                        Debug.LogWarning("[GSM] EventRelicOffer: even fallback pool empty — advancing without reward.");
                        node.cleared = true;
                        _pendingEventNode = null;
                        AdvanceToNextFloorOrVictory();
                        return;
                    }
                }

                EnsureEventRelicOfferUIAttached();

                EventOfferedRelic = first;
                _eventShownRelicIds.Add(first.id);
                EventRerollCost = EventInitialRerollCost;
                State = GameState.EventRelicOffer;
                Debug.Log($"[GSM] EventRelicOffer entered → offered={first.id} ({first.name}), nextRerollCost={EventRerollCost}");
                return;
            }

            // choice 1 = 그냥 지나간다
            Debug.Log("[GSM] Event '부서진 룬 제단' → 그냥 지나간다");
            node.cleared = true;
            _pendingEventNode = null;
            AdvanceToNextFloorOrVictory();
        }

        /// <summary>제안된 유물을 받고 노드 클리어 + 다음 층.</summary>
        public void AcceptEventRelic()
        {
            if (State != GameState.EventRelicOffer || _pendingEventNode == null)
            {
                Debug.LogWarning($"[GSM] AcceptEventRelic ignored — wrong state {State}");
                return;
            }

            var relic = EventOfferedRelic;
            if (relic != null && CurrentRun != null && !CurrentRun.relics.Contains(relic))
            {
                CurrentRun.relics.Add(relic);
                CurrentRun.hasNewRelic = true;
                RelicEffects.OnAcquired(CurrentRun, relic);
                Debug.Log($"[GSM] EventRelicOffer accepted: {relic.id} ({relic.name})");
            }

            var node = _pendingEventNode;
            node.cleared = true;
            ClearEventRelicOfferState();
            AdvanceToNextFloorOrVictory();
        }

        /// <summary>리롤 — 현재 EventRerollCost 만큼 HP 지불 후 새 후보 노출. 비용 부족 시 호출 무시.</summary>
        public void RerollEventRelic()
        {
            if (State != GameState.EventRelicOffer)
            {
                Debug.LogWarning($"[GSM] RerollEventRelic ignored — wrong state {State}");
                return;
            }
            if (CurrentRun == null) return;
            int cost = EventRerollCost;
            if (CurrentRun.playerCurrentHp < cost)
            {
                Debug.Log($"[GSM] RerollEventRelic blocked — HP {CurrentRun.playerCurrentHp} < cost {cost}");
                return;
            }

            int prevHp = CurrentRun.playerCurrentHp;
            CurrentRun.playerCurrentHp = Mathf.Max(0, prevHp - cost);

            // 리롤 비용으로 HP 0이 되면 받기 강제 — 패배 처리 없이 현재 후보 그대로 자동 수락.
            if (CurrentRun.playerCurrentHp <= 0)
            {
                Debug.Log($"[GSM] RerollEventRelic: HP {prevHp} → 0, auto-accepting current offer ({EventOfferedRelic?.id})");
                AcceptEventRelic();
                return;
            }

            var next = RollSingleEventRelic();
            if (next == null)
            {
                // 풀 고갈 — 현재 후보 유지하고 비용만 부담 (UI에서 안내 가능). 비용은 한 단계 올리지 않음.
                Debug.Log($"[GSM] RerollEventRelic: pool exhausted, keeping current offer ({EventOfferedRelic?.id})");
                return;
            }

            EventOfferedRelic = next;
            _eventShownRelicIds.Add(next.id);
            EventRerollCost = cost + EventRerollCostStep;
            Debug.Log($"[GSM] RerollEventRelic: HP {prevHp}→{CurrentRun.playerCurrentHp}, new offer={next.id}, next cost={EventRerollCost}");
        }

        private void ClearEventRelicOfferState()
        {
            EventOfferedRelic = null;
            EventRerollCost = 0;
            _eventShownRelicIds.Clear();
            _pendingEventNode = null;
        }

        // 이미 보여준 id를 제외하고, 보유하지 않은 START 풀에서 1개 균등 랜덤.
        private RelicData RollSingleEventRelic()
        {
            var dm = DianoCard.Data.DataManager.Instance;
            if (dm == null || CurrentRun == null) return null;

            string archetype = null;
            if (!string.IsNullOrEmpty(CurrentRun.characterId))
            {
                var ch = dm.GetCharacter(CurrentRun.characterId);
                if (ch != null) archetype = ch.archetype;
            }

            // 우선 EVENT 풀에서 뽑되, 비면 ELITE → BOSS → SHOP → START 순으로 폴백.
            // (보유하지 않은 ELITE/BOSS 유물도 이벤트 노드에서 등장 가능하도록 풀 통합 — 한 런에
            // 거의 모든 ELITE/BOSS 유물 획득 가능하게 하는 조치.)
            var first = TryRollByPool(DianoCard.Data.RelicSource.EVENT, archetype);
            Debug.Log($"[GSM] RollSingleEventRelic: EVENT pool → {first?.id ?? "<empty>"} (archetype={archetype ?? "<null>"}, owned={CurrentRun.relics.Count}, shown={_eventShownRelicIds.Count})");
            if (first != null) return first;
            var elite = TryRollByPool(DianoCard.Data.RelicSource.ELITE, archetype);
            Debug.Log($"[GSM] RollSingleEventRelic: ELITE fallback → {elite?.id ?? "<empty>"}");
            if (elite != null) return elite;
            var boss = TryRollByPool(DianoCard.Data.RelicSource.BOSS, archetype);
            Debug.Log($"[GSM] RollSingleEventRelic: BOSS fallback → {boss?.id ?? "<empty>"}");
            if (boss != null) return boss;
            var shop = TryRollByPool(DianoCard.Data.RelicSource.SHOP, archetype);
            Debug.Log($"[GSM] RollSingleEventRelic: SHOP fallback → {shop?.id ?? "<empty>"}");
            if (shop != null) return shop;
            var start = TryRollByPool(DianoCard.Data.RelicSource.START, archetype);
            Debug.Log($"[GSM] RollSingleEventRelic: START fallback → {start?.id ?? "<empty>"}");
            return start;
        }

        // 모든 source 풀에서 보유하지 않은 첫 유물 — 정말 모든 1차 풀이 비었을 때 마지막 보루.
        private RelicData FindAnyUnownedRelic()
        {
            var dm = DianoCard.Data.DataManager.Instance;
            if (dm == null || CurrentRun == null) return null;
            foreach (var kv in dm.Relics)
            {
                var r = kv.Value;
                if (r == null) continue;
                if (CurrentRun.relics.Contains(r)) continue;
                if (_eventShownRelicIds.Contains(r.id)) continue;
                return r;
            }
            return null;
        }

        // EventRelicOfferUI 안전망 — Awake에서 못 붙은 경우(코드 hot-reload 등) 진입 직전에 강제 attach.
        private void EnsureEventRelicOfferUIAttached()
        {
            if (GetComponent<EventRelicOfferUI>() == null)
            {
                Debug.LogWarning("[GSM] EventRelicOfferUI not attached — adding now (was missed by AutoAttachUI).");
                gameObject.AddComponent<EventRelicOfferUI>();
            }
        }

        private RelicData TryRollByPool(DianoCard.Data.RelicSource source, string archetype)
        {
            var dm = DianoCard.Data.DataManager.Instance;
            if (dm == null) return null;

            var pool = new List<RelicData>();
            foreach (var kv in dm.Relics)
            {
                var r = kv.Value;
                if (r == null || r.source != source) continue;
                if (!string.IsNullOrEmpty(r.archetypeLock)
                    && !string.IsNullOrEmpty(archetype)
                    && !r.archetypeLock.Equals(archetype, System.StringComparison.OrdinalIgnoreCase))
                    continue;
                if (CurrentRun.relics.Contains(r)) continue;
                if (_eventShownRelicIds.Contains(r.id)) continue;
                pool.Add(r);
            }

            if (pool.Count == 0) return null;
            return pool[UnityEngine.Random.Range(0, pool.Count)];
        }

        private static List<RelicData> BuildRelicPickChoices(RunState run, int count)
        {
            var dm = DianoCard.Data.DataManager.Instance;
            if (dm == null) return new List<RelicData>();

            string archetype = null;
            if (!string.IsNullOrEmpty(run.characterId))
            {
                var character = dm.GetCharacter(run.characterId);
                if (character != null) archetype = character.archetype;
            }

            var pool = new List<RelicData>();
            foreach (var kv in dm.Relics)
            {
                var r = kv.Value;
                if (r == null || r.source != DianoCard.Data.RelicSource.START) continue;
                if (!string.IsNullOrEmpty(r.archetypeLock)
                    && !string.IsNullOrEmpty(archetype)
                    && !r.archetypeLock.Equals(archetype, System.StringComparison.OrdinalIgnoreCase))
                    continue;
                if (run.relics.Contains(r)) continue;
                pool.Add(r);
            }

            // Fisher-Yates shuffle then take up to count
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }
            return pool.Count <= count ? pool : pool.GetRange(0, count);
        }

        // 보물 노드용 — 시작 유물 풀(START, 전부 COMMON)을 쓰면 후반엔 풀이 마르고
        // 마르면 RelicPickerUI가 즉시 null 확정 → 노드가 그냥 스킵되는 버그가 됨.
        // 그래서 START를 제외한 비-START 풀에서 RARE 50% / UNCOMMON 35% / COMMON 15% 가중으로 뽑는다.
        private static List<RelicData> BuildTreasureRelicChoices(RunState run, int count)
        {
            var dm = DianoCard.Data.DataManager.Instance;
            if (dm == null) return new List<RelicData>();

            string archetype = null;
            if (!string.IsNullOrEmpty(run.characterId))
            {
                var character = dm.GetCharacter(run.characterId);
                if (character != null) archetype = character.archetype;
            }

            var common   = new List<RelicData>();
            var uncommon = new List<RelicData>();
            var rare     = new List<RelicData>();

            foreach (var kv in dm.Relics)
            {
                var r = kv.Value;
                if (r == null) continue;
                if (r.source == RelicSource.START) continue;
                if (!string.IsNullOrEmpty(r.archetypeLock)
                    && !string.IsNullOrEmpty(archetype)
                    && !r.archetypeLock.Equals(archetype, System.StringComparison.OrdinalIgnoreCase))
                    continue;
                if (run.relics.Contains(r)) continue;
                switch (r.rarity)
                {
                    case Rarity.RARE:     rare.Add(r); break;
                    case Rarity.UNCOMMON: uncommon.Add(r); break;
                    default:              common.Add(r); break;
                }
            }

            const int wCommon = 15, wUncommon = 35, wRare = 50;
            int total = wCommon + wUncommon + wRare;

            var picked = new List<RelicData>();
            for (int i = 0; i < count; i++)
            {
                int roll = UnityEngine.Random.Range(0, total);
                List<RelicData> bucket =
                    roll < wCommon ? common
                  : roll < wCommon + wUncommon ? uncommon
                  : rare;

                // 굴린 등급이 비면 RARE → UNCOMMON → COMMON 순으로 다운그레이드.
                if (bucket.Count == 0)
                {
                    if (rare.Count > 0) bucket = rare;
                    else if (uncommon.Count > 0) bucket = uncommon;
                    else if (common.Count > 0) bucket = common;
                    else break;
                }

                int idx = UnityEngine.Random.Range(0, bucket.Count);
                picked.Add(bucket[idx]);
                bucket.RemoveAt(idx);
            }

            // 마지막 안전망: 비-START 풀이 완전히 마른 극단적인 경우엔 START 잔여로 보충해서
            // 최소 1장은 보장 → UI auto-skip 버그 재발 차단.
            if (picked.Count == 0)
            {
                foreach (var kv in dm.Relics)
                {
                    var r = kv.Value;
                    if (r == null || r.source != RelicSource.START) continue;
                    if (!string.IsNullOrEmpty(r.archetypeLock)
                        && !string.IsNullOrEmpty(archetype)
                        && !r.archetypeLock.Equals(archetype, System.StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (run.relics.Contains(r)) continue;
                    picked.Add(r);
                    if (picked.Count >= count) break;
                }
            }

            return picked;
        }

        // 캐릭터 archetype별 시작 덱 구성.
        // HERB(초식 조련사): 트리/스테고 각 2장(덮어쓰기 보장) + 마법·버프
        // CARN(육식 사냥꾼): 랩터/카르노 각 2장(합성 보장) + 마법·버프
        public static readonly Dictionary<string, Dictionary<string, int>> StarterDecksByArchetype = new()
        {
            ["HERB"] = new()
            {
                { "C001", 2 }, // 트리케라톱스 x2 (덮어쓰기 재료)
                { "C002", 2 }, // 스테고사우루스 x2 (덮어쓰기 재료)
                { "C101", 1 }, // 공격 마법
                { "C102", 2 }, // 방어 마법
                { "C201", 1 }, // 공격 강화
                { "C202", 1 }, // 전체 힐
                { "C156", 1 }, // 증원 소집 — 보유 공룡 T0 1장 손패 소환 (이번 전투 한정)
            },
            ["CARN"] = new()
            {
                { "C004", 2 }, // 랩터 x2 (융합 재료)
                { "C005", 2 }, // 카르노타우루스 x2 (융합 재료)
                { "C101", 1 }, // 공격 마법
                { "C102", 2 }, // 방어 마법
                { "C201", 1 }, // 공격 강화
                { "C152", 1 }, // 융합의 각인 — 첫 런에서 T0+T0→T1 경험 보장
                { "C156", 1 }, // 증원 소집 — 보유 공룡 T0 1장 손패 소환 (이번 전투 한정)
            },
        };

        /// <summary>현재 실행 중인 런의 archetype에서 시작 덱 카드 id 집합을 반환.</summary>
        public static HashSet<string> GetStarterCardIdsFor(string archetype)
        {
            if (archetype != null && StarterDecksByArchetype.TryGetValue(archetype, out var comp))
                return new HashSet<string>(comp.Keys);
            return new HashSet<string>();
        }

        // 오버로드 — 인자 없이 호출 시 Arkane(육식 조련사) 덱을 기본 반환.
        // 1차 출시: 단일 캐릭터(CH002)이므로 폴백도 CH002로 통일.
        private List<CardData> BuildStarterDeck() => BuildStarterDeck("CH002");

        private List<CardData> BuildStarterDeck(string characterId)
        {
            var deck = new List<CardData>();
            var character = DataManager.Instance.GetCharacter(characterId);
            string archetype = character?.archetype ?? "HERB";
            if (!StarterDecksByArchetype.TryGetValue(archetype, out var composition))
            {
                Debug.LogError($"[GameStateManager] Unknown archetype '{archetype}', falling back to HERB");
                composition = StarterDecksByArchetype["HERB"];
            }
            foreach (var kv in composition)
            {
                var c = DataManager.Instance.GetCard(kv.Key);
                if (c == null) { Debug.LogError($"[GameStateManager] Missing card: {kv.Key}"); continue; }
                for (int i = 0; i < kv.Value; i++) deck.Add(c);
            }
            return deck;
        }

        // =========================================================
        // Map 생성
        // =========================================================

        // ---------------------------------------------------------
        // StS-style 맵 생성
        // ---------------------------------------------------------
        // 1. 7×15 격자에 6개의 random-walk 경로를 그린다 (좌/직/우 step, 교차 금지, 첫 두 경로는 시작 x 중복 금지).
        // 2. 경로가 지나간 (floor, column)만 노드로 남기고, 각 노드는 다음 층으로 가는 out-edge(nextColumns)를 갖는다.
        // 3. 16층 보스 단일 노드를 추가, 15층 모든 노드는 보스로 fan-in.
        // 4. 1층=전부 Combat, 9층=Treasure, 15층=Camp(Rest), 16층=Boss로 고정.
        //    그 외 층은 저승천 분포(53/8/22/12/5)로 굴리고 인접/시블링/층 제약을 검증.
        // 5. Combat/Elite 노드에 적 ID 채우기.
        private MapState GenerateMap(string chapterId)
        {
            var map = new MapState { currentFloor = 1, totalFloors = TotalFloors };

            var chapter = DataManager.Instance.GetChapter(chapterId);
            if (chapter == null)
            {
                Debug.LogError($"[GameStateManager] Chapter not found: {chapterId}");
                return map;
            }

            var paths = GeneratePaths();
            BuildNodesFromPaths(map, paths);
            FixTopCampfireRow(map);
            AddBossNode(map, chapter);
            AssignRoomTypes(map);
            FillEnemyIds(map, chapter);
            return map;
        }

        // 보스 직전 휴식 행(floor 15)을 col {1, 3, 5}로 고정. 시각 보정용.
        // 경로가 어느 컬럼으로 수렴했든 상관없이 항상 좌/중/우 3개 캠프가 균일하게 배치되어
        // "안 이뻐" 케이스(한쪽으로 몰리거나 1개만 남는 등)를 방지한다.
        // floor 14 노드들은 거리 ≤ 2 인 모든 캠프로 fan-out (없으면 가장 가까운 1개로 fallback).
        private static readonly int[] TopCampfireCols = { 1, 3, 5 };

        private static void FixTopCampfireRow(MapState map)
        {
            // 1) Floor 15 기존 노드 제거하고 고정 컬럼에 새로 추가
            map.nodes.RemoveAll(n => n.floor == TotalFloors);
            foreach (var col in TopCampfireCols)
            {
                map.nodes.Add(new MapNode { floor = TotalFloors, column = col });
            }

            // 2) Floor 14 노드들의 out-edge를 새 캠프 컬럼들로 다시 연결
            foreach (var n in map.NodesOnFloor(TotalFloors - 1))
            {
                n.nextColumns.Clear();
                foreach (var c in TopCampfireCols)
                {
                    if (Mathf.Abs(c - n.column) <= 2) n.nextColumns.Add(c);
                }
                if (n.nextColumns.Count == 0)
                {
                    // 거리 ≤ 2 캠프가 없으면 가장 가까운 1개라도 (col 0/6 처럼 끝쪽일 때).
                    int nearest = TopCampfireCols[0];
                    int bestDist = Mathf.Abs(TopCampfireCols[0] - n.column);
                    for (int i = 1; i < TopCampfireCols.Length; i++)
                    {
                        int d = Mathf.Abs(TopCampfireCols[i] - n.column);
                        if (d < bestDist) { bestDist = d; nearest = TopCampfireCols[i]; }
                    }
                    n.nextColumns.Add(nearest);
                }
            }
        }

        // 6개 경로의 column 시퀀스(길이 TotalFloors). path[y]는 floor=y+1에서의 column.
        // StS-style 시작 컬럼 보정:
        //   (1) Path 0 → col 0, Path 1 → col 6 무조건 시작 → 1층 양 끝단 노드 항상 보장
        //   (2) Path 2,3은 추가로 unique 시작 → 1층 unique 컬럼 4개 보장
        //   (3) Path 4,5는 위 4개 컬럼 중 하나를 재사용 → 1층 unique 컬럼 수 정확히 4개로 캡 (시각 정돈)
        //   (4) 좌측 절반(col 0~2)와 우측 절반(col 4~6) 양쪽 분포 검증 — 1,2에 의해 자동 충족이지만 안전장치.
        // 같은 (y → y+1) 구간에서 두 경로가 서로 자리를 바꾸는 식의 교차는 금지.
        private const int StartingUniqueRequired = 4;  // 1층 unique 시작 컬럼 정확히 4개로 캡
        private const int MaxPathRegen = 20;

        private static List<int[]> GeneratePaths()
        {
            for (int regen = 0; regen < MaxPathRegen; regen++)
            {
                var paths = new List<int[]>(PathCount);
                var startingXs = new HashSet<int>();
                // 같은 fromFloor의 기존 엣지들을 모아두고, 새 후보가 어떤 기존 엣지와도 교차하지 않는지 검사.
                var edgesByFloor = new Dictionary<int, List<(int fromX, int toX)>>();

                for (int p = 0; p < PathCount; p++)
                {
                    var path = new int[TotalFloors];

                    int startX;
                    if (p == 0)
                    {
                        startX = 0;                  // 양 끝 좌측 강제
                    }
                    else if (p == 1)
                    {
                        startX = MapWidth - 1;       // 양 끝 우측 강제 (7컬럼이면 col 6)
                    }
                    else if (p < StartingUniqueRequired)
                    {
                        // path 2,3은 0/끝 외 unique 시작 (총 4개 unique 보장).
                        int guard = 0;
                        do { startX = Random.Range(0, MapWidth); }
                        while (startingXs.Contains(startX) && ++guard < 64);
                    }
                    else
                    {
                        // path 4,5는 기존 4개 시작 컬럼 중 하나를 재사용 → 1층 unique 컬럼 수를 4로 캡.
                        var existing = new List<int>(startingXs);
                        startX = existing[Random.Range(0, existing.Count)];
                    }
                    startingXs.Add(startX);
                    path[0] = startX;

                    int x = startX;
                    for (int y = 0; y < TotalFloors - 1; y++)
                    {
                        var candidates = new List<int>(3);
                        int cm = Mathf.Clamp(x - 1, 0, MapWidth - 1);
                        int cz = x;
                        int cp = Mathf.Clamp(x + 1, 0, MapWidth - 1);
                        candidates.Add(cm);
                        if (cz != cm) candidates.Add(cz);
                        if (cp != cz && cp != cm) candidates.Add(cp);
                        ShuffleInPlace(candidates);

                        int chosen = -1;
                        foreach (var nx in candidates)
                        {
                            if (WouldCross(edgesByFloor, y, x, nx)) continue;
                            chosen = nx;
                            break;
                        }
                        if (chosen < 0) chosen = x; // 모두 교차 시 수직 fallback — (x,x)는 어떤 valid 엣지와도 교차하지 않음

                        if (!edgesByFloor.TryGetValue(y, out var list))
                        {
                            list = new List<(int, int)>();
                            edgesByFloor[y] = list;
                        }
                        list.Add((x, chosen));

                        path[y + 1] = chosen;
                        x = chosen;
                    }

                    paths.Add(path);
                }

                // 시작 컬럼이 좌(0~2)/우(4~6) 양쪽에 최소 1개씩 분포하는지 확인. 미달이면 재생성.
                bool hasLeft = false, hasRight = false;
                foreach (var sx in startingXs)
                {
                    if (sx <= 2) hasLeft = true;
                    else if (sx >= 4) hasRight = true;
                }
                if (hasLeft && hasRight) return paths;
            }

            // fallback: 재생성 한도 초과 — 마지막 시도 결과라도 반환되도록 한 번 더 그냥 생성
            return GeneratePathsFallback();
        }

        // MaxPathRegen 한도 초과 시(확률적으로 거의 발생 안 함) 무조건 한 번 돌려서 반환.
        private static List<int[]> GeneratePathsFallback()
        {
            var paths = new List<int[]>(PathCount);
            var startingXs = new HashSet<int>();
            var edgesByFloor = new Dictionary<int, List<(int fromX, int toX)>>();

            for (int p = 0; p < PathCount; p++)
            {
                var path = new int[TotalFloors];

                int startX;
                if (p == 0) startX = 0;
                else if (p == 1) startX = MapWidth - 1;
                else if (p < StartingUniqueRequired)
                {
                    int guard = 0;
                    do { startX = Random.Range(0, MapWidth); }
                    while (startingXs.Contains(startX) && ++guard < 64);
                }
                else
                {
                    var existing = new List<int>(startingXs);
                    startX = existing[Random.Range(0, existing.Count)];
                }
                startingXs.Add(startX);
                path[0] = startX;

                int x = startX;
                for (int y = 0; y < TotalFloors - 1; y++)
                {
                    var candidates = new List<int>(3);
                    int cm = Mathf.Clamp(x - 1, 0, MapWidth - 1);
                    int cz = x;
                    int cp = Mathf.Clamp(x + 1, 0, MapWidth - 1);
                    candidates.Add(cm);
                    if (cz != cm) candidates.Add(cz);
                    if (cp != cz && cp != cm) candidates.Add(cp);
                    ShuffleInPlace(candidates);

                    int chosen = -1;
                    foreach (var nx in candidates)
                    {
                        if (WouldCross(edgesByFloor, y, x, nx)) continue;
                        chosen = nx;
                        break;
                    }
                    if (chosen < 0) chosen = x;

                    if (!edgesByFloor.TryGetValue(y, out var list))
                    {
                        list = new List<(int, int)>();
                        edgesByFloor[y] = list;
                    }
                    list.Add((x, chosen));

                    path[y + 1] = chosen;
                    x = chosen;
                }

                paths.Add(path);
            }

            return paths;
        }

        // 같은 층 사이(y → y+1)에서 두 경로가 X자로 교차하는지 판정.
        // 한 경로는 왼→오른쪽으로, 다른 경로는 오른→왼쪽으로 가며 자리를 바꾸는 케이스만 막으면 충분.
        private static bool WouldCross(Dictionary<int, List<(int fromX, int toX)>> edgesByFloor, int fromFloor, int a1, int a2)
        {
            if (!edgesByFloor.TryGetValue(fromFloor, out var list)) return false;
            foreach (var (b1, b2) in list)
            {
                if (a1 < b1 && a2 > b2) return true;
                if (a1 > b1 && a2 < b2) return true;
            }
            return false;
        }

        private static void ShuffleInPlace<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        // path 시퀀스들을 dedupe하여 노드 그래프로 변환. 같은 (floor, column)이 여러 경로에 등장하면
        // 한 노드를 공유하고, out-edge(nextColumns)는 합집합으로 누적된다.
        private static void BuildNodesFromPaths(MapState map, List<int[]> paths)
        {
            var nodeByPos = new Dictionary<(int floor, int column), MapNode>();

            foreach (var path in paths)
            {
                for (int y = 0; y < path.Length; y++)
                {
                    int floor = y + 1;
                    int col = path[y];
                    var key = (floor, col);
                    if (!nodeByPos.TryGetValue(key, out var node))
                    {
                        node = new MapNode { floor = floor, column = col };
                        nodeByPos[key] = node;
                        map.nodes.Add(node);
                    }
                    if (y + 1 < path.Length)
                    {
                        int nextCol = path[y + 1];
                        if (!node.nextColumns.Contains(nextCol)) node.nextColumns.Add(nextCol);
                    }
                }
            }
        }

        // 16층(BossFloor)에 단일 보스 노드를 추가하고, 15층의 모든 노드가 보스로 fan-in 되도록 nextColumns를 강제 정렬.
        private static void AddBossNode(MapState map, ChapterData chapter)
        {
            int bossCol = MapWidth / 2; // 7 cols → 중앙 = 3
            var boss = new MapNode
            {
                floor = BossFloor,
                column = bossCol,
                kind = NodeKind.Boss,
                enemyIds = new List<string> { chapter.bossId },
            };
            map.nodes.Add(boss);

            foreach (var n in map.NodesOnFloor(TotalFloors))
            {
                n.nextColumns.Clear();
                n.nextColumns.Add(bossCol);
            }
        }

        // 챕터당 노드 쿼터(고정층 1/9/15/16 제외 슬롯에 배분).
        // 6 paths × ~12 random floors = 약 50~60 슬롯. 나머지는 모두 Combat.
        private const int EliteQuota   = 4;
        private const int UnknownQuota = 10;
        private const int CampQuota    = 4;
        private const int ShopQuota    = 3;

        // 노드 종류 결정. 고정층(1/9/15/16)은 먼저 박고, 나머지는 쿼터만큼 셔플 배치 → 남은 슬롯은 Combat.
        private static void AssignRoomTypes(MapState map)
        {
            var unassigned = new List<MapNode>();

            foreach (var n in map.nodes)
            {
                if (n.kind == NodeKind.Boss) continue;
                if (n.floor == 1)                  { n.kind = NodeKind.Combat;   }
                else if (n.floor == TreasureFloor) { n.kind = NodeKind.Treasure; }
                else if (n.floor == RestFloor)     { n.kind = NodeKind.Camp;     }
                else { n.kind = NodeKind.Combat; unassigned.Add(n); } // 일단 Combat으로 초기화 (남으면 그대로)
            }

            // 부모(in-edge) 룩업 — 인접 제약 검사용.
            var parentsOf = new Dictionary<MapNode, List<MapNode>>();
            foreach (var n in map.nodes)
            {
                foreach (var nextCol in n.nextColumns)
                {
                    var child = map.GetNode(n.floor + 1, nextCol);
                    if (child == null) continue;
                    if (!parentsOf.TryGetValue(child, out var list))
                    {
                        list = new List<MapNode>();
                        parentsOf[child] = list;
                    }
                    list.Add(n);
                }
            }

            // 제약이 빡빡한 순서대로 배정: Elite → Camp → Shop → Unknown.
            // (Unknown은 floor/인접 제약이 없어 마지막에 남은 자리에 박는다.)
            AssignQuota(map, unassigned, NodeKind.Elite,    EliteQuota,   parentsOf);
            AssignQuota(map, unassigned, NodeKind.Camp,     CampQuota,    parentsOf);
            AssignQuota(map, unassigned, NodeKind.Merchant, ShopQuota,    parentsOf);
            AssignQuota(map, unassigned, NodeKind.Unknown,  UnknownQuota, parentsOf);
        }

        private static void AssignQuota(MapState map, List<MapNode> pool, NodeKind kind,
            int targetCount, Dictionary<MapNode, List<MapNode>> parentsOf)
        {
            if (targetCount <= 0) return;

            var candidates = new List<MapNode>();
            foreach (var n in pool)
            {
                if (n.kind != NodeKind.Combat) continue;       // 이미 다른 타입 박힌 슬롯 제외
                if (!FloorAllowsKind(kind, n.floor)) continue; // 층 제약
                candidates.Add(n);
            }

            // Fisher-Yates shuffle
            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
            }

            int placed = 0;
            foreach (var n in candidates)
            {
                if (placed >= targetCount) break;
                if (!AdjacencyAllowsKind(map, n, kind, parentsOf)) continue;
                n.kind = kind;
                placed++;
            }

            if (placed < targetCount)
                Debug.LogWarning($"[GSM] Quota {kind}: wanted {targetCount}, placed {placed} (slot/제약 부족)");
        }

        // 층 제약: Elite/Camp는 1~5층 금지, Camp는 14층 추가 금지(15층이 고정 Camp).
        private static bool FloorAllowsKind(NodeKind kind, int floor)
        {
            if ((kind == NodeKind.Elite || kind == NodeKind.Camp) && floor < 6) return false;
            if (kind == NodeKind.Camp && floor == 14) return false;
            return true;
        }

        // 인접 제약: Elite/Merchant/Camp만 부모/형제에 동일 타입 금지(직접 연결 회피).
        private static bool AdjacencyAllowsKind(MapState map, MapNode node, NodeKind kind,
            Dictionary<MapNode, List<MapNode>> parentsOf)
        {
            if (kind != NodeKind.Elite && kind != NodeKind.Merchant && kind != NodeKind.Camp)
                return true;

            if (!parentsOf.TryGetValue(node, out var parents)) return true;

            foreach (var p in parents)
            {
                if (p.kind == kind) return false;
                foreach (var siblingCol in p.nextColumns)
                {
                    if (siblingCol == node.column) continue;
                    var sibling = map.GetNode(node.floor, siblingCol);
                    if (sibling == null) continue;
                    if (sibling.kind == kind) return false;
                }
            }
            return true;
        }

        private void FillEnemyIds(MapState map, ChapterData chapter)
        {
            // 엘리트는 풀이 작아서(2~3종) 단순 랜덤이면 같은 적이 연달아 뽑힐 수 있음.
            // 셔플 덱(deck) 방식 — 풀을 한 바퀴 다 돌기 전엔 중복 금지, 덱 재충전 시에도 직전 엘리트와 연속 등장 회피.
            // 엘리트 노드를 (floor, column) 순으로 처리해야 진행 방향대로 번갈아 나옴.
            var eliteNodes = new List<MapNode>();
            foreach (var n in map.nodes)
            {
                if (n.kind == NodeKind.Combat)
                {
                    int normalCount = NormalEnemyCountForFloor(n.floor);
                    n.enemyIds = PickN(chapter.GetNormalPoolForFloor(n.floor), normalCount);
                }
                else if (n.kind == NodeKind.Elite)
                {
                    eliteNodes.Add(n);
                }
                // Boss는 AddBossNode에서 설정. 그 외 비전투(Camp/Treasure/Merchant/Unknown)는 enemyIds 비움.
            }

            eliteNodes.Sort((a, b) =>
            {
                int c = a.floor.CompareTo(b.floor);
                return c != 0 ? c : a.column.CompareTo(b.column);
            });

            var eliteDeck = new Queue<string>();
            string lastElite = null;
            foreach (var n in eliteNodes)
            {
                string pick = DrawElite(chapter.eliteEnemyPool, eliteDeck, ref lastElite);
                if (pick == null) { n.enemyIds = new List<string>(); continue; }
                n.enemyIds = ExpandTwinElites(new List<string> { pick });
            }
        }

        // 셔플 덱에서 다음 엘리트 id를 꺼낸다. 덱이 비면 풀을 다시 셔플해 충전하고,
        // 충전 직후 첫 항목이 직전 엘리트와 같으면 두 번째 항목과 자리를 바꿔 연속 등장을 막는다.
        private static string DrawElite(List<string> pool, Queue<string> deck, ref string lastElite)
        {
            if (pool == null || pool.Count == 0) return null;
            if (deck.Count == 0)
            {
                var shuffled = new List<string>(pool);
                for (int i = shuffled.Count - 1; i > 0; i--)
                {
                    int j = Random.Range(0, i + 1);
                    (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
                }
                if (lastElite != null && shuffled.Count > 1 && shuffled[0] == lastElite)
                {
                    (shuffled[0], shuffled[1]) = (shuffled[1], shuffled[0]);
                }
                foreach (var id in shuffled) deck.Enqueue(id);
            }
            var pick = deck.Dequeue();
            lastElite = pick;
            return pick;
        }

        // 쌍둥이류 엘리트 — 한 ID로 2체가 동시 등장해야 하는 적.
        // ON_PARTNER_DEATH 격노 메커니즘은 같은 patternSetId 인스턴스가 2개 이상일 때만 의미가 있다.
        private static readonly HashSet<string> TwinEliteIds = new() { "E103" };

        private static List<string> ExpandTwinElites(List<string> ids)
        {
            if (ids == null || ids.Count == 0) return ids ?? new List<string>();
            var result = new List<string>(ids.Count * 2);
            foreach (var id in ids)
            {
                result.Add(id);
                if (TwinEliteIds.Contains(id)) result.Add(id); // 쌍둥이는 2체로 등장
            }
            return result;
        }

        // 층 진행에 따른 일반 전투 적 수. 첫 전투(층 0/1)는 1마리, 이후 점진 증가.
        // 5층+는 2~3마리 무작위 — 후반은 개체 자체가 강해서 2마리도 위협적이도록 설계.
        public static int NormalEnemyCountForFloor(int floor)
        {
            if (floor <= 1) return 1;
            if (floor <= 4) return 2;
            return Random.Range(0, 2) == 0 ? 2 : 3;
        }

        // 층 진행에 따른 일반 적 HP 배율. 보스/엘리트는 별도(BattleManager에서 NORMAL만 적용).
        // 층 0: 1.0x, 층 13(보스 전): 1.65x — 선형.
        public static float NormalEnemyHpScaleForFloor(int floor)
        {
            return 1f + 0.05f * Mathf.Max(0, floor);
        }

        // 층 진행에 따른 일반 적 데미지 배율. HP보다 약간 부드럽게.
        // floor 0~1(첫 노드)은 70%로 완화, floor 2부터 정상 스케일.
        public static float NormalEnemyDamageScaleForFloor(int floor)
        {
            if (floor <= 1) return 0.70f;
            return 1f + 0.04f * Mathf.Max(0, floor - 2);
        }

        private List<string> PickN(List<string> pool, int n)
        {
            var result = new List<string>();
            if (pool == null || pool.Count == 0) return result;
            for (int i = 0; i < n; i++)
            {
                result.Add(pool[Random.Range(0, pool.Count)]);
            }
            return result;
        }

        private string PickRandom(List<string> pool)
        {
            if (pool == null || pool.Count == 0) return null;
            return pool[Random.Range(0, pool.Count)];
        }

        // =========================================================
        // 노드 선택 → 전투 시작
        // =========================================================

        public void SelectMapNode(MapNode node)
        {
            Debug.Log($"[GSM] SelectMapNode called: floor={node?.floor} col={node?.column} kind={node?.kind}");

            if (CurrentMap == null) { Debug.LogWarning("[GSM] CurrentMap is null"); return; }
            if (node == null) { Debug.LogWarning("[GSM] node is null"); return; }
            if (node.floor != CurrentMap.currentFloor)
            {
                Debug.LogWarning($"[GSM] Wrong floor: node.floor={node.floor}, current={CurrentMap.currentFloor}");
                return;
            }
            if (node.cleared) { Debug.LogWarning("[GSM] Node already cleared"); return; }

            // 유물 NODE_ENTER 트리거 (R017 고고학자의 일지: GOLD +5).
            // 검증 통과 후·노드별 분기 전에 한 번만 발화.
            RelicEffects.OnNodeEnter(CurrentRun);

            // === 노드 종류별 분기 ===
            DispatchNodeKind(node);
        }

        // SelectMapNode 본문을 두 단계로 쪼갬 — 검증/유물훅과 분기 처리를 분리.
        private void DispatchNodeKind(MapNode node)
        {
            if (node.floor != CurrentMap.currentFloor)
            {
                Debug.LogWarning($"[GSM] Wrong floor: node.floor={node.floor}, current={CurrentMap.currentFloor}");
                return;
            }
            if (node.cleared) { Debug.LogWarning("[GSM] Node already cleared"); return; }

            // 상인 노드 — Shop 상태로 진입, 재고 생성.
            // 노드 clear 처리는 상점을 빠져나올 때 ExitShop에서 한다.
            if (node.kind == NodeKind.Merchant)
            {
                CurrentMap.currentColumn = node.column;
                CurrentRun.currentFloor = node.floor;
                CurrentShop = ShopGenerator.Generate(CurrentRun);
                State = GameState.Shop;
                Debug.Log($"[GSM] State=>Shop, cards={CurrentShop.cards.Count} potions={CurrentShop.potions.Count} relics={CurrentShop.relics.Count}");
                return;
            }

            // 마을(캠프) 노드 — Village 상태로 진입.
            // 보물상자 무료 개봉 / 최대 HP 25% 회복 중 택1.
            // 노드 clear는 선택지 처리 후 OpenVillageTreasure / RestAtVillage에서 한다.
            if (node.kind == NodeKind.Camp)
            {
                CurrentMap.currentColumn = node.column;
                CurrentRun.currentFloor = node.floor;
                State = GameState.Village;
                Debug.Log("[GSM] State=>Village");
                return;
            }

            // 미지(Unknown) 노드 — Slay the Spire 방식. 진입 순간 무작위로 해석된다.
            // 해석된 종류로 node.kind 자체를 덮어써서 EndBattle 등 후속 흐름이 정상 동작하고,
            // 클리어 후 회색 아이콘도 실제 결과로 표시되어 플레이어가 어떤 일이 있었는지 알 수 있다.
            if (node.kind == NodeKind.Unknown)
            {
                ResolveUnknownAndDispatch(node);
                return;
            }

            // 보물 노드(9층 고정) — 시작 유물과 동일한 3택 픽커. 선택 후 ConfirmRelicPick에서 cleared/Advance 처리.
            // 보물용 풀은 비-START + RARE 가중(50/35/15). START 풀 고갈로 노드가 스킵되던 버그 차단.
            if (node.kind == NodeKind.Treasure)
            {
                CurrentMap.currentColumn = node.column;
                CurrentRun.currentFloor = node.floor;
                RelicPickChoices = BuildTreasureRelicChoices(CurrentRun, 3);
                _relicPickReturnState = GameState.Map;
                _relicPickAdvancesMap = true;
                State = GameState.RelicPick;
                Debug.Log($"[GSM] Treasure F{node.floor} C{node.column} → RelicPick ({RelicPickChoices.Count} choices)");
                return;
            }

            // 그 외 비전투 노드는 MVP에선 전용 상호작용이 없으므로 그냥 클리어 처리.
            bool isBattleNode = node.kind == NodeKind.Combat || node.kind == NodeKind.Elite || node.kind == NodeKind.Boss;
            if (!isBattleNode)
            {
                Debug.Log($"[GSM] Non-battle node ({node.kind}) — skipping (MVP stub)");
                CurrentMap.currentColumn = node.column;
                CurrentRun.currentFloor = node.floor;
                node.cleared = true;
                AdvanceToNextFloorOrVictory();
                return;
            }

            CurrentEnemies.Clear();
            foreach (var id in node.enemyIds)
            {
                var e = DataManager.Instance.GetEnemy(id);
                if (e != null) CurrentEnemies.Add(e);
                else Debug.LogWarning($"[GSM] Enemy id not found: '{id}'");
            }

            if (CurrentEnemies.Count == 0)
            {
                Debug.LogError($"[GSM] No valid enemies for node F{node.floor} C{node.column}");
                return;
            }

            CurrentMap.currentColumn = node.column;
            CurrentRun.currentFloor = node.floor;
            State = GameState.Battle;
            Debug.Log($"[GSM] State=>Battle, enemies=[{string.Join(",", CurrentEnemies.ConvertAll(e => e.nameKr))}]");
        }

        // =========================================================
        // 미지(Unknown) 노드 해석 — StS 스타일 분포 + pity 시스템.
        //
        // 기본 확률(모든 카운터=0): Event 85% / Combat 10% / Shop 3% / Treasure 2%. Elite 없음.
        //
        // pity: Event가 나올 때마다 Combat/Shop/Treasure 카운터가 +1씩 누적.
        // 카운터 c일 때 해당 타입 확률 = base × (1 + c). 카운터는 5에서 캡.
        //   c=0  Combat 10% / Shop  3% / Treasure  2% / Event 85%
        //   c=1  Combat 20% / Shop  6% / Treasure  4% / Event 70%
        //   c=2  Combat 30% / Shop  9% / Treasure  6% / Event 55%
        //   c=3  Combat 40% / Shop 12% / Treasure  8% / Event 40%
        //   c=4  Combat 50% / Shop 15% / Treasure 10% / Event 25%
        //   c=5  Combat 60% / Shop 18% / Treasure 12% / Event 10%
        //
        // 리셋: 전투/상점/보물이 나오면 그 타입 카운터만 0으로 리셋(타 타입 누적 유지).
        //       Event는 누적 유지(다음 진입 시 다른 타입 +1단계). 챕터 전환 시 모두 리셋
        //       — 멀티 챕터는 미구현이므로 새 RunState 생성 시 자연 리셋.
        //
        // MVP에는 Event UI가 없어서 Event 결과는 임시로 Rest 처리(HP 25% 회복 + 자동 진행).
        // 이후 Event UI 추가 시 Event 분기만 교체하면 됨.
        // =========================================================
        public enum UnknownOutcome { Combat, Treasure, Shop, Event }

        private const int UnknownPityCap = 5;

        // 디버그/치트로 다음 미지 노드 결과를 강제 — null이면 정상 랜덤.
        public UnknownOutcome? ForcedUnknownOutcome { get; set; }

        private UnknownOutcome RollUnknownOutcome()
        {
            if (ForcedUnknownOutcome.HasValue)
            {
                var f = ForcedUnknownOutcome.Value;
                ForcedUnknownOutcome = null;
                return f;
            }

            var run = CurrentRun;
            int combatPct   = 10 * (1 + Mathf.Clamp(run.unknownPityCombat,   0, UnknownPityCap));
            int shopPct     =  3 * (1 + Mathf.Clamp(run.unknownPityShop,     0, UnknownPityCap));
            int treasurePct =  2 * (1 + Mathf.Clamp(run.unknownPityTreasure, 0, UnknownPityCap));
            // Event = 100 - 위 합. 모든 카운터 5캡일 때 60+18+12=90 → Event 10% 최저.

            int roll = Random.Range(0, 100);
            if (roll < combatPct) return UnknownOutcome.Combat;
            roll -= combatPct;
            if (roll < shopPct) return UnknownOutcome.Shop;
            roll -= shopPct;
            if (roll < treasurePct) return UnknownOutcome.Treasure;
            return UnknownOutcome.Event;
        }

        // 결과별로 pity 카운터 갱신 — 해당 타입은 0으로 리셋, Event는 나머지 셋을 +1(5캡).
        private static void UpdateUnknownPity(RunState run, UnknownOutcome outcome)
        {
            switch (outcome)
            {
                case UnknownOutcome.Combat:   run.unknownPityCombat = 0; break;
                case UnknownOutcome.Shop:     run.unknownPityShop = 0; break;
                case UnknownOutcome.Treasure: run.unknownPityTreasure = 0; break;
                case UnknownOutcome.Event:
                    run.unknownPityCombat   = Mathf.Min(run.unknownPityCombat   + 1, UnknownPityCap);
                    run.unknownPityShop     = Mathf.Min(run.unknownPityShop     + 1, UnknownPityCap);
                    run.unknownPityTreasure = Mathf.Min(run.unknownPityTreasure + 1, UnknownPityCap);
                    break;
            }
        }

        private void ResolveUnknownAndDispatch(MapNode node)
        {
            CurrentMap.currentColumn = node.column;
            CurrentRun.currentFloor = node.floor;

            var outcome = RollUnknownOutcome();
            UpdateUnknownPity(CurrentRun, outcome);
            switch (outcome)
            {
                case UnknownOutcome.Combat:
                {
                    var chapter = DataManager.Instance.GetChapter(CurrentRun.chapterId);
                    int unkCount = NormalEnemyCountForFloor(node.floor);
                    var ids = chapter != null ? PickN(chapter.GetNormalPoolForFloor(node.floor), unkCount) : new List<string>();
                    CurrentEnemies.Clear();
                    foreach (var id in ids)
                    {
                        var e = DataManager.Instance.GetEnemy(id);
                        if (e != null) CurrentEnemies.Add(e);
                    }
                    if (CurrentEnemies.Count == 0)
                    {
                        // 적 풀이 비면 안전하게 골드 캐시로 대체
                        Debug.LogWarning("[GSM] Unknown→Combat: empty enemy pool, falling back to gold cache");
                        int g = Random.Range(15, 31);
                        CurrentRun.gold += g;
                        node.cleared = true;
                        AdvanceToNextFloorOrVictory();
                        return;
                    }
                    // 결과 시각화(클리어 후 아이콘) + EndBattle TechPoint 산정을 위해 kind 자체를 덮어씀
                    node.kind = NodeKind.Combat;
                    node.enemyIds = ids;
                    State = GameState.Battle;
                    Debug.Log($"[GSM] Unknown→Combat F{node.floor} C{node.column} enemies=[{string.Join(",", ids)}]");
                    break;
                }
                case UnknownOutcome.Treasure:
                {
                    // 고정 보물 노드와 동일하게 비-START + RARE 가중 풀로 3택 픽커. ConfirmRelicPick에서 cleared/Advance 처리.
                    node.kind = NodeKind.Treasure;
                    RelicPickChoices = BuildTreasureRelicChoices(CurrentRun, 3);
                    _relicPickReturnState = GameState.Map;
                    _relicPickAdvancesMap = true;
                    State = GameState.RelicPick;
                    Debug.Log($"[GSM] Unknown→Treasure F{node.floor} C{node.column} → RelicPick ({RelicPickChoices.Count} choices)");
                    break;
                }
                case UnknownOutcome.Shop:
                {
                    CurrentShop = ShopGenerator.Generate(CurrentRun);
                    node.kind = NodeKind.Merchant;
                    State = GameState.Shop;
                    Debug.Log($"[GSM] Unknown→Shop F{node.floor} C{node.column}");
                    break;
                }
                case UnknownOutcome.Event:
                {
                    // 이벤트 노드 — EventUI에서 선택지를 보여주고 ResolveEventChoice로 결과 처리.
                    node.kind = NodeKind.Event;
                    _pendingEventNode = node;
                    State = GameState.Event;
                    Debug.Log($"[GSM] Unknown→Event F{node.floor} C{node.column}");
                    break;
                }
            }
        }

        // 비전투 노드 스킵 및 보상 이후 진행에서 공통으로 쓰는 층 진행.
        private void AdvanceToNextFloorOrVictory()
        {
            if (CurrentMap.currentFloor >= BossFloor)
            {
                State = GameState.Victory;
                return;
            }
            CurrentMap.currentFloor++;
            CurrentMap.currentColumn = -1;
            CurrentRun.currentFloor = CurrentMap.currentFloor;
            State = GameState.Map;
        }

        // =========================================================
        // 전투 종료 / 보상
        // =========================================================

        public void EndBattle(bool won, int remainingPlayerHp)
        {
            if (CurrentRun == null) return;

            CurrentRun.playerCurrentHp = Mathf.Max(0, remainingPlayerHp);

            // 튜토리얼 모드: 승패 무관하게 sandbox 폐기 + 진짜 런 복원 + RelicPick 진입.
            // 패배해도 다시 시작시키지 않는다 — 학습 흐름을 한 번에 마치기 위함.
            if (IsTutorialMode)
            {
                // 마지막 단계(finish_battle)가 BattleWon 트리거로 끝나도록 알림 발행.
                // EndTutorial이 곧 RelicPick 전이를 잡아도 단계 시퀀스는 깨끗하게 마무리됨.
                if (won) DianoCard.Tutorial.TutorialEvents.NotifyBattleWon();
                EndTutorial();
                return;
            }

            // 훈련장 모드: 보상/패배 없이 맵으로 복귀. HP/덱 리셋 + 노드 미클리어 처리(같은 노드 재진입 가능).
            if (IsTrainingMode)
            {
                CurrentRun.playerCurrentHp = CurrentRun.playerMaxHp;
                CurrentEnemies.Clear();
                State = GameState.Map;
                Debug.Log($"[GSM] Training: battle ended (won={won}) → back to Map");
                return;
            }

            if (won)
            {
                // 유물 BATTLE_END 트리거 (현재는 placeholder).
                // R012 태초의 알의 추가 카드 보상은 RewardGenerator.Generate에서 직접 처리.
                RelicEffects.OnBattleEnd(CurrentRun, true);

                // 테크 포인트 — 전투 클리어 시 노드 등급별 지급. 일반 +1 / 엘리트 +2 / 보스 +3.
                // 비전투(상점/휴식/이벤트)는 EndBattle을 거치지 않으므로 0 유지.
                if (TechTree != null && CurrentMap != null)
                {
                    var node = CurrentMap.nodes.Find(n =>
                        n.floor == CurrentMap.currentFloor && n.column == CurrentMap.currentColumn);
                    int gain = node?.kind switch
                    {
                        NodeKind.Combat => 1,
                        NodeKind.Elite  => 2,
                        NodeKind.Boss   => 3,
                        _ => 0,
                    };
                    if (gain > 0)
                    {
                        TechTree.GrantPoints(gain);
                        Debug.Log($"[GSM] TechPoint +{gain} ({node.kind}) → {TechTree.points}");
                    }
                }

                // 보상은 노드의 첫 적 기준으로 생성 (같은 노드는 같은 등급 적이므로 OK)
                var primary = PrimaryEnemy;
                if (primary != null)
                {
                    CurrentRun.pendingReward = RewardGenerator.Generate(primary, CurrentRun);
                    // 골드는 즉시 지급
                    CurrentRun.gold += CurrentRun.pendingReward.gold;
                }
                State = GameState.Reward;
            }
            else
            {
                State = GameState.Defeat;
            }
        }

        // =========================================================
        // 훈련장
        // =========================================================

        /// <summary>Lobby에서 훈련장 진입. 임시 Run + 챕터1 맵을 만들고 Map 상태로 전환.
        /// 일반 맵 흐름과 동일한 화면이 뜨고, 모든 치트는 에디터의 Cheat Panel(Tools/Cheat Panel) 탭에서 조작.
        /// 노드를 클릭하면 정상 전투, Cheat Panel의 적 탭에서 클릭하면 강제로 그 적과 전투(TrainingStartBattle).</summary>
        public void EnterTraining()
        {
            // 훈련장은 아케네(CH002, 육식 조련사)로 고정. character_id 미지정 시 BuildStarterDeck이
            // CH001(린네 계열 초식)로 폴백되므로 명시.
            const string trainingCharId = "CH002";
            CurrentRun = new RunState
            {
                playerMaxHp = 70,
                playerCurrentHp = 70,
                gold = 0,
                deck = BuildStarterDeck(trainingCharId),
                relics = new List<RelicData>(),
                potions = new List<PotionData>(),
                currentFloor = 1,
                chapterId = "CH01",
                characterId = trainingCharId,
            };
            CurrentMap = GenerateMap(CurrentRun.chapterId);
            CurrentShop = null;
            CurrentEnemies.Clear();
            IsTrainingMode = true;
            State = GameState.Map;
            Debug.Log("[GSM] EnterTraining — 훈련장 입장 (Map 상태)");
        }

        /// <summary>훈련장에서 특정 적(또는 여러 적)과의 강제 전투. EndBattle이 Map으로 복귀시킴.</summary>
        public void TrainingStartBattle(params string[] enemyIds)
        {
            if (!IsTrainingMode)
            {
                Debug.LogWarning("[GSM] TrainingStartBattle: not in training mode");
                return;
            }
            if (CurrentRun == null) { EnterTraining(); }

            CurrentEnemies.Clear();
            foreach (var id in enemyIds)
            {
                var e = DataManager.Instance.GetEnemy(id);
                if (e != null) CurrentEnemies.Add(e);
                else Debug.LogWarning($"[GSM] TrainingStartBattle: enemy '{id}' not found");
            }
            if (CurrentEnemies.Count == 0)
            {
                Debug.LogError("[GSM] TrainingStartBattle: no valid enemies loaded");
                return;
            }

            CurrentRun.playerCurrentHp = CurrentRun.playerMaxHp; // 매 전투마다 풀 HP로 시작

            // 이미 Battle 상태일 때(예: 보스 전투 중 패널에서 다른 적 선택)에도 BattleUI가 재초기화하도록 플래그 ON.
            bool wasBattle = State == GameState.Battle;
            State = GameState.Battle;
            if (wasBattle) CheatBattleReinitRequested = true;
            Debug.Log($"[GSM] Training battle: [{string.Join(",", enemyIds)}] → Battle (reinit={wasBattle})");
        }

        /// <summary>훈련장 종료 — Lobby로 복귀, Run 정리.</summary>
        public void ExitTraining()
        {
            IsTrainingMode = false;
            CurrentRun = null;
            CurrentEnemies.Clear();
            State = GameState.Lobby;
            Debug.Log("[GSM] ExitTraining — 로비로 복귀");
        }

        /// <summary>치트: 유물을 현재 RunState에 추가하고 OnAcquired 효과 즉시 적용.</summary>
        public void Cheat_AcquireRelic(string relicId)
        {
            if (CurrentRun == null) return;
            var data = DataManager.Instance.GetRelic(relicId);
            if (data == null) { Debug.LogWarning($"[GSM] Cheat_AcquireRelic: '{relicId}' 미발견"); return; }
            CurrentRun.relics.Add(data);
            CurrentRun.hasNewRelic = true;
            RelicEffects.OnAcquired(CurrentRun, data);
            Debug.Log($"[GSM] Cheat_AcquireRelic: {data.nameKr}");
        }

        /// <summary>치트: 물약을 현재 RunState에 추가 (슬롯 꽉 차면 무시).</summary>
        public void Cheat_AcquirePotion(string potionId)
        {
            if (CurrentRun == null) return;
            if (CurrentRun.PotionSlotFull) { Debug.LogWarning("[GSM] Cheat_AcquirePotion: 물약 슬롯 꽉 참"); return; }
            var data = DataManager.Instance.GetPotion(potionId);
            if (data == null) { Debug.LogWarning($"[GSM] Cheat_AcquirePotion: '{potionId}' 미발견"); return; }
            CurrentRun.potions.Add(data);
            CurrentRun.hasNewPotion = true;
            Debug.Log($"[GSM] Cheat_AcquirePotion: {data.nameKr}");
        }

        public void TakeCardReward(CardData card)
        {
            if (card != null && CurrentRun != null)
            {
                CurrentRun.deck.Add(card);
            }
        }

        /// <summary>보상 카드 제거 — 골드 비용 없이 덱에서 카드 1장 제거.</summary>
        public void TakeCardRemoveReward(CardData card)
        {
            if (card != null && CurrentRun != null && CurrentRun.deck.Contains(card))
                CurrentRun.deck.Remove(card);
        }

        public void TakePotionReward(PotionData potion)
        {
            if (potion != null && CurrentRun != null && !CurrentRun.PotionSlotFull)
            {
                CurrentRun.potions.Add(potion);
                CurrentRun.hasNewPotion = true;
            }
        }

        public void TakeRelicReward(RelicData relic)
        {
            if (relic != null && CurrentRun != null && !CurrentRun.relics.Contains(relic))
            {
                CurrentRun.relics.Add(relic);
                CurrentRun.hasNewRelic = true;
                RelicEffects.OnAcquired(CurrentRun, relic);
            }
        }

        public void ProceedAfterReward()
        {
            if (CurrentRun == null) return;
            CurrentRun.pendingReward = null;

            // 치트/테스트로 Map 없이 Reward에 진입한 경우 — 정상 경로가 없으니 그냥 Lobby로 복귀
            if (CurrentMap == null)
            {
                Debug.Log("[GSM] ProceedAfterReward: no CurrentMap (cheat path), returning to Lobby");
                ReturnToLobby();
                return;
            }

            // 현재 선택한 노드 clear 처리
            var cleared = CurrentMap.nodes.Find(n =>
                n.floor == CurrentMap.currentFloor && n.column == CurrentMap.currentColumn);
            if (cleared != null) cleared.cleared = true;

            AdvanceToNextFloorOrVictory();
        }

        public void ReturnToLobby()
        {
            CurrentRun = null;
            CurrentMap = null;
            CurrentShop = null;
            CurrentEnemies.Clear();
            IsTrainingMode = false;
            State = GameState.Lobby;
        }

        // =========================================================
        // 상점
        // =========================================================

        public bool BuyShopCard(ShopCardEntry entry)
        {
            if (CurrentRun == null || entry == null || entry.sold) return false;
            if (CurrentRun.gold < entry.price) return false;
            CurrentRun.gold -= entry.price;
            CurrentRun.deck.Add(entry.card);
            entry.sold = true;
            return true;
        }

        public bool BuyShopPotion(ShopPotionEntry entry)
        {
            if (CurrentRun == null || entry == null || entry.sold) return false;
            if (CurrentRun.gold < entry.price) return false;
            if (CurrentRun.PotionSlotFull) return false;
            CurrentRun.gold -= entry.price;
            CurrentRun.potions.Add(entry.potion);
            CurrentRun.hasNewPotion = true;
            entry.sold = true;
            return true;
        }

        public bool BuyShopRelic(ShopRelicEntry entry)
        {
            if (CurrentRun == null || entry == null || entry.sold) return false;
            if (CurrentRun.gold < entry.price) return false;
            if (CurrentRun.relics.Contains(entry.relic)) return false;
            CurrentRun.gold -= entry.price;
            CurrentRun.relics.Add(entry.relic);
            CurrentRun.hasNewRelic = true;
            RelicEffects.OnAcquired(CurrentRun, entry.relic);
            entry.sold = true;
            return true;
        }

        public bool UseCardRemoveService(CardData cardToRemove)
        {
            if (CurrentRun == null || CurrentShop == null) return false;
            if (CurrentShop.cardRemoveUsed) return false;
            if (CurrentRun.gold < CurrentShop.cardRemovePrice) return false;
            if (cardToRemove == null || !CurrentRun.deck.Contains(cardToRemove)) return false;
            CurrentRun.gold -= CurrentShop.cardRemovePrice;
            CurrentRun.deck.Remove(cardToRemove);
            CurrentShop.cardRemoveUsed = true;
            return true;
        }

        public void ExitShop()
        {
            if (CurrentMap == null) { ReturnToLobby(); return; }

            var shopNode = CurrentMap.nodes.Find(n =>
                n.floor == CurrentMap.currentFloor && n.column == CurrentMap.currentColumn);
            if (shopNode != null) shopNode.cleared = true;

            CurrentShop = null;
            AdvanceToNextFloorOrVictory();
        }

        // =========================================================
        // 마을 (캠프)
        // =========================================================

        /// <summary>
        /// 마을 — 보물상자 무료 개봉. ELITE 풀에서 유물 1개 + 약간의 골드를
        /// pendingReward로 채우고 Reward 화면으로 전환한다.
        /// 노드 clear / 다음 층 진행은 ProceedAfterReward에서 자동 처리.
        /// </summary>
        public void OpenVillageTreasure()
        {
            if (CurrentRun == null || CurrentMap == null) return;

            var reward = RewardGenerator.GenerateTreasureChest(CurrentRun);
            CurrentRun.pendingReward = reward;
            CurrentRun.gold += reward.gold;
            State = GameState.Reward;
            Debug.Log($"[GSM] Village treasure → Reward, gold={reward.gold} relic={(reward.relic != null ? reward.relic.id : "none")}");
        }

        /// <summary>
        /// 마을 — 최대 HP의 25%만큼 현재 HP 회복(상한은 최대 HP).
        /// 노드 클리어 후 다음 층으로 진행.
        /// </summary>
        public void RestAtVillage()
        {
            if (CurrentRun == null || CurrentMap == null) return;

            int healAmount = Mathf.Max(1, Mathf.RoundToInt(CurrentRun.playerMaxHp * 0.25f));
            CurrentRun.playerCurrentHp = Mathf.Min(CurrentRun.playerCurrentHp + healAmount, CurrentRun.playerMaxHp);
            Debug.Log($"[GSM] Village rest: +{healAmount} HP → {CurrentRun.playerCurrentHp}/{CurrentRun.playerMaxHp}");

            var villageNode = CurrentMap.nodes.Find(n =>
                n.floor == CurrentMap.currentFloor && n.column == CurrentMap.currentColumn);
            if (villageNode != null) villageNode.cleared = true;

            AdvanceToNextFloorOrVictory();
        }

        // =========================================================
        // Debug / Cheat
        // =========================================================

        /// <summary>치트 진입 시 CurrentRun.deck이 비어있으면 캐릭터 archetype 기준 시작 덱으로 채운다.
        /// StartNewRun 직후 캐릭터 미확정 상태(deck=빈 리스트)에서 치트로 전투/상점 등에 진입하면 빈 덱으로 시작하는 버그 방지.</summary>
        private void EnsureCheatStarterDeck()
        {
            if (CurrentRun == null) return;
            if (CurrentRun.deck != null && CurrentRun.deck.Count > 0) return;
            string cid = string.IsNullOrEmpty(CurrentRun.characterId) ? "CH002" : CurrentRun.characterId;
            CurrentRun.deck = BuildStarterDeck(cid);
            Debug.Log($"[GSM] Cheat: rebuilt empty deck (characterId={cid}, cards={CurrentRun.deck.Count})");
        }

        /// <summary>
        /// 치트: 현재 상태 무시하고 바로 Reward 화면을 띄움.
        /// CurrentRun이 없으면 임시 러닝 상태를 생성해서 사용.
        /// </summary>
        public void Cheat_TriggerReward()
        {
            if (CurrentRun == null)
            {
                CurrentRun = new RunState
                {
                    playerMaxHp = 70,
                    playerCurrentHp = 56,
                    gold = 100,
                    deck = BuildStarterDeck(),
                    relics = new List<RelicData>(),
                    potions = new List<PotionData>(),
                    currentFloor = 1,
                    chapterId = "CH01",
                };
            }
            EnsureCheatStarterDeck();

            var reward = new BattleReward { gold = Random.Range(15, 40) };

            // 카드 3장 — 챕터 제한 + RITUAL/STATUS 제외 (STATUS는 적 강제 추가 전용 저주 카드)
            // T1/T2 진화 결과체는 융합 전용 — 보상에 절대 노출 금지(RewardGenerator·ShopGenerator와 동일 안전망).
            var evoResults = DataManager.Instance.EvolutionResultIds;
            var eligibleCards = new List<CardData>();
            foreach (var c in DataManager.Instance.Cards.Values)
            {
                if (c.cardType == CardType.RITUAL) continue;
                if (c.subType == CardSubType.STATUS) continue;
                if (evoResults != null && evoResults.Contains(c.id)) continue;
                eligibleCards.Add(c);
            }
            for (int i = 0; i < 3 && eligibleCards.Count > 0; i++)
            {
                int idx = Random.Range(0, eligibleCards.Count);
                reward.cardChoices.Add(eligibleCards[idx]);
                eligibleCards.RemoveAt(idx);
            }

            // 물약 — 아무거나 첫 번째
            foreach (var p in DataManager.Instance.Potions.Values)
            {
                reward.potion = p;
                break;
            }

            // 유물 — 아무거나 첫 번째 (이미 보유 중이면 스킵)
            foreach (var r in DataManager.Instance.Relics.Values)
            {
                if (!CurrentRun.relics.Contains(r))
                {
                    reward.relic = r;
                    break;
                }
            }

            CurrentRun.pendingReward = reward;
            CurrentRun.gold += reward.gold;
            State = GameState.Reward;
            Debug.Log("[GSM] Cheat_TriggerReward: forced Reward state");
        }

        /// <summary>
        /// 치트: 현재 상태 무시하고 바로 상점으로 진입.
        /// Run이 없으면 테스트용 Run을 만들고, CurrentMap이 없으면 ExitShop에서 Lobby 복귀로 흘러간다.
        /// </summary>
        public void Cheat_EnterShop()
        {
            if (CurrentRun == null)
            {
                CurrentRun = new RunState
                {
                    playerMaxHp = 70,
                    playerCurrentHp = 60,
                    gold = 300,
                    deck = BuildStarterDeck(),
                    relics = new List<RelicData>(),
                    potions = new List<PotionData>(),
                    currentFloor = 1,
                    chapterId = "CH01",
                };
            }
            EnsureCheatStarterDeck();

            CurrentShop = ShopGenerator.Generate(CurrentRun);
            State = GameState.Shop;
            Debug.Log($"[GSM] Cheat_EnterShop: cards={CurrentShop.cards.Count} potions={CurrentShop.potions.Count} relics={CurrentShop.relics.Count}");
        }

        /// <summary>
        /// 치트: 특정 적 ID로 바로 전투 진입.
        /// Run이 없으면 테스트 Run을 생성. Map은 건너뛰므로 EndBattle 후 Lobby로 복귀한다.
        /// </summary>
        public void Cheat_StartBattleWith(params string[] enemyIds)
        {
            if (enemyIds == null || enemyIds.Length == 0)
            {
                Debug.LogWarning("[GSM] Cheat_StartBattleWith: empty enemyIds");
                return;
            }

            if (CurrentRun == null)
            {
                CurrentRun = new RunState
                {
                    playerMaxHp = 70,
                    playerCurrentHp = 70,
                    gold = 50,
                    deck = BuildStarterDeck(),
                    relics = new List<RelicData>(),
                    potions = new List<PotionData>(),
                    currentFloor = 14,
                    chapterId = "CH01",
                };
            }
            EnsureCheatStarterDeck();

            CurrentEnemies.Clear();
            foreach (var id in enemyIds)
            {
                var e = DataManager.Instance.GetEnemy(id);
                if (e != null) CurrentEnemies.Add(e);
                else Debug.LogWarning($"[GSM] Cheat_StartBattleWith: enemy '{id}' not found");
            }

            if (CurrentEnemies.Count == 0)
            {
                Debug.LogError("[GSM] Cheat_StartBattleWith: no valid enemies loaded");
                return;
            }

            // 이미 Battle 상태일 때 BattleUI가 재초기화하도록 플래그 ON.
            // BattleUI.Update가 다음 프레임에 이 플래그 보고 강제로 _battleInitialized=false 처리한 뒤 InitBattle 재실행.
            bool wasBattle = State == GameState.Battle;
            State = GameState.Battle;
            if (wasBattle) CheatBattleReinitRequested = true;
            Debug.Log($"[GSM] Cheat_StartBattleWith: [{string.Join(",", enemyIds)}] → Battle (reinit={wasBattle})");
        }

        /// <summary>치트로 전투 중에 적을 갈아탈 때 BattleUI 재초기화 신호. BattleUI가 소비 후 false로 되돌림.</summary>
        public bool CheatBattleReinitRequested { get; set; }

        /// <summary>치트 편의 메서드 — 1챕터 보스(E901) 전투 시작.</summary>
        public void Cheat_StartBossBattle() => Cheat_StartBattleWith("E901");

        /// <summary>
        /// 치트: 현재 상태 무시하고 바로 마을로 진입.
        /// CurrentMap이 없으면 두 옵션 모두 확인용으로만 동작 — RestAtVillage가 안전 가드로 빠진다.
        /// </summary>
        public void Cheat_EnterVillage()
        {
            if (CurrentRun == null)
            {
                CurrentRun = new RunState
                {
                    playerMaxHp = 70,
                    playerCurrentHp = 35,
                    gold = 100,
                    deck = BuildStarterDeck(),
                    relics = new List<RelicData>(),
                    potions = new List<PotionData>(),
                    currentFloor = 1,
                    chapterId = "CH01",
                };
            }
            EnsureCheatStarterDeck();

            State = GameState.Village;
            Debug.Log("[GSM] Cheat_EnterVillage");
        }

        /// <summary>
        /// 치트: 즉석에서 미지 노드 진입 시뮬레이션.
        /// forced 가 null이면 정상 분포로 랜덤, 값이 있으면 해당 결과로 강제.
        /// 진행 중인 맵이 있으면 현재 층의 임시 Unknown 노드를 만들어 실제 흐름과 동일하게 dispatch.
        /// 맵이 없으면 1층 단일 Unknown 노드로 구성된 미니 맵을 만들어 dispatch (테스트 전용).
        /// </summary>
        public void Cheat_TriggerUnknown(UnknownOutcome? forced = null)
        {
            if (CurrentRun == null)
            {
                CurrentRun = new RunState
                {
                    playerMaxHp = 70,
                    playerCurrentHp = 50,
                    gold = 50,
                    deck = BuildStarterDeck(),
                    relics = new List<RelicData>(),
                    potions = new List<PotionData>(),
                    currentFloor = 1,
                    chapterId = "CH01",
                };
            }
            EnsureCheatStarterDeck();

            // 맵이 없거나 현재 층에 노드가 비면 즉석 미니 맵 — 결과 dispatch만 검증.
            // 보스층(BossFloor=16)에 배치해 두면 결과 처리 후 AdvanceToNextFloorOrVictory가 Victory로 빠진다.
            if (CurrentMap == null || CurrentMap.NodesOnFloor(CurrentMap.currentFloor).Count == 0)
            {
                CurrentMap = new MapState { currentFloor = BossFloor, totalFloors = TotalFloors };
                CurrentMap.nodes.Add(new MapNode { floor = BossFloor, column = 0, kind = NodeKind.Unknown });
            }

            // 현재 층의 첫 클리어되지 않은 노드를 Unknown으로 캐스팅.
            var floorNodes = CurrentMap.NodesOnFloor(CurrentMap.currentFloor);
            MapNode target = null;
            foreach (var n in floorNodes) if (!n.cleared) { target = n; break; }
            if (target == null)
            {
                Debug.LogWarning("[GSM] Cheat_TriggerUnknown: no uncleared node on current floor");
                return;
            }
            target.kind = NodeKind.Unknown;
            target.enemyIds = new List<string>();

            ForcedUnknownOutcome = forced;
            ResolveUnknownAndDispatch(target);
        }
    }
}
