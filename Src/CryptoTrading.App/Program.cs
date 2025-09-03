using Microsoft.Extensions.DependencyInjection;
using CryptoTrading.App.Process;
using System;
using Microsoft.Extensions.Logging;

namespace CryptoTrading.App
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                var database = string.IsNullOrEmpty(args[0]) ? "ANKUR-PC\\APDATASERVICE" : args[0];
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
        }
    }
}
