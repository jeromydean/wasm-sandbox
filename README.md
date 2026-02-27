# .NET 10 Wasm Sandbox

A .NET 10 console app that compiles C# to WebAssembly using [BytecodeAlliance.Componentize.DotNet.Wasm.SDK](https://www.nuget.org/packages/BytecodeAlliance.Componentize.DotNet.Wasm.SDK), runs it with **wasmtime**, and captures the result.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [wasmtime](https://github.com/bytecodealliance/wasmtime/releases) on your PATH (for running the compiled WASM)
- **WASI SDK** (for building WasmScript to WASM):
  - Download from [WebAssembly/wasi-sdk releases](https://github.com/WebAssembly/wasi-sdk/releases) (e.g. `wasi-sdk-29.0`; the build expects 29.x).
  - Extract so the SDK root contains `share/wasi-sysroot`.
  - Either set the `WASI_SDK_PATH` environment variable to that root, or place it at `%USERPROFILE%\.wasi-sdk\wasi-sdk-29.0` (the build will use this by default).

## Layout

- **`src/Host`** – Console app that builds the script project to WASM and runs it via wasmtime.
- **`src/WasmScript`** – C# “script” compiled to a WASI component with the Componentize SDK; edit `Program.cs` here to change what runs in WASM.

## Run

From the repo root:

```bash
cd src
dotnet run --project Host
```

Or build then run:

```bash
cd src
dotnet build WasmSandbox.slnx
dotnet run --project Host --no-build
```

The host will:

1. Build `WasmScript` for `wasi-wasm` (output: `src/WasmScript/bin/Release/net10.0/wasi-wasm/publish/WasmScript.wasm`).
2. Run that component with `wasmtime run -S cli ...`.
3. Print the script’s stdout as the result.

## Notes

- **WASI SDK**: Building WasmScript requires the WASI SDK (29.x). The project expects it at `%USERPROFILE%\.wasi-sdk\wasi-sdk-29.0` by default. To use another location, set the `WasiSdkPath` MSBuild property or the `WASI_SDK_PATH` environment variable to the SDK root (the folder that contains `share/wasi-sysroot`).
- **Windows**: The NativeAOT-LLVM dependency used by the SDK currently targets Windows; the WasmScript project references `runtime.win-x64.microsoft.dotnet.ilcompiler.llvm`. On Linux/macOS you’d use the matching runtime package.
- **NuGet**: `src/nuget.config` adds the `dotnet-experimental` feed required by the Componentize SDK and LLVM packages.

## Workaround: Visual Studio restore (NU1015 / NU1103 / NU1604)

The Componentize SDK has a transitive dependency on `Microsoft.DotNet.ILCompiler.LLVM` **without a version**. In .NET 10 this triggers [NU1015](https://learn.microsoft.com/en-us/dotnet/core/compatibility/sdk/10.0/nu1015-packagereference-version) (PackageReference without version), and in Visual Studio restore can also fail with NU1103 (“Unable to find a stable package … with version”) or NU1604 (“does not contain an inclusive lower bound”). Command-line restore may still succeed.

**What we do:** We use **Central Package Management (CPM)** so every reference to these packages gets a single, explicit version:

- **`src/Directory.Packages.props`** – `ManagePackageVersionsCentrally=true` and `PackageVersion` entries for the Componentize SDK and the ILCompiler.LLVM packages. That gives the unversioned transitive dependency a version during restore.
- **`src/WasmScript/WasmScript.csproj`** – `PackageReference` items for those packages **omit** `Version`; the version comes from the central file.

With this, restore and build work from both the command line and Visual Studio. If you add or upgrade packages used by WasmScript, add (or update) their `PackageVersion` in `src/Directory.Packages.props` and keep the corresponding `PackageReference` in the project without a `Version` attribute.
