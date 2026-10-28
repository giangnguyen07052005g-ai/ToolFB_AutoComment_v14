namespace ToolFB.App.Models;

public class AppConfig
{
    public string FixedComment { get; set; } = string.Empty;
    public string RandomComments { get; set; } = string.Empty;
    public string OrderMode { get; set; } = "Random first"; // "Random first" or "Fixed first"
    public double DelayMin { get; set; } = 30.0;
    public double DelayMax { get; set; } = 60.0;
    public int MaxCommentsPerToken { get; set; } = 50;
    public int MaxCommentsPerPost { get; set; } = 1;
    public int Slots { get; set; } = 3;
    public bool TrueRoundRobin { get; set; } = true;
    public bool AutoWarmup { get; set; } = true;
    public bool SendReactions { get; set; } = false;
    public string ReactionType { get; set; } = "LIKE"; // LIKE, LOVE, CARE, HAHA, WOW
    public bool UseMicroSalt { get; set; } = true;
    public string IconCategory { get; set; } = "🎲 Hỗn Hợp";
    public bool IconNewline { get; set; } = false;
    public int SmartRestAfter { get; set; } = 10;
    public double SmartRestDuration { get; set; } = 60.0;
    public string SelectedImagePath { get; set; } = string.Empty;
    public bool AutoRotate { get; set; } = true;

    // Target posts
    public List<string> TargetUrls { get; set; } = new();

    // Accounts (Encrypted in storage)
    public List<string> RawAccountLines { get; set; } = new();
}
