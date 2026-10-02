// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace nanoFramework.Tools.FirmwareFlasher.Mcuboot
{
    /// <summary>
    /// Manages MCUboot image signing and validation using imgtool.
    /// </summary>
    public class McubootImageManager
    {
        // MCUboot image header: first 32 bytes, little-endian multi-byte fields.
        // Offset 0-3:  magic (0x96f3b83d LE)
        // Offset 8-9:  hdr_size (uint16 LE)
        // Offset 12-15: img_size (uint32 LE)
        // Offset 20:   version.major (uint8)
        // Offset 21:   version.minor (uint8)
        // Offset 22-23: version.revision (uint16 LE)
        // Offset 24-27: version.build_num (uint32 LE)
        private const uint McubootMagic = 0x96f3b83d;
        private const int HeaderBytesNeeded = 32;

        // TLV area, which follows the payload at hdr_size + img_size:
        //   info header: magic (uint16 LE), total length including the header (uint16 LE)
        //   entries:     type (uint16 LE), length (uint16 LE), value
        private const ushort TlvInfoMagic = 0x6907;          // unprotected TLV area
        private const ushort TlvInfoMagicProtected = 0x6908; // protected TLV area
        private const ushort TlvSha256 = 0x10;
        private const int TlvHeaderSize = 4;
        private const int Sha256Size = 32;

        // Sentinel values returned by FindImgtool() to indicate Python-module invocation.
        private const string ImgtoolViaPython = "python";
        private const string ImgtoolViaPython3 = "python3";

        private readonly string _signingKeyPath;
        private readonly int _slotSize;
        private readonly int _headerSize;
        private readonly int _writeAlignment;
        private string _imgtoolPath; // null until first needed; set lazily

        /// <summary>Verbosity level for output messages.</summary>
        public VerbosityLevel Verbosity { get; set; }

        /// <summary>
        /// Initializes a new instance with the given signing parameters.
        /// </summary>
        /// <param name="signingKeyPath">Path to the PEM signing key.</param>
        /// <param name="slotSize">MCUboot slot size in bytes.</param>
        /// <param name="headerSize">MCUboot header size (default 0x200).</param>
        /// <param name="writeAlignment">Flash write alignment (default 4).</param>
        /// <param name="imgtoolPath">Custom path to imgtool; null for auto-detect.</param>
        public McubootImageManager(
            string signingKeyPath,
            int slotSize,
            int headerSize = 0x200,
            int writeAlignment = 4,
            string imgtoolPath = null)
        {
            _signingKeyPath = signingKeyPath ?? throw new ArgumentNullException(nameof(signingKeyPath));
            _slotSize = slotSize;
            _headerSize = headerSize;
            _writeAlignment = writeAlignment;

            // null means auto-detect on first use
            _imgtoolPath = imgtoolPath;
        }

        /// <summary>
        /// Signs a raw binary, producing a signed image suitable for MCUboot.
        /// </summary>
        /// <param name="inputBinPath">Path to the unsigned nanoCLR binary.</param>
        /// <param name="outputBinPath">Path for the signed output image.</param>
        /// <param name="version">Semantic version string (e.g., "1.12.0.45").</param>
        public ExitCodes SignImage(string inputBinPath, string outputBinPath, string version)
        {
            if (inputBinPath is null)
            {
                throw new ArgumentNullException(nameof(inputBinPath));
            }

            if (outputBinPath is null)
            {
                throw new ArgumentNullException(nameof(outputBinPath));
            }

            if (version is null)
            {
                throw new ArgumentNullException(nameof(version));
            }

            RequireImgtool();

            string imgtoolVersion = FormatImgtoolVersion(version);

            string args = $"sign"
                + $" --key \"{_signingKeyPath}\""
                + $" --align {_writeAlignment}"
                + $" --version {imgtoolVersion}"
                + $" --header-size {_headerSize}"
                + $" --pad-header"
                + $" --slot-size {_slotSize}"
                + $" \"{inputBinPath}\""
                + $" \"{outputBinPath}\"";

            var (exitCode, _, stderr) = RunImgtool(args);

            if (exitCode != 0)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Red;
                OutputWriter.WriteLine($"** ERROR: imgtool sign failed (exit {exitCode}): {stderr}");
                OutputWriter.ForegroundColor = ConsoleColor.White;

                return ExitCodes.E10002;
            }

            if (Verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Green;
                OutputWriter.WriteLine($"Signed image written to '{outputBinPath}'.");
                OutputWriter.ForegroundColor = ConsoleColor.White;
            }

            return ExitCodes.OK;
        }

        /// <summary>
        /// Validates a signed image: checks MCUboot header magic, version, fit within slot.
        /// </summary>
        /// <param name="signedImagePath">Path to the signed binary image.</param>
        /// <returns><see cref="McubootImageInfo"/> with parsed header details; <c>IsValid</c> is false on any failure.</returns>
        public McubootImageInfo ValidateImage(string signedImagePath)
        {
            if (signedImagePath is null)
            {
                throw new ArgumentNullException(nameof(signedImagePath));
            }

            var info = new McubootImageInfo();

            try
            {
                byte[] header = new byte[HeaderBytesNeeded];
                using FileStream fs = File.OpenRead(signedImagePath);
                int read = fs.Read(header, 0, header.Length);

                if (read < HeaderBytesNeeded)
                {
                    return info;
                }

                uint magic = BitConverter.ToUInt32(header, 0);
                info.HeaderMagic = magic;

                if (magic != McubootMagic)
                {
                    return info; // IsValid remains false
                }

                info.HeaderSize = BitConverter.ToUInt16(header, 8);
                info.ImageSize  = BitConverter.ToUInt32(header, 12);

                byte major       = header[20];
                byte minor       = header[21];
                ushort revision  = BitConverter.ToUInt16(header, 22);
                uint buildNum    = BitConverter.ToUInt32(header, 24);
                info.Version = $"{major}.{minor}.{revision}.{buildNum}";

                info.IsValid = (ulong)info.HeaderSize + info.ImageSize <= (ulong)_slotSize;
            }
            catch
            {
                info.IsValid = false;
            }

            return info;
        }

        /// <summary>
        /// Reads the SHA-256 image hash out of a signed MCUboot image.
        /// </summary>
        /// <remarks>
        /// This is the hash mcumgr identifies an image by, so it is what
        /// <see cref="McumgrClient.SetImageStateAsync"/> needs to mark an uploaded image
        /// pending. Reading it from the file avoids a round trip to query the device.
        /// </remarks>
        /// <param name="signedImageBytes">Complete signed image, header through TLV area.</param>
        /// <param name="hash">The 32-byte hash, or <see langword="null"/> if it was not found.</param>
        /// <returns><see langword="true"/> if a SHA-256 TLV was present.</returns>
        public static bool TryGetImageHash(byte[] signedImageBytes, out byte[] hash)
        {
            hash = null;

            if (signedImageBytes is null || signedImageBytes.Length < HeaderBytesNeeded)
            {
                return false;
            }

            if (BitConverter.ToUInt32(signedImageBytes, 0) != McubootMagic)
            {
                return false;
            }

            ushort headerSize = BitConverter.ToUInt16(signedImageBytes, 8);
            uint imageSize = BitConverter.ToUInt32(signedImageBytes, 12);

            long offset = (long)headerSize + imageSize;

            // Walk the TLV areas: a protected area, if present, precedes the unprotected one.
            while (offset + TlvHeaderSize <= signedImageBytes.Length)
            {
                ushort infoMagic = BitConverter.ToUInt16(signedImageBytes, (int)offset);
                ushort infoLength = BitConverter.ToUInt16(signedImageBytes, (int)offset + 2);

                if (infoMagic != TlvInfoMagic && infoMagic != TlvInfoMagicProtected)
                {
                    return false;
                }

                if (infoLength < TlvHeaderSize || offset + infoLength > signedImageBytes.Length)
                {
                    return false;
                }

                long areaEnd = offset + infoLength;
                long entry = offset + TlvHeaderSize;

                while (entry + TlvHeaderSize <= areaEnd)
                {
                    ushort type = BitConverter.ToUInt16(signedImageBytes, (int)entry);
                    ushort length = BitConverter.ToUInt16(signedImageBytes, (int)entry + 2);
                    long value = entry + TlvHeaderSize;

                    if (value + length > areaEnd)
                    {
                        return false;
                    }

                    if (type == TlvSha256 && length == Sha256Size)
                    {
                        hash = new byte[Sha256Size];
                        Array.Copy(signedImageBytes, value, hash, 0, Sha256Size);

                        return true;
                    }

                    entry = value + length;
                }

                // The hash lives in the unprotected area, so only a protected area is worth
                // stepping over; anything else means we are done.
                if (infoMagic != TlvInfoMagicProtected)
                {
                    return false;
                }

                offset = areaEnd;
            }

            return false;
        }

        /// <summary>
        /// Generates a new ECDSA P-256 signing key pair.
        /// </summary>
        /// <param name="outputKeyPath">Path for the generated PEM key file.</param>
        public ExitCodes GenerateSigningKey(string outputKeyPath)
        {
            if (outputKeyPath is null)
            {
                throw new ArgumentNullException(nameof(outputKeyPath));
            }

            RequireImgtool();

            string args = $"keygen --key \"{outputKeyPath}\" --type ecdsa-p256";
            var (exitCode, _, stderr) = RunImgtool(args);

            if (exitCode != 0)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Red;
                OutputWriter.WriteLine($"** ERROR: imgtool keygen failed (exit {exitCode}): {stderr}");
                OutputWriter.ForegroundColor = ConsoleColor.White;

                return ExitCodes.E10004;
            }

            if (Verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Green;
                OutputWriter.WriteLine($"Signing key written to '{outputKeyPath}'.");
                OutputWriter.ForegroundColor = ConsoleColor.White;
            }

            return ExitCodes.OK;
        }

        /// <summary>
        /// Extracts the public key from a signing key in C source format.
        /// </summary>
        /// <param name="signingKeyPath">Path to the PEM signing key.</param>
        /// <param name="outputCSourcePath">Path for the generated C source file.</param>
        public ExitCodes ExtractPublicKey(string signingKeyPath, string outputCSourcePath)
        {
            if (signingKeyPath is null)
            {
                throw new ArgumentNullException(nameof(signingKeyPath));
            }

            if (outputCSourcePath is null)
            {
                throw new ArgumentNullException(nameof(outputCSourcePath));
            }

            RequireImgtool();

            string args = $"getpub --key \"{signingKeyPath}\" --lang c";
            var (exitCode, stdout, stderr) = RunImgtool(args);

            if (exitCode != 0)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Red;
                OutputWriter.WriteLine($"** ERROR: imgtool getpub failed (exit {exitCode}): {stderr}");
                OutputWriter.ForegroundColor = ConsoleColor.White;

                return ExitCodes.E10004;
            }

            File.WriteAllText(outputCSourcePath, stdout);

            if (Verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Green;
                OutputWriter.WriteLine($"Public key C source written to '{outputCSourcePath}'.");
                OutputWriter.ForegroundColor = ConsoleColor.White;
            }

            return ExitCodes.OK;
        }

        /// <summary>
        /// Converts a dotted-quad version string to the format imgtool expects:
        /// "major.minor.revision+build" (build number separated by '+').
        /// </summary>
        /// <param name="dotQuadVersion">Version string like "1.12.0.45".</param>
        /// <returns>Version string like "1.12.0+45".</returns>
        internal static string FormatImgtoolVersion(string dotQuadVersion)
        {
            // Replace all dots with '+', then restore the first two back to dots.
            // "1.12.0.45" → "1+12+0+45" → "1.12.0+45"
            var chars = dotQuadVersion.Replace('.', '+').ToCharArray();
            int replaced = 0;

            for (int i = 0; i < chars.Length && replaced < 2; i++)
            {
                if (chars[i] == '+')
                {
                    chars[i] = '.';
                    replaced++;
                }
            }

            return new string(chars);
        }

        /// <summary>
        /// Locates imgtool. Returns:
        ///   "imgtool"  — direct PATH executable
        ///   "python"   — use 'python -m imgtool'
        ///   "python3"  — use 'python3 -m imgtool'
        ///   a full path — bundled imgtool.exe
        ///   null       — not found
        /// </summary>
        internal static string FindImgtool()
        {
            // 1. Direct executable on PATH
            if (TryRunProcess("imgtool", "--version", out _))
            {
                return "imgtool";
            }

            // 2. Python module (python / python3)
            if (TryRunProcess("python", "-m imgtool --version", out _))
            {
                return ImgtoolViaPython;
            }

            if (TryRunProcess("python3", "-m imgtool --version", out _))
            {
                return ImgtoolViaPython3;
            }

            // 3. Bundled alongside the assembly
            string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
            string bundled = Path.Combine(exeDir, "tools", "imgtool", "imgtool.exe");
            if (File.Exists(bundled))
            {
                return bundled;
            }

            return null;
        }

        private void RequireImgtool()
        {
            _imgtoolPath ??= FindImgtool();

            if (_imgtoolPath is null)
            {
                throw new ImgtoolNotFoundException(
                    "imgtool not found. Install via 'pip install imgtool' or place imgtool.exe in <nanoff-dir>/tools/imgtool/.");
            }
        }

        /// <summary>
        /// Runs imgtool with the given sub-command arguments.
        /// Handles both direct-executable and python-module invocation modes.
        /// </summary>
        private (int exitCode, string stdout, string stderr) RunImgtool(string arguments)
        {
            string executable;
            string fullArguments;

            if (_imgtoolPath == ImgtoolViaPython || _imgtoolPath == ImgtoolViaPython3)
            {
                executable = _imgtoolPath;
                fullArguments = $"-m imgtool {arguments}";
            }
            else
            {
                executable = _imgtoolPath;
                fullArguments = arguments;
            }

            try
            {
                using var proc = new Process();

                proc.StartInfo = new ProcessStartInfo(executable, fullArguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                proc.Start();

                // drain both streams at the same time
                var stdoutTask = proc.StandardOutput.ReadToEndAsync();
                var stderrTask = proc.StandardError.ReadToEndAsync();
                proc.WaitForExit(60000);

                return (proc.ExitCode, stdoutTask.GetAwaiter().GetResult(), stderrTask.GetAwaiter().GetResult());
            }
            catch (Exception ex)
            {
                return (-1, string.Empty, ex.Message);
            }
        }

        private static bool TryRunProcess(string executable, string arguments, out string stdout)
        {
            stdout = string.Empty;
            try
            {
                using var proc = new Process();

                proc.StartInfo = new ProcessStartInfo(executable, arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                proc.Start();

                var stdoutTask = proc.StandardOutput.ReadToEndAsync();
                var stderrTask = proc.StandardError.ReadToEndAsync();
                proc.WaitForExit(5000);

                stdout = stdoutTask.GetAwaiter().GetResult();
                stderrTask.GetAwaiter().GetResult();

                return proc.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
