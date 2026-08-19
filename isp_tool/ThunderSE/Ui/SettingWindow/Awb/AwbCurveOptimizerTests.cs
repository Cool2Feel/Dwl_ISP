using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using NUnit.Framework;
using ThunderSE.DeviceConfig.Isp;

namespace ThunderSE.Tests.AWB
{
    /// <summary>
    /// AWB曲线优化器单元测试
    /// 验证Douglas-Peucker算法、自适应采样、格式转换等核心功能的正确性
    /// </summary>
    [TestFixture]
    public class AwbCurveOptimizerTests
    {
        #region 测试数据准备

        /// <summary>
        /// 生成模拟的32点AWB曲线数据（模拟真实场景）
        /// </summary>
        private List<KeyValuePair<double, double>> GenerateTestCurve(int seed = 42)
        {
            var random = new Random(seed);
            var points = new List<KeyValuePair<double, double>>(32);
            
            byte rgainStart = 170;
            byte gainStep = 16;

            for (int i = 0; i < 32; i++)
            {
                double x = rgainStart + i * gainStep;
                
                // 模拟典型的AWB曲线形状（S形曲线+噪声）
                double t = (double)i / 31.0;  // 归一化到[0,1]
                double baseValue = 100 + 100 * (1.0 / (1.0 + Math.Exp(-10 * (t - 0.5)))); // Sigmoid
                double noise = (random.NextDouble() - 0.5) * 10;  // 小噪声
                
                double y = baseValue + noise;
                y = Math.Max(0, Math.Min(255, y));  // 裁剪到有效范围
                
                points.Add(new KeyValuePair<double, double>(x, y));
            }

            return points;
        }

        /// <summary>
        /// 生成标准的128字节legacy格式数据
        /// </summary>
        private byte[] GenerateLegacy128Bytes()
        {
            var random = new Random(12345);
            var data = new byte[128];
            
            for (int i = 0; i < 128; i++)
            {
                // 模拟4条不同的曲线
                int curveIdx = i / 32;
                int pointInCurve = i % 32;
                
                double t = (double)pointInCurve / 31.0;
                double baseValue;
                
                switch (curveIdx)
                {
                    case 0: // bgain_out_high: 递减曲线
                        baseValue = 180 - 60 * t;
                        break;
                    case 1: // bgain_in_high: 平稳曲线
                        baseValue = 140 + 20 * Math.Sin(t * Math.PI);
                        break;
                    case 2: // bgain_in_low: 递增曲线
                        baseValue = 80 + 40 * t;
                        break;
                    case 3: // bgain_out_low: U型曲线
                        baseValue = 100 + 50 * Math.Sin(t * Math.PI) * (1 - t);
                        break;
                    default:
                        baseValue = 128;
                        break;
                }
                
                double noise = (random.NextDouble() - 0.5) * 8;
                data[i] = (byte)Math.Max(0, Math.Min(255, baseValue + noise));
            }
            
            return data;
        }

        #endregion

        #region Douglas-Peucker 算法测试

        [Test]
        public void TestDouglasPeucker_BasicFunctionality()
        {
            // Arrange
            var curve = GenerateTestCurve();
            
            // Act
            var indices = AwbCurveOptimizer.DouglasPeucker(curve, epsilon: 3.0);
            
            // Assert
            Assert.IsNotNull(indices, "Result should not be null");
            Assert.IsTrue(indices.Count >= 2, "Should have at least start and end points");
            Assert.AreEqual(0, indices.First(), "First point should be index 0");
            Assert.AreEqual(curve.Count - 1, indices.Last(), "Last point should be the last index");
            
            // 验证索引是严格递增的
            for (int i = 1; i < indices.Count; i++)
            {
                Assert.IsTrue(indices[i] > indices[i - 1], 
                    $"Indices should be strictly increasing at position {i}");
            }
        }

