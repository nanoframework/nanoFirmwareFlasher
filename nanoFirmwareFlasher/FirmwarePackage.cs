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
using System.Web;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Abstract base class that handles the download and extraction of firmware file from Cloudsmith.
    /// </summary>
    internal abstract class FirmwarePackage : IDisposable
    {
        // HttpClient is intended to be instantiated once per application, rather than per-use.
        static readonly HttpClient _cloudsmithClient = new();

        /// <summary>
        /// Uri of Cloudsmith API
        /// </summary>
        private const string _cloudsmithPackages = "https://api.cloudsmith.io/v1/packages/net-nanoframework";

        private const string _refTargetsDevRepo = "nanoframework-images-dev";
        private const string _refTargetsStableRepo = "nanoframework-images";
        private const string _communityTargetsRepo = "nanoframework-images-community-targets";

        private readonly string _targetName;
        private string _fwVersion;
        private readonly bool _preview;

        private const string _readmeContent = "This folder contains nanoFramework firmware files. Can safely be removed.";

        /// <summary>
        /// Path with the location of the downloaded firmware.
        /// </summary>
        public string LocationPath { get; private set; }

        /// <summary>
        /// Version of the available firmware.
        /// </summary>
        public string Version { get; internal set; }

        public VerbosityLevel Verbosity { get; internal set; }

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
        /// Download the firmware zip, extract this zip file, and get the firmware parts
        /// </summary>
        /// <returns>a dictionary which keys are the start addresses and the values are the complete filenames (the bin files)</returns>
        protected async System.Threading.Tasks.Task<ExitCodes> DownloadAndExtractAsync()
        {
            string fwFileName = null;

            // query URL
            // https://api.cloudsmith.io/v1/packages/net-nanoframework/REPO-NAME-HERE/?page=1&query=/PACKAGE-NAME-HERE latest

            // download URL
            // https://dl.cloudsmith.io/public/net-nanoframework/REPO-NAME-HERE/raw/names/PACKAGE-NAME-HERE/versions/VERSION-HERE/ST_STM32F429I_DISCOVERY-1.6.2-preview.9.zip

            // reference targets
            var repoName = _preview ? _refTargetsDevRepo : _refTargetsStableRepo;
            // get the firmware version if it is defined
            var fwVersion = string.IsNullOrEmpty(_fwVersion) ? "latest" : _fwVersion;
            string requestUri = $"{_cloudsmithPackages}/{repoName}/?page=1&query={_targetName} {fwVersion}";

            string downloadUrl = string.Empty;

            // flag to signal if the work-flow step was successful
            bool stepSuccessful = false;

            // flag to skip download if the fw package exists and it's recent
            bool skipDownload = false;

            // setup download folder
            // set download path
            LocationPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nanoFramework");

            try
            {
                // create home directory
                Directory.CreateDirectory(LocationPath);

                // add readme file
                File.WriteAllText(
                    Path.Combine(
                        LocationPath,
                        "README.txt"),
                    _readmeContent);

                // set location path to target folder
                LocationPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".nanoFramework",
                    _targetName);

                Directory.CreateDirectory(LocationPath);
            }
            catch
            {
                Console.WriteLine("");

                return ExitCodes.E9006;
            }

            var fwFiles = Directory.EnumerateFiles(LocationPath, $"{_targetName}-*.zip").OrderByDescending(f => f).ToList();

            if (fwFiles.Any())
            {
                // get file creation date (from the 1st one)
                if ((DateTime.UtcNow - File.GetLastWriteTimeUtc(fwFiles.First())).TotalHours < 4)
                {
                    // fw package has less than 4 hours
                    // skip download
                    skipDownload = true;
                }
            }

            if (!skipDownload)
            {
                // try to perform request
                try
                {
                    if (Verbosity >= VerbosityLevel.Normal)
                    {
                        Console.Write($"Trying to find {_targetName} in {(_preview ? "developement" : "stable")} repository...");
                    }

                    HttpResponseMessage response = await _cloudsmithClient.GetAsync(requestUri);

                    var responseBody = await response.Content.ReadAsStringAsync();

                    // check for empty array 
                    if (responseBody == "[]")
                    {
                        if (Verbosity >= VerbosityLevel.Normal)
                        {
                            Console.WriteLine("");
                            Console.Write($"Trying to find {_targetName} in community targets repository...");
                        }

                        // try with community targets

                        requestUri = $"{_cloudsmithPackages}/{_communityTargetsRepo}/?page=1&query={_targetName} {fwVersion}";

                        await _cloudsmithClient.GetAsync(requestUri);

                        if (responseBody == "[]")
                        {
                            if (Verbosity >= VerbosityLevel.Normal)
                            {
                                Console.WriteLine("");
                            }

                            // can't find this target
                            return ExitCodes.E9005;
                        }
                    }

                    if (Verbosity >= VerbosityLevel.Normal)
                    {
                        Console.WriteLine($"OK");
                    }

                    // parse response
                    List<CloudsmithPackageInfo> packageInfo = JsonConvert.DeserializeObject<List<CloudsmithPackageInfo>>(responseBody);

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
                        if(packageInfo is null || packageInfo.Count == 0)
                        {    
                           return ExitCodes.E9005;
                        }
                        else
                        {
                            downloadUrl = packageInfo.Where(w => w.Version == _fwVersion).Select(s => s.DownloadUrl).FirstOrDefault();
                        }
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
                            Console.WriteLine("OK");
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
                fwFiles = Directory.EnumerateFiles(LocationPath, $"{_targetName}-*.zip").OrderByDescending(f => f).ToList();

                if (fwFiles.Any())
                {
                    // take the 1st one
                    fwFileName = fwFiles.First();

                    // get the version form the file name
                    var pattern = @"(\d+\.\d+\.\d+)(\.\d+|-.+)(?=\.zip)";
                    var match = Regex.Matches(fwFileName, pattern, RegexOptions.IgnoreCase);

                    // set property
                    Version = match[0].Value;

                    if (Verbosity >= VerbosityLevel.Normal)
                    {
                        Console.WriteLine("Using cached firmware package");
                    }
                }
                else
                {
                    // no fw file available

                    if (Verbosity >= VerbosityLevel.Normal)
                    {
                        Console.WriteLine("Failure to download package and couldn't find one in the cache.");
                    }

                    return ExitCodes.E9007;
                }
            }

            // got here, must have a file!

            // unzip the firmware
            if (Verbosity >= VerbosityLevel.Detailed)
            {
                Console.Write($"Extracting {Path.GetFileName(fwFileName)}...");
            }

            ZipFile.ExtractToDirectory(
                Path.Combine(LocationPath, fwFileName),
                LocationPath);

            if (Verbosity >= VerbosityLevel.Detailed)
            {
                Console.WriteLine("OK");
            }

            // be nice to the user and delete any fw packages other than the last one
            var allFwFiles = Directory.EnumerateFiles(LocationPath, "*.zip").OrderByDescending(f => f).ToList();
            if (allFwFiles.Count > 1)
            {
                foreach (var file in allFwFiles.Skip(1))
                {
                    File.Delete(file);
                }
            }

            Console.WriteLine($"Updating to {Version}");

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
