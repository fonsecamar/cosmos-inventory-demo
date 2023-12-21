using cosmos_inventory_api.Models.Response;
using Inventory.Infrastructure.Domain;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace cosmos_inventory_api
{
    public class CreateAsyncInventoryEvent
    {
        private readonly ILogger _logger;

        public CreateAsyncInventoryEvent(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<CreateAsyncInventoryEvent>();
        }

        private long _lowAvailabilityThreshold = 0;

        [Function("CreateAsyncInventoryEvent")]
        public async Task<CreateAsyncInventoryEventResponse> RunAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "CreateAsyncInventoryEvent")] HttpRequestData req,
            [FromBody] InventoryEvent ev,
            [CosmosDBInput(
                databaseName: "%inventoryDatabase%",
                containerName: "%ledgerContainer%",
                Connection = "CosmosInventoryConnection")] Container ledgerContainer,
            [CosmosDBInput(
                databaseName: "%inventoryDatabase%",
                containerName: "%snapshotContainer%",
                Connection = "CosmosInventoryConnection")] Container snapshotContainer,
            FunctionContext executionContext)
        {
            _lowAvailabilityThreshold = Convert.ToInt64(Environment.GetEnvironmentVariable("lowAvailabilityThreshold"));
            var response = req.CreateResponse();
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");

            try
            {
                if (ev == null) 
                {
                    response.StatusCode = System.Net.HttpStatusCode.BadRequest;
                    response.WriteString("Invalid request body");
                    return new CreateAsyncInventoryEventResponse()
                    {
                        HttpResponse = response
                    };
                }

                ev.id = Guid.NewGuid().ToString();
                ev.eventTime = DateTime.UtcNow;

                if(ev.eventDetails is ItemReservedEvent)
                {
                    var reservedQuantity = ((ItemReservedEvent)ev.eventDetails).reservedQuantity;

                    var snapshot = await snapshotContainer.ReadItemAsync<InventoryShapshot>(ev.pk, new PartitionKey(ev.pk));

                    if (snapshot.Resource.availableToSell < reservedQuantity)
                    {
                        response.StatusCode = System.Net.HttpStatusCode.BadRequest;
                        response.WriteString("Not enough inventory to reserve");
                        return new CreateAsyncInventoryEventResponse()
                        {
                            HttpResponse = response
                        };
                    }
                    else if ((snapshot.Resource.availableToSell - reservedQuantity) <= _lowAvailabilityThreshold)
                    {
                        var query = new QueryDefinition("SELECT SUM(c.eventDetails.reservedQuantity) AS ReservationsInFlight FROM c WHERE c.pk = @pk AND c.eventType = 'ItemReserved' AND c._ts > @ts")
                            .WithParameter("@pk", ev.pk)
                            .WithParameter("@ts", snapshot.Resource.lastEventTs);

                        var queryIterator = ledgerContainer.GetItemQueryIterator<dynamic>(query);

                        var result = await queryIterator.ReadNextAsync();
                        var reservationsInFlight = result.Resource.First()["ReservationsInFlight"];

                        if ((snapshot.Resource.availableToSell - reservationsInFlight?.Value<long>()) < reservedQuantity)
                        {
                            response.StatusCode = System.Net.HttpStatusCode.BadRequest;
                            response.WriteString("Not enough inventory to reserve");
                            return new CreateAsyncInventoryEventResponse()
                            {
                                HttpResponse = response
                            };
                        }
                    }
                }

                return new CreateAsyncInventoryEventResponse()
                {
                    InventoryEvent = ev,
                    HttpResponse = req.CreateResponse(System.Net.HttpStatusCode.OK)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing command");
                {
                    response.StatusCode = System.Net.HttpStatusCode.InternalServerError;
                    response.WriteString(ex.Message);
                    return new CreateAsyncInventoryEventResponse()
                    {
                        HttpResponse = response
                    };
                }
            }
        }
    }
}
