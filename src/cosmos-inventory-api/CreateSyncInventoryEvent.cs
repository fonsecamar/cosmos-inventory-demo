using Inventory.Infrastructure.Domain;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace cosmos_inventory_api
{
    public class CreateSyncInventoryEvent
    {
        private readonly ILogger _logger;

        public CreateSyncInventoryEvent(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<CreateSyncInventoryEvent>();
        }

        [Function("CreateSyncInventoryEvent")]
        public async Task<HttpResponseData> RunAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "CreateSyncInventoryEvent")] HttpRequestData req,
            [FromBody] InventoryEvent ev,
            [CosmosDBInput(
                databaseName: "%inventoryDatabase%",
                containerName: "%syncInventoryContainer%",
                Connection = "CosmosInventoryConnection")] Container unifiedContainer,
            FunctionContext executionContext)
        {
            var response = req.CreateResponse();
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");

            try
            {
                if (ev == null)
                {
                    response.StatusCode = System.Net.HttpStatusCode.BadRequest;
                    response.WriteString("Invalid request body");
                    return response;
                }

                ev.id = Guid.NewGuid().ToString();
                ev.eventTime = DateTime.UtcNow;

                (HttpStatusCode, string) result = (HttpStatusCode.BadRequest, string.Empty);

                switch (ev.eventType.ToLowerInvariant())
                {
                    case "inventoryupdated":
                        _logger.LogInformation($"Inventory item updated: {ev.eventDetails}");
                        result = await ProcessInventoryUpdatedEventAsync(ev, unifiedContainer);
                        break;
                    case "itemreserved":
                        _logger.LogInformation($"Inventory item reserved: {ev.eventDetails}");
                        result = await ProcessItemReservedEventAsync(ev, unifiedContainer);
                        break;
                    case "ordershipped":
                        _logger.LogInformation($"Inventory item shipped: {ev.eventDetails}");
                        result = await ProcessOrderShippedEventAsync(ev, unifiedContainer);
                        break;
                    case "ordercancelled":
                        _logger.LogInformation($"Inventory item cancelled: {ev.eventDetails}");
                        result = await ProcessOrderCancelledEventAsync(ev, unifiedContainer);
                        break;
                    case "orderreturned":
                        _logger.LogInformation($"Inventory item returned: {ev.eventDetails}");
                        result = await ProcessOrderReturnedEventAsync(ev, unifiedContainer);
                        break;
                }

                response.StatusCode = result.Item1;
                if(result.Item1 != HttpStatusCode.OK)
                    response.WriteString(result.Item2);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing command");
                response.StatusCode = HttpStatusCode.InternalServerError;
                response.WriteString(ex.Message);
            }

            return response;
        }

        public async Task<(HttpStatusCode, string)> ProcessInventoryUpdatedEventAsync(InventoryEvent inventoryEvent, Container unifiedContainer)
        {
            long onHandQuantity = ((InventoryUpdatedEvent)inventoryEvent.eventDetails).onHandQuantity;

            List<PatchOperation> patchOperations = new List<PatchOperation>();
            patchOperations.Add(PatchOperation.Increment("/onHand", onHandQuantity));
            patchOperations.Add(PatchOperation.Increment("/availableToSell", onHandQuantity));
            patchOperations.Add(PatchOperation.Set("/lastUpdated", DateTime.UtcNow));

            try
            {
                var pk = new PartitionKey(inventoryEvent.pk);

                var batch = unifiedContainer.CreateTransactionalBatch(pk);

                batch.PatchItem(inventoryEvent.pk, patchOperations);
                batch.CreateItem(inventoryEvent);

                var response = await batch.ExecuteAsync();

                if (response.IsSuccessStatusCode)
                    return new(HttpStatusCode.OK, string.Empty);
                else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogInformation($"Inventory snapshot not found for item {inventoryEvent.pk}. Creating new snapshot.");

                    var snapshot = new InventoryShapshot()
                    {
                        id = inventoryEvent.pk,
                        pk = inventoryEvent.pk,
                        onHand = onHandQuantity,
                        activeCustomerReservations = 0,
                        availableToSell = onHandQuantity,
                        lastUpdated = DateTime.UtcNow
                    };

                    var newSnapshotBatch = unifiedContainer.CreateTransactionalBatch(pk);
                    newSnapshotBatch.CreateItem<InventoryEvent>(inventoryEvent);
                    newSnapshotBatch.CreateItem<InventoryShapshot>(snapshot);

                    response = await newSnapshotBatch.ExecuteAsync();

                    if (response.IsSuccessStatusCode)
                        return new(HttpStatusCode.OK, string.Empty);
                    else
                        return new(response.StatusCode, response.ErrorMessage);
                }
                else
                    return new(HttpStatusCode.BadRequest, response.ErrorMessage);
            }
            catch (CosmosException ex)
            {
                _logger.LogError($"Error updating inventory snapshot: {ex.Message}");
                throw;
            }
        }

        private async Task<(HttpStatusCode, string)> ProcessOrderCancelledEventAsync(InventoryEvent inventoryEvent, Container unifiedContainer)
        {
            long cancelledQuantity = ((OrderCancelledEvent)inventoryEvent.eventDetails).cancelledQuantity;

            List<PatchOperation> patchOperations = new List<PatchOperation>();
            patchOperations.Add(PatchOperation.Increment("/activeCustomerReservations", -cancelledQuantity));
            patchOperations.Add(PatchOperation.Increment("/availableToSell", cancelledQuantity));
            patchOperations.Add(PatchOperation.Set("/lastUpdated", DateTime.UtcNow));

            var patchOptions = new TransactionalBatchPatchItemRequestOptions()
            {
                FilterPredicate = $"FROM c WHERE c.activeCustomerReservations >= {cancelledQuantity}"
            };

            try
            {
                var pk = new PartitionKey(inventoryEvent.pk);

                var batch = unifiedContainer.CreateTransactionalBatch(pk);

                batch.PatchItem(inventoryEvent.pk, patchOperations, patchOptions);
                batch.CreateItem(inventoryEvent);

                var response = await batch.ExecuteAsync();
                
                if (response.IsSuccessStatusCode)
                    return new (HttpStatusCode.OK, string.Empty);
                else if (response.StatusCode == HttpStatusCode.PreconditionFailed)
                    return new(HttpStatusCode.PreconditionFailed, $"Inventory snapshot not updated for item {inventoryEvent.pk} because of not enough reservations.");
                else
                    return new(response.StatusCode, response.ErrorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating inventory snapshot: {ex.Message}");
                throw;
            }
        }

        private async Task<(HttpStatusCode, string)> ProcessOrderReturnedEventAsync(InventoryEvent inventoryEvent, Container unifiedContainer)
        {
            long returnedQuantity = ((OrderReturnedEvent)inventoryEvent.eventDetails).returnedQuantity;

            List<PatchOperation> patchOperations = new List<PatchOperation>();
            patchOperations.Add(PatchOperation.Increment("/returned", returnedQuantity));
            patchOperations.Add(PatchOperation.Increment("/onHand", returnedQuantity));
            patchOperations.Add(PatchOperation.Set("/lastUpdated", DateTime.UtcNow));

            try
            {
                var pk = new PartitionKey(inventoryEvent.pk);

                var batch = unifiedContainer.CreateTransactionalBatch(pk);

                batch.PatchItem(inventoryEvent.pk, patchOperations);
                batch.CreateItem(inventoryEvent);

                var response = await batch.ExecuteAsync();

                if (response.IsSuccessStatusCode)
                    return new(HttpStatusCode.OK, string.Empty);
                else
                    return new(response.StatusCode, response.ErrorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating inventory snapshot: {ex.Message}");
                throw;
            }
        }

        private async Task<(HttpStatusCode, string)> ProcessOrderShippedEventAsync(InventoryEvent inventoryEvent, Container unifiedContainer)
        {
            long shippedQuantity = ((OrderShippedEvent)inventoryEvent.eventDetails).shippedQuantity;

            List<PatchOperation> patchOperations = new List<PatchOperation>();
            patchOperations.Add(PatchOperation.Increment("/activeCustomerReservations", -shippedQuantity));
            patchOperations.Add(PatchOperation.Increment("/onHand", -shippedQuantity));
            patchOperations.Add(PatchOperation.Set("/lastUpdated", DateTime.UtcNow));

            var patchOptions = new TransactionalBatchPatchItemRequestOptions()
            {
                FilterPredicate = $"FROM c WHERE c.activeCustomerReservations >= {shippedQuantity}"
            };

            try
            {
                var pk = new PartitionKey(inventoryEvent.pk);

                var batch = unifiedContainer.CreateTransactionalBatch(pk);

                batch.PatchItem(inventoryEvent.pk, patchOperations, patchOptions);
                batch.CreateItem(inventoryEvent);

                var response = await batch.ExecuteAsync();

                if (response.IsSuccessStatusCode)
                    return new(HttpStatusCode.OK, string.Empty);
                else if (response.StatusCode == HttpStatusCode.PreconditionFailed)
                    return new(HttpStatusCode.PreconditionFailed, $"Inventory snapshot not updated for item {inventoryEvent.pk} because of not enough reservations.");
                else
                    return new(response.StatusCode, response.ErrorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating inventory snapshot: {ex.Message}");
                throw;
            }
        }

        private async Task<(HttpStatusCode, string)> ProcessItemReservedEventAsync(InventoryEvent inventoryEvent, Container unifiedContainer)
        {
            long reservedQuantity = ((ItemReservedEvent)inventoryEvent.eventDetails).reservedQuantity;

            List<PatchOperation> patchOperations = new List<PatchOperation>();
            patchOperations.Add(PatchOperation.Increment("/activeCustomerReservations", reservedQuantity));
            patchOperations.Add(PatchOperation.Increment("/availableToSell", -reservedQuantity));
            patchOperations.Add(PatchOperation.Set("/lastUpdated", DateTime.UtcNow));

            var patchOptions = new TransactionalBatchPatchItemRequestOptions()
            {
                FilterPredicate = $"FROM c WHERE (c.availableToSell - {reservedQuantity}) >= 0"
            };

            try
            {
                var pk = new PartitionKey(inventoryEvent.pk);

                var batch = unifiedContainer.CreateTransactionalBatch(pk);

                batch.PatchItem(inventoryEvent.pk, patchOperations, patchOptions);
                batch.CreateItem(inventoryEvent);

                var response = await batch.ExecuteAsync();

                if (response.IsSuccessStatusCode)
                    return new(HttpStatusCode.OK, string.Empty);
                else if (response.StatusCode == HttpStatusCode.PreconditionFailed)
                    return new(HttpStatusCode.PreconditionFailed, $"Cannot reserve {reservedQuantity} of item {inventoryEvent.pk}!");
                else
                    return new(response.StatusCode, response.ErrorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating inventory snapshot: {ex.Message}");
                throw;
            }
        }
    }
}