        [Test]
        public void TestDouglasPeucker_PreservesKeyFeatures()
        {
            // Arrange - 创建有明显拐点的曲线
            var points = new List<KeyValuePair<double, double>>
            {
                new KeyValuePair<double, double>(0, 0),
                new KeyValuePair<double, double>(10, 0),   // 平坦段
                new KeyValuePair<double, double>(20, 50),  // 开始上升
                new KeyValuePair<double, double>(30, 100), // 急剧上升
                new KeyValuePair<double, double>(40, 120), // 趋于平缓
                new KeyValuePair<double, double>(50, 125), // 接近峰值
                new KeyValuePair<double, double>(60, 120), // 开始下降
                new KeyValuePair<double, double>(70, 80),  // 下降段
                new KeyValuePair<double, double>(80, 30),  // 继续下降
                new KeyValuePair<double, double>(90, 10),  // 趋于平缓
                new KeyValuePair<double, double>(100, 5)   // 结束
            };
            
            // Act - 使用较小的epsilon以保留更多特征点
            var indices = AwbCurveOptimizer.DouglasPeucker(points, epsilon: 5.0);
            
            // Assert - 应该保留拐点附近的点
            Assert.IsTrue(indices.Contains(2), "Should preserve point where curve starts rising");
            Assert.IsTrue(indices.Contains(3), "Should preserve steep rise section");
            Assert.IsTrue(indices.Contains(6), "Should preserve peak area");
            Assert.IsTrue(indices.Contains(7), "Should preserve where curve starts falling");
            
            Console.WriteLine($"Original: {points.Count} points → Simplified: {indices.Count} points");
        }

        [Test]
        public void TestDouglasPeucker_EpsilonParameterEffect()
        {
            // Arrange
            var curve = GenerateTestCurve();
            
            // Act - 使用不同epsilon值
            var tightIndices = AwbCurveOptimizer.DouglasPeucker(curve, epsilon: 1.0);  // 严格
            var looseIndices = AwbCurveOptimizer.DouglasPeucker(curve, epsilon: 10.0); // 宽松
            
            // Assert - 更小的epsilon应该产生更多的点
            Assert.IsTrue(tightIndices.Count >= looseIndices.Count,
                "Tighter epsilon should produce more or equal points");
            
            Console.WriteLine($"Epsilon=1.0: {tightIndices.Count} points");
            Console.WriteLine($"Epsilon=10.0: {looseIndices.Count} points");
        }

        #endregion

        #region 自适应降采样测试

        [Test]
        public void TestAdaptiveSampling_TargetPointCount()
        {
            // Arrange
            var curve = GenerateTestCurve();
            int targetPoints = 10;
            
            // Act
            var simplified = AwbCurveOptimizer.AdaptiveSampling(curve, targetPoints);
            
            // Assert
            Assert.IsNotNull(simplified);
            Assert.IsTrue(simplified.Count <= targetPoints + 2, 
                $"Should have approximately {targetPoints} points (got {simplified.Count})");
            Assert.IsTrue(simplified.Count >= 3, 
                "Should have at least a few control points");
            
            // 验证X坐标是单调递增的
            for (int i = 1; i < simplified.Count; i++)
            {
                Assert.IsTrue(simplified[i].Key > simplified[i-1].Key,
                    "X coordinates should be monotonically increasing");
            }
        }

        [Test]
        public void TestAdaptiveSampling_PreservesEndpoints()
        {
            // Arrange
            var curve = GenerateTestCurve();
            
            // Act
            var simplified = AwbCurveOptimizer.AdaptiveSampling(curve, 10);
            
            // Assert - 应该保留端点
            Assert.AreEqual(curve.First().Key, simplified.First().Key, 0.001,
                "Start X coordinate should match");
            Assert.AreEqual(curve.Last().Key, simplified.Last().Key, 0.001,
                "End X coordinate should match");
        }

        [Test]
        public void TestAdaptiveSampling_QualityMetrics()
        {
            // Arrange
            var originalCurve = GenerateTestCurve(seed: 99);
            
            // Act
            var simplifiedCurve = AwbCurveOptimizer.AdaptiveSampling(originalCurve, 10);
            var quality = AwbCurveOptimizer.EvaluateSimplificationQuality(originalCurve, simplifiedCurve);
            
            // Assert - 质量指标应在可接受范围内
            Assert.IsNotNull(quality);
            Assert.IsTrue(quality.CompressionRatio > 2.0, 
                $"Compression ratio should be significant (got {quality.CompressionRatio:F1}x)");
            Assert.IsTrue(quality.MeanError < 5.0, 
                $"Mean error should be small (got {quality.MeanError:F2})");
            Assert.IsTrue(quality.MaxError < 15.0, 
                $"Max error should be acceptable (got {quality.MaxError:F2})");
            
            Console.WriteLine($"Quality Report:");
            Console.WriteLine($"  Compression: {quality.CompressionRatio:F1}x");
            Console.WriteLine($"  Mean Error:  {quality.MeanError:F3}");
            Console.WriteLine($"  Max Error:   {quality.MaxError:F3}");
            Console.WriteLine($"  Within Tol:  {quality.IsWithinTolerance}");
        }

