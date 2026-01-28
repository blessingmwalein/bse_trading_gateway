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
    private readonly IHubContext<FixHub> _hubContextInstance;
    private readonly ConcurrentDictionary<string, QuickFix.Message> _orderStatus = new();
    private readonly ConcurrentQueue<object> _messages = new();
    private const int MaxMessages = 100;
    private SessionID? _activeSessionID;

    private static readonly Dictionary<int, string> TagNames = new()
    {
        { 8, "BeginString" }, { 9, "BodyLength" }, { 35, "MsgType" }, { 34, "SeqNum" },
        { 49, "SenderCompID" }, { 52, "SendingTime" }, { 56, "TargetCompID" },
        { 10, "CheckSum" }, { 1, "Account" }, { 11, "ClOrdID" }, { 38, "OrderQty" },
        { 40, "OrdType" }, { 44, "Price" }, { 54, "Side" }, { 55, "Symbol" },
        { 60, "TransactTime" }, { 150, "ExecType" }, { 39, "OrdStatus" },
        { 17, "ExecID" }, { 37, "OrderID" }, { 151, "LeavesQty" }, { 14, "CumQty" },
        { 6, "AvgPx" }, { 58, "Text" }, { 1137, "DefaultApplVerID" }, { 1128, "ApplVerID" },
        { 1409, "SessionStatus" }, { 98, "EncryptMethod" }, { 108, "HeartBtInt" },
        { 141, "ResetSeqNumFlag" }, { 30001, "OrderBook" }
    };

    public BseFixApplication(ILogger<BseFixApplication> logger, IHubContext<FixHub> hubContext)
    {
        _logger = logger;
        _hubContextInstance = hubContext;
    }

    private async void BroadcastMessage(string type, QuickFix.Message message, SessionID sessionID)
    {
        var rawString = message.ToString().Replace("\x01", "|");
        var decoded = DecodeFixMessage(message);

        var msgData = new
        {
            Timestamp = DateTime.UtcNow,
            Type = type,
            MsgType = message.Header.IsSetField(Tags.MsgType) ? message.Header.GetString(Tags.MsgType) : "Unknown",
            Raw = rawString,
            Decoded = decoded,
            Session = sessionID.ToString()
        };

        _messages.Enqueue(msgData);
        while (_messages.Count > MaxMessages) _messages.TryDequeue(out _);

        await _hubContextInstance.Clients.All.SendAsync("ReceiveFixMessage", msgData);
    }

    private List<object> DecodeFixMessage(QuickFix.Message message)
    {
        var fields = new List<object>();

        // Header
        foreach (var field in message.Header)
            fields.Add(new { Tag = field.Key, Name = TagNames.GetValueOrDefault(field.Key, $"Tag_{field.Key}"), Value = field.Value.ToString() });

        // Body
        foreach (var field in message)
            fields.Add(new { Tag = field.Key, Name = TagNames.GetValueOrDefault(field.Key, $"Tag_{field.Key}"), Value = field.Value.ToString() });

        // Trailer
        foreach (var field in message.Trailer)
            fields.Add(new { Tag = field.Key, Name = TagNames.GetValueOrDefault(field.Key, $"Tag_{field.Key}"), Value = field.Value.ToString() });

        return fields;
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

    public void OnCreate(SessionID sessionID)
    {
        _logger.LogInformation("Session created: {SessionID}", sessionID);
        _activeSessionID = sessionID;
        _hubContextInstance.Clients.All.SendAsync("UpdateSessionInfo", new { SessionID = sessionID.ToString(), LoggedOn = false });
    }

    public void OnLogon(SessionID sessionID)
    {
        _logger.LogInformation("Logon: {SessionID}", sessionID);
        _activeSessionID = sessionID;
        _hubContextInstance.Clients.All.SendAsync("UpdateSessionInfo", new { SessionID = sessionID.ToString(), LoggedOn = true });
    }

    public void OnLogout(SessionID sessionID)
    {
        _logger.LogInformation("Logout: {SessionID}", sessionID);
        _hubContextInstance.Clients.All.SendAsync("UpdateSessionInfo", new { SessionID = sessionID.ToString(), LoggedOn = false });
    }

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
        SendToTarget(nos);
    }

    private void SendToTarget(QuickFix.Message message)
    {
        var sessionID = _activeSessionID ?? new SessionID("FIXT.1.1", "ESC_MOTS", "FGW");
        
        if (!Session.DoesSessionExist(sessionID))
            throw new SessionNotFound($"Session {sessionID} not found. Ensure the session is correctly configured in client.cfg.");

        var session = Session.LookupSession(sessionID);
        if (session == null)
            throw new SessionNotFound($"Session {sessionID} could not be looked up.");

        if (!session.IsLoggedOn)
            _logger.LogWarning("Sending message to session {SessionID} while it is not logged on.", sessionID);

        Session.SendToTarget(message, sessionID);
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
        SendToTarget(ocr);
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
        SendToTarget(ocrr);
    }
}
