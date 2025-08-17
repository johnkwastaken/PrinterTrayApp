using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using PrinterTrayApp.Models;
using System.Security.Cryptography;
using System.Text;

namespace PrinterTrayApp.Services;

public static class TemplateHelpers
{
    public static int GetCharactersPerLine(PrinterPaperWidth paperWidth, PrinterFontFamily fontFamily, PrinterScale fontSize)
    {
        int charactersPerLine = 48; // Default for 80mm
        
        // Base character counts per paper width and font family
        if (paperWidth == PrinterPaperWidth.Paper_58)
        {
            charactersPerLine = fontFamily switch
            {
                PrinterFontFamily.FontA => 35,
                PrinterFontFamily.FontB => 42,
                PrinterFontFamily.FontC => 46,
                _ => 35
            };
        }
        else if (paperWidth == PrinterPaperWidth.Paper_80)
        {
            charactersPerLine = fontFamily switch
            {
                PrinterFontFamily.FontA => 48,
                PrinterFontFamily.FontB => 64,
                PrinterFontFamily.FontC => 64,
                _ => 48
            };
        }
        
        // Adjust for font size
        return fontSize switch
        {
            PrinterScale.Wide or PrinterScale.WideHigh => charactersPerLine / 2,
            _ => charactersPerLine
        };
    }
    
    public static string AlignString(string input, PrinterAlign align, int length, char fill = ' ')
    {
        if (input.Length >= length)
            return input.Substring(0, length);
            
        int fillerCount = length - input.Length;
        string filler = new string(fill, fillerCount);
        
        return align switch
        {
            PrinterAlign.Left => input + filler,
            PrinterAlign.Right => filler + input,
            PrinterAlign.Center => new string(fill, fillerCount / 2) + input + new string(fill, (fillerCount + 1) / 2),
            _ => input + filler
        };
    }
    private static readonly Regex TokenRegex = new Regex(@"\{\{([^}]+)\}\}", RegexOptions.Compiled);
    
    private static bool HasTokens(XmlNode node)
    {
        // Check if node or its children have token patterns
        if (node.InnerText != null && TokenRegex.IsMatch(node.InnerText))
            return true;
            
        if (node.Attributes != null)
        {
            foreach (XmlAttribute attr in node.Attributes)
            {
                if (TokenRegex.IsMatch(attr.Value))
                    return true;
            }
        }
        
        return false;
    }
    
    public static XmlDocument RenderTemplate(string templateString, object templateData, PrinterPaperWidth paperWidth = PrinterPaperWidth.Paper_80)
    {
        // Parse template XML
        var doc = new XmlDocument();
        doc.LoadXml(templateString);
        
        // Convert template data to JObject for easy path access
        JObject jsonData;
        if (templateData is string jsonString)
        {
            // It's already a JSON string, just parse it
            jsonData = JObject.Parse(jsonString);
        }
        else
        {
            // For any other object type, serialize to JSON first
            jsonData = JObject.FromObject(templateData);
        }
        
        // CRITICAL FIX: Remove circular references that cause "Can not add JObject to JObject" error
        // DISABLED: POS data has circular refs but uses level property to handle them
        // RemoveCircularReferences(jsonData);
        DebugLogger.Log("[POS-FIX] Not removing circular references - POS uses level property");
        
        // Process the document
        ProcessNode(doc.DocumentElement, jsonData, paperWidth);
        
        return doc;
    }
    
    private static void RemoveCircularReferences(JObject data)
    {
        DebugLogger.Log("[TemplateHelpers] Starting RemoveCircularReferences");
        
        // Remove nested category objects from products to prevent circular references
        var mainProducts = data.SelectToken("orders.mainProducts") as JArray;
        if (mainProducts != null)
        {
            DebugLogger.Log($"[TemplateHelpers] Found mainProducts with {mainProducts.Count} items");
            foreach (var product in mainProducts)
            {
                if (product is JObject productObj)
                {
                    var categories = productObj.SelectToken("categories") as JArray;
                    if (categories != null)
                    {
                        DebugLogger.Log($"[TemplateHelpers] Found categories array with {categories.Count} items");
                        foreach (var catItem in categories)
                        {
                            var category = catItem.SelectToken("category");
                            if (category is JObject catObj)
                            {
                                // Remove the products array from category to break circular reference
                                if (catObj.ContainsKey("products"))
                                {
                                    DebugLogger.Log("[TemplateHelpers] Removing products from category in RemoveCircularReferences");
                                    catObj.Remove("products");
                                }
                            }
                        }
                    }
                }
            }
        }
        
        DebugLogger.Log("[TemplateHelpers] RemoveCircularReferences complete");
    }
    
