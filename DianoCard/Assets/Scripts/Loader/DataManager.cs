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

        private Dictionary<string, CardData> _cards = new();
        private Dictionary<string, EnemyData> _enemies = new();
        private Dictionary<string, PotionData> _potions = new();
        private Dictionary<string, RelicData> _relics = new();
        private Dictionary<string, ChapterData> _chapters = new();
        private Dictionary<string, EventData> _events = new();
        private Dictionary<string, NodeData> _nodes = new();
        private Dictionary<string, CharacterData> _characters = new();

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

            IsLoaded = true;

            Debug.Log($"[DataManager] Loaded — cards:{_cards.Count} enemies:{_enemies.Count} " +
                      $"potions:{_potions.Count} relics:{_relics.Count} chapters:{_chapters.Count} " +
                      $"events:{_events.Count} nodes:{_nodes.Count} characters:{_characters.Count}");
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
    }
}
