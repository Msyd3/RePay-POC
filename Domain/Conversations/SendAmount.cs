using System.Globalization;
using System.Text.RegularExpressions;
namespace RePay.WhatsAppPoc.Services;

public static class SendAmount
{
    private static readonly Dictionary<string, decimal> Words = new(StringComparer.OrdinalIgnoreCase)
    {
        ["صفر"]=0,["واحد"]=1,["واحدة"]=1,["واحده"]=1,["اثنين"]=2,["اثنان"]=2,["ثلاثة"]=3,["ثلاثه"]=3,["اربعة"]=4,["اربعه"]=4,["خمسة"]=5,["خمسه"]=5,["ستة"]=6,["سته"]=6,["سبعة"]=7,["سبعه"]=7,["ثمانية"]=8,["ثمانيه"]=8,["تسعة"]=9,["تسعه"]=9,["عشرة"]=10,["عشره"]=10,["عشر"]=10,["احد"]=1,["احدى"]=1,["اثنا"]=2,["اثني"]=2,
        ["عشرين"]=20,["عشرون"]=20,["ثلاثين"]=30,["ثلاثون"]=30,["اربعين"]=40,["اربعون"]=40,["خمسين"]=50,["خمسون"]=50,["ستين"]=60,["ستون"]=60,["سبعين"]=70,["سبعون"]=70,["ثمانين"]=80,["ثمانون"]=80,["تسعين"]=90,["تسعون"]=90,
        ["مية"]=100,["ميه"]=100,["مئة"]=100,["مائة"]=100,["ميتين"]=200,["مئتين"]=200,["مائتين"]=200,["ثلاثمية"]=300,["اربعمية"]=400,["خمسمية"]=500,["ستمية"]=600,["سبعمية"]=700,["ثمانمية"]=800,["تسعمية"]=900,["الف"]=1000,["الاف"]=1000,["الفين"]=2000,["مليون"]=1000000,["نص"]=0.5m,["نصف"]=0.5m,
        ["zero"]=0,["one"]=1,["two"]=2,["three"]=3,["four"]=4,["five"]=5,["six"]=6,["seven"]=7,["eight"]=8,["nine"]=9,["ten"]=10,["eleven"]=11,["twelve"]=12,["thirteen"]=13,["fourteen"]=14,["fifteen"]=15,["sixteen"]=16,["seventeen"]=17,["eighteen"]=18,["nineteen"]=19,["twenty"]=20,["thirty"]=30,["forty"]=40,["fifty"]=50,["sixty"]=60,["seventy"]=70,["eighty"]=80,["ninety"]=90,["hundred"]=100,["thousand"]=1000,["million"]=1000000,["half"]=0.5m
    };
    public static bool TryExtract(string input, bool allowBare, out decimal amount, out string remainder)
    {
        var text = ConversationLifecycle.EnglishDigits(NameConversation.Normalize(input));
        amount = 0; remainder = text;
        var digits = Regex.Match(text, @"(?<![\d.])(-?\d+(?:[.٫]\d+)?)\s*(?:ريال(?: سعودي)?|SAR|riyals?)\b", RegexOptions.IgnoreCase);
        if (!digits.Success && allowBare) digits = Regex.Match(text, @"(?<![\d.])(-?\d+(?:[.٫]\d+)?)(?![\d.])");
        if (digits.Success && decimal.TryParse(digits.Groups[1].Value.Replace('٫','.'), CultureInfo.InvariantCulture, out amount))
        { remainder = text.Remove(digits.Index, digits.Length); return true; }
        if (Regex.IsMatch(text, @"^ريال(?: واحد)?$")) { amount = 1; remainder = ""; return true; }
        if (Regex.IsMatch(text, @"^ريالين$")) { amount = 2; remainder = ""; return true; }
        var vocabulary = string.Join("|", Words.Keys.OrderByDescending(w => w.Length).Select(Regex.Escape));
        var word = @"(?:و?(?:" + vocabulary + @"))";
        var matches = Regex.Matches(text, @"(?<!\p{L})" + word + @"(?:(?:\s+(?:and|و)\s+|\s+|-|\s*و)" + word + @")*(?!\p{L})", RegexOptions.IgnoreCase);
        foreach (Match match in matches)
        {
            var suffix = Regex.Match(text[(match.Index + match.Length)..], @"^\s*(?:ريال(?: سعودي)?|SAR|riyals?)\b", RegexOptions.IgnoreCase);
            if (!allowBare && !suffix.Success) continue;
            decimal group = 0, total = 0;
            foreach (var token in Regex.Split(match.Value, @"\s+|-"))
            {
                var key = token;
                if (key.Equals("and", StringComparison.OrdinalIgnoreCase) || key == "و") continue;
                if (!Words.ContainsKey(key) && key.StartsWith('و')) key = key[1..];
                if (!Words.TryGetValue(key, out var value)) return false;
                if (value is 1000 or 1000000) { total += (group == 0 ? 1 : group) * value; group = 0; }
                else if (value == 100 && group is > 0 and < 10) group *= 100;
                else group += value;
            }
            amount = total + group;
            if (Regex.IsMatch(text[..match.Index], @"(?:سالب|ناقص|minus|negative)\s*$", RegexOptions.IgnoreCase)) amount = -amount;
            remainder = text.Remove(match.Index, match.Length + suffix.Length);
            return true;
        }
        return false;
    }
    public static bool Valid(decimal amount) => amount >= 1 && amount <= 9999999 && decimal.Round(amount, 2) == amount;
}
