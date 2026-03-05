using Azure.Data.Tables;
using AzureAIProxy.Shared.TableStorage;

namespace AzureAIProxy.Shared.Services;

public interface ITableStorageService
{
    TableClient GetTableClient(string tableName);
}

public class TableStorageService : ITableStorageService
{
    private readonly TableServiceClient _serviceClient;

    public TableStorageService(TableServiceClient serviceClient)
    {
        _serviceClient = serviceClient;
        EnsureTablesCreated();
    }

    public TableClient GetTableClient(string tableName) =>
        _serviceClient.GetTableClient(tableName);

    private void EnsureTablesCreated()
    {
        string[] tables =
        [
            TableNames.Events,
            TableNames.Attendees,
            TableNames.AttendeeLookup,
            TableNames.AttendeeRequests,
            TableNames.Metrics,
            TableNames.Owners,
            TableNames.Catalogs,
            TableNames.OwnerEvents,
            TableNames.Assistants
        ];

        foreach (var table in tables)
        {
            _serviceClient.CreateTableIfNotExists(table);
        }
    }
}
