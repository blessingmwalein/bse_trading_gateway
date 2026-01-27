using Microsoft.AspNetCore.Mvc;
using QuickFix;
using QuickFix.Fields;

[ApiController]
[Route("api/[controller]")]
public class ReconciliationController : ControllerBase
{
    private readonly BseFixApplication _application;

    public ReconciliationController(BseFixApplication application)
    {
        _application = application;
    }

    /// <summary>
    /// Retrieves a detailed summary of all orders for reconciliation.
    /// </summary>
    /// <returns>A list of orders with their full FIX details (Symbol, Side, Qty, Status, etc.).</returns>
    [HttpGet("summary")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetSummary()
    {
        var statuses = _application.GetOrderStatuses();
        
        var summary = statuses.Select(kvp => 
        {
            var msg = kvp.Value;
            return new 
            {
                ClOrdID = kvp.Key,
                Symbol = msg.IsSetField(QuickFix.Fields.Tags.Symbol) ? msg.GetString(QuickFix.Fields.Tags.Symbol) : "N/A",
                Side = msg.IsSetField(QuickFix.Fields.Tags.Side) ? msg.GetString(QuickFix.Fields.Tags.Side) : "N/A",
                Qty = msg.IsSetField(QuickFix.Fields.Tags.OrderQty) ? msg.GetDecimal(QuickFix.Fields.Tags.OrderQty) : 0,
                Status = msg.IsSetField(QuickFix.Fields.Tags.OrdStatus) ? msg.GetString(QuickFix.Fields.Tags.OrdStatus) : "N/A",
                ExecType = msg.IsSetField(QuickFix.Fields.Tags.ExecType) ? msg.GetString(QuickFix.Fields.Tags.ExecType) : "N/A",
                LastPx = msg.IsSetField(QuickFix.Fields.Tags.LastPx) ? msg.GetDecimal(QuickFix.Fields.Tags.LastPx) : 0,
                LeavesQty = msg.IsSetField(QuickFix.Fields.Tags.LeavesQty) ? msg.GetDecimal(QuickFix.Fields.Tags.LeavesQty) : 0,
                CumQty = msg.IsSetField(QuickFix.Fields.Tags.CumQty) ? msg.GetDecimal(QuickFix.Fields.Tags.CumQty) : 0,
                AvgPx = msg.IsSetField(QuickFix.Fields.Tags.AvgPx) ? msg.GetDecimal(QuickFix.Fields.Tags.AvgPx) : 0
            };
        });

        return Ok(summary);
    }
}
