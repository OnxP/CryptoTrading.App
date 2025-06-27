// See https://aka.ms/new-console-template for more information
using CryptoTrading.App.Process;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

try
{
    var database = string.IsNullOrEmpty(args[0]) ? "SERVER=SQL-SERVER-2016" : args[0];
    var services = new ServiceCollection()
        .AddCryptoService(database)
        .AddLogging(builder => builder // configure logging.
            .SetMinimumLevel(LogLevel.Information)
            .AddConsole()
        )
        .BuildServiceProvider();

    var process = services.GetService<IProcessManagement>();
    process.Run(3);
}
catch (Exception e)
{
    Console.WriteLine(e.Message);
    Console.WriteLine();
    Console.WriteLine("  ...press any key to close window.");
    Console.ReadKey(true);
}
