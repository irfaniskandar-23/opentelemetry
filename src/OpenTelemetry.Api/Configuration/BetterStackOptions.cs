using System.ComponentModel.DataAnnotations;

public sealed class BetterStackOptions
{
    public const string SectionName = "BetterStack";

    [Required(ErrorMessage = $"{SectionName}:{nameof(Endpoint)} is required.")]
    public required string Endpoint { get; set; }

    [Required(ErrorMessage = $"{SectionName}:{nameof(SourceToken)} is required.")]
    public required string SourceToken { get; set; }
}
