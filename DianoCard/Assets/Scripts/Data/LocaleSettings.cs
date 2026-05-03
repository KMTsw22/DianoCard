namespace DianoCard.Data
{
    public enum Language
    {
        KR,
        EN,
    }

    /// <summary>
    /// 전역 언어 설정. 데이터 클래스의 *_kr / *_en 필드 중 어느 쪽을 쓸지 결정한다.
    /// 현재는 카드 description만 다국어 분리되어 있고, 다른 데이터는 단일 description을 유지한다.
    /// 향후 확장 시 동일 패턴(필드 두 개 + 활성언어 폴백 프로퍼티)으로 추가하면 된다.
    /// </summary>
    public static class LocaleSettings
    {
        public static Language Current = Language.KR;

        // 활성 언어 KR 우선 → 비어있으면 EN 폴백 → 그것도 비면 빈 문자열.
        // 단방향 폴백이라 EN만 채워두면 자동으로 보임 (점진적 번역에 안전).
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
    }
}
