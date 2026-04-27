using System.Collections.Generic;

namespace DianoCard.Data
{
    /// <summary>
    /// dino_skill.csv 한 행 — 진화 공룡(T1/T2)의 시그니처 스킬.
    /// T0는 스킬 없음(이 테이블에 행이 없음). 카드 에너지와 분리된 턴 단위 쿨다운으로 동작.
    /// </summary>
    [System.Serializable]
    public class DinoSkillData
    {
        public string cardId;        // 대상 카드 id (C004_T1 등)
        public string nameKr;
        public string nameEn;
        public TargetType target;    // ENEMY / ALL_ENEMY / SELF
        public int damage;           // 1히트당 피해. 0이면 비공격형
        public int hits;             // 타격 횟수
        public int cooldownTurns;    // 재사용 대기 턴. 0 = once-per-battle (isOnceBattle 참조)
        public bool isOnceBattle;    // cooldown == "BATTLE"
        public string description;

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
                description = CSVUtil.GetString(row, "description"),
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
