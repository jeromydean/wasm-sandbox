using System.Diagnostics;
using System.Reflection;

namespace WasmSandbox.Host
{
  internal static class Program
  {
    public static int Main(string[] args)
    {
      // When run from Host output: .../Host/bin/Debug/net10.0 -> go up 3 levels to src
      string? hostDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
          ?? AppContext.BaseDirectory;
      string srcDir = Path.GetFullPath(Path.Combine(hostDir, "..", "..", "..", ".."));
      string scriptProjectDir = Path.Combine(srcDir, "WasmScript");
      string scriptCsproj = Path.Combine(scriptProjectDir, "WasmScript.csproj");

      if (!File.Exists(scriptCsproj))
      {
        Console.Error.WriteLine($"Script project not found: {scriptCsproj}");
        return 1;
      }

      Console.WriteLine("Building C# script to WASM...");
      (int ExitCode, string Stdout, string Stderr) buildResult = RunDotnet(scriptProjectDir, "build");
      if (buildResult.ExitCode != 0)
      {
        Console.Error.WriteLine("Build failed:");
        Console.Error.WriteLine(buildResult.Stderr);
        return buildResult.ExitCode;
      }

      // Componentize SDK output: bin/Debug/net10.0/wasi-wasm/publish/WasmScript.wasm
      string wasmPath = Path.Combine(scriptProjectDir, "bin", "Debug", "net10.0", "wasi-wasm", "publish", "WasmScript.wasm");
      if (!File.Exists(wasmPath))
      {
        Console.Error.WriteLine($"WASM output not found: {wasmPath}");
        return 1;
      }

      Console.WriteLine($"Running WASM: {wasmPath}");
      (int ExitCode, string Stdout, string Stderr) runResult = RunWasmtime(wasmPath);
      Console.WriteLine("--- Script output ---");
      Console.WriteLine(runResult.Stdout);
      if (!string.IsNullOrEmpty(runResult.Stderr))
      {
        Console.Error.WriteLine(runResult.Stderr);
      }

      return runResult.ExitCode;
    }

    private static (int ExitCode, string Stdout, string Stderr) RunDotnet(string workingDir, params string[] args)
    {
      ProcessStartInfo psi = new ProcessStartInfo("dotnet")
      {
        WorkingDirectory = workingDir,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
      };
      foreach (string a in args)
      {
        psi.ArgumentList.Add(a);
      }

      using (Process p = Process.Start(psi)!)
      {
        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, stdout, stderr);
      }
    }

    private static (int ExitCode, string Stdout, string Stderr) RunWasmtime(string wasmPath)
    {
      ProcessStartInfo psi = new ProcessStartInfo("wasmtime")
      {
        ArgumentList = { "run", "-S", "cli", wasmPath },
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
      };

      try
      {
        using (Process p = Process.Start(psi)!)
        {
          string stdout = p.StandardOutput.ReadToEnd();
          string stderr = p.StandardError.ReadToEnd();
          p.WaitForExit();
          return (p.ExitCode, stdout, stderr);
        }
      }
      catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 2)
      {
        Console.Error.WriteLine("wasmtime not found. Install it from https://github.com/bytecodealliance/wasmtime/releases and ensure it is on PATH.");
        return (1, "", "wasmtime not found on PATH.");
      }
    }
  }
}
