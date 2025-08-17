# POS Complete Styling Flow Analysis

## Overview
The POS has THREE different styling paths depending on the element type:
1. **Regular `<text>` elements** - Attributes are preserved from template
2. **Special sections** (`<docket-section>`, `<receipt-section>`) - Replaced with generated text elements
3. **Generated text from sections** - Use section's attributes, NOT data attributes

## 1. Regular Text Elements
For normal `<text>` elements in the template, styling works as expected:

### Template XML:
```xml
<text font-family="a" font-invert="1" align="center">{{sites.name}}</text>
<text font-style="normal" align="left">Table: {{orders.tableNumber}}</text>
<text size="high" font-style="b" align="left">{{orders.docNotes}}</text>
```

### Processing Flow:
```
1. renderTemplate() in TemplateHelpers.ts
   - Parses XML with DOMParser
   - Finds {{tokens}} in text content
   - Replaces tokens with data values
   - KEEPS ALL ATTRIBUTES AS-IS
   - Removes element if text becomes empty

2. CommandBuilder.makeCommandFromElement()
   - Reads attributes directly from element:
     - font-family → element.getAttribute("font-family")
     - size → element.getAttribute("size")
     - align → element.getAttribute("align")
     - font-style → element.getAttribute("font-style")
   
3. createTextCommand()
   - Generates ESC/POS commands based on attributes
```

**Result: Text elements display with their defined styling ✅**

## 2. Special Section Elements

### Docket-Section Processing
The `<docket-section>` is completely replaced during rendering:

#### Original:
```xml
<docket-section 
  product-scale="6" 
  product-style="normal"
  modifier-scale="6"
  modifier-style="normal"
  category-scale="6"
  category-style="normal"
/>
```

#### After renderDocketSection():
```xml
<text font-family="a" size="wide-high" font-style="normal" align="center">Burgers</text>
<text font-family="a" size="wide-high" font-style="normal">1x Easy cheesy burger</text>
<text font-family="a" size="wide-high" font-style="normal">  2x Cheese</text>
```

### Key Points:
1. **Section attributes control ALL generated text**
2. **Product data attributes are IGNORED**
3. **Scale conversion applies** (scale="6" → size="wide-high")

## 3. The Scale-to-Attributes Mapping

```typescript
function scaleToAttributes(scale: string) {
  switch(scale) {
    case "1": return { "font-family": "c", size: "normal" };
    case "2": return { "font-family": "b", size: "normal" };
    case "3": return { "font-family": "a", size: "normal" };
    case "4": return { "font-family": "c", size: "wide-high" };
    case "5": return { "font-family": "b", size: "wide-high" };
    default:  return { "font-family": "a", size: "wide-high" }; // 6+
  }
}
```

## 4. Complete Flow Example

### Input Template:
```xml
<root>
  <text size="wide" align="center">{{header}}</text>
  <docket-section product-scale="6" />
</root>
```

### With Data:
```json
{
  "header": "RECEIPT",
  "orders": {
    "mainProducts": [{
      "categories": [{
        "category": { "name": "Burgers" },
        "products": [{
          "productName": "Burger",
          "fontSize": "wide-high",  // IGNORED!
          "fontStyle": "bold",       // IGNORED!
          "level": 0
        }]
      }]
    }]
  }
}
```

### Step-by-Step Processing:

#### Step 1: Token Replacement (renderTemplate)
```xml
<root>
  <text size="wide" align="center">RECEIPT</text>  <!-- Token replaced -->
  <docket-section product-scale="6" />              <!-- Not processed yet -->
</root>
```

#### Step 2: Section Replacement (renderDocketSection)
```xml
<root>
  <text size="wide" align="center">RECEIPT</text>
  <!-- docket-section replaced with: -->
  <text font-family="a" size="wide-high" font-style="normal" align="center">Burgers</text>
  <text font-family="a" size="wide-high" font-style="normal">1x Burger</text>
</root>
```

#### Step 3: Command Generation (CommandBuilder)
For each text element:
- Read attributes from XML element
- Generate ESC/POS commands
- Apply formatting before text
- Reset formatting after text

### Final ESC/POS:
```
<ESC>!  RECEIPT<ESC>!<0x00><LF>           // wide + center
<ESC>!0<ESC>a<0x01>Burgers<ESC>!<0x00><ESC>a<0x00><LF>  // wide-high + center
<ESC>!01x Burger<ESC>!<0x00><LF>          // wide-high
```

## 5. Why Styling "Doesn't Work"

### Our Current Implementation (WRONG):
```csharp
// We're using product attributes directly
var fontSize = product["fontSize"];  // "wide-high" from data
var fontStyle = product["fontStyle"]; // "bold" from data
productText.SetAttribute("size", fontSize);
productText.SetAttribute("font-style", fontStyle);
```

### What POS Actually Does (CORRECT):
```typescript
// POS uses docket-section attributes for ALL products
const productAttributes = {
  ...scaleToAttributes(node.getAttribute("product-scale")),  // scale="6" → size="wide-high"
  "font-style": node.getAttribute("product-style"),          // "normal" from template
};

// Apply same attributes to ALL products
createTextNode(productAttributes, productLine);
```

## 6. Critical Findings

### ✅ What Works:
1. Regular `<text>` elements with explicit attributes
2. Token replacement in text content
3. ESC/POS command generation from attributes

### ❌ What's Different:
1. **We use product data attributes** (fontSize, fontStyle from JSON)
2. **POS uses template section attributes** (product-scale, product-style from XML)
3. **We don't implement scale-to-attributes conversion**

## 7. Implementation Fix Required

### Current TemplateHelpers.cs:
```csharp
// WRONG - Using product's own attributes
productText.SetAttribute("font-family", product["fontFamily"]);
productText.SetAttribute("size", product["fontSize"]);
```

### Should Be:
```csharp
// CORRECT - Using docket-section's attributes
var productScale = docketSection.GetAttribute("product-scale") ?? "6";
var (fontFamily, fontSize) = GetScaleAttributes(productScale);
var fontStyle = docketSection.GetAttribute("product-style") ?? "normal";

// Apply to ALL products uniformly
productText.SetAttribute("font-family", fontFamily);
productText.SetAttribute("size", fontSize);
productText.SetAttribute("font-style", fontStyle);
```

## Summary

The POS styling system:
1. **Preserves** attributes on regular `<text>` elements
2. **Replaces** special sections with generated text
3. **Uses template attributes** for generated text, NOT data attributes
4. **Applies scale conversion** for size attributes
5. **Ignores** product font properties in the data

Our fix must:
- Stop using product font attributes from data
- Start using docket-section attributes from template
- Implement scale-to-attributes conversion
- Apply template styling uniformly to all generated products

---
*Analysis completed: 2025-01-17*