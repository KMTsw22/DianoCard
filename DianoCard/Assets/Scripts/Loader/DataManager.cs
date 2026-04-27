using System.Collections.Generic;
using UnityEngine;

namespace DianoCard.Data
{
    /// <summary>
    /// 모든 CSV 테이블을 로드하고 id -> data 딕셔너리로 제공하는 싱글톤.
    /// 사용: DataManager.Instance.Load(); DataManager.Instance.GetCard("C001");
    /// </summary>
    public class DataManager
    {
        private const string TablePath = "Tables/"; // Resources/Tables/*

        private static DataManager _instance;
        public static DataManager Instance => _instance ??= new DataManager();

        public IReadOnlyDictionary<string, CardData> Cards => _cards;
        public IReadOnlyDictionary<string, EnemyData> Enemies => _enemies;
        public IReadOnlyDictionary<string, PotionData> Potions => _potions;
        public IReadOnlyDictionary<string, RelicData> Relics => _relics;
        public IReadOnlyDictionary<string, ChapterData> Chapters => _chapters;
        public IReadOnlyDictionary<string, EventData> Events => _events;
        public IReadOnlyDictionary<string, NodeData> Nodes => _nodes;
        public IReadOnlyDictionary<string, CharacterData> Characters => _characters;
        public IReadOnlyDictionary<string, UIStringData> UIStrings => _uiStrings;
        public IReadOnlyDictionary<string, CardTypeLabelData> CardTypeLabels => _cardTypeLabels;
        public IReadOnlyDictionary<string, StatLabelData> StatLabels => _statLabels;
        public IReadOnlyDictionary<string, DinoEvolutionData> DinoEvolutions => _dinoEvolutions;
        public HashSet<string> EvolutionResultIds => _evolutionResultIds;
        public IReadOnlyDictionary<string, DinoSkillData> DinoSkills => _dinoSkills;

        // 패턴/페이즈/인텐트 (적 AI용)
        public IReadOnlyDictionary<string, List<EnemyPatternData>> EnemyPatterns => _enemyPatterns;
        public IReadOnlyDictionary<string, List<EnemyPhaseData>> EnemyPhases => _enemyPhases;
        public IReadOnlyDictionary<string, IntentIconData> IntentIcons => _intentIcons;
        public IReadOnlyDictionary<string, EnemyPassiveData> EnemyPassives => _enemyPassives;

        private Dictionary<string, CardData> _cards = new();
        private Dictionary<string, EnemyData> _enemies = new();
        private Dictionary<string, PotionData> _potions = new();
        private Dictionary<string, RelicData> _relics = new();
        private Dictionary<string, ChapterData> _chapters = new();
        private Dictionary<string, EventData> _events = new();
        private Dictionary<string, NodeData> _nodes = new();
        private Dictionary<string, CharacterData> _characters = new();
        private Dictionary<string, UIStringData> _uiStrings = new();
        private Dictionary<string, CardTypeLabelData> _cardTypeLabels = new();
        private Dictionary<string, StatLabelData> _statLabels = new();
        private Dictionary<string, List<EnemyPatternData>> _enemyPatterns = new();
        private Dictionary<string, List<EnemyPhaseData>> _enemyPhases = new();
        private Dictionary<string, IntentIconData> _intentIcons = new();
        private Dictionary<string, EnemyPassiveData> _enemyPassives = new();
        // 진화 테이블 — baseCardId(현재 카드) → 다음 진화 정보. 링크드 리스트처럼 연속 조회 가능.
        private Dictionary<string, DinoEvolutionData> _dinoEvolutions = new();
        private HashSet<string> _evolutionResultIds = new();
        // 진화 공룡 스킬 — cardId(C004_T1 등) → DinoSkillData. T0에는 행 없음.
        private Dictionary<string, DinoSkillData> _dinoSkills = new();

        public bool IsLoaded { get; private set; }

