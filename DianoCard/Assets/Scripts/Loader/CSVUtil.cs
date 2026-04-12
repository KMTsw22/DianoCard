using System;
using System.Collections.Generic;

namespace DianoCard.Data
{
    /// <summary>
    /// Dictionary(string, string) 로 표현된 CSV 행에서 타입별로 값 꺼내기.
    /// 값이 없거나 비어 있으면 기본값 반환.
    /// </summary>
    public static class CSVUtil
    {
        public static string GetString(Dictionary<string, string> row, string key, string fallback = "")
        {
            return row.TryGetValue(key, out var v) && v != null ? v : fallback;
        }

        public static int GetInt(Dictionary<string, string> row, string key, int fallback = 0)
        {
            if (row.TryGetValue(key, out var v) && int.TryParse(v, out var i)) return i;
            return fallback;
        }

        public static float GetFloat(Dictionary<string, string> row, string key, float fallback = 0f)
        {
            if (row.TryGetValue(key, out var v) && float.TryParse(v, out var f)) return f;
            return fallback;
        }

        public static bool GetBool(Dictionary<string, string> row, string key, bool fallback = false)
        {
            if (!row.TryGetValue(key, out var v) || string.IsNullOrEmpty(v)) return fallback;
            v = v.Trim().ToUpperInvariant();
            if (v == "TRUE" || v == "1" || v == "Y" || v == "YES") return true;
            if (v == "FALSE" || v == "0" || v == "N" || v == "NO") return false;
            return fallback;
        }

        public static TEnum GetEnum<TEnum>(Dictionary<string, string> row, string key, TEnum fallback) where TEnum : struct, Enum
        {
            if (!row.TryGetValue(key, out var v) || string.IsNullOrEmpty(v)) return fallback;
            if (Enum.TryParse<TEnum>(v.Trim(), true, out var result)) return result;
            return fallback;
        }

        /// <summary>콤마로 구분된 문자열을 리스트로 (예: "E001,E002,E003")</summary>
        public static List<string> GetStringList(Dictionary<string, string> row, string key)
        {
            var list = new List<string>();
            if (!row.TryGetValue(key, out var v) || string.IsNullOrEmpty(v)) return list;
            foreach (var part in v.Split(','))
            {
                var p = part.Trim();
                if (!string.IsNullOrEmpty(p)) list.Add(p);
            }
            return list;
        }
    }
}
