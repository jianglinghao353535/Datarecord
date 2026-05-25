using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Datarecord.Models;
using S7.Net;
using S7.Net.Types;

namespace Datarecord.Services
{
    public sealed class PlcIntegrationService : IPlcIntegrationService
    {
        private static readonly TimeSpan DeltaConnectTimeout = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan SiemensConnectTimeout = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan SiemensOperationTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan DeltaOperationTimeout = TimeSpan.FromSeconds(5);
        private const int DeltaIoTimeoutMs = 3000;
        private const int SiemensIoTimeoutMs = 3000;

        public Task<PlcRealtimeSnapshotModel> ReadCurrentValuesAsync(MachineItemModel machine, CancellationToken cancellationToken = default)
        {
            return machine.PlcType switch
            {
                PlcType.SiemensS7 => ReadSiemensValuesAsync(machine, cancellationToken),
                PlcType.DeltaModbusTcp => ReadDeltaValuesAsync(machine, cancellationToken),
                PlcType.InovanceModbusTcp => ReadDeltaValuesAsync(machine, cancellationToken),
                _ => Task.FromResult(new PlcRealtimeSnapshotModel())
            };
        }

        private async Task<PlcRealtimeSnapshotModel> ReadSiemensValuesAsync(MachineItemModel machine, CancellationToken cancellationToken)
        {
            await EnsureSiemensPortReachableAsync(machine.IpAddress, machine.Port, SiemensConnectTimeout, cancellationToken);

            try
            {
                return await Task.Run(() =>
                {
                    var snapshot = new PlcRealtimeSnapshotModel();
                    using var plc = new Plc(CpuType.S7200Smart, machine.IpAddress, machine.Port, 0, 0)
                    {
                        ReadTimeout = SiemensIoTimeoutMs,
                        WriteTimeout = SiemensIoTimeoutMs
                    };
                    plc.Open();

                    snapshot.ProductionSpeed = ReadSiemensDouble(plc, machine.PlcAddressProductionSpeed);
                    snapshot.ProductionLength = ReadSiemensDouble(plc, machine.PlcAddressProductionLength);
                    snapshot.ProductionWeight = ReadSiemensDouble(plc, machine.PlcAddressProductionWeight);
                    snapshot.ReportWeight = string.IsNullOrWhiteSpace(machine.PlcAddressWeight)
                        ? snapshot.ProductionWeight
                        : ReadSiemensDouble(plc, machine.PlcAddressWeight);
                    snapshot.CurrentDiameter = ReadSiemensDouble(plc, machine.PlcAddressDiameter);
                    snapshot.IsRunningSignalOn = ReadSiemensBool(plc, machine.PlcAddressRuningSignal);

                    plc.Close();
                    return snapshot;
                }, cancellationToken).WaitAsync(SiemensOperationTimeout, cancellationToken);
            }
            catch (TimeoutException ex)
            {
                throw new TimeoutException($"Siemens PLC read timeout: {machine.IpAddress}:{machine.Port}", ex);
            }
        }

        private async Task<PlcRealtimeSnapshotModel> ReadDeltaValuesAsync(MachineItemModel machine, CancellationToken cancellationToken)
        {
            try
            {
                return await ReadDeltaValuesCoreAsync(machine, cancellationToken).WaitAsync(DeltaOperationTimeout, cancellationToken);
            }
            catch (TimeoutException ex)
            {
                throw new TimeoutException($"Delta/Inovance PLC read timeout: {machine.IpAddress}:{machine.Port}", ex);
            }
        }

        private async Task<PlcRealtimeSnapshotModel> ReadDeltaValuesCoreAsync(MachineItemModel machine, CancellationToken cancellationToken)
        {
            using var tcpClient = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(DeltaConnectTimeout);

            try
            {
                await tcpClient.ConnectAsync(machine.IpAddress, machine.Port, timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"PLC connection timeout: {machine.IpAddress}:{machine.Port}");
            }

            tcpClient.ReceiveTimeout = DeltaIoTimeoutMs;
            tcpClient.SendTimeout = DeltaIoTimeoutMs;

            using var stream = tcpClient.GetStream();
            stream.ReadTimeout = DeltaIoTimeoutMs;
            stream.WriteTimeout = DeltaIoTimeoutMs;

            var transactionId = 0;

            var snapshot = new PlcRealtimeSnapshotModel
            {
                ProductionSpeed = ReadDeltaRealNumeric(stream, ref transactionId, machine.PlcAddressProductionSpeed),
                ProductionLength = ReadDeltaRealNumeric(stream, ref transactionId, machine.PlcAddressProductionLength),
                ProductionWeight = ReadDeltaRealNumeric(stream, ref transactionId, machine.PlcAddressProductionWeight),
                ReportWeight = string.IsNullOrWhiteSpace(machine.PlcAddressWeight)
                    ? ReadDeltaRealNumeric(stream, ref transactionId, machine.PlcAddressProductionWeight)
                    : ReadDeltaRealNumeric(stream, ref transactionId, machine.PlcAddressWeight),
                CurrentDiameter = ReadDeltaRealNumeric(stream, ref transactionId, machine.PlcAddressDiameter),
                IsRunningSignalOn = ReadDeltaBoolSignal(stream, ref transactionId, machine.PlcAddressRuningSignal)
            };

            return snapshot;
        }

