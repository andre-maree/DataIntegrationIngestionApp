using System;
using System.Collections.Generic;
using System.Text.Json;
using DataIntegrationIngestionApp.Models;

namespace DataIntegrationIngestionApp.Services;

/// <summary>
/// Deserializes a JSON array of <see cref="Contact"/> records with case-insensitive
/// property matching and validates required, non-nullable fields.
/// </summary>
public sealed class RecordDeserializer : IRecordDeserializer
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public IReadOnlyList<Contact> Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("Payload is empty; expected a JSON array of records.");
        }

        List<Contact>? contacts;
        try
        {
            contacts = JsonSerializer.Deserialize<List<Contact>>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Payload is not a valid JSON array of records.", ex);
        }

        if (contacts is null)
        {
            throw new InvalidOperationException("Payload deserialized to null; expected a JSON array of records.");
        }

        ValidateRequiredFields(contacts);
        return contacts;
    }

    private static void ValidateRequiredFields(IReadOnlyList<Contact> contacts)
    {
        for (var i = 0; i < contacts.Count; i++)
        {
            var contact = contacts[i];

            if (string.IsNullOrWhiteSpace(contact.Name))
            {
                throw new InvalidOperationException($"Record at index {i} is missing the required 'Name' field.");
            }

            if (string.IsNullOrWhiteSpace(contact.Surname))
            {
                throw new InvalidOperationException($"Record at index {i} is missing the required 'Surname' field.");
            }
        }
    }
}
