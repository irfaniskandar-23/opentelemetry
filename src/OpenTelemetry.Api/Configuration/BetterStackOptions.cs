using System.ComponentModel.DataAnnotations;

public sealed class BetterStackOptions
{
    public const string SectionName = "BetterStack";

    [Required(ErrorMessage = "Set BetterStack:Endpoint in appsettings.json.")]
    public required string Endpoint { get; set; }

    [Required(ErrorMessage = "Set BetterStack:SourceToken with dotnet user-secrets or an app setting.")]
    public required string SourceToken { get; set; }
}
