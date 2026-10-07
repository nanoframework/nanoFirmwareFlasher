// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

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
        // unprotected TLV area
        private const ushort TlvInfoMagic = 0x6907;
        // protected TLV area
        private const ushort TlvInfoMagicProtected = 0x6908;
        private const ushort TlvSha256 = 0x10;
        private const int TlvHeaderSize = 4;
        private const int Sha256Size = 32;

        // Sentinel values returned by FindImgtool() to indicate Python-module invocation.
        private const string ImgtoolViaPython = "python";
        private const string ImgtoolViaPython3 = "python3";

        // Signing a full slot takes about a second; the margin covers a cold Python start
        // and the scan an anti-malware tool may run on first use.
        private const int ImgtoolTimeoutMs = 60_000;

        // "imgtool --version" probe, same reasoning: a first Python start under an AV scan can exceed 5 s.
        private const int ImgtoolProbeTimeoutMs = 15_000;

        // Win32 ERROR_ACCESS_DENIED: the executable exists but its execution was refused.
        private const int AccessDeniedError = 5;

        private const string BlockedProcessHint =
            "imgtool runs as an external process (Python) to sign images and manage keys. "
            + "Security software such as Windows Defender, Smart App Control or AppLocker can block it: "
            + "allow imgtool/python to run, or sign the image beforehand and upload it without signkey.";

        private static readonly char[] s_charsNeedingQuotes = { ' ', '\t', '\n', '\v', '"' };

        private readonly string _signingKeyPath;
        private readonly int _slotSize;
        private readonly int _headerSize;
        private readonly int _writeAlignment;

        // null until first needed; set lazily
        private string _imgtoolPath;

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
        public ExitCodes SignImage(
            string inputBinPath,
            string outputBinPath,
            string version)
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

            string keyPath = GetFullPath(_signingKeyPath, "signing key");
            inputBinPath = GetFullPath(inputBinPath, "image");
            outputBinPath = GetFullPath(outputBinPath, "signed image");

            RequireImgtool();

            string imgtoolVersion = FormatImgtoolVersion(version);

            var (exitCode, _, stderr) = RunImgtool(
                "sign",
                "--key", keyPath,
                "--align", _writeAlignment.ToString(CultureInfo.InvariantCulture),
                "--version", imgtoolVersion,
                "--header-size", _headerSize.ToString(CultureInfo.InvariantCulture),
                "--pad-header",
                "--slot-size", _slotSize.ToString(CultureInfo.InvariantCulture),
                inputBinPath,
                outputBinPath);

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
                    // IsValid remains false
                    return info;
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
        public static bool TryGetImageHash(
            byte[] signedImageBytes,
            out byte[] hash)
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

            outputKeyPath = GetFullPath(outputKeyPath, "signing key");

            RequireImgtool();

            var (exitCode, _, stderr) = RunImgtool("keygen", "--key", outputKeyPath, "--type", "ecdsa-p256");

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
        public ExitCodes ExtractPublicKey(
            string signingKeyPath,
            string outputCSourcePath)
        {
            if (signingKeyPath is null)
            {
                throw new ArgumentNullException(nameof(signingKeyPath));
            }

            if (outputCSourcePath is null)
            {
                throw new ArgumentNullException(nameof(outputCSourcePath));
            }

            signingKeyPath = GetFullPath(signingKeyPath, "signing key");
            outputCSourcePath = GetFullPath(outputCSourcePath, "public key output");

            RequireImgtool();

            var (exitCode, stdout, stderr) = RunImgtool("getpub", "--key", signingKeyPath, "--lang", "c");

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
        internal static string FindImgtool() => FindImgtool(out _);

        /// <param name="blocked">
        /// <see langword="true"/> when a candidate exists but couldn't run (execution refused, or it hung),
        /// which usually means security software is blocking it.
        /// </param>
        internal static string FindImgtool(out bool blocked)
        {
            blocked = false;

            // 1. Direct executable on PATH
            if (TryRunProcess("imgtool", new[] { "--version" }, ref blocked))
            {
                return "imgtool";
            }

            // 2. Python module (python / python3)
            if (TryRunProcess("python", new[] { "-m", "imgtool", "--version" }, ref blocked))
            {
                return ImgtoolViaPython;
            }

            if (TryRunProcess("python3", new[] { "-m", "imgtool", "--version" }, ref blocked))
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
            if (_imgtoolPath is null)
            {
                _imgtoolPath = FindImgtool(out bool blocked);

                if (_imgtoolPath is null && blocked)
                {
                    WriteBlockedProcessWarning();
                }
            }

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
        /// <param name="arguments">The arguments, one token each: they are passed to imgtool as-is, never parsed by a shell.</param>
        private (int exitCode, string stdout, string stderr) RunImgtool(params string[] arguments)
        {
            IEnumerable<string> fullArguments = _imgtoolPath == ImgtoolViaPython || _imgtoolPath == ImgtoolViaPython3
                ? new[] { "-m", "imgtool" }.Concat(arguments)
                : arguments;

            try
            {
                ProcessResult result = RunProcess(_imgtoolPath, fullArguments, ImgtoolTimeoutMs);

                if (result.TimedOut)
                {
                    WriteBlockedProcessWarning();

                    return (-1, result.Stdout, $"imgtool didn't complete within {ImgtoolTimeoutMs / 1000} seconds.");
                }

                return (result.ExitCode, result.Stdout, result.Stderr);
            }
            catch (Exception ex)
            {
                if (IsAccessDenied(ex))
                {
                    WriteBlockedProcessWarning();
                }

                return (-1, string.Empty, ex.Message);
            }
        }

        /// <summary>
        /// Runs <paramref name="executable"/> without a shell and waits for it to exit, killing it on timeout.
        /// </summary>
        private static ProcessResult RunProcess(
            string executable,
            IEnumerable<string> arguments,
            int timeoutMs)
        {
            var startInfo = new ProcessStartInfo(executable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

#if NET
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
#else
            startInfo.Arguments = BuildArguments(arguments);
#endif

            using var proc = new Process { StartInfo = startInfo };

            proc.Start();

            Task<string> stdoutTask = proc.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = proc.StandardError.ReadToEndAsync();

            if (!proc.WaitForExit(timeoutMs))
            {
                try
                {
#if NET
                    proc.Kill(entireProcessTree: true);
#else
                    proc.Kill();
#endif
                }
                catch (InvalidOperationException)
                {
                    // exited in the meantime
                }

                // the streams close once the process is gone
                proc.WaitForExit();

                return new ProcessResult(-1, stdoutTask.GetAwaiter().GetResult(), stderrTask.GetAwaiter().GetResult(), timedOut: true);
            }

            return new ProcessResult(proc.ExitCode, stdoutTask.GetAwaiter().GetResult(), stderrTask.GetAwaiter().GetResult(), timedOut: false);
        }

        private static bool TryRunProcess(
            string executable,
            string[] arguments,
            ref bool blocked)
        {
            try
            {
                ProcessResult result = RunProcess(executable, arguments, ImgtoolProbeTimeoutMs);

                blocked |= result.TimedOut;

                return !result.TimedOut && result.ExitCode == 0;
            }
            catch (Exception ex)
            {
                // a missing executable just isn't a candidate; a refused one is worth reporting
                blocked |= IsAccessDenied(ex);

                return false;
            }
        }

        private static bool IsAccessDenied(Exception ex) => ex is Win32Exception { NativeErrorCode: AccessDeniedError };

        private static void WriteBlockedProcessWarning()
        {
            OutputWriter.ForegroundColor = ConsoleColor.Yellow;
            OutputWriter.WriteLine(BlockedProcessHint);
            OutputWriter.ForegroundColor = ConsoleColor.White;
        }

        /// <summary>
        /// Normalises a path given by the user, rejecting malformed ones before they reach the command line.
        /// </summary>
        private static string GetFullPath(
            string path,
            string description)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                throw new McubootImageException($"Invalid {description} path: '{path}'.", ex);
            }
        }

        /// <summary>
        /// Joins arguments into a command line that the Windows C runtime parses back into the same tokens
        /// (same rules as <c>ProcessStartInfo.ArgumentList</c>, which isn't available on .NET Framework).
        /// </summary>
        internal static string BuildArguments(IEnumerable<string> arguments) => string.Join(" ", arguments.Select(QuoteArgument));

        /// <summary>
        /// Quotes an argument for the Windows command line: wraps it in quotes when needed, escapes embedded
        /// quotes, and doubles the backslashes that precede a quote (including the closing one).
        /// </summary>
        internal static string QuoteArgument(string argument)
        {
            if (argument.Length > 0 && argument.IndexOfAny(s_charsNeedingQuotes) < 0)
            {
                return argument;
            }

            var quoted = new StringBuilder("\"");
            int backslashes = 0;

            foreach (char c in argument)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }

                if (c == '"')
                {
                    quoted.Append('\\', (backslashes * 2) + 1);
                }
                else
                {
                    quoted.Append('\\', backslashes);
                }

                quoted.Append(c);
                backslashes = 0;
            }

            // backslashes before the closing quote must be doubled
            quoted.Append('\\', backslashes * 2);
            quoted.Append('"');

            return quoted.ToString();
        }

        private readonly struct ProcessResult
        {
            public ProcessResult(int exitCode, string stdout, string stderr, bool timedOut)
            {
                ExitCode = exitCode;
                Stdout = stdout;
                Stderr = stderr;
                TimedOut = timedOut;
            }

            public int ExitCode { get; }

            public string Stdout { get; }

            public string Stderr { get; }

            public bool TimedOut { get; }
        }
    }
}
