using System.ComponentModel.DataAnnotations;

public sealed class BetterStackOptions
{
    public const string SectionName = "BetterStack";

    [Required]
    public required string Endpoint { get; set; }

    [Required]
    public required string SourceToken { get; set; }
}
