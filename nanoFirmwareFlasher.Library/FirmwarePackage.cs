// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.DataContracts;
using nanoFramework.Tools.Debugger;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Abstract base class that handles the download and extraction of firmware file from Cloudsmith.
    /// </summary>
    public abstract class FirmwarePackage : IDisposable
    {
        // HttpClient is intended to be instantiated once per application, rather than per-use.
        static readonly HttpClient s_cloudsmithClient;

        private const string RefTargetsDevRepo = "nanoframework-images-dev";
        private const string RefTargetsStableRepo = "nanoframework-images";
        private const string CommunityTargetsRepo = "nanoframework-images-community-targets";

        private readonly string _targetName;
        private readonly bool _preview;
        private const string ReadmeContent = "This folder contains nanoFramework firmware files. Can safely be removed.";

        private static readonly string s_defaultLocationPathBase = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".nanoFramework",
                    "fw_cache");
        private static readonly AsyncLocal<string> s_locationPathBase = new();
        private static readonly HashSet<string> s_supportedPlatforms = new HashSet<string>(Enum.GetNames(typeof(SupportedPlatform)), StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Path with the base location for firmware packages.
        /// </summary>
        public static string LocationPathBase
        {
            get => s_locationPathBase.Value is null ? s_defaultLocationPathBase : s_locationPathBase.Value;

            // The path must be assignable for testability
            internal set => s_locationPathBase.Value = value;
        }

        /// <summary>
        /// Path with the location of the downloaded firmware.
        /// </summary>
        public string LocationPath { get; private set; }

        /// <summary>
        /// Version of the available firmware.
        /// </summary>
        public string Version { get; internal set; }

        /// <summary>
        /// The verbosity level.
        /// </summary>
        public VerbosityLevel Verbosity { get; internal set; }

        /// <summary>
        /// Path to nanoBooter file. Hex format.
        /// </summary>
        /// <remarks>For the binary file please use the <see cref="NanoBooterFileBinary"/> property.</remarks>
        public string NanoBooterFile { get; internal set; }

        /// <summary>
        /// Path to nanoBooter file. Binary format.
        /// </summary>
        /// <remarks>For the HEX format file please use the <see cref="NanoBooterFile"/> property.</remarks>
        public string NanoBooterFileBinary => NanoBooterFile.Replace(".hex", ".bin");

        /// <summary>
        /// Path to nanoCLR file. Hex format.
        /// </summary>
        /// <remarks>For the binary file please use the <see cref="NanoClrFileBinary"/> property.</remarks>
        public string NanoClrFile { get; internal set; }

        /// <summary>
        /// Path to nanoCLR file. Binary format.
        /// </summary>
        /// <remarks>For the HEX format file please use the <see cref="NanoClrFile"/> property.</remarks>
        public string NanoClrFileBinary => NanoClrFile.Replace(".hex", ".bin");

        /// <summary>
        /// The address of nanoCLR in flash.
        /// </summary>
        public uint ClrStartAddress { get; internal set; }

        /// <summary>
        /// The address of nanoBooter in flash.
        /// </summary>
        public object BooterStartAddress { get; internal set; }



        static FirmwarePackage()
        {
            s_cloudsmithClient = new HttpClient
            {
                BaseAddress = new Uri("https://api.cloudsmith.io/v1/packages/net-nanoframework/")
            };
            s_cloudsmithClient.DefaultRequestHeaders.Add("Accept", "*/*");
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="nanoDevice"></param>
        protected FirmwarePackage(NanoDeviceBase nanoDevice)
        {
            _targetName = nanoDevice.TargetName;
            Version = "";
            _preview = false;
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="targetName">Target name as designated in the repositories.</param>
        /// <param name="fwVersion">The firmware version.</param>
        /// <param name="preview">Whether to use preview versions.</param>
        protected FirmwarePackage(
            string targetName,
            string fwVersion,
            bool preview)
        {
            _targetName = targetName;
            Version = fwVersion;
            _preview = preview;
        }

        /// <summary>
        /// Get a list of all available targets from Cloudsmith repository.
        /// </summary>
        /// <param name="communityTargets"><see langword="true"/> to list community targets.<see langword="false"/> to list reference targets.</param>
        /// <param name="preview">Option for preview version.</param>
        /// <param name="platform">Platform code to use on search.</param>
        /// <param name="verbosity">VerbosityLevel to use when outputting progress and error messages.</param>
        /// <returns>List of <see cref="CloudSmithPackageDetail"/> with details on target firmware packages.</returns>
        public static List<CloudSmithPackageDetail> GetTargetList(
    bool communityTargets,
    bool preview,
    SupportedPlatform? platform,
    VerbosityLevel verbosity)
        {
            string repoName = communityTargets ? CommunityTargetsRepo : preview ? RefTargetsDevRepo : RefTargetsStableRepo;

            // NOTE: the query seems to be the opposite, it should be LESS THAN.
            // this has been reported to Cloudsmith and it's being checked. Maybe need to revisit this if changes are made in their API.
            // Because new stable releases are published on a regular basis and preview very rarely, we query for stable versions published in past month and preview versions published during the past 6 months.
            // This is a paged query with page size 100 to improve performance.
            string requestUri = $"{repoName}/?page_size=100&q=uploaded:'>{(preview ? "6" : "1")} month ago' {(platform.HasValue ? "AND tag:" + platform.Value : "")}";

            List<CloudSmithPackageDetail> targetPackages = [];
            int page = 1;
            bool morePages = true;

            while (morePages)
            {
                if (verbosity > VerbosityLevel.Normal)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                    OutputWriter.Write($"Listing {platform} targets from '{repoName}' repository, page {page}...");

                    if (!communityTargets)
                    {
                        if (preview)
                        {
                            OutputWriter.Write(" [PREVIEW]");
                        }
                        else
                        {
                            OutputWriter.Write(" [STABLE]");
                        }
                    }

                    OutputWriter.WriteLine("...");
                }

                HttpResponseMessage response = s_cloudsmithClient.GetAsync($"{requestUri}&page={page}").GetAwaiter().GetResult();
                string responseBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                if (responseBody == "[]" || responseBody.Contains("\"Invalid page.\""))
                {
                    morePages = false;
                    continue;
                }

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                };

                List<CloudSmithPackageDetailJson> deserializedPackages = JsonSerializer.Deserialize<List<CloudSmithPackageDetailJson>>(responseBody, options);
                targetPackages.AddRange(from p in deserializedPackages
                                        select new CloudSmithPackageDetail()
                                        {
                                            Name = p.Name,
                                            Version = p.Version,
                                            Platform = p.Tags?.Info is null
                                                ? null
                                                : (from t in p.Tags.Info
                                                   where s_supportedPlatforms.Contains(t)
                                                   select t).FirstOrDefault(),
                                        });

                page++;
            }

            return targetPackages;
        }

        private sealed class CloudSmithPackageDetailJson : CloudSmithPackageDetail
        {
            public CloudSmithPackageDetailTagsJson Tags { get; set; }

            public sealed class CloudSmithPackageDetailTagsJson
            {
                public List<string> Info { get; set; }
            }
        }


        /// <summary>
        /// Download the firmware zip, extract this zip file, and get the firmware parts
        /// </summary>
        /// <param name="archiveDirectoryPath">Path to the archive directory where all targets are located. Pass <c>null</c> if there is no archive.
        /// If not <c>null</c>, the package will always be retrieved from the archive and never be downloaded.</param>
        /// <returns>The result of the download and extract operation</returns>
        internal async Task<ExitCodes> DownloadAndExtractAsync(string archiveDirectoryPath)
        {
            // setup download folder
            // set download path
            try
            {
                // create "home" directory
                Directory.CreateDirectory(LocationPathBase);

                // add readme file
                File.WriteAllText(
                    Path.Combine(
                        LocationPathBase,
                        "README.txt"),
                    ReadmeContent);

                // set location path to target folder
                LocationPath = Path.Combine(
                    LocationPathBase,
                    _targetName);
            }
            catch
            {
                OutputWriter.WriteLine("");

                return ExitCodes.E9006;
            }

            // download the firmware package
            (ExitCodes exitCode, string fwFilePath) = await DownloadPackageAsync(LocationPath, archiveDirectoryPath, true, true);
            if (exitCode != ExitCodes.OK)
            {
                return exitCode;
            }

            if (fwFilePath.EndsWith(".zip"))
            {
                // unzip the firmware
                if (Verbosity >= VerbosityLevel.Normal)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                    OutputWriter.Write($"Extracting {Path.GetFileName(fwFilePath)}...");
                }
                ZipFile.ExtractToDirectory(
                    fwFilePath,
                    LocationPath);
                if (Verbosity >= VerbosityLevel.Normal)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Green;
                    OutputWriter.WriteLine("OK");
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                }
            }

            // be nice to the user and delete any fw packages older than a month
            Directory.GetFiles(LocationPath)
                     .Select(f => new FileInfo(f))
                     .Where(f => f.Name != Path.GetFileName(fwFilePath) && f.Extension == Path.GetExtension(fwFilePath) && f.LastWriteTime < DateTime.Now.AddMonths(-1))
                     .ToList()
                     .ForEach(f => f.Delete());

            PostProcessDownloadAndExtract();

            if (Verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Yellow;

                OutputWriter.WriteLine("");
                OutputWriter.WriteLine($"Updating to {Version}");
                OutputWriter.WriteLine("");

                OutputWriter.ForegroundColor = ConsoleColor.White;
            }

            return ExitCodes.OK;
        }

        /// <summary>
        /// Download the firmware zip to the <paramref name="locationPath"/>.
        /// </summary>
        /// <param name="locationPath">The directory to download the zip file to</param>
        /// <param name="archiveDirectoryPath">Path to the archive directory where all targets are located. Pass <c>null</c> if there is no archive.
        /// If not <c>null</c>, the package will always be retrieved from the archive and <paramref name="useExistingIfDownloadFails"/> is <c>false</c>.</param>
        /// <param name="useExistingIfDownloadFails">If the download fails and there is a matching zip file present, use that zip instead.
        /// The match is done by version only, so pass <c>true</c> only if the <paramref name="locationPath"/> is specific for the target.</param>
        /// <param name="cleanupUnpackedFiles">Removes existing files in <paramref name="locationPath"/> with the same extensions as files that can be present in the zip file.</param>
        /// <returns>The result of the operation, and the full path to the downloaded file in case of success.</returns>
        internal async Task<(ExitCodes exitCode, string fwFilePath)> DownloadPackageAsync(string locationPath, string archiveDirectoryPath, bool useExistingIfDownloadFails, bool cleanupUnpackedFiles)
        {
            LocationPath = locationPath;
            string fwFileName = null!;
            string extension = null!;
            List<FileInfo> fwFiles = [];

            void UpdateLocationPathAndFileName(bool initial)
            {
                bool withPreview = _preview;

                if (_targetName == "WIN_DLL_nanoCLR" || _targetName == "WIN32_nanoCLR")
                {
                    fwFileName = "nanoFramework.nanoCLR.dll";
                    extension = ".dll";
                    if (Version is not null)
                    {
                        LocationPath = Path.Combine(locationPath, $"{_targetName}-{Version}{(_preview ? "-preview" : "")}");
                        initial = true;
                        withPreview = false;
                    }
                }
                else
                {
                    extension = ".zip";
                    fwFileName = $"{_targetName}-{Version}{(_preview ? "-preview" : "")}{extension}";
                }
                if (initial)
                {
                    Directory.CreateDirectory(LocationPath);

                    if (withPreview)
                    {
                        fwFiles = [.. Directory.GetFiles(LocationPath)
                                   .Select(f => new FileInfo(f))
                                   .Where(f => f.Name.Contains("-preview.") && f.Extension == extension)
                                   .OrderByDescending(f => f.Name)];
                    }
                    else
                    {
                        fwFiles = [.. Directory.GetFiles(LocationPath)
                                   .Select(f => new FileInfo(f))
                                   .Where(f => !f.Name.Contains("-preview.") && f.Extension == extension)
                                   .OrderByDescending(f => f.Name)];
                    }
                }

            }

            if (archiveDirectoryPath is not null && Version is null)
            {
                // Find the latest version in the archive directory
                var archiveManager = new FirmwareArchiveManager(archiveDirectoryPath);
                Version = archiveManager.GetLatestVersion(_preview, _targetName)?.Version;
                if (Version is null)
                {
                    // Package is considered to be not present, even if it does exist in the cache location
                    return (ExitCodes.E9015, null);
                }
            }

            // create the download folder
            try
            {
                UpdateLocationPathAndFileName(true);
            }
            catch
            {
                OutputWriter.WriteLine("");

                return (ExitCodes.E9006, null);
            }

            // flag to skip download if the fw package exists and it's recent
            // Not sure what "recent" is, version number should be enough to identify a unique version
            bool skipDownload = (from f in fwFiles
                                 where f.Name == fwFileName
                                 select f).Any();

            if (archiveDirectoryPath is not null)
            {
                // Make sure the package is present in the archive
                string archiveFileName = Path.Combine(archiveDirectoryPath, fwFileName);
                if (!File.Exists(archiveFileName))
                {
                    // Package is considered to be not present, even if it does exist in the cache location
                    return (ExitCodes.E9015, null);
                }

                if (!skipDownload)
                {
                    // Copy the package to the cache
                    File.Copy(archiveFileName, Path.Combine(LocationPath, fwFileName));
                    skipDownload = true;
                }
            }

            // flag to signal if the work-flow step was successful
            bool stepSuccessful = skipDownload;

            string downloadUrl = string.Empty;

            if (!skipDownload)
            {
                // try to get download URL
                DownloadUrlResult downloadResult = await GetDownloadUrlAsync(
                    _targetName,
                    Version,
                    _preview,
                    Verbosity);

                if (downloadResult.Outcome != ExitCodes.OK)
                {
                    return (downloadResult.Outcome, null);
                }

                downloadUrl = downloadResult.Url;

                // update with version from package about to be downloaded
                Version = downloadResult.Version;
                try
                {
                    UpdateLocationPathAndFileName(false);
                }
                catch
                {
                    return (ExitCodes.E9006, null);
                }
                skipDownload = (from f in fwFiles
                                where f.Name == fwFileName
                                select f).Any();

                stepSuccessful = !string.IsNullOrEmpty(downloadUrl);
            }

            if (cleanupUnpackedFiles)
            {
                // cleanup any fw file in the folder
                var filesToDelete = Directory.EnumerateFiles(LocationPath, "*.bin").ToList();
                filesToDelete.AddRange(Directory.EnumerateFiles(LocationPath, "*.hex").ToList());
                filesToDelete.AddRange(Directory.EnumerateFiles(LocationPath, "*.s19").ToList());
                filesToDelete.AddRange(Directory.EnumerateFiles(LocationPath, "*.dfu").ToList());
                filesToDelete.AddRange(Directory.EnumerateFiles(LocationPath, "*.csv").ToList());

                foreach (string file in filesToDelete)
                {
                    File.Delete(file);
                }
            }

            // check for file existence or download one
            if (stepSuccessful &&
                !skipDownload)
            {
                // reset flag
                stepSuccessful = false;

                // check if we already have the file
                if (Verbosity >= VerbosityLevel.Normal)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                    OutputWriter.Write($"Downloading firmware package...");
                }

                try
                {
                    // setup and perform download request
                    using (HttpResponseMessage fwFileResponse = await s_cloudsmithClient.GetAsync(downloadUrl))
                    {
                        if (fwFileResponse.IsSuccessStatusCode)
                        {
                            using Stream readStream = await fwFileResponse.Content.ReadAsStreamAsync();
                            using var fileStream = new FileStream(
                                Path.Combine(LocationPath, fwFileName),
                                FileMode.Create, FileAccess.Write);
                            await readStream.CopyToAsync(fileStream);
                        }
                        else
                        {
                            return (ExitCodes.E9007, null);
                        }
                    }

                    if (Verbosity >= VerbosityLevel.Normal)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Green;
                        OutputWriter.WriteLine("OK");
                        OutputWriter.ForegroundColor = ConsoleColor.White;
                    }

                    stepSuccessful = true;

                    // send telemetry data on successful download
                    if (NanoTelemetryClient.TelemetryClient is not null)
                    {
                        AssemblyInformationalVersionAttribute nanoffVersion = null;

                        try
                        {
                            nanoffVersion = Attribute.GetCustomAttribute(
                                     Assembly.GetEntryAssembly()!,
                                     typeof(AssemblyInformationalVersionAttribute))
                                 as AssemblyInformationalVersionAttribute;
                        }
                        catch
                        {
                            // OK to fail here, just telemetry
                        }

                        var packageTelemetry = new EventTelemetry("PackageDownloaded");
                        packageTelemetry.Properties.Add("TargetName", _targetName);
                        packageTelemetry.Properties.Add("Version", Version);
                        packageTelemetry.Properties.Add("nanoffVersion", nanoffVersion == null ? "unknown" : nanoffVersion.InformationalVersion);

                        NanoTelemetryClient.TelemetryClient.TrackEvent(packageTelemetry);
                        NanoTelemetryClient.TelemetryClient.Flush();
                    }
                }
                catch
                {
                    // exception with download, assuming it's something with network connection or Cloudsmith API
                }
            }

            if (!stepSuccessful)
            {
                // couldn't download the fw file
                // check if there is one available

                if (useExistingIfDownloadFails && fwFiles.Count != 0)
                {
                    if (string.IsNullOrEmpty(Version))
                    {// take the 1st one
                        fwFileName = fwFiles.First().FullName;
                    }
                    else
                    {
                        return (ExitCodes.E9007, null);
                    }

                    // get the version form the file name
                    string pattern = $@"(\d+\.\d+\.\d+)(\.\d+|-.+)(?=\{extension})";
                    MatchCollection match = Regex.Matches(fwFileName, pattern, RegexOptions.IgnoreCase);

                    // set property
                    Version = match[0].Value;

                    if (Verbosity > VerbosityLevel.Detailed)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                        OutputWriter.WriteLine("Using cached firmware package");
                        OutputWriter.ForegroundColor = ConsoleColor.White;
                    }
                }
                else
                {
                    // no fw file available

                    if (Verbosity > VerbosityLevel.Quiet)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Red;
                        if (useExistingIfDownloadFails)
                        {
                            OutputWriter.WriteLine("Failure to download package and couldn't find one in the cache.");
                        }
                        else
                        {
                            OutputWriter.WriteLine("Failure to download package.");
                        }
                        OutputWriter.ForegroundColor = ConsoleColor.White;
                    }

                    return (ExitCodes.E9007, null);
                }
            }

            // got here, must have a file!
            return (ExitCodes.OK, Path.Combine(LocationPath, fwFileName));
        }

        private static async Task<DownloadUrlResult> GetDownloadUrlAsync(
            string targetName,
            string fwVersion,
            bool isPreview,
            VerbosityLevel verbosity)
        {
            // reference targets
            string repoName = isPreview ? RefTargetsDevRepo : RefTargetsStableRepo;

            // get the firmware version if it is defined
            string fwVersionParam = string.IsNullOrEmpty(fwVersion) ? "latest" : fwVersion;

            string downloadUrl = string.Empty;
            string version = string.Empty;

            try
            {
                if (verbosity >= VerbosityLevel.Detailed)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                    OutputWriter.Write($"Trying to find {targetName} in {(isPreview ? "development" : "stable")} repository...");
                }

                HttpResponseMessage response = await s_cloudsmithClient.GetAsync($"{repoName}/?query=name:^{targetName}$ version:^{fwVersionParam}$");

                string responseBody = await response.Content.ReadAsStringAsync();

                bool targetNotFound = false;

                bool packageOutdated = false;
                List<CloudsmithPackageInfo> packageInfo = null;

                // check for empty array 
                if (responseBody == "[]")
                {
                    if (verbosity >= VerbosityLevel.Detailed)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                        OutputWriter.WriteLine("Not found");
                        OutputWriter.ForegroundColor = ConsoleColor.White;
                    }

                    //  try now with community targets
                    repoName = CommunityTargetsRepo;

                    if (verbosity >= VerbosityLevel.Detailed)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.White;
                        OutputWriter.Write($"Trying to find {targetName} in community targets repository...");
                    }

                    response = await s_cloudsmithClient.GetAsync($"{repoName}/?query=name:^{targetName}$ version:^{fwVersionParam}$");

                    responseBody = await response.Content.ReadAsStringAsync();
                }

                if (responseBody == "[]")
                {
                    targetNotFound = true;
                }
                else
                {
                    // parse response
                    packageInfo = JsonSerializer.Deserialize<List<CloudsmithPackageInfo>>(responseBody);

                    // sanity check
                    if (packageInfo.Count != 1)
                    {
                        OutputWriter.WriteLine("");

                        if (verbosity > VerbosityLevel.Quiet)
                        {
                            OutputWriter.ForegroundColor = ConsoleColor.Red;

                            OutputWriter.WriteLine($"Several hits returned, expecting only one!");
                            OutputWriter.Write("Please report this issue.");

                            return new DownloadUrlResult(string.Empty, string.Empty, ExitCodes.E9005);
                        }
                    }
                    else
                    {
                        // if no specific version was requested, use latest available
                        if (string.IsNullOrEmpty(fwVersion))
                        {
                            fwVersion = packageInfo.ElementAt(0).Version;
                            // grab download URL
                            downloadUrl = packageInfo.ElementAt(0).DownloadUrl;
                        }
                        else
                        {
                            //get the download Url from the Cloudsmith Package info
                            // addition check if the cloudsmith json return empty json
                            if (packageInfo is null || packageInfo.Count == 0)
                            {
                                return new DownloadUrlResult(string.Empty, string.Empty, ExitCodes.E9005);
                            }
                            else
                            {
                                downloadUrl = packageInfo.Where(w => w.Version == fwVersion).Select(s => s.DownloadUrl).FirstOrDefault();
                            }
                        }

                        // sanity check for target name matching requested
                        if (packageInfo.ElementAt(0).TargetName != targetName)
                        {
                            targetNotFound = true;

                            OutputWriter.WriteLine("");

                            if (verbosity > VerbosityLevel.Quiet)
                            {
                                OutputWriter.ForegroundColor = ConsoleColor.Red;

                                OutputWriter.WriteLine($"There's a mismatch in the target name. Requested '{targetName}' but got '{packageInfo.ElementAt(0).TargetName}'!");
                                OutputWriter.Write("Please report this issue.");

                                return new DownloadUrlResult(string.Empty, string.Empty, ExitCodes.E9005);
                            }
                        }
                        else
                        {
                            // check package published date
                            if (packageInfo.ElementAt(0).PackageDate < DateTime.UtcNow.AddMonths(-2))
                            {
                                // if older than 2 months warn user
                                packageOutdated = true;
                            }
                        }
                    }
                }

                if (targetNotFound)
                {
                    // can't find this target

                    OutputWriter.WriteLine("");

                    if (verbosity > VerbosityLevel.Quiet)
                    {
                        // output helpful message
                        OutputWriter.ForegroundColor = ConsoleColor.Red;

                        OutputWriter.WriteLine("");
                        OutputWriter.WriteLine("*************************** ERROR **************************");
                        OutputWriter.WriteLine("Couldn't find this target in our Cloudsmith repositories!");
                        OutputWriter.WriteLine("To list the available targets use this option --listtargets.");
                        OutputWriter.WriteLine("************************************************************");
                        OutputWriter.WriteLine("");

                        OutputWriter.ForegroundColor = ConsoleColor.White;
                    }

                    return new DownloadUrlResult(string.Empty, string.Empty, ExitCodes.E9005);
                }

                if (verbosity >= VerbosityLevel.Detailed)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Green;
                    OutputWriter.WriteLine($"OK");
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                }

                if (packageOutdated)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.DarkYellow;
                    OutputWriter.WriteLine();

                    OutputWriter.WriteLine("******************************* WARNING ******************************");
                    OutputWriter.WriteLine($"** This firmware package was released at {packageInfo.ElementAt(0).PackageDate.ToShortDateString()}                 **");
                    OutputWriter.WriteLine("** The target it's probably outdated.                               **");
                    OutputWriter.WriteLine("** Please check the current target names here: https://git.io/JyfuI **");
                    OutputWriter.WriteLine("**********************************************************************");

                    OutputWriter.WriteLine();
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                }

                // set exposed property
                version = fwVersion;
            }
            catch
            {
                // exception with download, assuming it's something with network connection or Cloudsmith API
            }

            return new DownloadUrlResult(downloadUrl, version, ExitCodes.OK);
        }


        #region IDisposable Support

        private bool _disposedValue = false; // To detect redundant calls

        /// <inherit/>
        protected void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    try
                    {
                        // lets tidy up the disk and delete the fw files from disk
                        // wrap on a try/catch in case something goes wrong
                        if (!string.IsNullOrEmpty(LocationPath))
                        {
                            Directory.Delete(LocationPath, true);
                        }
                    }
                    catch
                    {
                        // don't care about exceptions where deleting folder
                        // can't do anything about it
                        // worst case is that the files will be hanging there until a disk clean-up occurs
                    }
                }

                _disposedValue = true;
            }
        }

        // This code added to correctly implement the disposable pattern.
        void IDisposable.Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        internal record struct DownloadUrlResult(string Url, string Version, ExitCodes Outcome)
        {
        }

        private static uint FindStartAddressInHexFile(string hexFilePath)
        {
            uint address = 0;

            // find out what's the block start

            // do this by reading the HEX format file...
            string[] textLines = File.ReadAllLines(hexFilePath);

            // ... and decoding the start address
            string addressRecord = textLines.FirstOrDefault();
            string startAddress = string.Empty;

            // 1st line can be either:
            // 1) an Extended Segment Address Records (HEX86)
            // format ":02000004FFFFFC"
            // 2) a plain Data Record (HEX86)
            // format ":10246200464C5549442050524F46494C4500464C33"

            // perform sanity checks and...
            // ... check for Extended Segment Address Record
            if (addressRecord != null &&
                addressRecord.Length == 15 &&
                addressRecord.Substring(0, 9) == ":02000004")
            {
                startAddress = addressRecord.Substring(9, 4);

                // looking good, grab the upper 16bits
                address = (uint)int.Parse(startAddress, System.Globalization.NumberStyles.HexNumber);
                address <<= 16;

                // now the 2nd line to get the lower 16 bits of the address
                addressRecord = textLines.Skip(1).FirstOrDefault();

                // 2nd line is a Data Record
                // format ":10246200464C5549442050524F46494C4500464C33"

                // perform sanity checks
                if (addressRecord == null ||
                    addressRecord.Substring(0, 1) != ":" ||
                    addressRecord.Length < 7)
                {
                    // wrong format
                    throw new FormatException("Wrong data in nanoBooter file");
                }

                // looking good, grab the lower 16bits
                address += (uint)int.Parse(addressRecord.Substring(3, 4), System.Globalization.NumberStyles.HexNumber);
            }
            // try now with Data Record format
            else if (addressRecord != null &&
                    addressRecord.Length == 43 &&
                    addressRecord.Substring(0, 3) == ":10")
            {
                startAddress = addressRecord.Substring(3, 4);

                // looking good, grab the address
                address = (uint)int.Parse(startAddress, System.Globalization.NumberStyles.HexNumber);
            }

            // do we have a valid one?
            if (string.IsNullOrEmpty(startAddress))
            {
                // wrong format
                throw new FormatException("Wrong data in nanoBooter file");
            }

            // all good
            return address;
        }

        private void FindBooterStartAddress()
        {
            if (string.IsNullOrEmpty(NanoBooterFile))
            {
                // nothing to do here
                return;
            }

            // find out what's the booter block start
            BooterStartAddress = FirmwarePackage.FindStartAddressInHexFile(NanoBooterFile);
        }

        private void FindClrStartAddress()
        {
            if (string.IsNullOrEmpty(NanoClrFile))
            {
                // nothing to do here
                return;
            }

            // find out what's the CLR block start
            ClrStartAddress = FirmwarePackage.FindStartAddressInHexFile(NanoClrFile);
        }

        internal void PostProcessDownloadAndExtract()
        {
            NanoBooterFile = Directory.EnumerateFiles(LocationPath, "nanoBooter.hex").FirstOrDefault();
            NanoClrFile = Directory.EnumerateFiles(LocationPath, "nanoCLR.hex").FirstOrDefault();

            if (NanoBooterFile != null)
            {
                FindBooterStartAddress();
            }

            FindClrStartAddress();
        }
    }

}
