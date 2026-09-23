using System;
using System.Collections.Generic;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using HonorControl.Models;

namespace HonorControl.Services
{
    public sealed class OemWmiClient
    {
        private const string NamespacePath = @"\\.\root\wmi";
        private const string PreferredInstance = @"ACPI\PNP0C14\HWMI_0";

        public ChargeThreshold GetChargeThreshold()
        {
            Response response = Send(0x1103);
            RequireBiosSuccess(response, "读取充电阈值");
            if (response.Output.Length < 3) throw new InvalidOperationException("充电阈值响应长度不足。");
            return new ChargeThreshold(response.Output[1], response.Output[2]);
        }

        public ChargeThreshold SetChargeThreshold(int start, int end)
        {
            if (start < 0 || start > 100 || end < 0 || end > 100 || start > end)
                throw new ArgumentOutOfRangeException("start", "充电阈值必须满足 0 <= start <= end <= 100。");

            Response response = Send(0x1003, (byte)start, (byte)end);
            RequireBiosSuccess(response, "设置充电阈值");
            return GetChargeThreshold();
        }

        public PerformanceStatus GetPerformanceStatus()
        {
            Response mode = Send(0x0802);
            RequireBiosSuccess(mode, "读取性能模式状态");
            Response support = Send(0x3C06);
            RequireBiosSuccess(support, "读取性能模式支持信息");
            Response adapter = Send(0x0902);
            RequireBiosSuccess(adapter, "读取适配器输出信息");
            if (support.Output.Length < 2 || adapter.Output.Length < 4)
                throw new InvalidOperationException("性能模式能力或适配器响应长度不足。");

            int supportMask = support.Output[1];
            int adapterOutput = adapter.Output[2] | (adapter.Output[3] << 8);
            return new PerformanceStatus(ToHex(mode.Output, 8), ToHex(support.Output, 8), ToHex(adapter.Output, 8), supportMask, adapterOutput);
        }

        public PerformanceStatus SetPerformanceMode(int mode)
        {
            if (mode < 1 || mode > 2)
                throw new ArgumentOutOfRangeException("mode", "当前应用仅允许实验性的模式 1 或模式 2。");

            // PerfCommonPlugin.dll SetTurboMode(mode): byte 2 is mode - 1.
            Response response = Send(0x0C07, (byte)(mode - 1));
            RequireBiosSuccess(response, "设置性能模式");
            return GetPerformanceStatus();
        }

        private static Response Send(ushort command, params byte[] payload)
        {
            if (payload.Length > 62) throw new ArgumentOutOfRangeException("payload", "WMI 短命令载荷不能超过 62 字节。");
            byte[] input = new byte[64];
            input[0] = (byte)(command & 0xFF);
            input[1] = (byte)((command >> 8) & 0xFF);
            Array.Copy(payload, 0, input, 2, payload.Length);

            ManagementScope scope = new ManagementScope(NamespacePath);
            scope.Connect();
            List<Candidate> candidates = GetCandidates(scope);
            Exception? lastTransportError = null;
            foreach (Candidate candidate in candidates)
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    try { return Invoke(scope, candidate.Path, input); }
                    catch (Exception exception) when (IsTransportError(exception))
                    {
                        lastTransportError = exception;
                        if (attempt == 0) Thread.Sleep(100);
                    }
                }
            }
            if (lastTransportError != null)
                throw new InvalidOperationException("荣耀 HWMI 接口调用失败，已重试一次。", lastTransportError);
            throw new InvalidOperationException("未找到活动的荣耀 HWMI 接口。此机型可能不支持该接口。");
        }

        private static List<Candidate> GetCandidates(ManagementScope scope)
        {
            List<Candidate> result = new List<Candidate>();
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM OemWMIMethod")))
            using (ManagementObjectCollection objects = searcher.Get())
            {
                foreach (ManagementObject item in objects)
                {
                    using (item)
                    {
                        string? instanceName = Convert.ToString(item["InstanceName"]);
                        string? path = item.Path?.Path;
                        bool active = IsActive(item["Active"]);
                        if (!string.IsNullOrWhiteSpace(instanceName) && !string.IsNullOrWhiteSpace(path) && active)
                            result.Add(new Candidate(instanceName, path));
                    }
                }
            }
            result.Sort((left, right) => CandidateRank(left).CompareTo(CandidateRank(right)));
            return result;
        }

        private static int CandidateRank(Candidate candidate)
        {
            if (string.Equals(candidate.InstanceName, PreferredInstance, StringComparison.OrdinalIgnoreCase)) return 0;
            if (candidate.InstanceName.EndsWith("HWMI_1", StringComparison.OrdinalIgnoreCase)) return 1;
            return 2;
        }

        private static Response Invoke(ManagementScope scope, string path, byte[] input)
        {
            using (ManagementObject candidate = new ManagementObject(scope, new ManagementPath(path), null))
            using (ManagementBaseObject parameters = candidate.GetMethodParameters("OemWMIfun"))
            {
                parameters["u8Input"] = input;
                ManagementBaseObject? result = candidate.InvokeMethod("OemWMIfun", parameters, null);
                if (result == null) throw new InvalidOperationException("WMI 方法没有返回结果对象。");
                using (result)
                {
                    ValidateTransportResult(result);
                    byte[] output = ConvertToByteArray(result["u8Output"]);
                    if (output.Length == 0) throw new InvalidOperationException("BIOS WMI 调用没有返回 u8Output。");
                    return new Response(output[0], output);
                }
            }
        }

        private static void ValidateTransportResult(ManagementBaseObject result)
        {
            object? returnValue = result["ReturnValue"];
            if (returnValue == null) throw new InvalidOperationException("WMI 调用没有返回 ReturnValue。");
            bool success = returnValue is bool boolean ? boolean : Convert.ToUInt64(returnValue) == 0;
            if (!success) throw new InvalidOperationException("WMI 传输层返回失败：" + returnValue + "。");

            object? reserved = result["u32Resrved"];
            if (reserved != null && Convert.ToUInt32(reserved) != 0)
                throw new InvalidOperationException("WMI 返回非零保留状态：" + reserved + "。");
        }

        private static bool IsActive(object? value)
        {
            return value is null || value is bool boolean && boolean || value is not bool && Convert.ToUInt32(value) != 0;
        }

        private static bool IsTransportError(Exception exception)
        {
            return exception is ManagementException || exception is COMException || exception is UnauthorizedAccessException;
        }

        private static byte[] ConvertToByteArray(object? value)
        {
            if (value is byte[] bytes) return bytes;
            if (value is not Array values) return Array.Empty<byte>();
            bytes = new byte[values.Length];
            for (int index = 0; index < values.Length; index++) bytes[index] = Convert.ToByte(values.GetValue(index));
            return bytes;
        }

        private static void RequireBiosSuccess(Response response, string operation)
        {
            if (response.Status != 0)
                throw new InvalidOperationException(string.Format("{0} 被 BIOS 拒绝，状态码：0x{1:X2}。", operation, response.Status));
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

        private sealed class Candidate
        {
            public Candidate(string instanceName, string path) { InstanceName = instanceName; Path = path; }
            public string InstanceName { get; private set; }
            public string Path { get; private set; }
        }

        private sealed class Response
        {
            public Response(byte status, byte[] output) { Status = status; Output = output; }
            public byte Status { get; private set; }
            public byte[] Output { get; private set; }
        }
    }
}
