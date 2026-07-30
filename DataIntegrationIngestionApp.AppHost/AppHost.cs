var builder = DistributedApplication.CreateBuilder(args);

// Ensure the Azure Functions Core Tools ('func') are discoverable even when this process
// inherited a stale PATH (e.g. Core Tools were installed after the IDE was started).
EnsureFuncOnPath();

// Azure Service Bus, run locally as an emulator for development.
// The resource is named "ServiceBusConnection" so the Functions Service Bus trigger
// (Connection = "ServiceBusConnection") resolves the injected connection string.
var serviceBus = builder.AddAzureServiceBus("ServiceBusConnection")
    .RunAsEmulator();

// Session-enabled queue so batches are processed strictly FIFO per SQL table (SessionId).
serviceBus.AddServiceBusQueue("ingestion-queue")
    .WithProperties(queue =>
    {
        queue.RequiresSession = true;
        queue.MaxDeliveryCount = 10;
    });

// Target SQL Server for the ingestion bulk insert, run as a local container.
// DemoDatabase holds the dbo.Contacts table created by the init script so the whole
// execution path (queue message -> bulk insert) can be tested without any external SQL.
var demoDatabase = builder.AddSqlServer("sql")
    .WithLifetime(ContainerLifetime.Persistent)
    .AddDatabase("DemoDatabase")
    .WithCreationScript(File.ReadAllText(
        Path.Combine(builder.AppHostDirectory, "sql", "init-contacts.sql")));

builder.AddAzureFunctionsProject<Projects.DataIntegrationIngestionApp>("ingestion-functions")
    .WithReference(serviceBus)
    .WithReference(demoDatabase)
    // Surface the Aspire-managed connection string under the config key the app binds to
    // (Ingestion:SqlConnectionString) so SqlBulkInserter targets DemoDatabase.
    .WithEnvironment("Ingestion__SqlConnectionString", demoDatabase.Resource.ConnectionStringExpression)
    .WaitFor(serviceBus)
    .WaitFor(demoDatabase);

builder.Build().Run();

static void EnsureFuncOnPath()
{
    // Already resolvable on the current PATH? Nothing to do.
    if (IsFuncResolvable())
    {
        return;
    }

    // Pull the latest machine/user PATH (updated by the Core Tools installer) so we pick up
    // the install directory without requiring the IDE/terminal to be restarted.
    var machinePath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine);
    var userPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User);
    var candidateDirs = new List<string>();

    foreach (var value in new[] { machinePath, userPath })
    {
        if (!string.IsNullOrEmpty(value))
        {
            candidateDirs.AddRange(value.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        }
    }

    // Well-known default install location as a fallback.
    candidateDirs.Add(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "Microsoft", "Azure Functions Core Tools"));

    var funcDir = candidateDirs.FirstOrDefault(dir =>
        File.Exists(Path.Combine(dir, "func.exe")) || File.Exists(Path.Combine(dir, "func")));

    if (funcDir is null)
    {
        return;
    }

    var currentPath = Environment.GetEnvironmentVariable("Path") ?? string.Empty;
    Environment.SetEnvironmentVariable("Path", $"{funcDir}{Path.PathSeparator}{currentPath}");
}

static bool IsFuncResolvable()
{
    var path = Environment.GetEnvironmentVariable("Path") ?? string.Empty;
    foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
    {
        if (File.Exists(Path.Combine(dir, "func.exe")) || File.Exists(Path.Combine(dir, "func")))
        {
            return true;
        }
    }

    return false;
}