        public void Load(bool force = false)
        {
            if (IsLoaded && !force) return;

            _cards = LoadTable("card", CardData.FromRow, d => d.id);
            _enemies = LoadTable("enemy", EnemyData.FromRow, d => d.id);
            _potions = LoadTable("potion", PotionData.FromRow, d => d.id);
            _relics = LoadTable("relic", RelicData.FromRow, d => d.id);
            _chapters = LoadTable("chapter", ChapterData.FromRow, d => d.id);
            _events = LoadTable("event", EventData.FromRow, d => d.id);
            _nodes = LoadTable("node", NodeData.FromRow, d => d.id);
            _characters = LoadTable("character", CharacterData.FromRow, d => d.id);
            _uiStrings = LoadTable("ui_string", UIStringData.FromRow, d => d.id);
            _cardTypeLabels = LoadTable("card_type_label", CardTypeLabelData.FromRow, d => d.id);
            _statLabels = LoadTable("stat_label", StatLabelData.FromRow, d => d.id);

            // 패턴은 patternSetId당 여러 행 → 그룹화 로드
            _enemyPatterns = LoadGroupedTable("enemy_pattern", EnemyPatternData.FromRow,
                d => d.patternSetId,
                list => list.Sort((a, b) => a.stepOrder.CompareTo(b.stepOrder)));
            _enemyPhases = LoadGroupedTable("enemy_phase", EnemyPhaseData.FromRow,
                d => d.phaseSetId,
                list => list.Sort((a, b) => a.phase.CompareTo(b.phase)));
            _intentIcons = LoadTable("intent_icon", IntentIconData.FromRow, d => d.id);
            _enemyPassives = LoadTable("enemy_passive", EnemyPassiveData.FromRow, d => d.id);
            _dinoEvolutions = LoadTable("dino_evolution", DinoEvolutionData.FromRow, d => d.baseCardId);
            _evolutionResultIds = new HashSet<string>();
            foreach (var evo in _dinoEvolutions.Values)
                if (!string.IsNullOrEmpty(evo.resultCardId)) _evolutionResultIds.Add(evo.resultCardId);
            _dinoSkills = LoadTable("dino_skill", DinoSkillData.FromRow, d => d.cardId);

            IsLoaded = true;

            Debug.Log($"[DataManager] Loaded — cards:{_cards.Count} enemies:{_enemies.Count} " +
                      $"potions:{_potions.Count} relics:{_relics.Count} chapters:{_chapters.Count} " +
                      $"events:{_events.Count} nodes:{_nodes.Count} characters:{_characters.Count} " +
                      $"uiStrings:{_uiStrings.Count} cardTypeLabels:{_cardTypeLabels.Count} statLabels:{_statLabels.Count} " +
                      $"patternSets:{_enemyPatterns.Count} phaseSets:{_enemyPhases.Count} intentIcons:{_intentIcons.Count}");
        }

        /// <summary>같은 키로 묶이는 행들을 List로 모아 반환. 정렬 후처리도 가능.</summary>
        private Dictionary<string, List<T>> LoadGroupedTable<T>(
            string tableName,
            System.Func<Dictionary<string, string>, T> fromRow,
            System.Func<T, string> keySelector,
            System.Action<List<T>> postSort = null)
        {
            var result = new Dictionary<string, List<T>>();
            var asset = Resources.Load<TextAsset>(TablePath + tableName);
            if (asset == null)
            {
                Debug.LogError($"[DataManager] CSV not found: Resources/{TablePath}{tableName}");
                return result;
            }

            var rows = CSVParser.Parse(asset.text);
            foreach (var row in rows)
            {
                var data = fromRow(row);
                var key = keySelector(data);
                if (string.IsNullOrEmpty(key)) continue;
                if (!result.TryGetValue(key, out var list))
                {
                    list = new List<T>();
                    result[key] = list;
                }
                list.Add(data);
            }
            if (postSort != null)
                foreach (var kv in result) postSort(kv.Value);
            return result;
        }

