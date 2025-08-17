# POS Command Tracing - Complete Analysis

## Command Generation Flow in POS

### 1. Entry Point: makeCommands()
```typescript
// CommandBuilder.ts line 104
export const makeCommands = async (task: PrinterTask) => {
  // Step 1: Render template (replace sections, tokens)
  let renderedTemplate = renderTemplate(
    task.template.body,
    JSON.parse(task.templateData),
    paperWidth
  );
  
  // Step 2: Convert XML to commands
  commands = makeCommandsFromElement(
    renderedTemplate.documentElement,
    paperWidth
  );
  
  // Step 3: Join with newlines
  return commands.join(POS80Commands.nextLine); // "\x0A"
}
```

## 2. Command Generation Per Element Type

### For Regular `<text>` Elements

#### Input XML:
```xml
<text font-family="a" size="wide" align="center">RECEIPT</text>
```

#### Processing in makeCommandFromElement():
```typescript
case NodeName.TextNode: {
  return createTextCommand({
    fontColor: element.getAttribute("font-color"),    // null → Color_1
    fontFamily: element.getAttribute("font-family"),  // "a" → FontA
    fontStyle: element.getAttribute("font-style"),    // null → Normal
    size: element.getAttribute("size"),               // "wide" → Wide
    align: element.getAttribute("align"),             // "center" → Center
    text: element.textContent                         // "RECEIPT"
  });
}
```

#### createTextCommand() builds:
```typescript
const commands = [
  POS80Commands.fontColor[Color_1],     // "\x1B\x72\x00"
  POS80Commands.size[Wide],             // "\x1B\x21\x20"
  POS80Commands.fontFamily[FontA],      // "\x1B\x4D\x00"
  POS80Commands.align[Center],          // "\x1B\x61\x01"
  POS80Commands.fontStyle[Normal],      // "\x1B\x45\x00"
];
commands.push(POS80Commands.lineSpace(0));  // "\x1B\x32"
commands.push(POS80Commands.text("RECEIPT")); // "RECEIPT"
commands.push(POS80Commands.lineSpace(0));  // "\x1B\x32"
```

#### Final Command String:
```
\x1B\x72\x00\x1B\x21\x20\x1B\x4D\x00\x1B\x61\x01\x1B\x45\x00\x1B\x32RECEIPT\x1B\x32
```

### For Docket-Section Generated Text

#### Original:
```xml
<docket-section product-scale="6" product-style="normal" />
```

#### After renderDocketSection():
```xml
<text font-family="a" size="wide-high" font-style="normal">1x Burger</text>
```

#### Processing:
```typescript
// Same as above but with:
size: "wide-high" → POS80Commands.size[WideHigh] → "\x1B\x21\x30"
```

#### Final Command:
```
\x1B\x72\x00\x1B\x21\x30\x1B\x4D\x00\x1B\x61\x00\x1B\x45\x00\x1B\x321x Burger\x1B\x32
```

## 3. Critical Observation: Command Order

The POS generates commands in this EXACT order for EVERY text element:
1. **Font Color** - `\x1B\x72\x00`
2. **Size** - `\x1B\x21\x30` (for wide-high)
3. **Font Family** - `\x1B\x4D\x00`
4. **Alignment** - `\x1B\x61\x00`
5. **Font Style** - `\x1B\x45\x00`
6. **Line Space** - `\x1B\x32`
7. **Text** - actual text content
8. **Line Space Reset** - `\x1B\x32`

## 4. What Our App Is Doing

Looking at our debug output:
```
[STYLE-DEBUG] Adding size command for WideHigh: <ESC>!0
```

Our CommandBuilder.cs generates:
```csharp
if (size != PrinterScale.Normal)
    _commands.Append(POS80Commands.size[size]);  // Correct: \x1B\x21\x30
```

The command IS correct! But let's check the COMPLETE command sequence.

## 5. The Real Issue - Command Sequence Differences

### POS Always Sends (even for defaults):
```
Color + Size + Font + Align + Style + LineSpace + Text + LineSpace
```

### Our App Only Sends Non-Defaults:
```csharp
if (size != PrinterScale.Normal)  // Only if not normal
    _commands.Append(POS80Commands.size[size]);
```

## 6. Another Critical Difference - No Reset!

### POS (line 174-176):
```typescript
commands.push(POS80Commands.lineSpace(attributes.lineSpace ?? 0));
commands.push(POS80Commands.text(attributes.text));
commands.push(POS80Commands.lineSpace(0));
// NO RESET AFTER TEXT!
```

### Our App (CommandBuilder.cs):
```csharp
// Add text
_commands.Append(text);

// Reset formatting - THIS IS WRONG!
if (size != PrinterScale.Normal)
    _commands.Append(POS80Commands.size[PrinterScale.Normal]);
```

## 7. The Line Feed Issue

### POS lineFeed command is complex:
```typescript
lineFeed: (lines) => "\x1B\x4D\x00\x1B\x21\x00\x1B\x46 ".repeat(lines)
```
This resets font AND size!

### Our simple line feed:
```csharp
_commands.Append("\n");
```

## 8. The Real Problem

**We're resetting the formatting after each text element!**

When we print:
1. Set wide-high: `\x1B\x21\x30`
2. Print text: "1x Burger"
3. **Reset to normal: `\x1B\x21\x00`** ← THIS CANCELS THE STYLING!
4. Add newline: `\n`

The printer sees the reset command BEFORE it prints, canceling the style!

## 9. Fix Required

### Remove the resets in CommandBuilder.cs:
```csharp
// DON'T RESET after text!
// Remove these lines:
if (style != PrinterFontStyle.Normal)
    _commands.Append(POS80Commands.fontStyle[PrinterFontStyle.Normal]);
if (size != PrinterScale.Normal)
    _commands.Append(POS80Commands.size[PrinterScale.Normal]);
```

### Always send all commands (like POS):
```csharp
// Always send all formatting commands
_commands.Append(POS80Commands.fontColor[fontColor]);
_commands.Append(POS80Commands.size[size]);
_commands.Append(POS80Commands.fontFamily[fontFamily]);
_commands.Append(POS80Commands.align[align]);
_commands.Append(POS80Commands.fontStyle[fontStyle]);
```

## Summary

The styling IS being generated correctly, but:
1. **We reset formatting after each text** (POS doesn't)
2. **We only send non-default commands** (POS sends all)
3. **We use simple newlines** (POS uses complex lineFeed)

The fix is to match POS behavior exactly:
- Send ALL formatting commands for each text
- DON'T reset after text
- Use proper line spacing commands

---
*Analysis Date: 2025-01-17*