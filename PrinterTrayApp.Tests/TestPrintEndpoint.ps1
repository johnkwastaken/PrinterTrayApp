# Test script for the /print endpoint with POS-formatted PrinterTask

# Sample PrinterTask matching POS structure
$printerTask = @{
    _id = @{
        id = "test-001"
        siteId = "site-001"
    }
    evicted = $false
    active = $true
    targetDevice = @{
        name = "Main POS"
        deviceId = "pos-001"
    }
    sourceDevice = @{
        name = "Kitchen Display"
        deviceId = "kds-001"
    }
    template = @{
        body = @"
<root charset="utf-8">
    <text align="center" size="wide">TEST RECEIPT</text>
    <separator char="=" />
    <text font-family="b">Date: {{dateOfPrinting}}</text>
    <blank lines="1" />
    <text>Item: {{item.name}}</text>
    <text>Price: ${{item.price}}</text>
    <separator char="-" />
    <text size="wide" font-style="b">Total: ${{total}}</text>
    <blank lines="2" />
    <text align="center">Thank you!</text>
    <blank lines="3" />
    <command cmd="cut" />
</root>
"@
        name = "TestReceipt"
        templateType = "Receipt"
    }
    templateData = (@{
        item = @{
            name = "Test Product"
            price = "9.99"
        }
        total = "9.99"
    } | ConvertTo-Json)
    currentPrinterId = "printer-001"
    printerName = "Test Printer"
    printerDeviceName = "Microsoft Print to PDF"  # Use a common Windows printer for testing
    secondPrinterIds = @()
    isOpenCashDrawer = $false
    isComplete = $false
    inProgress = $false
    retryCount = 0
    isSuspend = $false
    ipAddress = "127.0.0.1"
    appVersion = "1.0.0"
    templateVersion = "1.0.0"
    error = $null
    jobId = "job-001"
    name = "Test Print Job"
    registerName = "Register 1"
    printerLocation = "Counter"
}

# Convert to JSON
$json = $printerTask | ConvertTo-Json -Depth 10

Write-Host "Sending print job to API..." -ForegroundColor Cyan
Write-Host "Printer: $($printerTask.printerDeviceName)" -ForegroundColor Yellow
Write-Host ""

try {
    $response = Invoke-WebRequest -Uri "http://127.0.0.1:9877/print" `
        -Method POST `
        -Body $json `
        -ContentType "application/json" `
        -ErrorAction Stop
    
    Write-Host "Response Status: $($response.StatusCode)" -ForegroundColor Green
    Write-Host "Response Body:" -ForegroundColor Green
    $response.Content | ConvertFrom-Json | ConvertTo-Json -Depth 10
}
catch {
    Write-Host "Error occurred:" -ForegroundColor Red
    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $responseBody = $reader.ReadToEnd()
        Write-Host "Status: $($_.Exception.Response.StatusCode.value__)" -ForegroundColor Red
        Write-Host "Response: $responseBody" -ForegroundColor Red
    } else {
        Write-Host $_.Exception.Message -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "To test with a real thermal printer, change 'printerDeviceName' to your printer name" -ForegroundColor Cyan
Write-Host "To test cash drawer, set 'isOpenCashDrawer' to true" -ForegroundColor Cyan