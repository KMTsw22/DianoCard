using System.Collections.Generic;

namespace DianoCard.Game
{
    public enum NodeKind
    {
        Combat,
        Elite,
        Boss,
        Camp,      // R — 휴식 (StS의 캠프파이어). 15층 고정 + 일반 층 랜덤.
        Event,     // 더 이상 맵에 직접 스폰되지 않음 — UNKNOWN/EVENT/TREASURE는 모두 Unknown으로 접힘. 향후 이벤트 UI 추가 시 부활.
        Merchant,
        Unknown,   // 미지의 노드 — 진입 시 StS 방식으로 Combat/Treasure(보물)/Shop/Camp(휴식)로 해석된다.
        Treasure,  // T — 9층 고정 보물 노드 (확정 유물). 전용 아이콘 추가 전까지 Unknown 아이콘 재활용.
    }

    /// <summary>
    /// 맵 상의 단일 노드. 적 id 리스트가 사전에 고정되어 있어서
    /// 같은 층 내에서도 왼쪽/오른쪽 선택에 따라 다른 적 조합과 싸우게 됨.
    /// 일반 전투는 보통 2마리, 보스는 1마리.
    ///
    /// nextColumns: StS식 path-first 생성 결과로 미리 결정되는 out-edge 목록.
    /// floor f의 노드가 floor f+1의 어떤 column들로 연결되는지를 저장한다.
    /// 보스 직전 층(15)의 노드들은 모두 보스 column(중앙)으로 fan-in.
    /// </summary>
    public class MapNode
    {
        public int floor;                    // 1-based. 1 = 첫 층, 15 = 휴식, 16 = 보스
        public int column;                   // 0..6 (보스는 중앙 = 3)
        public NodeKind kind;
        public List<string> enemyIds = new(); // 사전 선택된 적 id 리스트
        public bool cleared;
        public List<int> nextColumns = new(); // 다음 층(f+1)에서 연결되는 column들 — path 생성 시 결정
    }

    /// <summary>
    /// 한 run의 맵 레이아웃 & 진행 상태.
    /// </summary>
    public class MapState
    {
        public List<MapNode> nodes = new();
        public int currentFloor = 1;        // 현재 선택 가능한 층. 1층에 cleared 노드가 없으면 1층 전부가 시작 선택지.
        public int currentColumn = -1;      // 직전에 선택한 column (진행 위치 표시용)
        public int totalFloors = 15;        // 1..15 + 보스(16). StS와 동일.

        public bool IsBossCleared => currentFloor > totalFloors;

        public List<MapNode> NodesOnFloor(int floor)
        {
            var result = new List<MapNode>();
            foreach (var n in nodes) if (n.floor == floor) result.Add(n);
            return result;
        }

        public MapNode GetNode(int floor, int column)
        {
            foreach (var n in nodes) if (n.floor == floor && n.column == column) return n;
            return null;
        }
    }
}
