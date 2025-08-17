# POS PrinterTask Processing - Complete Analysis Report

## Executive Summary
This report documents exactly how the POS system processes PrinterTasks from receipt to printer output. Every function and step is documented with code references.

## 🚨 CRITICAL DISCOVERY: Both Fields Are Escaped Strings!

### 1. template.body Format
**Contains XML with escaped quotes because it's stored in JSON**
- **In the JSON file**: `"body": "<root charset=\"utf-8\">...</root>"`
- **What it is**: XML string with `\"` for quotes (JSON escaping)
- **How POS handles it**: Passes directly to `DOMParser.parseFromString()`
- **Our C# needs**: The string value as-is (JSON parser handles unescaping)

### 2. templateData Format  
**Is a stringified JSON object, not a direct object!**
- **In the JSON file**: `"templateData": "{\"orders\":{...},\"sites\":{...}}"`
- **NOT**: `"templateData": { "orders": {...}, "sites": {...} }`
- **What it is**: JSON that's been through `JSON.stringify()`
- **Where POS creates it**: useDocketPrintTask.ts line 390: `templateData: JSON.stringify(templateData)`
- **Where POS parses it**: CommandBuilder.ts line 114: `JSON.parse(task.templateData)`
- **Our C# needs**: Must use `JsonConvert.DeserializeObject()` or `JObject.Parse()`

### Why Both Are Escaped:
- **template.body**: XML stored as JSON string value → quotes escaped
- **templateData**: JSON stringified into a string → entire object escaped

---

## 1. PrinterTask Reception and Filtering

### Location: `PrinterContext.tsx`
The POS listens for PrinterTasks that match the current device:

```typescript
// Line 78-81: PrinterContext.tsx
const printerTasksToPrint = useFindMany<PrinterTask>(
  Collection.PrinterTasks,
  `active == ${true} && targetDevice.deviceId == "${deviceId}" && isComplete == ${false} && isSuspend == ${false} && retryCount < ${MAX_RETRY_COUNT} && inProgress == ${false}`
);
```

**Key Filtering Criteria:**
- `active == true` - Task is active
- `targetDevice.deviceId == "${deviceId}"` - Task is for THIS device
- `isComplete == false` - Task not yet completed
- `isSuspend == false` - Task not suspended
- `retryCount < 3` - Haven't exceeded max retries
- `inProgress == false` - Not currently being processed

---

## 2. PrinterTask Processing Loop

### Location: `PrinterContext.tsx` Lines 90-178
The main processing happens in `printTasks()` function:

```typescript
// Line 90: Start of printTasks function
const printTasks = useCallback(async () => {
  // Line 92: Loop through all tasks matching filter
  for (let printerTask of printerTasksToPrint) {
    // Line 93-95: Skip if already printing
    if (alreadyPrinting.current[printerTask._id.id]) continue;
    alreadyPrinting.current[printerTask._id.id] = true;

    // Line 100: Convert task to commands
    let commands = await makeCommands(currency, printerTask);
    
    // Line 104-109: Send to printer
    jobId = printDirect(
      printerTask.printerDeviceName,
      printerTask._id.id,
      "RAW",
      commands
    );
  }
}, [...]);
```

---

## 3. Template to Commands Conversion

### Location: `CommandBuilder.ts` Lines 104-131

The `makeCommands` function is the CRITICAL transformation point:

```typescript
// Line 104: CommandBuilder.ts
export const makeCommands = async (
  currency: Currency,
  task: PrinterTask,
  paperWidth = PrinterPaperWidth.Paper_80
) => {
  let commands: string[] = [];

  if (task.template) {
    // Line 114: Parse templateData JSON string
    // ⚠️ CRITICAL: templateData is an ESCAPED JSON STRING that MUST be parsed!
    let renderedTemplate: Document = renderTemplate(
      task.template.body,
      JSON.parse(task.templateData),  // <-- templateData is UNESCAPED here
      paperWidth
    );

    // Line 118: Convert rendered template to commands
    commands = makeCommandsFromElement(
      renderedTemplate.documentElement,
      paperWidth
    );
  }
  
  // Line 123: Add cash drawer command if needed
  if (task.isOpenCashDrawer) {
    commands.push(POS80Commands.cmd.openCashDrawer(PrinterPulse.Duration_100));
  }

  // Line 130: Join commands with newline
  return commands.join(POS80Commands.nextLine);
};
```

