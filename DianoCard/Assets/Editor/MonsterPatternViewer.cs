using System.Collections.Generic;
using System.Text;
using DianoCard.Data;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 적 패턴 디버그 뷰어. 메뉴: Tools/Monster Pattern Viewer (단축키 Ctrl+Alt+M).
/// - 에디터 모드: CSV를 바로 로드해서 enemy.csv + enemy_pattern.csv + enemy_phase.csv 확인
/// - 플레이 모드: 진행 중인 전투의 EnemyInstance 라이브 상태(현재 step, 텔레그래프, 상태이상)도 같이 표시
/// </summary>
public class MonsterPatternViewer : EditorWindow
{
    [MenuItem("Tools/Monster Pattern Viewer %&m")]
    static void Open() => GetWindow<MonsterPatternViewer>("Pattern Viewer");

    private string _selectedEnemyId;
    private Vector2 _leftScroll;
    private Vector2 _rightScroll;
    private int _chapterFilter = 1; // 0=All, 1~4=챕터
    private bool _showPlaceholders;  // TBD 패턴셋 가진 적도 보일지
    private string _spawnIdInput = "E102";
    private int _customDamage = 5;

    private GUIStyle _monoStyle;
    private GUIStyle _headerStyle;

    private void OnEnable()
    {
        // 에디터에서 즉시 로드
        if (DataManager.Instance != null)
            DataManager.Instance.Load();
    }

    private void EnsureStyles()
    {
        if (_monoStyle == null)
        {
            _monoStyle = new GUIStyle(EditorStyles.label) { font = EditorStyles.standardFont, fontSize = 11, richText = true };
        }
        if (_headerStyle == null)
        {
            _headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
        }
    }

