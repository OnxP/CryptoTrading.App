namespace Tulip
{
    internal static partial class Tinet
    {
        private static int WacdStart(double[] options)
        {
            // NB we return data before signal is strictly valid.
            int longPeriod = (int)options[1];
            return longPeriod - 1;
        }

        private static int WacdStart(decimal[] options)
        {
            // NB we return data before signal is strictly valid.
            int longPeriod = (int)options[1];
            return longPeriod - 1;
        }

        private static int Wacd(int size, double[][] inputs, double[] options, double[][] outputs)
        {
            int shortPeriod = (int)options[0];
            int longPeriod = (int)options[1];
            int signalPeriod = (int)options[2];

            if (shortPeriod < 1 || longPeriod < 2 || longPeriod < shortPeriod || signalPeriod < 1)
            {
                return TI_INVALID_OPTION;
            }

            if (size <= MacdStart(options))
            {
                return TI_OKAY;
            }

            double[] input = inputs[0];
            double[] macd = outputs[0];
            double[] signal = outputs[1];
            double[] hist = outputs[2];

            double shortWeightSum = default; // Weighted sum of previous numbers.
            double shortSum = default; // Flat sum of previous numbers.
            for (var i = 0; i < shortPeriod - 1; ++i)
            {
                shortWeightSum += input[i] * (i + 1);
                shortSum += input[i];
            }

            double longWeightSum = default; // Weighted sum of previous numbers.
            double longSum = default; // Flat sum of previous numbers.
            for (var i = 0; i < longPeriod - 1; ++i)
            {
                longWeightSum += input[i] * (i + 1);
                longSum += input[i];
            }

            double signalWeightSum = default; // Weighted sum of previous numbers.
            double signalSum = default; // Flat sum of previous numbers.
            for (var i = 0; i < signalPeriod - 1; ++i)
            {
                signalWeightSum += input[i] * (i + 1);
                signalSum += input[i];
            }

            double shortWeights = shortPeriod * (shortPeriod + 1) / 2.0;
            double longWeights = longPeriod * (longPeriod + 1) / 2.0;
            double signalWeights = signalPeriod * (signalPeriod + 1) / 2.0;

            int macdIndex = default;
            int signalIndex = default;
            int histIndex = default;
            double shortEma = shortWeightSum / shortWeights;
            double longEma = longWeightSum / longWeights;
            for (var i = longPeriod - 1; i < size; ++i)
            {
                if (i >= shortPeriod - 1)
                {
                    shortWeightSum += input[i] * shortPeriod;
                    shortSum += input[i];

                    shortEma = shortWeightSum / shortWeights;

                    shortWeightSum -= shortSum;
                    shortSum -= input[i - shortPeriod + 1];
                }
                if (i >= longPeriod - 1)
                {
                    longWeightSum += input[i] * longPeriod;
                    longSum += input[i];

                    longEma = longWeightSum / longWeights;

                    longWeightSum -= longSum;
                    longSum -= input[i - longPeriod + 1];
                }
                double outEma = shortEma - longEma;
                double signalEma  = outEma;

                if (i >= signalPeriod - 1)
                {
                    signalWeightSum += input[i] * signalPeriod;
                    signalSum += input[i];

                    signalEma = signalWeightSum / signalWeights;

                    signalWeightSum -= signalSum;
                    signalSum -= input[i - signalPeriod + 1];
                }
                if (i >= longPeriod - 1)
                {
                    macd[macdIndex++] = outEma;
                    signal[signalIndex++] = signalEma;
                    hist[histIndex++] = outEma - signalEma;
                }
            }

            return TI_OKAY;
        }

        private static int Wacd(int size, decimal[][] inputs, decimal[] options, decimal[][] outputs)
        {
            int shortPeriod = (int)options[0];
            int longPeriod = (int)options[1];
            int signalPeriod = (int)options[2];

            if (shortPeriod < 1 || longPeriod < 2 || longPeriod < shortPeriod || signalPeriod < 1)
            {
                return TI_INVALID_OPTION;
            }

            if (size <= MacdStart(options))
            {
                return TI_OKAY;
            }

            decimal[] input = inputs[0];
            decimal[] macd = outputs[0];
            decimal[] signal = outputs[1];
            decimal[] hist = outputs[2];

            decimal shortWeightSum = default; // Weighted sum of previous numbers.
            decimal shortSum = default; // Flat sum of previous numbers.
            for (var i = 0; i < shortPeriod - 1; ++i)
            {
                shortWeightSum += input[i] * (i + 1);
                shortSum += input[i];
            }

            decimal longWeightSum = default; // Weighted sum of previous numbers.
            decimal longSum = default; // Flat sum of previous numbers.
            for (var i = 0; i < longPeriod - 1; ++i)
            {
                longWeightSum += input[i] * (i + 1);
                longSum += input[i];
            }
            decimal signalWeightSum = default; // Weighted sum of previous numbers.
            decimal signalSum = default; // Flat sum of previous numbers.


            decimal shortWeights = shortPeriod * (shortPeriod + 1) / 2m;
            decimal longWeights = longPeriod * (longPeriod + 1) / 2m;
            decimal signalWeights = signalPeriod * (signalPeriod + 1) / 2m;

            int macdIndex = default;
            int signalIndex = default;
            int histIndex = default;
            decimal shortEma = shortWeightSum / shortWeights;
            decimal longEma = longWeightSum / longWeights;
            for (var i = 1; i < size; ++i)
            {
                if (i >= shortPeriod - 1)
                {
                    shortWeightSum += input[i] * shortPeriod;
                    shortSum += input[i];

                    shortEma = shortWeightSum / shortWeights;

                    shortWeightSum -= shortSum;
                    shortSum -= input[i - shortPeriod + 1];
                }
                if (i >= longPeriod - 1)
                {
                    longWeightSum += input[i] * longPeriod;
                    longSum += input[i];

                    longEma = longWeightSum / longWeights;

                    longWeightSum -= longSum;
                    longSum -= input[i - longPeriod + 1];
                }
                decimal outEma = shortEma - longEma;
                decimal signalEma = outEma;
                if (i <= longPeriod + signalPeriod - 2 && i >= longPeriod - 1)
                {
                    signalWeightSum += outEma * (i - longPeriod);
                    signalSum += outEma;
                }

                if (i >= longPeriod + signalPeriod - 2)
                {
                    signalWeightSum += outEma * signalPeriod;
                    signalSum += outEma;

                    signalEma = signalWeightSum / signalWeights;

                    signalWeightSum -= signalSum;
                    signalSum -= macd[macdIndex - signalPeriod + 1]; // input[i - signalPeriod + 1];
                }
                if (i >= longPeriod - 1)
                {
                    macd[macdIndex++] = outEma;
                    signal[signalIndex++] = signalEma;
                    hist[histIndex++] = outEma - signalEma;
                }
            }
                return TI_OKAY;
        }
    }
}
