using System.Text;
using System.Text.RegularExpressions;

namespace ToolFB.App.Services;

public static class SpintaxEngine
{
    public static readonly Dictionary<string, List<string>> IconCategories = new()
    {
        ["🔥 Nổi Bật"] = new() { "🔥", "⚡", "✨", "🌟", "💎", "🚀", "💯", "⭐", "🎉", "💥", "🏆", "👑" },
        ["😊 Cảm Xúc"] = new() { "😊", "🥰", "🤩", "🥳", "❤️", "💖", "😍", "👍", "👏", "🙌", "🤗", "😎", "💐" },
        ["🍀 May Mắn"] = new() { "🍀", "🌸", "🌺", "🌼", "🌻", "💐", "🌿", "🍃", "☀️", "🌈", "🌱", "🌾" },
        ["🛍️ Bán Hàng"] = new() { "✅", "🎯", "🎁", "👌", "📦", "📌", "🏆", "💯", "🤝", "🛒", "📞", "📩" },
        ["🎲 Hỗn Hợp"] = new()
        {
            "✨", "👍", "💬", "🍀", "🌸", "⚡", "🌟", "🔥", "🎯", "❤️",
            "💎", "👏", "🚀", "💯", "🎁", "🎉", "👌", "⭐", "🌺", "🌼",
            "🍃", "🌿", "🌻", "💐", "🤩", "😊", "🥰", "🥳", "🙌", "💪",
            "✅", "📦", "📌", "🏆", "☀️", "🌈"
        }
    };

    private static readonly string[] InvisibleSalts = new[]
    {
        "\u200B", // Zero-width space
        "\u200C", // Zero-width non-joiner
        "\u200D", // Zero-width joiner
        "\u2060"  // Word joiner
    };

    public static List<string> SplitCommentsSafely(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return new List<string>();

        var comments = new List<string>();
        var current = new StringBuilder();
        int depth = 0;

        foreach (char c in rawText)
        {
            if (c == '{')
            {
                depth++;
                current.Append(c);
            }
            else if (c == '}')
            {
                depth = Math.Max(0, depth - 1);
                current.Append(c);
            }
            else if (c == '|' && depth == 0)
            {
                var item = current.ToString().Trim();
                if (!string.IsNullOrEmpty(item))
                    comments.Add(item);
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        var last = current.ToString().Trim();
        if (!string.IsNullOrEmpty(last))
            comments.Add(last);

        return comments;
    }

    public static string ParseSpintax(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var pattern = new Regex(@"\{([^{}]+)\}");
        int loop = 0;
        var rng = Random.Shared;

        while (pattern.IsMatch(text) && loop < 25)
        {
            text = pattern.Replace(text, m =>
            {
                var parts = m.Groups[1].Value.Split('|');
                return parts[rng.Next(parts.Length)];
            });
            loop++;
        }

        return text;
    }

    public static string ApplyDynamicTagsAndSalt(string text, bool useSalt = true, string iconCat = "🎲 Hỗn Hợp", bool iconNewline = false)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var rng = Random.Shared;
        var now = DateTime.Now;

        // Greetings based on current hour
        string[] greetings = now.Hour switch
        {
            >= 5 and < 11 => new[] { "Chào buổi sáng", "Chào bạn", "Hello bạn", "Chúc ngày mới tốt lành", "Hi bạn" },
            >= 11 and < 14 => new[] { "Chào buổi trưa", "Chào bạn", "Hello bạn", "Hi bạn", "Chào bạn nhé" },
            >= 14 and < 18 => new[] { "Chào buổi chiều", "Chào bạn", "Hello bạn", "Hi bạn", "Chào bạn nhé" },
            _ => new[] { "Chào buổi tối", "Chào bạn", "Hello bạn", "Hi bạn", "Chào bạn nhé" }
        };

        string[] daysOfWeekVn = { "Chủ Nhật", "Thứ 2", "Thứ 3", "Thứ 4", "Thứ 5", "Thứ 6", "Thứ 7" };
        string todayVn = daysOfWeekVn[(int)now.DayOfWeek];

        var icons = IconCategories.TryGetValue(iconCat, out var list) ? list : IconCategories["🎲 Hỗn Hợp"];

        const string chars = "abcdefghjkmnpqrstuvwxyz23456789";
        var randStr = new string(Enumerable.Range(0, rng.Next(4, 7)).Select(_ => chars[rng.Next(chars.Length)]).ToArray());
        var randNum = rng.Next(100, 99999).ToString();

        // Replace dynamic tags
        text = text.Replace("{time}", now.ToString("HH:mm"));
        text = text.Replace("{date}", now.ToString("dd/MM/yyyy"));
        text = text.Replace("{datetime}", now.ToString("dd/MM/yyyy HH:mm"));
        text = text.Replace("{today}", todayVn);
        text = text.Replace("{greeting}", greetings[rng.Next(greetings.Length)]);
        text = text.Replace("{rand_num}", randNum);
        text = text.Replace("{rand_str}", randStr);
        text = text.Replace("{icon}", icons[rng.Next(icons.Count)]);
        text = text.Replace("{dot}", " " + (new[] { ".", "..", "...", "~", "" })[rng.Next(5)]);

        var result = ParseSpintax(text);

        // Micro-Salting & Icons
        if (useSalt && !string.IsNullOrWhiteSpace(result))
        {
            // Insert subtle invisible salt between random words (Word-Joiner / Zero-width)
            var saltChar = InvisibleSalts[rng.Next(InvisibleSalts.Length)];
            if (result.Contains(' '))
            {
                var words = result.Split(' ');
                int insertPos = rng.Next(1, Math.Max(1, words.Length));
                result = string.Join(" ", words.Take(insertPos)) + saltChar + " " + string.Join(" ", words.Skip(insertPos));
            }
            else
            {
                result += saltChar;
            }

            // Append Icons if category is not turned off
            if (!iconCat.Contains("Tắt") && !iconCat.Contains("None") && !iconCat.Contains("Off") && !string.IsNullOrWhiteSpace(iconCat))
            {
                int count = rng.Next(1, 4);
                var picked = Enumerable.Range(0, count).Select(_ => icons[rng.Next(icons.Count)]);
                var iconString = string.Concat(picked);

                string sep = iconNewline ? "\n" : "  ";
                result = result.TrimEnd() + sep + iconString;
            }
        }

        return result;
    }
}
