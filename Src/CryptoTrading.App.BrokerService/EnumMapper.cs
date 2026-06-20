using System;
using CryptoTrading.App.Core.Exchange;

namespace CryptoTrading.App.BrokerService
{
    public static class EnumMapper
    {
        public static ExchangeOrderSide ParseSide(string side)
        {
            if (Enum.TryParse<ExchangeOrderSide>(side, true, out var result))
                return result;
            throw new ArgumentException($"Invalid order side: '{side}'");
        }

        public static PositionSide ParsePositionSide(string positionSide)
        {
            if (string.IsNullOrEmpty(positionSide))
                return PositionSide.Both;
            if (Enum.TryParse<PositionSide>(positionSide, true, out var result))
                return result;
            return PositionSide.Both;
        }

        public static MarginSideEffect ParseMarginSideEffect(string effect)
        {
            if (string.IsNullOrEmpty(effect))
                return MarginSideEffect.None;
            if (Enum.TryParse<MarginSideEffect>(effect, true, out var result))
                return result;
            return MarginSideEffect.None;
        }

        public static TradingVenue ParseVenue(string venue)
        {
            if (string.IsNullOrEmpty(venue))
                return TradingVenue.Spot;
            if (Enum.TryParse<TradingVenue>(venue, true, out var result))
                return result;
            return TradingVenue.Spot;
        }
    }
}
