using System.Net;
using Inventory.Infrastructure.Domain;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace cosmos_inventory_api
{
    public class GetSyncSnapshot
    {
        private readonly ILogger _logger;

        public GetSyncSnapshot(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<GetSyncSnapshot>();
        }

        /// <summary>
        /// Retrieve a snapshot from the Cosmos DB container (point read)
        /// </summary>
        [Function("GetSyncSnapshot")]
        public HttpResponseData Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "GetSyncSnapshot/{id}")] HttpRequestData req,
            [CosmosDBInput(
                databaseName: "%inventoryDatabase%",
                containerName: "%syncInventoryContainer%",
                Id = "{id}",
                PartitionKey = "{id}",
                Connection = "CosmosInventoryConnection")] InventoryShapshot shapshot
            )
        {
            var response = req.CreateResponse();
            response.StatusCode = shapshot == null ? HttpStatusCode.NotFound : HttpStatusCode.OK;
            response.WriteAsJsonAsync(shapshot);

            return response;
        }
    }
}
