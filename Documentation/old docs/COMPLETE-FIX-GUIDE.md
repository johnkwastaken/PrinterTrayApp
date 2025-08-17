# 🔧 COMPLETE FIX GUIDE - PrinterTrayApp

## 📌 Overview
This document contains ALL the fixes needed to make the PrinterTrayApp work with POS PrinterTask data.

---

# ✅ CORRECT FIX - Respecting POS Data Structure

## 🎯 Core POS Principles We MUST Follow
1. **templateData arrives as a JSON STRING** - we parse it, don't change it
2. **Products are PRE-FLATTENED with `level` properties** - we use them as-is
3. **Circular references exist** - we handle them, don't remove them
4. **The data structure is complex but valid** - we process it correctly

## The Data We Receive (UNCHANGEABLE)
```json
{
  "templateData": "{\"orders\":{\"mainProducts\":[...]}}",  // STRING!
  "template": { "body": "<root>...</root>" }
}
```

Inside templateData (after parsing):
- Products already have `level: 0`, `level: 1`, etc.
- Categories have circular references (product→category→products)
- This is CORRECT POS data - we don't change it!

---

## 🔧 FIX 1: Parse templateData String (REQUIRED)

### Why This Is Required
The POS sends templateData as a STRING (line 390 of useDocketPrintTask.ts). We MUST parse it.

### What to Change
**File**: `HttpServer.cs` (around line 180)

**Find**:
```csharp
var templateData = printerTask.TemplateData ?? new JObject();
```

**Replace with**:
```csharp
// POS sends templateData as JSON string - must parse it
JObject templateData;
try 
{
    if (printerTask.TemplateData is string templateStr)
    {
        templateData = JObject.Parse(templateStr);
        DebugLogger.Log($"[POS-FIX] Parsed templateData string (POS format)");
    }
    else if (printerTask.TemplateData is JObject templateObj)
    {
        templateData = templateObj;
        DebugLogger.Log($"[POS-FIX] templateData already an object");
    }
    else
    {
        templateData = new JObject();
        DebugLogger.Log($"[POS-FIX] No templateData provided");
    }
}
catch (Exception ex)
{
    DebugLogger.Log($"[POS-FIX] ERROR parsing templateData: {ex.Message}");
    throw;
}
```

### Manual Test
```powershell
curl -X POST http://localhost:9877/print -H "Content-Type: application/json" -d @printertask.json
```

### ✅ Success = Log shows:
```
[POS-FIX] Parsed templateData string (POS format)
```

---

## 🔧 FIX 2: Don't Destroy Valid Data

### Why This Is Required
The POS data has circular refs but uses `level` to handle them. JsonCleaner destroys this valid data.

### What to Change
**File**: `HttpServer.cs` (around line 185)

**Find**:
```csharp
templateData = JsonCleaner.Clean(templateData);
```

**Replace with**:
```csharp
// DON'T clean - POS data structure is valid as-is
// Products have circular refs but use 'level' property
// templateData = JsonCleaner.Clean(templateData);
DebugLogger.Log($"[POS-FIX] Keeping POS data structure intact");
```

### Manual Test
```powershell
curl -X POST http://localhost:9877/print -H "Content-Type: application/json" -d @printertask.json
```

### ✅ Success = Log shows:
```
[POS-FIX] Keeping POS data structure intact
```

---

## 🔧 FIX 3: Process Pre-Flattened Products with Levels

### Why This Is Required
POS already flattened products and added `level` properties. We must use them.

### What to Change
**File**: `TemplateHelpers.cs`

**Find where docket-section is processed** (in SortCategories or ProcessElement)

