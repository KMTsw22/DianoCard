using System.Collections.Generic;
using System.Text;

namespace DianoCard.Data
{
    /// <summary>
    /// RFC 4180 대응 간이 CSV 파서.
    /// - 쉼표 구분
    /// - 큰따옴표로 감싸진 필드 지원 ("a,b" -> a,b)
    /// - 필드 내 "" -> " 로 이스케이프
    /// - 개행(\r\n, \n) 지원
    /// </summary>
    public static class CSVParser
    {
        public static List<Dictionary<string, string>> Parse(string csvText)
        {
            var rows = ParseRaw(csvText);
            var result = new List<Dictionary<string, string>>();
            if (rows.Count == 0) return result;

            var headers = rows[0];
            for (int r = 1; r < rows.Count; r++)
            {
                var fields = rows[r];
                // 완전 빈 라인 스킵
                if (fields.Count == 1 && string.IsNullOrWhiteSpace(fields[0])) continue;

                var dict = new Dictionary<string, string>();
                for (int c = 0; c < headers.Count; c++)
                {
                    string key = headers[c].Trim();
                    string val = c < fields.Count ? fields[c] : string.Empty;
                    dict[key] = val;
                }
                result.Add(dict);
            }
            return result;
        }

        private static List<List<string>> ParseRaw(string csvText)
        {
            var rows = new List<List<string>>();
            var currentRow = new List<string>();
            var field = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < csvText.Length; i++)
            {
                char ch = csvText[i];

                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        // escaped "" -> "
                        if (i + 1 < csvText.Length && csvText[i + 1] == '"')
                        {
                            field.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        field.Append(ch);
                    }
                }
                else
                {
                    if (ch == '"')
                    {
                        inQuotes = true;
                    }
                    else if (ch == ',')
                    {
                        currentRow.Add(field.ToString());
                        field.Clear();
                    }
                    else if (ch == '\r')
                    {
                        // \r\n 처리: 다음 \n은 스킵
                        currentRow.Add(field.ToString());
                        field.Clear();
                        rows.Add(currentRow);
                        currentRow = new List<string>();
                        if (i + 1 < csvText.Length && csvText[i + 1] == '\n') i++;
                    }
                    else if (ch == '\n')
                    {
                        currentRow.Add(field.ToString());
                        field.Clear();
                        rows.Add(currentRow);
                        currentRow = new List<string>();
                    }
                    else
                    {
                        field.Append(ch);
                    }
                }
            }

            // 마지막 필드/행 flush
            if (field.Length > 0 || currentRow.Count > 0)
            {
                currentRow.Add(field.ToString());
                rows.Add(currentRow);
            }

            return rows;
        }
    }
}
