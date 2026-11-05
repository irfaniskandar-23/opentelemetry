using System.ComponentModel.DataAnnotations;

public sealed class GeocodingOptions
{
    public const string SectionName = "Geocoding";

    [Required(ErrorMessage = "Set Geocoding:BaseUrl in appsettings.json.")]
    public required string BaseUrl { get; set; }

    [Required(ErrorMessage = "Set Geocoding:UserAgent in appsettings.json.")]
    public required string UserAgent { get; set; }
}
