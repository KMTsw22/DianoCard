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
                characterId = "CH001",
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
            State = GameState.RelicPick;
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
            AddBossNode(map, chapter);
            AssignRoomTypes(map);
            FillEnemyIds(map, chapter);
            return map;
        }

        // 6개 경로의 column 시퀀스(길이 TotalFloors). path[y]는 floor=y+1에서의 column.
        // 첫 두 경로는 1층 시작 column이 서로 달라야 한다(StS 규칙: 시작 선택지 ≥ 2).
        // 같은 (y → y+1) 구간에서 두 경로가 서로 자리를 바꾸는 식의 교차는 금지.
        private static List<int[]> GeneratePaths()
        {
            var paths = new List<int[]>(PathCount);
            var startingXs = new HashSet<int>();
            // 같은 fromFloor의 기존 엣지들을 모아두고, 새 후보가 어떤 기존 엣지와도 교차하지 않는지 검사.
            var edgesByFloor = new Dictionary<int, List<(int fromX, int toX)>>();

            for (int p = 0; p < PathCount; p++)
            {
                var path = new int[TotalFloors];

                int startX;
                if (p < 2)
                {
                    int guard = 0;
                    do { startX = Random.Range(0, MapWidth); }
                    while (startingXs.Contains(startX) && ++guard < 64);
                }
                else
                {
                    startX = Random.Range(0, MapWidth);
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

        // 노드 종류 결정. 고정층(1/9/15)은 먼저 박고, 나머지는 위에서 아래로 굴리며 인접 제약 통과까지 reroll.
        private static void AssignRoomTypes(MapState map)
        {
            var assigned = new HashSet<MapNode>();
            var unassigned = new List<MapNode>();

            foreach (var n in map.nodes)
            {
                if (n.kind == NodeKind.Boss) { assigned.Add(n); continue; }
                if (n.floor == 1)             { n.kind = NodeKind.Combat;   assigned.Add(n); }
                else if (n.floor == TreasureFloor) { n.kind = NodeKind.Treasure; assigned.Add(n); }
                else if (n.floor == RestFloor)     { n.kind = NodeKind.Camp;     assigned.Add(n); }
                else unassigned.Add(n);
            }

            // 부모(in-edge) 룩업 — 같은 부모의 형제 검사를 위해서도 사용.
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

            // 위→아래로 처리해야 부모는 항상 먼저 배정되어 인접 제약 검사가 의미를 갖는다.
            unassigned.Sort((a, b) => a.floor.CompareTo(b.floor));

            const int MaxAttempts = 32;
            foreach (var n in unassigned)
            {
                NodeKind picked = NodeKind.Combat;
                bool ok = false;
                for (int i = 0; i < MaxAttempts; i++)
                {
                    picked = RollRoomType();
                    if (IsValidRoomType(map, n, picked, parentsOf, assigned)) { ok = true; break; }
                }
                n.kind = ok ? picked : NodeKind.Combat; // 다 실패하면 안전하게 일반 전투
                assigned.Add(n);
            }
        }

        // 저승천 분포: Combat 53 / Elite 8 / Event(=Unknown) 22 / Rest 12 / Shop 5.
        // (StS Steam 가이드 기준. 고승천에서는 Elite 16 / Combat 45로 조정.)
        private static NodeKind RollRoomType()
        {
            int r = Random.Range(0, 100);
            if (r < 53) return NodeKind.Combat;
            if (r < 61) return NodeKind.Elite;
            if (r < 83) return NodeKind.Unknown; // EVENT 슬롯 — UI 추가 전까진 Unknown으로 표기
            if (r < 95) return NodeKind.Camp;    // REST
            return NodeKind.Merchant;             // SHOP
        }

        private static bool IsValidRoomType(MapState map, MapNode node, NodeKind kind,
            Dictionary<MapNode, List<MapNode>> parentsOf, HashSet<MapNode> assigned)
        {
            // Elite/Rest는 6층 미만(1~5층)에 배치 불가.
            if ((kind == NodeKind.Elite || kind == NodeKind.Camp) && node.floor < 6) return false;
            // Rest는 14층 금지(15층이 어차피 Rest 고정 → 연속 Rest 방지).
            if (kind == NodeKind.Camp && node.floor == 14) return false;

            // 같은 특수 타입(Elite/Merchant/Camp) 직접 연결 금지 — 부모 중 동일 타입이 있으면 reject.
            if (kind == NodeKind.Elite || kind == NodeKind.Merchant || kind == NodeKind.Camp)
            {
                if (parentsOf.TryGetValue(node, out var parents))
                {
                    foreach (var p in parents)
                    {
                        if (p.kind == kind) return false;
                    }
                }
            }

            // 한 부모의 자식들은 서로 다른 타입이어야 함(이미 배정된 형제만 검사 — 미배정 형제는 나중에 이 노드를 보게 됨).
            if (parentsOf.TryGetValue(node, out var parents2))
            {
                foreach (var p in parents2)
                {
                    foreach (var siblingCol in p.nextColumns)
                    {
                        if (siblingCol == node.column) continue;
                        var sibling = map.GetNode(node.floor, siblingCol);
                        if (sibling == null) continue;
                        if (!assigned.Contains(sibling)) continue;
                        if (sibling.kind == kind) return false;
                    }
                }
            }

            return true;
        }

        private void FillEnemyIds(MapState map, ChapterData chapter)
        {
            foreach (var n in map.nodes)
            {
                if (n.kind == NodeKind.Combat)
                {
                    int normalCount = NormalEnemyCountForFloor(n.floor);
                    n.enemyIds = PickN(chapter.GetNormalPoolForFloor(n.floor), normalCount);
                }
                else if (n.kind == NodeKind.Elite)
                {
                    n.enemyIds = ExpandTwinElites(PickN(chapter.eliteEnemyPool, 1));
                }
                // Boss는 AddBossNode에서 설정. 그 외 비전투(Camp/Treasure/Merchant/Unknown)는 enemyIds 비움.
            }
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
            if (node.kind == NodeKind.Treasure)
            {
                CurrentMap.currentColumn = node.column;
                CurrentRun.currentFloor = node.floor;
                RelicPickChoices = BuildRelicPickChoices(CurrentRun, 3);
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
                    // 고정 보물 노드와 동일하게 시작 유물 3택 픽커로 진행. ConfirmRelicPick에서 cleared/Advance 처리.
                    node.kind = NodeKind.Treasure;
                    RelicPickChoices = BuildRelicPickChoices(CurrentRun, 3);
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
                    // Event UI 미구현 — 임시로 Rest 효과(HP 25% 회복 + 자동 진행). 추후 UI 붙으면 이 분기 교체.
                    int healAmount = Mathf.Max(1, Mathf.RoundToInt(CurrentRun.playerMaxHp * 0.25f));
                    CurrentRun.playerCurrentHp = Mathf.Min(CurrentRun.playerCurrentHp + healAmount, CurrentRun.playerMaxHp);
                    node.kind = NodeKind.Event;
                    node.cleared = true;
                    Debug.Log($"[GSM] Unknown→Event(stub:Rest) F{node.floor} C{node.column} +{healAmount}HP → {CurrentRun.playerCurrentHp}/{CurrentRun.playerMaxHp}");
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
                // 유물 BATTLE_END 트리거 — R012 태초의 알 등이 일시 카드를 RunState에 추가.
                // 보상 화면 전에 호출해서 다음 전투 시작 시 시작 덱에 자동 합류되도록 한다.
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

        /// <summary>치트: 허수아비(E999)와 QA 전투 시작. 훈련 모드로 진입해 RunState를 유지한 채 재사용.</summary>
        public void Cheat_StartQABattle()
        {
            if (!IsTrainingMode || CurrentRun == null) EnterTraining();
            TrainingStartBattle("E999");
            Debug.Log("[GSM] Cheat_StartQABattle");
        }

        /// <summary>치트: 유물을 현재 RunState에 추가하고 OnAcquired 효과 즉시 적용.</summary>
        public void Cheat_AcquireRelic(string relicId)
        {
            if (CurrentRun == null) return;
            var data = DataManager.Instance.GetRelic(relicId);
            if (data == null) { Debug.LogWarning($"[GSM] Cheat_AcquireRelic: '{relicId}' 미발견"); return; }
            CurrentRun.relics.Add(data);
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
            }
        }

        public void TakeRelicReward(RelicData relic)
        {
            if (relic != null && CurrentRun != null && !CurrentRun.relics.Contains(relic))
            {
                CurrentRun.relics.Add(relic);
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
