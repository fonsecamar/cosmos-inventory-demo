using Inventory.Infrastructure.Domain;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace cosmos_inventory_api.Models.Response
{
    public class CreateAsyncInventoryEventResponse
    {
        [CosmosDBOutput(databaseName: "%inventoryDatabase%", containerName: "%ledgerContainer%", Connection = "CosmosInventoryConnection", PartitionKey = "pk")]
        public InventoryEvent? InventoryEvent { get; set; }

        public required HttpResponseData HttpResponse { get; set; }
    }
}
