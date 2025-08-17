# Interactive Print Test Script
# Allows pasting JSON directly for testing

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "   PRINTER TRAY APP - INTERACTIVE TEST  " -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check if server is running
try {
    $health = Invoke-RestMethod -Uri "http://127.0.0.1:9877/health" -Method GET -ErrorAction Stop
    Write-Host "✓ Server is running (v$($health.version))" -ForegroundColor Green
    Write-Host "✓ Available printers: $($health.printers -join ', ')" -ForegroundColor Green
    Write-Host ""
} catch {
    Write-Host "✗ Server is not running! Please start PrinterTrayApp first." -ForegroundColor Red
    exit 1
}

Write-Host "Instructions:" -ForegroundColor Yellow
Write-Host "1. Paste your JSON PrinterTask below" -ForegroundColor White
Write-Host "2. Press Enter twice when done" -ForegroundColor White
Write-Host "3. Type 'EXIT' to quit" -ForegroundColor White
Write-Host ""

while ($true) {
    Write-Host "----------------------------------------" -ForegroundColor DarkGray
    Write-Host "Paste JSON (or type EXIT to quit):" -ForegroundColor Cyan
    Write-Host "----------------------------------------" -ForegroundColor DarkGray
    
    # Collect multi-line JSON input
    $jsonLines = @()
    $emptyLineCount = 0
    
    while ($true) {
        $line = Read-Host
        
        if ($line -eq "EXIT") {
            Write-Host "Exiting..." -ForegroundColor Yellow
            exit 0
        }
        
        if ([string]::IsNullOrWhiteSpace($line)) {
            $emptyLineCount++
            if ($emptyLineCount -ge 2 -and $jsonLines.Count -gt 0) {
                break  # Two empty lines = end of input
            }
        } else {
            $emptyLineCount = 0
            $jsonLines += $line
        }
    }
    
    if ($jsonLines.Count -eq 0) {
        Write-Host "No input provided. Try again." -ForegroundColor Yellow
        continue
    }
    
    $json = $jsonLines -join "`n"
    
    # Validate JSON
    try {
        $null = $json | ConvertFrom-Json
        Write-Host "✓ Valid JSON detected" -ForegroundColor Green
    } catch {
        Write-Host "✗ Invalid JSON: $($_.Exception.Message)" -ForegroundColor Red
        continue
    }
    
    # Parse and display info
    try {
        $task = $json | ConvertFrom-Json
        Write-Host ""
        Write-Host "Print Job Details:" -ForegroundColor Cyan
        Write-Host "  Printer: $($task.printerDeviceName)" -ForegroundColor White
        Write-Host "  Template: $($task.template.name)" -ForegroundColor White
        Write-Host "  Cash Drawer: $($task.isOpenCashDrawer)" -ForegroundColor White
        Write-Host ""
    } catch {
        Write-Host "Warning: Could not parse job details" -ForegroundColor Yellow
    }
    
    # Send to API
    Write-Host "Sending to printer..." -ForegroundColor Yellow
    
    try {
        $response = Invoke-RestMethod `
            -Uri "http://127.0.0.1:9877/print" `
            -Method POST `
            -Body $json `
            -ContentType "application/json" `
            -ErrorAction Stop
        
        if ($response.success) {
            Write-Host "✓ SUCCESS!" -ForegroundColor Green
            Write-Host "  Job Number: $($response.jobNumber)" -ForegroundColor White
            Write-Host "  Spooler ID: $($response.spoolerJobId)" -ForegroundColor White
            if ($response.retryCount -gt 0) {
                Write-Host "  Retries: $($response.retryCount)" -ForegroundColor Yellow
            }
        } else {
            Write-Host "✗ FAILED!" -ForegroundColor Red
            Write-Host "  Error: $($response.error)" -ForegroundColor Red
            if ($response.retryCount -gt 0) {
                Write-Host "  Retries: $($response.retryCount)" -ForegroundColor Yellow
            }
        }
    } catch {
        Write-Host "✗ API Error!" -ForegroundColor Red
        if ($_.Exception.Response) {
            $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
            $errorBody = $reader.ReadToEnd() | ConvertFrom-Json
            Write-Host "  Error: $($errorBody.error)" -ForegroundColor Red
        } else {
            Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
        }
    }
    
    Write-Host ""
    Write-Host "Ready for next print job..." -ForegroundColor Cyan
    Write-Host ""
}