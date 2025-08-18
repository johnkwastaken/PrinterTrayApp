using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PrinterTrayApp.Services;
using Moq;

namespace PrinterTrayApp.Tests.Integration;

public class PrinterTrayAppFactory : WebApplicationFactory<TestStartup>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseStartup<TestStartup>();
        builder.UseEnvironment("Testing");
    }
}