using System.Windows;

namespace MediaStation.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        CompositionRoot.Start(this, e.Args);
    }
}
