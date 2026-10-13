// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace nanoFramework.Tools.FirmwareFlasher.Mcuboot
{
    /// <summary>
    /// Builds update bundles: one file carrying a signed nanoCLR image and a signed deployment
    /// image, for the MCUboot bootloader to import.
    /// </summary>
    /// <remarks>
    /// See nf-interpreter <c>MCUboot/docs/update-bundle.md</c> for format details.
    /// <code>
    /// 0   8  marker          "NFUPDATE"
    /// 8   2  format_version  1
    /// 10  2  header_size     bytes from 0 to the end of the entry table
    /// 12  1  entry_count
    /// 13  3  reserved        0
    /// 16  12 entry[i]: image (u8), reserved (3 x u8), offset (u32, multiple of 4), size (u32)
    /// </code>
    /// Each entry is the signed image byte for byte, header through TLV area.
    /// </remarks>
    public static class McubootUpdateBundle
    {
        /// <summary>
        /// Marker at the start of every bundle.
        /// </summary>
        public const string Marker = "NFUPDATE";

        /// <summary>
        /// Bundle format version written by this tool.
        /// </summary>
        public const ushort BundleFormatVersion = 1;

        /// <summary>
        /// MCUboot image index of the nanoCLR.
        /// </summary>
        public const byte ClrImageIndex = 0;

        /// <summary>
        /// MCUboot image index of the deployment.
        /// </summary>
        public const byte DeploymentImageIndex = 1;

        internal const int ManifestFixedSize = 16;
        internal const int EntrySize = 12;
        internal const byte PaddingByte = 0xFF;

        private const uint McubootMagic = 0x96f3b83d;
        private const int ImageHeaderSize = 32;
        private const ushort TlvInfoMagic = 0x6907;
        private const int TlvInfoSize = 4;
        private const int ImageAlignment = 4;

        /// <summary>
        /// One entry of a bundle manifest.
        /// </summary>
        public sealed class BundleEntry
        {
            /// <summary>MCUboot image index.</summary>
            public byte Image { get; set; }

            /// <summary>Offset of the signed image in the bundle.</summary>
            public uint Offset { get; set; }

            /// <summary>Size of the signed image.</summary>
            public uint Size { get; set; }
        }

        /// <summary>
        /// A signed image checked and ready to go into a bundle.
        /// </summary>
        internal sealed class SignedImage
        {
            public string Path { get; set; }

            /// <summary>The signed image, header through TLV area (trailing bytes of the file dropped).</summary>
            public byte[] Bytes { get; set; }

            public string Version { get; set; }

            public byte[] Hash { get; set; }

            /// <summary>Bytes in the file after the TLV area, not carried into the bundle.</summary>
            public long TrailingBytes { get; set; }
        }

        /// <summary>
        /// Checks both signed images and writes the bundle.
        /// </summary>
        /// <param name="clrPath">Signed nanoCLR image (image 0).</param>
        /// <param name="deploymentPath">Signed deployment image (image 1).</param>
        /// <param name="output">
        /// Output file, or a directory to write the bundle into under its default name.
        /// <see langword="null"/> or empty for the current directory.
        /// </param>
        /// <param name="overwrite">Replace the output file when it exists.</param>
        /// <param name="verbosity">Output verbosity.</param>
        /// <param name="bundlePath">Full path of the bundle written, when successful.</param>
        /// <param name="errorDetail">Details on the failure, to show along with the exit code.</param>
        /// <returns><see cref="ExitCodes.OK"/> when the bundle was written.</returns>
        public static ExitCodes Create(
            string clrPath,
            string deploymentPath,
            string output,
            bool overwrite,
            VerbosityLevel verbosity,
            out string bundlePath,
            out string errorDetail)
        {
            bundlePath = null;

            ExitCodes result = LoadImage(
                clrPath,
                out SignedImage clr,
                out errorDetail);

            if (result != ExitCodes.OK)
            {
                return result;
            }

            result = LoadImage(
                deploymentPath,
                out SignedImage deployment,
                out errorDetail);

            if (result != ExitCodes.OK)
            {
                return result;
            }

            // the bundle is built in a single array: its size, alignment padding included, must fit one
            long bundleSize = AlignUp(AlignUp((long)ManifestFixedSize + 2 * EntrySize) + clr.Bytes.Length) + deployment.Bytes.Length;

            if (bundleSize > int.MaxValue)
            {
                errorDetail = $"{clrPath} + {deploymentPath}: {bundleSize} bytes.";
                return ExitCodes.E10028;
            }

            string target = ResolveOutputPath(
                output,
                DefaultFileName(clr.Version));

            if (IsSamePath(target, clrPath) || IsSamePath(target, deploymentPath))
            {
                errorDetail = "The output file can't be one of the input images.";
                return ExitCodes.E9000;
            }

            if (File.Exists(target) && !overwrite)
            {
                errorDetail = target;
                return ExitCodes.E10026;
            }

            byte[] bundle = BuildBundleBytes(
                clr.Bytes,
                deployment.Bytes);

            result = WriteBundle(
                target,
                bundle,
                clr.Bytes,
                deployment.Bytes,
                out errorDetail);

            if (result != ExitCodes.OK)
            {
                return result;
            }

            bundlePath = target;

            ShowSummary(
                verbosity,
                clr,
                deployment,
                bundle,
                target);

            return ExitCodes.OK;
        }

        /// <summary>
        /// Version of a signed image as <c>major.minor.revision.build</c>.
        /// </summary>
        public static string GetVersion(byte[] signedImage)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}.{1}.{2}.{3}",
                signedImage[20],
                signedImage[21],
                BitConverter.ToUInt16(signedImage, 22),
                BitConverter.ToUInt32(signedImage, 24));
        }

        /// <summary>
        /// Default bundle file name for a nanoCLR version, e.g. <c>nanoUpdate-1.12.0.123.bin</c>.
        /// </summary>
        public static string DefaultFileName(string clrVersion) => $"nanoUpdate-{clrVersion}.bin";

        /// <summary>
        /// Lays out a bundle with the nanoCLR and the deployment image.
        /// </summary>
        public static byte[] BuildBundleBytes(
            byte[] clr,
            byte[] deployment)
        {
            const int entryCount = 2;
            int headerSize = ManifestFixedSize + entryCount * EntrySize;
            int clrOffset = AlignUp(headerSize);
            int deploymentOffset = AlignUp(clrOffset + clr.Length);

            byte[] bundle = new byte[deploymentOffset + deployment.Length];

            Encoding.ASCII.GetBytes(Marker, 0, Marker.Length, bundle, 0);
            WriteUInt16(
                bundle,
                8,
                BundleFormatVersion);
            WriteUInt16(
                bundle,
                10,
                (ushort)headerSize);
            bundle[12] = entryCount;

            WriteEntry(
                bundle,
                ManifestFixedSize,
                ClrImageIndex,
                (uint)clrOffset,
                (uint)clr.Length);
            WriteEntry(
                bundle,
                ManifestFixedSize + EntrySize,
                DeploymentImageIndex,
                (uint)deploymentOffset,
                (uint)deployment.Length);

            for (int i = headerSize; i < clrOffset; i++)
            {
                bundle[i] = PaddingByte;
            }

            Buffer.BlockCopy(
                clr,
                0,
                bundle,
                clrOffset,
                clr.Length);

            for (int i = clrOffset + clr.Length; i < deploymentOffset; i++)
            {
                bundle[i] = PaddingByte;
            }

            Buffer.BlockCopy(
                deployment,
                0,
                bundle,
                deploymentOffset,
                deployment.Length);

            return bundle;
        }

        /// <summary>
        /// Parses and checks a bundle manifest with the same rules the bootloader applies to it.
        /// </summary>
        /// <returns><see langword="true"/> when the manifest is valid.</returns>
        public static bool TryParseBundle(
            byte[] data,
            out List<BundleEntry> entries)
        {
            entries = null;

            if (data is null
                || data.Length < ManifestFixedSize
                || Encoding.ASCII.GetString(data, 0, Marker.Length) != Marker
                || BitConverter.ToUInt16(data, 8) != BundleFormatVersion)
            {
                return false;
            }

            int headerSize = BitConverter.ToUInt16(data, 10);
            int count = data[12];

            if (count == 0 || headerSize < ManifestFixedSize + count * EntrySize || headerSize > data.Length)
            {
                return false;
            }

            var parsed = new List<BundleEntry>(count);

            for (int i = 0; i < count; i++)
            {
                int at = ManifestFixedSize + i * EntrySize;
                var entry = new BundleEntry
                {
                    Image = data[at],
                    Offset = BitConverter.ToUInt32(data, at + 4),
                    Size = BitConverter.ToUInt32(data, at + 8),
                };

                if (entry.Offset < headerSize
                    || entry.Offset % ImageAlignment != 0
                    || entry.Size == 0
                    || entry.Offset > data.Length
                    || entry.Size > data.Length - entry.Offset)
                {
                    return false;
                }

                foreach (BundleEntry other in parsed)
                {
                    if (other.Image == entry.Image
                        || (entry.Offset < (long)other.Offset + other.Size && other.Offset < (long)entry.Offset + entry.Size))
                    {
                        return false;
                    }
                }

                parsed.Add(entry);
            }

            entries = parsed;

            return true;
        }

        /// <summary>
        /// Reads a signed image and checks its header, its sizes and its SHA-256.
        /// </summary>
        internal static ExitCodes LoadImage(
            string path,
            out SignedImage image,
            out string errorDetail)
        {
            image = null;
            errorDetail = null;

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                errorDetail = path;
                return ExitCodes.E10023;
            }

            byte[] file;

            try
            {
                file = File.ReadAllBytes(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                errorDetail = $"{path}: {ex.Message}";
                return ExitCodes.E10023;
            }

            if (file.Length < ImageHeaderSize || BitConverter.ToUInt32(file, 0) != McubootMagic)
            {
                errorDetail = $"{path}: no MCUboot image header. Sign it first, e.g. with imgtool.";
                return ExitCodes.E10024;
            }

            ushort headerSize = BitConverter.ToUInt16(file, 8);
            ushort protectedTlvSize = BitConverter.ToUInt16(file, 10);
            uint imageSize = BitConverter.ToUInt32(file, 12);

            // header + payload + protected TLVs: what the SHA-256 covers
            long hashedSize = (long)headerSize + imageSize + protectedTlvSize;

            if (headerSize < ImageHeaderSize || hashedSize + TlvInfoSize > file.Length)
            {
                errorDetail = $"{path}: image sizes in the header don't match the file.";
                return ExitCodes.E10024;
            }

            ushort tlvMagic = BitConverter.ToUInt16(file, (int)hashedSize);
            ushort tlvTotal = BitConverter.ToUInt16(file, (int)hashedSize + 2);
            long signedSize = hashedSize + tlvTotal;

            if (tlvMagic != TlvInfoMagic || tlvTotal < TlvInfoSize || signedSize > file.Length)
            {
                errorDetail = $"{path}: TLV area missing or truncated.";
                return ExitCodes.E10024;
            }

            byte[] signed = new byte[signedSize];
            Buffer.BlockCopy(
                file,
                0,
                signed,
                0,
                signed.Length);

            if (!McubootImageManager.TryGetImageHash(signed, out byte[] hash))
            {
                errorDetail = $"{path}: no SHA-256 TLV.";
                return ExitCodes.E10025;
            }

            byte[] actual;

            using (SHA256 sha = SHA256.Create())
            {
                actual = sha.ComputeHash(signed, 0, (int)hashedSize);
            }

            if (!AreEqual(actual, hash))
            {
                errorDetail = $"{path}: SHA-256 doesn't match the image contents.";
                return ExitCodes.E10025;
            }

            image = new SignedImage
            {
                Path = path,
                Bytes = signed,
                Version = GetVersion(signed),
                Hash = hash,
                TrailingBytes = file.Length - signedSize,
            };

            return ExitCodes.OK;
        }

        /// <summary>
        /// Full path of the bundle: <paramref name="output"/> itself, or the default name in it
        /// when it is a directory, or in the current directory when it is not given.
        /// </summary>
        internal static string ResolveOutputPath(
            string output,
            string defaultName)
        {
            if (string.IsNullOrEmpty(output))
            {
                return Path.GetFullPath(defaultName);
            }

            if (Directory.Exists(output)
                || output.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                || output.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                return Path.GetFullPath(Path.Combine(output, defaultName));
            }

            return Path.GetFullPath(output);
        }

        private static ExitCodes WriteBundle(
            string target,
            byte[] bundle,
            byte[] clr,
            byte[] deployment,
            out string errorDetail)
        {
            errorDetail = null;
            string temp = null;

            try
            {
                string directory = Path.GetDirectoryName(target);

                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // write next to the target and move it in place, so a failure never leaves a partial bundle
                temp = $"{target}.{Guid.NewGuid():N}.tmp";

                File.WriteAllBytes(temp, bundle);

                if (File.Exists(target))
                {
                    File.Replace(temp, target, null);
                }
                else
                {
                    File.Move(temp, target);
                }

                temp = null;

                byte[] written = File.ReadAllBytes(target);

                if (!TryParseBundle(written, out List<BundleEntry> entries)
                    || entries.Count != 2
                    || !SliceEquals(written, entries[0], clr)
                    || !SliceEquals(written, entries[1], deployment))
                {
                    errorDetail = $"{target}: the bundle read back doesn't match what was written.";
                    return ExitCodes.E10027;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                errorDetail = ex.Message;
                return ExitCodes.E10027;
            }
            finally
            {
                if (temp != null && File.Exists(temp))
                {
                    try
                    {
                        File.Delete(temp);
                    }
                    catch (IOException)
                    {
                        // best effort
                    }
                }
            }

            return ExitCodes.OK;
        }

        private static void ShowSummary(
            VerbosityLevel verbosity,
            SignedImage clr,
            SignedImage deployment,
            byte[] bundle,
            string target)
        {
            if (verbosity < VerbosityLevel.Normal)
            {
                if (verbosity == VerbosityLevel.Minimal)
                {
                    OutputWriter.WriteLine(target);
                }

                return;
            }

            TryParseBundle(bundle, out List<BundleEntry> entries);

            OutputWriter.ForegroundColor = ConsoleColor.White;
            OutputWriter.WriteLine("Update bundle");
            ShowImage(verbosity, "nanoCLR   ", clr, entries[0]);
            ShowImage(verbosity, "Deployment", deployment, entries[1]);
            OutputWriter.WriteLine("  SHA-256 of both images verified.");
            OutputWriter.ForegroundColor = ConsoleColor.Green;
            OutputWriter.WriteLine($"  Written    : {target} ({bundle.Length} bytes)");
            OutputWriter.ForegroundColor = ConsoleColor.White;
        }

        private static void ShowImage(
            VerbosityLevel verbosity,
            string label,
            SignedImage image,
            BundleEntry entry)
        {
            OutputWriter.WriteLine(
                $"  {label} : {Path.GetFileName(image.Path)} {image.Version}, {entry.Size} bytes at 0x{entry.Offset:X8}");

            if (verbosity >= VerbosityLevel.Detailed)
            {
                OutputWriter.WriteLine($"               sha256 {ToHex(image.Hash)}");

                if (image.TrailingBytes > 0)
                {
                    OutputWriter.WriteLine($"               {image.TrailingBytes} bytes after the TLV area left out");
                }
            }
        }

        private static void WriteEntry(
            byte[] bundle,
            int at,
            byte image,
            uint offset,
            uint size)
        {
            bundle[at] = image;
            WriteUInt32(bundle, at + 4, offset);
            WriteUInt32(bundle, at + 8, size);
        }

        private static void WriteUInt16(
            byte[] buffer,
            int at,
            ushort value)
        {
            buffer[at] = (byte)value;
            buffer[at + 1] = (byte)(value >> 8);
        }

        private static void WriteUInt32(
            byte[] buffer,
            int at,
            uint value)
        {
            buffer[at] = (byte)value;
            buffer[at + 1] = (byte)(value >> 8);
            buffer[at + 2] = (byte)(value >> 16);
            buffer[at + 3] = (byte)(value >> 24);
        }

        private static int AlignUp(int value) => (value + ImageAlignment - 1) & ~(ImageAlignment - 1);

        private static long AlignUp(long value) => (value + ImageAlignment - 1) & ~(long)(ImageAlignment - 1);

        private static bool SliceEquals(
            byte[] data,
            BundleEntry entry,
            byte[] expected)
        {
            if (entry.Size != expected.Length)
            {
                return false;
            }

            for (int i = 0; i < expected.Length; i++)
            {
                if (data[entry.Offset + i] != expected[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AreEqual(
            byte[] a,
            byte[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            int diff = 0;

            for (int i = 0; i < a.Length; i++)
            {
                diff |= a[i] ^ b[i];
            }

            return diff == 0;
        }

        /// <summary>
        /// Whether two paths name the same file, comparing case as the platform's default file
        /// system does: case-sensitive on Linux, case-insensitive on Windows and macOS.
        /// </summary>
        public static bool IsSamePath(string a, string b)
            => string.Equals(
                Path.GetFullPath(a),
                Path.GetFullPath(b),
                RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

        private static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);

            foreach (byte b in bytes)
            {
                sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }
    }
}
