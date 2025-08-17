using System;
using System.Collections.Generic;
using System.Text;
using PrinterTrayApp.Models;

namespace PrinterTrayApp.Services;

public static class POS80Commands
{
    // Basic commands
    public const string init = "\x1B\x40";
    public const string cut = "\n\n\n\x1D\x56\x01";
    public const string beep = "\x1B\x70\x00\x32\x32";
    public const string feed = "\x0A";
    public const string nextLine = "\x0A";
    
    // Font family commands
    public static readonly Dictionary<PrinterFontFamily, string> fontFamily = new()
    {
        { PrinterFontFamily.A, "\x1B\x4D\x00" },
        { PrinterFontFamily.B, "\x1B\x4D\x01" },
        { PrinterFontFamily.C, "\x1B\x4D\x02" }
    };
    
    // Font size commands (matching POS system - using ESC ! not GS !)
    public static readonly Dictionary<PrinterScale, string> size = new()
    {
        { PrinterScale.Normal, "\x1B\x21\x00" },
        { PrinterScale.High, "\x1B\x21\x10" },
        { PrinterScale.Wide, "\x1B\x21\x20" },
        { PrinterScale.WideHigh, "\x1B\x21\x30" }
    };
    
    // Alignment commands
    public static readonly Dictionary<PrinterAlign, string> align = new()
    {
        { PrinterAlign.Left, "\x1B\x61\x00" },
        { PrinterAlign.Center, "\x1B\x61\x01" },
        { PrinterAlign.Right, "\x1B\x61\x02" }
    };
    
    // Font style commands (matching POS system exactly)
    public static readonly Dictionary<PrinterFontStyle, string> fontStyle = new()
    {
        { PrinterFontStyle.Normal, "\x1B\x45\x00" },      // Normal
        { PrinterFontStyle.Bold, "\x1B\x45\x01" },        // Bold
        { PrinterFontStyle.Underscore, "\x1B\x2D\x01" }   // Underline
    };
    
    // Inverse commands
    public static readonly Dictionary<bool, string> inverse = new()
    {
        { false, "\x1D\x42\x00" },
        { true, "\x1D\x42\x01" }
    };
    
    // Font color commands (matching POS system exactly)
    public static readonly Dictionary<PrinterFontColor, string> fontColor = new()
    {
        { PrinterFontColor.Color_1, "\x1B\x72\x00" },  // Black (default)
        { PrinterFontColor.Color_2, "\x1B\x72\x01" },  // Red
        { PrinterFontColor.Color_3, "\x1B\x72\x02" },  // Alternative color
        { PrinterFontColor.Color_4, "\x1B\x72\x03" }   // Alternative color
    };
    
    // Cash drawer command function (matching POS system exactly)
    public static string openCashDrawer(PrinterPulse duration)
    {
        // ESC p duration 0x40 0x50 (matching POS implementation)
        var durationByte = (byte)duration;
        var bytes = new byte[] { 0x1B, 0x70, durationByte, 0x40, 0x50 };
        return Encoding.Latin1.GetString(bytes);
    }
    
    // Barcode type commands
    public static readonly Dictionary<PrinterBarcodeType, string> barcodeType = new()
    {
        { PrinterBarcodeType.Code39, "\x04" },
        { PrinterBarcodeType.Code128, "\x49" },
        { PrinterBarcodeType.EAN13, "\x02" },
        { PrinterBarcodeType.QRCode, "\x31" }
    };
    
    // Special commands nested class
    public static class cmd
    {
        public const string partialCut = "\x1D\x56\x41\x00";
        public const string selfTest = "\x1B\x69";
    }
    
    // Barcode nested class
    public static class barcode
    {
        public const string height = "\x1D\x68";
        public const string width = "\x1D\x77";
        public const string textPosition = "\x1D\x48";
        
        public static string code128(string data)
        {
            // GS k m n data
            // m = 73 for CODE128
            // n = data length
            var n = (byte)data.Length;
            return $"\x1D\x6B\x49{(char)n}{data}";
        }
        
        public static string qrCode(string data)
        {
            // QR Code generation (simplified)
            var modelCommand = "\x1D\x28\x6B\x04\x00\x31\x41\x32\x00"; // Model 2
            var sizeCommand = "\x1D\x28\x6B\x03\x00\x31\x43\x06"; // Size 6
            var errorCommand = "\x1D\x28\x6B\x03\x00\x31\x45\x30"; // Error correction L
            
            var len = data.Length + 3;
            var pL = (byte)(len & 0xFF);
            var pH = (byte)((len >> 8) & 0xFF);
            var storeCommand = $"\x1D\x28\x6B{(char)pL}{(char)pH}\x31\x50\x30{data}";
            var printCommand = "\x1D\x28\x6B\x03\x00\x31\x51\x30";
            
            return modelCommand + sizeCommand + errorCommand + storeCommand + printCommand;
        }
    }
    
    // Helper methods
    public static string lineSpace(int spacing)
    {
        if (spacing <= 0) return "\x1B\x32"; // Default line spacing
        return $"\x1B\x33{(char)spacing}";
    }
    
    public static string lineFeed(int lines)
    {
        var result = "";
        for (int i = 0; i < lines; i++)
        {
            result += nextLine;
        }
        return result;
    }
    
    public static string text(string content)
    {
        return content ?? "";
    }
    
    public static string image(byte[] imageData, PrinterAlign alignment = PrinterAlign.Center)
    {
        // Simplified image handling
        return align[alignment] + "\x1B\x2A\x00" + align[PrinterAlign.Left];
    }
}