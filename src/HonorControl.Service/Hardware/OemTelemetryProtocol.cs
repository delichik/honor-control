namespace HonorControl.Services;

// Parses the documented Honor packets without opening CIM or sending firmware commands.
public static class OemTelemetryProtocol
{
    public const ushort PerformanceSetCommand = 0x0F04;

    public static byte PerformancePayload(int mode) => mode switch
    {
        1 => 0,
        2 => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), "性能模式必须是智能或高能。")
    };

    public static int ParsePerformanceMode(byte[] output)
    {
        RequirePacket(output, 2);
        return output[1] switch { 0 => 1, 1 => 2, _ => 0 };
    }

    public static double ParseBatteryTemperature(byte[] output)
    {
        RequirePacket(output, 3);
        if (output[1] > 1) throw new InvalidOperationException("电池 NTC 温度符号无效。");
        double temperature = output[1] == 1 ? -output[2] : output[2];
        if (temperature is < -40 or > 100) throw new InvalidOperationException("电池 NTC 温度超出合理范围。");
        return temperature;
    }

    public static double ParseAdapterValue(byte[] output, bool voltage)
    {
        RequirePacket(output, 4);
        if (output[1] > 1) throw new InvalidOperationException("USB 电压/电流符号无效。");
        double value = (output[2] | output[3] << 8) / 1000.0;
        if (output[1] == 1) value = -value;
        if (value < 0 || value > (voltage ? 60 : 20))
            throw new InvalidOperationException("USB 输入电压/电流超出合理范围。");
        return value;
    }

    private static void RequirePacket(byte[] output, int length)
    {
        if (output.Length < length) throw new InvalidOperationException("荣耀 HWMI 响应长度不足。");
        if (output[0] != 0) throw new InvalidOperationException($"荣耀 HWMI GET 被 BIOS 拒绝，状态码：0x{output[0]:X2}。");
    }
}

public sealed record OemTelemetryReading(
    double? BatteryTemperatureC = null,
    string? BatteryTemperatureError = null,
    double? AdapterVoltageV = null,
    double? AdapterCurrentA = null,
    double? AdapterReportedPowerW = null,
    string? AdapterDiagnosticError = null);