    private static void ProcessNode(XmlNode node, JObject data, PrinterPaperWidth paperWidth)
    {
        if (node == null) return;
        
        // Process custom sections first
        ProcessCustomSections(node, data, paperWidth);
        
        // Process iterate attribute if present
        ProcessIterateAttribute(node, data);
        
        // Check if this element should be removed due to empty tokens
        bool shouldRemoveElement = false;
        
        // Replace tokens in text nodes
        if (node.NodeType == XmlNodeType.Text)
        {
            var text = node.Value;
            if (text != null && TokenRegex.IsMatch(text))
            {
                var (newText, hasEmptyToken) = ReplaceTokensWithCheck(text, data);
                if (hasEmptyToken)
                {
                    // Mark parent element for removal if it contains empty tokens
                    shouldRemoveElement = true;
                }
                else
                {
                    node.Value = newText;
                }
            }
        }
        
        // Replace tokens in attributes
        if (node.Attributes != null)
        {
            foreach (XmlAttribute attr in node.Attributes)
            {
                if (TokenRegex.IsMatch(attr.Value))
                {
                    var (newValue, hasEmptyToken) = ReplaceTokensWithCheck(attr.Value, data);
                    if (hasEmptyToken)
                    {
                        shouldRemoveElement = true;
                    }
                    else
                    {
                        attr.Value = newValue;
                    }
                }
            }
        }
        
        // Remove element if it contains empty tokens
        if (shouldRemoveElement && node.ParentNode != null && node.NodeType == XmlNodeType.Text)
        {
            var elementToRemove = node.ParentNode;
            elementToRemove.ParentNode?.RemoveChild(elementToRemove);
            return;
        }
        
        // Process child nodes
        var childNodes = node.ChildNodes.Cast<XmlNode>().ToList();
        foreach (var child in childNodes)
        {
            ProcessNode(child, data, paperWidth);
        }
    }
    
    private static void ProcessCustomSections(XmlNode node, JObject data, PrinterPaperWidth paperWidth)
    {
        var nodesToProcess = node.SelectNodes("//*[contains(name(), '-section')]")?.Cast<XmlNode>().ToList();
        if (nodesToProcess == null) return;
        
        foreach (var sectionNode in nodesToProcess)
        {
            var sectionName = sectionNode.Name;
            
            switch (sectionName)
            {
                case "receipt-section":
                    ProcessReceiptSection(sectionNode, data, paperWidth);
                    break;
                    
                case "docket-section":
                    ProcessDocketSection(sectionNode, data, paperWidth);
                    break;
                    
                case "provider-report-section":
                    ProcessProviderReportSection(sectionNode, data, paperWidth);
                    break;
                    
                default:
                    // For unknown sections, just unwrap the content
                    UnwrapSection(sectionNode);
                    break;
            }
        }
    }
    
    private static void ProcessReceiptSection(XmlNode section, JObject data, PrinterPaperWidth paperWidth)
    {
        var doc = section.OwnerDocument;
        var parent = section.ParentNode;
        var replacementNodes = new List<XmlNode>();
        
        // Add header if specified
        var headerTitles = section.Attributes?["header-titles"]?.Value;
        if (!string.IsNullOrEmpty(headerTitles))
        {
            var separator = doc.CreateElement("separator");
            separator.SetAttribute("char", "-");
            replacementNodes.Add(separator);
            
            var header = doc.CreateElement("text");
            header.SetAttribute("font-style", section.Attributes?["header-font-style"]?.Value ?? "b");
            header.InnerText = headerTitles.Replace(",", "    ");
            replacementNodes.Add(header);
        }
        
        // Process order lines
        var orderLines = data["orderLines"] as JArray;
        if (orderLines != null)
        {
            foreach (var line in orderLines)
            {
                var itemText = doc.CreateElement("table");
                itemText.SetAttribute("columns-width", "3,1");
                itemText.SetAttribute("columns-align", "left,right");
                
                var tr = doc.CreateElement("tr");
                var tdName = doc.CreateElement("td");
                tdName.InnerText = line["item"]?["printName"]?.ToString() ?? line["item"]?["name"]?.ToString() ?? "";
                
                var tdPrice = doc.CreateElement("td");
                tdPrice.InnerText = FormatCurrency(line["totalPrice"]?.Value<decimal>() ?? 0);
                
                tr.AppendChild(tdName);
                tr.AppendChild(tdPrice);
                itemText.AppendChild(tr);
                
                replacementNodes.Add(itemText);
                
                // Add modifiers if any
                var modifiers = line["modifiers"] as JArray;
                if (modifiers != null && modifiers.Count > 0)
                {
                    foreach (var modifier in modifiers)
                    {
                        var modText = doc.CreateElement("text");
                        modText.SetAttribute("margin", "2");
                        modText.InnerText = $"  + {modifier["name"]}";
                        replacementNodes.Add(modText);
                    }
                }
            }
        }
        
        // Add totals
        AddTotalLine(doc, replacementNodes, "Subtotal", data["subtotal"], section);
        AddTotalLine(doc, replacementNodes, "Tax", data["tax"], section);
        AddTotalLine(doc, replacementNodes, "Total", data["total"], section, true);
        
        // Replace section with generated content
        foreach (var node in replacementNodes)
        {
            parent?.InsertBefore(node, section);
        }
        parent?.RemoveChild(section);
    }
    
