using DianoCard.Game;
using UnityEditor;
using UnityEngine;

public class CheatWindow : EditorWindow
{
    [MenuItem("Tools/Cheat Panel %#F12")]
    static void Open() => GetWindow<CheatWindow>("Cheat Panel");

    private void OnGUI()
    {
        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("플레이 모드에서만 사용 가능합니다.", MessageType.Info);
            return;
        }

        var gsm = GameStateManager.Instance;
        if (gsm == null)
        {
            EditorGUILayout.HelpBox("GameStateManager를 찾을 수 없습니다.", MessageType.Warning);
            return;
        }

        EditorGUILayout.LabelField("현재 상태", gsm.State.ToString(), EditorStyles.boldLabel);
        EditorGUILayout.Space(8);

        EditorGUILayout.LabelField("UI 전환", EditorStyles.boldLabel);

        if (GUILayout.Button("Lobby", GUILayout.Height(28)))
            gsm.ReturnToLobby();

        if (GUILayout.Button("Character Select", GUILayout.Height(28)))
            gsm.StartNewRun();

        if (GUILayout.Button("Reward", GUILayout.Height(28)))
            gsm.Cheat_TriggerReward();

        if (GUILayout.Button("Card Picker (F10)", GUILayout.Height(28)))
        {
            gsm.Cheat_TriggerReward();
            var rewardUI = Object.FindFirstObjectByType<RewardUI>();
            if (rewardUI != null)
                rewardUI.Cheat_JumpToCardPicker();
        }

        if (GUILayout.Button("Shop", GUILayout.Height(28)))
            gsm.Cheat_EnterShop();

        if (GUILayout.Button("Village", GUILayout.Height(28)))
            gsm.Cheat_EnterVillage();

        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("골드", EditorStyles.boldLabel);

        if (gsm.CurrentRun != null)
        {
            EditorGUILayout.LabelField("보유 골드", gsm.CurrentRun.gold.ToString());
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+100G")) gsm.CurrentRun.gold += 100;
            if (GUILayout.Button("+500G")) gsm.CurrentRun.gold += 500;
            if (GUILayout.Button("+1000G")) gsm.CurrentRun.gold += 1000;
            EditorGUILayout.EndHorizontal();
        }
        else
        {
            EditorGUILayout.HelpBox("Run이 없습니다. 위 버튼으로 먼저 진입하세요.", MessageType.None);
        }

        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("플레이어", EditorStyles.boldLabel);

        if (gsm.CurrentRun != null)
        {
            EditorGUILayout.LabelField("HP", $"{gsm.CurrentRun.playerCurrentHp} / {gsm.CurrentRun.playerMaxHp}");
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Full Heal"))
                gsm.CurrentRun.playerCurrentHp = gsm.CurrentRun.playerMaxHp;
            if (GUILayout.Button("HP -10"))
                gsm.CurrentRun.playerCurrentHp = Mathf.Max(1, gsm.CurrentRun.playerCurrentHp - 10);
            EditorGUILayout.EndHorizontal();
        }

        Repaint();
    }
}
