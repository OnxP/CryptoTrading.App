using System;
using System.Linq;
using System.Threading.Tasks;
using CryptoTrading.App.ServiceContracts;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace CryptoTrading.App.BrokerService
{
    public class BrokerGrpcService : CryptoTrading.App.ServiceContracts.BrokerService.BrokerServiceBase
    {
        private readonly BrokerExchangeRegistry _registry;
        private readonly ILogger<BrokerGrpcService> _logger;

        public BrokerGrpcService(BrokerExchangeRegistry registry, ILogger<BrokerGrpcService> logger)
        {
            _registry = registry;
            _logger = logger;
        }

        public override async Task<OrderResponse> PlaceMarketOrder(PlaceMarketOrderRequest request, ServerCallContext context)
        {
            var provider = _registry.Get(request.ExchangeId, request.Venue);
            var order = await provider.PlaceMarketOrderAsync(
                request.Symbol,
                EnumMapper.ParseSide(request.Side),
                (decimal)request.Quantity,
                EnumMapper.ParsePositionSide(request.PositionSide),
                EnumMapper.ParseMarginSideEffect(request.MarginSideEffect),
                request.ReduceOnly);
            return MapOrder(order);
        }

        public override async Task<OrderResponse> PlaceLimitOrder(PlaceLimitOrderRequest request, ServerCallContext context)
        {
            var provider = _registry.Get(request.ExchangeId, request.Venue);
            var order = await provider.PlaceLimitOrderAsync(
                request.Symbol,
                EnumMapper.ParseSide(request.Side),
                (decimal)request.Price,
                (decimal)request.Quantity,
                EnumMapper.ParsePositionSide(request.PositionSide),
                EnumMapper.ParseMarginSideEffect(request.MarginSideEffect),
                request.ReduceOnly);
            return MapOrder(order);
        }

        public override async Task<OrderResponse> PlaceStopLimitOrder(PlaceStopLimitOrderRequest request, ServerCallContext context)
        {
            var provider = _registry.Get(request.ExchangeId, request.Venue);
            var order = await provider.PlaceStopLimitOrderAsync(
                request.Symbol,
                EnumMapper.ParseSide(request.Side),
                (decimal)request.StopPrice,
                (decimal)request.LimitPrice,
                (decimal)request.Quantity,
                EnumMapper.ParsePositionSide(request.PositionSide),
                EnumMapper.ParseMarginSideEffect(request.MarginSideEffect),
                request.ReduceOnly);
            return MapOrder(order);
        }

        public override async Task<OrderResponse> CancelOrder(CancelOrderRequest request, ServerCallContext context)
        {
            var provider = _registry.Get(request.ExchangeId, "");
            var order = await provider.CancelOrderAsync(request.Symbol, request.OrderId);
            return MapOrder(order);
        }

        public override async Task<OrderResponse> GetOrder(GetOrderRequest request, ServerCallContext context)
        {
            var provider = _registry.Get(request.ExchangeId, "");
            var order = await provider.GetOrderAsync(request.Symbol, request.OrderId);
            return MapOrder(order);
        }

        public override async Task<GetOpenOrdersResponse> GetOpenOrders(GetOpenOrdersRequest request, ServerCallContext context)
        {
            var provider = _registry.Get(request.ExchangeId, "");
            var orders = await provider.GetOpenOrdersAsync();
            var response = new GetOpenOrdersResponse();
            response.Orders.AddRange(orders.Select(MapOrder));
            return response;
        }

        public override async Task<GetPositionsResponse> GetPositions(GetPositionsRequest request, ServerCallContext context)
        {
            var provider = _registry.Get(request.ExchangeId, "");
            var positions = await provider.GetPositionsAsync();
            var response = new GetPositionsResponse();
            response.Positions.AddRange(positions.Select(p => new PositionInfo
            {
                ExchangeId = p.ExchangeId ?? request.ExchangeId,
                Symbol = p.Symbol,
                Side = p.Side.ToString(),
                Quantity = (double)p.Quantity,
                EntryPrice = (double)p.EntryPrice,
                MarkPrice = (double)p.MarkPrice,
                Leverage = p.Leverage,
                UnrealizedPnl = (double)p.UnrealizedPnl,
                Notional = (double)p.Notional,
                Timestamp = Timestamp.FromDateTime(DateTime.SpecifyKind(p.Timestamp, DateTimeKind.Utc))
            }));
            return response;
        }

        public override async Task<GetBalancesResponse> GetBalances(GetBalancesRequest request, ServerCallContext context)
        {
            var provider = _registry.Get(request.ExchangeId, "");
            var balances = await provider.GetBalancesAsync();
            var response = new GetBalancesResponse();
            response.Balances.AddRange(balances.Select(b => new BalanceInfo
            {
                ExchangeId = b.ExchangeId ?? request.ExchangeId,
                Asset = b.Asset,
                Free = (double)b.Free,
                Locked = (double)b.Locked
            }));
            return response;
        }

        public override async Task<FeeScheduleResponse> GetFeeSchedule(GetFeeScheduleRequest request, ServerCallContext context)
        {
            var provider = _registry.Get(request.ExchangeId, "");
            var fees = await provider.GetFeeScheduleAsync();
            return new FeeScheduleResponse
            {
                ExchangeId = fees.ExchangeId ?? request.ExchangeId,
                MakerFee = (double)fees.MakerFee,
                TakerFee = (double)fees.TakerFee,
                FeeAsset = fees.FeeAsset ?? "",
                HasFeeDiscount = fees.HasFeeDiscount,
                DiscountRate = (double)fees.DiscountRate
            };
        }

        public override async Task<SetLeverageResponse> SetLeverage(SetLeverageRequest request, ServerCallContext context)
        {
            var provider = _registry.Get(request.ExchangeId, "");
            await provider.SetLeverageAsync(request.Symbol, request.Leverage);
            return new SetLeverageResponse { Success = true, Message = "Leverage set" };
        }

        public override async Task SubscribeOrderUpdates(
            SubscribeOrderUpdatesRequest request,
            IServerStreamWriter<OrderFillEvent> responseStream,
            ServerCallContext context)
        {
            var provider = _registry.Get(request.ExchangeId, "");
            var tcs = new TaskCompletionSource<bool>();

            context.CancellationToken.Register(() => tcs.TrySetResult(true));

            await provider.SubscribeUserStreamAsync(async fill =>
            {
                try
                {
                    var evt = new OrderFillEvent
                    {
                        ExchangeId = fill.ExchangeId ?? request.ExchangeId,
                        OrderId = fill.OrderId ?? "",
                        ClientOrderId = fill.ClientOrderId ?? "",
                        Symbol = fill.Symbol ?? "",
                        Side = fill.Side.ToString(),
                        FilledQuantity = (double)fill.FilledQuantity,
                        Price = (double)fill.Price,
                        Commission = (double)fill.Commission,
                        CommissionAsset = fill.CommissionAsset ?? "",
                        Status = fill.Status.ToString(),
                        Timestamp = Timestamp.FromDateTime(DateTime.SpecifyKind(fill.Timestamp, DateTimeKind.Utc))
                    };
                    await responseStream.WriteAsync(evt);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error writing fill event to stream");
                }
            });

            await tcs.Task;
        }

        private static OrderResponse MapOrder(Core.Exchange.ExchangeOrder order)
        {
            return new OrderResponse
            {
                ExchangeId = order.ExchangeId ?? "",
                OrderId = order.OrderId ?? "",
                ClientOrderId = order.ClientOrderId ?? "",
                Symbol = order.Symbol ?? "",
                Side = order.Side.ToString(),
                Type = order.Type.ToString(),
                Status = order.Status.ToString(),
                Price = (double)order.Price,
                StopPrice = (double)order.StopPrice,
                Quantity = (double)order.Quantity,
                FilledQuantity = (double)order.FilledQuantity,
                QuoteQuantity = (double)order.QuoteQuantity,
                Timestamp = Timestamp.FromDateTime(DateTime.SpecifyKind(order.Timestamp, DateTimeKind.Utc))
            };
        }
    }
}