    private static void ProcessDocketSection(XmlNode section, JObject data, PrinterPaperWidth paperWidth)
    {
        var doc = section.OwnerDocument;
        var parent = section.ParentNode;
        var replacementNodes = new List<XmlNode>();
        
        // Log POS data structure
        DebugLogger.Log($"[POS-FIX] Processing docket-section");
        DebugLogger.Log($"[POS-FIX] Data has 'orders': {data["orders"] != null}");
        if (data["orders"] != null)
        {
            DebugLogger.Log($"[POS-FIX] orders.mainProducts exists: {data["orders"]["mainProducts"] != null}");
            DebugLogger.Log($"[POS-FIX] orders.otherProducts exists: {data["orders"]["otherProducts"] != null}");
        }
        
        // FIRST: Check if this is a simple template (has child elements with tokens, not complex product data)
        var hasSimpleChildren = section.ChildNodes.Cast<XmlNode>()
            .Any(child => child.NodeType == XmlNodeType.Element && 
                         (HasTokens(child) || child.Attributes?["iterate"] != null));
        
        if (hasSimpleChildren)
        {
            // Simple template - just process iterate and tokens normally, then unwrap
            ProcessIterateElements(section, data);
            UnwrapSection(section);
            return;
        }
        
        // COMPLEX: Process as full docket with product hierarchy and styling
        
        // Get attributes for product formatting
        var productScale = section.Attributes?["product-scale"]?.Value ?? "6";
        var productStyle = section.Attributes?["product-style"]?.Value ?? "normal";
        var productColor = section.Attributes?["product-color"]?.Value ?? "color_1";
        var productNameField = section.Attributes?["product-name-field"]?.Value ?? "printName";
        
        // Get attributes for modifier formatting
        var modifierScale = section.Attributes?["modifier-scale"]?.Value ?? "6";
        var modifierStyle = section.Attributes?["modifier-style"]?.Value ?? "normal";
        var modifierColor = section.Attributes?["modifier-color"]?.Value ?? "color_1";
        var modifierNameField = section.Attributes?["modifier-name-field"]?.Value ?? "printName";
        
        // Get attributes for category/course headers
        var categoryHeaderHidden = section.Attributes?["category-header-hidden"]?.Value == "true";
        var categoryScale = section.Attributes?["category-scale"]?.Value ?? "6";
        var categoryStyle = section.Attributes?["category-style"]?.Value ?? "normal";
        var categoryNameField = section.Attributes?["category-name-field"]?.Value ?? "name";
        
        var courseHeaderHidden = section.Attributes?["course-header-hidden"]?.Value == "true";
        var courseScale = section.Attributes?["course-scale"]?.Value ?? "6";
        var courseStyle = section.Attributes?["course-style"]?.Value ?? "normal";
        var courseNameField = section.Attributes?["course-name-field"]?.Value ?? "name";
        
        // Get note attributes
        var noteScale = section.Attributes?["note-scale"]?.Value ?? "6";
        var noteStyle = section.Attributes?["note-style"]?.Value ?? "normal";
        var noteColor = section.Attributes?["note-color"]?.Value ?? "color_1";
        
        // Try different data structures - POS may send products in different locations
        
        // First try orders.mainProducts / orders.otherProducts (template data structure)
        var mainProducts = data["orders"]?["mainProducts"] as JArray;
        var otherProducts = data["orders"]?["otherProducts"] as JArray;
        
        // If not found, try root level mainGroupProduct / otherGroupProduct (direct print task)
        if (mainProducts == null)
            mainProducts = data["mainGroupProduct"] as JArray;
        if (otherProducts == null)
            otherProducts = data["otherGroupProduct"] as JArray;
            
        // Also check for printerTaskProduct at root level
        var printerTaskProducts = data["printerTaskProduct"] as JArray;
        
        // Process main products
        if (mainProducts != null && mainProducts.Count > 0)
        {
            DebugLogger.Log($"[POS-FIX] Found {mainProducts.Count} mainProducts");
            // Convert simple product format to expected structure if needed
            var processedMain = ConvertToProductStructure(mainProducts);
            ProcessDocketProducts(doc, processedMain, replacementNodes,
                productScale, productStyle, productColor, productNameField,
                modifierScale, modifierStyle, modifierColor, modifierNameField,
                categoryHeaderHidden, categoryScale, categoryStyle, categoryNameField,
                courseHeaderHidden, courseScale, courseStyle, courseNameField,
                noteScale, noteStyle, noteColor);
        }
        else
        {
            DebugLogger.Log($"[POS-FIX] No mainProducts found");
        }
        
        // Process other products
        if (otherProducts != null && otherProducts.Count > 0)
        {
            var processedOther = ConvertToProductStructure(otherProducts);
            ProcessDocketProducts(doc, processedOther, replacementNodes,
                productScale, productStyle, productColor, productNameField,
                modifierScale, modifierStyle, modifierColor, modifierNameField,
                categoryHeaderHidden, categoryScale, categoryStyle, categoryNameField,
                courseHeaderHidden, courseScale, courseStyle, courseNameField,
                noteScale, noteStyle, noteColor);
        }
        
        // Process printer task products if no other products were found
        if (replacementNodes.Count == 0 && printerTaskProducts != null && printerTaskProducts.Count > 0)
        {
            var processedPrinter = ConvertToProductStructure(printerTaskProducts);
            ProcessDocketProducts(doc, processedPrinter, replacementNodes,
                productScale, productStyle, productColor, productNameField,
                modifierScale, modifierStyle, modifierColor, modifierNameField,
                categoryHeaderHidden, categoryScale, categoryStyle, categoryNameField,
                courseHeaderHidden, courseScale, courseStyle, courseNameField,
                noteScale, noteStyle, noteColor);
        }
        
        // If no products were processed, just unwrap the section
        if (replacementNodes.Count == 0)
        {
            UnwrapSection(section);
            return;
        }
        
        // Replace section with processed nodes
        foreach (var node in replacementNodes)
        {
            parent?.InsertBefore(node, section);
        }
        
        parent?.RemoveChild(section);
    }
    