        private static object? ReadSiemensValue(Plc plc, string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                return null;
            }

            var normalizedAddress = NormalizeSiemensAddress(address);
            try
            {
                return plc.Read(normalizedAddress);
            }
            catch (InvalidAddressException)
            {
                throw new InvalidAddressException($"Invalid PLC address format: {address}. Normalized: {normalizedAddress}.");
            }
            catch (PlcException ex) when (ex.Message.Contains("Address out of range", StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException($"PLC address is out of range: {address}. Normalized: {normalizedAddress}. Please verify the PLC memory area.", ex);
            }
        }

        private static string NormalizeSiemensAddress(string address)
        {
            var trimmed = address.Trim();
            if (trimmed.Length == 0)
            {
                return trimmed;
            }

            var upper = trimmed.ToUpperInvariant();

            if (upper.Length >= 3
                && upper[0] == 'V'
                && (upper[1] == 'B' || upper[1] == 'W' || upper[1] == 'D')
                && char.IsDigit(upper[2]))
            {
                return $"DB1.DB{upper[1]}{upper[2..]}";
            }

            if (upper.Length >= 4 && upper[0] == 'V')
            {
                var dotIndex = upper.IndexOf('.');
                if (dotIndex > 1 && dotIndex < upper.Length - 1
                    && int.TryParse(upper.AsSpan(1, dotIndex - 1), out var byteIndex)
                    && int.TryParse(upper.AsSpan(dotIndex + 1), out var bitIndex)
                    && bitIndex is >= 0 and <= 7)
                {
                    return $"DB1.DBX{byteIndex}.{bitIndex}";
                }
            }

            return upper;
        }

        private static double ReadSiemensDouble(Plc plc, string address)
        {
            if (TryReadSiemensFloat(plc, address, out var floatValue))
            {
                return floatValue;
            }

            var value = ReadSiemensValue(plc, address);
            return ConvertToDouble(value);
        }

        private static bool ReadSiemensBool(Plc plc, string address)
        {
            var value = ReadSiemensValue(plc, address);
            return ConvertToBool(value);
        }

        private static bool TryReadSiemensFloat(Plc plc, string address, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(address))
            {
                return false;
            }

            var normalizedAddress = NormalizeSiemensAddress(address);
            var upper = normalizedAddress.ToUpperInvariant();

            if (TryReadFloatFromDbAddress(plc, upper, out value))
            {
                return true;
            }

            if (TryReadFloatFromMarkerAddress(plc, upper, out value))
            {
                return true;
            }

            return false;
        }

