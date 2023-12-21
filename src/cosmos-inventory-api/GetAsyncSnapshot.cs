using System.Net;
using Inventory.Infrastructure.Domain;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace cosmos_inventory_api
{
    public class GetAsyncSnapshot
    {
        private readonly ILogger _logger;

        public GetAsyncSnapshot(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<GetAsyncSnapshot>();
        }

        [Function("GetAsyncSnapshot")]
        public HttpResponseData Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "GetAsyncSnapshot/{id}")] HttpRequestData req,
            [CosmosDBInput(
                databaseName: "%inventoryDatabase%",
                containerName: "%snapshotContainer%",
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
