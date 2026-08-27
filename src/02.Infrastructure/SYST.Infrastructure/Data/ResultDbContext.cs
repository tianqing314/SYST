using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SYST.Infrastructure.Data;

/// <summary>
/// 结果库共用基类：定义产品结果表（product_test_data / product_test_data_details）的 DbSet 与索引映射，
/// 供本地 SQLite 与远程 MySQL 复用。
/// </summary>
public abstract class ResultDbContextBase : DbContext
{
    /// <summary>
    /// 用 EF 选项构造。
    /// </summary>
    /// <param name="options">DbContext 选项。</param>
    protected ResultDbContextBase(DbContextOptions options) : base(options)
    {
    }

    /// <summary>
    /// 主表：product_test_data，以 GUID 为维度，一次会话（TaskId）一条。
    /// </summary>
    public DbSet<ProductTestData> ProductTestData => Set<ProductTestData>();

    /// <summary>
    /// 明细表：product_test_data_details，以 TaskId + 测试项为维度。
    /// </summary>
    public DbSet<ProductTestDataDetail> ProductTestDataDetails => Set<ProductTestDataDetail>();

    /// <summary>
    /// 配置主键与索引（主表按 Id；明细表按 SN+项、TaskId 建索引）。
    /// </summary>
    /// <param name="b">模型构建器。</param>
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<ProductTestData>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.DeviceSn);
            // updateTime 由 MySQL CURRENT_TIMESTAMP ON UPDATE 自动维护；SQLite 插入时由默认值填充
            e.Property(x => x.UpdateTime)
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });
        b.Entity<ProductTestDataDetail>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.DeviceSn, x.TestItemCode });
            e.HasIndex(x => x.TaskId);
            // update_time 由 MySQL CURRENT_TIMESTAMP ON UPDATE 自动维护；SQLite 插入时由默认值填充
            e.Property(x => x.UpdateTime)
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });
    }
}

/// <summary>
/// 本地 SQLite 结果库。远程上报由 <see cref="SYST.Core.Abstractions.IExternalSync"/> 适配器单独实现。
/// </summary>
public sealed class ResultDbContext : ResultDbContextBase
{
    /// <summary>
    /// 用 EF 选项构造。
    /// </summary>
    /// <param name="options">DbContext 选项。</param>
    public ResultDbContext(DbContextOptions<ResultDbContext> options) : base(options)
    {
    }
}

/// <summary>
/// 远程 MySQL 结果库（正式环境上报目标），复用与本地相同的 schema 映射。
/// </summary>
public sealed class RemoteResultDbContext : ResultDbContextBase
{
    /// <summary>
    /// 用 EF 选项构造。
    /// </summary>
    /// <param name="options">DbContext 选项。</param>
    public RemoteResultDbContext(DbContextOptions<RemoteResultDbContext> options) : base(options)
    {
    }
}

/// <summary>产品结果主表，对应 product_test_data。</summary>
[Table("product_test_data")]
public sealed class ProductTestData
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("fork_sn")]
    public string? ForkSn { get; set; }

    [Column("device_sn")]
    public string DeviceSn { get; set; } = "";

    [Column("device_model")]
    public string DeviceModel { get; set; } = "";

    [Column("task_name")]
    public string? TaskName { get; set; }

    [Column("test_type_class")]
    public string? TestTypeClass { get; set; }

    [Column("test_type_detail")]
    public int? TestTypeDetail { get; set; }

    [Column("station_no")]
    public int? StationNo { get; set; }

    [Column("batch_no")]
    public string? BatchNo { get; set; }

    [Column("is_once_pass")]
    public bool? IsOncePass { get; set; }

    [Column("is_all_completed")]
    public bool? IsAllCompleted { get; set; }

    [Column("is_final_pass")]
    public bool? IsFinalPass { get; set; }

    [Column("total_items")]
    public int? TotalItems { get; set; }

    [Column("time_consume")]
    public double? TimeConsume { get; set; }

    [Column("start_time")]
    public DateTime StartTime { get; set; }

    [Column("end_time")]
    public DateTime EndTime { get; set; }

    [Column("operator")]
    public string? Operator { get; set; }

    [Column("updateTime")]
    public DateTime UpdateTime { get; set; }
}

/// <summary>产品结果明细表，对应 product_test_data_details。</summary>
[Table("product_test_data_details")]
public sealed class ProductTestDataDetail
{
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("task_id")]
    public Guid? TaskId { get; set; }

    [Column("device_sn")]
    public string DeviceSn { get; set; } = "";

    [Column("test_item_code")]
    public string TestItemCode { get; set; } = "";

    [Column("test_item_name")]
    public string TestItemName { get; set; } = "";

    [Column("test_item_desc")]
    public string? TestItemDesc { get; set; }

    [Column("test_item_parameters")]
    public string? TestItemParameters { get; set; }

    [Column("test_item_conditions")]
    public string? TestItemConditions { get; set; }

    [Column("test_process_infos")]
    public string? TestProcessInfos { get; set; }

    [Column("test_process_data")]
    public string? TestProcessData { get; set; }

    [Column("result_status")]
    public string? ResultStatus { get; set; }

    [Column("result_data")]
    public string? ResultData { get; set; }

    [Column("error_message")]
    public string? ErrorMessage { get; set; }

    [Column("retry_index")]
    public int? RetryIndex { get; set; }

    [Column("retry_category_name")]
    public string? RetryCategoryName { get; set; }

    [Column("theory_time")]
    public double? TheoryTime { get; set; }

    [Column("start_time")]
    public DateTime StartTime { get; set; }

    [Column("end_time")]
    public DateTime? EndTime { get; set; }

    [Column("operator")]
    public string? Operator { get; set; }

    [Column("update_time")]
    public DateTime UpdateTime { get; set; }
}
