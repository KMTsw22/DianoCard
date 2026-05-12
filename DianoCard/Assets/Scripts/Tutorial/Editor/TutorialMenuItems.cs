#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace DianoCard.Tutorial.Editor
{
    /// <summary>Unity Editor 메뉴바 > DianoCard 에서 튜토리얼 PlayerPrefs 플래그를 리셋.
    /// 개발 중 튜토리얼 재테스트할 때 사용.</summary>
    public static class TutorialMenuItems
    {
        [MenuItem("DianoCard/Reset Tutorial Flags")]
        public static void ResetTutorialFlags()
        {
            PlayerPrefs.DeleteKey(DianoCard.Game.GameStateManager.TutorialCompletedKey);
            PlayerPrefs.DeleteKey(TutorialTipKeys.AfterTutorial);
            PlayerPrefs.DeleteKey(TutorialTipKeys.FirstReward);
            PlayerPrefs.DeleteKey(TutorialTipKeys.FirstPotion);
            PlayerPrefs.DeleteKey(TutorialTipKeys.FirstRelicPick);
            PlayerPrefs.Save();
            Debug.Log("[Tutorial] Reset — 튜토리얼/팁 PlayerPrefs 플래그 모두 삭제됨. 다음 New Run 시 다시 표시.");
        }

        [MenuItem("DianoCard/Check Tutorial Flags")]
        public static void CheckTutorialFlags()
        {
            string key = DianoCard.Game.GameStateManager.TutorialCompletedKey;
            int v = PlayerPrefs.GetInt(key, 0);
            Debug.Log($"[Tutorial] '{key}' = {v} ({(v == 1 ? "완료 표시됨 — 튜토리얼 안 뜸" : "미완료 — 다음 New Run 시 표시")})");
            Debug.Log($"[Tutorial] AfterTutorial = {PlayerPrefs.GetInt(TutorialTipKeys.AfterTutorial, 0)}");
            Debug.Log($"[Tutorial] FirstReward = {PlayerPrefs.GetInt(TutorialTipKeys.FirstReward, 0)}");
            Debug.Log($"[Tutorial] FirstPotion = {PlayerPrefs.GetInt(TutorialTipKeys.FirstPotion, 0)}");
        }
    }
}
#endif