        private static bool TryReadFloatFromDbAddress(Plc plc, string address, out double value)
        {
            value = 0;
            var match = Regex.Match(address, @"^DB(?<db>\d+)\.DBD(?<byte>\d+)$", RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                return false;
            }

            if (!int.TryParse(match.Groups["db"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dbNumber)
                || !int.TryParse(match.Groups["byte"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var byteOffset)
                || dbNumber < 1
                || byteOffset < 0)
            {
                return false;
            }

            var bytes = plc.ReadBytes(DataType.DataBlock, dbNumber, byteOffset, 4);
            if (bytes.Length < 4)
            {
                return false;
            }

            value = Real.FromByteArray(bytes);
            return true;
        }

        private static bool TryReadFloatFromMarkerAddress(Plc plc, string address, out double value)
        {
            value = 0;
            var match = Regex.Match(address, @"^MD(?<byte>\d+)$", RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                return false;
            }

            if (!int.TryParse(match.Groups["byte"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var byteOffset)
                || byteOffset < 0)
            {
                return false;
            }

            var bytes = plc.ReadBytes(DataType.Memory, 0, byteOffset, 4);
            if (bytes.Length < 4)
            {
                return false;
            }

            value = Real.FromByteArray(bytes);
            return true;
        }

        private static string ReadStatusText(object? value)
        {
            if (value is bool boolValue)
            {
                return boolValue ? "Running" : "Idle";
            }

            if (value is string stringValue && !string.IsNullOrWhiteSpace(stringValue))
            {
                return stringValue;
            }

            var number = ConvertToDouble(value);
            var rounded = (int)Math.Round(number);
            return rounded switch
            {
                0 => "Idle",
                1 => "Running",
                2 => "Alarm",
                3 => "Stopped",
                _ => rounded.ToString(CultureInfo.InvariantCulture)
            };
        }

        private static bool ConvertToBool(object? value)
        {
            return value switch
            {
                null => false,
                bool boolValue => boolValue,
                byte byteValue => byteValue != 0,
                short shortValue => shortValue != 0,
                ushort ushortValue => ushortValue != 0,
                int intValue => intValue != 0,
                uint uintValue => uintValue != 0,
                long longValue => longValue != 0,
                float floatValue => Math.Abs(floatValue) > float.Epsilon,
                double doubleValue => Math.Abs(doubleValue) > double.Epsilon,
                decimal decimalValue => decimalValue != 0,
                _ when bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var parsedBool) => parsedBool,
                _ when double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedNumber) => Math.Abs(parsedNumber) > double.Epsilon,
                _ => false
            };
        }

        private static double ConvertToDouble(object? value)
        {
            return value switch
            {
                null => 0,
                bool boolValue => boolValue ? 1 : 0,
                byte byteValue => byteValue,
                short shortValue => shortValue,
                ushort ushortValue => ushortValue,
                int intValue => intValue,
                uint uintValue => uintValue,
                long longValue => longValue,
                float floatValue => floatValue,
                double doubleValue => doubleValue,
                decimal decimalValue => (double)decimalValue,
                _ when double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => 0
            };
        }

        private static double ReadDeltaNumeric(NetworkStream stream, ref int transactionId, string address, double minExpected, double maxExpected)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                return 0;
            }

            var trimmed = address.Trim().ToUpperInvariant();
            if (trimmed.StartsWith("M", StringComparison.Ordinal))
            {
                return ReadDeltaCoil(stream, ref transactionId, ParseAddressNumber(trimmed, 1)) ? 1 : 0;
            }

            if (!trimmed.StartsWith("D", StringComparison.Ordinal))
            {
                return 0;
            }

            var startAddress = ParseAddressNumber(trimmed, 1);
            var registers = ReadHoldingRegisters(stream, ref transactionId, startAddress, 2);
            if (registers.Length < 2)
            {
                return 0;
            }

