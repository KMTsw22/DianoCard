using DianoCard.Battle;
using DianoCard.Game;
using UnityEngine;

[DefaultExecutionOrder(2000)]
public class CheatUI : MonoBehaviour
{
    private bool _open;
    private Rect _windowRect = new(20f, 20f, 260f, 620f);
    private GUIStyle _btnStyle;
    private GUIStyle _titleStyle;
    private GUIStyle _stateStyle;

    void Update()
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.backquoteKey.wasPressedThisFrame)
            _open = !_open;
    }

    void OnGUI()
    {
        if (!_open) return;

        var matrix = GUI.matrix;
        float scale = Screen.width / 1280f;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

        _windowRect = GUI.Window(9999, _windowRect, DrawWindow, "");
        GUI.matrix = matrix;
    }

    private void DrawWindow(int id)
    {
        if (_btnStyle == null)
        {
            _btnStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                fixedHeight = 30f,
            };
            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 0.85f, 0.4f) },
            };
            _stateStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.7f, 0.9f, 1f) },
            };
        }

        var gsm = GameStateManager.Instance;
        string state = gsm != null ? gsm.State.ToString() : "N/A";

        GUILayout.Space(4f);
        GUILayout.Label("CHEAT PANEL", _titleStyle);
        GUILayout.Label($"Current: {state}", _stateStyle);
        GUILayout.Space(8f);

        if (gsm == null)
        {
            GUILayout.Label("GameStateManager not found");
            GUI.DragWindow();
            return;
        }

        if (GUILayout.Button("Lobby", _btnStyle))
            gsm.ReturnToLobby();

        if (GUILayout.Button("Character Select", _btnStyle))
            gsm.StartNewRun();

        if (GUILayout.Button("Reward", _btnStyle))
            gsm.Cheat_TriggerReward();

        if (GUILayout.Button("Shop", _btnStyle))
            gsm.Cheat_EnterShop();

        if (GUILayout.Button("Village", _btnStyle))
            gsm.Cheat_EnterVillage();

        GUILayout.Space(8f);
        GUILayout.Label("— 훈련장 입장 —", _stateStyle);

        if (GUILayout.Button("E901 이끼 수호석상 (보스)", _btnStyle))
            gsm.Cheat_StartBossBattle();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("E001", _btnStyle)) gsm.Cheat_StartBattleWith("E001");
        if (GUILayout.Button("E008", _btnStyle)) gsm.Cheat_StartBattleWith("E008");
        if (GUILayout.Button("E101", _btnStyle)) gsm.Cheat_StartBattleWith("E101");
        GUILayout.EndHorizontal();

        // ===== 전투 중에만 보이는 제어 =====
        var battle = GetActiveBattle();
        if (gsm.State == GameState.Battle && battle != null && battle.state != null)
        {
            GUILayout.Space(10f);
            GUILayout.Label("— 전투 중 제어 —", _stateStyle);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("전체 즉사", _btnStyle)) battle.Cheat_KillAllEnemies();
            if (GUILayout.Button("쫄류만 즉사", _btnStyle)) battle.Cheat_ClearAddsOnly();
            GUILayout.EndHorizontal();

            GUILayout.Label("보스 HP 설정", _stateStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("70%", _btnStyle)) battle.Cheat_SetPrimaryHpRatio(0.70f);
            if (GUILayout.Button("50%", _btnStyle)) battle.Cheat_SetPrimaryHpRatio(0.50f);
            if (GUILayout.Button("30%", _btnStyle)) battle.Cheat_SetPrimaryHpRatio(0.30f);
            if (GUILayout.Button("5%",  _btnStyle)) battle.Cheat_SetPrimaryHpRatio(0.05f);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("풀 회복", _btnStyle)) battle.Cheat_FullHeal();
            string invLabel = battle.state.player.cheatInvincible ? "무적 OFF" : "무적 ON";
            if (GUILayout.Button(invLabel, _btnStyle)) battle.Cheat_ToggleInvincible();
            GUILayout.EndHorizontal();
        }

        GUILayout.Space(12f);
        GUILayout.Label("— Gold —", _stateStyle);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("+100G", _btnStyle) && gsm.CurrentRun != null)
            gsm.CurrentRun.gold += 100;
        if (GUILayout.Button("+500G", _btnStyle) && gsm.CurrentRun != null)
            gsm.CurrentRun.gold += 500;
        GUILayout.EndHorizontal();

        GUI.DragWindow();
    }

    /// <summary>현재 씬에 떠있는 BattleUI에서 BattleManager 인스턴스를 획득.</summary>
    private DianoCard.Battle.BattleManager GetActiveBattle()
    {
        var ui = Object.FindFirstObjectByType<BattleUI>();
        return ui != null ? ui.Battle : null;
    }
}
