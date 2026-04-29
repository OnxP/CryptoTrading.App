using CryptoTrading.App.Core.Position;
using CryptoTrading.App.Core.Trade;
using FluentAssertions;
using Xunit;

namespace CryptoTrading.App.Tests.CorePosition
{
    /// <summary>
    /// Pins the balance accounting and lifecycle behaviour of the core
    /// Position class. Two things matter most:
    ///   * FreeAmount must subtract pending sells (negative pending legs)
    ///     so we never double-spend an in-flight outflow.
    ///   * HasOpenPosition must report true while a leg is pending OR while
    ///     the lock flag is set, otherwise the strategy could open a second
    ///     position on top of an unsettled one.
    /// </summary>
    public class PositionTests
    {
        private const string Symbol = "BTC";

        #region Construction

        [Fact]
        public void Ctor_WithPositiveFreeAmount_SeedsCompletedLeg()
        {
            var position = new CryptoTrading.App.Core.Position.Position(Symbol, 10m);

            position.Symbol.Should().Be(Symbol);
            position.FreeAmount.Should().Be(10m);
            position.NonFreeAmount.Should().Be(0m);
            position.IsLocked.Should().BeFalse();
            position.HasOpenPosition.Should().BeFalse();
        }

        [Fact]
        public void Ctor_WithZeroFreeAmount_AddsNoLeg()
        {
            var position = new CryptoTrading.App.Core.Position.Position(Symbol, 0m);

            position.FreeAmount.Should().Be(0m);
            position.NonFreeAmount.Should().Be(0m);
            position.HasOpenPosition.Should().BeFalse();
        }

        [Fact]
        public void Ctor_WithNegativeFreeAmount_AddsNoLeg()
        {
            // Production guard: only seed when freeAmount > 0.
            var position = new CryptoTrading.App.Core.Position.Position(Symbol, -5m);

            position.FreeAmount.Should().Be(0m);
        }

        #endregion

        #region FreeAmount accounting

        [Fact]
        public void FreeAmount_IgnoresPositivePendingLegs()
        {
            // Pending buys (positive Pending leg) are NOT yet free balance -
            // they shouldn't show up until they complete.
            var position = new CryptoTrading.App.Core.Position.Position(Symbol, 10m);
            position.CreatePendingTransaction(5m);

            position.FreeAmount.Should().Be(10m,
                "a pending buy must not inflate free balance until it completes");
        }

        [Fact]
        public void FreeAmount_SubtractsPendingSells()
        {
            // Pending sells (negative Pending leg) reduce free balance
            // immediately so we can't spend the same coins twice.
            var position = new CryptoTrading.App.Core.Position.Position(Symbol, 10m);
            position.CreatePendingTransaction(-3m);

            position.FreeAmount.Should().Be(7m,
                "an in-flight sell must lock the corresponding free balance");
        }

        [Fact]
        public void FreeAmount_SumsCompletedLegs()
        {
            var position = new CryptoTrading.App.Core.Position.Position(Symbol, 10m);
            position.CreateTransaction(2.5m);
            position.CreateTransaction(-1m);

            position.FreeAmount.Should().Be(11.5m);
        }

        [Fact]
        public void FreeAmount_PendingSellAndCompletedAdjustment_StackCorrectly()
        {
            var position = new CryptoTrading.App.Core.Position.Position(Symbol, 10m);
            position.CreateTransaction(5m);              // completed +5  -> 15
            position.CreatePendingTransaction(-2m);      // pending sell -> -2
            position.CreatePendingTransaction(3m);       // pending buy   -> ignored

            position.FreeAmount.Should().Be(13m);
        }

        #endregion

        #region NonFreeAmount

        [Fact]
        public void NonFreeAmount_IsZeroWhenNoPendingLegs()
        {
            var position = new CryptoTrading.App.Core.Position.Position(Symbol, 10m);
            position.CreateTransaction(2m);

            position.NonFreeAmount.Should().Be(0m);
        }

        [Fact]
        public void NonFreeAmount_SumsAllPendingLegs_PositiveAndNegative()
        {
            var position = new CryptoTrading.App.Core.Position.Position(Symbol, 10m);
            position.CreatePendingTransaction(4m);
            position.CreatePendingTransaction(-1.5m);

            position.NonFreeAmount.Should().Be(2.5m);
        }

        #endregion

        #region CheckHasEnoughBalance

        [Fact]
        public void CheckHasEnoughBalance_TrueWhenFreeStrictlyExceeds()
        {
            var position = new CryptoTrading.App.Core.Position.Position(Symbol, 10m);

            position.CheckHasEnoughBalance(9m).Should().BeTrue();
        }

        [Fact]
        public void CheckHasEnoughBalance_FalseWhenFreeEqualsAmount()
        {
            // Production uses strict greater-than - equal amount is NOT enough.
            // This pins that boundary so a refactor to >= would be caught.
            var position = new CryptoTrading.App.Core.Position.Position(Symbol, 10m);

            position.CheckHasEnoughBalance(10m).Should().BeFalse();
        }

        [Fact]
        public void CheckHasEnoughBalance_FalseWhenFreeIsZero()
        {
            var position = new CryptoTrading.App.Core.Position.Position(Symbol, 0m);

            position.CheckHasEnoughBalance(0m).Should().BeFalse();
            position.CheckHasEnoughBalance(1m).Should().BeFalse();
        }

