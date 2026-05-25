using System;
using System.Collections.Generic;
using Datarecord.Models;

namespace Datarecord.Services
{
    public interface IProductionReportService
    {
        void SaveRunReport(ProductionReportRecordModel record);

        IReadOnlyList<ProductionReportRecordModel> QueryReports(Guid machineId, DateTime startInclusive, DateTime endExclusive);

        (double TotalLength, double TotalWeight) QueryTotals(Guid machineId, DateTime startInclusive, DateTime endExclusive);

        void ClearReports(Guid machineId);
    }
}
