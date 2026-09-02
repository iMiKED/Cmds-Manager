using System;
using System.Collections.Generic;
using System.Text;

namespace CmdsManager.Infrastructure.Execution
{
    public enum AnsiColorKind
    {
        Default,
        Indexed,
        Rgb
    }

    public struct AnsiColor : IEquatable<AnsiColor>
    {
        private AnsiColor(AnsiColorKind kind, int index, byte red, byte green, byte blue)
        {
            Kind = kind;
            Index = index;
            Red = red;
            Green = green;
            Blue = blue;
        }

        public AnsiColorKind Kind { get; }
        public int Index { get; }
        public byte Red { get; }
        public byte Green { get; }
        public byte Blue { get; }

        public static AnsiColor Default => new AnsiColor(AnsiColorKind.Default, 0, 0, 0, 0);

        public static AnsiColor FromIndex(int index)
        {
            return new AnsiColor(AnsiColorKind.Indexed, Math.Max(0, Math.Min(255, index)), 0, 0, 0);
        }

        public static AnsiColor FromRgb(int red, int green, int blue)
        {
            return new AnsiColor(AnsiColorKind.Rgb, 0, Clamp(red), Clamp(green), Clamp(blue));
        }

        public bool Equals(AnsiColor other)
        {
            return Kind == other.Kind && Index == other.Index && Red == other.Red &&
                Green == other.Green && Blue == other.Blue;
        }

        public override bool Equals(object obj)
        {
            return obj is AnsiColor && Equals((AnsiColor)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (int)Kind;
                hash = (hash * 397) ^ Index;
                hash = (hash * 397) ^ Red;
                hash = (hash * 397) ^ Green;
                return (hash * 397) ^ Blue;
            }
        }

        private static byte Clamp(int value)
        {
            return (byte)Math.Max(byte.MinValue, Math.Min(byte.MaxValue, value));
        }
    }

    public struct AnsiTextStyle : IEquatable<AnsiTextStyle>
    {
        public bool Bold { get; internal set; }
        public bool Dim { get; internal set; }
        public bool Italic { get; internal set; }
        public bool Underline { get; internal set; }
        public bool Inverse { get; internal set; }
        public bool Concealed { get; internal set; }
        public bool Strikethrough { get; internal set; }
        public AnsiColor Foreground { get; internal set; }
        public AnsiColor Background { get; internal set; }

        public static AnsiTextStyle Default
        {
            get
            {
                return new AnsiTextStyle
                {
                    Foreground = AnsiColor.Default,
                    Background = AnsiColor.Default
                };
            }
        }

        public bool Equals(AnsiTextStyle other)
        {
            return Bold == other.Bold && Dim == other.Dim && Italic == other.Italic &&
                Underline == other.Underline && Inverse == other.Inverse &&
                Concealed == other.Concealed && Strikethrough == other.Strikethrough &&
                Foreground.Equals(other.Foreground) && Background.Equals(other.Background);
        }

        public override bool Equals(object obj)
        {
            return obj is AnsiTextStyle && Equals((AnsiTextStyle)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Bold ? 1 : 0;
                hash = (hash * 397) ^ (Dim ? 1 : 0);
                hash = (hash * 397) ^ (Italic ? 1 : 0);
                hash = (hash * 397) ^ (Underline ? 1 : 0);
                hash = (hash * 397) ^ (Inverse ? 1 : 0);
                hash = (hash * 397) ^ (Concealed ? 1 : 0);
                hash = (hash * 397) ^ (Strikethrough ? 1 : 0);
                hash = (hash * 397) ^ Foreground.GetHashCode();
                return (hash * 397) ^ Background.GetHashCode();
            }
        }
    }

    public sealed class AnsiTextRun
    {
        internal AnsiTextRun(string text, AnsiTextStyle style)
        {
            Text = text ?? string.Empty;
            Style = style;
        }

        public string Text { get; internal set; }
        public AnsiTextStyle Style { get; }
    }

    public sealed class AnsiParseResult
    {
        internal AnsiParseResult(string plainText, IList<AnsiTextRun> runs)
        {
            PlainText = plainText ?? string.Empty;
            Runs = new List<AnsiTextRun>(runs ?? new AnsiTextRun[0]).AsReadOnly();
        }