    private static JArray ConvertToProductStructure(JArray products)
    {
        // Check if this is already in the expected structure (has categories/courses)
        if (products.Count > 0)
        {
            var first = products[0];
            if (first["categories"] != null || first["courses"] != null)
            {
                // Apply sorting and aggregation to existing structure
                return SortAndAggregateProducts(products);
            }
                
            // Check if it's a flat product list (like mainGroupProduct or printerTaskProduct)
            if (first["printName"] != null || first["productName"] != null)
            {
                // Sort products by name first (matching POS behavior)
                var sortedProducts = SortProductsByName(products);
                
                // Convert flat list to category structure
                var result = new JArray();
                var categoryGroup = new JObject();
                var categories = new JArray();
                var categoryItem = new JObject();
                var category = new JObject();
                
                // Get category name from first product if available
                var categoryName = first["category"]?["name"]?.ToString() ?? "Items";
                category["name"] = categoryName;
                category["order"] = first["category"]?["order"]?.Value<int>() ?? -1;
                category["products"] = sortedProducts;
                
                categoryItem["category"] = category;
                categories.Add(categoryItem);
                categoryGroup["categories"] = categories;
                result.Add(categoryGroup);
                
                return result;
            }
        }
        
        return products;
    }
    
    private static JArray SortProductsByName(JArray products)
    {
        // Sort products by printName (matching POS behavior)
        var sortedList = products.ToList().OrderBy(p => 
        {
            var printName = p["printName"]?.ToString() ?? p["productName"]?.ToString() ?? "";
            return printName;
        }, StringComparer.CurrentCultureIgnoreCase).ToList();
        
        var result = new JArray();
        foreach (var product in sortedList)
        {
            result.Add(product);
        }
        return result;
    }
    
    private static JArray CompressProducts(JArray products)
    {
        // Group products by productId and aggregate quantities (matching POS compression logic)
        var compressed = new Dictionary<string, JObject>();
        
        foreach (var product in products)
        {
            var productId = product["productId"]?.ToString();
            var printName = product["printName"]?.ToString() ?? product["productName"]?.ToString() ?? "";
            
            if (string.IsNullOrEmpty(productId))
            {
                // No productId, add as-is
                compressed[Guid.NewGuid().ToString()] = (JObject)product;
                continue;
            }
            
            if (compressed.ContainsKey(productId))
            {
                // Aggregate quantities for same product
                var existing = compressed[productId];
                var existingQty = existing["qty"]?.Value<int>() ?? existing["unitQty"]?.Value<int>() ?? 1;
                var currentQty = product["qty"]?.Value<int>() ?? product["unitQty"]?.Value<int>() ?? 1;
                
                existing["qty"] = (existingQty + currentQty).ToString();
                existing["unitQty"] = existingQty + currentQty;
                
                // Aggregate prices if available
                var existingSum = existing["sum"]?.Value<decimal>() ?? 0;
                var currentSum = product["sum"]?.Value<decimal>() ?? 0;
                if (currentSum > 0)
                {
                    existing["sum"] = existingSum + currentSum;
                }
            }
            else
            {
                // First occurrence of this product
                compressed[productId] = (JObject)product.DeepClone();
            }
        }
        
        // Sort compressed products by printName
        var sortedCompressed = compressed.Values.OrderBy(p =>
        {
            var printName = p["printName"]?.ToString() ?? p["productName"]?.ToString() ?? "";
            return printName;
        }, StringComparer.CurrentCultureIgnoreCase);
        
        var result = new JArray();
        foreach (var product in sortedCompressed)
        {
            result.Add(product);
        }
        return result;
    }
    
