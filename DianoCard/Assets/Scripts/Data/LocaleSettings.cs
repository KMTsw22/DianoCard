using UnityEngine;

namespace DianoCard.Data
{
    public enum Language
    {
        KR,
        EN,
    }

    public static class LocaleSettings
    {
        private const string PrefsKey = "dianocard_language";

        public static Language Current { get; private set; } = Load();

        private static Language Load()
        {
            int saved = SaveSystem.GetInt(PrefsKey, (int)Language.EN);
            return (Language)saved;
        }

        public static void Set(Language lang)
        {
            Current = lang;
            SaveSystem.SetInt(PrefsKey, (int)lang);
            SaveSystem.Save();
        }

        // KR 우선 → 비어있으면 EN 폴백. EN 모드에선 반대. 점진적 번역에 안전.
        public static string Pick(string kr, string en)
        {
            if (Current == Language.KR)
            {
                if (!string.IsNullOrEmpty(kr)) return kr;
                return en ?? "";
            }
            if (!string.IsNullOrEmpty(en)) return en;
            return kr ?? "";
        }

        // 하드코딩 UI 문자열용. en 먼저(코드 가독성). 둘 다 있다고 가정.
        public static string L(string en, string kr) => Current == Language.KR ? kr : en;
    }
}
