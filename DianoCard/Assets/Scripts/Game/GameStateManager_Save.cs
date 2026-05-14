using System;
using DianoCard.Data;
using UnityEngine;

namespace DianoCard.Game
{
    // 런 영구 저장 — Application.persistentDataPath/save.json 의 "RunState_v1" 키에 JSON으로 박는다.
    // 저장 트리거: ConfirmRelicPick(→Map) / AdvanceToNextFloorOrVictory(→Map) / OnApplicationQuit / OnApplicationPause(true)
    // 클리어 트리거: ReturnToLobby / EndBattle(Defeat) / AdvanceToNextFloorOrVictory(Victory)
    // 비저장: 튜토리얼/훈련장 모드. 정규화: 저장 시 currentColumn=-1, state=Map (Reward/Shop/Event 등 도중 종료 → 해당 노드 재진입).
    public partial class GameStateManager
    {
        private const string RunSaveKey = "RunState_v1";

        [Serializable]
        private class RunSavePayload
        {
            public int version = 1;
            public RunState run;
            public MapState map;
            public string chapterId;
        }

        /// <summary>저장된 런이 디스크에 있는지. LobbyUI의 Continue 버튼 활성/비활성 판단용.</summary>
        public static bool HasSavedRun()
        {
            bool hasKey = SaveSystem.HasKey(RunSaveKey);
            string s = hasKey ? SaveSystem.GetString(RunSaveKey, "") : "";
            bool result = hasKey && !string.IsNullOrEmpty(s);
            Debug.Log($"[GSM-Save] HasSavedRun: hasKey={hasKey} strLen={s.Length} result={result} path={Application.persistentDataPath}");
            return result;
        }

        /// <summary>저장된 런을 폐기. 패배/승리/Lobby 복귀 시 호출.</summary>
        public static void ClearSavedRun()
        {
            if (SaveSystem.HasKey(RunSaveKey))
            {
                SaveSystem.DeleteKey(RunSaveKey);
                SaveSystem.Save();
                Debug.Log("[GSM-Save] ClearSavedRun");
            }
        }

        /// <summary>현재 CurrentRun/CurrentMap을 디스크에 스냅샷.
        /// 정규화: 저장 직전 state를 Map으로, currentColumn을 -1로 강제 — 따라서
        /// Reward/Shop/Event 도중 강제 종료해도 복원 시 해당 노드를 다시 선택할 수 있는 상태로 시작.</summary>
        public void SaveCurrentRun()
        {
            // 튜토리얼/훈련장은 영구 저장 대상 아님.
            if (IsTutorialMode || IsTrainingMode) return;
            if (CurrentRun == null || CurrentMap == null) return;
            // CharacterSelect/RelicPick 진입 직전 등 맵이 아직 안 생성된 시점은 저장 의미 없음 (덱이 비어있을 수 있음).
            if (CurrentRun.deck == null || CurrentRun.deck.Count == 0) return;

            // 저장 직전 정규화 — 현재 노드 선택 미확정으로 되돌림.
            // 이미 currentColumn이 -1이면 (=Map 진입 직후 상태) 그대로 유지됨.
            int normalizedColumn = CurrentMap.currentColumn;
            if (State != GameState.Map)
            {
                normalizedColumn = -1;
            }

            var snapshotMap = new MapState
            {
                nodes = CurrentMap.nodes,
                currentFloor = CurrentMap.currentFloor,
                currentColumn = normalizedColumn,
                totalFloors = CurrentMap.totalFloors,
            };

            // pendingReward는 [NonSerialized]이므로 자동 제외. 그 외 RunState 필드는 그대로.
            var payload = new RunSavePayload
            {
                version = 1,
                run = CurrentRun,
                map = snapshotMap,
                chapterId = CurrentRun.chapterId,
            };

            try
            {
                string json = JsonUtility.ToJson(payload);
                SaveSystem.SetString(RunSaveKey, json);
                SaveSystem.Save();
                Debug.Log($"[GSM-Save] SaveCurrentRun floor={CurrentMap.currentFloor} hp={CurrentRun.playerCurrentHp}/{CurrentRun.playerMaxHp} gold={CurrentRun.gold} deck={CurrentRun.deck.Count} relics={CurrentRun.relics.Count}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GSM-Save] SaveCurrentRun failed: {ex.Message}");
            }
        }

