using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using CryptoTrading.App.Core.Exchange;

namespace CryptoTrading.App.Persistence.Entities
{
    [Table("candlesticks")]
    public class CandlestickEntity
    {
        [Key]
        [Column("id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        [MaxLength(50)]
        [Column("exchange_id")]
        public string ExchangeId { get; set; } = "Binance";

        [Required]
        [MaxLength(50)]
        [Column("symbol")]
        public string Symbol { get; set; }

        [Column("interval")]
        public int Interval { get; set; }

        [Column("open_time")]
        public DateTime OpenTime { get; set; }

        [Column("close_time")]
        public DateTime CloseTime { get; set; }

        [Column("open")]
        public double Open { get; set; }

        [Column("high")]
        public double High { get; set; }

        [Column("low")]
        public double Low { get; set; }

        [Column("close")]
        public double Close { get; set; }

        [Column("volume")]
        public decimal Volume { get; set; }

        [Column("quote_volume")]
        public decimal QuoteVolume { get; set; }

        [Column("number_of_trades")]
        public long NumberOfTrades { get; set; }

        public ExchangeCandlestick ToExchangeCandlestick()
        {
            return new ExchangeCandlestick
            {
                ExchangeId = ExchangeId,
                Symbol = Symbol,
                Interval = (CandleInterval)Interval,
                OpenTime = OpenTime,
                CloseTime = CloseTime,
                Open = (decimal)Open,
                High = (decimal)High,
                Low = (decimal)Low,
                Close = (decimal)Close,
                Volume = Volume,
                QuoteVolume = QuoteVolume,
                NumberOfTrades = NumberOfTrades,
                IsClosed = true
            };
        }

        public static CandlestickEntity FromExchangeCandlestick(ExchangeCandlestick c)
        {
            return new CandlestickEntity
            {
                ExchangeId = c.ExchangeId ?? "Binance",
                Symbol = c.Symbol,
                Interval = (int)c.Interval,
                OpenTime = c.OpenTime,
                CloseTime = c.CloseTime,
                Open = (double)c.Open,
                High = (double)c.High,
                Low = (double)c.Low,
                Close = (double)c.Close,
                Volume = c.Volume,
                QuoteVolume = c.QuoteVolume,
                NumberOfTrades = c.NumberOfTrades
            };
        }
    }
}
