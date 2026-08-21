using SYST.UI.Shared.ViewModels;
using SYST.UI.Shared.Views.Chrome;

namespace SYST.UI.Shared.Views;

/// <summary>
/// 连接配置窗口（标准盒/PLC/被检端点）。
/// </summary>
public partial class ConnectionConfigWindow : ChromeWindow
{
    /// <summary>
    /// 注入连接配置 ViewModel 构造，并订阅其关闭请求。
    /// </summary>
    /// <param name="vm">连接配置 ViewModel。</param>
    public ConnectionConfigWindow(ConnectionConfigViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.RequestClose += (_, _) => Close();
    }
}
