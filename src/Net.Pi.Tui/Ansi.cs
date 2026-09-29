namespace Net.Pi.Tui;

public static class Ansi
{
    public const string Reset = "\x1b[0m";
    public const string Bold = "\x1b[1m";
    public const string Dim = "\x1b[2m";
    public const string Italic = "\x1b[3m";
    public const string Underline = "\x1b[4m";

    public const string Black = "\x1b[30m";
    public const string Red = "\x1b[31m";
    public const string Green = "\x1b[32m";
    public const string Yellow = "\x1b[33m";
    public const string Blue = "\x1b[34m";
    public const string Magenta = "\x1b[35m";
    public const string Cyan = "\x1b[36m";
    public const string White = "\x1b[37m";
    public const string Gray = "\x1b[90m";

    public const string BgBlue = "\x1b[44m";
    public const string BgDarkGray = "\x1b[100m";

    public static string Color(string text, string ansiColor) => $"{ansiColor}{text}{Reset}";
    public static string CyanText(string text) => Color(text, Cyan);
    public static string GreenText(string text) => Color(text, Green);
    public static string YellowText(string text) => Color(text, Yellow);
    public static string RedText(string text) => Color(text, Red);
    public static string GrayText(string text) => Color(text, Gray);
    public static string BoldText(string text) => $"{Bold}{text}{Reset}";
}
