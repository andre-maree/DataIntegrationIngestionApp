using System.Text.Json.Serialization;

namespace DataIntegrationIngestionApp.Models;

/// <summary>
/// Represents a single record for the <c>dbo.Contacts</c> table.
/// </summary>
public sealed class Contact
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("surname")]
    public string Surname { get; set; } = string.Empty;

    [JsonPropertyName("age")]
    public int? Age { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }
}
