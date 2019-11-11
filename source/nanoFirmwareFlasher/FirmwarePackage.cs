//
// Copyright (c) 2019 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Abstract base class that handles the download and extraction of firmware file from Bintray.
    /// </summary>
    internal abstract class FirmwarePackage : IDisposable
    {
        // HttpClient is intended to be instantiated once per application, rather than per-use.
        static HttpClient _bintrayClient = new HttpClient();

        /// <summary>
        /// Uri of Bintray API
        /// </summary>
        internal const string _bintrayApiPackages = "https://api.bintray.com/packages/nfbot";

        internal const string _refTargetsDevRepo = "nanoframework-images-dev";
        internal const string _refTargetsStableRepo = "nanoframework-images";
        internal const string _communityTargetsepo = "nanoframework-images-community-targets";

        internal string _targetName;
        internal string _fwVersion;
        internal bool _stable;

        internal const string _readmeContent = "This folder contains nanoFramework firmware files. Can safely be removed.";

        /// <summary>
        /// Path with the location of the downloaded firmware.
        /// </summary>
        public string LocationPath { get; internal set; }

        /// <summary>
        /// Version of the available firmware.
        /// </summary>
        public string Version { get; internal set; }

        public VerbosityLevel Verbosity { get; internal set; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="targetName">Target name as designated in the repositories.</param>
        protected FirmwarePackage(string targetName, string fwVersion, bool stable)
        {
            _targetName = targetName;
            _fwVersion = fwVersion;
            _stable = stable;
        }

        /// <summary>
        /// Download the firmware zip, extract this zip file, and get the firmware parts
        /// </summary>
        /// <returns>a dictionary which keys are the start addresses and the values are the complete filenames (the bin files)</returns>
        protected async System.Threading.Tasks.Task<ExitCodes> DownloadAndExtractAsync()
        {
            // reference targets
            var repoName = _stable ? _refTargetsStableRepo : _refTargetsDevRepo;
            string requestUri = $"{_bintrayApiPackages}/{repoName}/{_targetName}";

            if (Verbosity >= VerbosityLevel.Normal)
            {
                Console.Write($"Trying to find {_targetName} in {(_stable ? "stable" : "developement")} repository...");
            }

            HttpResponseMessage response = await _bintrayClient.GetAsync(requestUri);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                if (Verbosity >= VerbosityLevel.Normal)
                {
                    Console.Write($"Trying to find {_targetName} in community targets repository...");
                }

                // try with community targets
                requestUri = $"{_bintrayApiPackages}/{_communityTargetsepo}/{_targetName}";
                repoName = _communityTargetsepo;

                response = await _bintrayClient.GetAsync(requestUri);

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    // can't find this target
                    return ExitCodes.E9005;
                }
            }

            if (Verbosity >= VerbosityLevel.Normal)
            {
                Console.WriteLine($"OK");

                Console.Write($"Downloading firmware package...");
            }

            // read and parse response
            string responseBody = await response.Content.ReadAsStringAsync();
            BintrayPackageInfo packageInfo = JsonConvert.DeserializeObject<BintrayPackageInfo>(responseBody);

            // if no specific version was requested, use latest available
            if (string.IsNullOrEmpty(_fwVersion))
            {
                _fwVersion = packageInfo.LatestVersion;
            }

            // set exposed property
            Version = _fwVersion;

            // setup download folder
            try
            {
                // set download path
                LocationPath = Path.Combine(
                    Path.GetTempPath(),
                    Guid.NewGuid().ToString());

                // create directory
                Directory.CreateDirectory(LocationPath);

                if (Verbosity >= VerbosityLevel.Normal)
                {
                    Console.WriteLine("OK");
                }

                if (Verbosity >= VerbosityLevel.Detailed)
                {
                    Console.WriteLine($"Download location is {LocationPath}");
                }

                // add readme file
                File.WriteAllText(
                    Path.Combine(
                        LocationPath,
                        "README.txt"),
                    _readmeContent);
            }
            catch
            {
                Console.WriteLine("");

                return ExitCodes.E9006;
            }

            // setup and perform download request
            string fwFileName = $"{_targetName}-{_fwVersion}.zip";
            requestUri = $"https://dl.bintray.com/nfbot/{repoName}/{fwFileName}";

            using (var fwFileResponse = await _bintrayClient.GetAsync(requestUri))
            {
                if (fwFileResponse.IsSuccessStatusCode)
                {
                    using (var readStream = await fwFileResponse.Content.ReadAsStreamAsync())
                    {
                        using (var fileStream = new FileStream(
                            Path.Combine(LocationPath, fwFileName),
                            FileMode.Create, FileAccess.Write))
                        {
                            await readStream.CopyToAsync(fileStream);
                        }
                    }
                }
                else
                {
                    return ExitCodes.E9007;
                }
            }

            Console.WriteLine($"Updating to {_fwVersion}");

            // unzip the firmware
            if (Verbosity >= VerbosityLevel.Detailed)
            {
                Console.Write($"Extracting {fwFileName}...");
            }

            ZipFile.ExtractToDirectory(
                Path.Combine(LocationPath, fwFileName),
                LocationPath);

            if (Verbosity >= VerbosityLevel.Detailed)
            {
                Console.WriteLine($"OK");
            }

            return ExitCodes.OK;
        }


        #region IDisposable Support

        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
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

                disposedValue = true;
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
