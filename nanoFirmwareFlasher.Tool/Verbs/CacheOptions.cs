// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Options for the <c>cache</c> verb: clear the local firmware cache, or download
    /// firmware packages into a firmware archive directory.
    /// </summary>
    [Verb("cache", HelpText = "Clear the local firmware cache, or download firmware into a firmware archive.")]
    public class CacheOptions : VerbOptionsBase
    {
        [Option(
            "clear",
            Required = false,
            Default = false,
            HelpText = "Clear the cache folder with firmware images.")]
        public bool Clear { get; set; }

        [Option(
            "download",
            Required = false,
            Default = false,
            HelpText = "Copy the firmware from the online repository to the firmware archive directory.")]
        public bool Download { get; set; }

        [Option(
            "archivepath",
            Required = false,
            Default = null,
            HelpText = "Path of the directory where the firmware is archived. Required when download is specified.")]
        public string FwArchivePath { get; set; }

        [Option(
            "platform",
            Required = false,
            Default = null,
            HelpText = "Target platform. Acceptable values are: esp32, stm32, cc13x2, efm32, rpi_pico.")]
        public SupportedPlatform? Platform { get; set; }

        [Option(
            "target",
            Required = false,
            Default = null,
            HelpText = "Target name. This is the target name used in the GitHub and Cloudsmith repositories.")]
        public string TargetName { get; set; }

        [Option(
            "fwversion",
            Required = false,
            Default = null,
            HelpText = "Firmware version to download.")]
        public string FwVersion { get; set; }

        [Option(
            "preview",
            Required = false,
            Default = false,
            HelpText = "Download a firmware package from the preview repository that includes major changes or experimental features.")]
        public bool Preview { get; set; }

        /// <summary>
        /// Validates early constraints for the <c>cache</c> verb.
        /// </summary>
        /// <returns><see langword="null"/> if valid, or an error message describing the constraint violation.</returns>
        public static string Validate(CacheOptions o)
        {
            int count = (o.Clear ? 1 : 0) + (o.Download ? 1 : 0);

            if (count == 0)
            {
                return "cache requires one of clear or download.";
            }

            if (count > 1)
            {
                return "Only one of clear or download can be specified at a time.";
            }

            if (o.Download)
            {
                if (string.IsNullOrEmpty(o.FwArchivePath))
                {
                    return "download requires archivepath to specify the firmware archive location.";
                }

                if (o.Platform is null && string.IsNullOrEmpty(o.TargetName))
                {
                    return "download requires platform or target to specify what firmware to download.";
                }
            }

            return null;
        }
    }
}