        #endregion

        #region 格式转换测试

        [Test]
        public void TestMigrateFromLegacyFormat_StructureIntegrity()
        {
            // Arrange
            var legacyData = GenerateLegacy128Bytes();
            byte rgainStart = 170;
            
            // Act
            var optimizedCurves = AwbCurveOptimizer.MigrateFromLegacyFormat(legacyData, rgainStart);
            
            // Assert
            Assert.IsNotNull(optimizedCurves);
            Assert.AreEqual(4, optimizedCurves.Length, "Should have 4 curves");
            
            foreach (var curve in optimizedCurves)
            {
                Assert.IsTrue(curve.NumPoints >= 8 && curve.NumPoints <= 12,
                    $"Each curve should have 8-12 points (got {curve.NumPoints})");
                Assert.IsNotNull(curve.ControlPointsX);
                Assert.IsNotNull(curve.ControlPointsY);
                Assert.AreEqual(curve.NumPoints, curve.ControlPointsX.Length,
                    "ControlPointsX length should match NumPoints");
                Assert.AreEqual(curve.NumPoints, curve.ControlPointsY.Length,
                    "ControlPointsY length should match NumPoints");
                Assert.AreEqual(rgainStart, curve.RGainStart,
                    "RGainStart should match input parameter");
            }
            
            // 计算总存储节省
            int totalOptimizedSize = optimizedCurves.Sum(c => c.TotalSize);
            double reduction = (1 - (double)totalOptimizedSize / 128) * 100;
            Console.WriteLine($"Storage: 128 bytes → {totalOptimizedSize} bytes ({reduction:F1}% reduction)");
        }

        [Test]
        public void TestConvertToLegacyFormat_RoundTripConsistency()
        {
            // Arrange
            var originalLegacy = GenerateLegacy128Bytes();
            var optimizedCurves = AwbCurveOptimizer.MigrateFromLegacyFormat(originalLegacy, 170);
            
            // Act - 转换为优化格式再转回legacy格式
            var restoredLegacy = AwbCurveOptimizer.ConvertToLegacyFormat(optimizedCurves);
            
            // Assert
            Assert.IsNotNull(restoredLegacy);
            Assert.AreEqual(128, restoredLegacy.Length, "Restored data should be 128 bytes");
            
            // 计算转换误差
            double maxError = 0;
            double meanSquaredError = 0;
            
            for (int i = 0; i < 128; i++)
            {
                double error = Math.Abs(restoredLegacy[i] - originalLegacy[i]);
                maxError = Math.Max(maxError, error);
                meanSquaredError += error * error;
            }
            meanSquaredError /= 128;
            double rmse = Math.Sqrt(meanSquaredError);
            
            // 允许一定的插值误差（由于降采样+上采样）
            Assert.IsTrue(rmse < 10.0, 
                $"Round-trip RMSE should be small (got {rmse:F2})");
            Assert.IsTrue(maxError < 25.0,
                $"Max round-trip error should be acceptable (got {maxError:F1})");
            
            Console.WriteLine($"Round-trip conversion quality:");
            Console.WriteLine($"  RMSE:      {rmse:F3}");
            Console.WriteLine($"  Max Error: {maxError:F1}");
        }

        [Test]
        public void TestFormatConversion_PreservesCurveShape()
        {
            // Arrange - 创建一条简单的线性递增曲线
            var simpleLegacy = new byte[32];
            for (int i = 0; i < 32; i++)
            {
                simpleLegacy[i] = (byte)(100 + i * 4);  // 线性: 100 → 228
            }
            
            // Pad to 128 bytes (4 curves, use same data for all)
            var fullLegacy = new byte[128];
            for (int c = 0; c < 4; c++)
            {
                Array.Copy(simpleLegacy, 0, fullLegacy, c * 32, 32);
            }
            
            // Act
            var optimized = AwbCurveOptimizer.MigrateFromLegacyFormat(fullLegacy, 170);
            var restored = AwbCurveOptimizer.ConvertToLegacyFormat(optimized);
            
            // Assert - 对于简单线性曲线，误差应该非常小
            double maxDeviation = 0;
            for (int i = 0; i < 32; i++)
            {
                double deviation = Math.Abs(restored[i] - simpleLegacy[i]);
                maxDeviation = Math.Max(maxDeviation, deviation);
            }
            
            Assert.IsTrue(maxDeviation < 5.0,
                $"Simple linear curve should have minimal error (got {maxDeviation:F1})");
        }

