using System;
using System.Drawing;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace PrinterTrayApp.Forms;

public class PrintTestForm : Form
{
    private TextBox _jsonInput = null!;
    private TextBox _responseOutput = null!;
    private Button _sendButton = null!;
    private Button _clearButton = null!;
    private Button _loadSampleButton = null!;
    private ComboBox _printerCombo = null!;
    private Label _statusLabel = null!;
    private readonly HttpClient _httpClient;
    
    public PrintTestForm()
    {
        _httpClient = new HttpClient();
        InitializeComponent();
        LoadPrinters();
    }
    
    private void InitializeComponent()
    {
        Text = "Print Test - JSON Input";
        Size = new Size(1000, 700);
        StartPosition = FormStartPosition.CenterScreen;
        
        // Main split container
        var splitContainer = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 400
        };
        
        // Top panel for JSON input
        var topPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
        
        var inputLabel = new Label
        {
            Text = "JSON Input (Paste PrinterTask JSON here):",
            Location = new Point(10, 10),
            Size = new Size(300, 20),
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };
        
        _jsonInput = new TextBox
        {
            Location = new Point(10, 35),
            Size = new Size(950, 300),
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            Font = new Font("Consolas", 10),
            WordWrap = false
        };
        
        // Button panel
        var buttonPanel = new Panel
        {
            Location = new Point(10, 340),
            Size = new Size(950, 60)
        };
        
        var printerLabel = new Label
        {
            Text = "Override Printer (optional):",
            Location = new Point(0, 0),
            Size = new Size(150, 20),
            Font = new Font("Segoe UI", 9)
        };
        
        _printerCombo = new ComboBox
        {
            Location = new Point(0, 20),
            Size = new Size(200, 30),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _printerCombo.Items.Add("(Use JSON printer)");
        _printerCombo.SelectedIndex = 0;
        
        _sendButton = new Button
        {
            Text = "Send to Printer",
            Location = new Point(210, 20),
            Size = new Size(120, 35),
            BackColor = Color.Green,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };
        _sendButton.Click += SendButton_Click;
        
        _clearButton = new Button
        {
            Text = "Clear All",
            Location = new Point(340, 20),
            Size = new Size(100, 35),
            BackColor = Color.DarkGray,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10)
        };
        _clearButton.Click += (s, e) => { _jsonInput.Clear(); _responseOutput.Clear(); };
        
