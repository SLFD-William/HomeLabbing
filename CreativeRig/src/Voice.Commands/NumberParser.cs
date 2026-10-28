using System.Globalization;

namespace Voice.Commands;

/// <summary>Finds a number in a transcript, as digits ("171") or words ("one seventy one").</summary>
public static class NumberParser
{
    private static readonly Dictionary<string, int> Units = new()
    {
        ["zero"] = 0, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5,
        ["six"] = 6, ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11,
        ["twelve"] = 12, ["thirteen"] = 13, ["fourteen"] = 14, ["fifteen"] = 15, ["sixteen"] = 16,
        ["seventeen"] = 17, ["eighteen"] = 18, ["nineteen"] = 19,
    };

    private static readonly Dictionary<string, int> Tens = new()
    {
        ["twenty"] = 20, ["thirty"] = 30, ["forty"] = 40, ["fifty"] = 50,
        ["sixty"] = 60, ["seventy"] = 70, ["eighty"] = 80, ["ninety"] = 90,
    };

    /// <summary>Returns the first number found in <paramref name="tokens"/> starting at <paramref name="start"/>.</summary>
    public static bool TryFind(IReadOnlyList<string> tokens, int start, out double value)
    {
        for (var i = start; i < tokens.Count; i++)
        {
            if (double.TryParse(tokens[i], NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }

            if (IsNumberWord(tokens[i]) && TryParseWords(tokens, i, out value))
            {
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static bool IsNumberWord(string t) => Units.ContainsKey(t) || Tens.ContainsKey(t) || t == "hundred";

    private static bool TryParseWords(IReadOnlyList<string> tokens, int start, out double value)
    {
        var total = 0;
        var seenHundred = false;
        var any = false;

        for (var i = start; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (any && t is "and" or "a" && i + 1 < tokens.Count && IsNumberWord(tokens[i + 1]))
            {
                continue;
            }

            if (t == "hundred")
            {
                total = (total == 0 ? 1 : total) * 100;
                seenHundred = true;
            }
            else if (Tens.TryGetValue(t, out var ten))
            {
                // "one seventy one" is spoken shorthand for 171.
                if (!seenHundred && total is >= 1 and <= 9)
                {
                    total = total * 100 + ten;
                    seenHundred = true;
                }
                else
                {
                    total += ten;
                }
            }
            else if (Units.TryGetValue(t, out var unit))
            {
                if (!seenHundred && total is >= 20 && total % 10 != 0)
                {
                    break;
                }

                total += unit;
            }
            else
            {
                break;
            }

            any = true;
        }

        value = total;
        return any;
    }
}