        public string PlainText { get; }
        public IReadOnlyList<AnsiTextRun> Runs { get; }
    }

    public sealed class AnsiTextParser
    {
        private AnsiTextStyle _style;

        public AnsiTextParser() : this(AnsiTextStyle.Default)
        {
        }

        public AnsiTextParser(AnsiTextStyle initialStyle)
        {
            _style = initialStyle;
        }

        public AnsiTextStyle Style => _style;

        public AnsiParseResult Parse(string value)
        {
            value = value ?? string.Empty;
            if (value.IndexOf('\x1b') < 0 && value.IndexOf('\u009b') < 0 &&
                value.IndexOf('\u009d') < 0)
            {
                var plainRun = value.Length == 0
                    ? new List<AnsiTextRun>()
                    : new List<AnsiTextRun> { new AnsiTextRun(value, _style) };
                return new AnsiParseResult(value, plainRun);
            }

            var plain = new StringBuilder(value.Length);
            var runs = new List<AnsiTextRun>();
            var textStart = 0;
            var index = 0;
            while (index < value.Length)
            {
                var character = value[index];
                if (character != '\x1b' && character != '\u009b' && character != '\u009d')
                {
                    index++;
                    continue;
                }

                AppendText(value, textStart, index - textStart, plain, runs);
                if (character == '\u009b')
                    index = ConsumeCsi(value, index + 1);
                else if (character == '\u009d')
                    index = ConsumeStringControl(value, index + 1, false);
                else
                    index = ConsumeEscape(value, index);
                textStart = index;
            }

            AppendText(value, textStart, value.Length - textStart, plain, runs);
            return new AnsiParseResult(plain.ToString(), runs);
        }

        public static string Strip(string value)
        {
            return new AnsiTextParser().Parse(value).PlainText;
        }

        private int ConsumeEscape(string value, int escapeIndex)
        {
            if (escapeIndex + 1 >= value.Length) return value.Length;
            var command = value[escapeIndex + 1];
            switch (command)
            {
                case '[':
                    return ConsumeCsi(value, escapeIndex + 2);
                case ']':
                    return ConsumeStringControl(value, escapeIndex + 2, false);
                case 'P':
                case 'X':
                case '^':
                case '_':
                    return ConsumeStringControl(value, escapeIndex + 2, true);
                default:
                    var finalIndex = escapeIndex + 1;
                    while (finalIndex < value.Length && value[finalIndex] >= ' ' &&
                        value[finalIndex] <= '/') finalIndex++;
                    return finalIndex < value.Length ? finalIndex + 1 : value.Length;
            }
        }

        private int ConsumeCsi(string value, int parameterStart)
        {
            for (var index = parameterStart; index < value.Length; index++)
            {
                var character = value[index];
                if (character < '@' || character > '~') continue;
                if (character == 'm') ApplySgr(value.Substring(parameterStart, index - parameterStart));
                return index + 1;
            }

            return value.Length;
        }

        private static int ConsumeStringControl(string value, int contentStart, bool requireStringTerminator)
        {
            for (var index = contentStart; index < value.Length; index++)
            {
                if (!requireStringTerminator && value[index] == '\a') return index + 1;
                if (value[index] == '\u009c') return index + 1;
                if (value[index] == '\x1b' && index + 1 < value.Length && value[index + 1] == '\\')
                    return index + 2;
            }

            return value.Length;
        }

