using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace DianoCard.Data
{
    /// <summary>
    /// 단일 JSON 파일 기반 영구 저장소. PlayerPrefs 드롭인 대체.
    /// 파일: Application.persistentDataPath/save.json — Steam Auto-Cloud 친화 경로
    /// (Windows: %APPDATA%/../LocalLow/&lt;Company&gt;/&lt;Product&gt; / Mac: ~/Library/Application Support/&lt;Company&gt;/&lt;Product&gt;).
    /// 모든 쓰기는 .tmp → File.Replace로 원자성 보장.
    /// </summary>
    public static class SaveSystem
    {
        private const string FileName = "save.json";
        private const int CurrentVersion = 1;

        [Serializable]
        private class Entry { public string k; public string v; }

        [Serializable]
        private class Store
        {
            public int version = CurrentVersion;
            public List<Entry> entries = new();
        }

        private static Dictionary<string, string> _cache;
        private static bool _dirty;

        private static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        private static void EnsureLoaded()
        {
            if (_cache != null) return;
            _cache = new Dictionary<string, string>();
            try
            {
                if (File.Exists(FilePath))
                {
                    var store = JsonUtility.FromJson<Store>(File.ReadAllText(FilePath));
                    if (store?.entries != null)
                        foreach (var e in store.entries)
                            if (!string.IsNullOrEmpty(e.k))
                                _cache[e.k] = e.v ?? "";
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SaveSystem] Load failed: {ex.Message} — starting fresh");
                _cache = new Dictionary<string, string>();
            }

            // 파일이 없을 때만 1회 PlayerPrefs에서 기존 값 이관.
            // 이관 후 즉시 저장하여 다음 부팅부터는 파일이 진실.
            MigrateFromPlayerPrefs();
        }

        // 출시 후 한참 지나면 제거해도 됨. PlayerPrefs는 그대로 둠(롤백 가능성).
        private static readonly (string key, char kind)[] LegacyKeys =
        {
            ("dianocard.audio.bgm", 'f'),
            ("dianocard.audio.sfx", 'f'),
            ("dianocard_language", 'i'),
            ("DianoCard.Tutorial.CH01.Completed", 'i'),
            ("TechTreeState_v2", 's'),
            ("DianoCard.Tip.AfterTutorial", 'i'),
            ("DianoCard.Tip.FirstReward", 'i'),
            ("DianoCard.Tip.FirstPotion", 'i'),
            ("DianoCard.Tip.FirstRelicPick", 'i'),
        };

        private static void MigrateFromPlayerPrefs()
        {
            bool any = false;
            foreach (var (key, kind) in LegacyKeys)
            {
                if (!PlayerPrefs.HasKey(key)) continue;
                switch (kind)
                {
                    case 'i': _cache[key] = PlayerPrefs.GetInt(key).ToString(CultureInfo.InvariantCulture); break;
                    case 'f': _cache[key] = PlayerPrefs.GetFloat(key).ToString("R", CultureInfo.InvariantCulture); break;
                    case 's': _cache[key] = PlayerPrefs.GetString(key, ""); break;
                }
                any = true;
            }
            if (any)
            {
                _dirty = true;
                Save();
                Debug.Log("[SaveSystem] Migrated legacy PlayerPrefs → save.json");
            }
        }

        public static bool HasKey(string key)
        {
            EnsureLoaded();
            return _cache.ContainsKey(key);
        }

        public static int GetInt(string key, int defaultValue = 0)
        {
            EnsureLoaded();
            return _cache.TryGetValue(key, out var s)
                   && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
                ? v : defaultValue;
        }

        public static float GetFloat(string key, float defaultValue = 0f)
        {
            EnsureLoaded();
            return _cache.TryGetValue(key, out var s)
                   && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                ? v : defaultValue;
        }

        public static string GetString(string key, string defaultValue = "")
        {
            EnsureLoaded();
            return _cache.TryGetValue(key, out var s) ? s : defaultValue;
        }

        public static void SetInt(string key, int value)
        {
            EnsureLoaded();
            _cache[key] = value.ToString(CultureInfo.InvariantCulture);
            _dirty = true;
        }

        public static void SetFloat(string key, float value)
        {
            EnsureLoaded();
            _cache[key] = value.ToString("R", CultureInfo.InvariantCulture);
            _dirty = true;
        }

        public static void SetString(string key, string value)
        {
            EnsureLoaded();
            _cache[key] = value ?? "";
            _dirty = true;
        }

        public static void DeleteKey(string key)
        {
            EnsureLoaded();
            if (_cache.Remove(key)) _dirty = true;
        }

        /// <summary>변경분을 디스크에 flush. PlayerPrefs.Save()와 동일 시맨틱.</summary>
        public static void Save()
        {
            EnsureLoaded();
            if (!_dirty) return;
            try
            {
                var store = new Store { version = CurrentVersion };
                foreach (var kv in _cache)
                    store.entries.Add(new Entry { k = kv.Key, v = kv.Value });
                string json = JsonUtility.ToJson(store);
                string path = FilePath;
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
                _dirty = false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveSystem] Save failed: {ex.Message}");
            }
        }

#if UNITY_EDITOR
        /// <summary>에디터 메뉴에서 강제 리로드용. 런타임에서는 호출 X.</summary>
        public static void ReloadForEditor()
        {
            _cache = null;
            _dirty = false;
        }

        public static string DebugFilePath => FilePath;
#endif
    }
}
