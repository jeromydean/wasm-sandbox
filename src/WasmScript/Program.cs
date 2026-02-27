// This C# "script" is compiled to WASM and executed by the host via wasmtime.
// Output is captured and returned to the host.

namespace WasmSandbox.WasmScript
{
  internal static class Program
  {
    public static void Main(string[] args)
    {
      Console.WriteLine("Hello from WASM!");
      int result = 2 + 2;
      Console.WriteLine($"Result: {result}");
    }
  }
}
