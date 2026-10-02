// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Options for the <c>keys</c> verb: manage the keys used to sign MCUboot images.
    /// </summary>
    [Verb("keys", HelpText = "Generate an MCUboot signing key or export its public key.")]
    public class KeysOptions : VerbOptionsBase
    {
        [Option(
            "generate",
            Required = false,
            Default = null,
            HelpText = "Generate a new ECDSA P-256 MCUboot signing key and write it (PEM) to this path.")]
        public string Generate { get; set; }

        [Option(
            "getpub",
            Required = false,
            Default = null,
            HelpText = "Extract the public key from the signing key in signkey and write it as a C source file to this path, to be built into MCUboot.")]
        public string GetPub { get; set; }

        [Option(
            "signkey",
            Required = false,
            Default = null,
            HelpText = "Path to the PEM signing key. Required with getpub.")]
        public string SignKey { get; set; }

        /// <summary>
        /// Validates early constraints for the <c>keys</c> verb.
        /// </summary>
        /// <returns><see langword="null"/> if valid, or an error message describing the constraint violation.</returns>
        public static string Validate(KeysOptions o)
        {
            string mutuallyExclusiveError = ValidateMutuallyExclusive(
                "keys",
                requireOne: true,
                "generate or getpub",
                !string.IsNullOrEmpty(o.Generate),
                !string.IsNullOrEmpty(o.GetPub));

            if (mutuallyExclusiveError != null)
            {
                return mutuallyExclusiveError;
            }

            if (!string.IsNullOrEmpty(o.GetPub) && string.IsNullOrEmpty(o.SignKey))
            {
                return "getpub requires signkey to specify the signing key to extract the public key from.";
            }

            if (!string.IsNullOrEmpty(o.Generate) && !string.IsNullOrEmpty(o.SignKey))
            {
                return "signkey can only be used with getpub.";
            }

            return null;
        }
    }
}