        #endregion

        #region 边界条件和异常处理测试

        [Test]
        public void TestEdgeCase_EmptyInput()
        {
            // Arrange
            var emptyList = new List<KeyValuePair<double, double>>();
            
            // Act & Assert - 不应抛出异常
            var result = AwbCurveOptimizer.AdaptiveSampling(emptyList, 10);
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count, "Empty input should produce empty output");
        }

        [Test]
        public void TestEdgeCase_SinglePoint()
        {
            // Arrange
            var singlePoint = new List<KeyValuePair<double, double>>
            {
                new KeyValuePair<double, double>(100, 150)
            };
            
            // Act & Assert
            var result = AwbCurveOptimizer.AdaptiveSampling(singlePoint, 10);
            Assert.AreEqual(1, result.Count, "Single point should remain as single point");
        }

        [Test]
        public void TestEdgeCase_TwoPoints()
        {
            // Arrange
            var twoPoints = new List<KeyValuePair<double, double>>
            {
                new KeyValuePair<double, double>(0, 0),
                new KeyValuePair<double, double>(100, 255)
            };
            
            // Act & Assert
            var result = AwbCurveOptimizer.AdaptiveSampling(twoPoints, 10);
            Assert.AreEqual(2, result.Count, "Two points should remain unchanged");
        }

        [Test]
        public void TestEdgeCase_LessThanTargetPoints()
        {
            // Arrange - 只有5个点的曲线，目标要求10个点
            var fewPoints = new List<KeyValuePair<double, double>>();
            for (int i = 0; i < 5; i++)
            {
                fewPoints.Add(new KeyValuePair<double, double>(i * 20, 50 + i * 30));
            }
            
            // Act & Assert - 不应超过原始点数
            var result = AwbCurveOptimizer.AdaptiveSampling(fewPoints, 10);
            Assert.IsTrue(result.Count <= fewPoints.Count,
                "Output should not exceed input size when input is smaller than target");
        }

        [Test]
        public void TestInvalidInput_NullLegacyFormat()
        {
            // Act & Assert - 应抛出ArgumentException
            Assert.Throws<ArgumentException>(() => 
                AwbCurveOptimizer.MigrateFromLegacyFormat(null, 170),
                "Null input should throw ArgumentException");
        }

        [Test]
        public void TestInvalidInput_WrongSizeLegacyFormat()
        {
            // Arrange
            var wrongSize = new byte[100]; // Should be 128
            
            // Act & Assert - 应抛出ArgumentException
            Assert.Throws<ArgumentException>(() => 
                AwbCurveOptimizer.MigrateFromLegacyFormat(wrongSize, 170),
                "Wrong size input should throw ArgumentException");
        }

        #endregion

        #region 性能基准测试

        [Test]
        public void PerformanceTest_DouglasPeucker_LargeDataset()
        {
            // Arrange - 生成大数据集
            var largeCurve = new List<KeyValuePair<double, double>>(1000);
            var random = new Random(42);
            for (int i = 0; i < 1000; i++)
            {
                largeCurve.Add(new KeyValuePair<double, double>(
                    i, 
                    100 + 50 * Math.Sin(i * 0.01) + (random.NextDouble() - 0.5) * 10));
            }
            
            // Act - 计时
            var watch = System.Diagnostics.Stopwatch.StartNew();
            
            for (int iteration = 0; iteration < 100; iteration++)
            {
                var result = AwbCurveOptimizer.DouglasPeucker(largeCurve, epsilon: 2.0);
            }
            
            watch.Stop();
            
            // Assert - 性能应该在合理范围内（100次迭代<500ms）
            Assert.IsTrue(watch.ElapsedMilliseconds < 500,
                $"Performance regression: 100 iterations took {watch.ElapsedMilliseconds}ms");
            
            Console.WriteLine($"Performance: 100 iterations in {watch.ElapsedMilliseconds}ms" +
                           $" ({watch.ElapsedMilliseconds / 100.0:F2}ms per call)");
        }

