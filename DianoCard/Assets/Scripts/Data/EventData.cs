using System.Collections.Generic;

namespace DianoCard.Data
{
    [System.Serializable]
    public class EventChoice
    {
        public string text;
        public string effect;
        public int value;

        public bool IsEmpty => string.IsNullOrEmpty(text);
    }

    [System.Serializable]
    public class EventData
    {
        public string id;
        public string nameKr;
        public string nameEn;
        public int chapter;
        public string description;
        public EventChoice choice1;
        public EventChoice choice2;
        public EventChoice choice3;

        public IEnumerable<EventChoice> Choices
        {
            get
            {
                if (!choice1.IsEmpty) yield return choice1;
                if (!choice2.IsEmpty) yield return choice2;
                if (!choice3.IsEmpty) yield return choice3;
            }
        }

        public static EventData FromRow(Dictionary<string, string> row)
        {
            return new EventData
            {
                id = CSVUtil.GetString(row, "id"),
                nameKr = CSVUtil.GetString(row, "name_kr"),
                nameEn = CSVUtil.GetString(row, "name_en"),
                chapter = CSVUtil.GetInt(row, "chapter"),
                description = CSVUtil.GetString(row, "description"),
                choice1 = new EventChoice
                {
                    text = CSVUtil.GetString(row, "choice1_text"),
                    effect = CSVUtil.GetString(row, "choice1_effect"),
                    value = CSVUtil.GetInt(row, "choice1_value"),
                },
                choice2 = new EventChoice
                {
                    text = CSVUtil.GetString(row, "choice2_text"),
                    effect = CSVUtil.GetString(row, "choice2_effect"),
                    value = CSVUtil.GetInt(row, "choice2_value"),
                },
                choice3 = new EventChoice
                {
                    text = CSVUtil.GetString(row, "choice3_text"),
                    effect = CSVUtil.GetString(row, "choice3_effect"),
                    value = CSVUtil.GetInt(row, "choice3_value"),
                },
            };
        }
    }
}
