using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ResBinManager.Core;

namespace ResBinWriterTests
{
    class Program
    {
        static int _passed;
        static int _failed;

        static void Main()
        {
            Console.WriteLine("=== ResBinWriter ReplaceWithShift 优化测试 ===\n");

            RunTest("正确性-基础扩容", TestBasicExpand);
            RunTest("正确性-后续资源偏移更新", TestSubsequentOffsetUpdate);
            RunTest("正确性-后续资源数据完整", TestSubsequentDataPreserved);
            RunTest("正确性-小增量重叠覆盖", TestSmallDeltaOverlap);
            RunTest("正确性-大增量无重叠", TestLargeDeltaNoOverlap);
            RunTest("边界-最后资源扩容(无后续数据)", TestLastResourceExpand);
            RunTest("边界-单资源文件", TestSingleResource);
            RunTest("边界-连续多次扩容", TestMultipleExpansions);
            RunTest("边界-扩容后缩容再扩容", TestExpandShrinkExpand);
            RunTest("边界-表文件一致性(FIX-2)", TestTableConsistency);

            Console.WriteLine($"\n=== 测试汇总: {_passed} 通过, {_failed} 失败 ===\n");

            if (_failed == 0)
                RunPerformanceBenchmark();

            Console.WriteLine("\n按任意键退出...");
            Console.ReadKey();
        }

        static void RunTest(string name, Func<bool> test)
        {
            Console.Write($"  [{name}]... ");
            try
            {
                if (test())
                {
                    Console.WriteLine("PASS");
                    _passed++;
                }
                else
                {
                    Console.WriteLine("FAIL");
                    _failed++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"EXCEPTION: {ex.Message}");
                _failed++;
            }
        }

        // ========== 测试数据工厂 ==========

        static (byte[] fileData, uint tableOffset, List<ResInfoEntry> table)
            CreateFakeFile(int dataSize, int numResources)
        {
            int tableSize = numResources * 8;
            int totalSize = dataSize + tableSize;
            var data = new byte[totalSize];
            new Random(42).NextBytes(data);

            uint tableOffset = (uint)dataSize;
            var table = new List<ResInfoEntry>(numResources);

            uint prevEnd = 0;
            int resDataSize = dataSize / numResources;
            for (int i = 0; i < numResources; i++)
            {
                uint len = (i == numResources - 1)
                    ? (uint)(dataSize - prevEnd)
                    : (uint)resDataSize;
                table.Add(new ResInfoEntry { Offset = prevEnd, Length = len });
                prevEnd += len;
            }

            uint tblPos = tableOffset;
            foreach (var entry in table)
            {
                BitConverter.GetBytes(entry.Offset).CopyTo(data, tblPos);
                BitConverter.GetBytes(entry.Length).CopyTo(data, tblPos + 4);
                tblPos += 8;
            }

            return (data, tableOffset, table);
        }

        static byte[] CreateKnownData(int size, byte startValue)
        {
            var data = new byte[size];
            for (int i = 0; i < size; i++)
                data[i] = (byte)((startValue + i) & 0xFF);
            return data;
        }

        // ========== 测试用例 ==========

        static bool TestBasicExpand()
        {
            var (data, tblOff, table) = CreateFakeFile(200, 3);
            var writer = new ResBinWriter(data, tblOff, table);
            uint oldLen = table[0].Length;
            uint newLen = oldLen + 50;
            var newData = CreateKnownData((int)newLen, 0xAA);

            if (!writer.ReplaceResource(0, newData)) return false;

            var outTable = writer.GetTable();
            if (outTable[0].Length != newLen) return false;

            var outData = writer.GetData();
            if (outData.Length != data.Length + 50) return false;

            for (int i = 0; i < newLen; i++)
                if (outData[(int)(outTable[0].Offset + i)] != newData[i])
                    return false;

            return true;
        }

        static bool TestSubsequentOffsetUpdate()
        {
            var (data, tblOff, table) = CreateFakeFile(500, 5);
            var origOff = table.Select(e => e.Offset).ToArray();

            var writer = new ResBinWriter(data, tblOff, table);
            uint delta = 100;
            var newData = CreateKnownData((int)(table[1].Length + delta), 0xBB);
            if (!writer.ReplaceResource(1, newData)) return false;

            var outTable = writer.GetTable();
            // 资源0不受影响
            if (outTable[0].Offset != origOff[0]) return false;
            // 资源1偏移不变(原地替换)
            if (outTable[1].Offset != origOff[1]) return false;
            // 资源2+偏移增加delta
            for (int i = 2; i < 5; i++)
                if (outTable[i].Offset != origOff[i] + delta)
                    return false;
            return true;
        }