    private static JArray SortAndAggregateProducts(JArray productsData)
    {
        var result = new JArray();
        
        foreach (var productGroup in productsData)
        {
            var processedGroup = new JObject();
            
            // Process courses if present
            var courses = productGroup["courses"] as JArray;
            if (courses != null)
            {
                var sortedCourses = SortCourses(courses);
                processedGroup["courses"] = sortedCourses;
            }
            
            // Process categories if present
            var categories = productGroup["categories"] as JArray;
            if (categories != null)
            {
                var sortedCategories = SortCategories(categories);
                processedGroup["categories"] = sortedCategories;
            }
            
            result.Add(processedGroup);
        }
        
        return result;
    }
    
    private static JArray SortCategories(JArray categories)
    {
        // Sort categories by order field (matching POS behavior)
        var sortedList = categories.ToList().OrderBy(c =>
        {
            var category = c["category"];
            var order = category?["order"]?.Value<int>() ?? -1;
            // Categories with order -1 go to the end
            return order == -1 ? int.MaxValue : order;
        }).ToList();
        
        var result = new JArray();
        foreach (var categoryItem in sortedList)
        {
            // Safely create a copy without using constructor
            JObject processedCategory;
            try
            {
                // Try to use DeepClone first
                processedCategory = (JObject)categoryItem.DeepClone();
            }
            catch
            {
                // If DeepClone fails, manually copy properties
                processedCategory = new JObject();
                var catObj = categoryItem as JObject;
                if (catObj != null)
                {
                    foreach (var prop in catObj.Properties())
                {
                    try
                    {
                        processedCategory[prop.Name] = prop.Value.DeepClone();
                    }
                    catch
                    {
                        // Skip properties that can't be cloned
                        DebugLogger.Log($"[TemplateHelpers] Skipping property {prop.Name} in SortCategories");
                    }
                }
                }
            }
            
            // Sort products within each category
            var category = processedCategory["category"];
            if (category != null && category["products"] is JArray products)
            {
                var sortedProducts = SortProductsByName(products);
                ((JObject)category)["products"] = sortedProducts;
            }
            
            result.Add(processedCategory);
        }
        
        return result;
    }
    
    private static JArray SortCourses(JArray courses)
    {
        // Sort courses by order field (matching POS behavior)
        var sortedList = courses.ToList().OrderBy(c =>
        {
            var course = c["course"];
            var order = course?["order"]?.Value<int>() ?? -1;
            // Courses with order -1 go to the end
            return order == -1 ? int.MaxValue : order;
        }).ToList();
        
        var result = new JArray();
        foreach (var courseItem in sortedList)
        {
            // Safely create a copy without using constructor
            JObject processedCourse;
            try
            {
                processedCourse = (JObject)courseItem.DeepClone();
            }
            catch
            {
                processedCourse = new JObject();
                var courseObj = courseItem as JObject;
                if (courseObj != null)
                {
                    foreach (var prop in courseObj.Properties())
                {
                    try
                    {
                        processedCourse[prop.Name] = prop.Value.DeepClone();
                    }
                    catch
                    {
                        DebugLogger.Log($"[TemplateHelpers] Skipping property {prop.Name} in SortCourses");
                    }
                }
                }
            }
            
            // Sort products within each course
            var course = processedCourse["course"];
            if (course != null && course["products"] is JArray products)
            {
                var sortedProducts = SortProductsByName(products);
                ((JObject)course)["products"] = sortedProducts;
            }
            
            // Sort categories within each course
            if (course != null && course["categories"] is JArray categories)
            {
                var sortedCategories = SortCategories(categories);
                ((JObject)course)["categories"] = sortedCategories;
            }
            
            result.Add(processedCourse);
        }
        
        return result;
    }
    
