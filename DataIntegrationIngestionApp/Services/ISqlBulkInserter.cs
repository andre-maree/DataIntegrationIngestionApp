using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataIntegrationIngestionApp.Models;

namespace DataIntegrationIngestionApp.Services;

/// <summary>
/// Performs a bulk insert of records into a destination SQL Server table.
/// </summary>
public interface ISqlBulkInserter
{
    /// <summary>
    /// Bulk-inserts the supplied records into <paramref name="destinationTable"/>.
    /// </summary>
    /// <param name="destinationTable">Schema-qualified destination table (e.g. <c>dbo.Contacts</c>).</param>
    /// <param name="records">The records to insert.</param>
    Task BulkInsertAsync(
        string destinationTable,
        IReadOnlyList<Contact> records,
        CancellationToken cancellationToken = default);
}
