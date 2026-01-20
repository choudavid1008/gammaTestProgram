using MathNet.Numerics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WpfAppGui
{
    /// <summary>
    /// Encapsulates the results of the backlight calibration process.
    /// </summary>
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
            // Constructor is now empty as it doesn't need any dependencies.
        }

        /// <summary>
        /// Performs backlight calibration using an exponential model fit.
        /// y = a * e^(b * x)
        /// </summary>
        public CalibrationResult PerformCalibration()
        {
            // 1. Define input values
            double[] pwm_inputs = { 0, 10, 25, 50, 75, 85, 95, 97, 98, 100 };
            double[] measured_luminances = { 1, 1, 1, 3, 18, 37, 75, 87, 93, 107 };

            // 2. Data Filtering: Exclude points where the measured value is near zero.
            var dataPoints = pwm_inputs.Zip(measured_luminances, (x, y) => new { Pwm = x, Lum = y })
                                       .Where(p => p.Lum > 0.001)
                                       .ToList();

            if (dataPoints.Count < 2)
            {
                return new CalibrationResult { IsSuccess = false, ErrorMessage = "有效非零數據點不足 (少於 2 點) 來進行曲線擬合。" };
            }

            double[] x_valid = dataPoints.Select(p => p.Pwm).ToArray();
            double[] y_valid = dataPoints.Select(p => p.Lum).ToArray();

            // 3. Model Transformation and Linear Fitting
            // Transform y = a * e^(b*x) to ln(y) = ln(a) + b*x
            double[] y_log = y_valid.Select(y => Math.Log(y)).ToArray();

            // Perform linear regression: ln(y) = intercept + slope * x
            var parameters = Fit.Line(x_valid, y_log);
            double intercept = parameters.Item1; // ln(a)
            double slope = parameters.Item2;     // b

            // 4. Recover original coefficients
            double a_coeff = Math.Exp(intercept);
            double b_coeff = slope;

            // 5. Generate prediction table
            var steps_5_percent = Enumerable.Range(0, 20).Select(i => (double)i * 5); // 0, 5, ..., 95
            var steps_1_percent = Enumerable.Range(96, 5).Select(i => (double)i);      // 96, 97, 98, 99, 100
            var prediction_pwm_inputs = steps_5_percent.Concat(steps_1_percent).Distinct().OrderBy(p => p);

            var predictionTable = new List<Tuple<double, double>>();
            foreach (var pwm in prediction_pwm_inputs)
            {
                double predictedValue = (pwm == 0) ? 0.0 : a_coeff * Math.Exp(b_coeff * pwm);
                predictionTable.Add(new Tuple<double, double>(pwm, predictedValue));
            }

            // 6. Validation
            double measured_max = measured_luminances.Last();
            double predicted_max = a_coeff * Math.Exp(b_coeff * 100);

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
