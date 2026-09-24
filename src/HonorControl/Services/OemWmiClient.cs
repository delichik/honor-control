using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using HonorControl.Models;
using Microsoft.Management.Infrastructure;

namespace HonorControl.Services
{
    public sealed class OemWmiClient
    {
        private const string NamespacePath = @"root\wmi";
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
            Response modeState = Send(0x0E04);
            RequireBiosSuccess(modeState, "读取性能模式状态");
            Response telemetry = Send(0x0802);
            RequireBiosSuccess(telemetry, "读取性能遥测");
            Response support = Send(0x3C06);
            RequireBiosSuccess(support, "读取性能模式支持信息");
            Response adapter = Send(0x0902);
            RequireBiosSuccess(adapter, "读取适配器电压");
            if (modeState.Output.Length < 2 || support.Output.Length < 2 || adapter.Output.Length < 4)
                throw new InvalidOperationException("性能模式状态、能力或适配器电压响应长度不足。");

            int currentMode = modeState.Output[1] switch { 0 => 1, 1 => 2, _ => 0 };
            int supportMask = support.Output[1];
            int adapterVoltage = adapter.Output[2] | (adapter.Output[3] << 8);
            return new PerformanceStatus(
                currentMode,
                ToHex(modeState.Output, 8),
                ToHex(telemetry.Output, 8),
                ToHex(support.Output, 8),
                ToHex(adapter.Output, 8),
                supportMask,
                adapterVoltage);
        }

        public PerformanceStatus SetPerformanceMode(int mode)
        {
            if (mode < 1 || mode > 2)
                throw new ArgumentOutOfRangeException("mode", "当前应用仅允许智能模式或高能模式。");

            // PerfCommonPlugin.dll SetTurboMode(mode): byte 2 is mode - 1.
            Response response = Send(0x0C07, (byte)(mode - 1));
            RequireBiosSuccess(response, "设置性能模式");

            PerformanceStatus? latest = null;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                if (attempt > 0) Thread.Sleep(200);
                latest = GetPerformanceStatus();
                if (latest.CurrentMode == mode) return latest;
            }

