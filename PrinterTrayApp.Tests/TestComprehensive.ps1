# Comprehensive Test Suite for PrinterTrayApp
# Tests all major functionality

$baseUrl = "http://127.0.0.1:9877"
$testResults = @()
$testPrinter = "passkitchen"  # Use actual thermal printer

Write-Host "`n🧪 COMPREHENSIVE PRINTERTRAYAPP TEST SUITE" -ForegroundColor Magenta
Write-Host "=" * 60

# Function to test endpoint
function Test-Endpoint {
    param(
        [string]$Name,
        [string]$Method,
        [string]$Endpoint,
        [object]$Body = $null
    )
    
    Write-Host "`n📋 Testing: $Name" -ForegroundColor Cyan
    
    try {
        $uri = "$baseUrl$Endpoint"
        
        if ($Method -eq "GET") {
            $response = Invoke-RestMethod -Uri $uri -Method Get
        } else {
            $json = $Body | ConvertTo-Json -Depth 10
            $response = Invoke-RestMethod -Uri $uri -Method Post -Body $json -ContentType "application/json"
        }
        
        Write-Host "✅ PASSED" -ForegroundColor Green
        
        # Show key response data
        if ($response.version) {
            Write-Host "   Version: $($response.version)" -ForegroundColor Gray
        }
        if ($response.printers) {
            Write-Host "   Printers: $($response.printers -join ', ')" -ForegroundColor Gray
        }
        if ($response.guid) {
            Write-Host "   GUID: $($response.guid)" -ForegroundColor Gray
        }
        
        return @{
            Test = $Name
            Status = "PASSED"
            Response = $response
        }
    }
    catch {
        Write-Host "❌ FAILED: $_" -ForegroundColor Red
        return @{
            Test = $Name
            Status = "FAILED"
            Error = $_.ToString()
        }
    }
}

# 1. Test Health Endpoint
$testResults += Test-Endpoint -Name "Health Check" -Method "GET" -Endpoint "/health"

# 2. Test Printers Endpoint
$testResults += Test-Endpoint -Name "List Printers" -Method "GET" -Endpoint "/printers"

# 3. Test Basic Print
$basicPrint = @{
    _id = @{ id = "test-001"; siteId = "site-001" }
    template = @{
        body = "<root><text align='center'>TEST RECEIPT</text><separator char='-' /><text>Simple test print</text><command cmd='cut' /></root>"
        name = "Basic Test"
        templateType = "Receipt"
    }
    templateData = "{}"
    printerDeviceName = $testPrinter
    isOpenCashDrawer = $false
}
$testResults += Test-Endpoint -Name "Basic Print" -Method "POST" -Endpoint "/print" -Body $basicPrint

# 4. Test Font Sizes and Styles
$fontTest = @{
    _id = @{ id = "test-002"; siteId = "site-001" }
    template = @{
        body = @"
<root>
    <text align="center" size="normal">Normal Size</text>
    <text align="center" size="wide">Wide Size</text>
    <text align="center" size="high">High Size</text>
    <text align="center" size="wide-high">Wide-High Size</text>
    <separator char="=" />
    <text font-style="b">Bold Text</text>
    <text font-style="u">Underlined Text</text>
    <text align="left">Left Aligned</text>
    <text align="center">Center Aligned</text>
    <text align="right">Right Aligned</text>
    <command cmd="cut" />
</root>
"@
        name = "Font Test"
        templateType = "Receipt"
    }
    templateData = "{}"
    printerDeviceName = $testPrinter
    isOpenCashDrawer = $false
}
$testResults += Test-Endpoint -Name "Font Styles & Sizes" -Method "POST" -Endpoint "/print" -Body $fontTest

# 5. Test Token Replacement
$tokenTest = @{
    _id = @{ id = "test-003"; siteId = "site-001" }
    template = @{
        body = @"
<root>
    <text align="center">{{storeName}}</text>
    <text>Order: {{orderNumber}}</text>
    <text>Customer: {{customerName}}</text>
    <text>Total: {{total}}</text>
    <text>Date: {{dateOfPrinting}}</text>
    <command cmd="cut" />
</root>
"@
        name = "Token Test"
        templateType = "Receipt"
    }
    templateData = @{
        storeName = "Test Store"
        orderNumber = "12345"
        customerName = "John Doe"
        total = "25.50"
    } | ConvertTo-Json
    printerDeviceName = $testPrinter
    isOpenCashDrawer = $false
}
$testResults += Test-Endpoint -Name "Token Replacement" -Method "POST" -Endpoint "/print" -Body $tokenTest

