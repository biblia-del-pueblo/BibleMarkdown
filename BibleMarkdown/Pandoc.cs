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

public class Pandoc
{
	static string path;
	public static bool IsWindows => RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
	public static IEnumerable<string> Paths
	{
		get
		{
			string proc, machine = "", user = "";
			string[] sources;
			proc = System.Environment.GetEnvironmentVariable("PATH");
			if (IsWindows)
			{
				machine = System.Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine);
				user = System.Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User);
				sources = new string[] {
						System.Environment.GetFolderPath(System.Environment.SpecialFolder.System),
						System.Environment.GetFolderPath(System.Environment.SpecialFolder.SystemX86),
						proc, machine, user };
			}
			else sources = new string[] { proc };

			return sources
				.SelectMany(paths => paths.Split(new char[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
				.Select(path => path.Trim())
				.Distinct();
		}
	}
	public static string Find(string cmd)
	{
		string file = null;
		cmd = cmd.Trim('"');
		if (cmd.IndexOf(Path.DirectorySeparatorChar) >= 0)
		{
			if (File.Exists(cmd)) file = cmd;
		}
		else
		{
			file = Paths
				  .SelectMany(p =>
				  {
					  var p1 = Path.Combine(p, cmd);
					  return new string[] { p1, Path.ChangeExtension(p1, "exe") };
				  })
				  .FirstOrDefault(p => File.Exists(p));
		}
		return file;
	}

	public static void SetPandocPath(string exe) 
	{
		path = Find("pandoc");
		Version version;
		if (path != null)
		{
			var ver = Regex.Match(Version(), "[0-9\\.]+");
			if (ver.Success && System.Version.TryParse(ver.Value, out version))
			{
				// if version < 3.9.0.2 use internal pandoc
				if (version.Major < 3 || version.Major == 3 &&
					(version.Minor < 9 || version.Minor == 9 &&
					(version.Build < 0 || version.Build == 0 &&
					version.Revision < 2))) {
					path = null; // use internal pandoc
				}
			}
		}
		if (path == null)
		{
			var exepath = AppDomain.CurrentDomain.BaseDirectory;
			path = Path.Combine(exepath, exe);
			if (path.Contains("/")) Process.Start("chmod", $"+x \"{path}\"").WaitForExit();
			// Set PATH to pandoc
			var userenv = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
			var pandocpath = Path.GetDirectoryName(path) ?? "";
			var envpaths = userenv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
			if (!envpaths.Contains(pandocpath))
			{
				userenv = string.Join(Path.PathSeparator, envpaths.Concat(new[] { pandocpath }));
				; Environment.SetEnvironmentVariable("PATH", userenv, EnvironmentVariableTarget.User);
			}
		} else
		{
			Program.Log("Pandoc found in PATH");
		}
		Program.Log(Version());
	}

	public static string Version()
	{
		var stdOutBuffer = new StringBuilder();
		var result = Cli.Wrap(path)
			.WithArguments($"--version")
			.WithStandardOutputPipe(PipeTarget.ToStringBuilder(stdOutBuffer))
			.ExecuteAsync().Task.Result;
		return stdOutBuffer.ToString().Split('\n').FirstOrDefault() ?? "";
	}
	public static async Task RunAsync(string sourcefile, string destfile, string sourceformat, string destformat)
	{
		var stdOutBuffer = new StringBuilder();
		var stdErrBuffer = new StringBuilder();

		var result = await Cli.Wrap(path)
			.WithArguments($@"""{sourcefile}"" -o ""{destfile}"" --from {sourceformat} --to {destformat}")
			.WithWorkingDirectory(Environment.CurrentDirectory)
			.WithStandardOutputPipe(PipeTarget.ToStringBuilder(stdOutBuffer))
			.WithStandardErrorPipe(PipeTarget.ToStringBuilder(stdErrBuffer))
			.ExecuteAsync();

		Program.Log(stdOutBuffer.ToString().Trim(' ', '\t', '\r', '\n'));
		Program.Log(stdErrBuffer.ToString().Trim(' ', '\t', '\r', '\n'));
	}
}
