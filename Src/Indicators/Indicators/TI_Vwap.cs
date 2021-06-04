namespace Tulip
{
    internal static partial class Tinet
    {
        private static int VwapStart(double[] options) => (int)options[0] - 1;

        private static int VwapStart(decimal[] options) => (int)options[0] - 1;

        private static int Vwap(int size, double[][] inputs, double[] options, double[][] outputs)
        {
            var period = (int)options[0];

            if (period < 0)
            {
                return TI_INVALID_OPTION;
            }

            if (size <= VwapStart(options))
            {
                return TI_OKAY;
            }

            double[] input = inputs[0];
            double[] volume = inputs[1];
            double[] high = inputs[2];
            double[] low = inputs[3];
            double[] output = outputs[0];

            var sum = BufferDoubleFactory(period);
            var vSum = BufferDoubleFactory(period);
            int outputIndex = default;

            for (var i = 0; i < input.Length; ++i)
            {
                double averagePrice = (high[i] + low[i] + input[i]) / 3;
                BufferPush(ref sum, averagePrice * volume[i]);
                BufferPush(ref vSum, volume[i]);
                output[outputIndex++] = sum.sum / vSum.sum;
            }

            return TI_OKAY;
        }

        private static int Vwap(int size, decimal[][] inputs, decimal[] options, decimal[][] outputs)
        {
            var period = (int)options[0];

            if (period < 0)
            {
                return TI_INVALID_OPTION;
            }

            if (size <= VwapStart(options))
            {
                return TI_OKAY;
            }

            decimal[] input = inputs[0];
            decimal[] volume = inputs[1];
            decimal[] high = inputs[2];
            decimal[] low = inputs[3];
            decimal[] output = outputs[0];
            var sum = BufferDecimalFactory(period);
            var vSum = BufferDecimalFactory(period);
            int outputIndex = default;

            for (var i = 0; i < input.Length; ++i)
            {
                decimal averagePrice = (high[i] + low[i] + input[i]) / 3m;
                var vol = volume[i] == 0m ? 1 : volume[i];
                BufferPush(ref sum, averagePrice * vol);
                BufferPush(ref vSum, vol);
                if (i>period)output[outputIndex++] = sum.sum / vSum.sum;
            }

            return TI_OKAY;
        }
    }
}
