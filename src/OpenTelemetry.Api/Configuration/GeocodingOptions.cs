using System.ComponentModel.DataAnnotations;

public sealed class GeocodingOptions
{
    public const string SectionName = "Geocoding";

    [Required(ErrorMessage = "Geocoding:BaseUrl is required.")]
    public required string BaseUrl { get; set; }

    [Required(ErrorMessage = "Geocoding:UserAgent is required.")]
    public required string UserAgent { get; set; }
}
