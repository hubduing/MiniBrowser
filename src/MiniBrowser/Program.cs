namespace MiniBrowser;

/// <summary>Точка входа приложения — единственный запускаемый файл.</summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var app = new App();
        app.InitializeComponent();
        app.Run(new MainWindow(args));
    }
}