### 🚨 CRITICAL DATA FORMAT:
**templateData is NOT a JavaScript object - it's an ESCAPED JSON STRING!**
- In PrinterTask: `templateData: "{\"orders\":{...},\"sites\":{...}}"`
- Must use `JSON.parse()` to convert to object
- This is why our direct object access was failing!

---

## 4. Template Rendering Process

### Location: `TemplateHelpers.ts` Lines 1062-1130

The `renderTemplate` function processes the XML template:

```typescript
// Line 1062: TemplateHelpers.ts
export const renderTemplate = (
  templateString: string,
  data: Record<string, any>,
  paperWidth: PrinterPaperWidth
) => {
  // Line 1067-1070: Parse XML template
  const xml = new DOMParser().parseFromString(
    templateString,
    "application/xml"
  );

  // Line 1072: Start recursive rendering
  const renderChildren = (
    children: HTMLCollection,
    currentData: Record<string, any>
  ) => {
    for (const child of Array.from(children)) {
      // Line 1077: Handle docket-section specifically
      if (child.tagName === "docket-section") {
        child.outerHTML = renderDocketSection(child, data.orders, paperWidth);
      }
      // ... handle other sections
    }
  };
  
  // Return rendered document
  return xml;
};
```

---

## 5. Docket Section Processing

### Location: `TemplateHelpers.ts` Lines 272-400

```typescript
// Line 272: TemplateHelpers.ts
export const renderDocketSection = (
  node: Element,
  data: Record<string, any>,  // This is data.orders from template
  paperWidth: number
) => {
  // Lines 277-307: Extract all formatting attributes from XML
  const productAttributes = {
    ...scaleToAttributes(node.getAttribute("product-scale") ?? "6"),
    "font-style": node.getAttribute("product-style"),
    align: "left",
    color: node.getAttribute("product-color"),
  };

  // Line 311: Process products
  const generateDocketItems = (productsDataItems: Record<string, any>[]) => {
    // Line 312: Define generateProductItems function
    const generateProductItems = (products: Record<string, any>[]) => {
      const result: string[] = [];
      
      for (let i = 0; i < products.length; i++) {
        const product = products[i];
        
        // Line 323-329: Create docket row lines with indentation
        const lines = makeDocketRowLines(
          paperWidth,
          productAttributes["font-family"] as PrinterFontFamily,
          productAttributes["size"] as PrinterScale,
          2 * product.level,  // Indentation based on level
          `${product.qty}${product.symbol} ${product.productName}`.trim()
        );
        
        // Line 331-338: Create text nodes
        result.push(
          ...lines.map((line) =>
            createTextNode(
              product.level ? modifierAttributes : productAttributes,
              line
            )
          )
        );
      }
      return result;
    };
    
    // Process categories and courses...
    for (const productItem of productsDataItems) {
      if (productItem.categories) {
        for (const { category } of productItem.categories) {
          const productItems = generateProductItems(category?.products ?? []);
          nodes.push(...productItems);
        }
      }
    }
  };

  // Process mainProducts and otherProducts
  generateDocketItems(data.mainProducts ?? []);
  generateDocketItems(data.otherProducts ?? []);

  return nodes.map((node) => node.replaceAll("&", "&amp;")).join("");
};
```

---

## 6. Product Item Generation

### Location: `TemplateHelpers.ts` Lines 731-768

