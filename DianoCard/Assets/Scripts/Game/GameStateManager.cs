using System.Collections.Generic;
using DianoCard.Data;
using UnityEngine;

namespace DianoCard.Game
{
    public enum GameState
    {
        Lobby,
        CharacterSelect, // 캐릭터 선택 화면
        Map,             // 노드 선택 화면
        Battle,
        Reward,
        Defeat,
        Victory,
    }

    /// <summary>
    /// 전역 게임 상태/플로우 관리 싱글톤.
    ///
    /// 챕터 구성 (14 floor):
    /// - Floor 1~13: 일반 전투 + 엘리트 (각 floor 3~5개 노드 선택지, 층마다 랜덤)
    /// - Floor 14: 보스 (1개 고정 노드)
    /// 층별 종류는 FloorKinds 배열로 정의.
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
        public List<EnemyData> CurrentEnemies { get; private set; } = new();

        // 이전 코드 호환용 — 리스트의 첫 적 (배경/보상 결정에 사용)
        public EnemyData PrimaryEnemy => CurrentEnemies.Count > 0 ? CurrentEnemies[0] : null;

        // 층 구성: 시작층 제외, 1~13층 + 14층 보스
        private const int BossFloor = 14;
        private const int TotalFloors = 14;

        // 층별 노드 종류 (index = floor - 1). 보스 직전 엘리트, 중간중간 엘리트로 페이스 조절.
        private static readonly NodeKind[] FloorKinds = new[]
        {
            NodeKind.Combat, // 1
            NodeKind.Combat, // 2
            NodeKind.Combat, // 3
            NodeKind.Elite,  // 4
            NodeKind.Combat, // 5
            NodeKind.Combat, // 6
            NodeKind.Combat, // 7
            NodeKind.Elite,  // 8
            NodeKind.Combat, // 9
            NodeKind.Combat, // 10
            NodeKind.Combat, // 11
            NodeKind.Combat, // 12
            NodeKind.Elite,  // 13
            NodeKind.Boss,   // 14
        };

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
            State = GameState.Lobby;
        }

        private void AutoAttachUI()
        {
            if (GetComponent<LobbyUI>() == null) gameObject.AddComponent<LobbyUI>();
            if (GetComponent<CharacterSelectUI>() == null) gameObject.AddComponent<CharacterSelectUI>();
            if (GetComponent<MapUI>() == null) gameObject.AddComponent<MapUI>();
            if (GetComponent<BattleUI>() == null) gameObject.AddComponent<BattleUI>();
            if (GetComponent<RewardUI>() == null) gameObject.AddComponent<RewardUI>();
            if (GetComponent<GameOverUI>() == null) gameObject.AddComponent<GameOverUI>();
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
                gold = 0,
                deck = BuildStarterDeck(),
                relics = new List<RelicData>(),
                potions = new List<PotionData>(),
                currentFloor = 0,
                chapterId = "CH01",
            };

            // 캐릭터 선택 화면을 먼저 보여주고, 거기서 확인하면 맵을 생성한다
            State = GameState.CharacterSelect;
        }

        /// <summary>
        /// 캐릭터 선택을 확정 → 맵 생성 후 Map 상태로 전환.
        /// 추후 캐릭터별 다른 시작 덱/HP/유물을 적용할 자리.
        /// </summary>
        public void ConfirmCharacterSelection()
        {
            if (CurrentRun == null)
            {
                Debug.LogError("[GSM] ConfirmCharacterSelection: CurrentRun is null");
                return;
            }

            // MVP: 고고학자 1명만 있어서 RunState는 이미 세팅되어 있음
            CurrentMap = GenerateMap(CurrentRun.chapterId);
            State = GameState.Map;
        }

        private List<CardData> BuildStarterDeck()
        {
            var deck = new List<CardData>();
            void Add(string id, int count)
            {
                var c = DataManager.Instance.GetCard(id);
                if (c == null) { Debug.LogError($"[GameStateManager] Missing card: {id}"); return; }
                for (int i = 0; i < count; i++) deck.Add(c);
            }
            Add("C001", 3); // 트리케라톱스
            Add("C002", 2); // 스테고사우루스
            Add("C101", 6); // 공격 마법
            Add("C102", 5); // 방어 마법
            Add("C201", 2); // 공격 강화
            Add("C202", 2); // 전체 힐
            return deck;
        }

        // =========================================================
        // Map 생성
        // =========================================================

        private MapState GenerateMap(string chapterId)
        {
            // 시작층(floor 0)부터 — 첫 전투는 약한 일반 적 1마리
            var map = new MapState { currentFloor = 0, totalFloors = TotalFloors };

            var chapter = DataManager.Instance.GetChapter(chapterId);
            if (chapter == null)
            {
                Debug.LogError($"[GameStateManager] Chapter not found: {chapterId}");
                return map;
            }

            // Floor 0: 시작 전투 — 단일 노드, 중앙, 일반 적 1마리
            map.nodes.Add(new MapNode
            {
                floor = 0,
                column = 1,
                kind = NodeKind.Combat,
                enemyIds = PickN(chapter.normalEnemyPool, 1),
            });

            for (int floor = 1; floor <= TotalFloors; floor++)
            {
                NodeKind kind = FloorKinds[floor - 1];

                if (kind == NodeKind.Boss)
                {
                    map.nodes.Add(new MapNode
                    {
                        floor = floor,
                        column = 1, // 중앙
                        kind = NodeKind.Boss,
                        enemyIds = new List<string> { chapter.bossId },
                    });
                    continue;
                }

                // 층마다 3~5개 중 하나로 노드 수가 달라져 경로 선택이 매번 다른 인상.
                int nodeCount = Random.Range(3, 6);
                for (int col = 0; col < nodeCount; col++)
                {
                    List<string> enemyIds = kind == NodeKind.Elite
                        ? PickN(chapter.eliteEnemyPool, 1)   // 엘리트는 단독
                        : PickN(chapter.normalEnemyPool, 2); // 일반 전투는 2마리

                    map.nodes.Add(new MapNode
                    {
                        floor = floor,
                        column = col,
                        kind = kind,
                        enemyIds = enemyIds,
                    });
                }
            }

            return map;
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
            Debug.Log($"[GSM] SelectMapNode called: floor={node?.floor} col={node?.column}");

            if (CurrentMap == null) { Debug.LogWarning("[GSM] CurrentMap is null"); return; }
            if (node == null) { Debug.LogWarning("[GSM] node is null"); return; }
            if (node.floor != CurrentMap.currentFloor)
            {
                Debug.LogWarning($"[GSM] Wrong floor: node.floor={node.floor}, current={CurrentMap.currentFloor}");
                return;
            }
            if (node.cleared) { Debug.LogWarning("[GSM] Node already cleared"); return; }

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
        // 전투 종료 / 보상
        // =========================================================

        public void EndBattle(bool won, int remainingPlayerHp)
        {
            if (CurrentRun == null) return;

            CurrentRun.playerCurrentHp = Mathf.Max(0, remainingPlayerHp);

            if (won)
            {
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

        public void TakeCardReward(CardData card)
        {
            if (card != null && CurrentRun != null)
            {
                CurrentRun.deck.Add(card);
            }
        }

        public void TakePotionReward(PotionData potion)
        {
            if (potion != null && CurrentRun != null && !CurrentRun.PotionSlotFull)
            {
                CurrentRun.potions.Add(potion);
            }
        }

        public void TakeRelicReward(RelicData relic)
        {
            if (relic != null && CurrentRun != null && !CurrentRun.relics.Contains(relic))
            {
                CurrentRun.relics.Add(relic);
            }
        }

        public void ProceedAfterReward()
        {
            if (CurrentRun == null || CurrentMap == null) return;
            CurrentRun.pendingReward = null;

            // 현재 선택한 노드 clear 처리
            var cleared = CurrentMap.nodes.Find(n =>
                n.floor == CurrentMap.currentFloor && n.column == CurrentMap.currentColumn);
            if (cleared != null) cleared.cleared = true;

            // 보스 클리어?
            if (CurrentMap.currentFloor >= BossFloor)
            {
                State = GameState.Victory;
                return;
            }

            // 다음 층으로. 맵 화면으로 복귀 (자동 전투 아님)
            CurrentMap.currentFloor++;
            CurrentMap.currentColumn = -1;
            CurrentRun.currentFloor = CurrentMap.currentFloor;
            State = GameState.Map;
        }

        public void ReturnToLobby()
        {
            CurrentRun = null;
            CurrentMap = null;
            CurrentEnemies.Clear();
            State = GameState.Lobby;
        }
    }
}
