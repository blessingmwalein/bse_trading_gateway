using System.ComponentModel.DataAnnotations;

namespace FixGateway.Models
{
    public class OrderRequest
    {
        [Required]
        public string Symbol { get; set; } = string.Empty;

        [Required]
        public decimal Price { get; set; }

        [Required]
        public decimal Quantity { get; set; }

        [Required]
        public string Side { get; set; } = "1"; // 1=Buy, 2=Sell

        public string OrderType { get; set; } = "2"; // 2=Limit

        public string? Account { get; set; }
        
        public string OrderBook { get; set; } = "1"; // 1=Regular
    }

    public class OrderResponse
    {
        public string ClOrdID { get; set; } = string.Empty;
        public string Status { get; set; } = "Pending";
        public string Message { get; set; } = string.Empty;
    }
}