        /// <summary>저장된 런을 복원해서 Map 상태로 진입. LobbyUI Continue 버튼이 호출.
        /// 저장이 없거나 파싱 실패하면 false 반환 (호출자가 폴백 처리).</summary>
        public bool LoadSavedRun()
        {
            if (!HasSavedRun())
            {
                Debug.LogWarning("[GSM-Save] LoadSavedRun: no save");
                return false;
            }

            try
            {
                string json = SaveSystem.GetString(RunSaveKey, "");
                var payload = JsonUtility.FromJson<RunSavePayload>(json);
                if (payload == null || payload.run == null || payload.map == null)
                {
                    Debug.LogError("[GSM-Save] LoadSavedRun: payload parse null");
                    ClearSavedRun();
                    return false;
                }

                // JsonUtility는 enum/구조체는 잘 복원하지만 ScriptableObject나 외부 참조는 못 함.
                // CardData/RelicData/PotionData는 단순 데이터 클래스라 OK. 그러나 같은 id의 인스턴스가
                // DataManager에 따로 캐시돼있을 수 있으므로, 가능하면 id로 다시 lookup해서 카논 인스턴스로 교체.
                ReconcileWithCatalog(payload.run);

                CurrentRun = payload.run;
                CurrentMap = payload.map;
                CurrentRun.pendingReward = null; // [NonSerialized]였지만 명시 초기화

                // 비-Map 상태에서 저장됐다면 Map으로 정규화 (SaveCurrentRun이 이미 처리하지만 보험)
                if (CurrentMap.currentColumn != -1 && State != GameState.Map)
                {
                    CurrentMap.currentColumn = -1;
                }

                CurrentShop = null;
                CurrentEnemies.Clear();
                RelicPickChoices = null;
                IsTrainingMode = false;
                IsTutorialMode = false;

                State = GameState.Map;
                Debug.Log($"[GSM-Save] LoadSavedRun OK floor={CurrentMap.currentFloor} hp={CurrentRun.playerCurrentHp}/{CurrentRun.playerMaxHp} gold={CurrentRun.gold} deck={CurrentRun.deck.Count} relics={CurrentRun.relics.Count}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GSM-Save] LoadSavedRun failed: {ex.Message}");
                ClearSavedRun();
                return false;
            }
        }

        // JsonUtility로 복원한 CardData/RelicData/PotionData는 새 인스턴스 — 게임 다른 부분이
        // 카탈로그 인스턴스와 ==(레퍼런스) 비교하는 경우(예: ShopRelicEntry.relic == r) 깨질 수 있다.
        // id 기반으로 카탈로그의 카논 인스턴스로 교체해서 레퍼런스 동등성을 회복.
        private static void ReconcileWithCatalog(RunState run)
        {
            if (run == null || DataManager.Instance == null || !DataManager.Instance.IsLoaded) return;

            if (run.deck != null)
            {
                for (int i = 0; i < run.deck.Count; i++)
                {
                    var c = run.deck[i];
                    if (c == null || string.IsNullOrEmpty(c.id)) continue;
                    var canonical = DataManager.Instance.GetCard(c.id);
                    if (canonical != null) run.deck[i] = canonical;
                }
            }
            if (run.relics != null)
            {
                for (int i = 0; i < run.relics.Count; i++)
                {
                    var r = run.relics[i];
                    if (r == null || string.IsNullOrEmpty(r.id)) continue;
                    var canonical = DataManager.Instance.GetRelic(r.id);
                    if (canonical != null) run.relics[i] = canonical;
                }
            }
            if (run.potions != null)
            {
                for (int i = 0; i < run.potions.Count; i++)
                {
                    var p = run.potions[i];
                    if (p == null || string.IsNullOrEmpty(p.id)) continue;
                    var canonical = DataManager.Instance.GetPotion(p.id);
                    if (canonical != null) run.potions[i] = canonical;
                }
            }
        }

        // Unity 라이프사이클 — 알트탭(Pause)이나 강제 종료 직전 마지막 보험 저장.
        // OnApplicationQuit는 깨끗한 종료 경로만 호출되므로, 모바일/포커스 손실 대비로 OnApplicationPause(true)도 같이 건다.
        void OnApplicationQuit()
        {
            Debug.Log($"[GSM-Save] OnApplicationQuit. state={State} currentRun={(CurrentRun != null ? "set" : "null")}");
            if (CurrentRun != null && State != GameState.Lobby && State != GameState.Defeat && State != GameState.Victory)
            {
                SaveCurrentRun();
            }
        }

        void OnApplicationPause(bool pause)
        {
            Debug.Log($"[GSM-Save] OnApplicationPause(pause={pause}). state={State} currentRun={(CurrentRun != null ? "set" : "null")}");
            if (!pause) return;
            // RelicPick/CharacterSelect는 아직 미확정 상태 — 이 시점에서 저장하면 빈 relics가 박힘.
            // 사용자 결정이 완료되는 ConfirmRelicPick에서 저장하도록 유보.
            if (State == GameState.RelicPick || State == GameState.CharacterSelect) return;
            if (CurrentRun != null && State != GameState.Lobby && State != GameState.Defeat && State != GameState.Victory)
            {
                SaveCurrentRun();
            }
        }
    }
}
