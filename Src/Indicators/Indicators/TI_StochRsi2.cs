using System.Linq;

namespace Tulip
{
    internal static partial class Tinet
    {
        private static int StochRsi2Start(double[] options) => (int)options[0] + (int)options[1] + (int)options[2] + (int)options[3] - 4;

        private static int StochRsi2Start(decimal[] options) => (int)options[0] + (int)options[1] + (int)options[2] + (int)options[3] - 4;

        private static int StochRsi2(int size, double[][] inputs, double[] options, double[][] outputs)
        {
            int rsiPeriod = (int)options[0];
            int stochPeriod = (int)options[1];
            int kPeriod = (int)options[2];
            int dPeriod = (int)options[3];

            if (rsiPeriod < 1 || kPeriod < 1 || kPeriod < 1 || dPeriod < 1)
            {
                return TI_INVALID_OPTION;
            }

            if (size <= StochRsi2Start(options))
            {
                return TI_OKAY;
            }

            double[] input = inputs[0];
            double[] KSmooth = outputs[0];
            double[] DSmooth = outputs[1];

            //first calculate the RSI
            var rsi = BufferDoubleFactory(stochPeriod);
            var kSum = BufferDoubleFactory(kPeriod);
            var dSum = BufferDoubleFactory(dPeriod);
            double smoothUp = default;
            double smoothDown = default;
            int stochIndex = default;
            int stochMaIndex = default;
            for (var i = 1; i <= rsiPeriod; ++i)
            {
                double upward = input[i] > input[i - 1] ? input[i] - input[i - 1] : 0;
                double downward = input[i] < input[i - 1] ? input[i - 1] - input[i] : 0;
                smoothUp += upward;
                smoothDown += downward;
            }

            smoothUp /= rsiPeriod;
            smoothDown /= rsiPeriod;

            double r = 100.0 * (smoothUp / (smoothUp + smoothDown));
            BufferPush(ref rsi, r);

            double per = 1.0 / rsiPeriod;
            double min = r;
            double max = r;
            int mini = default;
            int trail = default;
            double kPer = 1.0 / kPeriod;
            double dPer = 1.0 / dPeriod;

            int maxi = default;
            for (var i = rsiPeriod + 1; i < size; ++i)
            {
                double upward = input[i] > input[i - 1] ? input[i] - input[i - 1] : 0;
                double downward = input[i] < input[i - 1] ? input[i - 1] - input[i] : 0;

                smoothUp = (upward - smoothUp) * per + smoothUp;
                smoothDown = (downward - smoothDown) * per + smoothDown;

                r = 100.0 * (smoothUp / (smoothUp + smoothDown));
                BufferQPush(ref rsi, r);

                max = rsi.vals.Max();
                min = rsi.vals.Min();

                if (i > rsiPeriod - 1)
                {
                    double kDiff = max - min;
                    double kFast = kDiff.Equals(0.0) ? 0.0 : 100.0 * ((r - min) / kDiff);
                    BufferPush(ref kSum, kFast);
                }

                if (i >= rsiPeriod - 1 + kPeriod - 1)
                {
                    double k = kSum.sum * kPer;
                    BufferPush(ref dSum, k);

                    if (i >= stochPeriod + rsiPeriod - 2 + kPeriod - 1 + dPeriod - 1)
                    {
                        KSmooth[stochIndex++] = k;
                        DSmooth[stochMaIndex++] = dSum.sum * dPer;
                    }
                }

            }
            return TI_OKAY;
        }

        private static int StochRsi2(int size, decimal[][] inputs, decimal[] options, decimal[][] outputs)
        {
            int rsiPeriod = (int)options[0];
            int stochPeriod = (int)options[1];
            int kPeriod = (int)options[2];
            int dPeriod = (int)options[3];

            if (rsiPeriod < 1 || kPeriod < 1 || kPeriod < 1 || dPeriod < 1)
            {
                return TI_INVALID_OPTION;
            }

            if (size <= StochRsi2Start(options))
            {
                return TI_OKAY;
            }

            decimal[] input = inputs[0];
            decimal[] KSmooth = outputs[0];
            decimal[] DSmooth = outputs[1];

            //first calculate the RSI
            var rsi = BufferDecimalFactory(stochPeriod);
            var kSum = BufferDecimalFactory(kPeriod);
            var dSum = BufferDecimalFactory(dPeriod);
            decimal smoothUp = default;
            decimal smoothDown = default;
            int stochIndex = default;
            int stochMaIndex = default;
            for (var i = 1; i <= rsiPeriod; ++i)
            {
                decimal upward = input[i] > input[i - 1] ? input[i] - input[i - 1] : 0;
                decimal downward = input[i] < input[i - 1] ? input[i - 1] - input[i] : 0;
                smoothUp += upward;
                smoothDown += downward;
            }

            smoothUp /= rsiPeriod;
            smoothDown /= rsiPeriod;

            decimal r = 100m * (smoothUp / (smoothUp + smoothDown));
            BufferPush(ref rsi, r);

            decimal per = 1m / rsiPeriod;
            decimal min = r;
            decimal max = r;
            int mini = default;
            int trail = default;
            decimal kPer = 1m / kPeriod;
            decimal dPer = 1m / dPeriod;

            int maxi = default;
            for (var i = rsiPeriod + 1; i < size; ++i)
            {
                decimal upward = input[i] > input[i - 1] ? input[i] - input[i - 1] : 0;
                decimal downward = input[i] < input[i - 1] ? input[i - 1] - input[i] : 0;

                smoothUp = (upward - smoothUp) * per + smoothUp;
                smoothDown = (downward - smoothDown) * per + smoothDown;

                r = 100m * (smoothUp / (smoothUp + smoothDown));
                BufferQPush(ref rsi, r);

                max = rsi.vals.Max();
                min = rsi.vals.Min();

                if (i > rsiPeriod - 1)
                {
                    decimal kDiff = max - min;
                    decimal kFast = kDiff.Equals(0m) ? 0m : 100m * ((r - min) / kDiff);
                    BufferPush(ref kSum, kFast);
                }

                if (i >= rsiPeriod - 1 + kPeriod - 1)
                {
                    decimal k = kSum.sum * kPer;
                    BufferPush(ref dSum, k);

                    if (i >= stochPeriod + rsiPeriod - 2 + kPeriod - 1 + dPeriod - 1)
                    {
                        KSmooth[stochIndex++] = k;
                        DSmooth[stochMaIndex++] = dSum.sum * dPer;
                    }
                }

            }
            return TI_OKAY;
        }
    }
}
