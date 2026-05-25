using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Datarecord.Models;

namespace Datarecord.Services
{
    public interface IDeepSeekAnalysisService
    {
        Task<string> AnalyzeTemperatureTrendAsync(
            string machineName,
            IReadOnlyList<MachineTrendRecordModel> points,
            CancellationToken cancellationToken = default);
    }
}
