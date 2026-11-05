/// <summary>Only the fields this project needs from a Nominatim match; Lat and Lon are JSON strings.</summary>
public record GeocodingMatch(string Lat, string Lon);
