using System.Collections.Generic;

namespace DianoCard.Game
{
    public enum NodeKind
    {
        Combat,
        Elite,
        Boss,
        Camp,      // MVP 미구현
        Event,     // MVP 미구현
        Merchant,  // MVP 미구현
    }

    /// <summary>
    /// 맵 상의 단일 노드. 적 id 리스트가 사전에 고정되어 있어서
    /// 같은 층 내에서도 왼쪽/오른쪽 선택에 따라 다른 적 조합과 싸우게 됨.
    /// 일반 전투는 보통 2마리, 보스는 1마리.
    /// </summary>
    public class MapNode
    {
        public int floor;                    // 1-based. 1 = 첫 층, 5 = 보스
        public int column;                   // 0..2 (보스는 1)
        public NodeKind kind;
        public List<string> enemyIds = new(); // 사전 선택된 적 id 리스트
        public bool cleared;
    }

    /// <summary>
    /// 한 run의 맵 레이아웃 & 진행 상태.
    /// </summary>
    public class MapState
    {
        public List<MapNode> nodes = new();
        public int currentFloor = 1;
        public int currentColumn = -1;  // 직전에 선택한 column (진행 위치 표시용)
        public int totalFloors = 5;

        public bool IsBossCleared => currentFloor > totalFloors;

        public List<MapNode> NodesOnFloor(int floor)
        {
            var result = new List<MapNode>();
            foreach (var n in nodes) if (n.floor == floor) result.Add(n);
            return result;
        }
    }
}
