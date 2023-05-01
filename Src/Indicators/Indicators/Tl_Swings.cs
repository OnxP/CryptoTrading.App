namespace Tulip
{
    internal static partial class Tinet
    {
        private static int SwingsStart(double[] options) => (int)options[0];

        private static int SwingsStart(decimal[] options) => (int)options[0];

        private static int Swing(int size, double[][] inputs, double[] options, double[][] outputs)
        {
            var period = (int)options[0];

            if (period < 1)
            {
                return TI_INVALID_OPTION;
            }

            if (size <= CloseStart(options))
            {
                return TI_OKAY;
            }

            double[] high = inputs[2];
            double[] low = inputs[3];
            var os = 0;
            var prevOs = 0;
            for (int i = period; i < outputs.Length - 1; i++)
            {
                var upper = Highest(high,period,i);
                var lower = Lowest(low, period, i);

                os = high[i] > upper ? 0 : low[i] < lower ? 1 : prevOs;

                outputs[0][i] = os==0 && prevOs!=0 ? high[i] : 0;
                outputs[1][i] = os == 1 && prevOs != 1 ? low[i] : 0;
                prevOs = os;
            }

            return TI_OKAY;
        }

        private static double Highest(double[] high, int period, int i)
        {
            double highest = default;
            for (int j = 0; j < period; j++)
            {
                if (highest < high[i - j]) highest = high[i - j];
            }
            return highest;
        }
        private static decimal Highest(decimal[] high, int period, int i)
        {
            decimal highest = default;
            for (int j = 0; j < period; j++)
            {
                if (highest < high[i - j]) highest = high[i - j];
            }
            return highest;
        }
        private static double Lowest(double[] low, int period, int i)
        {
            double lowest = default;
            for (int j = 0; j < period; j++)
            {
                if (lowest > low[i - j]) lowest = low[i - j];
            }
            return lowest;
        }
        private static decimal Lowest(decimal[] low, int period, int i)
        {
            decimal lowest = default;
            for (int j = 0; j < period; j++)
            {
                if (lowest > low[i - j]) lowest = low[i - j];
            }
            return lowest;
        }

        private static int Swing(int size, decimal[][] inputs, decimal[] options, decimal[][] outputs)
        {
            var period = (int)options[0];

            if (period < 1)
            {
                return TI_INVALID_OPTION;
            }

            if (size <= CloseStart(options))
            {
                return TI_OKAY;
            }

            decimal[] high = inputs[2];
            decimal[] low = inputs[3];
            var os = 0;
            var prevOs = 0;
            for (int i = period; i < outputs.Length - 1; i++)
            {
                var upper = Highest(high, period, i);
                var lower = Lowest(low, period, i);

                os = high[i] > upper ? 0 : low[i] < lower ? 1 : prevOs;

                outputs[0][i] = os == 0 && prevOs != 0 ? high[i] : 0;
                outputs[1][i] = os == 1 && prevOs != 1 ? low[i] : 0;
                prevOs = os;
            }

            return TI_OKAY;
        }
    }
}
