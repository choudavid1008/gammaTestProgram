using MathNet.Numerics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace WpfAppGui
{
    public class BacklightCalibrator
    {
        private readonly MainWindow _mainWindow;

        public BacklightCalibrator(MainWindow mainWindow)
        {
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
        }

        public double[] PerformCalibration()
        {
            // 1. Define input values
            // PWM input values
            double[] pwm_inputs = { 0, 10, 25, 50, 75, 85, 95, 97, 98, 100 };

            // Fixed luminance measurement values as per your request
            double[] luminance_targets = { 1, 1, 1, 3, 18, 37, 75, 87, 93, 107 };

            // In a real scenario, this would involve a loop calling hardware.
            // For now, we use the fixed target values as the measured values.
            double[] measured_luminances = luminance_targets;


            // 3. Perform polynomial fit (2nd degree)
            // Fit.Polynomial(x, y, degree) returns coefficients as [c, b, a] for y = a*x^2 + b*x + c
            double[] coefficients = Fit.Polynomial(pwm_inputs, measured_luminances, 2);

            // Reverse the array to get it in the order [a, b, c] for clarity
            Array.Reverse(coefficients);

            // 4. Return the coefficients
            return coefficients;
        }
    }
}
