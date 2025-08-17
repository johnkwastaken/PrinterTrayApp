namespace PrinterTrayApp.Models;

public enum PortType
{
    Unknown,
    USB,
    NetworkIP,      // Direct IP: 192.168.1.100
    NetworkShare,   // UNC path: \\server\printer
    NetworkHost,    // Hostname: printer.local
    Serial,
    Parallel,
    File,
    WSD,
    Virtual
}

public static class PortTypeHelper
{
    public static PortType GetPortType(string portName)
    {
        if (string.IsNullOrEmpty(portName))
            return PortType.Unknown;
            
        var port = portName.ToUpper();
        
        // USB ports (USB001, USB002, etc.)
        if (port.StartsWith("USB"))
            return PortType.USB;
            
        // Serial/COM ports (COM1, COM2, etc.)
        if (port.StartsWith("COM"))
            return PortType.Serial;
            
        // Parallel ports (LPT1, LPT2, etc.)
        if (port.StartsWith("LPT"))
            return PortType.Parallel;
            
        // WS-Discovery ports (WSD-e3a5b2c1-...)
        if (port.StartsWith("WSD-") || port.Contains("WSD"))
            return PortType.WSD;
            
        // File/Virtual ports
        if (port.Contains("PORTPROMPT") || port == "FILE:" || port == "NUL:")
            return PortType.File;
            
        // Network Share (\\server\printer)
        if (portName.StartsWith(@"\\"))
            return PortType.NetworkShare;
            
        // IP Address (192.168.1.100 or 2001:db8::1)
        if (IsIPAddress(portName))
            return PortType.NetworkIP;
            
        // Hostname (printer.local, print-server, etc.)
        if (IsHostname(portName))
            return PortType.NetworkHost;
            
        return PortType.Unknown;
    }
    
    private static bool IsIPAddress(string port)
    {
        // Check for IPv4 pattern (rough check)
        var parts = port.Split('.');
        if (parts.Length == 4)
        {
            foreach (var part in parts)
            {
                if (!int.TryParse(part, out int num) || num < 0 || num > 255)
                    return false;
            }
            return true;
        }
        
        // Could also check for IPv6 with ':'
        return port.Contains(':') && port.Count(c => c == ':') >= 2;
    }
    
    private static bool IsHostname(string port)
    {
        // Simple hostname check - contains dots or is likely a hostname
        return !string.IsNullOrEmpty(port) && 
               (port.Contains('.') || port.Contains('-')) && 
               !port.Contains(@"\") &&
               !port.Contains(':') &&
               !IsIPAddress(port);
    }
    
    public static string GetPortTypeDisplay(PortType type)
    {
        return type switch
        {
            PortType.USB => "🔌 USB",
            PortType.NetworkIP => "🌐 Network (IP)",
            PortType.NetworkShare => "🖧 Network (Share)",
            PortType.NetworkHost => "🌐 Network (Host)",
            PortType.Serial => "📟 Serial",
            PortType.Parallel => "🖨️ Parallel",
            PortType.WSD => "🔍 WS-Discovery",
            PortType.File => "📁 File",
            PortType.Virtual => "💾 Virtual",
            _ => "❓ Unknown"
        };
    }
    
    public static bool SupportsRawPrinting(PortType type)
    {
        return type switch
        {
            PortType.USB => true,
            PortType.NetworkIP => true,      // Direct IP usually works
            PortType.NetworkShare => false,  // Shares often don't support RAW
            PortType.NetworkHost => true,    // Hostname usually works
            PortType.Serial => true,
            PortType.Parallel => true,
            PortType.WSD => false,           // WSD often has issues with RAW
            PortType.File => false,
            PortType.Virtual => false,
            _ => false
        };
    }
    
    public static string GetConnectionInfo(string portName)
    {
        var type = GetPortType(portName);
        return type switch
        {
            PortType.USB => $"USB Port {portName}",
            PortType.NetworkIP => $"TCP/IP: {portName}",
            PortType.NetworkShare => $"Share: {portName}",
            PortType.NetworkHost => $"Network: {portName}",
            PortType.Serial => $"Serial: {portName}",
            PortType.Parallel => $"Parallel: {portName}",
            PortType.WSD => "Web Services Discovery",
            PortType.File => "Print to File",
            _ => portName
        };
    }
}