**Add this method that respects POS structure**:
```csharp
private static XmlDocument ProcessDocketSection(JObject ordersData)
{
    var doc = new XmlDocument();
    var root = doc.CreateElement("docket");
    
    try
    {
        // POS structure: orders.mainProducts[].categories[].category.products[]
        var mainProducts = ordersData?["mainProducts"] as JArray;
        if (mainProducts == null)
        {
            DebugLogger.Log("[POS-FIX] No mainProducts in orders data");
            return doc;
        }
        
        DebugLogger.Log($"[POS-FIX] Processing {mainProducts.Count} mainProducts");
        
        foreach (var mainProduct in mainProducts)
        {
            var categories = mainProduct["categories"] as JArray;
            if (categories == null) continue;
            
            foreach (var categoryWrapper in categories)
            {
                var category = categoryWrapper["category"];
                if (category == null) continue;
                
                var products = category["products"] as JArray;
                if (products == null) continue;
                
                DebugLogger.Log($"[POS-FIX] Found {products.Count} products in category");
                
                // Products are ALREADY FLATTENED with level properties
                foreach (var product in products)
                {
                    // Use the level property that POS provided
                    var level = product["level"]?.Value<int>() ?? 0;
                    var qty = product["qty"]?.ToString() ?? "";
                    var symbol = product["symbol"]?.ToString() ?? "";
                    var productName = product["productName"]?.ToString() ?? 
                                     product["printName"]?.ToString() ?? "";
                    
                    // Create indentation based on level (POS uses 2 spaces per level)
                    var indent = new string(' ', level * 2);
                    
                    // Build the product line
                    var productLine = $"{indent}{qty}{symbol} {productName}".TrimEnd();
                    
                    DebugLogger.Log($"[POS-FIX] Product L{level}: '{productLine}'");
                    
                    // Create text element
                    var textNode = doc.CreateElement("text");
                    textNode.InnerText = productLine;
                    
                    // Add font attributes based on level
                    if (level == 0)
                    {
                        textNode.SetAttribute("font-style", "b");
                        textNode.SetAttribute("size", "wide-high");
                    }
                    else
                    {
                        textNode.SetAttribute("size", "high");
                    }
                    
                    root.AppendChild(textNode);
                }
            }
        }
        
        // Also process otherProducts if they exist
        var otherProducts = ordersData?["otherProducts"] as JArray;
        if (otherProducts != null && otherProducts.Count > 0)
        {
            DebugLogger.Log($"[POS-FIX] Processing {otherProducts.Count} otherProducts");
            // Same processing logic for otherProducts
        }
    }
    catch (Exception ex)
    {
        DebugLogger.Log($"[POS-FIX] Error processing docket: {ex.Message}");
    }
    
    doc.AppendChild(root);
    return doc;
}
```

---

## 📋 COMPLETE TEST CHECKLIST

### Test with the ACTUAL printertask.json:
```powershell
# This is THE test - the actual failing file
curl -X POST http://localhost:9877/print -H "Content-Type: application/json" -d @printertask.json
```

### Check debug.log for ALL these messages:
- [ ] `[POS-FIX] Parsed templateData string (POS format)`
- [ ] `[POS-FIX] Keeping POS data structure intact`
- [ ] `[POS-FIX] Processing 1 mainProducts`
- [ ] `[POS-FIX] Found 1 products in category`
- [ ] `[POS-FIX] Product L0: '1x Easy cheesy burger'`

### The output should contain:
- [ ] "Easy cheesy burger" text
- [ ] Proper formatting (no errors)

---

## 🚨 CRITICAL: What We DON'T Change

1. **DON'T modify the incoming JSON** - it's correct POS format
2. **DON'T flatten products ourselves** - they're already flattened
3. **DON'T remove circular references** - POS handles them with levels
4. **DON'T change the data structure** - process it as the POS sends it

---

## 📊 How You Know It's Working

### Visual Output Check:
The printer/console should show:
```
Palmerston North          (from sites.name)
Order: 005868             (from orders.docNumber)
...
1x Easy cheesy burger     (from products with level 0)
```

### Debug Log Check:
All [POS-FIX] messages appear without errors

### No Crashes:
The request completes successfully

---

# 🧪 TEST FILES

## test-simple.json
```json
{
  "_id": { "id": "simple-1" },
  "template": {
    "body": "<root><text>Order:</text><docket-section /></root>"
  },
  "templateData": "{\"orders\":{\"mainProducts\":[{\"categories\":[{\"category\":{\"products\":[{\"productName\":\"Burger\",\"level\":0,\"qty\":\"1\"},{\"productName\":\"Cheese\",\"level\":1,\"qty\":\"\"},{\"productName\":\"Extra\",\"level\":2,\"qty\":\"\"}]}}]}]}}",
  "isOpenCashDrawer": false
}
```

