using Binance;
using System;
using System.Linq;

namespace CryptoTrading.App.BrokerTesting
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            //first test is to go off to the server and get the current positions in the account.
            var api = new BinanceApi();
            var apiUser = new BinanceApiUser("C5ILSb0VCvBN3BgHMW4MlEqeNKtlif7w7Ib3Jgspl0tdefJZ3WnRn64YKaiPEkTE", "7guaW7iFbPwKqT3dgpL7Tht2L6xPNAxkIk41teMzjxD6G4qn5KaGCi4rCqLc8vW3");

            var account = api.GetAccountInfoAsync(apiUser);

            foreach (var item in account.Result.Balances.Where(x => x.Free != 0.0m))
            {
                Console.WriteLine($"Asset: {item.Asset}, Free: {item.Free}");
            }
        }

        private static async System.Threading.Tasks.Task LoadAccountData(BinanceApi api, IBinanceApiUser user)
        {
            var account = await api.GetAccountInfoAsync(user);

            Console.WriteLine();
        }
    }
}