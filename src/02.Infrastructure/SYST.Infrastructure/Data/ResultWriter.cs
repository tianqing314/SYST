using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SYST.Core.Abstractions;
using SYST.Infrastructure.Configuration;

namespace SYST.Infrastructure.Data;

/// <summary>
/// 把一次测试会话结果写入结果库的共用逻辑（本地 SQLite 与远程 MySQL 复用同一套映射规则）。
/// </summary>
public static class ResultWriter
{
    /// <summary>
    /// 保存会话结果：子表逐项写；全测（FullRun）且有 SN 时主表按 SN upsert（首测时间取最早、末测时间取最晚）。
    /// 只装载实体，不提交——由调用方决定何时 <see cref="DbContext.SaveChangesAsync(CancellationToken)"/>。
    /// </summary>
    /// <param name="db">结果库上下文。</param>
    /// <param name="result">测试会话结果。</param>
    /// <param name="ct">取消令牌。</param>
    public static Task WriteSessionAsync(ResultDbContextBase db, TestSessionResult result, CancellationToken ct = default) =>
        WriteSessionAsync(db, result, new ResultStoreOptions(), ct);

    /// <summary>
    /// 写入一次测试会话：主表按 TaskId 建/更新一条，明细表逐项写入。
    /// </summary>
    public static async Task WriteSessionAsync(ResultDbContextBase db, TestSessionResult result,
        ResultStoreOptions options, CancellationToken ct = default)
    {
        await WriteProductAsync(db, result, options, ct);
    }

    private static async Task WriteProductAsync(ResultDbContextBase db, TestSessionResult result,
        ResultStoreOptions options, CancellationToken ct)
    {
        var positions = result.Positions.Where(p => !string.IsNullOrWhiteSpace(p.SerialNumber)).ToList();
        if (positions.Count == 0)
        {
            return;
        }

        // product_test_data_details.task_id 是 product_test_data.id 的业务关联，不能只写一个孤立的 TaskId。
        var first = positions[0];
        var start = positions.SelectMany(p => p.Steps).Select(s => s.StartedAt).DefaultIfEmpty(result.StartedAt).Min();
        var end = positions.SelectMany(p => p.Steps).Select(s => s.FinishedAt).DefaultIfEmpty(result.FinishedAt).Max();
        var actualStepCount = positions.Sum(p => p.Steps.Count);
        var expectedStepCount = result.ExpectedStepCount;
        var allCompleted = result.FullRun && expectedStepCount > 0 && actualStepCount >= expectedStepCount;
        var allStepsPassed = allCompleted && positions.SelectMany(p => p.Steps).All(s => s.Result.IsPass);
        var main = await db.ProductTestData.FirstOrDefaultAsync(x => x.Id == result.TaskId, ct);
        if (main is null)
        {
            main = new ProductTestData { Id = result.TaskId, StartTime = start };
            db.ProductTestData.Add(main);
        }

        main.ForkSn = first.Position.Name;
        main.DeviceSn = first.SerialNumber!;
        main.DeviceModel = result.DeviceModel ?? "";
        main.TaskName = result.TaskKey;
        main.TestTypeClass = options.TestTypeClass;
        main.TestTypeDetail = options.TestTypeDetail;
        main.StationNo = ParseStationNo(result.StationNo);
        main.BatchNo = result.BatchNo;
        // 三个状态都以“是否真正完成全部应测项”为前提；中途停止时即使已测项通过，也全部记 false。
        main.IsAllCompleted = allCompleted;
        main.IsOncePass = allStepsPassed && !result.IsRePress;
        main.IsFinalPass = allStepsPassed;
        main.TotalItems = actualStepCount;
        main.TimeConsume = Math.Max(0, (end - start).TotalSeconds);
        main.EndTime = end;
        main.Operator = result.Operator;

        foreach (var pos in positions)
        {
            foreach (var rec in pos.Steps)
            {
                db.ProductTestDataDetails.Add(new ProductTestDataDetail
                {
                    TaskId = main.Id,
                    DeviceSn = pos.SerialNumber!,
                    TestItemCode = rec.Step.Key,
                    TestItemName = rec.Step.Name,
                    TestItemDesc = rec.Step.Description,
                    TestItemParameters = SerializeParameters(rec.Step.Parameters),
                    TestItemConditions = SerializeConditions(rec.Step.Conditions),
                    TestProcessInfos = rec.ProcessInfos,
                    TestProcessData = rec.ProcessData,
                    ResultStatus = rec.Result.Status.ToString(),
                    ResultData = SerializeResultData(rec.Result),
                    ErrorMessage = rec.Result.IsPass ? null : (rec.Result.Detail ?? rec.Result.Summary),
                    StartTime = rec.StartedAt,
                    EndTime = rec.FinishedAt == default ? null : rec.FinishedAt,
                    Operator = result.Operator,
                });
            }
        }
    }

    private static int? ParseStationNo(string? stationNo) =>
        int.TryParse(stationNo, out var value) ? value : null;

    private static string? SerializeConditions(IReadOnlyList<ConditionDescriptor> conditions) =>
        conditions.Count == 0 ? null : JsonSerializer.Serialize(conditions);

    /// <summary>
    /// 测试项参数 JSON（test_item_parameters）：参数名/值/单位列表；无参数返回 null。
    /// </summary>
    private static string? SerializeParameters(IReadOnlyList<ParameterDescriptor> parameters) =>
        parameters.Count == 0 ? null : JsonSerializer.Serialize(parameters);

    /// <summary>
    /// 测试结果数据 JSON（result_data）：结果结论/摘要/测量值/明细，与产品库明细表注释的约定结构一致。
    /// </summary>
    private static string? SerializeResultData(StepResult result) =>
        JsonSerializer.Serialize(new
        {
            outcome = result.IsPass,
            summary = result.Summary,
            measuredValue = result.MeasuredValue,
            detail = result.Detail,
        });
}
