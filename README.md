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
- **`src/WasmScript`** – C# “script” compiled to a WASI component with the Componentize SDK; output is captured via stdout.
- **`src/AnimalComponent`** – **WIT export example**: library component that exports `get-animal` returning a typed record via the component model (the component itself doesn’t serialize to stdout). Uses a `.wit` file and wit-bindgen-generated C#. The current host workaround invokes via wasmtime CLI, which prints the return value to stdout in WAVE, so we still parse that output until .NET has a proper component-invoke API.
- **`src/ComponentHost`** – Console app that builds AnimalComponent and **invokes** the component export `get-animal()` via `wasmtime run --invoke`, then parses the WAVE result and prints the typed object.

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

With this, restore and build work from both the command line and Visual Studio. If you add or upgrade packages used by WasmScript or AnimalComponent, add (or update) their `PackageVersion` in `src/Directory.Packages.props` and keep the corresponding `PackageReference` in the project without a `Version` attribute.

## WIT export: returning a typed object (AnimalComponent)

Instead of serializing to stdout, you can use [WIT](https://github.com/WebAssembly/component-model/blob/main/design/mvp/WIT.md) so the component **exports** a function that returns a typed value.

- **`src/AnimalComponent/animal.wit`** – Defines a record `animal` and an interface `api` with `get-animal: func() -> animal`. World `script` exports that interface.
- **`src/AnimalComponent/ApiImpl.cs`** – Implements the wit-bindgen-generated `IApi` and returns an `IApi.Animal` instance; the component returns the value through the WIT export, not by writing to stdout.
- Build: `dotnet build src/AnimalComponent/AnimalComponent.csproj` → `src/AnimalComponent/bin/Debug/net10.0/wasi-wasm/publish/AnimalComponent.wasm`.

**Invoking the export from .NET:** Use **ComponentHost**, which builds AnimalComponent and calls the export via the wasmtime CLI ([wasmtime 33+](https://bytecodealliance.org/articles/invoking-component-functions-in-wasmtime-cli) supports `wasmtime run --invoke 'get-animal()'` for components). ComponentHost parses the WAVE-encoded return value and prints the typed animal:

```bash
cd src
dotnet run --project ComponentHost
```

You need **wasmtime v33.0.0 or newer** for component `--invoke`. The host parses the WAVE record from stdout into a C# object and displays it.

**Invoking without stdout?** The wasmtime engine (Rust/C API) can load a component, instantiate it, and call an export with the return value returned in-process—no CLI, no stdout. The [wasmtime-dotnet](https://github.com/bytecodealliance/wasmtime-dotnet) bindings do **not** yet expose this Component Model API; they only wrap the **core module** API ([Module](https://bytecodealliance.github.io/wasmtime-dotnet/api/Wasmtime.Module.html), [Linker](https://bytecodealliance.github.io/wasmtime-dotnet/api/Wasmtime.Linker.html), [Instance](https://bytecodealliance.github.io/wasmtime-dotnet/api/Wasmtime.Instance.html), [Function](https://bytecodealliance.github.io/wasmtime-dotnet/api/Wasmtime.Function.html)). So from .NET today the only practical option is the CLI + stdout + WAVE parsing used here.

- **Why not `Module.FromFile` + `GetFunction`?** That API is for **core WebAssembly modules** (single module, simple value types), not for **components**. Our AnimalComponent is a component (component model format); loading it as a module would use the wrong binary format. Core modules also don’t handle WIT types (e.g. records like `animal`). See [Using WebAssembly from .NET](https://docs.wasmtime.dev/lang-dotnet.html) for the current wasmtime-dotnet capabilities.
- **Component support in wasmtime-dotnet:** Tracked in [bytecodealliance/wasmtime-dotnet#346](https://github.com/bytecodealliance/wasmtime-dotnet/issues/346) (Component Support WIP) and [bytecodealliance/wasmtime-dotnet#324](https://github.com/bytecodealliance/wasmtime-dotnet/issues/324) (Component model support). Once that lands, ComponentHost could call the export in-process and get a typed result without stdout.
