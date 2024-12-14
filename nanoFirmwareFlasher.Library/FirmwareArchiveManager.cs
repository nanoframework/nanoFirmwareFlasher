// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// The manager of a firmware archive directory. It supports downloading firmware packages and the runtimes
    /// for the Virtual Device to a local directory from the online archive. That directory can then be used as
    /// a source for list and deployment operations instead of the online archive.
    /// </summary>
    public sealed class FirmwareArchiveManager
    {
        private readonly string _archivePath;
        private const string INFOFILE_EXTENSION = ".json";

        #region Construction
        /// <summary>
        /// Create a new manager for the firmware archive
        /// </summary>
        /// <param name="fwArchivePath">Path to the firmware archive directory</param>
        public FirmwareArchiveManager(string fwArchivePath)
        {
            _archivePath = Path.GetFullPath(fwArchivePath);
        }
        #endregion

        #region Methods
        /// <summary>
        /// List the targets for which firmware is present in the firmware archive
        /// </summary>
        /// <param name="preview">Option for preview version.</param>
        /// <param name="platform">Platform code to use on search.</param>
        /// <param name="verbosity">VerbosityLevel to use when outputting progress and error messages.</param>
        /// <returns>List of <see cref="CloudSmithPackageDetail"/> with details on target firmware packages.</returns>
        public List<CloudSmithPackageDetail> GetTargetList(
            bool preview,
            SupportedPlatform? platform,
            VerbosityLevel verbosity)
        {
            List<CloudSmithPackageDetail> targetPackages = [];

            if (verbosity > VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.White;
                OutputWriter.WriteLine($"Listing {platform} targets from firmware archive '{_archivePath}'...");
            }

            if (Directory.Exists(_archivePath))
            {
                foreach (string filePath in Directory.EnumerateFiles(_archivePath, $"*{INFOFILE_EXTENSION}"))
                {
                    PersistedPackageInformation packageInformation = JsonConvert.DeserializeObject<PersistedPackageInformation>(File.ReadAllText(filePath));
                    if (packageInformation.IsPreview == preview &&
                        (platform is null || platform.Value.ToString().Equals(packageInformation.Platform, StringComparison.OrdinalIgnoreCase)))
                    {
                        targetPackages.Add(packageInformation);
                    }
                }
            }

            return targetPackages;
        }

        /// <summary>
        /// Get the latest version present in the firmware archive for the specified target.
        /// </summary>
        /// <param name="preview">Option for preview version.</param>
        /// <param name="target">Target to find the latest version of.</param>
        /// <returns>The <see cref="CloudSmithPackageDetail"/> with details on latest firmware package for the target, or <see langword="null"/> if none is present.</returns>
        public CloudSmithPackageDetail GetLatestVersion(
            bool preview,
            string target)
        {
            CloudSmithPackageDetail result = null;

            Version latest = null;
            if (Directory.Exists(_archivePath) && !string.IsNullOrEmpty(target))
            {
                foreach (string filePath in Directory.EnumerateFiles(_archivePath, $"{target}-*{INFOFILE_EXTENSION}"))
                {
                    PersistedPackageInformation packageInformation = JsonConvert.DeserializeObject<PersistedPackageInformation>(File.ReadAllText(filePath));
                    if (packageInformation is not null && packageInformation.IsPreview == preview)
                    {
                        if (Version.TryParse(packageInformation.Version, out Version version))
                        {
                            if (latest is null || latest < version)
                            {
                                latest = version;
                                result = packageInformation;
                            }
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Download a firmware package from the repository and add it to the archive directory
        /// </summary>
        /// <param name="preview">Option for preview version.</param>
        /// <param name="platform">Platform code to use on search. This should not be <c>null</c> otherwise the firmware may not be found by nanoff.</param>
        /// <param name="targetName">Name of the target the firmware is created for. Must be assigned.</param>
        /// <param name="version">Version of the firmware to download; can be <c>null</c>.</param>
        /// <param name="keepAllVersions">Do not remove old firmware versions or targets that are no longer available for the specified <paramref name="platform"/>.</param>
        /// <param name="verbosity">VerbosityLevel to use when outputting progress and error messages.</param>
        /// <returns>The result of the task</returns>
        public async Task<ExitCodes> DownloadFirmwareFromRepository(
            bool preview,
            SupportedPlatform? platform,
            string targetName,
            string version,
            bool keepAllVersions,
            VerbosityLevel verbosity)
        {
            // Find the requested firmware in the repository
            var remoteTargets = new Dictionary<string, (CloudSmithPackageDetail package, Version version)>();
            foreach (bool isCommunityTarget in new bool[] { false, true })
            {
                if (preview && isCommunityTarget)
                {
                    continue;
                }
                foreach (CloudSmithPackageDetail targetInfo in FirmwarePackage.GetTargetList(
                                                isCommunityTarget,
                                                preview,
                                                platform,
                                                verbosity))
                {
                    if ((targetName is null || targetInfo.Name == targetName)
                        && (version is null || version == targetInfo.Version))
                    {
                        var thisVersion = new Version(targetInfo.Version);
                        if (!remoteTargets.TryGetValue(targetInfo.Name, out (CloudSmithPackageDetail package, Version version) latestVersion)
                            || latestVersion.version < thisVersion)
                        {
                            remoteTargets[targetInfo.Name] = (targetInfo, thisVersion);
                            if (targetName is not null && version is not null)
                            {
                                break;
                            }
                        }
                    }
                }
                if (targetName is not null && version is not null && remoteTargets.Count > 0)
                {
                    break;
                }
            }
            if (remoteTargets.Count == 0)
            {
                return ExitCodes.E9005;
            }

            ExitCodes result = ExitCodes.OK;

            Dictionary<string, CloudSmithPackageDetail> packagesToDelete = [];
            if (!keepAllVersions)
            {
                if (Directory.Exists(_archivePath))
                {
                    foreach (string filePath in Directory.EnumerateFiles(_archivePath, string.IsNullOrEmpty(targetName) ? $"*{INFOFILE_EXTENSION}" : $"{targetName}-*{INFOFILE_EXTENSION}"))
                    {
                        PersistedPackageInformation packageInformation = JsonConvert.DeserializeObject<PersistedPackageInformation>(File.ReadAllText(filePath));
                        if (packageInformation.IsPreview == preview &&
                            (platform is null || platform.Value.ToString().Equals(packageInformation.Platform, StringComparison.OrdinalIgnoreCase))
                        )
                        {
                            packagesToDelete[filePath] = packageInformation;
                        }
                    }
                }
            }

            foreach ((CloudSmithPackageDetail remoteTarget, Version _) in remoteTargets.Values)
            {
                // Download the firmware
                var package = new ArchiveFirmwarePackage(remoteTarget.Name, remoteTarget.Version, preview)
                {
                    Verbosity = verbosity
                };
                (ExitCodes exitCode, string fwFilePath) = await package.DownloadPackageAsync(_archivePath, null, false, false);
                if (exitCode != ExitCodes.OK)
                {
                    result = exitCode;
                    if (verbosity >= VerbosityLevel.Normal)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Red;
                        OutputWriter.WriteLine($"Could not download target {remoteTarget.Name} {remoteTarget.Version}");
                        OutputWriter.ForegroundColor = ConsoleColor.White;
                    }

                    // Do not remove old versions for this target
                    foreach (string filePath in (from p in packagesToDelete
                                                 where p.Value.Name == remoteTarget.Name
                                                 select p.Key).ToList())
                    {
                        packagesToDelete.Remove(filePath);
                    }
                    continue;
                }
                else if (verbosity > VerbosityLevel.Normal)
                {
                    OutputWriter.WriteLine($"Added target {remoteTarget.Name} {remoteTarget.Version} to the archive");
                }

                // Write the file with package information
                var packageInformation = new PersistedPackageInformation
                {
                    IsPreview = preview,
                    Name = remoteTarget.Name,
                    Platform = platform.ToString(),
                    Version = remoteTarget.Version,
                };
                string infoFilePath = $"{(fwFilePath.EndsWith(".zip") ? fwFilePath : Path.GetDirectoryName(fwFilePath))}{INFOFILE_EXTENSION}";
                File.WriteAllText(
                    infoFilePath,
                    JsonConvert.SerializeObject(packageInformation)
                );
                packagesToDelete.Remove(infoFilePath);
            }

            foreach (string filePath in packagesToDelete.Keys)
            {
                string packageFilePath = Path.ChangeExtension(filePath, null);
                if (File.Exists(packageFilePath))
                {
                    try
                    {
                        File.Delete(packageFilePath);
                    }
                    catch (Exception ex)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                        OutputWriter.WriteLine($"Could not delete firmware package '{Path.GetFileName(packageFilePath)}': {ex.Message}");
                        OutputWriter.ForegroundColor = ConsoleColor.White;
                        continue;
                    }
                }
                else if (Directory.Exists(packageFilePath))
                {
                    try
                    {
                        Directory.Delete(packageFilePath, true);
                    }
                    catch (Exception ex)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                        OutputWriter.WriteLine($"Could not delete firmware package '{Path.GetFileName(packageFilePath)}': {ex.Message}");
                        OutputWriter.ForegroundColor = ConsoleColor.White;
                        continue;
                    }
                }

                try
                {
                    File.Delete(filePath);
                }
                catch (Exception ex)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                    OutputWriter.WriteLine($"Could not delete info file '{Path.GetFileName(filePath)}': {ex.Message}");
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                }
            }

            return result;
        }
        #endregion

        #region Auxiliary classes that should only be used by the archive manager

        #region Persisted firmware information
        /// <summary>
        /// Class with details of a CloudSmith package. The base class is the one
        /// used for listing targets. The extra fields are required to select the
        /// correct package for the firmware that should be installed.
        /// </summary>
        private sealed class PersistedPackageInformation : CloudSmithPackageDetail
        {
            /// <summary>
            /// Platform code
            /// </summary>
            public string Platform { get; set; }

            /// <summary>
            /// Indicates whether this is a preview
            /// </summary>
            public bool IsPreview { get; set; }
        }
        #endregion

        #region Firmware package
        /// <summary>
        /// A lot of business logic for downloading the firmware is coded in the
        /// <see cref="FirmwarePackage"/> class. Unfortunately the constructor required
        /// for the archive manager is accessible only via a derived class.  
        /// </summary>
        private sealed class ArchiveFirmwarePackage : FirmwarePackage
        {
            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="targetName">Target name as designated in the repositories.</param>
            /// <param name="fwVersion">The firmware version.</param>
            /// <param name="preview">Whether to use preview versions.</param>
            internal ArchiveFirmwarePackage(
                string targetName,
                string fwVersion,
                bool preview)
                : base(targetName, fwVersion, preview)
            {
            }
        }
        #endregion

        #endregion
    }
}
