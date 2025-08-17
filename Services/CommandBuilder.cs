using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using PrinterTrayApp.Models;

namespace PrinterTrayApp.Services;

/// <summary>
/// ESC/POS Command Builder - Converts rendered XML to printer control codes
/// 
/// PRIMARY PURPOSE:
/// Translates human-readable XML into ESC/POS (Epson Standard Code) commands
/// that thermal printers understand. This is the final step before printing.
/// 
/// INPUT: Rendered XML document with actual values (no more {{tokens}})
/// OUTPUT: String of ESC/POS commands ready for RAW printing
/// 
/// ESC/POS PROTOCOL OVERVIEW:
/// - Industry standard since 1990s for thermal receipt printers
/// - Uses escape sequences: ESC (0x1B) and GS (0x1D) followed by command bytes
/// - Common commands:
///   * ESC @ (0x1B 0x40): Initialize printer
///   * ESC ! n (0x1B 0x21 n): Select print modes (size, emphasis)
///   * ESC a n (0x1B 0x61 n): Text alignment
///   * GS V (0x1D 0x56): Cut paper
/// - Commands are sent as raw byte strings through Windows spooler
/// 
/// XML ELEMENTS PROCESSED:
/// - <text>: Regular text with formatting attributes
///   * size: normal, wide, high, wide-high
///   * align: left, center, right
///   * font-style: normal, b (bold), u (underline)
///   * font-family: a, b, c (different font sizes)
/// - <separator>: Horizontal line (e.g., "--------")
/// - <blank lines="n">: Insert n empty lines
/// - <command cmd="cut|opencashdrawer|beep">: Hardware commands
/// - <barcode>/<qrcode>: Machine-readable codes
/// - <table>: Multi-column layout for receipts
/// 
/// OUTPUT: String of ESC/POS commands ready to send to printer via RAW printing
/// </summary>
public class CommandBuilder
{
    private readonly StringBuilder _commands;  // Accumulates all ESC/POS commands
    private readonly PrinterPaperWidth _paperWidth;  // 58mm or 80mm paper roll
    
    /// <summary>
    /// Creates a new command builder for generating ESC/POS commands
    /// </summary>
    /// <param name="paperWidth">Width of thermal paper (affects text wrapping)</param>
    public CommandBuilder(PrinterPaperWidth paperWidth = PrinterPaperWidth.Paper_80)
    {
        _commands = new StringBuilder();
        _paperWidth = paperWidth;
        Reset();  // Send printer initialization command
    }
    
    public CommandBuilder Reset()
    {
        _commands.Append(POS80Commands.init);
        return this;
    }
    
    public CommandBuilder AddText(string text, PrinterAlign align = PrinterAlign.Left, 
        PrinterScale size = PrinterScale.Normal, PrinterFontStyle style = PrinterFontStyle.Normal, 
        PrinterFontColor color = PrinterFontColor.Color_1)
    {
        if (!string.IsNullOrEmpty(text))
        {
            // Apply formatting
            if (color != PrinterFontColor.Color_1)
                _commands.Append(POS80Commands.fontColor[color]);
            
            if (size != PrinterScale.Normal)
                _commands.Append(POS80Commands.size[size]);
            
            if (align != PrinterAlign.Left)
                _commands.Append(POS80Commands.align[align]);
            
            if (style != PrinterFontStyle.Normal)
                _commands.Append(POS80Commands.fontStyle[style]);
            
            // Add text
            _commands.Append(text);
            
            // Reset formatting
            if (style != PrinterFontStyle.Normal)
                _commands.Append(POS80Commands.fontStyle[PrinterFontStyle.Normal]);
            
            if (size != PrinterScale.Normal)
                _commands.Append(POS80Commands.size[PrinterScale.Normal]);
            
            if (align != PrinterAlign.Left)
                _commands.Append(POS80Commands.align[PrinterAlign.Left]);
            
            if (color != PrinterFontColor.Color_1)
                _commands.Append(POS80Commands.fontColor[PrinterFontColor.Color_1]);
        }
        return this;
    }
    
    public CommandBuilder AddNewLine()
    {
        _commands.Append("\n");
        return this;
    }
    
    public CommandBuilder AddBlankLines(int count)
    {
        for (int i = 0; i < count; i++)
        {
            _commands.Append("\n");
        }
        return this;
    }
    
