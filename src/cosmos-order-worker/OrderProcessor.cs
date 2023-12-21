using Inventory.Infrastructure.Domain;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace cosmos_order_worker
{
    public class OrderProcessor
    {
        private readonly ILogger _logger;

        public OrderProcessor(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<OrderProcessor>();
        }

        [Function("OrderProcessor")]
        public void Run([CosmosDBTrigger(
            databaseName: "%orderDatabase%",
                containerName: "%orderContainer%",
                Connection = "CosmosOrderConnection",
                LeaseContainerName = "leases",
                MaxItemsPerInvocation = 20,
                FeedPollDelay = 1000,
                StartFromBeginning = false,
                CreateLeaseContainerIfNotExists = true)]IReadOnlyList<Order> input
            )
        {
            
        }
    }
}
