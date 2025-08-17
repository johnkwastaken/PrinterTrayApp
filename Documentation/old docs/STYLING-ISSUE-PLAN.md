# Text Styling Not Working - Investigation Plan

## 🔍 Issue Description
Text styling attributes (font-family, font-style, size) are present in the XML but not being applied to the printed output.

## Current Behavior

### What We See in XML:
```xml
<text font-family="a" size="wide-high" font-style="normal">1x Easy cheesy burger</text>
```

### What Should Happen:
- `font-family="a"` → Should use Font A on printer
- `size="wide-high"` → Should print double width and height
- `font-style="normal"` → Should use normal (not bold) style

## 🐛 Potential Issues

### 1. CommandBuilder Not Reading Attributes
**Location**: `Services/CommandBuilder.cs`
- The CommandBuilder expects hyphenated attributes: `font-family`, `font-style`, `size`
- Check if attributes are being read correctly from XML nodes

### 2. ESC/POS Commands Not Being Generated
**Location**: `Services/POS80Commands.cs`
- Commands might not be formatted correctly
- String commands vs byte commands issue

### 3. Attributes Lost During Processing
**Location**: `Services/TemplateHelpers.cs`
- When creating text nodes for products, attributes might not be set properly
- Check lines where `SetAttribute` is called

## 📋 Investigation Steps

### Step 1: Verify Attributes in XML
```csharp
// Add logging in CommandBuilder.ProcessTextNode
DebugLogger.Log($"[STYLE] Text node attributes:");
DebugLogger.Log($"[STYLE] font-family: {node.Attributes?["font-family"]?.Value}");
DebugLogger.Log($"[STYLE] font-style: {node.Attributes?["font-style"]?.Value}");
DebugLogger.Log($"[STYLE] size: {node.Attributes?["size"]?.Value}");
```

### Step 2: Check Command Generation
```csharp
// Add logging for ESC/POS commands
DebugLogger.Log($"[STYLE] Generated size command: {POS80Commands.size[size]}");
DebugLogger.Log($"[STYLE] Generated font command: {POS80Commands.fontFamily[fontFamily]}");
```

### Step 3: Verify Product Text Node Creation
In TemplateHelpers.cs around line 869:
```csharp
var productText = doc.CreateElement("text");
// Check these are actually being set:
productText.SetAttribute("font-family", prodFontFamily);
productText.SetAttribute("size", prodFontSize);
productText.SetAttribute("font-style", productStyle);
```

## 🔧 Quick Fix Checklist

1. **Check POS80Commands.cs**
   - Are size commands correct? (e.g., `\x1D\x21\x11` for wide-high)
   - Are font family commands correct? (e.g., `\x1B\x4D\x00` for Font A)

2. **Check CommandBuilder.cs**
   - Is `ProcessTextNode` reading attributes?
   - Are default values overriding styled values?

3. **Check TemplateHelpers.cs**
   - Are product text nodes getting attributes set?
   - Is the scale-to-attributes conversion working?

## 🧪 Test Cases

### Test 1: Simple styled text
```json
{
  "template": {
    "body": "<root><text size=\"wide-high\">BIG TEXT</text><text size=\"normal\">normal text</text></root>"
  },
  "templateData": "{}",
  "isOpenCashDrawer": false
}
```

### Test 2: Product with styling
```json
{
  "template": {
    "body": "<root><docket-section product-scale=\"8\" /></root>"
  },
  "templateData": "{\"orders\":{\"mainProducts\":[...]}}",
  "isOpenCashDrawer": false
}
```

## 📊 Expected vs Actual

### Expected ESC/POS Commands:
```
\x1D\x21\x11  (size: wide-high)
1x Easy cheesy burger
\x1D\x21\x00  (size: normal)
```

### Actual Commands (need to verify):
```
1x Easy cheesy burger  (no size commands?)
```

## 🎯 Solution Approach

1. **Add detailed logging** to track attribute flow
2. **Verify ESC/POS command strings** are correct
3. **Test with simple styled text** first
4. **Fix attribute passing** in product generation
5. **Ensure commands reset** after styled text

## 📝 Notes

- The POS80Commands use string representations of ESC/POS commands
- The printer expects specific byte sequences for formatting
- Attributes must be read from XML nodes during command building
- Default values shouldn't override explicit attributes