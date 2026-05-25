using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Datarecord.Models;

namespace Datarecord.Services
{
    public sealed class DeepSeekAnalysisService : IDeepSeekAnalysisService
    {
        private static readonly HttpClient HttpClient = new();

        public async Task<string> AnalyzeTemperatureTrendAsync(
            string machineName,
            IReadOnlyList<MachineTrendRecordModel> points,
            CancellationToken cancellationToken = default)
        {
            if (points.Count == 0)
            {
                return "当前时间范围内无数据，无法进行温度曲线分析。";
            }

            var apiKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                apiKey = "sk-1a9b5ed504744ffab41d0607738d49d1";
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return "未配置 DEEPSEEK_API_KEY，已跳过 AI 分析。";
            }

            var sampledPoints = SamplePoints(points, 60);
            var prompt = BuildPrompt(machineName, sampledPoints);

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.deepseek.com/chat/completions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var payload = new
            {
                model = "deepseek-v4-pro",
                messages = new object[]
                {
                    new { role = "system", content = "你是工业漆包机温度数据分析助手，请用中文给出简明结论、异常判断和建议。" },
                    new { role = "user", content = prompt }
                },
                stream = false,
                reasoning_effort = "high",
                thinking = new { type = "enabled" },
                temperature = 0.2,
                max_tokens = 800
            };

            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await HttpClient.SendAsync(request, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorMessage = TryExtractErrorMessage(json);
                return string.IsNullOrWhiteSpace(errorMessage)
                    ? $"AI 分析请求失败：{(int)response.StatusCode} {response.ReasonPhrase}"
                    : $"AI 分析请求失败：{(int)response.StatusCode} {errorMessage}";
            }

            var result = JsonSerializer.Deserialize<DeepSeekResponse>(json);
            var message = result?.Choices?.FirstOrDefault()?.Message;
            var content = message?.Content?.Trim();
            if (!string.IsNullOrWhiteSpace(content))
            {
                return content;
            }

            var reasoningContent = message?.ReasoningContent?.Trim();
            if (!string.IsNullOrWhiteSpace(reasoningContent))
            {
                return $"AI 返回了推理内容（无最终正文）：\n{reasoningContent}";
            }

            var fallback = TryExtractFirstMessageText(json);
            return string.IsNullOrWhiteSpace(fallback)
                ? "AI 分析返回为空。"
                : fallback;
        }

        private static List<MachineTrendRecordModel> SamplePoints(IReadOnlyList<MachineTrendRecordModel> points, int maxCount)
        {
            if (points.Count <= maxCount)
            {
                return points.ToList();
            }

            var step = (points.Count - 1d) / (maxCount - 1d);
            var sampled = new List<MachineTrendRecordModel>(maxCount);
            for (var i = 0; i < maxCount; i++)
            {
                var index = (int)Math.Round(i * step);
                index = Math.Clamp(index, 0, points.Count - 1);
                sampled.Add(points[index]);
            }

            return sampled;
        }

        private static string BuildPrompt(string machineName, IReadOnlyList<MachineTrendRecordModel> points)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Machine: {machineName}");
            sb.AppendLine($"Sample count: {points.Count}");
            sb.AppendLine("Data format: Time, Speed, Length, Diameter, Tension");

            foreach (var p in points)
            {
                sb.AppendLine(
                    $"{p.Timestamp:yyyy-MM-dd HH:mm:ss}, {p.Speed.ToString("0.###", CultureInfo.InvariantCulture)}, {p.Length.ToString("0.###", CultureInfo.InvariantCulture)}, {p.Diameter.ToString("0.###", CultureInfo.InvariantCulture)}, {p.Tension.ToString("0.###", CultureInfo.InvariantCulture)}");
            }

            sb.AppendLine();
            sb.AppendLine("Please provide:");
            sb.AppendLine("1) Trend analysis for speed/length/diameter/tension");
            sb.AppendLine("2) Possible abnormal points and causes");
            sb.AppendLine("3) Risk hints for current process");
            sb.AppendLine("4) Three actionable optimization suggestions");
            return sb.ToString();
        }

        private sealed class DeepSeekResponse
        {
            public DeepSeekChoice[]? Choices { get; set; }
        }

        private sealed class DeepSeekChoice
        {
            public DeepSeekMessage? Message { get; set; }
        }

        private sealed class DeepSeekMessage
        {
            public string? Content { get; set; }

            public string? ReasoningContent { get; set; }
        }

        private static string? TryExtractFirstMessageText(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("choices", out var choices)
                    || choices.ValueKind != JsonValueKind.Array
                    || choices.GetArrayLength() == 0)
                {
                    return null;
                }

                var first = choices[0];
                if (!first.TryGetProperty("message", out var message)
                    || message.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                if (message.TryGetProperty("content", out var content))
                {
                    if (content.ValueKind == JsonValueKind.String)
                    {
                        return content.GetString();
                    }

                    if (content.ValueKind == JsonValueKind.Array)
                    {
                        var parts = content
                            .EnumerateArray()
                            .Where(x => x.ValueKind == JsonValueKind.Object && x.TryGetProperty("text", out _))
                            .Select(x => x.GetProperty("text").GetString())
                            .Where(x => !string.IsNullOrWhiteSpace(x));
                        var combined = string.Join("\n", parts!);
                        return string.IsNullOrWhiteSpace(combined) ? null : combined;
                    }
                }

                if (message.TryGetProperty("reasoning_content", out var reasoning)
                    && reasoning.ValueKind == JsonValueKind.String)
                {
                    return reasoning.GetString();
                }
            }
            catch
            {
            }

            return null;
        }

        private static string? TryExtractErrorMessage(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("error", out var errorElement))
                {
                    if (errorElement.ValueKind == JsonValueKind.String)
                    {
                        return errorElement.GetString();
                    }

                    if (errorElement.ValueKind == JsonValueKind.Object
                        && errorElement.TryGetProperty("message", out var messageElement)
                        && messageElement.ValueKind == JsonValueKind.String)
                    {
                        return messageElement.GetString();
                    }
                }

                if (doc.RootElement.TryGetProperty("message", out var rootMessage)
                    && rootMessage.ValueKind == JsonValueKind.String)
                {
                    return rootMessage.GetString();
                }
            }
            catch
            {
            }

            return null;
        }
    }
}
