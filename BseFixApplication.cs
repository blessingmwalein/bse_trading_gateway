using QuickFix;
using QuickFix.Fields;
using MyFixFields;
using System.Collections.Concurrent;
using QuickFix.FIX50SP2;
using Microsoft.AspNetCore.SignalR;
using QuickFixT11Client.Hubs;

public class BseFixApplication : QuickFix.IApplication
{
    private readonly ILogger<BseFixApplication> _logger;
    private readonly IHubContext<FixHub> _hubContext;
    private readonly ConcurrentDictionary<string, QuickFix.Message> _orderStatus = new();
    private readonly ConcurrentQueue<object> _messages = new();
    private const int MaxMessages = 100;

    public BseFixApplication(ILogger<BseFixApplication> logger, IHubContext<FixHub> hubContext)
    {
        _logger = logger;
        _hubContext = hubContext;
    }

    private async void BroadcastMessage(string type, QuickFix.Message message, SessionID sessionID)
    {
        var msgData = new
        {
            Timestamp = DateTime.UtcNow,
            Type = type,
            MsgType = message.Header.IsSetField(Tags.MsgType) ? message.Header.GetString(Tags.MsgType) : "Unknown",
            Content = message.ToString().Replace("\x01", "|"),
            Session = sessionID.ToString()
        };

        _messages.Enqueue(msgData);
        while (_messages.Count > MaxMessages) _messages.TryDequeue(out _);

        await _hubContext.Clients.All.SendAsync("ReceiveFixMessage", msgData);
    }

    public void FromAdmin(QuickFix.Message message, SessionID sessionID)
    {
        _logger.LogInformation("FromAdmin: {Message}", message);
        BroadcastMessage("FromAdmin", message, sessionID);
    }

    public void FromApp(QuickFix.Message message, SessionID sessionID)
    {
        _logger.LogInformation("FromApp: {Message}", message);
        BroadcastMessage("FromApp", message, sessionID);
        HandleExecutionReport(message);
    }

    public void OnCreate(SessionID sessionID) => _logger.LogInformation("Session created: {SessionID}", sessionID);
    public void OnLogon(SessionID sessionID) => _logger.LogInformation("Logon: {SessionID}", sessionID);
    public void OnLogout(SessionID sessionID) => _logger.LogInformation("Logout: {SessionID}", sessionID);

    public void ToAdmin(QuickFix.Message message, SessionID sessionID)
    {
        _logger.LogInformation("ToAdmin: {Message}", message);
        BroadcastMessage("ToAdmin", message, sessionID);
    }

    public void ToApp(QuickFix.Message message, SessionID sessionID)
    {
        _logger.LogInformation("ToApp: {Message}", message);
        BroadcastMessage("ToApp", message, sessionID);
    }

    private void HandleExecutionReport(QuickFix.Message message)
    {
        if (message.Header.GetString(Tags.MsgType) == MsgType.EXECUTION_REPORT)
        {
            string clOrdID = message.IsSetField(Tags.ClOrdID) ? message.GetString(Tags.ClOrdID) : "UNKNOWN";
            _orderStatus[clOrdID] = message;
            _logger.LogInformation("Order {ClOrdID} updated. Status: {Status}", clOrdID, message.IsSetField(Tags.OrdStatus) ? message.GetString(Tags.OrdStatus) : "N/A");
        }
    }

    public ConcurrentDictionary<string, QuickFix.Message> GetOrderStatuses() => _orderStatus;
    public IEnumerable<object> GetRecentMessages() => _messages.ToArray();

    public void SendOrder(FixGateway.Models.OrderRequest request)
    {
        var nos = new NewOrderSingle(
            new ClOrdID(Guid.NewGuid().ToString("N")),
            new Side(request.Side[0]),
            new TransactTime(DateTime.UtcNow),
            new OrdType(request.OrderType[0])
        );

        nos.Set(new Symbol(request.Symbol));
        nos.Set(new OrderQty(request.Quantity));
        if (request.OrderType == "2")
            nos.Set(new Price(request.Price));

        if (!string.IsNullOrEmpty(request.Account))
            nos.Set(new Account(request.Account));

        // BSE Specific Tag: OrderBook (30001)
        nos.SetField(new IntField(30001, int.Parse(request.OrderBook)));

        _logger.LogInformation("Sending Order: {ClOrdID}", nos.ClOrdID.Value);
        Session.SendToTarget(nos, new SessionID("FIXT.1.1", "ESC_MOTS", "FGW"));
    }

    public void CancelOrder(string origClOrdID, string symbol, char side)
    {
        var ocr = new OrderCancelRequest();
        ocr.Set(new ClOrdID(Guid.NewGuid().ToString("N")));
        ocr.Set(new OrigClOrdID(origClOrdID));
        ocr.Set(new Side(side));
        ocr.Set(new TransactTime(DateTime.UtcNow));
        ocr.Set(new Symbol(symbol));

        _logger.LogInformation("Sending Cancel Request for: {OrigClOrdID}", origClOrdID);
        Session.SendToTarget(ocr, new SessionID("FIXT.1.1", "ESC_MOTS", "FGW"));
    }

    public void AmendOrder(string origClOrdID, FixGateway.Models.OrderRequest request)
    {
        var ocrr = new OrderCancelReplaceRequest();
        ocrr.Set(new ClOrdID(Guid.NewGuid().ToString("N")));
        ocrr.Set(new OrigClOrdID(origClOrdID));
        ocrr.Set(new Side(request.Side[0]));
        ocrr.Set(new TransactTime(DateTime.UtcNow));
        ocrr.Set(new OrdType(request.OrderType[0]));

        ocrr.Set(new Symbol(request.Symbol));
        ocrr.Set(new OrderQty(request.Quantity));
        if (request.OrderType == "2")
            ocrr.Set(new Price(request.Price));

        // BSE Specific Tag: OrderBook (30001)
        ocrr.SetField(new IntField(30001, int.Parse(request.OrderBook)));

        _logger.LogInformation("Sending Amend Request for: {OrigClOrdID}", origClOrdID);
        Session.SendToTarget(ocrr, new SessionID("FIXT.1.1", "ESC_MOTS", "FGW"));
    }
}
