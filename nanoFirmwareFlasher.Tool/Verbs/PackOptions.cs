// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using CommandLine;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Options for the <c>pack</c> verb: build files out of signed images.
    /// </summary>
    [Verb("pack", HelpText = "Build an update bundle from signed MCUboot images, for import from an SD card or USB mass storage.")]
    public class PackOptions : VerbOptionsBase
    {
        [Option(
            "update",
            Required = false,
            Default = false,
            HelpText = "Build an update bundle with a nanoCLR and a deployment image.")]
        public bool Update { get; set; }

        [Option(
            "clr",
            Required = false,
            Default = null,
            HelpText = "Path to the signed nanoCLR image (MCUboot image 0).")]
        public string Clr { get; set; }

        [Option(
            "deployment",
            Required = false,
            Default = null,
            HelpText = "Path to the signed deployment image (MCUboot image 1).")]
        public string Deployment { get; set; }

        [Option(
            "output",
            Required = false,
            Default = null,
            HelpText = "Bundle file, or directory to write it into. Default: nanoUpdate-<nanoCLR version>.bin in the current directory.")]
        public string Output { get; set; }

        [Option(
            "overwrite",
            Required = false,
            Default = false,
            HelpText = "Replace the bundle file if it already exists.")]
        public bool Overwrite { get; set; }

        /// <summary>
        /// Validates early constraints for the <c>pack</c> verb.
        /// </summary>
        /// <returns><see langword="null"/> if valid, or an error message describing the constraint violation.</returns>
        public static string Validate(PackOptions o)
        {
            if (!o.Update)
            {
                return "pack requires update.";
            }

            if (string.IsNullOrEmpty(o.Clr) || string.IsNullOrEmpty(o.Deployment))
            {
                return "update requires both clr and deployment. A bundle with a single image is not supported.";
            }

            if (o.Output != null && string.IsNullOrWhiteSpace(o.Output))
            {
                return "output can't be empty.";
            }

            try
            {
                if (string.Equals(Path.GetFullPath(o.Clr), Path.GetFullPath(o.Deployment), StringComparison.OrdinalIgnoreCase))
                {
                    return "clr and deployment must be different files.";
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return $"invalid image path: {ex.Message}";
            }

            return null;
        }
    }
}