            return DecodeBestModbusNumeric(registers[0], registers[1], minExpected, maxExpected);
        }

        private static double ReadDeltaIntegerNumeric(NetworkStream stream, ref int transactionId, string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                return 0;
            }

            var trimmed = address.Trim().ToUpperInvariant();
            if (trimmed.StartsWith("M", StringComparison.Ordinal))
            {
                return ReadDeltaCoil(stream, ref transactionId, ParseAddressNumber(trimmed, 1)) ? 1 : 0;
            }

            if (!trimmed.StartsWith("D", StringComparison.Ordinal))
            {
                return 0;
            }

            var startAddress = ParseAddressNumber(trimmed, 1);
            var registers = ReadHoldingRegisters(stream, ref transactionId, startAddress, 2);
            if (registers.Length < 2)
            {
                return 0;
            }

            var value = unchecked((int)(((uint)registers[1] << 16) | registers[0]));
            return value;
        }

        private static double ReadDeltaRealNumeric(NetworkStream stream, ref int transactionId, string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                return 0;
            }

            var trimmed = address.Trim().ToUpperInvariant();
            if (trimmed.StartsWith("M", StringComparison.Ordinal))
            {
                return ReadDeltaCoil(stream, ref transactionId, ParseAddressNumber(trimmed, 1)) ? 1 : 0;
            }

            if (!trimmed.StartsWith("D", StringComparison.Ordinal))
            {
                return 0;
            }

            var startAddress = ParseAddressNumber(trimmed, 1);
            var registers = ReadHoldingRegisters(stream, ref transactionId, startAddress, 2);
            if (registers.Length < 2)
            {
                return 0;
            }

            var a = (byte)(registers[0] >> 8);
            var b = (byte)registers[0];
            var c = (byte)(registers[1] >> 8);
            var d = (byte)registers[1];
            var value = ToSingleFromBytes(b, a, d, c);
            return double.IsFinite(value) ? value : 0;
        }

        private static double ReadDeltaAdaptiveNumeric(NetworkStream stream, ref int transactionId, string address, double minExpected, double maxExpected, double integerScale = 1.0)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                return 0;
            }

            var trimmed = address.Trim().ToUpperInvariant();
            if (trimmed.StartsWith("M", StringComparison.Ordinal))
            {
                return ReadDeltaCoil(stream, ref transactionId, ParseAddressNumber(trimmed, 1)) ? 1 : 0;
            }

            if (!trimmed.StartsWith("D", StringComparison.Ordinal))
            {
                return 0;
            }

            var startAddress = ParseAddressNumber(trimmed, 1);
            var registers = ReadHoldingRegisters(stream, ref transactionId, startAddress, 2);
            if (registers.Length < 2)
            {
                return 0;
            }

            if (TryDecodeIntegerCandidate(registers[0], registers[1], minExpected, maxExpected, integerScale, out var integerDecoded))
            {
                return integerDecoded;
            }

            var a = (byte)(registers[0] >> 8);
            var b = (byte)registers[0];
            var c = (byte)(registers[1] >> 8);
            var d = (byte)registers[1];
            var candidates = new[]
            {
                ToSingleFromBytes(d, c, b, a),
                ToSingleFromBytes(b, a, d, c),
                ToSingleFromBytes(c, d, a, b),
                ToSingleFromBytes(a, b, c, d)
            };

            foreach (var candidate in candidates)
            {
                if (!double.IsFinite(candidate))
                {
                    continue;
                }

                if (candidate >= minExpected && candidate <= maxExpected)
                {
                    return candidate;
                }
            }

            var rawFallback = unchecked((int)(((uint)registers[1] << 16) | registers[0]));
            return Math.Abs(integerScale) > double.Epsilon ? rawFallback / integerScale : rawFallback;
        }

        private static bool TryDecodeIntegerCandidate(ushort lowWord, ushort highWord, double minExpected, double maxExpected, double integerScale, out double decoded)
        {
            decoded = 0;

            var rawCandidates = new[]
            {
                unchecked((int)(((uint)highWord << 16) | lowWord)),
                unchecked((int)(((uint)lowWord << 16) | highWord))
            };

            foreach (var raw in rawCandidates)
            {
                if (TrySelectScaledInteger(raw, minExpected, maxExpected, integerScale, out decoded))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TrySelectScaledInteger(int rawValue, double minExpected, double maxExpected, double integerScale, out double scaled)
        {
            scaled = 0;

            var safeScale = Math.Abs(integerScale) > double.Epsilon ? integerScale : 1.0;
            var baseValue = rawValue / safeScale;
            if (double.IsFinite(baseValue) && baseValue >= minExpected && baseValue <= maxExpected)
            {
                scaled = baseValue;
                return true;
            }

            var normalized = Math.Abs(baseValue);
            if (normalized < 1e-12)
            {
                return false;
            }

            var divisors = new[] { 10d, 100d, 1000d, 10000d };
            foreach (var divisor in divisors)
            {
                var candidate = baseValue / divisor;
                if (!double.IsFinite(candidate))
                {
                    continue;
                }

                if (candidate >= minExpected && candidate <= maxExpected)
                {
                    scaled = candidate;
                    return true;
                }
            }

            return false;
        }

        private static string ReadDeltaStatus(NetworkStream stream, ref int transactionId, string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                return "待機";
            }

            var trimmed = address.Trim().ToUpperInvariant();
            if (trimmed.StartsWith("M", StringComparison.Ordinal))
            {
                return ReadDeltaCoil(stream, ref transactionId, ParseAddressNumber(trimmed, 1)) ? "運行中" : "待機";
            }

            var value = (int)Math.Round(ReadDeltaIntegerNumeric(stream, ref transactionId, address));
            return value switch
            {
                0 => "待機",
                1 => "運行中",
                2 => "警報",
                3 => "停機",
                _ => value.ToString(CultureInfo.InvariantCulture)
            };
        }

        private static ushort[] ReadHoldingRegisters(NetworkStream stream, ref int transactionId, ushort startAddress, ushort quantity)
        {
            var request = BuildModbusRequest(ref transactionId, 3, startAddress, quantity);
            stream.Write(request, 0, request.Length);

            var response = ReadModbusResponse(stream);
            if (response.Length < 9)
            {
                return [];
            }

            var byteCount = response[8];
            var result = new ushort[byteCount / 2];
            for (var i = 0; i < result.Length; i++)
            {
                var offset = 9 + (i * 2);
                result[i] = (ushort)((response[offset] << 8) | response[offset + 1]);
            }

            return result;
        }

        private static bool ReadDeltaCoil(NetworkStream stream, ref int transactionId, ushort address)
        {
            var request = BuildModbusRequest(ref transactionId, 1, address, 1);
            stream.Write(request, 0, request.Length);

            var response = ReadModbusResponse(stream);
            return response.Length >= 10 && response[9] > 0;
        }

        private static bool ReadDeltaBoolSignal(NetworkStream stream, ref int transactionId, string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                return false;
            }

            var trimmed = address.Trim().ToUpperInvariant();
            if (trimmed.StartsWith("M", StringComparison.Ordinal))
            {
                return ReadDeltaCoil(stream, ref transactionId, ParseAddressNumber(trimmed, 1));
            }

            if (trimmed.StartsWith("D", StringComparison.Ordinal))
            {
                return Math.Abs(ReadDeltaIntegerNumeric(stream, ref transactionId, trimmed)) > double.Epsilon;
            }

            return false;
        }

        private static byte[] BuildModbusRequest(ref int transactionId, byte functionCode, ushort startAddress, ushort quantity)
        {
            transactionId = (transactionId + 1) % ushort.MaxValue;
            return
            [
                (byte)(transactionId >> 8), (byte)transactionId,
                0, 0,
                0, 6,
                1,
                functionCode,
                (byte)(startAddress >> 8), (byte)startAddress,
                (byte)(quantity >> 8), (byte)quantity
            ];
        }

        private static byte[] ReadModbusResponse(NetworkStream stream)
        {
            var header = new byte[7];
            FillBuffer(stream, header);

            var bodyLength = (header[4] << 8) | header[5];
            var body = new byte[bodyLength - 1];
            FillBuffer(stream, body);

            return header.Concat(body).ToArray();
        }

        private static void FillBuffer(Stream stream, byte[] buffer)
        {
            var read = 0;
            while (read < buffer.Length)
            {
                var count = stream.Read(buffer, read, buffer.Length - read);
                if (count <= 0)
                {
                    throw new IOException("PLC 通訊中斷。");
                }

                read += count;
            }
        }

        private static ushort ParseAddressNumber(string address, int prefixLength)
        {
            var numericPart = address[prefixLength..].Trim();
            return ushort.TryParse(numericPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : (ushort)0;
        }

        private static double DecodeBestModbusNumeric(ushort register0, ushort register1, double minExpected, double maxExpected)
        {
            var a = (byte)(register0 >> 8);
            var b = (byte)register0;
            var c = (byte)(register1 >> 8);
            var d = (byte)register1;

            var candidates = new[]
            {
                ToSingleFromBytes(d, c, b, a), // ABCD
                ToSingleFromBytes(b, a, d, c), // CDAB (word swap)
                ToSingleFromBytes(c, d, a, b), // BADC
                ToSingleFromBytes(a, b, c, d)  // DCBA
            };

            foreach (var candidate in candidates)
            {
                if (double.IsFinite(candidate) && candidate >= minExpected && candidate <= maxExpected)
                {
                    return candidate;
                }
            }

            var firstFinite = candidates.FirstOrDefault(x => double.IsFinite(x));
            if (double.IsFinite(firstFinite) && Math.Abs(firstFinite) <= 1_000_000_000)
            {
                return firstFinite;
            }

            return register0;
        }

        private static float ToSingleFromBytes(byte b0, byte b1, byte b2, byte b3)
        {
            return BitConverter.ToSingle(new[] { b0, b1, b2, b3 }, 0);
        }

        private static async Task EnsureSiemensPortReachableAsync(string ipAddress, int port, TimeSpan timeout, CancellationToken cancellationToken)
        {
            using var probe = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            try
            {
                await probe.ConnectAsync(ipAddress, port, timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"連線 PLC 逾時：{ipAddress}:{port}");
            }
            catch (SocketException ex)
            {
                throw new IOException($"無法連線 PLC：{ipAddress}:{port}，{ex.Message}", ex);
            }
        }
    }
}
