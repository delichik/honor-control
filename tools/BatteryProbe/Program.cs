using HonorControl.Service.Hardware;

namespace HonorControl.Tools.BatteryProbe;

/// <summary>
/// 只读诊断工具：调用**服务端真实的** <see cref="BatteryService"/>，把电池读数打印出来。
///
/// 为什么需要它：面板上的"电量 / 电池功率 / 插电状态"直接来自这里，而功率的**符号**、
/// 单位（毫瓦）以及"满充容量 ≠ 设计容量"这些语义必须先在真机上量过一次，
/// 猜错的后果是把放电显示成充电。
///
/// 本工具只读，不写任何硬件状态。
/// 用法：dotnet run --project tools/BatteryProbe
/// </summary>
internal static class Program
{
    private static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        BatteryService battery = new();
        BatteryReading reading = battery.Read();

        Console.WriteLine("=== BatteryService.Read()（服务端真实代码）===");
        Console.WriteLine($"  电池存在        {reading.Present}");
        Console.WriteLine($"  接入适配器      {reading.PluggedIn}");
        Console.WriteLine($"  充电中          {reading.Charging}");
        Console.WriteLine($"  放电中          {reading.Discharging}");
        Console.WriteLine($"  电量            {reading.Percent?.ToString() ?? "不可用"} %");
        Console.WriteLine($"  电池功率        {(reading.PowerW.HasValue ? $"{reading.PowerW.Value:+0.0;-0.0;0.0} W（正=充入）" : "不可用")}");
        Console.WriteLine($"  剩余容量        {(reading.RemainingWh.HasValue ? $"{reading.RemainingWh.Value:F2} Wh" : "不可用")}");
        Console.WriteLine($"  满充容量        {(reading.FullChargeWh.HasValue ? $"{reading.FullChargeWh.Value:F2} Wh" : "不可用")}");
        Console.WriteLine($"  健康度          {(reading.RemainingWh.HasValue && reading.FullChargeWh.HasValue ? "—（缺设计容量，服务端报 null）" : "—")}");

        Console.WriteLine();
        Console.WriteLine("=== 循环次数 ===");
        int? cycles = battery.GetCycleCount();
        Console.WriteLine($"  CycleCount      {(cycles.HasValue ? cycles.Value.ToString() : "不可用（机型不支持）")}");

        Console.WriteLine();
        Console.WriteLine("自检：");
        bool ok = reading.Present && reading.Percent.HasValue;
        // 充电时必须为正、放电时必须为负——这是面板判断充放方向的唯一依据。
        if (reading.Charging && reading.PowerW is < 0) ok = false;
        if (reading.Discharging && reading.PowerW is > 0) ok = false;
        Console.WriteLine(ok ? "  battery probe: OK" : "  battery probe: FAILED（读数自相矛盾）");
        return ok ? 0 : 1;
    }
}
