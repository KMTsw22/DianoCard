using System.Collections.Generic;

namespace DianoCard.Data
{
    [System.Serializable]
    public class UIStringData
    {
        public string id;
        public string value;

        public static UIStringData FromRow(Dictionary<string, string> row)
        {
            return new UIStringData
            {
                id = CSVUtil.GetString(row, "id"),
                value = CSVUtil.GetString(row, "value"),
            };
        }
    }
}
