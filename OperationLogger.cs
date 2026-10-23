using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace DeleteDuplicates
{
    internal sealed class RunCounters
    {
        public int DirectoriesVisited;
        public int DirectoriesFailed;
        public int ImageCandidates;
        public int ReparseEntriesSkipped;
        public int CloudPlaceholdersSkipped;
        public int MetadataFailures;
        public int FilesHashed;
        public int HashFailures;
        public int UniqueSizeFiles;
        public int ExactDuplicateGroups;
        public int RemovableDuplicateInstances;
        public int MovedToRecycleBin;
        public int RecycleFailures;
        public int RevalidationSkips;
        public int Survivors;
        public int GroupsSkipped;
    }

    /// <summary>
    /// One UTF-8 log file per run. Logging is a safety requirement: if no log
    /// can be created the process refuses to touch any file.
    /// </summary>
    internal sealed class OperationLogger : IDisposable
    {
        private const string TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffzzz";

        private readonly StreamWriter _writer;
        private readonly object _sync = new object();
        private readonly Stopwatch _stopwatch;

        public readonly RunCounters Counters = new RunCounters();
        public readonly string LogPath;

        private OperationLogger(StreamWriter writer, string logPath)
        {
            _writer = writer;
            LogPath = logPath;
            _stopwatch = Stopwatch.StartNew();
        }

        public long ElapsedMilliseconds
        {
            get { return _stopwatch.ElapsedMilliseconds; }
        }

        /// <summary>
        /// Tries %TEMP%, then %LOCALAPPDATA%\Temp. Returns null if neither is
        /// writable; the caller must then exit without processing anything.
        /// </summary>
        public static OperationLogger TryCreate()
        {
            DateTime now = DateTime.Now;
            int pid = Process.GetCurrentProcess().Id;

            string fileName = string.Format(
                CultureInfo.InvariantCulture,
                "DeleteDuplicates_{0:yyyyMMdd_HHmmss_fff}_{1}.log",
                now,
                pid);

            foreach (string directory in CandidateLogDirectories())
            {
                if (string.IsNullOrEmpty(directory))
                    continue;

                try
                {
                    Directory.CreateDirectory(directory);

                    string path = Path.Combine(directory, fileName);

                    var stream = new FileStream(
                        path,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.Read,
                        4096,
                        FileOptions.WriteThrough);

                    var writer = new StreamWriter(stream, new UTF8Encoding(false));
                    writer.AutoFlush = true;

                    return new OperationLogger(writer, path);
                }
                catch (Exception)
                {
                    // Try the next location.
                }
            }

            return null;
        }

        private static string[] CandidateLogDirectories()
        {
            string temp = null;
            string localAppDataTemp = null;

            try
            {
                temp = Path.GetTempPath();
            }
            catch (Exception)
            {
            }

            try
            {
                string localAppData = Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData);

                if (!string.IsNullOrEmpty(localAppData))
                    localAppDataTemp = Path.Combine(localAppData, "Temp");
            }
            catch (Exception)
            {
            }

            return new[] { temp, localAppDataTemp };
        }

        public void WriteHeader(string version, string root, string rawArgument)
        {
            DateTime local = DateTime.Now;

            lock (_sync)
            {
                _writer.WriteLine("DeleteDuplicates version=" + version);
                _writer.WriteLine("startLocal=" + local.ToString(TimestampFormat, CultureInfo.InvariantCulture));
                _writer.WriteLine("startUtc=" + local.ToUniversalTime().ToString(
                    "yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
                _writer.WriteLine("argument=" + Quote(rawArgument));
                _writer.WriteLine("root=" + Quote(root));
                _writer.WriteLine("processId=" + Process.GetCurrentProcess().Id);
                _writer.WriteLine("logPath=" + Quote(LogPath));
                _writer.WriteLine();
            }
        }

        public void Write(string tag, params string[] keyValuePairs)
        {
            var builder = new StringBuilder();

            builder.Append(DateTime.Now.ToString(TimestampFormat, CultureInfo.InvariantCulture));
            builder.Append(" [");
            builder.Append(tag);
            builder.Append(']');

            foreach (string pair in keyValuePairs)
            {
                builder.Append('\t');
                builder.Append(pair);
            }

            lock (_sync)
            {
                _writer.WriteLine(builder.ToString());
            }
        }

        public void WriteException(string tag, string path, string operation, Exception exception)
        {
            int hresult = exception == null ? 0 : System.Runtime.InteropServices.Marshal.GetHRForException(exception);

            Write(tag,
                "path=" + Quote(path),
                "operation=" + Quote(operation),
                "hresult=" + FormatHResult(hresult),
                "message=" + Quote(exception == null ? string.Empty : exception.Message));
        }

        public void WriteSummary()
        {
            lock (_sync)
            {
                _writer.WriteLine();
                _writer.WriteLine("summary:");
                _writer.WriteLine("directoriesVisited=" + Counters.DirectoriesVisited);
                _writer.WriteLine("directoriesFailed=" + Counters.DirectoriesFailed);
                _writer.WriteLine("imageCandidates=" + Counters.ImageCandidates);
                _writer.WriteLine("reparseEntriesSkipped=" + Counters.ReparseEntriesSkipped);
                _writer.WriteLine("cloudPlaceholdersSkipped=" + Counters.CloudPlaceholdersSkipped);
                _writer.WriteLine("metadataFailures=" + Counters.MetadataFailures);
                _writer.WriteLine("uniqueSizeFiles=" + Counters.UniqueSizeFiles);
                _writer.WriteLine("filesHashed=" + Counters.FilesHashed);
                _writer.WriteLine("hashFailures=" + Counters.HashFailures);
                _writer.WriteLine("exactDuplicateGroups=" + Counters.ExactDuplicateGroups);
                _writer.WriteLine("removableDuplicateInstances=" + Counters.RemovableDuplicateInstances);
                _writer.WriteLine("movedToRecycleBin=" + Counters.MovedToRecycleBin);
                _writer.WriteLine("recycleFailures=" + Counters.RecycleFailures);
                _writer.WriteLine("revalidationSkips=" + Counters.RevalidationSkips);
                _writer.WriteLine("groupsSkipped=" + Counters.GroupsSkipped);
                _writer.WriteLine("survivors=" + Counters.Survivors);
                _writer.WriteLine("elapsedMilliseconds=" + _stopwatch.ElapsedMilliseconds);
                _writer.WriteLine("completedLocal=" + DateTime.Now.ToString(
                    TimestampFormat, CultureInfo.InvariantCulture));
            }
        }

        public static string Quote(string value)
        {
            if (value == null)
                return "\"\"";

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        public static string FormatHResult(int hresult)
        {
            return "0x" + ((uint)hresult).ToString("X8", CultureInfo.InvariantCulture);
        }

        public void Dispose()
        {
            lock (_sync)
            {
                _writer.Dispose();
            }
        }
    }
}
