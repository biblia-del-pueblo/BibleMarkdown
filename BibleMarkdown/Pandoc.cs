using CliWrap;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BibleMarkdown;

public class Pandoc: WithTransform
{
	public readonly static Version PackagedVersion = new Version("3.13"); 
	
	string PandocPath = "";
	public Pandoc(Transform transform): base(transform) {
        var arch = RuntimeInformation.ProcessArchitecture;
        if (arch == Architecture.X86) arch = Architecture.X64;
        var archstr = arch.ToString().ToLower();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) SetPandocPath(@"runtimes\win\pandoc.exe");
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) SetPandocPath($"rutimes/linux-{archstr}/pandoc");
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) SetPandocPath($"runtimes/osx-{archstr}/pandoc");
        else throw new PlatformNotSupportedException();
	}
    public static bool IsWindows => RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
	IEnumerable<string> Paths
	{
		get
		{
			string proc, machine = "", user = "";
			string[] sources;
			proc = System.Environment.GetEnvironmentVariable("PATH") ?? "";
			if (IsWindows)
			{
				machine = System.Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) ?? "";
				user = System.Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
				sources = new string[] {
						System.Environment.GetFolderPath(System.Environment.SpecialFolder.System),
						System.Environment.GetFolderPath(System.Environment.SpecialFolder.SystemX86),
						proc, machine, user };
			}
			else sources = new string[] { proc };

			return sources
				.SelectMany(paths => paths.Split(new char[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path.Trim())
				.Distinct();
		}
	}
	public string? Find(string cmd)
	{
		string? file = null;
		cmd = cmd.Trim('"');
		if (cmd.IndexOf(Path.DirectorySeparatorChar) >= 0)
		{
			if (File.Exists(cmd)) file = cmd;
		}
		else
		{
			file = Paths
				  .Select(p => IsWindows ? Path.Combine(p, cmd + ".exe") : Path.Combine(p,cmd))
				  .FirstOrDefault(p => File.Exists(p));
		}
		return file;
	}

	void SetPandocPath(string exe) 
	{
		var pandocPath = Find("pandoc");
		if (pandocPath != null)
		{
			var version = Version;
			if (version != null && version < PackagedVersion)
			{
				pandocPath = null; // use internal pandoc
			}
		}
		if (pandocPath == null)
		{
			var exepath = AppDomain.CurrentDomain.BaseDirectory;
			pandocPath = Path.Combine(exepath, exe);
			if (!IsWindows && PandocPath.Contains(Path.DirectorySeparatorChar)) Process.Start("chmod", $"+x \"{PandocPath}\"").WaitForExit();
			// Set PATH to pandoc
			var userenv = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
			var pandocpath = Path.GetDirectoryName(PandocPath) ?? "";
			var envpaths = userenv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
			if (!envpaths.Contains(pandocpath))
			{
				userenv = string.Join(Path.PathSeparator, envpaths.Concat(new[] { pandocpath }));
				try
				{
					Environment.SetEnvironmentVariable("PATH", userenv, EnvironmentVariableTarget.User);
				}
				catch { }
			}
		} else
		{
			Log("Pandoc found in PATH");
		}
		PandocPath = pandocPath;
		//Log(Version?.ToString(3));
	}

    public Version? Version
	{
		get
		{
			var stdOutBuffer = new StringBuilder();
			var result = Cli.Wrap(PandocPath)
				.WithArguments($"--version")
				.WithStandardOutputPipe(PipeTarget.ToStringBuilder(stdOutBuffer))
				.ExecuteAsync().Task.Result;
			return Version.TryParse(Regex.Match(stdOutBuffer.ToString(), "[0-9.]+").Value, out var version) ? version : null;
		}
	}
	public async Task RunAsync(string sourcefile, string destfile, string sourceformat, string destformat)
	{
		var stdOutBuffer = new StringBuilder();
		var stdErrBuffer = new StringBuilder();

		var result = await Cli.Wrap(PandocPath)
			.WithArguments($@"""{sourcefile}"" -o ""{destfile}"" --from {sourceformat} --to {destformat}")
			.WithWorkingDirectory(Environment.CurrentDirectory)
			.WithStandardOutputPipe(PipeTarget.ToStringBuilder(stdOutBuffer))
			.WithStandardErrorPipe(PipeTarget.ToStringBuilder(stdErrBuffer))
			.ExecuteAsync();

		Log(stdOutBuffer.ToString().Trim(' ', '\t', '\r', '\n'));
		Log(stdErrBuffer.ToString().Trim(' ', '\t', '\r', '\n'));
	}
}