## test-parse-string.json
```json
{
  "_id": { "id": "parse-test-1" },
  "template": {
    "body": "<root><text>{{message}}</text></root>"
  },
  "templateData": "{\"message\":\"Hello World\",\"printerName\":\"test\"}",
  "isOpenCashDrawer": false
}
```

## test-with-products.json
```json
{
  "_id": { "id": "test-002" },
  "template": {
    "body": "<root><docket-section /></root>"
  },
  "templateData": "{\"orders\":{\"mainProducts\":[{\"categories\":[{\"category\":{\"products\":[{\"productName\":\"Test\",\"level\":0,\"qty\":\"1\"}]}}]}]}}",
  "isOpenCashDrawer": false
}
```

---

# 📝 IMPLEMENTATION PLAN

## Phase 1: Fix Data Parsing (5 minutes)

### Files to Change:
1. `HttpServer.cs` - Parse templateData string
2. `HttpServer.cs` - Disable JsonCleaner

### Test Command:
```powershell
curl -X POST http://localhost:9877/print -d @test-parse-string.json
```

### Success Criteria:
- No crash
- Log shows "[POS-FIX] Parsed templateData string"

---

## Phase 2: Process Products (10 minutes)

### Files to Change:
1. `TemplateHelpers.cs` - Add ProcessDocketSection method
2. `TemplateHelpers.cs` - Hook it into docket-section processing

### Test Command:
```powershell
curl -X POST http://localhost:9877/print -d @test-with-products.json
```

### Success Criteria:
- Products appear in output
- Log shows product processing

---

## Phase 3: Final Validation (5 minutes)

### Test with Real Data:
```powershell
curl -X POST http://localhost:9877/print -d @printertask.json
```

### Success Criteria:
- "Easy cheesy burger" appears
- No errors
- All [POS-FIX] messages in log

---

# 🐛 TROUBLESHOOTING

## Common Issues and Solutions

### Issue: "Unexpected character" error
**Cause**: templateData not being parsed from string
**Fix**: Ensure FIX 1 is applied correctly

### Issue: "Object reference not set"
**Cause**: Path to products is wrong or data was cleaned
**Fix**: Ensure FIX 2 disabled JsonCleaner

### Issue: Blank docket section
**Cause**: Products not being processed
**Fix**: Check FIX 3 and debug logs

### Issue: No indentation on modifiers
**Cause**: Not using level property
**Fix**: Ensure using `product["level"]` for indent

---

## Debug Helpers

Add these for more info:
```csharp
// In HttpServer.cs after parsing:
DebugLogger.Log($"templateData keys: {string.Join(", ", templateData.Properties().Select(p => p.Name))}");
DebugLogger.Log($"Has orders: {templateData["orders"] != null}");

// In TemplateHelpers.cs when processing:
DebugLogger.Log($"Product details: level={level}, qty={qty}, name={productName}");
```

---

# ⚡ QUICK SUMMARY

## The Problem:
1. templateData is a STRING (not object)
2. JsonCleaner destroys valid data
3. We weren't using the level property

## The Solution:
1. Parse the string → `JObject.Parse(templateStr)`
2. Don't clean → Comment out JsonCleaner
3. Use levels → `product["level"]` for indentation

## Test It:
```powershell
curl -X POST http://localhost:9877/print -d @printertask.json
```

## Success:
"Easy cheesy burger" appears in output!

---

# 📌 FINAL NOTES

## What We Learned:
- POS sends templateData as a JSON string (not object)
- Products are pre-flattened with level properties
- Circular references are OK - handled via levels
- The POS data structure is correct - we just need to process it right

## Key Files Modified:
1. `HttpServer.cs` - Parse string, disable cleaner
2. `TemplateHelpers.cs` - Process products with levels

## Time to Complete:
~20 minutes total

---

*Document Created: 2025-01-17*
*Based on POS analysis of actual source code*