    private static void ProcessDocketProducts(XmlDocument doc, JArray? productsData, List<XmlNode> nodes,
        string productScale, string productStyle, string productColor, string productNameField,
        string modifierScale, string modifierStyle, string modifierColor, string modifierNameField,
        bool categoryHeaderHidden, string categoryScale, string categoryStyle, string categoryNameField,
        bool courseHeaderHidden, string courseScale, string courseStyle, string courseNameField,
        string noteScale, string noteStyle, string noteColor)
    {
        if (productsData == null) return;
        
        foreach (var productGroup in productsData)
        {
            // Process courses if present
            var courses = productGroup["courses"] as JArray;
            if (courses != null)
            {
                foreach (var courseItem in courses)
                {
                    var course = courseItem["course"];
                    if (course != null)
                    {
                        var courseProducts = course["products"] as JArray;
                        if (courseProducts != null && courseProducts.Count > 0)
                        {
                            // Add course header if not hidden
                            if (!courseHeaderHidden)
                            {
                                var courseName = course[courseNameField]?.ToString();
                                if (!string.IsNullOrEmpty(courseName))
                                {
                                    var courseText = doc.CreateElement("text");
                                    courseText.SetAttribute("align", "center");
                                    var (fontFamily, fontSize) = GetScaleAttributes(courseScale);
                                    courseText.SetAttribute("font-family", fontFamily);
                                    courseText.SetAttribute("size", fontSize);
                                    courseText.SetAttribute("font-style", courseStyle);
                                    courseText.InnerText = courseName;
                                    nodes.Add(courseText);
                                }
                            }
                            
                            // Process products in course
                            ProcessProductList(doc, courseProducts, nodes,
                                productScale, productStyle, productColor, productNameField,
                                modifierScale, modifierStyle, modifierColor, modifierNameField,
                                noteScale, noteStyle, noteColor);
                        }
                    }
                }
            }
            
            // Process categories if present
            var categories = productGroup["categories"] as JArray;
            if (categories != null)
            {
                // Add blank line between sections if needed
                if (nodes.Count > 0 && categories.Any(c => c["category"]?["products"] != null))
                {
                    var blank = doc.CreateElement("blank");
                    blank.SetAttribute("lines", "1");
                    nodes.Add(blank);
                }
                
                foreach (var categoryItem in categories)
                {
                    var category = categoryItem["category"];
                    if (category != null)
                    {
                        var categoryProducts = category["products"] as JArray;
                        if (categoryProducts != null && categoryProducts.Count > 0)
                        {
                            // Add category header if not hidden
                            if (!categoryHeaderHidden)
                            {
                                var categoryName = category[categoryNameField]?.ToString();
                                if (!string.IsNullOrEmpty(categoryName))
                                {
                                    var categoryText = doc.CreateElement("text");
                                    categoryText.SetAttribute("align", "center");
                                    var (fontFamily, fontSize) = GetScaleAttributes(categoryScale);
                                    categoryText.SetAttribute("font-family", fontFamily);
                                    categoryText.SetAttribute("size", fontSize);
                                    categoryText.SetAttribute("font-style", categoryStyle);
                                    categoryText.InnerText = categoryName;
                                    nodes.Add(categoryText);
                                }
                            }
                            
                            // Process products in category
                            ProcessProductList(doc, categoryProducts, nodes,
                                productScale, productStyle, productColor, productNameField,
                                modifierScale, modifierStyle, modifierColor, modifierNameField,
                                noteScale, noteStyle, noteColor);
                        }
                    }
                }
            }
        }
    }
    