# 6. Test Empty Token Handling
$emptyTokenTest = @{
    _id = @{ id = "test-004"; siteId = "site-001" }
    template = @{
        body = @"
<root>
    <text>Name: {{name}}</text>
    <text>Phone: {{phone}}</text>
    <text>Email: {{email}}</text>
    <text>Notes: {{notes}}</text>
    <command cmd="cut" />
</root>
"@
        name = "Empty Token Test"
        templateType = "Receipt"
    }
    templateData = @{
        name = "Test User"
        # phone missing - should skip line
        email = ""  # empty - should skip line
        # notes missing - should skip line
    } | ConvertTo-Json
    printerDeviceName = $testPrinter
    isOpenCashDrawer = $false
}
$testResults += Test-Endpoint -Name "Empty Token Handling" -Method "POST" -Endpoint "/print" -Body $emptyTokenTest

# 7. Test Docket Section
$docketTest = @{
    _id = @{ id = "test-005"; siteId = "site-001" }
    template = @{
        body = @"
<root>
    <text align="center">KITCHEN DOCKET</text>
    <separator char="-" />
    <docket-section
        product-name-field="productName"
        product-scale="3"
        product-style="normal"
        category-header-hidden="false"
        category-name-field="name"
        category-scale="6"
        category-style="b"
    />
    <separator char="-" />
    <command cmd="cut" />
</root>
"@
        name = "Docket Test"
        templateType = "Docket"
    }
    templateData = @{
        orders = @{
            mainProducts = @(
                @{
                    categories = @(
                        @{
                            category = @{
                                name = "MAIN DISHES"
                                products = @(
                                    @{
                                        productName = "Burger"
                                        qty = "2"
                                        symbol = "x"
                                        level = 0
                                    },
                                    @{
                                        productName = "- Extra Cheese"
                                        qty = "1"
                                        level = 1
                                    }
                                )
                            }
                        }
                    )
                }
            )
        }
    } | ConvertTo-Json -Depth 10
    printerDeviceName = $testPrinter
    isOpenCashDrawer = $false
}
$testResults += Test-Endpoint -Name "Docket Section" -Method "POST" -Endpoint "/print" -Body $docketTest

# 8. Test Cash Drawer
$drawerTest = @{
    _id = @{ id = "test-006"; siteId = "site-001" }
    template = @{
        body = "<root><text>Cash Drawer Test</text><command cmd='cut' /></root>"
        name = "Drawer Test"
        templateType = "Receipt"
    }
    templateData = "{}"
    printerDeviceName = $testPrinter
    isOpenCashDrawer = $true
}
$testResults += Test-Endpoint -Name "Cash Drawer Command" -Method "POST" -Endpoint "/print" -Body $drawerTest

# 9. Test Special Commands
$commandTest = @{
    _id = @{ id = "test-007"; siteId = "site-001" }
    template = @{
        body = @"
<root>
    <text>Testing Special Commands</text>
    <blank lines="2" />
    <separator char="*" />
    <command cmd="beep" />
    <command cmd="cut" />
</root>
"@
        name = "Command Test"
        templateType = "Receipt"
    }
    templateData = "{}"
    printerDeviceName = $testPrinter
    isOpenCashDrawer = $false
}
$testResults += Test-Endpoint -Name "Special Commands" -Method "POST" -Endpoint "/print" -Body $commandTest

# Summary
Write-Host "`n" 
Write-Host "=" * 60
Write-Host "📊 TEST SUMMARY" -ForegroundColor Yellow
Write-Host "=" * 60

$passed = ($testResults | Where-Object { $_.Status -eq "PASSED" }).Count
$failed = ($testResults | Where-Object { $_.Status -eq "FAILED" }).Count
$total = $testResults.Count

Write-Host "`nResults:" -ForegroundColor White
foreach ($result in $testResults) {
    $icon = if ($result.Status -eq "PASSED") { "✅" } else { "❌" }
    $color = if ($result.Status -eq "PASSED") { "Green" } else { "Red" }
    Write-Host "$icon $($result.Test)" -ForegroundColor $color
    if ($result.Error) {
        Write-Host "   Error: $($result.Error)" -ForegroundColor Gray
    }
}

Write-Host "`nTotal: $total tests" -ForegroundColor White
Write-Host "Passed: $passed" -ForegroundColor Green
Write-Host "Failed: $failed" -ForegroundColor Red

if ($failed -eq 0) {
    Write-Host "`n🎉 ALL TESTS PASSED!" -ForegroundColor Green
    Write-Host "PrinterTrayApp is working correctly!" -ForegroundColor Green
} else {
    Write-Host "`n⚠️ SOME TESTS FAILED" -ForegroundColor Yellow
    Write-Host "Please review the errors above." -ForegroundColor Yellow
}

# Save results
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$resultsFile = "TestResults_$timestamp.json"
$testResults | ConvertTo-Json -Depth 10 | Out-File $resultsFile
Write-Host "`n📁 Results saved to: $resultsFile" -ForegroundColor Cyan