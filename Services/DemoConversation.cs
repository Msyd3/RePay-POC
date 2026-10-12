using System.Globalization;
using System.Text.RegularExpressions;
namespace RePay.WhatsAppPoc.Services;

public sealed class DemoState
{
    public string Kind { get; set; } = "";
    public string Stage { get; set; } = "";
    public string? Recipient { get; set; }
    public string? Product { get; set; }
    public string? Summary { get; set; }
    public decimal? Amount { get; set; }
    public decimal? Budget { get; set; }
    public bool BudgetCeiling { get; set; }
}

