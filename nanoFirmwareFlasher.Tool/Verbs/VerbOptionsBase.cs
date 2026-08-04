// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
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
    }
}
