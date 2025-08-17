using System;
using System.Text;
using System.Xml;
using Newtonsoft.Json.Linq;

namespace PrinterTrayApp.Services
{
    public static class SimpleTemplateProcessor
    {
        public static string ProcessDocketSection(XmlNode docketSection, JObject data)
        {
            var result = new StringBuilder();
            
            // Get the product data directly
            var mainProducts = data.SelectToken("orders.mainProducts") as JArray;
            if (mainProducts == null || mainProducts.Count == 0)
            {
                DebugLogger.Log("[SimpleTemplateProcessor] No mainProducts found");
                return "";
            }
            
            DebugLogger.Log($"[SimpleTemplateProcessor] Found {mainProducts.Count} products");
            
            // Process each product simply and directly
            foreach (var product in mainProducts)
            {
                // Extract product info
                var productName = product["productName"]?.ToString() ?? 
                                product["printName"]?.ToString() ?? 
                                "Unknown Product";
                var qty = product["qty"]?.ToString() ?? "1";
                var price = product["price"]?.ToString() ?? "";
                
                DebugLogger.Log($"[SimpleTemplateProcessor] Processing product: {productName}, qty: {qty}");
                
                // Check for categories
                var categories = product["categories"] as JArray;
                if (categories != null && categories.Count > 0)
                {
                    foreach (var catItem in categories)
                    {
                        var category = catItem["category"];
                        if (category != null)
                        {
                            var categoryName = category["name"]?.ToString() ?? "Items";
                            
                            // Add category header
                            result.AppendLine($"=== {categoryName} ===");
                            
                            // Check for products in category
                            var categoryProducts = category["products"] as JArray;
                            if (categoryProducts != null)
                            {
                                foreach (var catProduct in categoryProducts)
                                {
                                    var catProductName = catProduct["productName"]?.ToString() ?? 
                                                       catProduct["printName"]?.ToString() ?? "";
                                    var catQty = catProduct["qty"]?.ToString() ?? "1";
                                    
                                    if (!string.IsNullOrEmpty(catProductName))
                                    {
                                        result.AppendLine($"{catQty}x {catProductName}");
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    // No categories, just print the product
                    result.AppendLine($"{qty}x {productName}");
                }
            }
            
            return result.ToString();
        }
        
        public static XmlDocument RenderTemplateSimple(string templateXml, string templateDataJson)
        {
            DebugLogger.Log("[SimpleTemplateProcessor] Starting simple render");
            
            // Parse the template
            var doc = new XmlDocument();
            doc.LoadXml(templateXml);
            
            // Parse the data
            var data = JObject.Parse(templateDataJson);
            
            // Find docket-section
            var docketSections = doc.GetElementsByTagName("docket-section");
            foreach (XmlNode section in docketSections)
            {
                var parent = section.ParentNode;
                if (parent == null) continue;
                
                // Get the product text
                var productText = ProcessDocketSection(section, data);
                
                if (!string.IsNullOrEmpty(productText))
                {
                    // Create text nodes for each line
                    var lines = productText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var textNode = doc.CreateElement("text");
                        textNode.InnerText = line;
                        
                        // Add basic formatting based on content
                        if (line.StartsWith("==="))
                        {
                            textNode.SetAttribute("align", "center");
                            textNode.SetAttribute("font-style", "bold");
                        }
                        else if (line.Contains("x "))
                        {
                            textNode.SetAttribute("font-size", "normal");
                        }
                        
                        parent.InsertBefore(textNode, section);
                    }
                }
                
                // Remove the docket-section node
                parent.RemoveChild(section);
            }
            
            // Process simple token replacements
            ProcessTokens(doc.DocumentElement, data);
            
            DebugLogger.Log($"[SimpleTemplateProcessor] Render complete. XML length: {doc.OuterXml.Length}");
            
            return doc;
        }
        
        private static void ProcessTokens(XmlNode node, JObject data)
        {
            if (node.NodeType == XmlNodeType.Text)
            {
                var text = node.Value;
                if (text != null && text.Contains("{{"))
                {
                    // Simple token replacement
                    var start = 0;
                    while ((start = text.IndexOf("{{", start)) != -1)
                    {
                        var end = text.IndexOf("}}", start);
                        if (end == -1) break;
                        
                        var token = text.Substring(start + 2, end - start - 2).Trim();
                        var value = data.SelectToken(token)?.ToString() ?? "";
                        
                        text = text.Substring(0, start) + value + text.Substring(end + 2);
                    }
                    node.Value = text;
                }
            }
            
            // Process attributes
            if (node.Attributes != null)
            {
                foreach (XmlAttribute attr in node.Attributes)
                {
                    if (attr.Value.Contains("{{"))
                    {
                        var text = attr.Value;
                        var start = 0;
                        while ((start = text.IndexOf("{{", start)) != -1)
                        {
                            var end = text.IndexOf("}}", start);
                            if (end == -1) break;
                            
                            var token = text.Substring(start + 2, end - start - 2).Trim();
                            var value = data.SelectToken(token)?.ToString() ?? "";
                            
                            text = text.Substring(0, start) + value + text.Substring(end + 2);
                        }
                        attr.Value = text;
                    }
                }
            }
            
            // Process children
            foreach (XmlNode child in node.ChildNodes)
            {
                ProcessTokens(child, data);
            }
        }
    }
}