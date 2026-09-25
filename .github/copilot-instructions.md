# Copilot Instructions for nanoFirmwareFlasher

## Repository Overview

This repository contains the **.NET nanoFramework Firmware Flasher** (`nanoff`), a cross-platform .NET tool and library for flashing firmware images to nanoFramework-supported microcontroller targets. It is published both as:

- A **.NET global tool** (`dotnet tool install -g nanoff`)
- A **NuGet library** (`nanoFramework.Tools.FirmwareFlasher`) for embedding in third-party apps

Firmware images are fetched from **Cloudsmith** repositories (`nanoframework-images` for stable, `nanoframework-images-dev` for preview, `nanoframework-images-community-targets` for community targets).

---

## Solution Structure

```
nanoFirmwareFlasher.sln
├── nanoFirmwareFlasher.Library/   # Core library (NuGet: nanoFramework.Tools.FirmwareFlasher)
│   ├── Esp32Serial/               # Native C# ESP32 serial bootloader protocol implementation
│   ├── Swd/                       # Native SWD / ST-LINK / CMSIS-DAP transports (STM32)
│   ├── UsbDfu/                    # Native USB DFU transport (STM32)
│   ├── DeploymentHelpers/         # Device helper utilities
│   ├── FileDeployment/            # File-based deployment support
│   ├── NetworkDeployment/         # Network-based deployment support
│   ├── Exceptions/                # Custom exception types
│   └── ...                        # Per-platform operations, firmware packages, utilities
├── nanoFirmwareFlasher.Tool/      # CLI entry point (command: nanoff)
│   ├── Program.cs                 # Main async entry point, verb parsing and dispatch
│   ├── Verbs/                     # Per-verb option classes, VerbTokenizer, VerbOptionsMapper
│   ├── Options.cs                 # Legacy flat options bag consumed by the managers
│   └── *Manager.cs                # Per-platform CLI managers (Esp32, Stm32, TI, Silabs, Pico, NanoDevice)
├── nanoFirmwareFlasher.Tests/     # Unit tests (MSTest); shared helpers in Helpers/
├── lib/                           # Bundled third-party CLI tools (jlink, silink, uniflash, esp32 bootloaders)
└── Samples/                       # Sample apps demonstrating library usage (separate Samples.sln)
```

---

## Supported Platforms and Connections

