using System.Text;

namespace ClipDwg.Style;

/// <summary>
/// 한 줄짜리 JSON을 사람이 고칠 수 있게 들여쓴다.
/// 구조를 해석하지 않고 문자 단위로만 다시 배치하므로 내용은 그대로 보존된다.
/// </summary>
public static class JsonFormatter
{
    private const string Indentation = "  ";

    public static string Indent(string json)
    {
        if (string.IsNullOrEmpty(json))
            return json;

        var sb = new StringBuilder(json.Length * 2);
        int depth = 0;
        bool inString = false;
        bool escaped = false;

        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];

            if (inString)
            {
                sb.Append(c);
                if (escaped)
                    escaped = false;
                else if (c == '\\')
                    escaped = true;
                else if (c == '"')
                    inString = false;

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    sb.Append(c);
                    break;

                case '{':
                case '[':
                    sb.Append(c);
                    // 빈 객체·배열은 "{}" / "[]" 로 붙여 둔다.
                    if (json[NextNonSpace(json, i + 1)] == (c == '{' ? '}' : ']'))
                    {
                        i = NextNonSpace(json, i + 1);
                        sb.Append(json[i]);
                        break;
                    }

                    depth++;
                    AppendLine(sb, depth);
                    break;

                case '}':
                case ']':
                    depth--;
                    AppendLine(sb, depth);
                    sb.Append(c);
                    break;

                case ',':
                    sb.Append(c);
                    AppendLine(sb, depth);
                    break;

                case ':':
                    sb.Append(": ");
                    break;

                default:
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>공백을 건너뛴 다음 문자의 위치. 끝까지 공백이면 마지막 위치.</summary>
    private static int NextNonSpace(string s, int from)
    {
        int i = from;
        while (i < s.Length && char.IsWhiteSpace(s[i]))
            i++;
        return i < s.Length ? i : s.Length - 1;
    }

    private static void AppendLine(StringBuilder sb, int depth)
    {
        sb.Append('\n');
        for (int i = 0; i < depth; i++)
            sb.Append(Indentation);
    }
}
