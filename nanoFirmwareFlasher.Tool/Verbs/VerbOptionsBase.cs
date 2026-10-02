// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Globalization;
using System.Linq;
using CommandLine;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Options common to every nanoff verb.
    /// </summary>
    public abstract class VerbOptionsBase
    {
        /// <summary>
        /// Allowed values:
        /// q[uiet]
        /// m[inimal]
        /// n[ormal]
        /// d[etailed]
        /// diag[nostic]
        /// </summary>
        [Option(
            'v',
            "verbosity",
            Required = false,
            Default = "n",
            HelpText = "Sets the verbosity level of the command. Allowed values are q[uiet], m[inimal], n[ormal], d[etailed], and diag[nostic].")]
        public string Verbosity { get; set; }

        [Option(
            "suppressnanoffversioncheck",
            Required = false,
            Default = false,
            HelpText = "Do not check whether a new version of nanoff is available.")]
        public bool SuppressNanoFFVersionCheck { get; set; }

        /// <summary>
        /// Parses <see cref="Verbosity"/> into a <see cref="VerbosityLevel"/>.
        /// </summary>
        public VerbosityLevel GetVerbosityLevel()
        {
            return ParseVerbosity(Verbosity);
        }

        /// <summary>
        /// Parses a verbosity string (short or long form) into a <see cref="VerbosityLevel"/>.
        /// </summary>
        /// <param name="value">The verbosity string (e.g. "q", "quiet", "n", "normal", "diag").</param>
        /// <returns>The parsed <see cref="VerbosityLevel"/>.</returns>
        /// <exception cref="ArgumentException">Thrown when the value is not a recognized verbosity level.</exception>
        internal static VerbosityLevel ParseVerbosity(string value)
        {
            switch (value)
            {
                case "q":
                case "quiet":
                    return VerbosityLevel.Quiet;

                case "m":
                case "minimal":
                    return VerbosityLevel.Minimal;

                case "n":
                case "normal":
                    return VerbosityLevel.Normal;

                case "d":
                case "detailed":
                    return VerbosityLevel.Detailed;

                case "diag":
                case "diagnostic":
                    return VerbosityLevel.Diagnostic;

                default:
                    throw new ArgumentException("Invalid option for Verbosity");
            }
        }

        /// <summary>
        /// Validates a set of mutually-exclusive keywords, shared by every verb's <c>Validate</c>
        /// method: an error is returned if more than one is set, or (when <paramref name="requireOne"/>
        /// is <see langword="true"/>) if none are set.
        /// </summary>
        /// <param name="verb">The verb name, used in the "requires one of" message.</param>
        /// <param name="requireOne">Whether at least one of the keywords must be set.</param>
        /// <param name="keywordNames">The keyword names as shown in error messages, e.g. "dfu, jtag or xds".</param>
        /// <param name="isSet">Whether each keyword is set, in any order.</param>
        internal static string ValidateMutuallyExclusive(string verb, bool requireOne, string keywordNames, params bool[] isSet)
        {
            int count = isSet.Count(b => b);

            if (requireOne && count == 0)
            {
                return $"{verb} requires one of {keywordNames}.";
            }

            if (count > 1)
            {
                return $"Only one of {keywordNames} can be specified at a time.";
            }

            return null;
        }

        /// <summary>
        /// Validates the MCUboot keywords shared by the <c>flash</c> and <c>deploy</c> verbs.
        /// </summary>
        /// <returns><see langword="null"/> if valid, or an error message describing the constraint violation.</returns>
        internal static string ValidateMcubootUpload(
            bool mcuboot,
            string serialPort,
            string signKey,
            string slotSize,
            string headerSize,
            string writeAlign,
            bool secondarySlot)
        {
            bool anySigningParameter = !string.IsNullOrEmpty(slotSize)
                || !string.IsNullOrEmpty(headerSize)
                || !string.IsNullOrEmpty(writeAlign);

            if (!mcuboot)
            {
                if (!string.IsNullOrEmpty(signKey) || anySigningParameter || secondarySlot)
                {
                    return "signkey, slotsize, headersize, writealign and secondaryslot can only be used with mcuboot.";
                }

                return null;
            }

            if (string.IsNullOrEmpty(serialPort))
            {
                return "mcuboot requires serialport to specify the port for the SMP transport.";
            }

            if (anySigningParameter && string.IsNullOrEmpty(signKey))
            {
                return "slotsize, headersize and writealign only apply when signing the image with signkey.";
            }

            foreach ((string name, string value) in new[] { ("slotsize", slotSize), ("headersize", headerSize), ("writealign", writeAlign) })
            {
                if (!TryParseMcubootSize(value, out _))
                {
                    return $"{name} must be a positive number, in decimal or hexadecimal (e.g. 0x100000) format.";
                }
            }

            return null;
        }

        /// <summary>
        /// Parses an MCUboot size/alignment value given in decimal or <c>0x</c> prefixed hexadecimal format.
        /// </summary>
        /// <param name="value">The value to parse. <see langword="null"/> or empty means "not specified".</param>
        /// <param name="result">The parsed value, or <see langword="null"/> when not specified.</param>
        /// <returns><see langword="false"/> if the value is specified but isn't a valid positive number.</returns>
        internal static bool TryParseMcubootSize(string value, out int? result)
        {
            result = null;

            if (string.IsNullOrEmpty(value))
            {
                return true;
            }

            int parsed;
            bool success = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? int.TryParse(value.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out parsed)
                : int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed);

            if (!success || parsed <= 0)
            {
                return false;
            }

            result = parsed;
            return true;
        }
    }
}