        [Test]
        public void PerformanceTest_CompleteOptimizationPipeline()
        {
            // Arrange
            var legacyData = GenerateLegacy128Bytes();
            
            // Act - 完整优化流程计时
            var watch = System.Diagnostics.Stopwatch.StartNew();
            
            const int iterations = 1000;
            for (int i = 0; i < iterations; i++)
            {
                var optimized = AwbCurveOptimizer.MigrateFromLegacyFormat(legacyData, 170);
                var restored = AwbCurveOptimizer.ConvertToLegacyFormat(optimized);
            }
            
            watch.Stop();
            
            // Assert - 完整流程应该在合理时间内完成
            double avgTimeMs = (double)watch.ElapsedMilliseconds / iterations;
            Assert.IsTrue(avgTimeMs < 1.0,
                $"Complete pipeline too slow: avg {avgTimeMs:F3}ms per iteration");
            
            Console.WriteLine($"Pipeline performance: {iterations} iterations in {watch.ElapsedMilliseconds}ms" +
                           $" (avg {avgTimeMs:F3}ms/iter)");
        }

        #endregion

        #region 集成测试

        [Test]
        public void IntegrationTest_RealWorldScenario()
        {
            // Arrange - 模拟真实的AWB标定场景
            Console.WriteLine("=== Integration Test: Real-world AWB Calibration Scenario ===\n");
            
            // Step 1: 模拟从设备采集的原始128字节数据
            var rawDeviceData = GenerateLegacy128Bytes();
            Console.WriteLine($"[Step 1] Acquired raw device data: {rawDeviceData.Length} bytes");
            
            // Step 2: 优化为紧凑格式
            var optimizedCurves = AwbCurveOptimizer.MigrateFromLegacyFormat(rawDeviceData, 170);
            int optimizedTotalSize = optimizedCurves.Sum(c => c.TotalSize);
            Console.WriteLine($"[Step 2] Optimized curves:");
            for (int c = 0; c < 4; c++)
            {
                Console.WriteLine($"  Curve {c}: {optimizedCurves[c].NumPoints} control points, " +
                               $"{optimizedCurves[c].TotalSize} bytes");
            }
            Console.WriteLine($"  Total: {optimizedTotalSize} bytes ({(1 - optimizedTotalSize/128.0)*100:F0}% smaller)\n");
            
            // Step 3: 验证质量
            var testStatisticData = new ObservableCollection<ObservableCollection<KeyValuePair<double, double>>>();
            for (int c = 0; c < 4; c++)
            {
                var curveData = new ObservableCollection<KeyValuePair<double, double>>();
                for (int i = 0; i < 32; i++)
                {
                    curveData.Add(new KeyValuePair<double, double>(
                        170 + i * 16, 
                        rawDeviceData[c * 32 + i]));
                }
                testStatisticData.Add(curveData);
            }
            
            var validationResults = AwbCurveOptimizer.ValidateAllCurves(testStatisticData);
            Console.WriteLine("[Step 3] Quality validation:");
            foreach (var kvp in validationResults)
            {
                var r = kvp.Value;
                Console.WriteLine($"  Curve {kvp.Key}: MaxErr={r.MaxError:F2}, MeanErr={r.MeanError:F2}, OK={r.IsWithinTolerance}");
                Assert.IsTrue(r.IsWithinTolerance, $"Curve {kvp.Key} should be within tolerance");
            }
            Console.WriteLine();
            
            // Step 4: 模拟保存到配置文件并重新加载
            var restoredData = AwbCurveOptimizer.ConvertToLegacyFormat(optimizedCurves);
            
            double roundTripRmse = 0;
            for (int i = 0; i < 128; i++)
            {
                roundTripRmse += Math.Pow(restoredData[i] - rawDeviceData[i], 2);
            }
            roundTripRmse = Math.Sqrt(roundTripRmse / 128);
            
            Console.WriteLine($"[Step 4] Round-trip consistency check:");
            Console.WriteLine($"  RMSE between original and restored: {roundTripRmse:F3}");
            Assert.IsTrue(roundTripRmse < 10.0, "Round-trip error should be minimal");
            Console.WriteLine();
            
            // Step 5: 生成报告
            string report = AwbCurveOptimizer.GenerateOptimizationReport(optimizedCurves);
            Console.WriteLine("[Step 5] Optimization report generated successfully");
            Console.WriteLine($"  Report length: {report.Length} characters\n");
            
            Console.WriteLine("=== Integration Test PASSED ✓ ===");
        }

        #endregion
    }
}
