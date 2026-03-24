using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;

namespace CryptoTrading.App.Core.Logging
{
    public sealed class FileLogger : ILogger
    {
        #region Private Constants

        private const string Spaces = "       ";

        #endregion Private Constants

        #region Private Fields

        private readonly string _filePath;

        private readonly LogLevel _level;

        private readonly object _sync = new object();

        private readonly int _maxLogSize;

        #endregion Private Fields

        #region Constructors

        public FileLogger(string filePath, LogLevel level)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentNullException(nameof(filePath));

            // Tag log file name with current git commit
            var commitHash = GetGitCommitHash();
            if (!string.IsNullOrEmpty(commitHash))
            {
                var dir = Path.GetDirectoryName(filePath);
                var name = Path.GetFileNameWithoutExtension(filePath);
                var ext = Path.GetExtension(filePath);
                filePath = Path.Combine(dir ?? "", $"{name}_{commitHash}{ext}");
            }

            _filePath = filePath;
            _level = level;

            // Truncate log file for each new run
            FileInfo fi1 = new FileInfo(_filePath);
            if(fi1.Exists) fi1.Delete();

            // Write header with run metadata
            WriteHeader(commitHash);
        }

        public FileLogger(string filePath, LogLevel level, int maxLogSize) : this(filePath, level)
        {
            _maxLogSize = maxLogSize;
        }

        #endregion Constructors

        #region Public Methods

        public IDisposable BeginScope<TState>(TState state)
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            if (logLevel == LogLevel.None)
                return false;

            return logLevel >= _level;
        }

        private int _logFileNumber = 0;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (formatter == null)
                throw new ArgumentNullException(nameof(formatter));

            if (!IsEnabled(logLevel))
                return;

            var message = formatter(state, exception);
            if (string.IsNullOrWhiteSpace(message))
                return;

            try
            {
                lock (_sync)
                {

                    FileInfo fi1 = new FileInfo(_filePath);
                    if (fi1.Exists && fi1.Length / 1000 >= _maxLogSize)
                    {
                        var name = fi1.FullName + _logFileNumber+1;
                        var fitemp = new FileInfo(name);
                        if (fitemp.Exists) fitemp.Delete();
                        fi1.MoveTo(fi1.FullName+_logFileNumber++);
                        FileInfo fi2 = new FileInfo(_filePath);
                        if (fi2.Exists) fi2.Delete();
                    }

                    

                    using (var stream = new FileStream(_filePath, FileMode.Append, FileAccess.Write, FileShare.None))
                    using (var streamWriter = new StreamWriter(stream) { AutoFlush = false })
                    {
                        var now = DateTimeOffset.Now;

                        streamWriter.WriteLine($"[{ConvertLogLevelToString(logLevel)}] {now}");

                        foreach (var line in message.Split(new[] { Environment.NewLine }, StringSplitOptions.None))
                        {
                            streamWriter.WriteLine($"{Spaces}{line}");
                        }

                        var prefix = string.Empty;

                        while (exception != null)
                        {
                            streamWriter.WriteLine($"{Spaces}{prefix}(exception: \"{exception.Message}\")");

                            prefix += "  ";
                            exception = exception.InnerException;
                        }

                        streamWriter.Flush();
                    }
                }
            }
            catch { /* ignore */ }
        }

        #endregion Public Methods

        #region Private Methods

        private static string ConvertLogLevelToString(LogLevel logLevel)
        {
            switch (logLevel)
            {
                case LogLevel.Trace:
                    return "trce";
                case LogLevel.Debug:
                    return "dbug";
                case LogLevel.Information:
                    return "info";
                case LogLevel.Warning:
                    return "warn";
                case LogLevel.Error:
                    return "fail";
                case LogLevel.Critical:
                    return "crit";
                case LogLevel.None:
                    return "none";
                default:
                    throw new ArgumentOutOfRangeException(nameof(logLevel), logLevel, null);
            }
        }

        private static string GetGitCommitHash()
        {
            try
            {
                var psi = new ProcessStartInfo("git", "rev-parse --short HEAD")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                var output = process?.StandardOutput.ReadToEnd().Trim();
                process?.WaitForExit(3000);
                return output;
            }
            catch
            {
                return null;
            }
        }

        private void WriteHeader(string commitHash)
        {
            try
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                using var stream = new FileStream(_filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                using var writer = new StreamWriter(stream) { AutoFlush = true };
                writer.WriteLine("================================================================");
                writer.WriteLine($"  Run started: {DateTimeOffset.Now}");
                writer.WriteLine($"  Git commit:  {commitHash ?? "unknown"}");
                writer.WriteLine($"  Log level:   {_level}");
                writer.WriteLine("================================================================");
                writer.WriteLine();
            }
            catch { /* ignore */ }
        }

        #endregion

        #region Private Classes

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new NullScope();

            private NullScope() { }

            public void Dispose() { }
        }

        #endregion
    }
}
