using System.ComponentModel.DataAnnotations;

public sealed class GeocodingOptions
{
    public const string SectionName = "Geocoding";

    [Required]
    public required string BaseUrl { get; set; }

    [Required]
    public required string UserAgent { get; set; }
}
