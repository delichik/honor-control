using System.Globalization;
using System.Text;
using HonorControl.Contracts;

// 命名空间 …Service.Telemetry 与契约类型 Telemetry 同名，这里起别名区分。
using TelemetrySnapshot = HonorControl.Contracts.Telemetry;

namespace HonorControl.Service.Telemetry;

/// <summary>
/// 历史数据存储。
///
/// 形态：按天一个 CSV 文件，放在 <c>%ProgramData%\HonorControl\history\</c>（与该目录的既有 ACL 一致，
/// 只有 SYSTEM 与管理员可写）。60 秒一个采样点，保留 30 天——7 天 300 点的曲线只需要很小的数据量，
/// 不值得引入数据库依赖。
///
/// 查询时在服务端**降采样**：面板只要求"等间隔的数值数组 + 区间小时数"（见契约里的
/// <see cref="HistorySeries"/>），因为管道请求行有 4096 字节上限、服务端每请求 5 秒超时，
/// 不能把 7 天的原始点直接甩过去。
/// </summary>
internal sealed class HistoryStore
{
    private const int RetentionDays = 30;
    private static readonly string DefaultDirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HonorControl", "history");

    private readonly object sync = new();
    private readonly string directoryPath;

    public HistoryStore() : this(DefaultDirectoryPath)
    {
    }

    /// <summary>可注入目录：让诊断工具能在临时目录里验证读写与降采样，而不碰 ProgramData。</summary>
    internal HistoryStore(string directoryPath)
    {
        this.directoryPath = directoryPath;
        EnsureDirectory();
    }

    /// <summary>写入一个采样点。存储失败只记日志，绝不影响采样循环。</summary>
    public void Append(TelemetrySnapshot telemetry)
    {
        try
        {
            lock (sync)
            {
                EnsureDirectory();
                string line = string.Join(',',
                    telemetry.CheckedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                    Format(telemetry.BatteryPercent),
                    Format(telemetry.BatteryPowerW),
                    Format(telemetry.AdapterPowerW),
                    Format(telemetry.SystemLoadW),
                    telemetry.PluggedIn switch { true => "1", false => "0", null => string.Empty });

                string path = Path.Combine(directoryPath, telemetry.CheckedAt.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".csv");
                File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(false));
                PruneOldFiles();
            }
        }
        catch
        {
            // 历史是展示性数据：写不进去也不能影响采样与控制。
        }
    }

    /// <summary>
    /// 查询一个指标的等间隔序列。
    ///
    /// 返回的点数等于请求的 Points（数据不足时更少）。没有服务记录的时间桶返回 null，
    /// 不沿用旧值，也不补 0；面板据此在曲线上留出空档。
    /// </summary>
    public HistorySeries Query(HistoryQuery query)
    {
        (double hours, int defaultPoints) = query.Range switch
        {
            "1h" => (1, 60),
            "24h" => (24, 240),
            "7d" => (168, 300),
            _ => (24, 240),
        };

        int points = query.Points > 0 ? Math.Min(query.Points, 1000) : defaultPoints;
        DateTimeOffset until = DateTimeOffset.Now;
        DateTimeOffset from = until.AddHours(-hours);
        List<(DateTimeOffset Time, double? Value)> samples = ReadSamples(query.Metric, from, until);

        // 该指标在这段时间里**一个有效值都没有**（例如适配器功率：服务端没有可信来源，
        // 采样时一直写空字段）→ 返回空序列，面板显示暂无记录。
        // 不能返回一串 0：那会在界面上画出一条"功耗恒为 0"的假曲线。
        if (!samples.Any(sample => sample.Value.HasValue))
        {
            return new HistorySeries(query.Metric, query.Range, hours, Array.Empty<double?>());
        }

        double?[] result = new double?[points];
        double bucketSeconds = hours * 3600 / points;
        int cursor = 0;

        for (int bucket = 0; bucket < points; bucket++)
        {
            DateTimeOffset bucketEnd = from.AddSeconds((bucket + 1) * bucketSeconds);
            double sum = 0;
            int count = 0;
            while (cursor < samples.Count && samples[cursor].Time <= bucketEnd)
            {
                if (samples[cursor].Value.HasValue)
                {
                    sum += samples[cursor].Value!.Value;
                    count++;
                }
                cursor++;
            }

            result[bucket] = count > 0 ? sum / count : null;
        }

        return new HistorySeries(query.Metric, query.Range, hours, result);
    }

    private List<(DateTimeOffset, double?)> ReadSamples(string metric, DateTimeOffset from, DateTimeOffset until)
    {
        List<(DateTimeOffset, double?)> samples = new();
        for (DateTimeOffset day = from.Date; day <= until; day = day.AddDays(1))
        {
            string path = Path.Combine(directoryPath, day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".csv");
            if (!File.Exists(path)) continue;

            string[] lines;
            try
            {
                lines = File.ReadAllLines(path, Encoding.UTF8);
            }
            catch
            {
                continue;
            }

            foreach (string line in lines)
            {
                string[] parts = line.Split(',');
                if (parts.Length < 6) continue;
                if (!DateTimeOffset.TryParse(parts[0], CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTimeOffset time))
                {
                    continue;
                }
                if (time < from || time > until) continue;

                double? value = metric switch
                {
                    "BatteryPower" => Parse(parts[2]),
                    "AdapterPower" => Parse(parts[3]),
                    "SystemLoad" => Parse(parts[4]),
                    _ => null,
                };
                samples.Add((time, value));
            }
        }

        samples.Sort((left, right) => left.Item1.CompareTo(right.Item1));
        return samples;
    }

    private static double? Parse(string text) =>
        string.IsNullOrWhiteSpace(text) ? null : double.Parse(text, CultureInfo.InvariantCulture);

    private static string Format(double? value) =>
        value.HasValue ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : string.Empty;

    private void EnsureDirectory()
    {
        DirectoryInfo directory = Directory.CreateDirectory(directoryPath);
        // 防重解析点：与 ConfigurationStore 同样的加固思路，避免历史目录被指向别处。
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("历史数据目录不能是重解析点。");
        }
    }

    private void PruneOldFiles()
    {
        DateTime cutoff = DateTime.Now.Date.AddDays(-RetentionDays);
        foreach (string file in Directory.EnumerateFiles(directoryPath, "*.csv"))
        {
            if (DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date) && date < cutoff)
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // 删不掉就留着，下次再试。
                }
            }
        }
    }
}
