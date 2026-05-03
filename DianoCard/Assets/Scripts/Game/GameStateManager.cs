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
        Shop,            // 상인 노드 상호작용 화면
        Village,         // 마을(캠프) 노드 상호작용 화면
        Defeat,
        Victory,
        Training,        // 훈련장 — 임의 적을 골라 자유롭게 전투 (승패 무관, 맵/보상 없음)
        AnimationTest,   // 애니메이션 테스트 — Resources/AnimationTest 폴더의 프레임 시퀀스 프리뷰 (에디터 전용 개발 툴)
        TechTree,        // 메타 진행 — 영구 해금 트리. 로비에서만 진입 가능, 임시 UI(MVP 3브랜치 9노드).
    }

    /// <summary>
    /// 전역 게임 상태/플로우 관리 싱글톤.
    ///
    /// 챕터 구성 (14 floor):
    /// - Floor 1~13: 각 3~5개 노드, 타입은 node.csv weight 기반 랜덤 (밸런스 룰 적용)
    /// - Floor 14: 보스 (1개 고정 노드)
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
        public ShopState CurrentShop { get; private set; }
        public List<EnemyData> CurrentEnemies { get; private set; } = new();

        /// <summary>훈련장 모드 플래그 — true면 EndBattle이 Reward/Defeat 대신 Training으로 복귀.</summary>
        public bool IsTrainingMode { get; private set; }

        /// <summary>영구 메타 진행(테크트리) 상태. PlayerPrefs에서 로드, 노드 해금 시 자동 저장.</summary>
        public TechTreeState TechTree { get; private set; }

        // 이전 코드 호환용 — 리스트의 첫 적 (배경/보상 결정에 사용)
        public EnemyData PrimaryEnemy => CurrentEnemies.Count > 0 ? CurrentEnemies[0] : null;

        // 층 구성: 시작층 제외, 1~13층 + 14층 보스
        private const int BossFloor = 14;
        private const int TotalFloors = 14;

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
            if (TechTree == null) TechTree = TechTreeState.Load();
            State = GameState.Lobby;
        }

        private void AutoAttachUI()
        {
            if (GetComponent<LobbyUI>() == null) gameObject.AddComponent<LobbyUI>();
            if (GetComponent<CharacterSelectUI>() == null) gameObject.AddComponent<CharacterSelectUI>();
            if (GetComponent<MapUI>() == null) gameObject.AddComponent<MapUI>();
            if (GetComponent<BattleUI>() == null) gameObject.AddComponent<BattleUI>();
            if (GetComponent<RewardUI>() == null) gameObject.AddComponent<RewardUI>();
            if (GetComponent<ShopUI>() == null) gameObject.AddComponent<ShopUI>();
            if (GetComponent<VillageUI>() == null) gameObject.AddComponent<VillageUI>();
            if (GetComponent<GameOverUI>() == null) gameObject.AddComponent<GameOverUI>();
            if (GetComponent<TrainingUI>() == null) gameObject.AddComponent<TrainingUI>();
            if (GetComponent<AnimationTestUI>() == null) gameObject.AddComponent<AnimationTestUI>();
            if (GetComponent<TechTreeUI>() == null) gameObject.AddComponent<TechTreeUI>();
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
            if (TechTree == null) TechTree = TechTreeState.Load();
            if (State != GameState.TechTree) _stateBeforeTechTree = State;
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
                gold = 0,
                deck = new List<CardData>(), // 캐릭터 확정 시 채움
                relics = new List<RelicData>(),
                potions = new List<PotionData>(),
                currentFloor = 0,
                chapterId = "CH01",
                characterId = "CH001",
            };

            // 캐릭터 선택 화면을 먼저 보여주고, 거기서 확인하면 맵을 생성한다
            State = GameState.CharacterSelect;
        }

        /// <summary>
        /// 캐릭터 선택을 확정 → 시작 덱 빌드 + 맵 생성 후 Map 상태로 전환.
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
            State = GameState.Map;
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
                { "C101", 2 }, // 공격 마법
                { "C102", 2 }, // 방어 마법
                { "C201", 1 }, // 공격 강화
                { "C202", 1 }, // 전체 힐
            },
            ["CARN"] = new()
            {
                { "C004", 2 }, // 랩터 x2 (융합 재료)
                { "C005", 2 }, // 카르노타우루스 x2 (융합 재료)
                { "C101", 2 }, // 공격 마법
                { "C102", 2 }, // 방어 마법
                { "C201", 1 }, // 공격 강화
                { "C152", 1 }, // 융합의 각인 — 첫 런에서 T0+T0→T1 경험 보장
            },
        };

        /// <summary>현재 실행 중인 런의 archetype에서 시작 덱 카드 id 집합을 반환.</summary>
        public static HashSet<string> GetStarterCardIdsFor(string archetype)
        {
            if (archetype != null && StarterDecksByArchetype.TryGetValue(archetype, out var comp))
                return new HashSet<string>(comp.Keys);
            return new HashSet<string>();
        }

        // 오버로드 — 인자 없이 호출 시 HERB 덱을 기본 반환(치트/훈련 경로용).
        private List<CardData> BuildStarterDeck() => BuildStarterDeck("CH001");

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

            // 층별 노드 타입을 node.csv 기반 가중치 랜덤으로 결정.
            // 같은 층 안에서 3~5개의 서로 다른 타입이 섞이게 되어 경로 선택에 의미가 생긴다.
            var nodeTable = new List<NodeData>(DataManager.Instance.Nodes.Values);

            for (int floor = 1; floor <= TotalFloors; floor++)
            {
                if (floor == BossFloor)
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

                int nodeCount = Random.Range(3, 6);
                var floorKinds = PickFloorNodeKinds(nodeTable, floor, nodeCount);

                // 일반 적 수: 층이 올라갈수록 증가. 첫 전투(층 1)는 무조건 1마리.
                int normalCount = NormalEnemyCountForFloor(floor);

                for (int col = 0; col < nodeCount; col++)
                {
                    NodeKind kind = floorKinds[col];
                    List<string> enemyIds = kind switch
                    {
                        // 쌍둥이류 엘리트는 2체로 등장해야 하므로 ExpandTwinElites로 확장
                        NodeKind.Elite  => ExpandTwinElites(PickN(chapter.eliteEnemyPool, 1)),
                        NodeKind.Combat => PickN(chapter.normalEnemyPool, normalCount),
                        _ => new List<string>(), // 비전투 노드(Camp/Event/Merchant)는 적 없음
                    };

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

        // 한 층의 노드 타입 배열을 결정. node.csv의 weight/min_floor/max_floor를 존중하며
        // 밸런스 규칙을 적용:
        //  - Floor 1: 전원 일반 전투(첫인상 단순하게)
        //  - 진행을 보장하기 위해 최소 1개는 NORMAL_BATTLE
        //  - 엘리트/상인/마을/보물은 한 층에 각각 최대 1개
        //  - TREASURE/UNKNOWN은 전용 아이콘이 없어 Event로 접힘 (MVP)
        private List<NodeKind> PickFloorNodeKinds(List<NodeData> nodeTable, int floor, int count)
        {
            var result = new List<NodeKind>(count);

            if (floor == 1)
            {
                for (int i = 0; i < count; i++) result.Add(NodeKind.Combat);
                return result;
            }

            // 해당 층에서 뽑을 수 있는 후보 (보스/시작 제외)
            var candidates = new List<NodeData>();
            foreach (var nd in nodeTable)
            {
                if (nd.nodeType == NodeType.BOSS || nd.nodeType == NodeType.START) continue;
                if (floor < nd.minFloor || floor > nd.maxFloor) continue;
                if (nd.weight <= 0) continue;
                candidates.Add(nd);
            }

            // 한 층당 단독 타입 카운트 (Elite/Shop/Town/Treasure은 최대 1개씩)
            var usedOnce = new Dictionary<NodeType, int>();
            int Cap(NodeType t) => t switch
            {
                NodeType.ELITE_BATTLE => 1,
                NodeType.SHOP         => 1,
                NodeType.TOWN         => 1,
                NodeType.TREASURE     => 1,
                _ => int.MaxValue,
            };

            for (int i = 0; i < count; i++)
            {
                // 캡을 초과한 타입 제외
                int totalWeight = 0;
                foreach (var c in candidates)
                {
                    usedOnce.TryGetValue(c.nodeType, out int used);
                    if (used >= Cap(c.nodeType)) continue;
                    totalWeight += c.weight;
                }

                NodeType picked;
                if (totalWeight <= 0)
                {
                    picked = NodeType.NORMAL_BATTLE;
                }
                else
                {
                    int roll = Random.Range(0, totalWeight);
                    picked = NodeType.NORMAL_BATTLE;
                    foreach (var c in candidates)
                    {
                        usedOnce.TryGetValue(c.nodeType, out int used);
                        if (used >= Cap(c.nodeType)) continue;
                        if (roll < c.weight) { picked = c.nodeType; break; }
                        roll -= c.weight;
                    }
                }

                usedOnce.TryGetValue(picked, out int prev);
                usedOnce[picked] = prev + 1;
                result.Add(CsvTypeToKind(picked));
            }

            // 진행 보장: 전투/엘리트가 하나도 없으면 첫 칸을 NORMAL_BATTLE로 교체
            bool hasBattle = false;
            foreach (var k in result) if (k == NodeKind.Combat || k == NodeKind.Elite) { hasBattle = true; break; }
            if (!hasBattle) result[0] = NodeKind.Combat;

            return result;
        }

        // EVENT / TREASURE 도 UNKNOWN과 함께 모두 미지의 노드(Unknown)로 접힘.
        // 이벤트 시스템과 보물 노드 전용 UI가 추가되면 다시 분리.
        private static NodeKind CsvTypeToKind(NodeType t) => t switch
        {
            NodeType.NORMAL_BATTLE => NodeKind.Combat,
            NodeType.ELITE_BATTLE  => NodeKind.Elite,
            NodeType.SHOP          => NodeKind.Merchant,
            NodeType.TOWN          => NodeKind.Camp,
            NodeType.EVENT         => NodeKind.Unknown,
            NodeType.UNKNOWN       => NodeKind.Unknown,
            NodeType.TREASURE      => NodeKind.Unknown,
            _ => NodeKind.Combat,
        };

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
        public static int NormalEnemyCountForFloor(int floor)
        {
            if (floor <= 1) return 1;
            if (floor <= 4) return 2;
            return 3;
        }

        // 층 진행에 따른 일반 적 HP 배율. 보스/엘리트는 별도(BattleManager에서 NORMAL만 적용).
        // 층 0: 1.0x, 층 13(보스 전): 1.65x — 선형.
        public static float NormalEnemyHpScaleForFloor(int floor)
        {
            return 1f + 0.05f * Mathf.Max(0, floor);
        }

        // 층 진행에 따른 일반 적 데미지 배율. HP보다 약간 부드럽게.
        public static float NormalEnemyDamageScaleForFloor(int floor)
        {
            return 1f + 0.04f * Mathf.Max(0, floor);
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
        // 미지(Unknown) 노드 해석 — Slay the Spire 분포에 가깝게.
        //
        // StS의 ? 노드는 진입 시 Event/Combat/Treasure/Shop으로 무작위 해석된다.
        // DianoCard MVP에는 Event UI가 없으므로 Event 비중을 다른 결과로 재분배.
        //
        // 분포:
        //   40% Combat   — 챕터 일반 적 풀에서 2마리 매복 전투
        //   25% Treasure — 마을 보물상자와 동일 (유물 + 약간의 골드)
        //   20% Shop     — 일반 상점
        //   15% Rest     — 평화로운 쉼터, 최대 HP 25% 회복 후 자동 진행
        // =========================================================
        public enum UnknownOutcome { Combat, Treasure, Shop, Rest }

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
            int roll = Random.Range(0, 100);
            if (roll < 40) return UnknownOutcome.Combat;
            if (roll < 65) return UnknownOutcome.Treasure;
            if (roll < 85) return UnknownOutcome.Shop;
            return UnknownOutcome.Rest;
        }

        private void ResolveUnknownAndDispatch(MapNode node)
        {
            CurrentMap.currentColumn = node.column;
            CurrentRun.currentFloor = node.floor;

            var outcome = RollUnknownOutcome();
            switch (outcome)
            {
                case UnknownOutcome.Combat:
                {
                    var chapter = DataManager.Instance.GetChapter(CurrentRun.chapterId);
                    int unkCount = NormalEnemyCountForFloor(node.floor);
                    var ids = chapter != null ? PickN(chapter.normalEnemyPool, unkCount) : new List<string>();
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
                    var reward = RewardGenerator.GenerateTreasureChest(CurrentRun);
                    CurrentRun.pendingReward = reward;
                    CurrentRun.gold += reward.gold;
                    State = GameState.Reward;
                    Debug.Log($"[GSM] Unknown→Treasure F{node.floor} C{node.column} relic={(reward.relic != null ? reward.relic.id : "none")} gold={reward.gold}");
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
                case UnknownOutcome.Rest:
                {
                    int healAmount = Mathf.Max(1, Mathf.RoundToInt(CurrentRun.playerMaxHp * 0.25f));
                    CurrentRun.playerCurrentHp = Mathf.Min(CurrentRun.playerCurrentHp + healAmount, CurrentRun.playerMaxHp);
                    node.kind = NodeKind.Camp;
                    node.cleared = true;
                    Debug.Log($"[GSM] Unknown→Rest F{node.floor} C{node.column} +{healAmount}HP → {CurrentRun.playerCurrentHp}/{CurrentRun.playerMaxHp}");
                    AdvanceToNextFloorOrVictory();
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

            // 훈련장 모드: 보상/패배 없이 훈련장 메뉴로 복귀. HP/덱 리셋으로 자유롭게 재시도.
            if (IsTrainingMode)
            {
                CurrentRun.playerCurrentHp = CurrentRun.playerMaxHp;
                CurrentEnemies.Clear();
                State = GameState.Training;
                Debug.Log($"[GSM] Training: battle ended (won={won}) → back to Training menu");
                return;
            }

            if (won)
            {
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

        /// <summary>Lobby에서 훈련장 진입. 임시 Run(70HP, 스타터덱)을 만들고 Training 상태로 전환.</summary>
        public void EnterTraining()
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
            CurrentMap = null;
            CurrentShop = null;
            CurrentEnemies.Clear();
            IsTrainingMode = true;
            State = GameState.Training;
            Debug.Log("[GSM] EnterTraining — 훈련장 입장");
        }

        /// <summary>훈련장에서 특정 적(또는 여러 적)과의 전투 시작. EndBattle이 Training으로 복귀시킴.</summary>
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
            State = GameState.Battle;
            Debug.Log($"[GSM] Training battle: [{string.Join(",", enemyIds)}] → Battle");
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
            string cid = string.IsNullOrEmpty(CurrentRun.characterId) ? "CH001" : CurrentRun.characterId;
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
            var eligibleCards = new List<CardData>();
            foreach (var c in DataManager.Instance.Cards.Values)
            {
                if (c.cardType == CardType.RITUAL) continue;
                if (c.subType == CardSubType.STATUS) continue;
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
            // 보스층(14)에 배치해 두면 결과 처리 후 AdvanceToNextFloorOrVictory가 Victory로 빠진다.
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
