using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;

namespace WasmSandbox.ComponentHost
{
  internal static class Program
  {
    public static int Main(string[] args)
    {
      string? hostDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
          ?? AppContext.BaseDirectory;
      string srcDir = Path.GetFullPath(Path.Combine(hostDir, "..", "..", "..", ".."));
      string componentProjectDir = Path.Combine(srcDir, "AnimalComponent");
      string componentCsproj = Path.Combine(componentProjectDir, "AnimalComponent.csproj");

      if (!File.Exists(componentCsproj))
      {
        Console.Error.WriteLine($"Component project not found: {componentCsproj}");
        return 1;
      }

      Console.WriteLine("Building AnimalComponent to WASM...");
      (int ExitCode, string Stdout, string Stderr) buildResult = RunDotnet(componentProjectDir, "build");
      if (buildResult.ExitCode != 0)
      {
        Console.Error.WriteLine("Build failed:");
        Console.Error.WriteLine(buildResult.Stderr);
        return buildResult.ExitCode;
      }

      string wasmPath = Path.Combine(componentProjectDir, "bin", "Debug", "net10.0", "wasi-wasm", "publish", "AnimalComponent.wasm");
      if (!File.Exists(wasmPath))
      {
        Console.Error.WriteLine($"WASM component not found: {wasmPath}");
        return 1;
      }

      Console.WriteLine("Invoking component export get-animal()...");
      (int ExitCode, string Stdout, string Stderr) invokeResult = RunWasmtimeInvoke(wasmPath, "get-animal");
      if (invokeResult.ExitCode != 0)
      {
        Console.Error.WriteLine("Invoke failed:");
        Console.Error.WriteLine(invokeResult.Stderr);
        return invokeResult.ExitCode;
      }

      string result = invokeResult.Stdout.Trim();
      Console.WriteLine("--- Component return value (WAVE) ---");
      Console.WriteLine(result);

      Animal? animal = TryParseWaveRecord(result);
      if (animal != null)
      {
        Console.WriteLine("--- Parsed object ---");
        Console.WriteLine($"  Name: {animal.Name}");
        Console.WriteLine($"  Species: {animal.Species}");
        Console.WriteLine($"  Age: {animal.Age}");
        Console.WriteLine($"  IsMammal: {animal.IsMammal}");
      }

      return 0;
    }

    /// <summary>
    /// Simple parser for WAVE-style record output from wasmtime --invoke (e.g. (record (name "Rex") (species "Dog") (age 3) (is-mammal true))).
    /// </summary>
    private static Animal? TryParseWaveRecord(string wave)
    {
      string? name = null;
      string? species = null;
      int? age = null;
      bool? isMammal = null;

      // Match (field "value") or (field number) or (field true/false)
      foreach (Match m in Regex.Matches(wave, @"\(\s*([a-z-]+)\s+(""[^""]*""|\d+|true|false)\s*\)"))
      {
        string field = m.Groups[1].Value;
        string value = m.Groups[2].Value;
        if (field == "name")
        {
          name = value.Trim('"');
        }
        else if (field == "species")
        {
          species = value.Trim('"');
        }
        else if (field == "age" && int.TryParse(value, out int a))
        {
          age = a;
        }
        else if (field == "is-mammal")
        {
          isMammal = value == "true";
        }
      }

      if (name != null && species != null && age.HasValue && isMammal.HasValue)
      {
        return new Animal(name, species, age.Value, isMammal.Value);
      }

      return null;
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

    /// <summary>
    /// Invokes a component export via wasmtime run --invoke 'name()' (wasmtime 33+).
    /// </summary>
    private static (int ExitCode, string Stdout, string Stderr) RunWasmtimeInvoke(string wasmPath, string exportName)
    {
      ProcessStartInfo psi = new ProcessStartInfo("wasmtime")
      {
        // Component invoke: wasmtime 33+ uses WAVE; function name in single-quoted form for shell
        ArgumentList = { "run", "--invoke", exportName + "()", wasmPath },
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
        Console.Error.WriteLine("wasmtime not found (need v33+ for --invoke). Install from https://github.com/bytecodealliance/wasmtime/releases");
        return (1, "", "wasmtime not found on PATH.");
      }
    }

    private sealed record Animal(string Name, string Species, int Age, bool IsMammal);
  }
}