        private void ApplySgr(string parameterText)
        {
            var parameters = ParseParameters(parameterText);
            if (parameters.Count == 0) parameters.Add(0);
            for (var index = 0; index < parameters.Count; index++)
            {
                var code = parameters[index] ?? 0;
                if (code == 0)
                {
                    _style = AnsiTextStyle.Default;
                }
                else if (code == 1) _style.Bold = true;
                else if (code == 2) _style.Dim = true;
                else if (code == 3) _style.Italic = true;
                else if (code == 4) _style.Underline = true;
                else if (code == 7) _style.Inverse = true;
                else if (code == 8) _style.Concealed = true;
                else if (code == 9) _style.Strikethrough = true;
                else if (code == 21) _style.Bold = false;
                else if (code == 22) { _style.Bold = false; _style.Dim = false; }
                else if (code == 23) _style.Italic = false;
                else if (code == 24) _style.Underline = false;
                else if (code == 27) _style.Inverse = false;
                else if (code == 28) _style.Concealed = false;
                else if (code == 29) _style.Strikethrough = false;
                else if (code >= 30 && code <= 37) _style.Foreground = AnsiColor.FromIndex(code - 30);
                else if (code == 38) ApplyExtendedColor(parameters, ref index, false);
                else if (code == 39) _style.Foreground = AnsiColor.Default;
                else if (code >= 40 && code <= 47) _style.Background = AnsiColor.FromIndex(code - 40);
                else if (code == 48) ApplyExtendedColor(parameters, ref index, true);
                else if (code == 49) _style.Background = AnsiColor.Default;
                else if (code == 58) SkipExtendedColor(parameters, ref index);
                else if (code >= 90 && code <= 97) _style.Foreground = AnsiColor.FromIndex(code - 90 + 8);
                else if (code >= 100 && code <= 107) _style.Background = AnsiColor.FromIndex(code - 100 + 8);
            }
        }

        private void ApplyExtendedColor(IList<int?> parameters, ref int index, bool background)
        {
            if (index + 1 >= parameters.Count || !parameters[index + 1].HasValue) return;
            var mode = parameters[++index].Value;
            if (mode == 5 && index + 1 < parameters.Count && parameters[index + 1].HasValue)
            {
                var color = AnsiColor.FromIndex(parameters[++index].Value);
                if (background) _style.Background = color;
                else _style.Foreground = color;
                return;
            }

            if (mode != 2) return;
            var first = index + 1;
            if (first < parameters.Count && !parameters[first].HasValue) first++;
            if (first + 2 >= parameters.Count || !parameters[first].HasValue ||
                !parameters[first + 1].HasValue || !parameters[first + 2].HasValue) return;
            var rgb = AnsiColor.FromRgb(parameters[first].Value, parameters[first + 1].Value,
                parameters[first + 2].Value);
            if (background) _style.Background = rgb;
            else _style.Foreground = rgb;
            index = first + 2;
        }

        private static void SkipExtendedColor(IList<int?> parameters, ref int index)
        {
            if (index + 1 >= parameters.Count || !parameters[index + 1].HasValue) return;
            var mode = parameters[++index].Value;
            if (mode == 5 && index + 1 < parameters.Count) index++;
            else if (mode == 2)
            {
                var first = index + 1;
                if (first < parameters.Count && !parameters[first].HasValue) first++;
                index = Math.Min(parameters.Count - 1, first + 2);
            }
        }

        private static List<int?> ParseParameters(string value)
        {
            var result = new List<int?>();
            if (string.IsNullOrEmpty(value)) return result;
            foreach (var field in value.Split(new[] { ';' }, StringSplitOptions.None))
            {
                var colonParts = field.Split(new[] { ':' }, StringSplitOptions.None);
                int colorCode;
                int colorMode;
                if (colonParts.Length >= 3 && int.TryParse(colonParts[0], out colorCode) &&
                    (colorCode == 38 || colorCode == 48 || colorCode == 58) &&
                    int.TryParse(colonParts[1], out colorMode))
                {
                    result.Add(colorCode);
                    result.Add(colorMode);
                    if (colorMode == 5)
                    {
                        AddParameter(result, colonParts[colonParts.Length - 1]);
                    }
                    else if (colorMode == 2)
                    {
                        var colorStart = colonParts.Length >= 6 ? 3 : 2;
                        for (var index = colorStart; index < Math.Min(colorStart + 3, colonParts.Length); index++)
                            AddParameter(result, colonParts[index]);
                    }
                    continue;
                }

                AddParameter(result, colonParts[0]);
            }
            return result;
        }

        private static void AddParameter(ICollection<int?> result, string token)
        {
            int parsed;
            result.Add(int.TryParse(token, out parsed) ? parsed : (int?)null);
        }

        private void AppendText(string value, int start, int length, StringBuilder plain,
            IList<AnsiTextRun> runs)
        {
            if (length <= 0) return;
            var text = value.Substring(start, length);
            plain.Append(text);
            if (runs.Count > 0 && runs[runs.Count - 1].Style.Equals(_style))
                runs[runs.Count - 1].Text += text;
            else
                runs.Add(new AnsiTextRun(text, _style));
        }
    }
}
