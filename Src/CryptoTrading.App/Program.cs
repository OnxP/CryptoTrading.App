using Microsoft.Extensions.DependencyInjection;
using CryptoTrading.App.Process;
using System;
using Microsoft.Extensions.Logging;

namespace CryptoTrading.App
{
    internal class Program
    {
        static void Main(string[] args)
        {
            try
            {
                var services = new ServiceCollection()
                    .AddCryptoService()
                    .AddLogging(builder => builder // configure logging.
                        .SetMinimumLevel(LogLevel.Trace)
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
