using System.Text.Json.Serialization;

namespace Inventory.Infrastructure.Domain
{
    public class InventoryShapshot
    {
        public required string id { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? pk { get; set; }
        public long onHand { get; set; }
        public long activeCustomerReservations { get; set; }
        public long availableToSell { get; set; }
        public long returned { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public long lastEventTs { get; set; } = 0;
        public string? docType { get; set; } = "InventoryShapshot";
        public DateTime lastUpdated { get; set; }
        public long ttl { get; set; } = -1;
    }
}
