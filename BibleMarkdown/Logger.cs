using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
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
        bool isExtended = false;
        public void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            if (!Regex.IsMatch(message, @"^\.$|^[0-9.]+\s?%$"))
            {
                if (isExtended) message = Environment.NewLine + message + Environment.NewLine;
                else message += Environment.NewLine;
                isExtended = false;
            } else
            {
                isExtended = true;
            }

            lock (this)
            {
                Console.Write(message);
                if (!string.IsNullOrEmpty(File)) IO.File.AppendAllText(File, message);
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
