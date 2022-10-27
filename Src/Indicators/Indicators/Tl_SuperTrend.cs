namespace Tulip
{
    internal static partial class Tinet
    {
        private static int SuperTrendStart(double[] options) => (int)options[0];

        private static int SuperTrendStart(decimal[] options) => (int)options[0];

        private static int SuperTrend(int size, double[][] inputs, double[] options, double[][] outputs)
        {
            var period = (int)options[0];
            var factor = (int)options[1];

            if (period < 1)
            {
                return TI_INVALID_OPTION;
            }
            if (factor < 1)
            {
                return TI_INVALID_OPTION;
            }

            if (size <= CloseStart(options))
            {
                return TI_OKAY;
            }

            double[] close = inputs[0];
            double[] high = inputs[2];
            double[] low = inputs[3];

            double[] superTrend = outputs[0];
            double[] direction = outputs[1];
            double[] output = outputs[2];

            //calc ATR
            double sum = high[0] - low[0];
            for (var i = 1; i < period; ++i)
            {
                CalcTrueRange(low, high, close, i, out double trueRange);
                sum += trueRange;
            }

            double per = 1.0 / period;
            double val = sum / period;
            int outputIndex = default;
            output[outputIndex++] = val;
            for (var i = period; i < size-1; ++i)
            {
                CalcTrueRange(low, high, close, i, out double trueRange);
                val = (trueRange - val) * per + val;
                output[outputIndex++] = val;
            }

            int outputIndex2 = default;
            double preLowerBand = default;
            double preUpperBand = default;
            double preSuperTrend = default;
            for (int i = period; i < close.Length; i++)
            {
                var src = high[i] + low[i] / 2;
                var upperBand = src + factor * output[i - period];
                var lowerBand = src - factor * output[i - period];

                var LowerBand = lowerBand > preLowerBand || close[i - 1] < preLowerBand ? lowerBand : preLowerBand;
                var UpperBand = upperBand < preUpperBand || close[i - 1] > preUpperBand ? upperBand : preUpperBand;

                var directionVal = -1;
                if (preSuperTrend == preUpperBand)
                {
                    directionVal = close[i] > upperBand ? -1 : 1;
                }
                else
                {
                    directionVal = close[i] < lowerBand ? 1 : -1;
                }

                direction[outputIndex2] = directionVal;
                superTrend[outputIndex2++] = directionVal == -1 ? lowerBand : upperBand;

                preLowerBand = LowerBand;
                preUpperBand = UpperBand;
            }

            return TI_OKAY;
        }

        private static int SuperTrend(int size, decimal[][] inputs, decimal[] options, decimal[][] outputs)
        {
            var period = (int)options[0];
            var factor = (int)options[1];

            if (period < 1)
            {
                return TI_INVALID_OPTION;
            }
            if (factor < 1)
            {
                return TI_INVALID_OPTION;
            }

            if (size <= CloseStart(options))
            {
                return TI_OKAY;
            }

            decimal[] close = inputs[0];
            decimal[] high = inputs[2];
            decimal[] low = inputs[3];
            
            decimal[] superTrend = outputs[0];
            decimal[] direction = outputs[1];
            decimal[] output = outputs[2];

            //calc ATR
            decimal sum = high[0] - low[0];
            for (var i = 1; i < period; ++i)
            {
                CalcTrueRange(low, high, close, i, out decimal trueRange);
                sum += trueRange;
            }

            decimal per = 1.0m / period;
            decimal val = sum / period;
            int outputIndex = default;
            output[outputIndex++] = val;
            for (var i = period; i < size; ++i)
            {
                CalcTrueRange(low, high, close, i, out decimal trueRange);
                val = (trueRange - val) * per + val;
                output[outputIndex++] = val;
            }

            int outputIndex2 = default;
            decimal preLowerBand = default;
            decimal preUpperBand = default;
            decimal preSuperTrend = default;
            for (int i = period; i < close.Length; i++)
            {
                var src = high[i] + low[i] / 2;
                var upperBand = src + factor * output[i - period];
                var lowerBand = src - factor * output[i - period];

                var LowerBand = lowerBand > preLowerBand || close[i - 1] < preLowerBand ? lowerBand : preLowerBand;
                var UpperBand = upperBand < preUpperBand || close[i - 1] > preUpperBand ? upperBand : preUpperBand;

                var directionVal = -1;
                if (preSuperTrend == preUpperBand)
                {
                    directionVal = close[i] > upperBand ? -1 : 1;
                }
                else
                {
                    directionVal = close[i] < lowerBand ? 1 : -1;
                }

                direction[outputIndex2] = directionVal;
                superTrend[outputIndex2++] = directionVal == -1 ? lowerBand : upperBand;

                preLowerBand = LowerBand;
                preUpperBand = UpperBand;
            }

            return TI_OKAY;
        }
    }
}