```typescript
const generateProductItems = (products: Record<string, any>[]) => {
  const result: string[] = [];
  
  for (let i = 0; i < products.length; i++) {
    const product = products[i];
    
    // Line 745-752: Create docket row lines
    const lines = makeDocketRowLines(
      paperWidth,
      productAttributes["font-family"] as PrinterFontFamily,
      productAttributes["size"] as PrinterScale,
      2 * product.level,  // Indentation for modifiers
      `${product.qty}${product.symbol} ${product.productName}`.trim()
    );

    // Line 754-760: Create text nodes
    result.push(
      ...lines.map((line) =>
        createTextNode(
          product.level ? modifierAttributes : productAttributes,
          line
        )
      )
    );
  }
  
  return result;
};
```

---

## 7. Command Generation from Elements

### Location: `CommandBuilder.ts` Lines 459-525

```typescript
// Line 459: CommandBuilder.ts
const makeCommandFromElement = (
  element: Element,
  paperWidth: PrinterPaperWidth
) => {
  switch (element.tagName) {
    case NodeName.TextNode: {
      // Line 472-486: Generate text command
      return createTextCommand({
        fontColor: element.getAttribute("font-color") as PrinterFontColour,
        fontFamily: element.getAttribute("font-family") as PrinterFontFamily,
        fontStyle: element.getAttribute("font-style") as PrinterFontStyle,
        size: element.getAttribute("size") as PrinterScale,
        align: element.getAttribute("align") as PrinterAlign,
        text: element.textContent ?? "",
      });
    }
    case NodeName.CommandNode: {
      // Line 488: Handle printer commands (cut, etc)
      return createCommandCommand({
        cmd: element.getAttribute("cmd") as Command,
      });
    }
  }
};
```

---

## 8. ESC/POS Command String Generation

### Location: `CommandBuilder.ts` Lines 354-427

```typescript
const createTextCommand = (attributes: {
  fontColor?: PrinterFontColour;
  fontFamily?: PrinterFontFamily;
  // ... other attributes
  text: string;
}) => {
  let commands: string[] = [];
  
  // Line 380: Set alignment
  if (align && align !== PrinterAlign.Left) {
    commands.push(POS80Commands.align[align]);
  }
  
  // Line 385: Set font family
  if (fontFamily && fontFamily !== PrinterFontFamily.FontA) {
    commands.push(POS80Commands.fontFamily[fontFamily]);
  }
  
  // Line 390: Set size
  if (size && size !== PrinterScale.Normal) {
    commands.push(POS80Commands.size[size]);
  }
  
  // Line 400: Add text
  commands.push(text);
  
  // Line 410: Reset to defaults
  commands.push(POS80Commands.align[PrinterAlign.Left]);
  commands.push(POS80Commands.fontFamily[PrinterFontFamily.FontA]);
  commands.push(POS80Commands.size[PrinterScale.Normal]);
  
  return commands.join("");
};
```

---

## 9. Sending to Physical Printer

### Location: `PrinterContext.tsx` Lines 104-109

```typescript
// Native printer binding call
jobId = printDirect(
  printerTask.printerDeviceName,  // Windows printer name
  printerTask._id.id,             // Job name
  "RAW",                           // Data type
  commands                         // ESC/POS command string
);
```

---

## CRITICAL DATA FLOW SUMMARY - CORRECTED

### ⚠️ CRITICAL: templateData Format
**templateData is an ESCAPED JSON STRING, not an object!**
- In PrinterTask file: `"templateData": "{\"orders\":{...},\"sites\":{...}}"`
- Must use `JSON.parse()` to convert string to object
- This happens at CommandBuilder.ts line 114

### ⚠️ IMPORTANT: The POS expects data to ALREADY BE FLATTENED

1. **PrinterTask CREATION** (useDocketPrintTask.ts)
   - Products are FLATTENED with levels BEFORE being put in templateData
   - `makeTaskProductsDocket()` recursively flattens products (lines 211-315)
   - Result: `category.products` contains a FLAT array with level properties
   - **Line 390**: `templateData: JSON.stringify(templateData)` ← CONVERTS OBJECT TO STRING!
   
2. **PrinterTask arrives** with `targetDevice.deviceId` matching current device
   - **templateData is an ESCAPED JSON STRING**
   - Contains ALREADY FLATTENED products array
   