            string actual = latest?.CurrentMode switch { 1 => "智能模式", 2 => "高能模式", _ => "未知模式" };
            throw new InvalidOperationException($"性能模式写入后的 0x0E04 回读不符：请求模式 {mode}，实际为{actual}。");
        }

        private static Response Send(ushort command, params byte[] payload)
        {
            if (payload.Length > 62) throw new ArgumentOutOfRangeException("payload", "WMI 短命令载荷不能超过 62 字节。");
            byte[] input = new byte[64];
            input[0] = (byte)(command & 0xFF);
            input[1] = (byte)((command >> 8) & 0xFF);
            Array.Copy(payload, 0, input, 2, payload.Length);

            CimSession session;
            try
            {
                session = CimSession.Create(null);
            }
            catch (Exception exception)
            {
                throw CreateDiagnosticException("创建本地 CIM 会话", Array.Empty<Candidate>(), exception);
            }

            using (session)
            {
                List<Candidate> candidates;
                try
                {
                    IEnumerable<CimInstance> instances = session.QueryInstances(NamespacePath, "WQL", "SELECT * FROM OemWMIMethod");
                    candidates = GetCandidates(instances);
                }
                catch (Exception exception)
                {
                    throw CreateDiagnosticException("通过 CIM 枚举 OemWMIMethod", Array.Empty<Candidate>(), exception);
                }

                try
                {
                    Exception? lastTransportError = null;
                    string? lastStage = null;
                    List<string> attemptFailures = new List<string>();
                    foreach (Candidate candidate in candidates)
                    {
                        if (!candidate.IsEligible) continue;
                        for (int attempt = 0; attempt < 2; attempt++)
                        {
                            string stage = $"通过 CIM 调用 OemWMIfun 命令 0x{command:X4}（{candidate.InstanceName}，第 {attempt + 1} 次）";
                            try { return Invoke(session, candidate.Object, input); }
                            catch (Exception exception) when (IsTransportError(exception))
                            {
                                lastTransportError = exception;
                                lastStage = stage;
                                attemptFailures.Add(stage + "：" + FormatExceptionChain(exception));
                                if (attempt == 0) Thread.Sleep(100);
                            }
                            catch (Exception exception)
                            {
                                attemptFailures.Add(stage + "：" + FormatExceptionChain(exception));
                                throw CreateDiagnosticException(stage, candidates, exception, attemptFailures);
                            }
                        }
                    }

                    if (lastTransportError != null)
                        throw CreateDiagnosticException(lastStage ?? $"通过 CIM 调用 OemWMIfun 命令 0x{command:X4}", candidates, lastTransportError, attemptFailures);
                    throw new InvalidOperationException($"未找到活动的荣耀 HWMI 接口。诊断：命令=0x{command:X4}；候选实例=" + FormatCandidates(candidates) + "。此机型可能不支持该接口。");
                }
                finally
                {
                    foreach (Candidate candidate in candidates) candidate.Dispose();
                }
            }
        }

        private static List<Candidate> GetCandidates(IEnumerable<CimInstance> instances)
        {
            List<Candidate> result = new List<Candidate>();
            try
            {
                foreach (CimInstance item in instances)
                {
                    try
                    {
                        string? instanceName = Convert.ToString(GetPropertyValue(item, "InstanceName"));
                        bool active = IsActive(GetPropertyValue(item, "Active"));
                        result.Add(new Candidate(item, instanceName, active));
                    }
                    catch
                    {
                        item.Dispose();
                        throw;
                    }
                }
                result.Sort((left, right) => CandidateRank(left).CompareTo(CandidateRank(right)));
                return result;
            }
            catch
            {
                foreach (Candidate candidate in result) candidate.Dispose();
                throw;
            }
        }

        private static object? GetPropertyValue(CimInstance instance, string propertyName)
        {
            foreach (CimProperty property in instance.CimInstanceProperties)
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)) return property.Value;
            }
            return null;
        }

        private static int CandidateRank(Candidate candidate)
        {
            if (string.Equals(candidate.InstanceName, PreferredInstance, StringComparison.OrdinalIgnoreCase)) return 0;
            if (candidate.InstanceName.EndsWith("HWMI_1", StringComparison.OrdinalIgnoreCase)) return 1;
            return 2;
        }

        private static Response Invoke(CimSession session, CimInstance candidate, byte[] input)
        {
            using (CimMethodParametersCollection parameters = new CimMethodParametersCollection())
            {
                parameters.Add(CimMethodParameter.Create("u8Input", input, CimFlags.In));
                using (CimMethodResult result = session.InvokeMethod(NamespacePath, candidate, "OemWMIfun", parameters))
                {
                    ValidateTransportResult(result);
                    byte[] output = ConvertToByteArray(GetOutParameterValue(result, "u8Output"));
                    if (output.Length == 0) throw new InvalidOperationException("BIOS WMI 调用没有返回 u8Output。");
                    return new Response(output[0], output);
                }
            }
        }

        private static object? GetOutParameterValue(CimMethodResult result, string parameterName)
        {
            foreach (CimMethodParameter parameter in result.OutParameters)
            {
                if (string.Equals(parameter.Name, parameterName, StringComparison.OrdinalIgnoreCase)) return parameter.Value;
            }
            return null;
        }

        private static InvalidOperationException CreateDiagnosticException(string stage, IReadOnlyCollection<Candidate> candidates, Exception exception, IReadOnlyCollection<string>? attemptFailures = null)
        {
            string attempts = attemptFailures == null || attemptFailures.Count == 0
                ? string.Empty
                : "；尝试记录=" + string.Join(" || ", attemptFailures);
            return new InvalidOperationException(
                "荣耀 HWMI 接口失败。诊断：阶段=" + stage + "；候选实例=" + FormatCandidates(candidates) + "；异常链=" + FormatExceptionChain(exception) + attempts + "。",
                exception);
        }

        private static string FormatCandidates(IEnumerable<Candidate> candidates)
        {
            StringBuilder text = new StringBuilder();
            foreach (Candidate candidate in candidates)
            {
                if (text.Length > 0) text.Append(", ");
                text.Append(candidate.InstanceName);
                text.Append(candidate.IsActive ? " (Active)" : " (Inactive)");
            }
            return text.Length == 0 ? "无" : text.ToString();
        }

        private static string FormatExceptionChain(Exception exception)
        {
            StringBuilder text = new StringBuilder();
            for (Exception? current = exception; current != null; current = current.InnerException)
            {
                if (text.Length > 0) text.Append(" -> ");
                text.Append(current.GetType().Name);
                text.Append("[HResult=0x");
                text.Append(current.HResult.ToString("X8"));
                if (current is CimException cimException)
                {
                    text.Append(", NativeErrorCode=");
                    text.Append(cimException.NativeErrorCode);
                    text.Append(", StatusCode=");
                    text.Append(cimException.StatusCode);
                }
                text.Append("]: ");
                text.Append(current.Message);
            }
            return text.ToString();
        }

        private static void ValidateTransportResult(CimMethodResult result)
        {
            object? returnValue = result.ReturnValue?.Value;
            if (returnValue == null) throw new InvalidOperationException("WMI 调用没有返回 ReturnValue。");
            bool success = returnValue is bool boolean ? boolean : Convert.ToUInt64(returnValue) == 0;
            if (!success) throw new InvalidOperationException("WMI 传输层返回失败：" + returnValue + "。");

            object? reserved = GetOutParameterValue(result, "u32Resrved");
            if (reserved != null && Convert.ToUInt32(reserved) != 0)
                throw new InvalidOperationException("WMI 返回非零保留状态：" + reserved + "。");
        }

        private static bool IsActive(object? value)
        {
            return value is null || value is bool boolean && boolean || value is not bool && Convert.ToUInt32(value) != 0;
        }

        private static bool IsTransportError(Exception exception)
        {
            return exception is CimException || exception is UnauthorizedAccessException;
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

        private sealed class Candidate : IDisposable
        {
            public Candidate(CimInstance @object, string? instanceName, bool isActive)
            {
                Object = @object;
                InstanceName = string.IsNullOrWhiteSpace(instanceName) ? "<未命名实例>" : instanceName;
                IsActive = isActive;
            }

            public CimInstance Object { get; private set; }
            public string InstanceName { get; private set; }
            public bool IsActive { get; private set; }
            public bool IsEligible => IsActive && InstanceName != "<未命名实例>";

            public void Dispose() => Object.Dispose();
        }

        private sealed class Response
        {
            public Response(byte status, byte[] output) { Status = status; Output = output; }
            public byte Status { get; private set; }
            public byte[] Output { get; private set; }
        }
    }
}
