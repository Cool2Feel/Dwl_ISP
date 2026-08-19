using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ThunderSE.DeviceConfig.Isp
{
    public class CubicSplineInterpolator
    {
        private double[] _x;
        private double[] _y;
        private double[] _h;
        private double[] _alpha;
        private double[] _l;
        private double[] _mu;
        private double[] _z;
        private double[] _c;
        private double[] _b;
        private double[] _d;

        public CubicSplineInterpolator(List<KeyValuePair<double, double>> controlPoints)
        {
            if (controlPoints == null || controlPoints.Count < 3)
                throw new ArgumentException("至少需要3个控制点");

            int n = controlPoints.Count - 1;

            _x = controlPoints.Select(p => p.Key).ToArray();
            _y = controlPoints.Select(p => p.Value).ToArray();

            InitializeSplineParameters(n);
        }

        private void InitializeSplineParameters(int n)
        {
            _h = new double[n];
            _alpha = new double[n];
            _l = new double[n + 1];
            _mu = new double[n + 1];
            _z = new double[n + 1];
            _c = new double[n + 1];
            _b = new double[n];
            _d = new double[n];

            for (int i = 0; i < n; i++)
                _h[i] = _x[i + 1] - _x[i];

            for (int i = 1; i < n; i++)
                _alpha[i] = (3 / _h[i]) * (_y[i + 1] - _y[i]) -
                           (3 / _h[i - 1]) * (_y[i] - _y[i - 1]);

            SolveTridiagonalSystem(n);
            CalculateCoefficients(n);
        }

        private void SolveTridiagonalSystem(int n)
        {
            _l[0] = 1;
            _mu[0] = _z[0] = 0;

            for (int i = 1; i < n; i++)
            {
                _l[i] = 2 * (_x[i + 1] - _x[i - 1]) - _h[i - 1] * _mu[i - 1];
                _mu[i] = _h[i] / _l[i];
                _z[i] = (_alpha[i] - _h[i - 1] * _z[i - 1]) / _l[i];
            }

            _l[n] = 1;
            _z[n] = _c[n] = 0;

            for (int j = n - 1; j >= 0; j--)
            {
                _c[j] = _z[j] - _mu[j] * _c[j + 1];
                _b[j] = (_y[j + 1] - _y[j]) / _h[j] - _h[j] * (_c[j + 1] + 2 * _c[j]) / 3;
                _d[j] = (_c[j + 1] - _c[j]) / (3 * _h[j]);
            }
        }

        private void CalculateCoefficients(int n)
        {
        }

        public double Interpolate(double xTarget)
        {
            int n = _x.Length - 1;

            if (xTarget <= _x[0])
                return _y[0];

            if (xTarget >= _x[n])
                return _y[n];

            int k = FindInterval(xTarget, n);

            if (k == -1) return _y[0];

            double dx = xTarget - _x[k];
            return _y[k] + _b[k] * dx + _c[k] * dx * dx + _d[k] * dx * dx * dx;
        }

        private int FindInterval(double x, int n)
        {
            for (int i = 0; i < n; i++)
            {
                if (x >= _x[i] && x <= _x[i + 1])
                    return i;
            }
            return -1;
        }

        public List<KeyValuePair<double, double>> GenerateInterpolatedCurve(
            double startX, double endX, int pointCount)
        {
            var result = new List<KeyValuePair<double, double>>();
            double step = (endX - startX) / (pointCount - 1);

            for (int i = 0; i < pointCount; i++)
            {
                double x = startX + step * i;
                double y = Interpolate(x);
                result.Add(new KeyValuePair<double, double>(x, y));
            }

            return result;
        }
    }

    public static class AwbSmartInterpolator
    {
        public const int DEFAULT_KEY_POINT_COUNT = 8;
        public const int OUTPUT_POINT_COUNT = 32;

        public static ObservableCollection<KeyValuePair<double, double>> GenerateFullCurveFromKeyPoints(
            ObservableCollection<KeyValuePair<double, double>> keyPoints,
            int outputPointCount = OUTPUT_POINT_COUNT)
        {
            if (keyPoints == null || keyPoints.Count < 2)
                throw new ArgumentException("至少需要2个关键点");

            if (keyPoints.Count == 2)
            {
                return LinearInterpolate(keyPoints, outputPointCount);
            }

            try
            {
                var spline = new CubicSplineInterpolator(keyPoints.ToList());
                double startX = keyPoints.First().Key;
                double endX = keyPoints.Last().Key;
                var result = spline.GenerateInterpolatedCurve(startX, endX, outputPointCount);
                return new ObservableCollection<KeyValuePair<double, double>>(result);
            }
            catch
            {
                return LinearInterpolate(keyPoints, outputPointCount);
            }
        }

        private static ObservableCollection<KeyValuePair<double, double>> LinearInterpolate(
            ObservableCollection<KeyValuePair<double, double>> keyPoints,
            int outputPointCount)
        {
            var result = new ObservableCollection<KeyValuePair<double, double>>();

            if (keyPoints.Count < 2) return result;

            double startX = keyPoints.First().Key;
            double endX = keyPoints.Last().Key;
            double startY = keyPoints.First().Value;
            double endY = keyPoints.Last().Value;
            double step = (endX - startX) / (outputPointCount - 1);

            for (int i = 0; i < outputPointCount; i++)
            {
                double t = (double)i / (outputPointCount - 1);
                double x = startX + step * i;
                double y = startY + t * (endY - startY);
                result.Add(new KeyValuePair<double, double>(x, y));
            }

            return result;
        }

        public static byte[] GenerateStatTabFromCurves(
            ObservableCollection<ObservableCollection<KeyValuePair<double, double>>> curves,
            int targetSize = 128)
        {
            byte[] statTab = new byte[targetSize];
            Array.Clear(statTab, 0, targetSize);

            int curveIndex = 0;
            foreach (var curve in curves)
            {
                int baseIndex = curveIndex * 32;
                for (int i = 0; i < Math.Min(curve.Count, 32); i++)
                {
                    if (baseIndex + i < targetSize)
                        statTab[baseIndex + i] = (byte)Math.Max(0, Math.Min(255, curve[i].Value));
                }
                curveIndex++;
                if (curveIndex >= 4) break;
            }

            return statTab;
        }

        public static ObservableCollection<KeyValuePair<double, double>> GenerateDefaultKeyPoints(
            double startGain, double gainStep, int keyPointCount = DEFAULT_KEY_POINT_COUNT)
        {
            var keyPoints = new ObservableCollection<KeyValuePair<double, double>>();

            double totalRange = gainStep * 31;
            double keyStep = totalRange / (keyPointCount - 1);

            for (int i = 0; i < keyPointCount; i++)
            {
                double x = startGain + keyStep * i;
                double y = 128.0;
                keyPoints.Add(new KeyValuePair<double, double>(x, y));
            }

            return keyPoints;
        }
    }
}
