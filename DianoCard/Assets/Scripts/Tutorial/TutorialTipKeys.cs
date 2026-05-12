namespace DianoCard.Tutorial
{
    /// <summary>STS 스타일 팁 모달의 PlayerPrefs 키. 한 번 본 팁은 다시 안 뜬다.
    /// 키별로 PlayerPrefs.SetInt(key, 1) 패턴.</summary>
    public static class TutorialTipKeys
    {
        public const string AfterTutorial = "DianoCard.Tip.AfterTutorial"; // 튜토리얼 끝나고 본격 시작 안내
        public const string FirstReward   = "DianoCard.Tip.FirstReward";   // 첫 전투 보상 화면
        public const string FirstPotion   = "DianoCard.Tip.FirstPotion";   // 첫 포션 획득
        public const string FirstRelicPick = "DianoCard.Tip.FirstRelicPick"; // 시작 유물 선택
    }
}