    public CommandBuilder AddSeparator(char character = '-')
    {
        var width = _paperWidth == PrinterPaperWidth.Paper_80 ? 48 : 32;
        _commands.Append(new string(character, width));
        _commands.Append("\n");
        return this;
    }
    
    public CommandBuilder CutPaper()
    {
        _commands.Append(POS80Commands.cut);
        return this;
    }
    
    public CommandBuilder OpenCashDrawer(PrinterPulse pulse = PrinterPulse.Duration_100)
    {
        _commands.Append(POS80Commands.openCashDrawer(pulse));
        return this;
    }
    
    /// <summary>
    /// Main entry point - processes entire XML document and generates commands
    /// Takes the rendered XML (with all tokens replaced) and converts to ESC/POS
    /// </summary>
    /// <param name="doc">Rendered XML document with actual values (no more {{tokens}})</param>
    public CommandBuilder ProcessXmlDocument(XmlDocument doc)
    {
        DebugLogger.Log($"[CommandBuilder] Processing XML document");
        if (doc.DocumentElement != null)
        {
            DebugLogger.Log($"[CommandBuilder] Root element: {doc.DocumentElement.Name}, Child count: {doc.DocumentElement.ChildNodes.Count}");
            // Start processing from root element
            ProcessNode(doc.DocumentElement);
        }
        DebugLogger.Log($"[CommandBuilder] Finished processing, command length: {_commands.Length}");
        return this;
    }
    
    private void ProcessNode(XmlNode node)
    {
        switch (node.Name.ToLower())
        {
            case "text":
                ProcessTextNode(node);
                break;
                
            case "blank":
                ProcessBlankNode(node);
                break;
                
            case "separator":
                ProcessSeparatorNode(node);
                break;
                
            case "table":
                ProcessTableNode(node);
                break;
                
            case "command":
                ProcessCommandNode(node);
                break;
                
            case "barcode":
                ProcessBarcodeNode(node);
                break;
                
            case "qrcode":
                ProcessQrCodeNode(node);
                break;
                
            case "docket-section":
                ProcessDocketSection(node);
                break;
                
            case "receipt-section":
                ProcessReceiptSection(node);
                break;
                
            case "root":
            default:
                foreach (XmlNode child in node.ChildNodes)
                {
                    ProcessNode(child);
                }
                break;
        }
    }
    
    /// <summary>
    /// Processes a <text> element and generates appropriate ESC/POS commands
    /// This is the most complex processor as it handles all text formatting
    /// </summary>
    private void ProcessTextNode(XmlNode node)
    {
        var text = node.InnerText ?? "";
        DebugLogger.Log($"[CommandBuilder] Processing text node: '{text}' (length: {text.Length})");
        if (string.IsNullOrEmpty(text)) 
        {
            DebugLogger.Log($"[CommandBuilder] Skipping empty text node");
            return;  // Don't process empty text nodes
        }
        
        // Log all attributes for debugging styling issues
        // This helps diagnose why text might not appear as expected
        DebugLogger.Log($"[STYLE-DEBUG] Text node attributes:");
        if (node.Attributes != null)
        {
            foreach (XmlAttribute attr in node.Attributes)
            {
                DebugLogger.Log($"[STYLE-DEBUG]   {attr.Name}='{attr.Value}'");
            }
        }
        
        // Get attributes
        var align = GetAlignment(node.Attributes?["align"]?.Value);
        var size = GetSize(node.Attributes?["size"]?.Value);
        var scaleAttr = node.Attributes?["scale"]?.Value;
        var fontFamily = GetFontFamily(node.Attributes?["font-family"]?.Value);
        // Support both "font-style" and "font" attributes
        var fontStyle = GetFontStyle(node.Attributes?["font-style"]?.Value ?? node.Attributes?["font"]?.Value);
        
        DebugLogger.Log($"[STYLE-DEBUG] Parsed values:");
        DebugLogger.Log($"[STYLE-DEBUG]   align={align}, size={size}, fontFamily={fontFamily}, fontStyle={fontStyle}");
        
        // Handle scale attribute (overrides size and font-family)
        if (!string.IsNullOrEmpty(scaleAttr) && int.TryParse(scaleAttr, out var scale))
        {
            var (scaledSize, scaledFontFamily) = GetScaleMapping(scale);
            size = scaledSize;
            fontFamily = scaledFontFamily;
        }
        
        // Apply font color
        var fontColor = GetFontColor(node.Attributes?["font-color"]?.Value);
        
        // CRITICAL: Send ALL formatting commands in exact order
        // The POS always sends all commands even for defaults
        // This ensures printer state is consistent and predictable
        // Order matters: Color -> Size -> Font -> Align -> Style
        
        // 1. Font Color
        var colorCmd = POS80Commands.fontColor[fontColor];
        DebugLogger.Log($"[STYLE-DEBUG] Adding color command for {fontColor}: {EscapeString(colorCmd)}");
        _commands.Append(colorCmd);
        
        // 2. Size
        var sizeCmd = POS80Commands.size[size];
        DebugLogger.Log($"[STYLE-DEBUG] Adding size command for {size}: {EscapeString(sizeCmd)}");
        _commands.Append(sizeCmd);
        
        // 3. Font Family
        var fontCmd = POS80Commands.fontFamily[fontFamily];
        DebugLogger.Log($"[STYLE-DEBUG] Adding font command for {fontFamily}: {EscapeString(fontCmd)}");
        _commands.Append(fontCmd);
        
        // 4. Alignment
        var alignCmd = POS80Commands.align[align];
        DebugLogger.Log($"[STYLE-DEBUG] Adding align command for {align}: {EscapeString(alignCmd)}");
        _commands.Append(alignCmd);
        
        // 5. Font Style (bold, underline, etc)
        var styleCmd = POS80Commands.fontStyle[fontStyle];
        DebugLogger.Log($"[STYLE-DEBUG] Adding style command for {fontStyle}: {EscapeString(styleCmd)}");
        _commands.Append(styleCmd);
        
        // Add text
        _commands.Append(text);
        
        // IMPORTANT: Do NOT reset formatting after text!
        // The POS doesn't reset - each element sets its own formatting
        // We previously had reset commands here that were canceling styles
        // This was the main cause of styling not appearing on printouts
        
        // Add newline
        _commands.Append("\n");
    }
    
