using CryptoTrading.App.Core.Exchange;
using FluentAssertions;
using Xunit;

namespace CryptoTrading.App.Tests.Exchange.Models
{
    public class ExchangeFeeScheduleTests
    {
        [Fact]
        public void CalculateTakerFee_WithoutDiscount_UsesFullRate()
        {
            var fees = new ExchangeFeeSchedule("Binance", 0.001m, 0.001m, "USDT");

            var fee = fees.CalculateTakerFee(10000m);

            fee.Should().Be(10m); // 10000 * 0.001 = 10
        }

        [Fact]
        public void CalculateMakerFee_WithoutDiscount_UsesFullRate()
        {
            var fees = new ExchangeFeeSchedule("Binance", 0.001m, 0.001m, "USDT");

            var fee = fees.CalculateMakerFee(10000m);

            fee.Should().Be(10m);
        }

        [Fact]
        public void CalculateTakerFee_WithDiscount_AppliesDiscountRate()
        {
            var fees = new ExchangeFeeSchedule("Binance", 0.001m, 0.001m, "BNB")
            {
                HasFeeDiscount = true,
                DiscountRate = 0.25m // 25% discount
            };

            var fee = fees.CalculateTakerFee(10000m);

            fee.Should().Be(7.5m); // 10000 * 0.001 * (1 - 0.25) = 7.5
        }

        [Fact]
        public void Fee_Binance_AppliesBinanceRate()
        {
            var binanceFees = new ExchangeFeeSchedule("Binance", 0.001m, 0.001m, "USDT");
            var fee = binanceFees.CalculateTakerFee(50000m);

            fee.Should().Be(50m); // 50000 * 0.1% = 50
        }

        [Fact]
        public void Fee_Bitfinex_AppliesBitfinexRate()
        {
            var bitfinexFees = new ExchangeFeeSchedule("Bitfinex", 0.001m, 0.002m, "USD");
            var fee = bitfinexFees.CalculateTakerFee(50000m);

            fee.Should().Be(100m); // 50000 * 0.2% = 100
        }

        [Fact]
        public void Fee_DifferentExchanges_CalculateIndependently()
        {
            var binanceFees = new ExchangeFeeSchedule("Binance", 0.001m, 0.001m, "USDT");
            var bitfinexFees = new ExchangeFeeSchedule("Bitfinex", 0.001m, 0.002m, "USD");

            var orderValue = 10000m;

            var binanceFee = binanceFees.CalculateTakerFee(orderValue);
            var bitfinexFee = bitfinexFees.CalculateTakerFee(orderValue);

            binanceFee.Should().NotBe(bitfinexFee);
            binanceFee.Should().Be(10m);   // 0.1%
            bitfinexFee.Should().Be(20m);  // 0.2%
        }

        [Fact]
        public void Fee_UsesExchangeSpecificFeeAsset()
        {
            var binanceFees = new ExchangeFeeSchedule("Binance", 0.001m, 0.001m, "BNB");
            var bitfinexFees = new ExchangeFeeSchedule("Bitfinex", 0.001m, 0.002m, "USD");

            binanceFees.FeeAsset.Should().Be("BNB");
            bitfinexFees.FeeAsset.Should().Be("USD");
        }
    }
}
