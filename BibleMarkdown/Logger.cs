using System;
using System.Collections.Generic;
using System.Text;
using IO = System.IO;


namespace BibleMarkdown
{
    public interface ISimpleLogger
    {
        public void Log(string message);
        public void LogFile(string file);
        public void LogFile(string file, string label);
    }

    public class ConsoleAndFileLogger: ISimpleLogger
    {
        Transform Transform { get; set; }
        public string? File
        {
            get => field ??= Transform != null ? Path.Combine(Transform.OutputPath, "bibmark.log") : null;
            set => field = value;
        }
        public ConsoleAndFileLogger(Transform transform, string? file = null)
        {
            Transform = transform;
            File = file;
            if (!string.IsNullOrEmpty(File))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(File));
                IO.File.WriteAllText(File, "");
            }
        }
        public void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            
            var line = message + Environment.NewLine;
            lock (this)
            {
                Console.WriteLine(message);
                if (!string.IsNullOrEmpty(File)) IO.File.AppendAllText(File, line);
            }
        }
        public void LogFile(string file)
        {
            LogFile(file, "Created");
        }
        public void LogFile(string file, string label)
        {
            var current = Directory.GetCurrentDirectory();
            if (file.StartsWith(current))
            {
                file = file.Substring(current.Length);
            }
            Log($"{label} {file}");
        }
    }
}
