using System.Collections.Generic;

namespace DianoCard.Data
{
    [System.Serializable]
    public class EventChoice
    {
        public string textKr;
        public string textEn;
        public string effect;
        public int value;

        public string text => LocaleSettings.Pick(textKr, textEn);
        public bool IsEmpty => string.IsNullOrEmpty(textKr) && string.IsNullOrEmpty(textEn);
    }

    [System.Serializable]
    public class EventData
    {
        public string id;
        public string nameKr;
        public string nameEn;
        public int chapter;
        public string descriptionKr;
        public string descriptionEn;
        public EventChoice choice1;
        public EventChoice choice2;
        public EventChoice choice3;

        public string name => LocaleSettings.Pick(nameKr, nameEn);
        public string description => LocaleSettings.Pick(descriptionKr, descriptionEn);

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
                descriptionKr = CSVUtil.GetString(row, "description_kr"),
                descriptionEn = CSVUtil.GetString(row, "description_en"),
                choice1 = new EventChoice
                {
                    textKr = CSVUtil.GetString(row, "choice1_text_kr"),
                    textEn = CSVUtil.GetString(row, "choice1_text_en"),
                    effect = CSVUtil.GetString(row, "choice1_effect"),
                    value = CSVUtil.GetInt(row, "choice1_value"),
                },
                choice2 = new EventChoice
                {
                    textKr = CSVUtil.GetString(row, "choice2_text_kr"),
                    textEn = CSVUtil.GetString(row, "choice2_text_en"),
                    effect = CSVUtil.GetString(row, "choice2_effect"),
                    value = CSVUtil.GetInt(row, "choice2_value"),
                },
                choice3 = new EventChoice
                {
                    textKr = CSVUtil.GetString(row, "choice3_text_kr"),
                    textEn = CSVUtil.GetString(row, "choice3_text_en"),
                    effect = CSVUtil.GetString(row, "choice3_effect"),
                    value = CSVUtil.GetInt(row, "choice3_value"),
                },
            };
        }
    }
}