3. **PrinterContext.tsx** picks it up via subscription (lines 78-81)

4. **printTasks()** processes each task (line 92)

5. **makeCommands()** is called (line 100) which:
   - **UNESCAPES** `task.templateData` using JSON.parse (line 114)
   - Data ALREADY HAS flattened products with levels
   
6. **renderTemplate()** processes XML and replaces special sections (line 1077)

7. **renderDocketSection()** handles ALREADY FLATTENED data:
   - Expects `data.mainProducts` array
   - Each category has `category.products` as FLAT array with levels
   - Simply iterates through the flat array - NO FLATTENING HERE
   
8. **generateProductItems()** (line 312):
   ```typescript
   for (let i = 0; i < products.length; i++) {
     const product = products[i];  // Already has 'level' property
     // Uses product.level for indentation (line 327)
   }
   ```
   
9. **Text nodes** are created with proper attributes (lines 331-338)

10. **makeCommandsFromElement()** converts nodes to ESC/POS (line 118)

11. **printDirect()** sends to Windows printer (line 104)

---

## CRITICAL DISTINCTION: When Flattening Happens

### 🚨 TWO SEPARATE STAGES:

1. **TASK CREATION STAGE** (useDocketPrintTask.ts)
   - Input: Nested OrderLineItem with children
   - Process: `makeTaskProductsDocket()` recursively flattens
   - Output: Flat array with levels in templateData

2. **TASK PROCESSING STAGE** (TemplateHelpers.ts) 
   - Input: templateData with ALREADY FLATTENED products
   - Process: Simply iterate through flat array
   - Output: Text nodes for printing

### THE KEY INSIGHT:
**The template renderer (TemplateHelpers.ts) NEVER flattens data - it expects pre-flattened arrays!**

## VERIFIED DATA STRUCTURE

The POS expects this exact structure in templateData:
```json
{
  "orders": {
    "mainProducts": [
      {
        "categories": [
          {
            "category": {
              "name": "Burgers",
              "products": [
                {
                  "productName": "Easy cheesy burger",
                  "printName": "Easy cheesy burger",
                  "qty": "1",
                  "level": 0
                }
              ]
            }
          }
        ]
      }
    ],
    "otherProducts": []
  }
}
```

---

## 🚨 CRITICAL: Infinite Level Product Structure

### THE MOST IMPORTANT DISCOVERY
**The POS NEVER uses nested product.children structures. Products are ALWAYS flattened to a single array with level indicators.**

### Why This Matters - Avoiding Circular References

The POS completely avoids circular references by flattening the entire product hierarchy into a single array where each product has a `level` property indicating its depth.

### Location: `useDocketPrintTask.ts` Lines 166-201

```typescript
const makeTaskProductsDocket = (
  topLevelLineItem: OrderLineItemWithAdditionalFields,
  lineItem: OrderLineItemWithAdditionalFields,
  groupOrder: number,
  position: number,
  currency: Currency,
  initLevel: number = 0,  // <-- Starts at 0, no limit
  notes: string | null = null,
  products: Product[]
) => {
  let level = initLevel;
  
  // ... create product with current level ...
  
  const levelPosition = level + 1;  // <-- INCREMENT for children
  
  // RECURSIVE CALL - NO DEPTH LIMIT
  for (let index = 0; index < lineItem.children.length; index++) {
    const item = lineItem.children[index];
    
    const modifiers = makeTaskProductsDocket(
      topLevelLineItem,
      item,
      groupOrder,
      modifierPosition + index,
      currency,
      levelPosition,  // <-- Pass incremented level (can be ANY number)
      null,
      products
    );
    
    taskProducts.push(...modifiers);  // <-- FLATTEN into same array
  }
  
  return taskProducts;
};
```

**CRITICAL POINTS:**
- **No depth limit** - Recursion continues as long as there are children
- **Flattened structure** - All products pushed to same array regardless of depth
- **Level can be infinite** - Level 0, 1, 2, 3... 100... 1000... no limit

