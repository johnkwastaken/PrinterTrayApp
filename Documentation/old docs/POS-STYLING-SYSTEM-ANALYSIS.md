# POS Styling System - Complete Analysis

## Executive Summary
The POS styling system has TWO separate and CONFLICTING styling mechanisms:
1. **Template-level styling** via docket-section attributes (ACTUALLY USED)
2. **Product-level styling** via product font properties (IGNORED)

## Key Discovery
**The product font attributes (fontSize, fontStyle, fontFamily) that are included in the PrinterTask JSON are NEVER USED by the POS template processor!**

## How POS Styling Actually Works

### 1. Template Defines All Styling
The `<docket-section>` element has attributes that control ALL product styling:

```xml
<docket-section 
  product-scale="6"        <!-- Controls main product font size -->
  product-style="normal"   <!-- Controls main product font style -->
  modifier-scale="6"       <!-- Controls modifier font size -->
  modifier-style="normal"  <!-- Controls modifier font style -->
/>
```

### 2. Scale-to-Attributes Conversion
The POS converts scale numbers to font combinations:

```typescript
const scaleToAttributes = (scale: string) => {
  if (scale === "1") return { "font-family": "c", size: "normal" };
  if (scale === "2") return { "font-family": "b", size: "normal" };
  if (scale === "3") return { "font-family": "a", size: "normal" };
  if (scale === "4") return { "font-family": "c", size: "wide-high" };
  if (scale === "5") return { "font-family": "b", size: "wide-high" };
  else return { "font-family": "a", size: "wide-high" };  // scale 6+
};
```

**Scale 6 = Font A + Wide-High (double width & height)**

### 3. Product Rendering Process

When rendering products in `renderDocketSection`:

```typescript
// Step 1: Get attributes from docket-section element
const productAttributes = {
  ...scaleToAttributes(node.getAttribute("product-scale") ?? "6"),
  "font-style": node.getAttribute("product-style"),
  align: "left",
  color: node.getAttribute("product-color"),
};

// Step 2: Apply to ALL products (ignoring product's own font properties)
createTextNode(
  product.level ? modifierAttributes : productAttributes,  // <-- Template attributes, NOT product attributes!
  line
)
```

### 4. Product Font Properties Are Created But Ignored

In `useDocketPrintTask.ts`, products are created with font properties:
```typescript
{
  fontStyle: PrinterFontStyle.Bold,
  fontSize: level === 0 ? PrinterScale.WideHigh : PrinterScale.High,
  fontFamily: PrinterFontFamily.FontA,
  fontColor: PrinterFontColour.Color_1,
  // ... BUT THESE ARE NEVER USED!
}
```

These properties are:
- ✅ Included in the PrinterTask JSON
- ✅ Sent to the printer app
- ❌ NEVER used by the template processor

## The Actual Styling Flow

```
1. Template XML
   <docket-section product-scale="6" />
         ↓
2. TemplateHelpers.renderDocketSection()
   - Reads product-scale="6" from element
   - Converts: scale 6 → font-family="a", size="wide-high"
         ↓
3. Creates Text Nodes
   <text font-family="a" size="wide-high">1x Product Name</text>
         ↓
4. CommandBuilder processes nodes
   - Reads attributes from text element
   - Generates ESC/POS commands
         ↓
5. ESC/POS Commands
   \x1B\x21\x30 (wide-high) + "1x Product Name"
```

## Why Our App Shows Products But No Styling

### What We're Doing Wrong
We're trying to use the product's font attributes directly:

```csharp
// We see: product.fontSize = "wide-high"
// We apply: <text size="wide-high">

// BUT the POS ignores these and uses template attributes!
```

### What We Should Do
We need to:
1. **Read the docket-section attributes** (product-scale, product-style, etc.)
2. **Convert scale to font attributes** using the same logic as POS
3. **Apply these template attributes to ALL products**, ignoring the product's own font properties

## Critical Implementation Fix

### Current (Wrong) Approach
```csharp
// Using product's font attributes
productText.SetAttribute("font-family", product.fontFamily);  // WRONG!
productText.SetAttribute("size", product.fontSize);           // WRONG!
```

### Correct Approach
```csharp
// Use docket-section attributes for ALL products
var productScale = section.GetAttribute("product-scale") ?? "6";
var (fontFamily, fontSize) = GetScaleAttributes(productScale);

// Apply to ALL level-0 products
if (product.level == 0) {
    productText.SetAttribute("font-family", fontFamily);  // From template!
    productText.SetAttribute("size", fontSize);           // From template!
}
```

## Evidence From POS Source

### TemplateHelpers.ts Line 334
```typescript
createTextNode(
  product.level ? modifierAttributes : productAttributes,  // <-- Uses template attrs
  line
)
```

### No Usage of Product Font Properties
```bash
grep "product\.font" TemplateHelpers.ts
# No matches - product font properties are never accessed!
```

## Conclusion

The POS has a **dual styling system** where:
1. Products carry font properties (fontSize, fontStyle, etc.) in the data
2. Templates define styling via docket-section attributes
3. **Only the template styling is actually used**

Our implementation must match this behavior by:
- Ignoring product font properties
- Using docket-section attributes for all styling
- Applying scale-to-attributes conversion correctly

## Action Items

1. ✅ Confirmed: ESC/POS commands are correct
2. ✅ Confirmed: Products are being rendered
3. ❌ **FIX NEEDED**: Use template attributes, not product attributes
4. ❌ **FIX NEEDED**: Implement scale-to-attributes conversion
5. ❌ **FIX NEEDED**: Apply template styling uniformly to all products

---
*Analysis Date: 2025-01-17*
*Critical Finding: Product font attributes are decorative only - template controls all styling*