using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace Ouro.Utils
{
    /// <summary>
    /// Lightweight append-only crash logger for the installed application.
    /// Writes to %APPDATA%\Ouro\logs so diagnostics survive even when the app is
    /// installed in a protected directory and no debugger is attached.
    /// </summary>
    public static class CrashLogger
    {
        private static readonly object SyncLock = new();

        public static string LogDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Ouro",
            "logs");

        public static string LogException(Exception? exception, string context)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);

                string logPath = Path.Combine(LogDirectory, $"ouro-{DateTime.Now:yyyyMMdd}.log");
                var builder = new StringBuilder();

                builder.AppendLine("============================================================");
                builder.AppendLine($"Fecha UTC : {DateTime.UtcNow:O}");
                builder.AppendLine($"Contexto  : {context}");
                builder.AppendLine($"App       : {GetAppVersion()}");
                builder.AppendLine($"Equipo    : {Environment.MachineName}");
                builder.AppendLine($"SO        : {Environment.OSVersion}");
                builder.AppendLine($"Runtime   : {Environment.Version}");
                builder.AppendLine();

                if (exception != null)
                {
                    builder.AppendLine(exception.ToString());
                }
                else
                {
                    builder.AppendLine("(Sin excepción asociada)");
                }

                builder.AppendLine();

                lock (SyncLock)
                {
                    File.AppendAllText(logPath, builder.ToString(), Encoding.UTF8);
                }

                return logPath;
            }
            catch
            {
                // Logging must never take down the application.
                return string.Empty;
            }
        }

        private static string GetAppVersion()
        {
            try
            {
                var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                return assembly.GetName().Version?.ToString() ?? "desconocida";
            }
            catch
            {
                return "desconocida";
            }
        }
    }
}