    private void ProcessBlankNode(XmlNode node)
    {
        var lines = 1;
        if (int.TryParse(node.Attributes?["lines"]?.Value, out var l))
        {
            lines = l;
        }
        
        for (int i = 0; i < lines; i++)
        {
            _commands.Append("\n");
        }
    }
    
    private void ProcessSeparatorNode(XmlNode node)
    {
        var character = '-';
        if (!string.IsNullOrEmpty(node.Attributes?["char"]?.Value))
        {
            character = node.Attributes["char"].Value[0];
        }
        
        var width = 48; // Default for 80mm
        if (_paperWidth == PrinterPaperWidth.Paper_58)
            width = 32;
            
        _commands.Append(new string(character, width));
        _commands.Append("\n");
    }
    
    private void ProcessTableNode(XmlNode node)
    {
        // Process table columns
        var columns = node.SelectNodes("column");
        if (columns != null)
        {
            var line = new StringBuilder();
            foreach (XmlNode col in columns)
            {
                var text = col.InnerText ?? "";
                var widthStr = col.Attributes?["width"]?.Value;
                var align = col.Attributes?["align"]?.Value;
                
                if (int.TryParse(widthStr, out var width))
                {
                    var widthChars = (int)(width / 100.0 * 48); // Convert percentage to characters
                    text = FormatTableCell(text, widthChars, align);
                }
                
                line.Append(text);
            }
            _commands.Append(line.ToString());
            _commands.Append("\n");
        }
    }
    
    private string FormatTableCell(string text, int width, string? align)
    {
        if (text.Length > width)
            text = text.Substring(0, width);
        
        if (align == "right")
            return text.PadLeft(width);
        else if (align == "center")
        {
            var padding = (width - text.Length) / 2;
            return text.PadLeft(text.Length + padding).PadRight(width);
        }
        else
            return text.PadRight(width);
    }
    
    private void ProcessCommandNode(XmlNode node)
    {
        var cmd = node.Attributes?["cmd"]?.Value?.ToLower();
        
        switch (cmd)
        {
            case "cut":
                _commands.Append(POS80Commands.cut);
                break;
                
            case "opencashdrawer":
                _commands.Append(POS80Commands.openCashDrawer(PrinterPulse.Duration_100));
                break;
                
            case "beep":
                _commands.Append(POS80Commands.beep);
                break;
        }
    }
    
    private void ProcessBarcodeNode(XmlNode node)
    {
        var data = node.InnerText;
        if (!string.IsNullOrEmpty(data))
        {
            // For simplicity, just add the barcode data as text
            // Real implementation would use proper barcode commands
            _commands.Append(data);
            _commands.Append("\n");
        }
    }
    