| Platform        | Connection Method           | Key Classes                              |
|-----------------|-----------------------------|------------------------------------------|
| ESP32 / S2 / S3 | Serial (native C# protocol) | `EspTool`, `Esp32Operations`, `Esp32Firmware` |
| STM32           | Native DFU / SWD / ST-LINK / CMSIS-DAP / UART bootloader (native C# protocols) | `StmNativeDfuDevice`, `StmSwdDevice`, `StmStLinkDevice`, `Stm32UartDevice`, `Stm32Operations`, `Stm32Firmware` (devices implement `IStmFlashableDevice`) |
| Raspberry Pi Pico / Pico 2 | UF2 copy to BOOTSEL mass-storage drive | `PicoOperations`, `PicoFirmware`, `PicoUf2Utility`, `PicoDeviceInfo` |
| TI CC13x2/CC26x2| TI Uniflash CLI             | `CC13x26x2Operations`, `CC13x26x2Firmware` |
| Silabs Giant Gecko | J-Link / silink CLI      | `JLinkOperations`, `JLinkFirmware`, `SilinkCli` |

> **Important:** ESP32 support uses a native C# implementation of the Espressif serial bootloader protocol (`EspTool` / `Esp32Serial/`). The `esptool` Python tool is no longer used (removed as of April 2026). STM32 support was similarly migrated to native C# protocol implementations (`StmNativeDfuDevice`, `StmSwdDevice`, `StmStLinkDevice`); the external STM32 CLI tool (`STM32_Programmer_CLI`, wrapped by the now-removed `StmDfuDevice`/`StmJtagDevice`/`StmDeviceBase` classes) has been fully removed and will not be reintroduced.

---

## Key Architectural Patterns

### CLI (verbs)
The CLI uses a **verb + bare keyword** syntax (`nanoff flash target ESP32_PSRAM_REV0 serialport COM31`); the `--` prefix on keywords is optional. Each command performs one verb only (e.g. a firmware update and an app deploy are separate `flash` and `deploy` invocations). Parsing is a three-stage pipeline in `nanoFirmwareFlasher.Tool/`:

1. `Verbs/VerbTokenizer.Normalize` rewrites bare-word args into `--keyword value` form using the option classes' attributes (`KnownVerbs` maps verb word → option type; `help`/`version` become `--help`/`--version`).
2. CommandLineParser parses into one of the per-verb classes in `Verbs/` (`FlashOptions`, `DeployOptions`, `ListOptions`, `DetailsOptions`, `IdentifyOptions`, `DriversOptions`, `CacheOptions`, all deriving from `VerbOptionsBase`), each with an optional static `Validate`.
3. `Verbs/VerbOptionsMapper.ToLegacyOptions` flattens the verb object into the legacy `Options` bag, which `Program` dispatches to the per-platform `*Manager` classes and to `FileDeploymentManager` / `NetworkDeploymentManager`.

Adding a CLI option therefore means touching the verb class, the mapper, and (if new) the legacy `Options` field read by the manager. `VerbTokenizerTests`, `VerbOptionsTests` and `VerbOptionsMapperTests` cover this pipeline. `README.md` documents every verb and keyword with examples and must be updated along with CLI changes.

### Output
All output goes through the `OutputWriter` static class, which uses `AsyncLocal<IOutputWriter>` to support per-test output redirection. Never write directly to `Console`; use `OutputWriter.Write()` / `OutputWriter.WriteLine()`.

### Exit Codes
All operations return an `ExitCodes` enum value. Return `ExitCodes.OK` (0) on success. Errors are in ranges:
- `E1000` – DFU errors
- `E2000` – nanoDevice errors
- `E3000` – Raspberry Pi Pico errors
- `E4000` – ESP32 errors
- `E5000` – STM32/JTAG errors
- `E6000` – COM port errors
- `E7000` – TI errors
- `E8000` – J-Link errors
- `E9000` – General/application errors

### Firmware Packages
`FirmwarePackage` is the abstract base class for all firmware. Concrete subclasses: `Esp32Firmware`, `Stm32Firmware`, `JLinkFirmware`, `CC13x26x2Firmware`, `PicoFirmware`. Firmware is downloaded from Cloudsmith, cached under `~/.nanoFramework/fw_cache/`.  
`FirmwarePackage.LocationPathBase` is settable (for tests) via its internal setter.

### Telemetry
`NanoTelemetryClient` wraps Application Insights. The connection string is loaded from `appsettings.json` in the Tool project.

### Namespace
All classes use the namespace `nanoFramework.Tools.FirmwareFlasher`.

---

## Building

```bash
dotnet build nanoFirmwareFlasher.sln
```

- The library targets **net8.0** and **net472**.
- The tool targets **net8.0** (or **net472** when built with `VSCodeExtensionBuild=True`, for the VS Code extension bundle).
- Tests target **net8.0**.
- Run the CLI from source with `dotnet run --project nanoFirmwareFlasher.Tool -- list ports`.
- NuGet packages use locked restore (`packages.lock.json`). Lock mode is enforced in CI (`TF_BUILD` or `ContinuousIntegrationBuild` env vars). When adding/updating NuGet dependencies locally, run `dotnet restore` to update the lock file.

---

## Testing

```bash
dotnet test nanoFirmwareFlasher.Tests/nanoFirmwareFlasher.Tests.csproj

# single test class / method
dotnet test nanoFirmwareFlasher.Tests/nanoFirmwareFlasher.Tests.csproj --filter "FullyQualifiedName~VerbTokenizerTests"
dotnet test nanoFirmwareFlasher.Tests/nanoFirmwareFlasher.Tests.csproj --filter "Name=<MethodName>"

# skip tests that call the Cloudsmith API (network)
dotnet test nanoFirmwareFlasher.Tests/nanoFirmwareFlasher.Tests.csproj --filter "TestCategory!=CloudSmith"
```

- Uses **MSTest** (`MSTest.TestFramework`, `MSTest.TestAdapter`).
- Tests redirect output using `OutputWriter.SetOutputWriter()` (internal API).
- `FirmwarePackage.LocationPathBase` can be set per test to isolate firmware cache.
- Tests run in parallel at method level (`[assembly: Parallelize]` in `MSTest.cs`), which works because of `AsyncLocal` usage in `OutputWriter` and `FirmwarePackage`. Tests must not share static state; use `OutputWriterHelper` / `TestDirectoryHelper` from `Tests/Helpers/`.
- Most tests that exercise hardware or network access are integration tests and may be skipped in CI environments without physical devices.

---

## Coding Conventions (from `.editorconfig`)

- **File encoding**: `utf-8-bom`, CRLF line endings.
- **License header** on every `.cs` file:
  ```csharp
  // Licensed to the .NET Foundation under one or more agreements.
  // The .NET Foundation licenses this file to you under the MIT license.
  ```
- **Indentation**: 4 spaces for C#; 2 spaces for XML/YAML/project files.
- **Naming**:
  - Private/internal fields: `_camelCase`
  - Private/internal static fields: `s_camelCase`
  - Constants: `PascalCase`
- **Braces**: Always use braces (`csharp_prefer_braces = true`).
- **`var`**: Avoid `var` except when the type is apparent (`csharp_style_var_for_built_in_types = false`).
- Prefer expression-bodied members, null-coalescing, null-conditional operators.
- System `using` directives come first, sorted.

---

## Assembly Signing

All projects are strong-named using `Key.snk` at the repository root. The `<SignAssembly>` and `<AssemblyOriginatorKeyFile>` MSBuild properties are set in each `.csproj`.

---

## External Bundled Tools (lib/ directory)

The `lib/` directory contains pre-built third-party tool executables that are copied to the build output. These are referenced from the `.csproj` via MSBuild `<None Include>` items. There is a corresponding `nugetcontent.targets` file that controls which of these are packed into the NuGet package.

When adding or changing external tool include paths, **also update `nugetcontent.targets`** (noted in a warning comment in `nanoFirmwareFlasher.Library.csproj`).

ESP32 stub images (JSON) are embedded as resources in `Esp32Serial/StubImages/`. Update them by running `update-stubs.ps1`.

---

## Versioning

Uses **Nerdbank.GitVersioning** (`version.json`). The version is automatically derived from git history.

---

## CI/CD

- CI runs on **Azure Pipelines** (`azure-pipelines.yml`), triggered on `main`, `develop`, and `release-*` branches.
- PRs always trigger a build. Changes limited to `Samples/`, `.github/`, `assets/`, `config/` or the READMEs do not trigger branch builds.
- Pipeline templates are shared from the `nanoframework/nf-tools` repository.

---

## Known Issues and Workarounds

- **STM32 native transports** require the probe's debug interface to be bound to a WinUSB-class driver (WinUSB/libusbK/libusb-win32) on Windows, because the transport uses raw USB via LibUsbDotNet. Bind the `ST-Link Debug` interface to WinUSB with Zadig. ST's proprietary STSW-LINK009 driver is **not** compatible with the native transport (reinstall it only to restore STM32CubeProgrammer/STM32CubeIDE for that probe). For a `STM32 BOOTLOADER` device in DFU mode, also bind it to WinUSB with Zadig. No external STM32 CLI tool is used anymore.
- **ESP32-S2**: It is not possible to safely auto-detect the best image; users must always specify `--target`.
- **FeatherS2, TinyS2, some S3 modules**: Must be placed in download mode manually (hold BOOT, click RESET, release BOOT) before flashing.
- When running `dotnet restore` in locked mode fails, it usually means a package was added/updated without regenerating the lock file. Run `dotnet restore --force-evaluate` to regenerate it.
