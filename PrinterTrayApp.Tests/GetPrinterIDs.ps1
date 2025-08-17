# Get all unique identifiers for printers

Write-Host "=== PRINTER UNIQUE IDENTIFIERS ===" -ForegroundColor Cyan

# Method 1: WMI Win32_Printer
Write-Host "`n--- Via Win32_Printer (WMI) ---" -ForegroundColor Yellow
$printers = Get-WmiObject Win32_Printer | Select-Object Name, DeviceID, PortName, ShareName, SystemName, Local, Network

foreach ($printer in $printers) {
    Write-Host "`nPrinter: $($printer.Name)" -ForegroundColor Green
    Write-Host "  DeviceID: $($printer.DeviceID)"
    Write-Host "  PortName: $($printer.PortName)"
    Write-Host "  ShareName: $($printer.ShareName)"
    Write-Host "  SystemName: $($printer.SystemName)"
    Write-Host "  Local: $($printer.Local)"
    Write-Host "  Network: $($printer.Network)"
}

# Method 2: Registry keys (more unique info)
Write-Host "`n--- Via Registry ---" -ForegroundColor Yellow
$regPath = "HKLM:\SYSTEM\CurrentControlSet\Control\Print\Printers"
$printerKeys = Get-ChildItem $regPath

foreach ($key in $printerKeys) {
    $printerName = $key.PSChildName
    Write-Host "`nPrinter: $printerName" -ForegroundColor Green
    
    # Get printer GUID if exists
    $printerGuid = (Get-ItemProperty "$($key.PSPath)" -Name "PrinterDriverData" -ErrorAction SilentlyContinue)
    if ($printerGuid) {
        Write-Host "  Registry Key: $($key.Name)"
    }
    
    # Check for unique printer ID in driver data
    $driverData = Get-ItemProperty "$($key.PSPath)\PrinterDriverData" -ErrorAction SilentlyContinue
    if ($driverData.PSObject.Properties["UniqueID"]) {
        Write-Host "  UniqueID: $($driverData.UniqueID)"
    }
}

# Method 3: Using .NET PrinterSettings
Write-Host "`n--- Via .NET PrinterSettings ---" -ForegroundColor Yellow
Add-Type -AssemblyName System.Drawing

$installedPrinters = [System.Drawing.Printing.PrinterSettings]::InstalledPrinters
foreach ($printer in $installedPrinters) {
    Write-Host "Printer: $printer" -ForegroundColor Green
    
    $ps = New-Object System.Drawing.Printing.PrinterSettings
    $ps.PrinterName = $printer
    
    Write-Host "  IsValid: $($ps.IsValid)"
    Write-Host "  IsDefaultPrinter: $($ps.IsDefaultPrinter)"
    Write-Host "  SupportsColor: $($ps.SupportsColor)"
    Write-Host "  IsPlotter: $($ps.IsPlotter)"
}

# Method 4: Check for PnP devices (USB printers)
Write-Host "`n--- USB/PnP Device IDs ---" -ForegroundColor Yellow
$pnpPrinters = Get-WmiObject Win32_PnPEntity | Where-Object { $_.Name -like "*print*" -or $_.Service -eq "usbprint" }

foreach ($device in $pnpPrinters) {
    Write-Host "`nDevice: $($device.Name)" -ForegroundColor Green
    Write-Host "  DeviceID: $($device.DeviceID)"
    Write-Host "  PNPDeviceID: $($device.PNPDeviceID)"
    Write-Host "  Service: $($device.Service)"
    Write-Host "  Status: $($device.Status)"
}

# Method 5: Get printer capabilities (includes unique driver info)
Write-Host "`n--- Printer Capabilities ---" -ForegroundColor Yellow
$printer = Get-WmiObject Win32_Printer | Where-Object { $_.Name -eq "passkitchen" }
if ($printer) {
    Write-Host "Printer: passkitchen" -ForegroundColor Green
    Write-Host "  Full DeviceID: $($printer.DeviceID)"
    Write-Host "  Driver Name: $($printer.DriverName)"
    Write-Host "  Port: $($printer.PortName)"
    Write-Host "  Attributes: $($printer.Attributes)"
    Write-Host "  Caption: $($printer.Caption)"
    Write-Host "  CreationClassName: $($printer.CreationClassName)"
    Write-Host "  SystemCreationClassName: $($printer.SystemCreationClassName)"
}