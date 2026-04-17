using DianoCard.Game;
using UnityEngine;

[DefaultExecutionOrder(2000)]
public class CheatUI : MonoBehaviour
{
    private bool _open;
    private Rect _windowRect = new(20f, 20f, 220f, 340f);
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
}