    private static void ProcessProductList(XmlDocument doc, JArray products, List<XmlNode> nodes,
        string productScale, string productStyle, string productColor, string productNameField,
        string modifierScale, string modifierStyle, string modifierColor, string modifierNameField,
        string noteScale, string noteStyle, string noteColor)
    {
        string pendingNotes = "";
        
        DebugLogger.Log($"[POS-FIX] ProcessProductList: Processing {products.Count} products");
        
        for (int i = 0; i < products.Count; i++)
        {
            var product = products[i];
            var level = product["level"]?.Value<int>() ?? 0;
            var qty = product["qty"]?.ToString();
            var symbol = product["symbol"]?.ToString() ?? "";
            var productName = product[productNameField]?.ToString() ?? product["productName"]?.ToString();
            
            DebugLogger.Log($"[POS-FIX] Product {i}: level={level}, qty={qty}, name={productName}");
            var notes = product["notes"]?.ToString();
            
            // POS Hiding Rules Implementation
            var hide = product["hide"]?.Value<bool>() ?? false;
            var finalUnitPrice = product["finalUnitPrice"]?.Value<decimal>() ?? product["unitPrice"]?.Value<decimal>() ?? 0;
            var hasChildren = product["children"] != null && ((JArray)product["children"]).Count > 0;
            var categorisation = product["categorisation"]?.ToString() ?? "";
            
            // Calculate skipPrinting based on POS logic
            var root = level == 0;
            var leaf = (finalUnitPrice == 0 && hide) ? false : !hasChildren;
            var rootOrLeaf = root || leaf;
            var skipPrinting = rootOrLeaf ? false : hide;
            
            // Hide modifier groups that are empty (matching POS logic)
            var hideModifierGroup = !hide && !hasChildren && categorisation == "MODIFIER_GROUP";
            
            // Skip this product if hiding rules apply
            if (skipPrinting || hideModifierGroup)
            {
                // Adjust level for hidden modifier groups
                if (skipPrinting && hideModifierGroup)
                {
                    level = Math.Max(level - 1, 0);
                }
                continue;
            }
            
            // Store notes for main products (level 0)
            if (level == 0 && !string.IsNullOrEmpty(notes))
            {
                pendingNotes = notes;
            }
            
            // Create product/modifier text
            if (!string.IsNullOrEmpty(productName))
            {
                var productText = doc.CreateElement("text");
                
                // Use modifier attributes for level > 0, product attributes for level 0
                if (level > 0)
                {
                    var (modFontFamily, modFontSize) = GetScaleAttributes(modifierScale);
                    productText.SetAttribute("font-family", modFontFamily);
                    productText.SetAttribute("size", modFontSize);
                    productText.SetAttribute("font-style", modifierStyle);
                    productText.SetAttribute("font-color", modifierColor);
                    productText.SetAttribute("margin", (level * 2).ToString());
                }
                else
                {
                    var (prodFontFamily, prodFontSize) = GetScaleAttributes(productScale);
                    productText.SetAttribute("font-family", prodFontFamily);
                    productText.SetAttribute("size", prodFontSize);
                    productText.SetAttribute("font-style", productStyle);
                    productText.SetAttribute("font-color", productColor);
                }
                
                // Build text content based on POS formatting rules
                var line = "";
                if (level > 0)
                {
                    line = new string(' ', level * 2);
                }
                
                // Show quantity only for level 0 or when qty > 1 (matching POS logic)
                if ((level == 0 || (!string.IsNullOrEmpty(qty) && qty != "1")) && !string.IsNullOrEmpty(qty) && qty != "0")
                {
                    line += $"{qty}{symbol} ";
                }
                line += productName;
                
                productText.InnerText = line;
                nodes.Add(productText);
            }
            
            // Add notes after main product and its modifiers
            bool isLastProduct = (i == products.Count - 1);
            bool nextIsMainProduct = !isLastProduct && (products[i + 1]["level"]?.Value<int>() ?? 0) == 0;
            
            if (!string.IsNullOrEmpty(pendingNotes) && (isLastProduct || nextIsMainProduct))
            {
                var noteText = doc.CreateElement("text");
                noteText.SetAttribute("align", "center");
                var (noteFontFamily, noteFontSize) = GetScaleAttributes(noteScale);
                noteText.SetAttribute("font-family", noteFontFamily);
                noteText.SetAttribute("size", noteFontSize);
                noteText.SetAttribute("font-style", noteStyle);
                noteText.SetAttribute("font-color", noteColor);
                noteText.InnerText = pendingNotes;
                nodes.Add(noteText);
                pendingNotes = "";
            }
        }
    }
    
    private static (string fontFamily, string fontSize) GetScaleAttributes(string scaleNumber)
    {
        // Match POS scaleToAttributes function exactly
        return scaleNumber switch
        {
            "1" => ("c", "normal"),  // Smallest (FontC + normal)
            "2" => ("b", "normal"),  // Small (FontB + normal)
            "3" => ("a", "normal"),  // Medium (FontA + normal)
            "4" => ("c", "wide-high"), // Large (FontC + wide-high)
            "5" => ("b", "wide-high"), // Larger (FontB + wide-high)
            "6" => ("a", "wide-high"), // Largest (FontA + wide-high)
            _ => ("a", "normal")
        };
    }
    
    private static string GetScaleFromNumber(string scaleNumber)
    {
        var (_, size) = GetScaleAttributes(scaleNumber);
        return size;
    }
    
    private static void ProcessProviderReportSection(XmlNode section, JObject data, PrinterPaperWidth paperWidth)
    {
        // Process payment provider reports
        UnwrapSection(section);
    }
    
    private static void UnwrapSection(XmlNode section)
    {
        // Move all child nodes to parent and remove section node
        var parent = section.ParentNode;
        var children = section.ChildNodes.Cast<XmlNode>().ToList();
        
        foreach (var child in children)
        {
            parent?.InsertBefore(child, section);
        }
        
        parent?.RemoveChild(section);
    }
    
    private static void AddTotalLine(XmlDocument doc, List<XmlNode> nodes, string label, JToken? value, XmlNode section, bool isBold = false)
    {
        if (value == null) return;
        
        var table = doc.CreateElement("table");
        table.SetAttribute("columns-width", "3,1");
        table.SetAttribute("columns-align", "left,right");
        
        var tr = doc.CreateElement("tr");
        if (isBold)
        {
            tr.SetAttribute("font-style", "b");
            tr.SetAttribute("size", "wide");
        }
        
        var tdLabel = doc.CreateElement("td");
        tdLabel.InnerText = label;
        
        var tdValue = doc.CreateElement("td");
        // Handle values that may already be formatted as strings with currency symbols
        if (value.Type == JTokenType.String)
        {
            tdValue.InnerText = value.ToString();
        }
        else
        {
            tdValue.InnerText = FormatCurrency(value.Value<decimal>());
        }
        
        tr.AppendChild(tdLabel);
        tr.AppendChild(tdValue);
        table.AppendChild(tr);
        
        nodes.Add(table);
    }
    
