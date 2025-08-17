namespace PrinterTrayApp.Models;

public enum PrinterFontFamily
{
    A,
    B,
    C,
    FontA = A,
    FontB = B,
    FontC = C
}

public enum PrinterScale
{
    Normal,
    High,
    Wide,
    WideHigh
}

public enum PrinterAlign
{
    Left,
    Center,
    Right
}

public enum PrinterFontColor
{
    Color_1,
    Color_2,
    Color_3,
    Color_4,
    // Backward compatibility aliases
    Black = Color_1,
    Red = Color_2
}

public enum PrinterFontColour
{
    Color_1,
    Color_2,
    Color_3,
    Color_4
}

public enum PrinterPulse
{
    Duration_50 = 50,
    Duration_100 = 100,
    Duration_150 = 150,
    Duration_200 = 200,
    Duration_250 = 250,
    Duration_300 = 300,
    Duration_400 = 400,
    Duration_500 = 500
}

public enum PrinterBarcodeType
{
    Code39,
    Code128,
    EAN13,
    QRCode
}

public enum PrinterFontStyle
{
    Normal,
    Bold,
    Underscore,
    Inverse
}

public enum PrinterPaperWidth
{
    Paper_58 = 58,
    Paper_80 = 80
}