using System.Collections.Generic;

namespace DianoCard.Data
{
    /// <summary>
    /// enemy_passive.csv 한 행 — 몬스터 패시브 카탈로그.
    /// MVP 단계에서는 "설명 표시" 용도로만 쓰임. 런타임 효과는 해당 몬스터의 패턴/조건으로 이미 구현됨.
    /// 추후 트리거 이벤트(BATTLE_START/ON_HIT 등)로 실제 효과를 일반화하려면
    /// trigger / action / value 컬럼을 추가 활용.
    /// </summary>
    [System.Serializable]
    public class EnemyPassiveData
    {
        public string id;
        public string nameKr;
        public string nameEn;
        public string description;
        public string icon;      // 선택 — 배지 아이콘 리소스 경로

        public static EnemyPassiveData FromRow(Dictionary<string, string> row)
        {
            return new EnemyPassiveData
            {
                id = CSVUtil.GetString(row, "id"),
                nameKr = CSVUtil.GetString(row, "name_kr"),
                nameEn = CSVUtil.GetString(row, "name_en"),
                description = CSVUtil.GetString(row, "description"),
                icon = CSVUtil.GetString(row, "icon"),
            };
        }
    }
}
