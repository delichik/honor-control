using HonorControl.Contracts;
using HonorControl.Service.Telemetry;

namespace HonorControl.Tools.HistoryProbe;

/// <summary>
/// 只读诊断工具：在临时目录里验证历史存储的写入、保留与**降采样**逻辑。
///
/// 为什么值得单独跑：面板的曲线完全依赖"等间隔数组 + 区间小时数"这个约定，
/// 分桶、边界、缺口补齐这些地方一旦写错，界面上就是一条形状可疑但看不出错的曲线。
/// 这个工具用合成数据把 1h / 24h / 7d 三种范围都跑一遍，并检查点数与均值。
///
/// 用法：dotnet run --project tools/HistoryProbe
/// </summary>
internal static class Program
{
    private static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        string directory = Path.Combine(Path.GetTempPath(), "honorcontrol-history-probe-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(directory);

        try
        {
            HistoryStore store = new(directory);

            // 合成 7 天数据：每分钟一个点，电池功率按正弦变化（便于人工核对均值）
            DateTimeOffset now = DateTimeOffset.Now;
            int written = 0;
            for (int minutesAgo = 7 * 24 * 60; minutesAgo >= 0; minutesAgo -= 1)
            {
                DateTimeOffset at = now.AddMinutes(-minutesAgo);
                double power = 20 * Math.Sin(minutesAgo / 240.0);
                store.Append(new Telemetry(
                    CheckedAt: at,
                    PluggedIn: power > 0,
                    BatteryPercent: 50,
                    BatteryPowerW: power,
                    AdapterPowerW: null,
                    SystemLoadW: power < 0 ? -power : null));
                written++;
            }

            Console.WriteLine($"写入 {written} 个采样点到 {directory}");
            Console.WriteLine();

            bool ok = true;
            ok &= Check(store, "BatteryPower", "1h", 60, -20, 20);
            ok &= Check(store, "BatteryPower", "24h", 240, -20, 20);
            ok &= Check(store, "BatteryPower", "7d", 300, -20, 20);

            // 适配器功率在服务端恒为 null（没有可信来源）→ 必须返回空序列，
            // 而不是画一条假的 0 线。
            HistorySeries adapter = store.Query(new HistoryQuery("AdapterPower", "24h", 240));
            Console.WriteLine($"AdapterPower 24h: {adapter.Samples.Count} 个点（期望 0——服务端不产这个指标）");
            ok &= adapter.Samples.Count == 0;

            Console.WriteLine();
            Console.WriteLine(ok ? "history probe: OK" : "history probe: FAILED");
            return ok ? 0 : 1;
        }
        finally
        {
            try { Directory.Delete(directory, true); } catch { /* 清理失败无所谓 */ }
        }
    }

    private static bool Check(HistoryStore store, string metric, string range, int points, double min, double max)
    {
        HistorySeries series = store.Query(new HistoryQuery(metric, range, points));
        bool countOk = series.Samples.Count == points;
        double[] values = series.Samples.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        double average = values.Length > 0 ? values.Average() : double.NaN;
        bool rangeOk = values.All(value => value >= min - 0.001 && value <= max + 0.001);
        // 7 天范围用 300 点覆盖 168 小时，正弦周期会互相抵消，均值应接近 0
        bool averageOk = Math.Abs(average) < 5;
        bool hoursOk = range switch
        {
            "1h" => Math.Abs(series.Hours - 1) < 0.001,
            "24h" => Math.Abs(series.Hours - 24) < 0.001,
            _ => Math.Abs(series.Hours - 168) < 0.001,
        };

        Console.WriteLine($"{metric} {range}: 点数={series.Samples.Count}/{points} 均值={average:F2} 区间内={rangeOk} 小时数={series.Hours}");
        return countOk && rangeOk && averageOk && hoursOk;
    }
}
