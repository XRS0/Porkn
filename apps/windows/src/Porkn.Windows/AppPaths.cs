namespace Porkn.Windows;

internal static class AppPaths
{
    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "porkn");

    public static string RuntimeDirectory => Path.Combine(DataDirectory, "Runtime");

    public static string AppDirectory => AppContext.BaseDirectory;
}