        private Dictionary<string, T> LoadTable<T>(
            string tableName,
            System.Func<Dictionary<string, string>, T> fromRow,
            System.Func<T, string> keySelector)
        {
            var result = new Dictionary<string, T>();
            var asset = Resources.Load<TextAsset>(TablePath + tableName);
            if (asset == null)
            {
                Debug.LogError($"[DataManager] CSV not found: Resources/{TablePath}{tableName}");
                return result;
            }

            var rows = CSVParser.Parse(asset.text);
            foreach (var row in rows)
            {
                var data = fromRow(row);
                var key = keySelector(data);
                if (string.IsNullOrEmpty(key)) continue;
                if (result.ContainsKey(key))
                {
                    Debug.LogWarning($"[DataManager] Duplicate id '{key}' in {tableName}.csv");
                    continue;
                }
                result[key] = data;
            }
            return result;
        }

        // === Convenience accessors ===
        public CardData GetCard(string id) => _cards.TryGetValue(id, out var d) ? d : null;
        public EnemyData GetEnemy(string id) => _enemies.TryGetValue(id, out var d) ? d : null;
        public PotionData GetPotion(string id) => _potions.TryGetValue(id, out var d) ? d : null;
        public RelicData GetRelic(string id) => _relics.TryGetValue(id, out var d) ? d : null;
        public ChapterData GetChapter(string id) => _chapters.TryGetValue(id, out var d) ? d : null;
        public EventData GetEvent(string id) => _events.TryGetValue(id, out var d) ? d : null;
        public NodeData GetNode(string id) => _nodes.TryGetValue(id, out var d) ? d : null;
        public CharacterData GetCharacter(string id) => _characters.TryGetValue(id, out var d) ? d : null;
        public List<EnemyPatternData> GetPatternSet(string patternSetId)
            => _enemyPatterns.TryGetValue(patternSetId, out var list) ? list : null;
        public List<EnemyPhaseData> GetPhaseSet(string phaseSetId)
            => _enemyPhases.TryGetValue(phaseSetId, out var list) ? list : null;
        public IntentIconData GetIntentIcon(string id)
            => _intentIcons.TryGetValue(id, out var d) ? d : null;
        public EnemyPassiveData GetPassive(string id)
            => _enemyPassives.TryGetValue(id, out var d) ? d : null;
        public DinoEvolutionData GetEvolution(string cardId)
            => _dinoEvolutions.TryGetValue(cardId, out var d) ? d : null;
        public DinoSkillData GetSkill(string cardId)
            => _dinoSkills.TryGetValue(cardId, out var d) ? d : null;

        // === UI / Label accessors ===

        /// <summary>ui_string.csv에서 id로 문자열을 꺼내 args로 string.Format 치환.</summary>
        public string GetUIString(string id, params object[] args)
        {
            if (!_uiStrings.TryGetValue(id, out var data))
            {
                Debug.LogWarning($"[DataManager] UIString not found: {id}");
                return id;
            }
            if (args == null || args.Length == 0) return data.value;
            try { return string.Format(data.value, args); }
            catch { return data.value; }
        }

        /// <summary>card_type_label.csv에서 cardType+subType 조합으로 라벨을 꺼냄.</summary>
        public string GetCardTypeLabel(CardType cardType, CardSubType subType)
        {
            var key = CardTypeLabelData.BuildKey(cardType, subType);
            return _cardTypeLabels.TryGetValue(key, out var data) ? data.label : key;
        }

        /// <summary>stat_label.csv에서 id(ATK/HP/DMG/BLOCK/COST)로 짧은 라벨을 꺼냄.</summary>
        public string GetStatLabel(string id)
        {
            return _statLabels.TryGetValue(id, out var data) ? data.label : id;
        }

        public string GetStatFullName(string id)
        {
            return _statLabels.TryGetValue(id, out var data) ? data.fullName : id;
        }
    }
}