        static bool TestSubsequentDataPreserved()
        {
            var (data, tblOff, table) = CreateFakeFile(500, 5);
            int resIdx = 3;
            uint origOff = table[resIdx].Offset;
            uint origLen = table[resIdx].Length;
            var origData = new byte[origLen];
            Array.Copy(data, origOff, origData, 0, origLen);

            var writer = new ResBinWriter(data, tblOff, table);
            uint delta = 100;
            var newData = CreateKnownData((int)(table[1].Length + delta), 0xBB);
            if (!writer.ReplaceResource(1, newData)) return false;

            var outTable = writer.GetTable();
            var outData = writer.GetData();
            uint expectedOffset = origOff + delta;

            if (outTable[resIdx].Offset != expectedOffset) return false;
            for (int i = 0; i < origLen; i++)
                if (outData[(int)(expectedOffset + i)] != origData[i])
                    return false;
            return true;
        }

        static bool TestSmallDeltaOverlap()
        {
            var (data, tblOff, table) = CreateFakeFile(300, 4);
            var writer = new ResBinWriter(data, tblOff, table);
            var newData = CreateKnownData((int)(table[0].Length + 5), 0xCC);
            if (!writer.ReplaceResource(0, newData)) return false;
            return writer.GetData().Length == data.Length + 5;
        }

        static bool TestLargeDeltaNoOverlap()
        {
            var (data, tblOff, table) = CreateFakeFile(300, 2);
            var writer = new ResBinWriter(data, tblOff, table);
            // delta(200) > remaining data after resource 0, so no overlap
            uint newLen = table[0].Length + 250;
            var newData = CreateKnownData((int)newLen, 0xDD);
            if (!writer.ReplaceResource(0, newData)) return false;
            return writer.GetData().Length == data.Length + 250;
        }

        static bool TestLastResourceExpand()
        {
            var (data, tblOff, table) = CreateFakeFile(200, 3);
            var writer = new ResBinWriter(data, tblOff, table);
            int last = table.Count - 1;
            var newData = CreateKnownData((int)(table[last].Length + 30), 0xEE);
            if (!writer.ReplaceResource((uint)last, newData)) return false;
            return writer.GetData().Length == data.Length + 30;
        }

        static bool TestSingleResource()
        {
            var (data, tblOff, table) = CreateFakeFile(100, 1);
            var writer = new ResBinWriter(data, tblOff, table);
            var newData = CreateKnownData((int)(table[0].Length + 20), 0xFF);
            if (!writer.ReplaceResource(0, newData)) return false;
            return writer.GetData().Length == data.Length + 20;
        }

        static bool TestMultipleExpansions()
        {
            var (data, tblOff, table) = CreateFakeFile(300, 3);
            var writer = new ResBinWriter(data, tblOff, table);
            uint cumulativeDelta = 0;

            for (int iter = 0; iter < 3; iter++)
            {
                uint oldLen = writer.GetTable()[1].Length;
                uint add = (uint)(30 * (iter + 1));
                var newData = CreateKnownData((int)(oldLen + add), (byte)(0x10 + iter));
                if (!writer.ReplaceResource(1, newData)) return false;
                cumulativeDelta += add;
            }

            return writer.GetData().Length == data.Length + cumulativeDelta;
        }

        static bool TestExpandShrinkExpand()
        {
            var (data, tblOff, table) = CreateFakeFile(400, 4);
            var writer = new ResBinWriter(data, tblOff, table);
            uint baseLen = table[2].Length;
            uint origSize = (uint)data.Length;

            // 扩容 +50
            var bigData = CreateKnownData((int)(baseLen + 50), 0x11);
            if (!writer.ReplaceResource(2, bigData)) return false;
            if (writer.GetData().Length != origSize + 50) return false;

            // 缩容回原大小
            var sameData = CreateKnownData((int)baseLen, 0x22);
            if (!writer.ReplaceResource(2, sameData)) return false;
            if (writer.GetData().Length != origSize) return false;

            // 再扩容 +30
            var bigData2 = CreateKnownData((int)(baseLen + 30), 0x33);
            if (!writer.ReplaceResource(2, bigData2)) return false;
            if (writer.GetData().Length != origSize + 30) return false;

            // 验证后续资源偏移正确
            var outTable = writer.GetTable();
            return outTable[3].Offset == table[3].Offset + 30;
        }

