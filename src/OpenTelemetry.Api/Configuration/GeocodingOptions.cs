using System.ComponentModel.DataAnnotations;

public sealed class GeocodingOptions
{
    public const string SectionName = "Geocoding";

    [Required(ErrorMessage = $"{SectionName}:{nameof(BaseUrl)} is required.")]
    public required string BaseUrl { get; set; }

    [Required(ErrorMessage = $"{SectionName}:{nameof(UserAgent)} is required.")]
    public required string UserAgent { get; set; }
}
