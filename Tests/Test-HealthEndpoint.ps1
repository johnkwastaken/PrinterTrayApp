# Test-HealthEndpoint.ps1
# Comprehensive tests for GET /health endpoint
# Requirements:
# - Returns ok=true
# - Returns version
# - Lists printers with status
# - Includes hasPrinterIssues flag
# - Shows uptime

$baseUrl = "http://127.0.0.1:9877"
$testsPassed = 0
$testsFailed = 0

function Test-HealthEndpoint {
    param(
        [string]$TestName,
        [scriptblock]$TestScript
    )
    
    Write-Host "`n🧪 Test: $TestName" -ForegroundColor Cyan
    try {
        $result = & $TestScript
        if ($result) {
            Write-Host "✅ PASSED" -ForegroundColor Green
            $script:testsPassed++
        } else {
            Write-Host "❌ FAILED" -ForegroundColor Red
            $script:testsFailed++
        }
    } catch {
        Write-Host "❌ ERROR: $_" -ForegroundColor Red
        $script:testsFailed++
    }
}

Write-Host "================================================" -ForegroundColor Yellow
Write-Host "        GET /health Endpoint Tests" -ForegroundColor Yellow
Write-Host "================================================" -ForegroundColor Yellow

# Test 1: Basic health check returns ok=true
Test-HealthEndpoint "Basic health check returns ok=true" {
    $response = Invoke-RestMethod -Uri "$baseUrl/health" -Method Get
    return $response.ok -eq $true
}

# Test 2: Returns correct version
Test-HealthEndpoint "Returns version field" {
    $response = Invoke-RestMethod -Uri "$baseUrl/health" -Method Get
    return ![string]::IsNullOrEmpty($response.version)
}

# Test 3: Lists all printers
Test-HealthEndpoint "Lists printers array" {
    $response = Invoke-RestMethod -Uri "$baseUrl/health" -Method Get
    return $null -ne $response.printers
}

# Test 4: Includes hasPrinterIssues flag
Test-HealthEndpoint "Includes hasPrinterIssues flag" {
    $response = Invoke-RestMethod -Uri "$baseUrl/health" -Method Get
    return $null -ne $response.hasPrinterIssues
}

# Test 5: Calculates uptime correctly
Test-HealthEndpoint "Returns uptimeSeconds" {
    $response = Invoke-RestMethod -Uri "$baseUrl/health" -Method Get
    return $response.uptimeSeconds -ge 0
}

# Test 6: Printer objects have required fields
Test-HealthEndpoint "Printer objects have status fields" {
    $response = Invoke-RestMethod -Uri "$baseUrl/health" -Method Get
    if ($response.printers -and $response.printers.Count -gt 0) {
        $firstPrinter = $response.printers[0]
        if ($firstPrinter -is [string]) {
            Write-Host "  ⚠️  Printers are strings, not objects" -ForegroundColor Yellow
            return $false
        }
        return $null -ne $firstPrinter.name -and 
               $null -ne $firstPrinter.isOnline -and 
               $null -ne $firstPrinter.status
    }
    Write-Host "  ⚠️  No printers found to test" -ForegroundColor Yellow
    return $true  # Pass if no printers
}

# Test 7: Summary object present
Test-HealthEndpoint "Includes summary object" {
    $response = Invoke-RestMethod -Uri "$baseUrl/health" -Method Get
    return $null -ne $response.summary
}

# Test 8: Summary has correct fields
Test-HealthEndpoint "Summary has totalPrinters, onlinePrinters, offlinePrinters" {
    $response = Invoke-RestMethod -Uri "$baseUrl/health" -Method Get
    if ($response.summary) {
        return $null -ne $response.summary.totalPrinters -and
               $null -ne $response.summary.onlinePrinters -and
               $null -ne $response.summary.offlinePrinters
    }
    return $false
}

# Display current response for debugging
Write-Host "`n📋 Current Response:" -ForegroundColor Magenta
try {
    $response = Invoke-RestMethod -Uri "$baseUrl/health" -Method Get
    $response | ConvertTo-Json -Depth 10 | Write-Host
} catch {
    Write-Host "Failed to get response: $_" -ForegroundColor Red
}

# Summary
Write-Host "`n================================================" -ForegroundColor Yellow
Write-Host "                Test Summary" -ForegroundColor Yellow
Write-Host "================================================" -ForegroundColor Yellow
Write-Host "✅ Passed: $testsPassed" -ForegroundColor Green
Write-Host "❌ Failed: $testsFailed" -ForegroundColor Red

if ($testsFailed -eq 0) {
    Write-Host "`n🎉 All tests passed!" -ForegroundColor Green
    exit 0
} else {
    Write-Host "`nSome tests failed. Enhancements needed." -ForegroundColor Yellow
    exit 1
}