        // 验证替换后文件内的资源表与内存表一致（FIX-2: RewriteAllTableEntries）
        static bool TestTableConsistency()
        {
            var (data, tblOff, table) = CreateFakeFile(400, 4);
            var writer = new ResBinWriter(data, tblOff, table);

            // 扩容: ReplaceWithShift
            var expandData = CreateKnownData((int)(table[2].Length + 80), 0x11);
            if (!writer.ReplaceResource(2, expandData)) return false;

            var outData = writer.GetData();
            var outTable = writer.GetTable();

            for (int i = 0; i < outTable.Count; i++)
            {
                uint fileOff = BitConverter.ToUInt32(outData, (int)(tblOff + i * 8));
                uint fileLen = BitConverter.ToUInt32(outData, (int)(tblOff + i * 8 + 4));
                if (fileOff != outTable[i].Offset || fileLen != outTable[i].Length)
                    return false;
            }

            // 缩容: ReplaceCompact
            var shrinkData = CreateKnownData((int)(outTable[2].Length - 30), 0x22);
            if (!writer.ReplaceResource(2, shrinkData)) return false;

            outData = writer.GetData();
            outTable = writer.GetTable();

            for (int i = 0; i < outTable.Count; i++)
            {
                uint fileOff = BitConverter.ToUInt32(outData, (int)(tblOff + i * 8));
                uint fileLen = BitConverter.ToUInt32(outData, (int)(tblOff + i * 8 + 4));
                if (fileOff != outTable[i].Offset || fileLen != outTable[i].Length)
                    return false;
            }

            return true;
        }

        // ========== 性能基准测试 ==========

        static void RunPerformanceBenchmark()
        {
            Console.WriteLine("--- 性能对比 (Buffer.BlockCopy vs 逐字节循环) ---\n");
            Console.WriteLine($"{"数据规模",-12} {"旧版(μs)",-12} {"新版(μs)",-12} {"加速比",-10}");
            Console.WriteLine(new string('-', 48));

            int[] sizes = { 1024, 10 * 1024, 100 * 1024, 512 * 1024, 2 * 1024 * 1024 };

            foreach (var size in sizes)
            {
                // 确保缓冲区足够容纳偏移
                int bufSize = size + size / 4 + 4096;
                var data = new byte[bufSize];
                new Random(123).NextBytes(data);

                int srcOff = 0;
                int dstOff = size / 4;
                int len = size;
                int iterations = (size < 100 * 1024) ? 500 : 50;

                // 旧版: byte-by-byte backward loop
                var swOld = Stopwatch.StartNew();
                for (int iter = 0; iter < iterations; iter++)
                {
                    var buf = new byte[bufSize];
                    Array.Copy(data, buf, data.Length);
                    for (long i = len - 1; i >= 0; i--)
                        buf[dstOff + i] = buf[srcOff + i];
                }
                swOld.Stop();
                long oldUs = swOld.ElapsedMilliseconds * 1000 / iterations;

                // 新版: Buffer.BlockCopy
                var swNew = Stopwatch.StartNew();
                for (int iter = 0; iter < iterations; iter++)
                {
                    var buf = new byte[bufSize];
                    Array.Copy(data, buf, data.Length);
                    Buffer.BlockCopy(buf, srcOff, buf, dstOff, len);
                }
                swNew.Stop();
                long newUs = swNew.ElapsedMilliseconds * 1000 / iterations;

                double speedup = (double)oldUs / Math.Max(newUs, 1);
                string speedStr = speedup > 100 ? $"{speedup,-10:F0}x" : $"{speedup,-10:F2}x";
                Console.WriteLine($"{FormatSize(size),-12} {oldUs,-12} {newUs,-12} {speedStr}");
            }
            Console.WriteLine();
        }

        static string FormatSize(int bytes)
        {
            if (bytes >= 1024 * 1024) return $"{bytes / (1024 * 1024)}MB";
            if (bytes >= 1024) return $"{bytes / 1024}KB";
            return $"{bytes}B";
        }
    }

    static class WriterExtensions
    {
        public static List<ResInfoEntry> GetTable(this ResBinWriter writer)
        {
            return writer.GetResourceTable();
        }
    }
}
