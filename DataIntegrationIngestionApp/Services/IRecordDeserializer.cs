using System.Collections.Generic;
using DataIntegrationIngestionApp.Models;

namespace DataIntegrationIngestionApp.Services;

/// <summary>
/// Deserializes a raw JSON array payload into a list of strongly-typed records.
/// </summary>
public interface IRecordDeserializer
{
    /// <summary>
    /// Deserializes the JSON array into a list of <see cref="Contact"/> records and validates required fields.
    /// </summary>
    IReadOnlyList<Contact> Deserialize(string json);
}
