namespace PocoToCsla;

public static class StringExtensions
{
    public static string TrimNewLine(this string @string) => @string.Trim(Environment.NewLine.ToCharArray());

    public static string TrimNewLineStart(this string @string) => @string.TrimStart(Environment.NewLine.ToCharArray());

    public static string TrimNewLineEnd(this string @string) => @string.TrimEnd(Environment.NewLine.ToCharArray());
}