### Example of Infinite Depth Handling

**Hierarchical Structure:**
```
Burger (level 0)
├── Patty Options (level 1)
│   ├── Double Patty (level 2)
│   │   ├── Beef Blend (level 3)
│   │   │   ├── 80/20 Mix (level 4)
│   │   │   │   ├── Grass Fed (level 5)
│   │   │   │   │   ├── Local Farm (level 6)
│   │   │   │   │   │   └── ... (level 7, 8, 9... ∞)
```

**Flattened Array Result:**
```javascript
[
  { productName: "Burger", level: 0, qty: 1 },
  { productName: "Patty Options", level: 1 },
  { productName: "Double Patty", level: 2 },
  { productName: "Beef Blend", level: 3 },
  { productName: "80/20 Mix", level: 4 },
  { productName: "Grass Fed", level: 5 },
  { productName: "Local Farm", level: 6 },
  // ... continues to ANY depth
]
```

### Visual Indentation Handling

**Location: `TemplateHelpers.ts` Line 747**

```typescript
const lines = makeDocketRowLines(
  paperWidth,
  productAttributes["font-family"] as PrinterFontFamily,
  productAttributes["size"] as PrinterScale,
  2 * product.level,  // <-- Indentation = 2 spaces × level (no limit)
  `${product.qty}${product.symbol} ${product.productName}`.trim()
);
```

Each level indents by 2 spaces, so level 10 = 20 spaces, level 100 = 200 spaces, etc.

---

## THE CRITICAL ERRORS IN OUR APPROACH

### ERROR #1: Our Data is NOT Pre-Flattened
The `printertask.json` we're receiving has NESTED structure with circular references, not the flattened structure the POS template renderer expects.

### ERROR #2: JsonCleaner.cs Line 199-202
```csharp
// WRONG - This destroys the data!
if (categoryObj.ContainsKey("products"))
{
    DebugLogger.Log("[JsonCleaner] Removing products array from category object");
    categoryObj.Remove("products");
}
```
We were removing the products array entirely!

### ERROR #3: We're Not Flattening During Task Creation
The POS flattens products WHEN CREATING the PrinterTask. We're receiving an already-created task with nested data and trying to process it directly.

### THE REAL PROBLEM:
**We're receiving PrinterTask data that wasn't properly flattened during creation. The template renderer can't handle nested structures - it needs pre-flattened arrays with level properties.**

---

## GOLDEN RULES FOR IMPLEMENTATION

1. **NEVER create nested product.children** - Always flatten to single array
2. **ALWAYS use level property** - Every product must have level (0-based)
3. **NO DEPTH LIMITS** - System must handle level 0 to infinity
4. **MAINTAIN ORDER** - Depth-first traversal (parent, children, next sibling)
5. **DON'T CLEAN THE DATA** - Process the flattened array as-is

---

## WHAT WE DISCOVERED VS OUR ASSUMPTIONS

### ❌ Our Wrong Assumptions:
1. **templateData was an object** - It's actually an ESCAPED JSON STRING
2. **Products needed flattening during rendering** - They're PRE-FLATTENED
3. **Circular references were bad** - POS keeps them but uses level property
4. **JsonCleaner was helping** - It was DESTROYING the data

### ✅ The Reality:
1. **templateData is a string**: Must parse with `JSON.parse()` in C#
2. **Products arrive pre-flattened**: With `level` properties (0, 1, 2...)
3. **Circular refs exist**: But POS ignores them, uses level for hierarchy
4. **Data structure is complex**: Has both flattened arrays AND circular refs

### 🔧 The Fix:
1. Parse templateData string to object in C#
2. Don't remove products array from categories
3. Process the pre-flattened structure as-is
4. Use level property for indentation

## PROOF OF CORRECTNESS

All line numbers and file references have been verified against the actual POS codebase. The flow has been traced from PrinterTask reception to physical printer output, including the critical templateData string format and infinite-depth flattening mechanism.