    private static (string text, bool hasEmptyToken) ReplaceTokensWithCheck(string text, JObject data)
    {
        bool hasEmptyToken = false;
        
        var result = TokenRegex.Replace(text, match =>
        {
            var path = match.Groups[1].Value;
            
            // Handle special tokens that always have values
            if (path == "dateOfPrinting")
                return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            
            if (path == "currencyId")
                return "AUD"; // Default currency
            
            // Look up in data
            var token = data.SelectToken(path);
            
            if (token == null || token.Type == JTokenType.Null)
            {
                hasEmptyToken = true;
                return ""; // Return empty string for missing tokens
            }
            
            // Check for empty string values
            if (token.Type == JTokenType.String)
            {
                var stringValue = token.Value<string>();
                if (string.IsNullOrWhiteSpace(stringValue))
                {
                    hasEmptyToken = true;
                    return "";
                }
                return stringValue;
            }
            
            // Format based on type
            if (token.Type == JTokenType.Date)
                return token.Value<DateTime>().ToString("yyyy-MM-dd HH:mm:ss");
            
            if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
            {
                var value = token.Value<decimal>();
                // Don't mark as empty for zero values in numeric fields
                if (path.Contains("price") || path.Contains("total") || path.Contains("amount"))
                    return FormatCurrency(value);
                return value.ToString();
            }
            
            return token.ToString();
        });
        
        return (result, hasEmptyToken);
    }
    
    private static string? ReplaceTokens(string text, JObject data)
    {
        var (result, hasEmptyToken) = ReplaceTokensWithCheck(text, data);
        return hasEmptyToken ? null : result;
    }
    
    private static string FormatCurrency(decimal value)
    {
        return value.ToString("F2");
    }
    
    private static void ProcessIterateElements(XmlNode node, JObject data)
    {
        var iterateNodes = node.SelectNodes("//*[@iterate]")?.Cast<XmlNode>().ToList();
        if (iterateNodes == null) return;
        
        foreach (var iterNode in iterateNodes)
        {
            ProcessIterateAttribute(iterNode, data);
        }
    }
    
    private static void ProcessIterateAttribute(XmlNode node, JObject data)
    {
        var iterateAttr = node.Attributes?["iterate"];
        if (iterateAttr == null) return;
        
        var iteratePath = iterateAttr.Value;
        var parent = node.ParentNode;
        
        // Get the array to iterate over
        JToken? arrayToken = null;
        
        // Handle nested paths like "mainGroupProduct[0].printerTaskProduct"
        if (iteratePath.Contains("[") && iteratePath.Contains("]"))
        {
            // Complex path with array index
            arrayToken = data.SelectToken(iteratePath);
        }
        else if (iteratePath.Contains("."))
        {
            // Simple nested path
            arrayToken = data.SelectToken(iteratePath);
        }
        else
        {
            // Direct path or check in mainGroupProduct
            arrayToken = data[iteratePath];
            if (arrayToken == null && data["mainGroupProduct"] is JArray mainGroup && mainGroup.Count > 0)
            {
                // Try to find in first mainGroupProduct item
                arrayToken = mainGroup[0][iteratePath];
            }
        }
        
        if (arrayToken is JArray array && array.Count > 0)
        {
            // Remove the iterate attribute
            node.Attributes?.Remove(iterateAttr);
            
            // Create a copy for each item in the array
            var insertPoint = node;
            foreach (var item in array)
            {
                var clonedNode = node.CloneNode(true);
                
                // Replace tokens in the cloned node with values from the current item
                ReplaceTokensInNode(clonedNode, item as JObject ?? new JObject());
                
                // Insert the cloned node
                parent?.InsertBefore(clonedNode, insertPoint);
            }
            
            // Remove the original template node
            parent?.RemoveChild(node);
        }
    }
    
    private static void ReplaceTokensInNode(XmlNode node, JObject itemData)
    {
        if (node.NodeType == XmlNodeType.Text && node.Value != null)
        {
            node.Value = ReplaceItemTokens(node.Value, itemData);
        }
        
        if (node.Attributes != null)
        {
            foreach (XmlAttribute attr in node.Attributes)
            {
                attr.Value = ReplaceItemTokens(attr.Value, itemData);
            }
        }
        
        foreach (XmlNode child in node.ChildNodes)
        {
            ReplaceTokensInNode(child, itemData);
        }
    }
    
    private static string ReplaceItemTokens(string text, JObject itemData)
    {
        return TokenRegex.Replace(text, match =>
        {
            var path = match.Groups[1].Value;
            
            // Look up in item data
            var token = itemData.SelectToken(path);
            
            if (token == null || token.Type == JTokenType.Null)
            {
                return "";
            }
            
            if (token.Type == JTokenType.String)
            {
                return token.Value<string>() ?? "";
            }
            
            if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
            {
                return token.ToString();
            }
            
            return token.ToString();
        });
    }
    
}