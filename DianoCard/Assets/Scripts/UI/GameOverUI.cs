using System;
using System.Collections.Generic;
using DianoCard.Game;
using UnityEngine;

/// <summary>
/// 승리/패배 화면. GameState == Victory 또는 Defeat일 때만 그려짐.
/// 단일 버튼: BACK TO LOBBY.
/// </summary>
public class GameOverUI : MonoBehaviour
{
    private const float RefW = 1280f;
    private const float RefH = 720f;

    private readonly List<Action> _pending = new();

    private GUIStyle _titleStyle;
    private GUIStyle _subStyle;
    private GUIStyle _buttonStyle;
    private bool _stylesReady;

    void Update()
    {
        if (_pending.Count == 0) return;
        var snapshot = new List<Action>(_pending);
        _pending.Clear();
        foreach (var a in snapshot) a?.Invoke();
    }

    void OnGUI()
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null) return;
        if (gsm.State != GameState.Defeat && gsm.State != GameState.Victory) return;

        EnsureStyles();

        float scale = Mathf.Min(Screen.width / RefW, Screen.height / RefH);
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));

        bool isVictory = gsm.State == GameState.Victory;

        // 배경
        var prev = GUI.color;
        GUI.color = isVictory
            ? new Color(0.12f, 0.08f, 0.04f, 1f)
            : new Color(0.1f, 0.04f, 0.04f, 1f);
        GUI.DrawTexture(new Rect(0, 0, RefW, RefH), Texture2D.whiteTexture);
        GUI.color = prev;

        // 타이틀
        string title = isVictory ? "VICTORY" : "DEFEAT";
        Color titleColor = isVictory
            ? new Color(1f, 0.9f, 0.5f)
            : new Color(0.9f, 0.3f, 0.3f);
        _titleStyle.normal.textColor = titleColor;
        GUI.Label(new Rect(0, 160, RefW, 140), title, _titleStyle);

        // 서브 문구
        string sub = isVictory ? "1챕터를 클리어했습니다!" : "당신의 모험은 여기서 끝났다...";
        GUI.Label(new Rect(0, 310, RefW, 30), sub, _subStyle);

        // 런 통계
        if (gsm.CurrentRun != null)
        {
            var run = gsm.CurrentRun;
            string stats =
                $"Floor {run.currentFloor}   Gold {run.gold}\n" +
                $"Deck {run.deck.Count}   Relics {run.relics.Count}   Potions {run.potions.Count}";
            GUI.Label(new Rect(0, 360, RefW, 60), stats, _subStyle);
        }

        // 로비로 돌아가기
        const float btnW = 280, btnH = 68;
        if (GUI.Button(new Rect((RefW - btnW) / 2f, 480, btnW, btnH), "BACK TO LOBBY", _buttonStyle))
        {
            _pending.Add(() => gsm.ReturnToLobby());
        }
    }

    private void EnsureStyles()
    {
        if (_stylesReady) return;
        _titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 110,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
        };
        _subStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 20,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.9f, 0.9f, 0.9f) },
        };
        _buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 24,
            fontStyle = FontStyle.Bold,
        };
        _stylesReady = true;
    }
}
