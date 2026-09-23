using System;
using System.Management;
using System.Text;
using HonorControl.Models;

namespace HonorControl.Services
{
    public sealed class OemWmiClient
    {
        private const string NamespacePath = @"\\.\root\wmi";
        private const string InstanceName = @"ACPI\PNP0C14\HWMI_0";

        public ChargeThreshold GetChargeThreshold()
        {
            Response response = Send(0x1103);
            RequireSuccess(response, "读取充电阈值");
            if (response.Output.Length < 3)
            {
                throw new InvalidOperationException("充电阈值响应长度不足。");
            }
            return new ChargeThreshold(response.Output[1], response.Output[2]);
        }

        public ChargeThreshold SetChargeThreshold(int start, int end)
        {
            if (start < 0 || start > 100 || end < 0 || end > 100 || start > end)
            {
                throw new ArgumentOutOfRangeException("start", "充电阈值必须满足 0 <= start <= end <= 100。");
            }
            Response response = Send(0x1003, (byte)start, (byte)end);
            RequireSuccess(response, "设置充电阈值");
            return GetChargeThreshold();
        }

        public PerformanceStatus GetPerformanceStatus()
        {
            Response mode = Send(0x0802);
            RequireSuccess(mode, "读取性能模式状态");
            Response support = Send(0x3C06);
            RequireSuccess(support, "读取性能模式支持信息");
            return new PerformanceStatus(ToHex(mode.Output, 8), ToHex(support.Output, 8));
        }

        public PerformanceStatus SetPerformanceMode(int mode)
        {
            if (mode < 1 || mode > 3)
            {
                throw new ArgumentOutOfRangeException("mode", "性能模式值必须为 1、2 或 3。");
            }

            // PerfCommonPlugin.dll SetTurboMode(mode): byte 2 is mode - 1.
            Response response = Send(0x0C07, (byte)(mode - 1));
            RequireSuccess(response, "设置性能模式");
            return GetPerformanceStatus();
        }

        private static Response Send(ushort command, params byte[] payload)
        {
            if (payload.Length > 62)
            {
                throw new ArgumentOutOfRangeException("payload", "WMI 短命令载荷不能超过 62 字节。");
            }

            byte[] input = new byte[64];
            input[0] = (byte)(command & 0xFF);
            input[1] = (byte)((command >> 8) & 0xFF);
            Array.Copy(payload, 0, input, 2, payload.Length);

            ManagementScope scope = new ManagementScope(NamespacePath);
            scope.Connect();
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM OemWMIMethod")))
            using (ManagementObjectCollection objects = searcher.Get())
            {
                foreach (ManagementObject candidate in objects)
                {
                    string? name = Convert.ToString(candidate["InstanceName"]);
                    if (!String.Equals(name, InstanceName, StringComparison.OrdinalIgnoreCase))
                    {
                        candidate.Dispose();
                        continue;
                    }

                    using (candidate)
                    using (ManagementBaseObject parameters = candidate.GetMethodParameters("OemWMIfun"))
                    {
                        parameters["u8Input"] = input;
                        using (ManagementBaseObject result = candidate.InvokeMethod("OemWMIfun", parameters, null))
                        {
                            byte[] output = ConvertToByteArray(result["u8Output"]);
                            if (output.Length == 0)
                            {
                                throw new InvalidOperationException("BIOS WMI 调用没有返回 u8Output。");
                            }
                            return new Response(output[0], output);
                        }
                    }
                }
            }
            throw new InvalidOperationException("未找到荣耀 HWMI 接口。此机型可能不支持该接口。");
        }

        private static byte[] ConvertToByteArray(object value)
        {
            byte[]? bytes = value as byte[];
            if (bytes != null)
            {
                return bytes;
            }

            Array? values = value as Array;
            if (values == null)
            {
                return new byte[0];
            }
            bytes = new byte[values.Length];
            for (int index = 0; index < values.Length; index++)
            {
                bytes[index] = Convert.ToByte(values.GetValue(index));
            }
            return bytes;
        }

        private static void RequireSuccess(Response response, string operation)
        {
            if (response.Status != 0)
            {
                throw new InvalidOperationException(String.Format("{0} 被 BIOS 拒绝，状态码：0x{1:X2}。", operation, response.Status));
            }
        }

        private static string ToHex(byte[] bytes, int count)
        {
            int limit = Math.Min(bytes.Length, count);
            StringBuilder text = new StringBuilder(limit * 3);
            for (int index = 0; index < limit; index++)
            {
                if (index > 0) text.Append(' ');
                text.Append(bytes[index].ToString("X2"));
            }
            return text.ToString();
        }

        private sealed class Response
        {
            public Response(byte status, byte[] output)
            {
                Status = status;
                Output = output;
            }

            public byte Status { get; private set; }
            public byte[] Output { get; private set; }
        }
    }
}
