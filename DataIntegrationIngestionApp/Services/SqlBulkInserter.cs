using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using DataIntegrationIngestionApp.Models;
using DataIntegrationIngestionApp.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataIntegrationIngestionApp.Services;

/// <summary>
/// Inserts <see cref="Contact"/> records into a SQL Server table using <see cref="SqlBulkCopy"/>
/// inside a transaction. The connection is configured with the Microsoft.Data.SqlClient
/// built-in transient retry provider (Layer 1) to absorb seconds-level transient failures.
/// </summary>
public sealed class SqlBulkInserter : ISqlBulkInserter
{
    private readonly IngestionOptions _options;
    private readonly ILogger<SqlBulkInserter> _logger;
    private readonly SqlRetryLogicBaseProvider _retryProvider;

    public SqlBulkInserter(IOptions<IngestionOptions> options, ILogger<SqlBulkInserter> logger)
    {
        _options = options.Value;
        _logger = logger;
        _retryProvider = CreateRetryProvider(_options.SqlRetry);
    }

    public async Task BulkInsertAsync(
        string destinationTable,
        IReadOnlyList<Contact> records,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.SqlConnectionString))
        {
            throw new InvalidOperationException(
                "SQL connection string is not configured. Set 'Ingestion:SqlConnectionString' in configuration.");
        }

        if (records.Count == 0)
        {
            _logger.LogInformation("No records to insert into {Table}.", destinationTable);
            return;
        }

        using var table = BuildDataTable(records);

        await using var connection = new SqlConnection(_options.SqlConnectionString)
        {
            RetryLogicProvider = _retryProvider,
        };

        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var transaction = (SqlTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using var bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, transaction)
            {
                DestinationTableName = destinationTable,
                BulkCopyTimeout = 0,
            };

            foreach (DataColumn column in table.Columns)
            {
                bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
            }

            await bulkCopy.WriteToServerAsync(table, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Bulk inserted {Count} record(s) into {Table}.",
                records.Count,
                destinationTable);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private static DataTable BuildDataTable(IReadOnlyList<Contact> records)
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("Surname", typeof(string));
        table.Columns.Add("Age", typeof(int));
        table.Columns.Add("Email", typeof(string));

        foreach (var record in records)
        {
            table.Rows.Add(
                record.Id,
                record.Name,
                record.Surname,
                (object?)record.Age ?? DBNull.Value,
                (object?)record.Email ?? DBNull.Value);
        }

        return table;
    }

    private static SqlRetryLogicBaseProvider CreateRetryProvider(SqlTransientRetryOptions retry)
    {
        var retryOptions = new SqlRetryLogicOption
        {
            NumberOfTries = retry.NumberOfTries,
            DeltaTime = TimeSpan.FromSeconds(retry.DeltaTimeSeconds),
            MaxTimeInterval = TimeSpan.FromSeconds(retry.MaxTimeIntervalSeconds),
        };

        return SqlConfigurableRetryFactory.CreateExponentialRetryProvider(retryOptions);
    }
}
