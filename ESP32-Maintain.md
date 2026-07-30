# Adding a New ESP32 Target

This guide describes every file that must be updated when Espressif releases a new ESP32 chip variant and it needs to be supported by nanoff.

---

## Prerequisites

Before starting, gather the following data from the chip's Technical Reference Manual (TRM) or ESP-IDF source:

| Data | Example (ESP32-C6) | Where to find |
| ---- | ------------------- | ------------- |
| Magic value | `0x2CE0806F` | [ESP-IDF `targets/` chip definitions](https://github.com/espressif/esp-idf/tree/master/components/efuse) — look for `esp_chip_info` or `CHIP_DETECT_MAGIC_VALUE` in each chip's target folder |
| Magic register address | `0x40001000` | Same for all current variants |
| eFuse MAC word0 address | `0x600B0844` | [Technical Reference Manuals](https://www.espressif.com/en/support/documents/technical-documents) — eFuse chapter, "MAC Address" register block. For non-ESP32 chips this is also `EFUSE_BLOCK1_ADDR` (`EFUSE_BASE + 0x44`), used by `ReadBlock1Word(n)` for revision/package/feature detection |
| eFuse MAC word1 address | `0x600B0848` | Same TRM eFuse chapter as above |
| eFuse base address | `0x600B0800` | TRM eFuse chapter — `EFUSE_RD_REG_BASE` (start of eFuse read registers). For original ESP32 only, used by `ReadEfuse(n)` to read word at base + 4*n. For newer chips, BLOCK1 reads go through `efuseMacWord0Addr` instead |
| SPI controller base | `0x60003000` | [Technical Reference Manuals](https://www.espressif.com/en/support/documents/technical-documents) — SPI (GP-SPI) chapter, register summary table |
| SPI register offsets | USR=0x18, W0=0x58, USR1=0x1C, USR2=0x20, MOSI_DLEN=0x24, MISO_DLEN=0x28 | Same TRM SPI chapter — register offset table for SPI2 (or the chip's default SPI peripheral) |
| Crystal clock divider | `1` | esptool.py chip class — `XTAL_CLK_DIVIDER` constant (1 for all ESP32 variants, 2 for ESP8266) |
| Bootloader load address | `0x0` | [ESP-IDF bootloader component](https://github.com/espressif/esp-idf/tree/master/components/bootloader) — `Kconfig.projbuild` for `BOOTLOADER_OFFSET_IN_FLASH` per target |
| Features | WiFi 6, BLE 5, 802.15.4 | [Espressif product pages](https://www.espressif.com/en/products/socs) — chip datasheet "Features" section |
| PSRAM support | Yes / No | Same datasheet — check for PSRAM/SPIRAM mentions |
| Chip revision scheme | e.g. "revision v0.0", "revision v0.1" | [ESP-IDF chip info](https://github.com/espressif/esp-idf/tree/master/components/efuse) — `esp_chip_info.h` and each chip's `include/esp_efuse_chip.h` for wafer version fields |
| Package version mapping | `{0: "QFN56", 1: "LGA56"}` | [esptool.py chip class](https://github.com/espressif/esptool/tree/master/esptool/targets) — `get_chip_description()` for the package-to-name mapping and `get_pkg_version()` for the eFuse bit positions. For RISC-V chips using common BLOCK1 layout, `ReadBlock1PkgVersion()` and `ReadRiscVChipRevision()` can be reused directly |
| Embedded flash/PSRAM info | Capacity + vendor eFuse bits | esptool.py chip class — `get_chip_features()`, `get_flash_cap()`, `get_psram_cap()` for eFuse bit positions within BLOCK1 |
| Flash frequency encoding | `{80: 0x0F, 40: 0x00, ...}` | esptool.py chip class — `FLASH_FREQUENCY` dict. **Only needed if the chip uses non-standard values.** All current ESP32 variants share the same encoding: `{80MHz: 0x0F, 40MHz: 0x00, 26MHz: 0x01, 20MHz: 0x02}`. If a new chip differs, update `FlashFreqEncoding` in `Esp32FlashController.cs` |

---

## Required Changes

### 1. Chip Configuration — `Esp32ChipConfigs.cs`

**Path**: [`nanoFirmwareFlasher.Library/Esp32Serial/Esp32ChipConfigs.cs`](nanoFirmwareFlasher.Library/Esp32Serial/Esp32ChipConfigs.cs)

Add a new static config property after the last existing one ([line 245](nanoFirmwareFlasher.Library/Esp32Serial/Esp32ChipConfigs.cs#L245)), following the existing pattern:

```csharp
// ======================== ESP32-NEW ========================
internal static Esp32ChipConfig ESP32_NEW { get; } = new(
    name: "ESP32-NEW",
    chipType: "esp32new",
    magicValue: 0xDEADBEEF,
    magicRegAddr: 0x40001000,
    efuseMacWord0Addr: 0x........,
    efuseMacWord1Addr: 0x........,
    spiRegBase: 0x........,
    spiUsrOffset: 0x18,
    spiW0Offset: 0x58,
    spiUsr1Offset: 0x1C,
    spiUsr2Offset: 0x20,
    spiMosiDlenOffset: 0x24,
    spiMisoDlenOffset: 0x28,
    efuseBaseAddr: 0x........,
    xtalClkDivider: 1,
    bootloaderAddress: 0x0,
    flashWriteBlockSize: 0x4000,
    usesOldSpiRegisters: false
);
```

Then register it in three places within the same file:

- [`s_configsByMagic` dictionary (line 253)](nanoFirmwareFlasher.Library/Esp32Serial/Esp32ChipConfigs.cs#L253) — add `{ ESP32_NEW.MagicValue, ESP32_NEW }`
- [`s_configsByType` dictionary (line 267)](nanoFirmwareFlasher.Library/Esp32Serial/Esp32ChipConfigs.cs#L267) — add `{ "esp32new", ESP32_NEW }`
- [`All` property iterator (line 300)](nanoFirmwareFlasher.Library/Esp32Serial/Esp32ChipConfigs.cs#L300) — add `yield return ESP32_NEW;`

> **Note**: All supported chips use `usesOldSpiRegisters: false`. This flag controls whether SPI data lengths are set via old-style USR1 register bit fields (ESP8266 only, not supported) or via dedicated MOSI_DLEN/MISO_DLEN registers configured through `spiMosiDlenOffset`/`spiMisoDlenOffset` (all ESP32 variants). The `efuseBaseAddr` is the chip's `EFUSE_RD_REG_BASE` used by the `ReadEfuse(n)` helper to read eFuse word n at `efuseBaseAddr + 4*n`. The `xtalClkDivider` is `1` for all current ESP32 variants (would be `2` for ESP8266).

---

### 2. Chip Detection — `Esp32ChipDetector.cs`

**Path**: [`nanoFirmwareFlasher.Library/Esp32Serial/Esp32ChipDetector.cs`](nanoFirmwareFlasher.Library/Esp32Serial/Esp32ChipDetector.cs)

Three changes are needed:

#### 2a. Chip description method

`ReadChipName()` dispatches to a per-chip method that reads eFuse registers to produce a string like `"ESP32-S3 (QFN56) (revision v0.2)"`. The revision substring is critical — `Esp32Operations.cs` uses `ChipName.Contains("revision vX.Y")` to validate supported revisions.

Add a new case to the `ReadChipName()` switch and implement the method:

```csharp
// In the ReadChipName() switch:
"esp32new" => ReadEsp32NewChipDescription(),
```

For RISC-V family chips (C-series, H-series) that share the common BLOCK1 eFuse layout, reuse the existing helpers:

```csharp
private string ReadEsp32NewChipDescription()
{
    int pkgVersion = ReadBlock1PkgVersion();                // BLOCK1 word3[23:21]
    var (majorRev, minorRev) = ReadRiscVChipRevision();     // BLOCK1 word3/word5

    string chipName = pkgVersion switch
    {
        0 => "ESP32-NEW (QFN40)",
        1 => "ESP32-NEW-V2 (QFN32)",
        _ => "Unknown ESP32-NEW"
    };

    return $"{chipName} (revision v{majorRev}.{minorRev})";
}
```

> **Helpers available**: `ReadBlock1PkgVersion()` reads package version from BLOCK1 word3 bits [23:21]. `ReadRiscVChipRevision()` reads major/minor revision from BLOCK1 words 3 & 5. These work for all non-ESP32 chips that use the common BLOCK1 layout (S3, C3, C6, H2). For Xtensa chips with a different layout, write a standalone method (see `ReadEsp32S3ChipDescription()` for S3's eco0 workaround).

> **Important**: The `efuseMacWord0Addr` config field doubles as `EFUSE_BLOCK1_ADDR` for non-ESP32 chips. The `ReadBlock1Word(n)` helper reads from `efuseMacWord0Addr + 4*n`. Set this address to the chip's `EFUSE_BASE + 0x44` (BLOCK1 start), which is also where MAC word 0 lives.

#### 2b. Features method

Add a case to the `ReadFeatures()` switch. For simple chips, a static string suffices. For chips with embedded flash/PSRAM variants, implement a method that reads BLOCK1 eFuses (see `ReadEsp32C3Features()` or `ReadEsp32S3Features()` as examples):

```csharp
// Static (no embedded flash/PSRAM variants):
"esp32new" => "Wi-Fi 6, BLE 5, 802.15.4",

// Or dynamic (with embedded flash/PSRAM):
"esp32new" => ReadEsp32NewFeatures(),
```

#### 2c. MAC and crystal frequency

If the chip uses a different UART clock register address for crystal frequency detection, update the `ReadCrystalFrequency()` method. The default address `0x60000014` works for most non-ESP32 chips.

If the chip has a non-standard MAC byte order (different from the default `struct.pack(">II", mac1, mac0)[2:]` layout used by S3/C3/C6/H2), add handling in `ReadMacAddress()`.

---

### 3. EspTool Integration — `EspTool.cs`

**Path**: [`nanoFirmwareFlasher.Library/EspTool.cs`](nanoFirmwareFlasher.Library/EspTool.cs)

Two changes:

1. Add to the [`displayChipType` switch (line 348)](nanoFirmwareFlasher.Library/EspTool.cs#L348) in `GetDeviceDetails()`:

    ```csharp
    "esp32new" => "ESP32-NEW",
    ```

2. Add PSRAM handling in `GetDeviceDetails()` at the [no-PSRAM condition (line 302)](nanoFirmwareFlasher.Library/EspTool.cs#L302):

    - If the chip has **no PSRAM**: add `|| _chipType == "esp32new"` to the existing condition alongside `esp32c3`, `esp32c6`, `esp32h2`.
    - If the chip **has PSRAM**: add detection logic similar to ESP32-S3 (feature string check) or ESP32 (force-flash test app).

---

### 4. Operations — `Esp32Operations.cs`

**Path**: [`nanoFirmwareFlasher.Library/Esp32Operations.cs`](nanoFirmwareFlasher.Library/Esp32Operations.cs)

Two changes:

1. Add to the [supported chip type validation check (line 137)](nanoFirmwareFlasher.Library/Esp32Operations.cs#L137):

    ```csharp
    esp32Device.ChipType != "ESP32-NEW" &&
    ```

2. Add a revision-to-target-name mapping block after the [last existing one (line 374)](nanoFirmwareFlasher.Library/Esp32Operations.cs#L374):

    ```csharp
    else if (esp32Device.ChipType == "ESP32-NEW")
    {
        string revisionSuffix;

        if (esp32Device.ChipName.Contains("revision v0.0")
            || esp32Device.ChipName.Contains("revision v0.1"))
        {
            revisionSuffix = "";
        }
        else
        {
            OutputWriter.ForegroundColor = ConsoleColor.Red;
            OutputWriter.WriteLine("");
            OutputWriter.WriteLine($"Unsupported ESP32_NEW revision.");
            OutputWriter.WriteLine("");
            OutputWriter.ForegroundColor = ConsoleColor.White;
            return ExitCodes.E9000;
        }

        targetName = $"ESP32_NEW{revisionSuffix}";
    }
    ```

---

### 5. Firmware Addresses — `Esp32Firmware.cs`

**Path**: [`nanoFirmwareFlasher.Library/Esp32Firmware.cs`](nanoFirmwareFlasher.Library/Esp32Firmware.cs)

Add the chip to the [bootloader address selection logic (line 81)](nanoFirmwareFlasher.Library/Esp32Firmware.cs#L81) if its address differs from the default `0x1000`:

```csharp
// Bootloader at 0x0
if (deviceInfo.ChipType == "ESP32-C3"
    || deviceInfo.ChipType == "ESP32-C6"
    || deviceInfo.ChipType == "ESP32-H2"
    || deviceInfo.ChipType == "ESP32-S3"
    || deviceInfo.ChipType == "ESP32-NEW")  // ← add here
{
    BootLoaderAddress = 0;
}

// Or if the address is unique (like ESP32-P4 at 0x2000):
if (deviceInfo.ChipType == "ESP32-NEW")
{
    BootLoaderAddress = 0x2000;
}
```

---

### 6. Stub Loader — `update-stubs.ps1`

**Path**: [`update-stubs.ps1`](update-stubs.ps1)

Add the chip type to the [`$chipTypes` array (line 29)](update-stubs.ps1#L29):

```powershell
$chipTypes = @("esp32", "esp32s2", "esp32s3", "esp32c3", "esp32c6", "esp32h2", "esp32new")
```

Then run the script to download the stub image. The stub JSON file will be saved to `nanoFirmwareFlasher.Library/Esp32Serial/StubImages/stub_esp32new.json` and is automatically included as an embedded resource via the existing wildcard in the [`.csproj` (line 88)](nanoFirmwareFlasher.Library/nanoFirmwareFlasher.Library.csproj#L88):

```xml
<EmbeddedResource Include="Esp32Serial\StubImages\*.json" />
```

> **Note**: If no stub is available yet from the official [esp-flasher-stub](https://github.com/espressif/esp-flasher-stub) repository, flashing will still work in ROM-only mode at 115200 baud. Stub support can be added later.

---

## Conditional Changes (only if shipping test bootloaders)

Test bootloaders are only needed for force PSRAM detection (flashing a test app, rebooting, reading serial output). If the chip doesn't have PSRAM or uses feature-string detection, skip this section.

### 7. Bootloader Files

Create the directory and add pre-built binaries from ESP-IDF:

```
lib/esp32newbootloader/
├── bootloader.bin
├── partitions_Xmb.bin    (X = default flash size: 2 for ESP32, 4 for others)
└── test_startup.bin
```

### 8. Project File — `nanoFirmwareFlasher.Library.csproj`

**Path**: [`nanoFirmwareFlasher.Library/nanoFirmwareFlasher.Library.csproj`](nanoFirmwareFlasher.Library/nanoFirmwareFlasher.Library.csproj)

Add linking entries after the [existing bootloader `<None Include>` items (lines 93-97)](nanoFirmwareFlasher.Library/nanoFirmwareFlasher.Library.csproj#L93-L97):

```xml
<None Include="..\lib\esp32newbootloader\**" 
      Link="esp32newbootloader\%(RecursiveDir)%(Filename)%(Extension)" />
```

And add `CopyToOutputDirectory=Always` entries for each `.bin` file after the [existing ones (lines 132-156)](nanoFirmwareFlasher.Library/nanoFirmwareFlasher.Library.csproj#L132-L156).

### 9. NuGet Packaging — `nugetcontent.targets`

**Path**: [`nanoFirmwareFlasher.Library/nugetcontent.targets`](nanoFirmwareFlasher.Library/nugetcontent.targets)

Add after the [existing bootloader `<Content>` items (lines 3-9)](nanoFirmwareFlasher.Library/nugetcontent.targets#L3-L9):

```xml
<Content Include="..\lib\esp32newbootloader\**">
    <Pack>true</Pack>
    <PackagePath>tools\$(PackageTfmSubFolder)any\esp32newbootloader</PackagePath>
</Content>
```

---

## No Changes Needed

These files are generic/data-driven and require no modification:

| File | Reason |
| ---- | ------ |
| [`Esp32DeviceInfo.cs`](nanoFirmwareFlasher.Library/Esp32DeviceInfo.cs) | Pure data class — accepts any chip type string |
| [`FirmwarePackageFactory.cs`](nanoFirmwareFlasher.Library/FirmwarePackageFactory.cs) | Dispatches by platform ("esp32"), not chip variant |
| [`Options.cs`](nanoFirmwareFlasher.Tool/Options.cs) | `--target` accepts any string; validated at CloudSmith API level |
| [`Esp32Manager.cs`](nanoFirmwareFlasher.Tool/Esp32Manager.cs) | Generic orchestrator, delegates to `Esp32Operations` |
| [`CloudSmithPackageDetails.cs`](nanoFirmwareFlasher.Library/CloudSmithPackageDetails.cs) | Target discovery is API-driven |

---

## Baud Rate Reference

| Phase | Baud Rate | Notes |
| ----- | --------- | ----- |
| Initial connection & sync | 115,200 | ROM bootloader fixed rate |
| After stub upload | 1,500,000 (default) | Configurable via `--baud` |
| PSRAM detection writes | 115,200 | Forced standard rate |

The connection always starts at 115,200. After the stub is uploaded, the baud rate is changed to the user-requested value (default 1,500,000) for flash operations. If stub upload fails, all operations continue at 115,200 in ROM-only mode.

---

## Flash Write Pipeline — How It Works

When `EspTool.WriteFlash()` is called, the following steps happen before and during flash writes. These are modelled after esptool.py's `write_flash` and `_update_image_flash_params` functions. **No changes are needed for a new chip** unless it uses non-standard flash frequency encoding (see Prerequisites table).

### Flash Parameter Configuration (`SPI_SET_PARAMS`)

Before any writes, `SendSpiSetParams(flashSizeBytes)` sends command `0x0B` to configure flash geometry:

```
[fl_id:4=0][total_size:4][block_size:4=64KB][sector_size:4=4KB][page_size:4=256][status_mask:4=0xFFFF]
```

This tells the stub/ROM the flash chip's size and geometry so erase and write operations calculate sector boundaries correctly. Without this, chips with >2 MB flash may fail to write past the 2 MB boundary.

### Bootloader Image Header Patching

`PatchBootloaderImageHeader()` modifies bytes 2–3 of the bootloader `.bin` file before writing it to flash. This is **critical** — the ESP ROM reads these bytes at boot to configure the SPI flash interface.

**Image header format** (first 4 bytes):

| Byte | Content |
| ---- | ------- |
| 0 | Magic (`0xE9`) |
| 1 | Segment count |
| 2 | Flash mode: `QIO=0, QOUT=1, DIO=2, DOUT=3` |
| 3 | Flash size (high nibble) \| Flash frequency (low nibble) |

**Flash size encoding** (high nibble of byte 3):

| Size | Value |
| ---- | ----- |
| 1 MB | `0x00` |
| 2 MB | `0x10` |
| 4 MB | `0x20` |
| 8 MB | `0x30` |
| 16 MB | `0x40` |
| 32 MB | `0x50` |
| 64 MB | `0x60` |
| 128 MB | `0x70` |

**Flash frequency encoding** (low nibble of byte 3):

| Freq | Value |
| ---- | ----- |
| 80 MHz | `0x0F` |
| 40 MHz | `0x00` |
| 26 MHz | `0x01` |
| 20 MHz | `0x02` |

**Example**: DIO mode + 16 MB flash + 80 MHz → byte 2 = `0x02`, byte 3 = `0x40 | 0x0F` = `0x4F` → "Flash params set to 0x024F".

Patching only occurs when:
- The write address matches `config.BootloaderAddress` (0x1000 for ESP32, 0x0 for S3/C3/C6/H2)
- The image starts with magic byte `0xE9`

The flash mode and frequency come from CLI options (`--flashmode dio --flashfreq 40`), and the flash size is auto-detected from the JEDEC flash ID during `GetDeviceDetails()`.

### SHA-256 Digest Recalculation

If byte 23 of the image is `1` (extended header field `append_digest`), the bootloader has a SHA-256 digest appended after the segment data. After patching bytes 2–3, `RecalculateImageSha256()` recomputes and rewrites the 32-byte digest so the ROM's integrity check passes.

The image layout for SHA calculation:
```
[8-byte header][16-byte extended header][segments...][checksum pad to 16 bytes][32-byte SHA-256]
```

The SHA-256 covers everything from byte 0 up to (but not including) the digest itself.

### Code Locations

| Step | Method | File |
| ---- | ------ | ---- |
| SPI_SET_PARAMS | `SendSpiSetParams()` | [`Esp32FlashController.cs`](nanoFirmwareFlasher.Library/Esp32Serial/Esp32FlashController.cs) |
| Header patching | `PatchBootloaderImageHeader()` | [`Esp32FlashController.cs`](nanoFirmwareFlasher.Library/Esp32Serial/Esp32FlashController.cs) |
| SHA-256 recalc | `RecalculateImageSha256()` | [`Esp32FlashController.cs`](nanoFirmwareFlasher.Library/Esp32Serial/Esp32FlashController.cs) |
| Orchestration | `WriteFlash()` | [`EspTool.cs`](nanoFirmwareFlasher.Library/EspTool.cs) |
| Encoding tables | `FlashModeEncoding`, `FlashFreqEncoding`, `FlashSizeEncoding` | [`Esp32FlashController.cs`](nanoFirmwareFlasher.Library/Esp32Serial/Esp32FlashController.cs) |

---

## Verification

After making the changes:

1. **Build**: `dotnet build nanoFirmwareFlasher.sln`
2. **Run unit tests**: `dotnet test nanoFirmwareFlasher.sln`
3. **Hardware test** (with a board connected):
   ```
   nanoff --target ESP32_NEW --serialport COMx --getdetails --verbosity diagnostic
   nanoff --target ESP32_NEW --serialport COMx --update --verbosity diagnostic
   ```