        _loadSampleButton = new Button
        {
            Text = "Load Sample",
            Location = new Point(450, 20),
            Size = new Size(100, 35),
            BackColor = Color.Blue,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10)
        };
        _loadSampleButton.Click += LoadSampleButton_Click;
        
        buttonPanel.Controls.AddRange(new Control[] { printerLabel, _printerCombo, _sendButton, _clearButton, _loadSampleButton });
        
        topPanel.Controls.AddRange(new Control[] { inputLabel, _jsonInput, buttonPanel });
        
        // Bottom panel for response
        var bottomPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
        
        var responseLabel = new Label
        {
            Text = "Response:",
            Location = new Point(10, 10),
            Size = new Size(300, 20),
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };
        
        _responseOutput = new TextBox
        {
            Location = new Point(10, 35),
            Size = new Size(950, 180),
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            Font = new Font("Consolas", 10),
            ReadOnly = true,
            BackColor = Color.FromArgb(240, 240, 240)
        };
        
        bottomPanel.Controls.AddRange(new Control[] { responseLabel, _responseOutput });
        
        // Status bar
        _statusLabel = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 25,
            Text = "Ready",
            BorderStyle = BorderStyle.FixedSingle,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.LightGray
        };
        
        splitContainer.Panel1.Controls.Add(topPanel);
        splitContainer.Panel2.Controls.Add(bottomPanel);
        
        Controls.Add(splitContainer);
        Controls.Add(_statusLabel);
    }
    
    private async void LoadPrinters()
    {
        try
        {
            var response = await _httpClient.GetStringAsync("http://127.0.0.1:9877/printers");
            var printers = JsonDocument.Parse(response);
            
            _printerCombo.Items.Clear();
            _printerCombo.Items.Add("(Use JSON printer)");
            
            foreach (var printer in printers.RootElement.GetProperty("printers").EnumerateArray())
            {
                var name = printer.GetProperty("windowsPrinterName").GetString();
                if (!string.IsNullOrEmpty(name))
                {
                    _printerCombo.Items.Add(name);
                }
            }
            
            _printerCombo.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            // If can't load from API, still provide basic options
            _printerCombo.Items.Clear();
            _printerCombo.Items.Add("(Use JSON printer)");
            _printerCombo.Items.Add("passkitchen");
            _printerCombo.Items.Add("Microsoft Print to PDF");
            _printerCombo.SelectedIndex = 0;
            UpdateStatus($"Could not load printers: {ex.Message}", Color.Orange);
        }
    }
    
    private async void SendButton_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_jsonInput.Text))
        {
            MessageBox.Show("Please enter JSON data first", "No Input", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        
        try
        {
            // Just validate the JSON is parseable
            var jsonDoc = JsonDocument.Parse(_jsonInput.Text);
            
            UpdateStatus("Sending to printer...", Color.Yellow);
            _responseOutput.Clear();
            
            // Determine which printer to use for testing
            string? testPrinter = null;
            if (_printerCombo.SelectedIndex > 0)
            {
                testPrinter = _printerCombo.SelectedItem?.ToString();
                _responseOutput.AppendText($"Test Override: Send to '{testPrinter}' printer\r\n");
                _responseOutput.AppendText($"(Ignoring printer in JSON for testing)\r\n");
            }
            else
            {
                _responseOutput.AppendText($"Using printer from JSON data\r\n");
            }
            _responseOutput.AppendText("----------------------------------------\r\n");
            
            // Build the request - add test printer as query parameter if selected
            var url = "http://127.0.0.1:9877/print";
            if (!string.IsNullOrEmpty(testPrinter))
            {
                url += $"?testPrinter={Uri.EscapeDataString(testPrinter)}";
            }
            
            // Send the original JSON without modifications
            var content = new StringContent(_jsonInput.Text, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content);
            var responseText = await response.Content.ReadAsStringAsync();
            
            // Pretty print response
            var responseJson = JsonDocument.Parse(responseText);
            var prettyResponse = JsonSerializer.Serialize(responseJson, new JsonSerializerOptions { WriteIndented = true });
            
            _responseOutput.AppendText($"Status Code: {(int)response.StatusCode} {response.StatusCode}\r\n");
            _responseOutput.AppendText("----------------------------------------\r\n");
            _responseOutput.AppendText(prettyResponse);
            
            if (response.IsSuccessStatusCode)
            {
                var responseObj = JsonSerializer.Deserialize<JsonElement>(responseText);
                if (responseObj.TryGetProperty("success", out var success) && success.GetBoolean())
                {
                    UpdateStatus("✓ Print job sent successfully!", Color.Green);
                    
                    if (responseObj.TryGetProperty("jobNumber", out var jobNumber))
                    {
                        _responseOutput.AppendText($"\r\n\r\n✓ SUCCESS - Job Number: {jobNumber}");
                    }
                }
                else
                {
                    UpdateStatus("✗ Print job failed!", Color.Red);
                    if (responseObj.TryGetProperty("error", out var error))
                    {
                        _responseOutput.AppendText($"\r\n\r\n✗ ERROR: {error}");
                    }
                }
            }
            else
            {
                UpdateStatus($"✗ HTTP Error: {response.StatusCode}", Color.Red);
            }
        }
        catch (JsonException ex)
        {
            UpdateStatus("✗ Invalid JSON format!", Color.Red);
            _responseOutput.Text = $"JSON Error:\r\n{ex.Message}\r\n\r\nPlease check your JSON format.";
        }
        catch (HttpRequestException ex)
        {
            UpdateStatus("✗ Connection failed!", Color.Red);
            _responseOutput.Text = $"Connection Error:\r\n{ex.Message}\r\n\r\nMake sure PrinterTrayApp is running.";
        }
        catch (Exception ex)
        {
            UpdateStatus("✗ Unexpected error!", Color.Red);
            _responseOutput.Text = $"Error:\r\n{ex.GetType().Name}: {ex.Message}";
        }
    }
    
    private void LoadSampleButton_Click(object? sender, EventArgs e)
    {
        // Load clean POS-compliant sample with all data in templateData
        _jsonInput.Text = @"{
  ""_id"": {
    ""id"": ""test-pos-compliant-001"",
    ""siteId"": ""65e5e19dfa0e95ca09e4d953""
  },
  ""template"": {
    ""body"": ""<root charset=\""utf-8\"">\n    <text align=\""center\"" size=\""wide\"" font=\""bold\"">{{sites.name}}</text>\n    <text align=\""center\"">{{sites.addressLine1}}</text>\n    <text align=\""center\"">{{sites.addressLine2}}</text>\n    <separator char=\""-\"" />\n    \n    <text>Order #: {{orders.docNumber}}</text>\n    <text>Date: {{dateOfPrinting}}</text>\n    <text>Staff: {{staff.firstName}} {{staff.lastName}}</text>\n    <text>Register: {{registers.name}}</text>\n    <separator char=\""-\"" />\n    \n    <docket-section>\n        <text font=\""bold\"">{{name}}</text>\n        <text>  Qty: {{quantity}} @ ${{unitPrice}}</text>\n        <text>  Total: ${{totalAmount}}</text>\n    </docket-section>\n    \n    <separator char=\""=\"" />\n    <text align=\""right\"" size=\""wide\"">Total: {{currencies.currencySymbol}}{{orders.totalAmount}}</text>\n    <text align=\""right\"">Tax: {{currencies.currencySymbol}}{{orders.taxAmount}}</text>\n    \n    <blank lines=\""2\"" />\n    <text align=\""center\"">Thank you for your order!</text>\n    <text align=\""center\"">{{sites.phone}}</text>\n    \n    <blank lines=\""3\"" />\n    <command cmd=\""cut\"" />\n</root>"",
    ""name"": ""POS Compliant Receipt"",
    ""templateType"": ""Docket""
  },
  ""templateData"": ""{\""printerDeviceName\"":\""passkitchen\"",\""sites\"":{\""id\"":\""65e5e19dfa0e95ca09e4d953\"",\""name\"":\""Test Restaurant - Main Branch\"",\""addressLine1\"":\""123 Test Street\"",\""addressLine2\"":\""Auckland, NZ 1010\"",\""phone\"":\""+64 9 123 4567\"",\""email\"":\""test@restaurant.com\""},\""orders\"":{\""id\"":\""order-12345\"",\""docNumber\"":\""ORD-2025-001\"",\""orderType\"":\""Dine In\"",\""tableNumber\"":\""Table 5\"",\""totalAmount\"":87.50,\""taxAmount\"":11.38,\""subtotal\"":76.12,\""createdTime\"":{\""$date\"":1753238900139},\""mainProducts\"":[{\""name\"":\""Beef Burger\"",\""quantity\"":2,\""unitPrice\"":18.50,\""totalAmount\"":37.00,\""category\"":\""Mains\""},{\""name\"":\""French Fries\"",\""quantity\"":2,\""unitPrice\"":8.50,\""totalAmount\"":17.00,\""category\"":\""Sides\""},{\""name\"":\""Coca Cola\"",\""quantity\"":2,\""unitPrice\"":4.50,\""totalAmount\"":9.00,\""category\"":\""Beverages\""},{\""name\"":\""Chocolate Cake\"",\""quantity\"":1,\""unitPrice\"":12.50,\""totalAmount\"":12.50,\""category\"":\""Desserts\""}]},\""products\"":[{\""name\"":\""Beef Burger\"",\""quantity\"":2,\""unitPrice\"":18.50,\""totalAmount\"":37.00,\""category\"":\""Mains\""},{\""name\"":\""French Fries\"",\""quantity\"":2,\""unitPrice\"":8.50,\""totalAmount\"":17.00,\""category\"":\""Sides\""},{\""name\"":\""Coca Cola\"",\""quantity\"":2,\""unitPrice\"":4.50,\""totalAmount\"":9.00,\""category\"":\""Beverages\""},{\""name\"":\""Chocolate Cake\"",\""quantity\"":1,\""unitPrice\"":12.50,\""totalAmount\"":12.50,\""category\"":\""Desserts\""}],\""staff\"":{\""id\"":\""staff-001\"",\""firstName\"":\""John\"",\""lastName\"":\""Smith\"",\""role\"":\""Server\""},\""registers\"":{\""id\"":\""reg-001\"",\""name\"":\""Main Register\"",\""terminalId\"":\""TERM001\""},\""currencies\"":{\""currencySymbol\"":\""$\"",\""code\"":\""NZD\"",\""decimals\"":2},\""dateOfPrinting\"":\""2025-01-17 14:30:00\"",\""printerName\"":\""passkitchen\"",\""appVersion\"":\""1.0.0\""}"",
  ""isOpenCashDrawer"": false
}";
        UpdateStatus("Loaded POS compliant sample", Color.Green);
    }
    
    private void UpdateStatus(string message, Color color)
    {
        _statusLabel.Text = $" {DateTime.Now:HH:mm:ss} - {message}";
        _statusLabel.BackColor = color;
        _statusLabel.ForeColor = color.GetBrightness() < 0.5 ? Color.White : Color.Black;
    }
    
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _httpClient?.Dispose();
        }
        base.Dispose(disposing);
    }
}