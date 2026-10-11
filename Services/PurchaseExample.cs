using System.Globalization;
using System.Text.RegularExpressions;

namespace RePay.WhatsAppPoc.Services;

public static class PurchaseExample
{
    // Synthetic options for the POC, never live prices or stock claims.
    public static decimal[] Prices(string product)
    {
        var text = NameConversation.Normalize(product).ToLowerInvariant();
        if (Regex.IsMatch(text, @"جوال|هاتف|ايفون|iphone|سامسونج")) return [1800m, 2000m, 2200m];
        if (Regex.IsMatch(text, @"لابتوب|حاسب|كمبيوتر|laptop")) return [2700m, 3000m, 3300m];
        if (Regex.IsMatch(text, @"سماعة|سماعات|headphone")) return [180m, 200m, 220m];
        if (Regex.IsMatch(text, @"حذاء|جزمة|shoe")) return [270m, 300m, 330m];
        return [90m, 100m, 110m];
    }

    public static bool TryBudget(string input, out decimal budget, out bool ceiling, out bool onlyBudget)
    {
        budget = 0; ceiling = false; onlyBudget = false;
        var text = ConversationLifecycle.EnglishDigits(NameConversation.Normalize(input)).Replace('٫', '.').Replace("٬", "").Replace(",", "");
        var match = Regex.Match(text, @"(?<prefix>بميزانية|بميزانيه|ميزانيتي|الميزانية|الميزانيه|ميزانية|ميزانيه|بمبلغ|بحدود|حوالي|حدي|حد اقصى|ما يتجاوز|ما يزيد عن|بسعر|بـ?)\s*[:=]?\s*(?<amount>\d{1,7}(?:\.\d{1,2})?)(?![\d.])\s*(?:ريال(?: سعودي)?|ر\.س|SAR)?", RegexOptions.IgnoreCase);
        if (!match.Success) match = Regex.Match(text, @"^\s*(?<amount>\d{1,7}(?:\.\d{1,2})?)\s*(?:ريال(?: سعودي)?|ر\.س|SAR)?\s*$", RegexOptions.IgnoreCase);
        if (!match.Success || !decimal.TryParse(match.Groups["amount"].Value, CultureInfo.InvariantCulture, out budget) || budget <= 0) return false;
        ceiling = Regex.IsMatch(match.Groups["prefix"].Value, @"حد اقصى|ما يتجاوز|ما يزيد عن");
        var remaining = text.Remove(match.Index, match.Length).Trim();
        onlyBudget = remaining.Length == 0 || Regex.IsMatch(remaining, @"^(?:ابي|ابغى|اريد|خله|خليها|غير|عدل|الميزانية|المبلغ|السعر|ابي يكون|ابي شي)\s*(?:الى)?$");
        return true;
    }

    public static decimal? Select(string product, decimal? budget, bool ceiling)
    {
        var options = Prices(product);
        if (budget is null) return options[1];
        var eligible = options.Where(p => (!ceiling || p <= budget.Value) && Math.Abs(p - budget.Value) <= budget.Value * 0.20m);
        return eligible.OrderBy(p => Math.Abs(p - budget.Value)).ThenBy(p => p).Select(p => (decimal?)p).FirstOrDefault();
    }
}
