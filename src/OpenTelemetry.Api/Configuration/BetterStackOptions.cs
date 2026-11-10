using System.ComponentModel.DataAnnotations;

public sealed class BetterStackOptions
{
    public const string SectionName = "BetterStack";

    [Required(ErrorMessage = "BetterStack:Endpoint is required.")]
    public required string Endpoint { get; set; }

    [Required(ErrorMessage = "BetterStack:SourceToken is required.")]
    public required string SourceToken { get; set; }
}
