# Printer Tray App

## Part 1 Complete - Console App with HTTP Server

### Open in Visual Studio
1. Open **PrinterTrayApp.sln** in Visual Studio
2. Press F5 to run

### Test the API
Once running, test these endpoints:

```bash
# Health check
curl http://127.0.0.1:9877/health

# Print endpoint (returns not implemented)
curl -X POST http://127.0.0.1:9877/print

# Self-test endpoint (returns not implemented)  
curl http://127.0.0.1:9877/self-test
```

### Or use PowerShell:
```powershell
# Health check
Invoke-WebRequest -Uri http://127.0.0.1:9877/health | Select-Object -ExpandProperty Content

# Print endpoint
Invoke-WebRequest -Uri http://127.0.0.1:9877/print -Method POST | Select-Object -ExpandProperty Content

# Self-test
Invoke-WebRequest -Uri http://127.0.0.1:9877/self-test | Select-Object -ExpandProperty Content
```

Press Ctrl+C in the console to stop the server.