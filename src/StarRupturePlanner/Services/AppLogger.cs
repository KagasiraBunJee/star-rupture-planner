using System.Diagnostics;
using Directory = System.IO.Directory;
using File = System.IO.File;
using Path = System.IO.Path;

namespace StarRupturePlanner.Services;

public sealed class AppLogger : IAppLogger
{
    private static readonly object Lock = new();
    private static readonly string LogFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "StarRupture Planner",
        "app.log");

    public void Info(string message) => Log("INFO", message);

    public void Warn(string message) => Log("WARN", message);

    public void Error(string message, Exception? exception = null)
    {
        string fullMessage = exception is null
            ? message
            : $"{message}{Environment.NewLine}{exception}";
        Log("ERROR", fullMessage);
    }

    private static void Log(string level, string message)
    {
        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";
        lock (Lock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);
                File.AppendAllText(LogFilePath, line + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AppLogger] Failed to write log: {ex.Message}");
            }
        }

        Debug.WriteLine(line);
    }
}
