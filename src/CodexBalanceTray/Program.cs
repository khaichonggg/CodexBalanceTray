namespace CodexBalanceTray;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var singleInstance = new Mutex(
            initiallyOwned: true,
            name: @"Local\CodexBalanceTray",
            createdNew: out var createdNew);

        if (!createdNew)
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
        GC.KeepAlive(singleInstance);
    }
}