        [Fact]
        public void CheckHasEnoughBalance_FalseAfterPendingSellDrainsFree()
        {
            var position = new CryptoTrading.App.Core.Position.Position(Symbol, 10m);
            position.CreatePendingTransaction(-9m); // 1 free remains

            position.CheckHasEnoughBalance(2m).Should().BeFalse();
            position.CheckHasEnoughBalance(0.5m).Should().BeTrue();
        }

        #endregion

        #region CheckHasEnoughBalanceIncludingFee

        [Fact]
        public void CheckHasEnoughBalanceIncludingFee_RequiresAmountPlusTwoPercent()
        {
            // 100 free, asking for 98: 98 + 1.96 = 99.96 < 100 -> ok
            var position = new CryptoTrading.App.Core.Position.Position(Symbol, 100m);

            position.CheckHasEnoughBalanceIncludingFee(98m).Should().BeTrue();
        }

        [Fact]
        public void CheckHasEnoughBalanceIncludingFee_FailsWhenFeeWouldOverdraw()
        {
            // 100 free, asking for 99: 99 + 1.98 = 100.98 > 100 -> not enough
            var position = new CryptoTrading.App.Core.Position.Position(Symbol, 100m);

            position.CheckHasEnoughBalanceIncludingFee(99m).Should().BeFalse();
        }

        [Fact]
        public void CheckHasEnoughBalanceIncludingFee_FalseAtZeroFree()
        {
            var position = new CryptoTrading.App.Core.Position.Position(Symbol, 0m);

            position.CheckHasEnoughBalanceIncludingFee(1m).Should().BeFalse();
        }

        #endregion

        #region HasOpenPosition

        [Fact]
        public void HasOpenPosition_FalseOnFreshFundedPosition()
        {
            var position = new CryptoTrading.App.Core.Position.Position(Symbol, 10m);

            position.HasOpenPosition.Should().BeFalse();
        }

        [Fact]
        public void HasOpenPosition_TrueWhilePendingLegExists()
        {
            var position = new CryptoTrading.App.Core.Position.Position(Symbol, 10m);
            position.CreatePendingTransaction(2m);

            position.HasOpenPosition.Should().BeTrue();
        }

        [Fact]
        public void HasOpenPosition_TrueWhenLockedEvenWithoutPendingLegs()
        {
            // The lock flag is the strategy-side guard against opening a
            // second order while one is being prepared. It must be respected
            // even when no pending leg has been recorded yet.
            var position = new CryptoTrading.App.Core.Position.Position(Symbol, 10m);
            position.IsLocked = true;

            position.HasOpenPosition.Should().BeTrue();
        }

        [Fact]
        public void HasOpenPosition_FalseAfterPendingLegCompletes()
        {
            var position = new CryptoTrading.App.Core.Position.Position(Symbol, 10m);
            var pending = position.CreatePendingTransaction(2m);
            position.HasOpenPosition.Should().BeTrue();

            pending.Status = TransactionLegStatus.Completed;

            position.HasOpenPosition.Should().BeFalse(
                "once the pending leg is settled and lock cleared, position is open-flat");
        }

        [Fact]
        public void HasOpenPosition_FalseWhenPendingCancelled()
        {
            var position = new CryptoTrading.App.Core.Position.Position(Symbol, 10m);
            var pending = position.CreatePendingTransaction(2m);

            pending.Status = TransactionLegStatus.Cancelled;

            position.HasOpenPosition.Should().BeFalse();
        }

        #endregion

        #region Transaction creation

        [Fact]
        public void CreatePendingTransaction_ReturnsLegWithPendingStatusAndCorrectQuantity()
        {
            var position = new CryptoTrading.App.Core.Position.Position(Symbol, 10m);

            var leg = position.CreatePendingTransaction(2.5m);

            leg.Should().NotBeNull();
            leg.Symbol.Should().Be(Symbol);
            leg.Quantity.Should().Be(2.5m);
            leg.Status.Should().Be(TransactionLegStatus.Pending);
        }

        [Fact]
        public void CreateTransaction_ReturnsLegWithCompletedStatusAndCorrectQuantity()
        {
            var position = new CryptoTrading.App.Core.Position.Position(Symbol, 10m);

            var leg = position.CreateTransaction(2.5m);

            leg.Should().NotBeNull();
            leg.Symbol.Should().Be(Symbol);
            leg.Quantity.Should().Be(2.5m);
            leg.Status.Should().Be(TransactionLegStatus.Completed);
        }

        [Fact]
        public void CreateTransaction_AppearsImmediatelyInFreeAmount()
        {
            var position = new CryptoTrading.App.Core.Position.Position(Symbol, 10m);

            position.CreateTransaction(5m);

            position.FreeAmount.Should().Be(15m);
        }

        [Fact]
        public void CreatePendingTransaction_DoesNotAffectFreeAmountForBuy()
        {
            var position = new CryptoTrading.App.Core.Position.Position(Symbol, 10m);

            position.CreatePendingTransaction(5m); // pending buy

            position.FreeAmount.Should().Be(10m);
            position.NonFreeAmount.Should().Be(5m);
        }

        #endregion
    }
}
