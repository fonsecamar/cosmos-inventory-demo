using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace cosmos_order_api
{
    public class CreateSyncOrder
    {
        private readonly ILogger _logger;

        public CreateSyncOrder(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<CreateSyncOrder>();
        }

        [Function("CreateSyncOrder")]
        public HttpResponseData Run([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req)
        {
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "text/plain; charset=utf-8");

            return response;
        }
    }
}
