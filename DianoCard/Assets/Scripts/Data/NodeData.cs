using System.Collections.Generic;

namespace DianoCard.Data
{
    [System.Serializable]
    public class NodeData
    {
        public string id;
        public NodeType nodeType;
        public int weight;
        public int minFloor;
        public int maxFloor;
        public string description;

        public static NodeData FromRow(Dictionary<string, string> row)
        {
            return new NodeData
            {
                id = CSVUtil.GetString(row, "id"),
                nodeType = CSVUtil.GetEnum(row, "node_type", NodeType.NORMAL_BATTLE),
                weight = CSVUtil.GetInt(row, "weight"),
                minFloor = CSVUtil.GetInt(row, "min_floor"),
                maxFloor = CSVUtil.GetInt(row, "max_floor"),
                description = CSVUtil.GetString(row, "description"),
            };
        }
    }
}
