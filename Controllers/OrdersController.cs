using Microsoft.AspNetCore.Mvc;
using FixGateway.Models;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly BseFixApplication _application;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(BseFixApplication application, ILogger<OrdersController> logger)
    {
        _application = application;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new order and sends it to the FIX gateway.
    /// </summary>
    /// <param name="request">The order details.</param>
    /// <returns>A message indicating the order has been submitted.</returns>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public IActionResult CreateOrder([FromBody] OrderRequest request)
    {
        try
        {
            _application.SendOrder(request);
            return Accepted(new { Message = "Order submitted to FIX Gateway" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to submit order");
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Modifies an existing order.
    /// </summary>
    /// <param name="clOrdId">The original Client Order ID.</param>
    /// <param name="request">The new order details.</param>
    /// <returns>A message indicating the amend request has been submitted.</returns>
    [HttpPut("{clOrdId}")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public IActionResult AmendOrder(string clOrdId, [FromBody] OrderRequest request)
    {
        try
        {
            _application.AmendOrder(clOrdId, request);
            return Accepted(new { Message = $"Amend request for {clOrdId} submitted" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to amend order {ClOrdId}", clOrdId);
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Cancels an existing order.
    /// </summary>
    /// <param name="clOrdId">The Client Order ID to cancel.</param>
    /// <param name="symbol">The symbol of the order.</param>
    /// <param name="side">The side (1=Buy, 2=Sell).</param>
    /// <returns>A message indicating the cancel request has been submitted.</returns>
    [HttpDelete("{clOrdId}")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public IActionResult CancelOrder(string clOrdId, [FromQuery] string symbol, [FromQuery] string side = "1")
    {
        try
        {
            _application.CancelOrder(clOrdId, symbol, side[0]);
            return Accepted(new { Message = $"Cancel request for {clOrdId} submitted" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel order {ClOrdId}", clOrdId);
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Retrieves the status of all orders.
    /// </summary>
    /// <returns>A dictionary of ClOrdIDs and their current FIX statuses.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetStatuses()
    {
        var statuses = _application.GetOrderStatuses();
        return Ok(statuses.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString()));
    }
}
