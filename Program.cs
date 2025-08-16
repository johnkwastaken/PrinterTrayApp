using PrinterTrayApp;
using System.Windows.Forms;

// Prevent multiple instances
using var mutex = new Mutex(true, "PrinterTrayApp_SingleInstance", out bool createdNew);
if (!createdNew)
{
    MessageBox.Show("Printer Tray App is already running!", "Already Running", 
        MessageBoxButtons.OK, MessageBoxIcon.Information);
    return;
}

// Enable visual styles for Windows Forms
Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);

// Run as tray application
Application.Run(new TrayApplicationContext());