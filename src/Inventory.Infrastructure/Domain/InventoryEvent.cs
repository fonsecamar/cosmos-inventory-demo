using System.Text.Json;
using System.Text.Json.Serialization;

namespace Inventory.Infrastructure.Domain
{
    [JsonConverter(typeof(InventoryEventConverter))]
    public class InventoryEvent
    {
        public string id { get; set; }
        public string pk { get; set; }
        public string eventType { get; set; }
        public EventDetails eventDetails { get; set; }
        public DateTime eventTime { get; set; }
        public long _ts { get; set; }
    }

    [JsonDerivedType(typeof(InventoryUpdatedEvent))]
    [JsonDerivedType(typeof(ItemReservedEvent))]
    [JsonDerivedType(typeof(OrderCancelledEvent))]
    [JsonDerivedType(typeof(OrderShippedEvent))]
    public abstract class EventDetails
    {
        public string productId { get; set; }
        public string nodeId { get; set; }
    }

    public class InventoryUpdatedEvent : EventDetails
    {
        public required long onHandQuantity { get; set; }
    }

    public class ItemReservedEvent : EventDetails
    {
        public required long reservedQuantity { get; set; }
    }

    public class OrderCancelledEvent : EventDetails
    {
        public required long cancelledQuantity { get; set; }
    }

    public class OrderShippedEvent : EventDetails
    {
        public required long shippedQuantity { get; set; }
    }

    public class InventoryEventConverter : JsonConverter<InventoryEvent>
    {
        public override InventoryEvent? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var token = JsonDocument.ParseValue(ref reader).RootElement;
            var eventType = token.GetProperty("eventType").GetString();

            EventDetails eventDetails = null;
            switch (eventType.ToLowerInvariant())
            {
                case "inventoryupdated":
                    eventDetails = JsonSerializer.Deserialize<InventoryUpdatedEvent>(token.GetProperty("eventDetails").GetRawText());
                    break;
                case "itemreserved":
                    eventDetails = JsonSerializer.Deserialize<ItemReservedEvent>(token.GetProperty("eventDetails").GetRawText());
                    break;
                case "ordershipped":
                    eventDetails = JsonSerializer.Deserialize<OrderShippedEvent>(token.GetProperty("eventDetails").GetRawText());
                    break;
                case "ordercancelled":
                    eventDetails = JsonSerializer.Deserialize<OrderCancelledEvent>(token.GetProperty("eventDetails").GetRawText());
                    break;
                default:
                    throw new Exception($"Unknown eventType: {eventType}");
            }

            JsonElement id;
            JsonElement _ts;

            var inventoryEvent = new InventoryEvent 
            { 
                id = token.TryGetProperty("id", out id) ? id.GetString() : null,
                pk = token.GetProperty("pk").GetString(),
                eventType = eventType,
                eventDetails = eventDetails,
                _ts = token.TryGetProperty("_ts", out _ts) ? _ts.GetInt64() : 0,
            };
            return inventoryEvent;
        }

        public override void Write(Utf8JsonWriter writer, InventoryEvent value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            
            writer.WriteString("id", value.id);
            writer.WriteString("pk", value.pk); 
            writer.WriteString("eventType", value.eventType);
            writer.WritePropertyName("eventDetails");
            JsonSerializer.Serialize(writer, value.eventDetails, options);
            writer.WriteString("eventTime", value.eventTime.ToString("o"));

            writer.WriteEndObject();
        }
    }
}
