//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Abstract base class that handles the download and extraction of firmware file from Cloudsmith.
    /// </summary>
    internal abstract class FirmwarePackage : IDisposable
    {
        // HttpClient is intended to be instantiated once per application, rather than per-use.
        static readonly HttpClient _cloudsmithClient;

        private const string _refTargetsDevRepo = "nanoframework-images-dev";
        private const string _refTargetsStableRepo = "nanoframework-images";
        private const string _communityTargetsRepo = "nanoframework-images-community-targets";

        private readonly string _targetName;
        private string _fwVersion;
        private readonly bool _preview;

        private const string _readmeContent = "This folder contains nanoFramework firmware files. Can safely be removed.";

        /// <summary>
        /// Path with the base location for firmware packages.
        /// </summary>
        public static string LocationPathBase
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".nanoFramework",
                    "fw_cache");
            }
        }

        /// <summary>
        /// Path with the location of the downloaded firmware.
        /// </summary>
        public string LocationPath { get; private set; }

        /// <summary>
        /// Version of the available firmware.
        /// </summary>
        public string Version { get; internal set; }

        public VerbosityLevel Verbosity { get; internal set; }

        static FirmwarePackage()
        {
            _cloudsmithClient = new HttpClient();
            _cloudsmithClient.BaseAddress = new Uri("https://api.cloudsmith.io/v1/packages/net-nanoframework/");
            _cloudsmithClient.DefaultRequestHeaders.Add("Accept", "*/*");
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="targetName">Target name as designated in the repositories.</param>
        protected FirmwarePackage(
            string targetName,
            string fwVersion,
            bool preview)
        {
            _targetName = targetName;
            _fwVersion = fwVersion;
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
            string repoName = communityTargets ? _communityTargetsRepo : preview ? _refTargetsDevRepo : _refTargetsStableRepo;

            // NOTE: the query seems to be the oposite, it should be LESS THAN.
            // this has been reported to Cloudsmith and it's being checked. Maybe need to revisit this if changes are made in their API.
            // Because new stable releases are published on a regular basis and preview very rarely, we query for stable versions published in past month and preview versions published during the past 6 months.
            string requestUri = $"{repoName}/?page_size=500&q=uploaded:'>{(preview ? "6" : "1")} month ago' {(platform.HasValue ? "AND tag:" + platform.Value : "")}";

            List<CloudSmithPackageDetail> targetPackages = new();

            if (verbosity > VerbosityLevel.Normal)
            {
                Console.ForegroundColor = ConsoleColor.White;

                Console.Write($"Listing {platform} targets from '{repoName}' repository");

                if (!communityTargets)
                {
                    if (preview)
                    {
                        Console.Write(" [PREVIEW]");
                    }
                    else
                    {
                        Console.Write(" [STABLE]");
                    }
                }

                Console.WriteLine("...");
            }

            HttpResponseMessage response = _cloudsmithClient.GetAsync(requestUri).GetAwaiter().GetResult();

            string responseBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            // check for empty array 
            if (responseBody == "[]")
            {
                if (verbosity >= VerbosityLevel.Normal)
                {
                    Console.WriteLine("");
                }

                // can't find this target
                return targetPackages;
            }

            targetPackages = JsonConvert.DeserializeObject<List<CloudSmithPackageDetail>>(responseBody);

            return targetPackages;
        }

        /// <summary>
        /// Download the firmware zip, extract this zip file, and get the firmware parts
        /// </summary>
        /// <returns>a dictionary which keys are the start addresses and the values are the complete filenames (the bin files)</returns>
        protected async System.Threading.Tasks.Task<ExitCodes> DownloadAndExtractAsync()
        {
            string fwFileName = null;

            // reference targets
            var repoName = _preview ? _refTargetsDevRepo : _refTargetsStableRepo;

            // get the firmware version if it is defined
            var fwVersion = string.IsNullOrEmpty(_fwVersion) ? "latest" : _fwVersion;

            // compose query
            string requestUri = $"{repoName}/?query=name:^{_targetName}$ version:^{fwVersion}$";

            string downloadUrl = string.Empty;

            // flag to signal if the work-flow step was successful
            bool stepSuccessful = false;

            // flag to skip download if the fw package exists and it's recent
            bool skipDownload = false;

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
                    _readmeContent);

                // set location path to target folder
                LocationPath = Path.Combine(
                    LocationPathBase,
                    _targetName);

                Directory.CreateDirectory(LocationPath);
            }
            catch
            {
                Console.WriteLine("");

                return ExitCodes.E9006;
            }

            List<FileInfo> fwFiles = new();

            if (_preview)
            {
                fwFiles = Directory.GetFiles(LocationPath)
                                   .Select(f => new FileInfo(f))
                                   .Where(f => f.Name.Contains("-preview.") && f.Extension == ".zip")
                                   .OrderByDescending(f => f.Name)
                                   .ToList();
            }
            else
            {
                fwFiles = Directory.GetFiles(LocationPath)
                                   .Select(f => new FileInfo(f))
                                   .Where(f => !f.Name.Contains("-preview.") && f.Extension == ".zip")
                                   .OrderByDescending(f => f.Name)
                                   .ToList();
            }


            if (!skipDownload)
            {
                // try to perform request
                try
                {
                    if (Verbosity >= VerbosityLevel.Normal)
                    {
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.Write($"Trying to find {_targetName} in {(_preview ? "development" : "stable")} repository...");
                    }

                    HttpResponseMessage response = await _cloudsmithClient.GetAsync(requestUri);

                    string responseBody = await response.Content.ReadAsStringAsync();

                    bool targetNotFound = false;

                    bool packageOutdated = false;
                    List<CloudsmithPackageInfo> packageInfo = null;

                    // check for empty array 
                    if (responseBody == "[]")
                    {
                        targetNotFound = true;
                    }
                    else
                    {
                        // parse response
                        packageInfo = JsonConvert.DeserializeObject<List<CloudsmithPackageInfo>>(responseBody);

                        // sanity check
                        if (packageInfo.Count() != 1)
                        {
                            Console.WriteLine("");

                            if (Verbosity >= VerbosityLevel.Normal)
                            {
                                Console.ForegroundColor = ConsoleColor.Red;

                                Console.WriteLine($"Several hits returned, expecting only one!");
                                Console.Write("Please report this issue.");

                                return ExitCodes.E9005;
                            }
                        }
                        else
                        {
                            // if no specific version was requested, use latest available
                            if (string.IsNullOrEmpty(_fwVersion))
                            {
                                _fwVersion = packageInfo.ElementAt(0).Version;
                                // grab download URL
                                downloadUrl = packageInfo.ElementAt(0).DownloadUrl;
                            }
                            else
                            {
                                //get the download Url from the Cloudsmith Package info
                                // addition check if the cloudsmith json return empty json
                                if (packageInfo is null || packageInfo.Count == 0)
                                {
                                    return ExitCodes.E9005;
                                }
                                else
                                {
                                    downloadUrl = packageInfo.Where(w => w.Version == _fwVersion).Select(s => s.DownloadUrl).FirstOrDefault();
                                }
                            }

                            // sanity check for target name matching requested
                            if (packageInfo.ElementAt(0).TargetName != _targetName)
                            {
                                targetNotFound = true;

                                Console.WriteLine("");

                                if (Verbosity >= VerbosityLevel.Normal)
                                {
                                    Console.ForegroundColor = ConsoleColor.Red;

                                    Console.WriteLine($"There's a mismatch in the target name. Requested '{_targetName}' but got '{packageInfo.ElementAt(0).TargetName}'!");
                                    Console.Write("Please report this issue.");

                                    return ExitCodes.E9005;
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

                        Console.WriteLine("");

                        if (Verbosity >= VerbosityLevel.Normal)
                        {
                            // output helpful message
                            Console.ForegroundColor = ConsoleColor.Red;

                            Console.WriteLine("");
                            Console.WriteLine("*************************** ERROR **************************");
                            Console.WriteLine("Couldn't find this target in our Cloudsmith repositories!");
                            Console.WriteLine("To list the available targets use this option --listtargets.");
                            Console.WriteLine("************************************************************");
                            Console.WriteLine("");

                            Console.ForegroundColor = ConsoleColor.White;
                        }

                        return ExitCodes.E9005;
                    }

                    if (Verbosity >= VerbosityLevel.Normal)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"OK");
                        Console.ForegroundColor = ConsoleColor.White;
                    }

                    if (packageOutdated)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Console.WriteLine();

                        Console.WriteLine("******************************* WARNING ******************************");
                        Console.WriteLine($"** This firmware package was released at {packageInfo.ElementAt(0).PackageDate.ToShortDateString()}                 **");
                        Console.WriteLine("** The target it's probably outdated.                               **");
                        Console.WriteLine("** Please check the current target names here: https://git.io/JyfuI **");
                        Console.WriteLine("**********************************************************************");

                        Console.WriteLine();
                        Console.ForegroundColor = ConsoleColor.White;
                    }

                    // set exposed property
                    Version = _fwVersion;

                    stepSuccessful = true;
                }
                catch
                {
                    // exception with download, assuming it's something with network connection or Cloudsmith API
                }
            }

            // cleanup any fw file in the folder
            var filesToDelete = Directory.EnumerateFiles(LocationPath, "*.bin").ToList();
            filesToDelete.AddRange(Directory.EnumerateFiles(LocationPath, "*.hex").ToList());
            filesToDelete.AddRange(Directory.EnumerateFiles(LocationPath, "*.s19").ToList());
            filesToDelete.AddRange(Directory.EnumerateFiles(LocationPath, "*.dfu").ToList());

            foreach (var file in filesToDelete)
            {
                File.Delete(file);
            }

            // check for file existence or download one
            if (stepSuccessful &&
                !skipDownload)
            {
                // reset flag
                stepSuccessful = false;

                fwFileName = $"{_targetName}-{_fwVersion}.zip";

                // check if we already have the file
                if (!File.Exists(
                    Path.Combine(
                        LocationPath,
                        fwFileName)))
                {
                    if (Verbosity >= VerbosityLevel.Normal)
                    {
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.Write($"Downloading firmware package...");
                    }

                    try
                    {
                        // setup and perform download request
                        using (var fwFileResponse = await _cloudsmithClient.GetAsync(downloadUrl))
                        {
                            if (fwFileResponse.IsSuccessStatusCode)
                            {
                                using var readStream = await fwFileResponse.Content.ReadAsStreamAsync();
                                using var fileStream = new FileStream(
                                    Path.Combine(LocationPath, fwFileName),
                                    FileMode.Create, FileAccess.Write);
                                await readStream.CopyToAsync(fileStream);
                            }
                            else
                            {
                                return ExitCodes.E9007;
                            }
                        }

                        if (Verbosity >= VerbosityLevel.Normal)
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine("OK");
                            Console.ForegroundColor = ConsoleColor.White;
                        }

                        stepSuccessful = true;
                    }
                    catch
                    {
                        // exception with download, assuming it's something with network connection or Cloudsmith API
                    }
                }
                else
                {
                    // file already exists
                    stepSuccessful = true;
                }
            }

            if (!stepSuccessful)
            {
                // couldn't download the fw file
                // check if there is one available

                if (fwFiles.Any())
                {
                    if (string.IsNullOrEmpty(_fwVersion))
                    {// take the 1st one
                        fwFileName = fwFiles.First().FullName;
                    }
                    else
                    {
                        string targetFileName = $"{_targetName}-{_fwVersion}.zip";
                        fwFileName = fwFiles.Where(w => w.Name == targetFileName).Select(s => s.FullName).FirstOrDefault();
                    }

                    if (string.IsNullOrEmpty(fwFileName))
                    {
                        return ExitCodes.E9007;
                    }

                    // get the version form the file name
                    var pattern = @"(\d+\.\d+\.\d+)(\.\d+|-.+)(?=\.zip)";
                    var match = Regex.Matches(fwFileName, pattern, RegexOptions.IgnoreCase);

                    // set property
                    Version = match[0].Value;

                    if (Verbosity >= VerbosityLevel.Normal)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("Using cached firmware package");
                        Console.ForegroundColor = ConsoleColor.White;
                    }
                }
                else
                {
                    // no fw file available

                    if (Verbosity >= VerbosityLevel.Normal)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("Failure to download package and couldn't find one in the cache.");
                        Console.ForegroundColor = ConsoleColor.White;
                    }

                    return ExitCodes.E9007;
                }
            }

            // got here, must have a file!

            // unzip the firmware
            if (Verbosity >= VerbosityLevel.Detailed)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write($"Extracting {Path.GetFileName(fwFileName)}...");
            }

            ZipFile.ExtractToDirectory(
                Path.Combine(LocationPath, fwFileName),
                LocationPath);

            if (Verbosity >= VerbosityLevel.Detailed)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("OK");
                Console.ForegroundColor = ConsoleColor.White;
            }

            // be nice to the user and delete any fw packages older than a month
            Directory.GetFiles(LocationPath)
                     .Select(f => new FileInfo(f))
                     .Where(f => f.Extension == ".zip" && f.LastWriteTime < DateTime.Now.AddMonths(-1))
                     .ToList()
                     .ForEach(f => f.Delete());

            if (Verbosity >= VerbosityLevel.Normal)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;

                Console.WriteLine("");
                Console.WriteLine($"Updating to {Version}");
                Console.WriteLine("");

                Console.ForegroundColor = ConsoleColor.White;
            }

            return ExitCodes.OK;
        }


        #region IDisposable Support

        private bool _disposedValue = false; // To detect redundant calls

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
        }

        #endregion
    }
}
