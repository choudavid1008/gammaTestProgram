using MathNet.Numerics;
using MathNet.Numerics.LinearAlgebra;
using System;
using System.Collections.Generic;
using System.Linq;

// Note: The LevenbergMarquardtMinimizer is included in the main MathNet.Numerics package in v5.0.0
// No separate Optimization package is needed.
using MathNet.Numerics.Optimization;

namespace WpfAppGui
{
    public class CalibrationResult
    {
        public double A_Coeff { get; set; }
        public double B_Coeff { get; set; }
        public int OriginalDataCount { get; set; }
        public int ValidDataCount { get; set; }
        public bool IsSuccess { get; set; }
        public string ErrorMessage { get; set; }
        public List<Tuple<double, double>> PredictionTable { get; set; }
        public double MeasuredMax { get; set; }
        public double PredictedMax { get; set; }
    }

    public class BacklightCalibrator
    {
        public BacklightCalibrator()
        {
        }

        private static Vector<double> ExponentialModel(Vector<double> parameters, Vector<double> x)
        {
            double a = parameters[0];
            double b = parameters[1];
            return x.Map(val => a * Math.Exp(b * val));
        }

        // Helper for single value prediction after fitting
        private static double Predict(Vector<double> parameters, double x)
        {
            return parameters[0] * Math.Exp(parameters[1] * x);
        }

        public CalibrationResult PerformCalibration(double[] measuredLuminances = null)
        {
            double[] pwm_inputs = { 0, 10, 25, 50, 75, 85, 95, 97, 98, 100 };
            double[] final_luminances;

            if (measuredLuminances != null && measuredLuminances.Length == pwm_inputs.Length)
            {
                final_luminances = measuredLuminances;
            }
            else
            {
                // Fallback to default values
                final_luminances = new double[] { 1, 1, 1, 3, 18, 37, 75, 87, 93, 107 };
            }

            var dataPoints = pwm_inputs.Zip(final_luminances, (x, y) => new { Pwm = x, Lum = y })
                                       .Where(p => p.Lum > 0.001)
                                       .ToList();

            if (dataPoints.Count < 2)
            {
                return new CalibrationResult { IsSuccess = false, ErrorMessage = "有效非零數據點不足 (少於 2 點) 來進行曲線擬合。" };
            }

            var x_valid = Vector<double>.Build.DenseOfArray(dataPoints.Select(p => p.Pwm).ToArray());
            var y_valid = Vector<double>.Build.DenseOfArray(dataPoints.Select(p => p.Lum).ToArray());

            var objective = ObjectiveFunction.NonlinearModel(ExponentialModel, x_valid, y_valid);

            var initialGuess = Vector<double>.Build.Dense(new[] { 2.5, 0.04 });
            var minimizer = new LevenbergMarquardtMinimizer(maximumIterations: 5000);
            var result = minimizer.FindMinimum(objective, initialGuess);

            if (!result.ReasonForExit.HasFlag(ExitCondition.Converged))
            {
                 return new CalibrationResult { IsSuccess = false, ErrorMessage = $"非線性擬合演算法未收斂: {result.ReasonForExit}" };
            }

            double a_coeff = result.MinimizingPoint[0];
            double b_coeff = result.MinimizingPoint[1];

            var steps_5_percent = Enumerable.Range(0, 20).Select(i => (double)i * 5);
            var steps_1_percent = Enumerable.Range(96, 5).Select(i => (double)i);
            var prediction_pwm_inputs = steps_5_percent.Concat(steps_1_percent).Distinct().OrderBy(p => p);

            var predictionTable = new List<Tuple<double, double>>();
            var finalParameters = Vector<double>.Build.Dense(new[] { a_coeff, b_coeff });
            foreach (var pwm in prediction_pwm_inputs)
            {
                double predictedValue = (pwm == 0) ? 0.0 : Predict(finalParameters, pwm);
                predictionTable.Add(new Tuple<double, double>(pwm, predictedValue));
            }

            double measured_max = final_luminances.Last();
            double predicted_max = Predict(finalParameters, 100);

            return new CalibrationResult
            {
                IsSuccess = true,
                A_Coeff = a_coeff,
                B_Coeff = b_coeff,
                OriginalDataCount = pwm_inputs.Length,
                ValidDataCount = dataPoints.Count,
                PredictionTable = predictionTable,
                MeasuredMax = measured_max,
                PredictedMax = predicted_max
            };
        }
    }
}
