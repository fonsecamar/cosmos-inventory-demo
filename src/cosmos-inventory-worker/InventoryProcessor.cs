using Azure;
using Inventory.Infrastructure.Domain;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace cosmos_inventory_worker
{
    public class InventoryProcessor
    {
        private readonly ILogger _logger;

        public InventoryProcessor(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<InventoryProcessor>();
        }

        [Function("InventoryProcessor")]
        public async Task RunAsync([CosmosDBTrigger(
                databaseName: "%inventoryDatabase%",
                containerName: "%ledgerContainer%",
                Connection = "CosmosInventoryConnection",
                LeaseContainerName = "leases",
                MaxItemsPerInvocation = 20,
                FeedPollDelay = 1000,
                StartFromBeginning = false,
                CreateLeaseContainerIfNotExists = true)]IReadOnlyList<InventoryEvent> input,
            [CosmosDBInput(
                databaseName: "%inventoryDatabase%",
                containerName: "%snapshotContainer%",
                Connection = "CosmosInventoryConnection")] Container snapshotContainer,
             FunctionContext executionContext)
        {
            foreach (var ev in input)
            {
                switch (ev.eventType.ToLowerInvariant())
                {
                    case "inventoryupdated":
                        _logger.LogInformation($"Inventory item updated: {ev.eventDetails}");
                        await ProcessInventoryUpdatedEventAsync(ev, snapshotContainer);
                        break;
                    case "itemreserved":
                        _logger.LogInformation($"Inventory item reserved: {ev.eventDetails}");
                        await ProcessItemReservedEventAsync(ev, snapshotContainer);
                        break;
                    case "ordershipped":
                        _logger.LogInformation($"Inventory item shipped: {ev.eventDetails}");
                        await ProcessOrderShippedEventAsync(ev, snapshotContainer);
                        break;
                    case "ordercancelled":
                        _logger.LogInformation($"Inventory item cancelled: {ev.eventDetails}");
                        await ProcessOrderCancelledEventAsync(ev, snapshotContainer);
                        break;
                    case "orderreturned":
                        _logger.LogInformation($"Inventory item returned: {ev.eventDetails}");
                        await ProcessOrderReturnedEventAsync(ev, snapshotContainer);
                        break;
                    default:
                        break;
                }
            }            
        }

        public async Task ProcessInventoryUpdatedEventAsync(InventoryEvent inventoryEvent, Container snapshotContainer)
        {
            long onHandQuantity = ((InventoryUpdatedEvent)inventoryEvent.eventDetails).onHandQuantity;

            List<PatchOperation> patchOperations = new List<PatchOperation>();
            patchOperations.Add(PatchOperation.Increment("/onHand", onHandQuantity));
            patchOperations.Add(PatchOperation.Increment("/availableToSell", onHandQuantity));
            patchOperations.Add(PatchOperation.Set("/lastEventTs", inventoryEvent._ts));
            patchOperations.Add(PatchOperation.Set("/lastUpdated", DateTime.UtcNow));

            var patchOptions = new PatchItemRequestOptions()
            {
                FilterPredicate = $"FROM c WHERE c.lastEventTs < {inventoryEvent._ts}"
            };

            try
            {
                var response = await snapshotContainer.PatchItemStreamAsync(inventoryEvent.pk, new PartitionKey(inventoryEvent.pk), patchOperations, patchOptions);

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogInformation($"Inventory snapshot not found for item {inventoryEvent.pk}. Creating new snapshot.");

                    var snapshot = new InventoryShapshot()
                    {
                        id = inventoryEvent.pk,
                        onHand = onHandQuantity,
                        activeCustomerReservations = 0,
                        availableToSell = onHandQuantity,
                        lastEventTs = inventoryEvent._ts,
                        lastUpdated = DateTime.UtcNow
                    };

                    await snapshotContainer.CreateItemAsync<InventoryShapshot>(snapshot, new PartitionKey(snapshot.id));
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
                {
                    //Enqueue event to analyze. Ignore due to same event being processed twice.
                    _logger.LogError($"Inventory snapshot not updated for item {inventoryEvent.pk} because duplicated processing. Event: {inventoryEvent}");
                }

            }
            catch (CosmosException ex)
            {
                _logger.LogError($"Error updating inventory snapshot: {ex.Message}");
                throw;
            }
        }

        private async Task ProcessOrderCancelledEventAsync(InventoryEvent inventoryEvent, Container snapshotContainer)
        {
            long cancelledQuantity = ((OrderCancelledEvent)inventoryEvent.eventDetails).cancelledQuantity;

            List<PatchOperation> patchOperations = new List<PatchOperation>();
            patchOperations.Add(PatchOperation.Increment("/activeCustomerReservations", -cancelledQuantity));
            patchOperations.Add(PatchOperation.Increment("/availableToSell", cancelledQuantity));
            patchOperations.Add(PatchOperation.Set("/lastEventTs", inventoryEvent._ts));
            patchOperations.Add(PatchOperation.Set("/lastUpdated", DateTime.UtcNow));

            var patchOptions = new PatchItemRequestOptions()
            {
                FilterPredicate = $"FROM c WHERE c.activeCustomerReservations >= {cancelledQuantity} AND c.lastEventTs < {inventoryEvent._ts}"
            };

            try
            {
                var response = await snapshotContainer.PatchItemStreamAsync(inventoryEvent.pk, new PartitionKey(inventoryEvent.pk), patchOperations, patchOptions);

                if (response.StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
                {
                    //Enqueue event to analyze. Handle not enough reservations or ignore due to same event being processed twice.
                    _logger.LogError($"Inventory snapshot not updated for item {inventoryEvent.pk} because of not enough reservations or duplicated processing. Event: {inventoryEvent}");
                }
            }
            catch (Exception ex)
            {

                _logger.LogError($"Error updating inventory snapshot: {ex.Message}");
                throw;
            }
        }

        private async Task ProcessOrderReturnedEventAsync(InventoryEvent inventoryEvent, Container snapshotContainer)
        {
            long returnedQuantity = ((OrderReturnedEvent)inventoryEvent.eventDetails).returnedQuantity;

            List<PatchOperation> patchOperations = new List<PatchOperation>();
            patchOperations.Add(PatchOperation.Increment("/returned", returnedQuantity));
            patchOperations.Add(PatchOperation.Increment("/onHand", returnedQuantity));
            patchOperations.Add(PatchOperation.Set("/lastEventTs", inventoryEvent._ts));
            patchOperations.Add(PatchOperation.Set("/lastUpdated", DateTime.UtcNow));

            var patchOptions = new PatchItemRequestOptions()
            {
                FilterPredicate = $"FROM c WHERE c.lastEventTs < {inventoryEvent._ts}"
            };

            try
            {
                var response = await snapshotContainer.PatchItemStreamAsync(inventoryEvent.pk, new PartitionKey(inventoryEvent.pk), patchOperations, patchOptions);

                if (response.StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
                {
                    //Enqueue event to analyze. Ignore due to same event being processed twice.
                    _logger.LogError($"Inventory snapshot not updated for item {inventoryEvent.pk} because duplicated processing. Event: {inventoryEvent}");
                }
            }
            catch (Exception ex)
            {

                _logger.LogError($"Error updating inventory snapshot: {ex.Message}");
                throw;
            }
        }

        private async Task ProcessOrderShippedEventAsync(InventoryEvent inventoryEvent, Container snapshotContainer)
        {
            long shippedQuantity = ((OrderShippedEvent)inventoryEvent.eventDetails).shippedQuantity;

            List<PatchOperation> patchOperations = new List<PatchOperation>();
            patchOperations.Add(PatchOperation.Increment("/activeCustomerReservations", -shippedQuantity));
            patchOperations.Add(PatchOperation.Increment("/onHand", -shippedQuantity));
            patchOperations.Add(PatchOperation.Set("/lastEventTs", inventoryEvent._ts));
            patchOperations.Add(PatchOperation.Set("/lastUpdated", DateTime.UtcNow));

            var patchOptions = new PatchItemRequestOptions()
            {
                FilterPredicate = $"FROM c WHERE c.activeCustomerReservations >= {shippedQuantity} AND c.lastEventTs < {inventoryEvent._ts}"
            };

            try
            {
                var response = await snapshotContainer.PatchItemStreamAsync(inventoryEvent.pk, new PartitionKey(inventoryEvent.pk), patchOperations, patchOptions);

                if (response.StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
                {
                    //Enqueue event to analyze. Handle not enough reservations or ignore due to same event being processed twice.
                    _logger.LogError($"Inventory snapshot not updated for item {inventoryEvent.pk} because of not enough reservations or duplicated processing. Event: {inventoryEvent}");
                }
            }
            catch (Exception ex)
            {

                _logger.LogError($"Error updating inventory snapshot: {ex.Message}");
                throw;
            }
        }

        private async Task ProcessItemReservedEventAsync(InventoryEvent inventoryEvent, Container snapshotContainer)
        {
            long reservedQuantity = ((ItemReservedEvent)inventoryEvent.eventDetails).reservedQuantity;

            List<PatchOperation> patchOperations = new List<PatchOperation>();
            patchOperations.Add(PatchOperation.Increment("/activeCustomerReservations", reservedQuantity));
            patchOperations.Add(PatchOperation.Increment("/availableToSell", -reservedQuantity));
            patchOperations.Add(PatchOperation.Set("/lastEventTs", inventoryEvent._ts));
            patchOperations.Add(PatchOperation.Set("/lastUpdated", DateTime.UtcNow));

            var patchOptions = new PatchItemRequestOptions()
            {
                FilterPredicate = $"FROM c WHERE (c.availableToSell - {reservedQuantity}) >= 0 AND c.lastEventTs < {inventoryEvent._ts}"
            };

            try
            {
                var response = await snapshotContainer.PatchItemStreamAsync(inventoryEvent.pk, new PartitionKey(inventoryEvent.pk), patchOperations, patchOptions);

                if (response.StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
                {
                    //Enqueue event to analyze. Handle overselling or ignore due to same event being processed twice.
                    _logger.LogError($"Inventory snapshot not updated for item {inventoryEvent.pk} because of overselling or duplicated processing. Event: {inventoryEvent}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating inventory snapshot: {ex.Message}");
                throw;
            }
        }
    }
}