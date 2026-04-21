using System.Collections.Generic;
using DianoCard.Data;
using DianoCard.Game;
using UnityEngine;

/// <summary>
/// 훈련장 화면 — GameState == Training일 때만 그려짐.
/// 모든 적을 타입(일반/엘리트/보스)별로 리스트업해서 클릭 한 번으로 전투에 들어감.
/// 전투 종료 시 GSM.EndBattle이 IsTrainingMode를 보고 Training 상태로 복귀시키므로
/// 반복 테스트가 가능. HP/덱은 매 전투 시작마다 풀로 리셋.
/// </summary>
public class TrainingUI : MonoBehaviour
{
    private const float RefW = 1280f;
    private const float RefH = 720f;

    private Vector2 _scroll;
    private GUIStyle _titleStyle;
    private GUIStyle _sectionStyle;
    private GUIStyle _btnStyle;
    private GUIStyle _descStyle;
    private bool _stylesReady;

    void OnGUI()
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null || gsm.State != GameState.Training) return;

        EnsureStyles();

        // 1280x720 가상 좌표로 스케일링
        float scale = Mathf.Min(Screen.width / RefW, Screen.height / RefH);
        var prevMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

        // 배경 어둡게
        var prevColor = GUI.color;
        GUI.color = new Color(0.06f, 0.05f, 0.09f, 1f);
        GUI.DrawTexture(new Rect(0, 0, RefW, RefH), Texture2D.whiteTexture);
        GUI.color = prevColor;

        GUILayout.BeginArea(new Rect(60, 40, RefW - 120, RefH - 80));

        GUILayout.Label("훈련장 — TRAINING GROUND", _titleStyle);
        GUILayout.Label("적을 골라 자유롭게 전투. HP와 덱은 매 전투마다 풀로 리셋됩니다.", _descStyle);
        GUILayout.Space(10);

        _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));

        DrawEnemySection("일반 (NORMAL)", EnemyType.NORMAL);
        GUILayout.Space(8);
        DrawEnemySection("엘리트 (ELITE)", EnemyType.ELITE);
        GUILayout.Space(8);
        DrawEnemySection("보스 (BOSS)", EnemyType.BOSS);

        GUILayout.Space(12);
        DrawMultiEnemyPresets(gsm);

        GUILayout.EndScrollView();

        GUILayout.Space(10);
        if (GUILayout.Button("로비로 돌아가기", _btnStyle, GUILayout.Height(40)))
            gsm.ExitTraining();

        GUILayout.EndArea();

        GUI.matrix = prevMatrix;
    }

    private void DrawEnemySection(string header, EnemyType type)
    {
        GUILayout.Label(header, _sectionStyle);

        var list = new List<EnemyData>();
        foreach (var kv in DataManager.Instance.Enemies)
        {
            var e = kv.Value;
            if (e == null || e.enemyType != type) continue;
            // TBD 패턴은 아직 구현 안 된 후속 챕터 — 숨김
            if (!string.IsNullOrEmpty(e.patternSetId) && e.patternSetId.EndsWith("_TBD")) continue;
            list.Add(e);
        }
        list.Sort((a, b) => string.Compare(a.id, b.id, System.StringComparison.Ordinal));

        const int cols = 3;
        for (int i = 0; i < list.Count; i += cols)
        {
            GUILayout.BeginHorizontal();
            for (int j = 0; j < cols; j++)
            {
                int idx = i + j;
                if (idx >= list.Count) { GUILayout.FlexibleSpace(); continue; }
                var e = list[idx];
                DrawEnemyButton(e);
            }
            GUILayout.EndHorizontal();
        }
    }

    private void DrawEnemyButton(EnemyData e)
    {
        string label = $"{e.id}  {e.nameKr}\nHP {e.hp} / ATK {e.attack} / DEF {e.defense}";
        if (GUILayout.Button(label, _btnStyle, GUILayout.Height(64)))
        {
            GameStateManager.Instance.TrainingStartBattle(e.id);
        }
    }

    private void DrawMultiEnemyPresets(GameStateManager gsm)
    {
        GUILayout.Label("복합 전투 프리셋", _sectionStyle);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("일반 2마리 (E001 + E003)", _btnStyle, GUILayout.Height(40)))
            gsm.TrainingStartBattle("E001", "E003");
        if (GUILayout.Button("쌍둥이 (E103 × 2)", _btnStyle, GUILayout.Height(40)))
            gsm.TrainingStartBattle("E103", "E103");
        GUILayout.EndHorizontal();
    }

    private void EnsureStyles()
    {
        if (_stylesReady) return;
        _titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 28,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = new Color(1f, 0.88f, 0.5f) },
        };
        _sectionStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.8f, 0.95f, 1f) },
        };
        _descStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            wordWrap = true,
            normal = { textColor = new Color(0.8f, 0.8f, 0.8f) },
        };
        _btnStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
        };
        _stylesReady = true;
    }
}
