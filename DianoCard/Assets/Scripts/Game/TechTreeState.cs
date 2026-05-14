using System;
using System.Collections.Generic;
using DianoCard.Data;
using UnityEngine;

namespace DianoCard.Game
{
    /// <summary>
    /// 트리 가지의 방향(테마) — 시각 레이아웃 + 스토리텔링 분류.
    /// 통화는 분리되지 않음(단일 포인트). 위치(=방향)이 정체성.
    /// </summary>
    public enum TechDirection
    {
        Center,   // 루트
        Right,    // 공격
        Left,     // 방어
        Up,       // 경제
        Down,     // 운영
    }

    /// <summary>
    /// 테크 노드 — Last Epoch 스타일. 한 노드에 여러 랭크(0..maxRank).
    /// 랭크당 perRankCost 포인트 차감. 캡스톤은 보통 1랭크/5pt, 일반 노드는 3~5랭크/1pt씩.
    /// </summary>
    public class TechNode
    {
        public string id;
        public string nameKr;
        public string nameEn;
        /// <summary>"+{r}" 같은 토큰을 직접 박지 않고, 효과 한 줄을 그대로 보여줌. UI에서 currentRank/maxRank를 함께 표기.</summary>
        public string descriptionKr;
        public string descriptionEn;
        public TechDirection direction;
        public int maxRank;
        public int perRankCost;
        public string prereqId;
        public Vector2 pos;        // 가상 1280×720 좌표
        public bool isCapstone;

        public string name => LocaleSettings.Pick(nameKr, nameEn);
        public string description => LocaleSettings.Pick(descriptionKr, descriptionEn);
    }

    [Serializable]
    public class NodeRankEntry
    {
        public string id;
        public int rank;
    }

    /// <summary>
    /// 영구 메타 진행 상태 — 단일 포인트 통화 + 노드별 현재 랭크.
    /// SaveSystem("TechTreeState_v2")에 JSON 문자열로 저장. v1(브랜치 분리형)과 호환되지 않음 — 자동 마이그레이션 없음(MVP).
    /// </summary>
    [Serializable]
    public class TechTreeState
    {
        private const string PrefsKey = "TechTreeState_v2";

        public int points;
        public List<NodeRankEntry> ranks = new();

        [NonSerialized] public bool hasNewPoints;

        public static TechTreeState Load()
        {
            string json = SaveSystem.GetString(PrefsKey, "");
            if (string.IsNullOrEmpty(json)) return new TechTreeState();
            try
            {
                var s = JsonUtility.FromJson<TechTreeState>(json);
                if (s == null) return new TechTreeState();
                if (s.ranks == null) s.ranks = new List<NodeRankEntry>();
                return s;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TechTreeState] Load failed: {e.Message}");
                return new TechTreeState();
            }
        }

        public void Save()
        {
            SaveSystem.SetString(PrefsKey, JsonUtility.ToJson(this));
            SaveSystem.Save();
        }

        public int GetRank(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return 0;
            for (int i = 0; i < ranks.Count; i++)
                if (ranks[i].id == nodeId) return ranks[i].rank;
            return 0;
        }

        private void SetRankInternal(string nodeId, int newRank)
        {
            for (int i = 0; i < ranks.Count; i++)
            {
                if (ranks[i].id == nodeId)
                {
                    if (newRank <= 0) ranks.RemoveAt(i);
                    else ranks[i].rank = newRank;
                    return;
                }
            }
            if (newRank > 0) ranks.Add(new NodeRankEntry { id = nodeId, rank = newRank });
        }

        // 루트("ROOT")는 시각용 가짜 노드 — 항상 해금 상태로 간주.
        public bool IsUnlocked(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return false;
            if (nodeId == TechTreeCatalog.RootId) return true;
            return GetRank(nodeId) > 0;
        }
        public bool IsMaxed(TechNode node)    => node != null && GetRank(node.id) >= node.maxRank;

        /// <summary>다음 랭크 1단계 올릴 수 있는가 — 포인트·전제·맥스 모두 검사.</summary>
        public bool CanRankUp(TechNode node)
        {
            if (node == null) return false;
            int cur = GetRank(node.id);
            if (cur >= node.maxRank) return false;
            if (points < node.perRankCost) return false;
            // 루트는 항상 해금된 가짜 노드로 취급
            if (!string.IsNullOrEmpty(node.prereqId)
                && node.prereqId != TechTreeCatalog.RootId
                && GetRank(node.prereqId) < 1)
                return false;
            return true;
        }

        public bool TryRankUp(TechNode node)
        {
            if (!CanRankUp(node)) return false;
            points -= node.perRankCost;
            SetRankInternal(node.id, GetRank(node.id) + 1);
            Save();
            return true;
        }

        public void GrantPoints(int amount)
        {
            if (amount == 0) return;
            points = Mathf.Max(0, points + amount);
            hasNewPoints = true;
            Save();
        }

        public void ResetAll()
        {
            points = 0;
            ranks.Clear();
            Save();
        }
    }
}