    private void OnGUI()
    {
        EnsureStyles();

        // 상단 툴바
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        if (GUILayout.Button("Reload CSV", EditorStyles.toolbarButton, GUILayout.Width(90)))
            DataManager.Instance.Load(force: true);

        GUILayout.Space(8);
        GUILayout.Label("챕터:", GUILayout.Width(38));
        _chapterFilter = EditorGUILayout.IntPopup(_chapterFilter,
            new[] { "All", "1", "2", "3", "4" },
            new[] { 0, 1, 2, 3, 4 },
            EditorStyles.toolbarPopup, GUILayout.Width(60));

        GUILayout.Space(8);
        _showPlaceholders = GUILayout.Toggle(_showPlaceholders, "TBD 표시", EditorStyles.toolbarButton, GUILayout.Width(80));

        GUILayout.FlexibleSpace();
        GUILayout.Label(Application.isPlaying ? "▶ Play Mode (라이브 상태 표시)" : "■ Edit Mode (CSV만 표시)", _monoStyle);
        EditorGUILayout.EndHorizontal();

        // 본체: 좌(적 리스트) / 우(상세)
        EditorGUILayout.BeginHorizontal();
        DrawEnemyList();
        DrawDetailPanel();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawEnemyList()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(220));
        _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll);

        var enemies = DataManager.Instance.Enemies;
        if (enemies == null || enemies.Count == 0)
        {
            EditorGUILayout.HelpBox("Enemy 데이터 없음. Reload CSV 클릭.", MessageType.Warning);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            return;
        }

        // 챕터/타입 그룹화 정렬
        var ordered = new List<EnemyData>(enemies.Values);
        ordered.Sort((a, b) =>
        {
            int c = a.chapter.CompareTo(b.chapter);
            if (c != 0) return c;
            int t = ((int)a.enemyType).CompareTo((int)b.enemyType);
            if (t != 0) return t;
            return string.Compare(a.id, b.id);
        });

        int currentChapter = -1;
        EnemyType currentType = (EnemyType)(-1);
        foreach (var e in ordered)
        {
            if (_chapterFilter != 0 && e.chapter != _chapterFilter) continue;
            bool isPlaceholder = e.patternSetId != null && e.patternSetId.EndsWith("_TBD");
            if (!_showPlaceholders && isPlaceholder) continue;

            if (e.chapter != currentChapter)
            {
                currentChapter = e.chapter;
                currentType = (EnemyType)(-1);
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField($"━ Chapter {e.chapter} ━", EditorStyles.miniBoldLabel);
            }
            if (e.enemyType != currentType)
            {
                currentType = e.enemyType;
                GUI.color = e.enemyType switch
                {
                    EnemyType.BOSS => new Color(1f, 0.5f, 0.5f),
                    EnemyType.ELITE => new Color(0.85f, 0.7f, 1f),
                    _ => Color.white,
                };
                EditorGUILayout.LabelField(e.enemyType.ToString(), EditorStyles.miniLabel);
                GUI.color = Color.white;
            }

            bool selected = e.id == _selectedEnemyId;
            var prevBg = GUI.backgroundColor;
            if (selected) GUI.backgroundColor = new Color(0.5f, 0.7f, 1f);
            string suffix = isPlaceholder ? " <color=#888>(TBD)</color>" : "";
            string artFlag = string.IsNullOrEmpty(e.image) ? " <color=#cc0>※</color>" : "";
            if (GUILayout.Button(new GUIContent($"{e.id}  {e.nameKr}{suffix}{artFlag}", "※ = 아트 없음 (placeholder 사용)"),
                                 _selectedEnemyId == e.id ? EditorStyles.helpBox : EditorStyles.miniButton,
                                 GUILayout.Height(22)))
            {
                _selectedEnemyId = e.id;
            }
            GUI.backgroundColor = prevBg;
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    private void DrawDetailPanel()
    {
        EditorGUILayout.BeginVertical();
        _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);

        if (string.IsNullOrEmpty(_selectedEnemyId))
        {
            EditorGUILayout.HelpBox("왼쪽에서 적을 선택하세요.", MessageType.Info);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            return;
        }

        var enemy = DataManager.Instance.GetEnemy(_selectedEnemyId);
        if (enemy == null)
        {
            EditorGUILayout.HelpBox("적 데이터 없음.", MessageType.Error);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            return;
        }

        // === 기본 스탯 ===
        EditorGUILayout.LabelField($"{enemy.id} · {enemy.nameKr} ({enemy.nameEn})", _headerStyle);
        EditorGUILayout.LabelField($"챕터 {enemy.chapter} · {enemy.enemyType}");
        EditorGUILayout.LabelField($"HP {enemy.hp} / ATK {enemy.attack} / DEF {enemy.defense} · 골드 {enemy.goldMin}~{enemy.goldMax}");
        EditorGUILayout.LabelField($"이미지: {(string.IsNullOrEmpty(enemy.image) ? "(없음 — placeholder)" : enemy.image)}");
        if (!string.IsNullOrEmpty(enemy.description))
            EditorGUILayout.HelpBox(enemy.description, MessageType.None);

        EditorGUILayout.Space(6);

        // === 패턴셋 ===
        EditorGUILayout.LabelField($"Pattern Set: {enemy.patternSetId}", EditorStyles.boldLabel);
        var steps = DataManager.Instance.GetPatternSet(enemy.patternSetId);
        if (steps == null)
        {
            EditorGUILayout.HelpBox("패턴 데이터 없음 (TBD 또는 미정의).", MessageType.Warning);
        }
        else
        {
            DrawPatternTable(steps);
        }

        // === 페이즈셋 (보스만) ===
        if (!string.IsNullOrEmpty(enemy.phaseSetId))
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField($"Phase Set: {enemy.phaseSetId}", EditorStyles.boldLabel);
            var phases = DataManager.Instance.GetPhaseSet(enemy.phaseSetId);
            if (phases == null)
            {
                EditorGUILayout.HelpBox("페이즈 데이터 없음.", MessageType.Warning);
            }
            else
            {
                foreach (var ph in phases)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.LabelField($"P{ph.phase} · enter HP {ph.enterHpRatio:P0} · pattern={ph.patternSetId}", EditorStyles.boldLabel);
                    if (!string.IsNullOrEmpty(ph.onEnterActions))
                        EditorGUILayout.LabelField($"on_enter: <color=#fc8>{ph.onEnterActions}</color>", _monoStyle);
                    if (!string.IsNullOrEmpty(ph.triggerText))
                        EditorGUILayout.LabelField($"trigger: \"{ph.triggerText}\"", _monoStyle);

                    var phaseSteps = DataManager.Instance.GetPatternSet(ph.patternSetId);
                    if (phaseSteps != null) DrawPatternTable(phaseSteps);
                    EditorGUILayout.EndVertical();
                }
            }
        }

        // === 라이브 상태 (플레이 모드 + 실제 전투 진행 중) ===
        if (Application.isPlaying)
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("━ 라이브 상태 ━", _headerStyle);
            DrawLiveState(enemy.id);
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    private void DrawPatternTable(List<EnemyPatternData> steps)
    {
        // 헤더
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label("step", EditorStyles.toolbarButton, GUILayout.Width(36));
        GUILayout.Label("phase", EditorStyles.toolbarButton, GUILayout.Width(40));
        GUILayout.Label("action", EditorStyles.toolbarButton, GUILayout.Width(140));
        GUILayout.Label("val×cnt", EditorStyles.toolbarButton, GUILayout.Width(60));
        GUILayout.Label("target", EditorStyles.toolbarButton, GUILayout.Width(70));
        GUILayout.Label("tele", EditorStyles.toolbarButton, GUILayout.Width(36));
        GUILayout.Label("condition", EditorStyles.toolbarButton, GUILayout.Width(100));
        GUILayout.Label("description", EditorStyles.toolbarButton);
        EditorGUILayout.EndHorizontal();

        foreach (var s in steps)
        {
            EditorGUILayout.BeginHorizontal();
            string stepLabel = s.stepOrder >= 90 ? $"<color=#fc8>!{s.stepOrder}</color>" : s.stepOrder.ToString();
            GUILayout.Label(stepLabel, _monoStyle, GUILayout.Width(36));
            GUILayout.Label(s.phase.ToString(), _monoStyle, GUILayout.Width(40));
            GUILayout.Label($"<color=#9cf>{s.action}</color>", _monoStyle, GUILayout.Width(140));
            GUILayout.Label(s.count > 1 ? $"{s.value}×{s.count}" : s.value.ToString(), _monoStyle, GUILayout.Width(60));
            GUILayout.Label(s.target.ToString(), _monoStyle, GUILayout.Width(70));
            GUILayout.Label(s.telegraphTurns.ToString(), _monoStyle, GUILayout.Width(36));
            GUILayout.Label(string.IsNullOrEmpty(s.condition) ? "-" : $"<color=#fc8>{s.condition}</color>", _monoStyle, GUILayout.Width(100));
            GUILayout.Label(s.description ?? "", _monoStyle);
            EditorGUILayout.EndHorizontal();
        }
    }

    private void DrawLiveState(string enemyId)
    {
        var battle = GetActiveBattle();
        if (battle == null || battle.state == null)
        {
            EditorGUILayout.HelpBox("진행 중인 전투 없음 (BattleUI/BattleManager 미초기화).", MessageType.None);
            return;
        }

        // === 글로벌 치트 ===
        DrawGlobalCheatPanel(battle);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("─ 적 인스턴스 ─", EditorStyles.boldLabel);

        var matches = new List<DianoCard.Battle.EnemyInstance>();
        foreach (var e in battle.state.enemies)
        {
            if (e.data.id == enemyId || e.data.id.StartsWith("ADD_" + enemyId))
                matches.Add(e);
        }

        if (matches.Count == 0)
        {
            EditorGUILayout.HelpBox($"전투에 {enemyId} 인스턴스 없음.", MessageType.None);
            return;
        }

        for (int i = 0; i < matches.Count; i++)
        {
            var e = matches[i];
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            string deadTag = e.IsDead ? " <color=#f44>[DEAD]</color>" : "";
            EditorGUILayout.LabelField($"#{i} {e.data.nameKr}{deadTag}", _monoStyle);
            EditorGUILayout.LabelField($"HP {e.hp}/{e.data.hp}  Block {e.block}  +ATK {e.extraAttack}", _monoStyle);
            EditorGUILayout.LabelField($"Pattern: {e.currentPatternSetId}  Phase: P{e.currentPhase}  StepCursor: {e.patternStepCursor}", _monoStyle);
            EditorGUILayout.LabelField($"Intent: {e.intentAction} {e.intentValue}×{e.intentCount} → {e.intentTarget}  Telegraph: T-{e.telegraphRemaining}", _monoStyle);
            if (e.poisonStacks > 0 || e.weakTurns > 0)
            {
                var sb = new StringBuilder("Status: ");
                if (e.poisonStacks > 0) sb.Append($"<color=#9c6>POISON {e.poisonStacks}</color>  ");
                if (e.weakTurns > 0) sb.Append($"<color=#fc6>WEAK {e.weakTurns}T</color>");
                EditorGUILayout.LabelField(sb.ToString(), _monoStyle);
            }

            // === 적 단위 치트 버튼 ===
            EditorGUILayout.BeginHorizontal();
            if (!e.IsDead)
            {
                if (GUILayout.Button("DMG 5", GUILayout.Width(54))) { e.TakeDamage(5); }
                if (GUILayout.Button("DMG 20", GUILayout.Width(60))) { e.TakeDamage(20); }
                if (GUILayout.Button("DMG 100", GUILayout.Width(64))) { e.TakeDamage(100); }
                if (GUILayout.Button("Kill", GUILayout.Width(40))) { e.TakeDamage(99999); }
            }
            if (GUILayout.Button("Remove (즉시 삭제)", GUILayout.Width(140)))
            {
                battle.state.enemies.Remove(e);
            }
            EditorGUILayout.EndHorizontal();

            if (!e.IsDead)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("+POISON 3", GUILayout.Width(80))) { e.poisonStacks += 3; }
                if (GUILayout.Button("+WEAK 2T", GUILayout.Width(80))) { e.weakTurns += 2; }
                if (GUILayout.Button("Roll Intent", GUILayout.Width(80)))
                    InvokePrivate(battle, "RollIntent", e);
                if (GUILayout.Button("Force Spawn Add", GUILayout.Width(120)))
                    InvokePrivate(battle, "SpawnAdd", e);
                if (GUILayout.Button("Phase Check", GUILayout.Width(90)))
                    InvokePrivate(battle, "CheckBossPhaseTransition", e);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
        }

        // === 플레이어 상태 ===
        var p = battle.state.player;
        EditorGUILayout.Space(4);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField($"Player: HP {p.hp}/{p.maxHp}  Block {p.block}  Mana {p.mana}/{p.maxMana}{(p.cheatInvincible ? " <color=#fc8>[INVINCIBLE]</color>" : "")}", _monoStyle);
        if (p.poisonStacks > 0 || p.weakTurns > 0)
        {
            var sb = new StringBuilder("Player Status: ");
            if (p.poisonStacks > 0) sb.Append($"<color=#9c6>POISON {p.poisonStacks}</color>  ");
            if (p.weakTurns > 0) sb.Append($"<color=#fc6>WEAK {p.weakTurns}T</color>");
            EditorGUILayout.LabelField(sb.ToString(), _monoStyle);
        }
        EditorGUILayout.EndVertical();
    }

    /// <summary>전투 진행 중일 때 공통으로 쓸 수 있는 글로벌 치트 패널.</summary>
    private void DrawGlobalCheatPanel(DianoCard.Battle.BattleManager battle)
    {
        EditorGUILayout.LabelField("─ 글로벌 치트 ─", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        var p = battle.state.player;

        // 무적 토글
        bool wasInvincible = p.cheatInvincible;
        p.cheatInvincible = EditorGUILayout.ToggleLeft(
            new GUIContent("Player 무적 (모든 피해/독 무시)", "ON: TakeDamage가 즉시 return"),
            p.cheatInvincible);
        if (wasInvincible != p.cheatInvincible)
            Debug.Log($"[Cheat] Player invincible = {p.cheatInvincible}");

        // 턴 컨트롤
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("End Turn (강제)", GUILayout.Height(24))) battle.EndTurn();
        if (GUILayout.Button("+1 Mana", GUILayout.Width(80))) p.mana++;
        if (GUILayout.Button("Mana Max", GUILayout.Width(80))) p.mana = p.maxMana;
        if (GUILayout.Button("Heal Full", GUILayout.Width(80))) p.hp = p.maxHp;
        EditorGUILayout.EndHorizontal();

        // Player 디버프 부여
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Self DMG 10", GUILayout.Width(100))) p.TakeDamage(10);
        if (GUILayout.Button("+POISON 3 (P)", GUILayout.Width(110))) p.poisonStacks += 3;
        if (GUILayout.Button("+WEAK 2T (P)", GUILayout.Width(110))) p.weakTurns += 2;
        if (GUILayout.Button("Clear Status", GUILayout.Width(100))) { p.poisonStacks = 0; p.weakTurns = 0; }
        EditorGUILayout.EndHorizontal();

        // 적 spawn (id로)
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Spawn ID:", GUILayout.Width(60));
        _spawnIdInput = EditorGUILayout.TextField(_spawnIdInput, GUILayout.Width(80));
        if (GUILayout.Button("적 추가", GUILayout.Width(70)))
        {
            var spawned = SpawnEnemyById(battle, _spawnIdInput);
            if (spawned == null) Debug.LogWarning($"[Cheat] enemy '{_spawnIdInput}' not found");
            else Debug.Log($"[Cheat] spawned {spawned.data.nameKr}");
        }
        if (GUILayout.Button("이 적으로만 교체 (전체 제거 후 추가)", GUILayout.Width(240)))
        {
            battle.state.enemies.Clear();
            var spawned = SpawnEnemyById(battle, _spawnIdInput);
            if (spawned == null) Debug.LogWarning($"[Cheat] enemy '{_spawnIdInput}' not found");
            else Debug.Log($"[Cheat] battle reset with {spawned.data.nameKr}");
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Kill All Enemies (HP 0)", GUILayout.Width(160)))
        {
            foreach (var e in battle.state.enemies) if (!e.IsDead) e.TakeDamage(99999);
        }
        if (GUILayout.Button("Remove Dead (시체 정리)", GUILayout.Width(160)))
        {
            battle.state.enemies.RemoveAll(e => e.IsDead);
        }
        if (GUILayout.Button("Remove ALL (즉시 비우기)", GUILayout.Width(170)))
        {
            battle.state.enemies.Clear();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    /// <summary>CSV에 정의된 적을 런타임 전투에 추가. RollIntent까지 호출.</summary>
    private DianoCard.Battle.EnemyInstance SpawnEnemyById(DianoCard.Battle.BattleManager battle, string id)
    {
        var data = DataManager.Instance.GetEnemy(id);
        if (data == null) return null;
        var inst = new DianoCard.Battle.EnemyInstance(data);
        battle.state.enemies.Add(inst);
        InvokePrivate(battle, "RollIntent", inst);
        return inst;
    }

    /// <summary>BattleManager의 private 메서드를 reflection으로 호출.</summary>
    private void InvokePrivate(DianoCard.Battle.BattleManager battle, string methodName, params object[] args)
    {
        var m = typeof(DianoCard.Battle.BattleManager).GetMethod(methodName,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (m == null) { Debug.LogWarning($"[PatternViewer] method not found: {methodName}"); return; }
        m.Invoke(battle, args);
    }

    /// <summary>현재 씬의 BattleManager 인스턴스(BattleUI._battle private 필드).</summary>
    private DianoCard.Battle.BattleManager GetActiveBattle()
    {
        var battleUI = Object.FindFirstObjectByType<BattleUI>();
        if (battleUI == null) return null;
        var f = typeof(BattleUI).GetField("_battle",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return f?.GetValue(battleUI) as DianoCard.Battle.BattleManager;
    }

    private void Update()
    {
        // 플레이 모드일 땐 라이브 상태가 변하므로 자주 repaint
        if (Application.isPlaying) Repaint();
    }
}
