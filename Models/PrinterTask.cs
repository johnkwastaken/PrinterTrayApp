namespace PrinterTrayApp.Models;

public class PrinterTask
{
    public ObjectId? _id { get; set; }
    public bool Evicted { get; set; }
    public bool Active { get; set; }
    public TargetDevice? TargetDevice { get; set; }
    public TargetDevice? SourceDevice { get; set; }
    public ReceiptTemplate? Template { get; set; }
    public string TemplateData { get; set; } = string.Empty;
    public string? CurrentPrinterId { get; set; }
    public string PrinterName { get; set; } = string.Empty;
    public string PrinterDeviceName { get; set; } = string.Empty;
    public List<string>? SecondPrinterIds { get; set; }
    public bool IsOpenCashDrawer { get; set; }
    public bool IsComplete { get; set; }
    public bool InProgress { get; set; }
    public int RetryCount { get; set; }
    public bool IsSuspend { get; set; }
    public string? IpAddress { get; set; }
    public string? AppVersion { get; set; }
    public string? TemplateVersion { get; set; }
    public string? Error { get; set; }
    public string? JobId { get; set; }
    public string? Name { get; set; }
    public string? RegisterName { get; set; }
    public PrinterLocationInfo? PrinterLocation { get; set; }
}

public class ObjectId
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SiteId { get; set; } = "";
    public string? OrgId { get; set; }
}

public class TargetDevice
{
    public string Name { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
}

public class ReceiptTemplate
{
    public string Body { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TemplateType { get; set; } = "Receipt";
}

public class PrinterLocationInfo
{
    public ObjectId? _id { get; set; }
    public bool Active { get; set; }
    public DateTime? CreatedTime { get; set; }
    public bool Evicted { get; set; }
    public string? Name { get; set; }
    public string? PrinterGroupId { get; set; }
    public List<string>? PrinterIds { get; set; }
    public string? SecondLocationId { get; set; }
    public string? UpdatedByDeviceId { get; set; }
    public string? UpdatedBySystem { get; set; }
    public DateTime? UpdatedTime { get; set; }
}

public enum TemplateType
{
    Receipt,
    Docket,
    Report,
    SelfTest,
    ShiftSummary,
    CloseDay,
    ShiftReview,
    StaffPayments,
    SalesReport,
    PaymentReport,
    WindCaveReceipt,
    Drawer
}

public enum Command
{
    OpenCashBox,
    Beep,
    SelfTest,
    Cut
}