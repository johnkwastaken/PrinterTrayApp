using System;
using System.Collections.Generic;

namespace PrinterTrayApp.Models;

public class PrinterTask
{
    public ObjectId? _id { get; set; }
    public bool evicted { get; set; }
    public bool active { get; set; }
    public TargetDevice? targetDevice { get; set; }
    public TargetDevice? sourceDevice { get; set; }
    public ReceiptTemplate? template { get; set; }
    public string templateData { get; set; } = string.Empty;
    public string? currentPrinterId { get; set; }
    public string printerName { get; set; } = string.Empty;
    public string printerDeviceName { get; set; } = string.Empty;
    public List<string>? secondPrinterIds { get; set; }
    public bool isOpenCashDrawer { get; set; }
    public bool isComplete { get; set; }
    public bool inProgress { get; set; }
    public int retryCount { get; set; }
    public bool isSuspend { get; set; }
    public string? ipAddress { get; set; }
    public string? appVersion { get; set; }
    public string? templateVersion { get; set; }
    public string? error { get; set; }
    public string? jobId { get; set; }
    public string? name { get; set; }
    public string? registerName { get; set; }
    public PrinterLocationInfo? printerLocation { get; set; }
}

public class ObjectId
{
    public string id { get; set; } = Guid.NewGuid().ToString();
    public string siteId { get; set; } = "";
    public string? orgId { get; set; }
}

public class TargetDevice
{
    public string name { get; set; } = string.Empty;
    public string deviceId { get; set; } = string.Empty;
}

public class ReceiptTemplate
{
    public string body { get; set; } = string.Empty;
    public string name { get; set; } = string.Empty;
    public string templateType { get; set; } = "Receipt";
}

public class PrinterLocationInfo
{
    public ObjectId? _id { get; set; }
    public bool active { get; set; }
    public DateTime? createdTime { get; set; }
    public bool evicted { get; set; }
    public string? name { get; set; }
    public string? printerGroupId { get; set; }
    public List<string>? printerIds { get; set; }
    public string? secondLocationId { get; set; }
    public string? updatedByDeviceId { get; set; }
    public string? updatedBySystem { get; set; }
    public DateTime? updatedTime { get; set; }
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