// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using nanoFramework.Tools.Debugger.NFDevice;
using nanoFramework.Tools.FirmwareFlasher.Mcuboot;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Manages MCUboot firmware update operations using the SMP serial transport (mcumgr protocol).
    /// Handles the image list query and the full upload lifecycle:
    /// sign (if key provided) → upload → reset.
    /// </summary>
    public class McubootManager : IManager
    {
        private readonly Options _options;
        private readonly VerbosityLevel _verbosity;
        private const int AccessSerialPortTimeout = 3000;
        // MCUboot serial transport uses 115200 by default; different from flash baud rate
        private const int SmpBaudRate = 115200;

        /// <summary>
        /// Creates a new <see cref="McubootManager"/>.
        /// </summary>
        public McubootManager(Options options, VerbosityLevel verbosity)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _verbosity = verbosity;
        }

        /// <inheritdoc/>
        public async Task<ExitCodes> ProcessAsync()
        {
            if (string.IsNullOrEmpty(_options.SerialPort))
            {
                return ExitCodes.E6001;
            }

            ExitCodes result;
            using (var access = GlobalExclusiveDeviceAccess.TryGet(_options.SerialPort, AccessSerialPortTimeout))
            {
                if (access is null)
                {
                    return ExitCodes.E6002;
                }

                result = await DoProcessAsync();
            }

            return result;
        }

        private async Task<ExitCodes> DoProcessAsync()
        {
            if (_options.ListMcuImages)
            {
                return await RunWithClientAsync(ListImagesAsync);
            }

            return await UploadFlowAsync();
        }

        /// <summary>
        /// Opens an SMP client, negotiates the device's MCUmgr buffer size, then calls <paramref name="operation"/>.
        /// </summary>
        private async Task<ExitCodes> RunWithClientAsync(Func<McumgrClient, Task<ExitCodes>> operation)
        {
            using (var client = new McumgrClient(
                _options.SerialPort,
                SmpBaudRate,
                verbosity: _verbosity))
            {
                try
                {
                    client.Open();
                }
                catch (Exception ex)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Red;
                    OutputWriter.WriteLine($"Failed to open SMP connection on {_options.SerialPort}: {ex.Message}");
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                    return ExitCodes.E10005;
                }

                await NegotiateTransportParametersAsync(client);

                return await operation(client);
            }
        }

        /// <summary>
        /// Best-effort query of the device's MCUmgr parameters so uploads use a chunk size the
        /// device can actually accept. MCUboot serial recovery built without
        /// MCUBOOT_BOOT_MGMT_MCUMGR_PARAMS replies with rc=ENOTSUP (no buffer size is reported);
        /// in that case, or on timeout, the conservative default chunk size is kept.
        /// </summary>
        private async Task NegotiateTransportParametersAsync(McumgrClient client)
        {
            try
            {
                McumgrParameters parameters = await client.GetParametersAsync();

                if (_verbosity >= VerbosityLevel.Detailed && parameters.BufSize > 0)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                    OutputWriter.WriteLine($"Device MCUmgr buffers: size={parameters.BufSize}, count={parameters.BufCount}; upload chunk set to {client.ChunkSize} bytes.");
                }
            }
            catch (McumgrTimeoutException)
            {
                // no reply to the command; keep the default chunk size
            }
            catch (McumgrProtocolException)
            {
                // device rejected the command; keep the default chunk size
            }
        }

        private async Task<ExitCodes> ListImagesAsync(McumgrClient client)
        {
            List<McumgrImageInfo> images;

            try
            {
                images = await client.GetImageListAsync();
            }
            catch (McumgrProtocolException ex)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Red;
                OutputWriter.WriteLine($"Failed to list images: {ex.Message}");
                OutputWriter.ForegroundColor = ConsoleColor.White;
                return ExitCodes.E10009;
            }
            catch (McumgrTimeoutException ex)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Red;
                OutputWriter.WriteLine($"Image list timed out: {ex.Message}");
                OutputWriter.ForegroundColor = ConsoleColor.White;
                return ExitCodes.E10007;
            }

            if (images.Count == 0)
            {
                OutputWriter.WriteLine("No images reported by device.");
                return ExitCodes.OK;
            }

            foreach (McumgrImageInfo img in images)
            {
                string hash = img.Hash != null
                    ? BitConverter.ToString(img.Hash).Replace("-", "").ToLowerInvariant()
                    : "(none)";

                IEnumerable<string> flagParts = new[]
                {
                    img.Active    ? "active"    : null,
                    img.Confirmed ? "confirmed" : null,
                    img.Pending   ? "pending"   : null,
                    img.Bootable  ? "bootable"  : null,
                }.Where(f => f != null);

                string flags = string.Join(", ", flagParts);

                OutputWriter.ForegroundColor = ConsoleColor.White;
                OutputWriter.WriteLine($"Image {img.Image} Slot {img.Slot}  version={img.Version}  hash={hash}  [{flags}]");
            }

            OutputWriter.ForegroundColor = ConsoleColor.White;
            return ExitCodes.OK;
        }

        private async Task<ExitCodes> UploadFlowAsync()
        {
            bool imageIsClr = !string.IsNullOrEmpty(_options.ClrFile);
            bool imageIsDeploy = !string.IsNullOrEmpty(_options.DeploymentImage);

            if (!imageIsClr && !imageIsDeploy)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Red;
                OutputWriter.WriteLine("No image specified. Use 'flash ... image <file> mcuboot' to upload a CLR image (MCUboot Image 0) or 'deploy ... image <file> mcuboot' to upload a deployment image (MCUboot Image 1).");
                OutputWriter.ForegroundColor = ConsoleColor.White;
                return ExitCodes.E10003;
            }

            if (imageIsClr && imageIsDeploy)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Red;
                OutputWriter.WriteLine("Specify either a CLR image (Image 0) or a deployment image (Image 1), not both. Use 'flash' and 'deploy' separately for CLR and deployment updates.");
                OutputWriter.ForegroundColor = ConsoleColor.White;
                return ExitCodes.E9000;
            }

            string imagePath = imageIsClr ? _options.ClrFile : _options.DeploymentImage;
            int imageIndex = GetImageIndex(_options);

            StringBuilder imageLabel = new StringBuilder();
            if (imageIsClr)
            {
                imageLabel.Append("CLR image");
            }
            else
            {
                imageLabel.Append("deployment image");
            }

            if(_options.SecondarySlot)
            {
                imageLabel.Append(" @ secondary slot");
            }

            if (!File.Exists(imagePath))
            {
                OutputWriter.ForegroundColor = ConsoleColor.Red;
                OutputWriter.WriteLine($"Image file not found: {imagePath}");
                OutputWriter.ForegroundColor = ConsoleColor.White;
                return ExitCodes.E10003;
            }

            if (!string.IsNullOrEmpty(_options.SigningKeyPath))
            {
                ExitCodes signResult = SignImage(ref imagePath);
                if (signResult != ExitCodes.OK)
                {
                    return signResult;
                }
            }

            byte[] imageBytes;
            try
            {
                imageBytes = File.ReadAllBytes(imagePath);
            }
            catch (Exception ex)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Red;
                OutputWriter.WriteLine($"Failed to read image file: {ex.Message}");
                OutputWriter.ForegroundColor = ConsoleColor.White;
                return ExitCodes.E10003;
            }

            return await RunWithClientAsync(client => UploadImageToClientAsync(
                client,
                imageBytes,
                imageIndex,
                imageLabel.ToString()));
        }

        private async Task<ExitCodes> UploadImageToClientAsync(McumgrClient client, byte[] imageBytes, int imageIndex, string imageLabel)
        {
            string uploadingPrefix = $"Uploading {imageLabel}...";
            bool normal = _verbosity >= VerbosityLevel.Normal;

            // Lines on screen while uploading:
            //   Uploading <label>...               (finalized with OK once upload completes)
            //     Preparing storage at device...OK  (the first frame erases the slot)
            //     NN% (sent / total bytes)          (updated in place via \r)
            // When the upload completes the cursor rolls back up to the "Uploading..." line
            // to write OK, leaving a single clean line.
            bool progressStarted = false;

            if (normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.White;
                OutputWriter.WriteLine(uploadingPrefix);
                OutputWriter.Write("  Preparing storage at device...");
            }

            // Synchronous reporter so progress callbacks run inline on the upload thread; this
            // keeps the output ordered (Progress<T> marshals callbacks and can fire after completion).
            var uploadProgress = new SynchronousProgress<McumgrUploadProgress>(p =>
            {
                if (!normal)
                {
                    return;
                }

                if (!progressStarted)
                {
                    // first chunk accepted: storage is prepared
                    progressStarted = true;
                    OutputWriter.ForegroundColor = ConsoleColor.Green;
                    OutputWriter.WriteLine("OK");
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                }

                OutputWriter.Write($"\r  {p.PercentComplete,3}% ({p.BytesSent:N0} / {p.TotalBytes:N0} bytes)");
            });

            try
            {
                await client.UploadImageAsync(imageBytes, imageIndex, uploadProgress, default);

                if (normal)
                {
                    // roll back over the progress (and preparing) lines, then finalize the upload line
                    RewindStatusLines(progressStarted ? 2 : 1);
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                    OutputWriter.Write(uploadingPrefix);
                    OutputWriter.ForegroundColor = ConsoleColor.Green;
                    OutputWriter.WriteLine("OK");
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                }
            }
            catch (McumgrProtocolException ex)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Red;
                OutputWriter.WriteLine($"\r{uploadingPrefix}FAILED: {ex.Message}");
                OutputWriter.ForegroundColor = ConsoleColor.White;
                return ExitCodes.E10010;
            }
            catch (McumgrTimeoutException ex)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Red;
                OutputWriter.WriteLine($"\r{uploadingPrefix}TIMED OUT: {ex.Message}");
                OutputWriter.ForegroundColor = ConsoleColor.White;
                return ExitCodes.E10007;
            }

            // An image landing in a secondary slot carries no pending marker in its trailer.
            // Need to mark it explicitly.
            if (_options.SecondarySlot)
            {
                ExitCodes stateResult = await MarkImageForSwapAsync(client, imageBytes);

                if (stateResult != ExitCodes.OK)
                {
                    return stateResult;
                }
            }

            if (_verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.White;
                OutputWriter.Write("Resetting device...");
            }

            try
            {
                await client.ResetAsync();
            }
            catch (Exception)
            {
                // device may reset before sending a response; this is expected
            }

            if (_verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Green;
                OutputWriter.WriteLine("OK");
                OutputWriter.ForegroundColor = ConsoleColor.White;
            }

            return ExitCodes.OK;
        }

        /// <summary>
        /// Marks a freshly uploaded secondary-slot image as pending, so MCUboot swaps it in on the
        /// next reset. Without this the image sits in the slot with no trailer marker and MCUboot
        /// reports swap type "none".
        /// </summary>
        private async Task<ExitCodes> MarkImageForSwapAsync(McumgrClient client, byte[] imageBytes)
        {
            bool normal = _verbosity >= VerbosityLevel.Normal;
            const string prefix = "Marking image as pending...";

            if (!McubootImageManager.TryGetImageHash(imageBytes, out byte[] hash))
            {
                OutputWriter.ForegroundColor = ConsoleColor.Red;
                OutputWriter.WriteLine(
                    "Could not read the SHA-256 hash from the image, so it cannot be marked for swap. "
                    + "Only signed MCUboot images can be placed in a secondary slot.");
                OutputWriter.ForegroundColor = ConsoleColor.White;

                return ExitCodes.E10022;
            }

            if (normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.White;
                OutputWriter.Write(prefix);
            }

            try
            {
                // always "test", never "confirm": making the swap permanent here would put the new
                // image beyond reach of a rollback before it has booted even once. Confirmation is
                // nanoCLR's job, once its startup health checks have passed.
                await client.SetImageStateAsync(hash, confirm: false, default);
            }
            catch (McumgrProtocolException ex)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Red;
                OutputWriter.WriteLine($"\r{prefix}FAILED: {ex.Message}");
                OutputWriter.ForegroundColor = ConsoleColor.White;

                return ExitCodes.E10021;
            }
            catch (McumgrTimeoutException ex)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Red;
                OutputWriter.WriteLine($"\r{prefix}TIMED OUT: {ex.Message}");
                OutputWriter.ForegroundColor = ConsoleColor.White;

                return ExitCodes.E10007;
            }

            if (normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Green;
                OutputWriter.WriteLine("OK");
                OutputWriter.ForegroundColor = ConsoleColor.White;
            }

            return ExitCodes.OK;
        }

        /// <summary>
        /// Clears the current line and <paramref name="linesAbove"/> lines above it, then leaves
        /// the cursor at the start of the topmost cleared line so it can be rewritten. Falls back
        /// to a simple newline when the output is redirected and cannot be repositioned.
        /// </summary>
        private static void RewindStatusLines(int linesAbove)
        {
            if (Console.IsOutputRedirected)
            {
                OutputWriter.WriteLine();
                return;
            }

            try
            {
                int bottom = Console.CursorTop;
                int width = Console.WindowWidth;

                for (int i = 0; i <= linesAbove; i++)
                {
                    int line = bottom - i;
                    if (line < 0)
                    {
                        break;
                    }

                    Console.SetCursorPosition(0, line);
                    Console.Write(new string(' ', width));
                }

                Console.SetCursorPosition(0, Math.Max(0, bottom - linesAbove));
            }
            catch
            {
                // cursor repositioning not supported in this environment; continue on a new line
                OutputWriter.WriteLine();
            }
        }

        /// <summary>
        /// An <see cref="IProgress{T}"/> that invokes its handler synchronously on the calling
        /// thread, preserving output ordering (unlike <see cref="Progress{T}"/>, which marshals).
        /// </summary>
        private sealed class SynchronousProgress<T> : IProgress<T>
        {
            private readonly Action<T> _handler;

            public SynchronousProgress(Action<T> handler) => _handler = handler;

            public void Report(T value) => _handler(value);
        }

        private ExitCodes SignImage(ref string imagePath)
        {
            var mgr = new McubootImageManager(
                _options.SigningKeyPath,
                _options.McubootSlotSize ?? 0x100000,
                _options.McubootHeaderSize ?? 0x200,
                _options.McubootWriteAlignment ?? 4);
            mgr.Verbosity = _verbosity;

            string signedPath = Path.Combine(
                Path.GetDirectoryName(imagePath) ?? string.Empty,
                Path.GetFileNameWithoutExtension(imagePath) + "-signed.bin");

            if (_verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.WriteLine($"Signing image with key: {_options.SigningKeyPath}");
            }

            ExitCodes result = mgr.SignImage(imagePath, signedPath, _options.FwVersion ?? "0.0.0.0");
            if (result == ExitCodes.OK)
            {
                imagePath = signedPath;
            }

            return result;
        }

        /// <summary>
        /// Get the image index to use for upload based on options.
        /// </summary>
        /// <param name="options"></param>
        /// <returns></returns>
        /// <remarks>
        /// This index is hardcoded based on the implementation of flash_area_id_from_direct_image() in the nanoCLR.
        /// Match the index to MCUboot\common\flash_map_extend.c
        /// </remarks>

        private int GetImageIndex(Options options)
        {
            // Direct-image id carried in the SMP upload "image" field. The numbering is fixed
            // by mcuboot core (bs_slot_info() advertises upload_image_id = image_index * 2 +
            // slot + 1 for every (image, slot) pair), not something the port or this tool gets
            // to choose - must match the mapping in flash_area_id_from_direct_image()
            // (MCUboot/common/flash_map_extend.c):
            //   CLR        (Image 0): primary = 1, secondary = 2
            //   deployment (Image 1): primary = 3, secondary = 4
            int directImageId;

            if (options.ClrFile != null)
            {
                // MCUboot Image 0 is for the CLR image
                directImageId = options.SecondarySlot ? 2 : 1;
            }
            else if (options.DeploymentImage != null)
            {
                // MCUboot Image 1 is for the deployment image
                directImageId = options.SecondarySlot ? 4 : 3;
            }
            else
            {
                throw new InvalidOperationException("No image specified in options.");
            }

            return directImageId;
        }
    }
}