    private void ProcessQrCodeNode(XmlNode node)
    {
        var data = node.InnerText;
        if (!string.IsNullOrEmpty(data))
        {
            // For simplicity, just add the QR code data as text
            // Real implementation would use proper QR code commands
            _commands.Append(data);
            _commands.Append("\n");
        }
    }
    
    private PrinterAlign GetAlignment(string? align)
    {
        return align?.ToLower() switch
        {
            "center" => PrinterAlign.Center,
            "right" => PrinterAlign.Right,
            _ => PrinterAlign.Left
        };
    }
    
    private PrinterScale GetSize(string? size)
    {
        return size?.ToLower() switch
        {
            "high" => PrinterScale.High,
            "wide" => PrinterScale.Wide,
            "wide-high" => PrinterScale.WideHigh,
            _ => PrinterScale.Normal
        };
    }
    
    private PrinterFontFamily GetFontFamily(string? family)
    {
        return family?.ToLower() switch
        {
            "a" => PrinterFontFamily.A,
            "b" => PrinterFontFamily.B,
            "c" => PrinterFontFamily.C,
            _ => PrinterFontFamily.A
        };
    }
    
    private PrinterFontColor GetFontColor(string? color)
    {
        return color?.ToLower() switch
        {
            "color_1" => PrinterFontColor.Color_1,
            "color_2" => PrinterFontColor.Color_2,
            "color_3" => PrinterFontColor.Color_3,
            "color_4" => PrinterFontColor.Color_4,
            "red" => PrinterFontColor.Color_2,  // Backward compatibility
            _ => PrinterFontColor.Color_1
        };
    }
    
    private PrinterFontStyle GetFontStyle(string? style)
    {
        return style?.ToLower() switch
        {
            "bold" => PrinterFontStyle.Bold,
            "b" => PrinterFontStyle.Bold,
            "underline" => PrinterFontStyle.Underscore,
            "underscore" => PrinterFontStyle.Underscore,
            "inverse" => PrinterFontStyle.Inverse,
            _ => PrinterFontStyle.Normal
        };
    }
    
    private (PrinterScale size, PrinterFontFamily fontFamily) GetScaleMapping(int scale)
    {
        // Match POS scaleToAttributes function exactly
        return scale switch
        {
            1 => (PrinterScale.Normal, PrinterFontFamily.C),     // Smallest (FontC + normal)
            2 => (PrinterScale.Normal, PrinterFontFamily.B),     // Small (FontB + normal)
            3 => (PrinterScale.Normal, PrinterFontFamily.A),     // Medium (FontA + normal)
            4 => (PrinterScale.WideHigh, PrinterFontFamily.C),   // Large (FontC + wide-high)
            5 => (PrinterScale.WideHigh, PrinterFontFamily.B),   // Larger (FontB + wide-high)
            6 => (PrinterScale.WideHigh, PrinterFontFamily.A),   // Largest (FontA + wide-high)
            _ => (PrinterScale.Normal, PrinterFontFamily.A)
        };
    }
    
    private void ProcessDocketSection(XmlNode node)
    {
        // Process docket-section children
        foreach (XmlNode child in node.ChildNodes)
        {
            ProcessNode(child);
        }
    }
    
    private void ProcessReceiptSection(XmlNode node)
    {
        // Process receipt-section children
        foreach (XmlNode child in node.ChildNodes)
        {
            ProcessNode(child);
        }
    }
    
    public string Build()
    {
        var result = _commands.ToString();
        
        // Log the final command string for debugging
        DebugLogger.Log($"[STYLE-DEBUG] Final ESC/POS command length: {result.Length} bytes");
        DebugLogger.Log($"[STYLE-DEBUG] First 200 chars (escaped): {EscapeString(result.Substring(0, Math.Min(200, result.Length)))}");
        
        return result;
    }
    
    private string EscapeString(string input)
    {
        var sb = new StringBuilder();
        foreach (char c in input)
        {
            if (c == '\x1B') sb.Append("<ESC>");
            else if (c == '\x1D') sb.Append("<GS>");
            else if (c == '\n') sb.Append("<LF>");
            else if (c == '\r') sb.Append("<CR>");
            else if (c < 32) sb.Append($"<0x{(int)c:X2}>");
            else sb.Append(c);
        }
        return sb.ToString();
    }
}