using System.Collections.Generic;

namespace DianoCard.Data
{
    [System.Serializable]
    public class DinoSkillData
    {
        public string cardId;
        public string nameKr;
        public string nameEn;
        public TargetType target;
        public int damage;
        public int hits;
        public int cooldownTurns;
        public bool isOnceBattle;
        public string descriptionKr;
        public string descriptionEn;

        public string name => LocaleSettings.Pick(nameKr, nameEn);
        public string description => LocaleSettings.Pick(descriptionKr, descriptionEn);

        // "bleed:1;vulnerable:2" 식 파싱된 효과 페어. 키는 lower-case.
        public List<(string key, int value)> effects = new();

        public static DinoSkillData FromRow(Dictionary<string, string> row)
        {
            var data = new DinoSkillData
            {
                cardId = CSVUtil.GetString(row, "card_id"),
                nameKr = CSVUtil.GetString(row, "skill_name_kr"),
                nameEn = CSVUtil.GetString(row, "skill_name_en"),
                target = CSVUtil.GetEnum(row, "target", TargetType.ENEMY),
                damage = CSVUtil.GetInt(row, "damage"),
                hits = CSVUtil.GetInt(row, "hits", 1),
                descriptionKr = CSVUtil.GetString(row, "description_kr"),
                descriptionEn = CSVUtil.GetString(row, "description_en"),
            };

            var rawCooldown = CSVUtil.GetString(row, "cooldown").Trim();
            if (rawCooldown.Equals("BATTLE", System.StringComparison.OrdinalIgnoreCase))
            {
                data.isOnceBattle = true;
                data.cooldownTurns = 0;
            }
            else
            {
                int.TryParse(rawCooldown, out data.cooldownTurns);
            }

            ParseEffects(CSVUtil.GetString(row, "effects"), data.effects);
            return data;
        }

        private static void ParseEffects(string raw, List<(string key, int value)> dst)
        {
            if (string.IsNullOrEmpty(raw)) return;
            foreach (var token in raw.Split(';'))
            {
                var t = token.Trim();
                if (string.IsNullOrEmpty(t)) continue;
                var parts = t.Split(':');
                if (parts.Length != 2) continue;
                var key = parts[0].Trim().ToLowerInvariant();
                if (!int.TryParse(parts[1].Trim(), out var v)) continue;
                dst.Add((key, v));
            }
        }
    }
}
