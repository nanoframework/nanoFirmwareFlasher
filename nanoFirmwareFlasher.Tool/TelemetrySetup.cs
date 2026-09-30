// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Wires the OpenTelemetry pipeline that exports nanoff telemetry to Azure Monitor (Application Insights).
    /// </summary>
    /// <remarks>
    /// Telemetry is only enabled when a connection string is configured and the user hasn't opted out
    /// (see <see cref="NanoTelemetry.IsOptedOut"/>). When disabled, no provider is built, no listener is attached
    /// to <see cref="Source"/> and nothing is sent.
    /// </remarks>
    internal static class TelemetrySetup
    {
        /// <summary>
        /// Name of the <see cref="ActivitySource"/> used for the per-command activity.
        /// </summary>
        internal const string ActivitySourceName = "nanoff";

        internal const string ServiceName = "nanoff";

        private const string NoticeUrl = "https://github.com/nanoframework/nanoFirmwareFlasher#telemetry";

        private static readonly string s_nanoFrameworkDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nanoFramework");

        // environment variables set by well known CI systems
        private static readonly string[] s_ciEnvironmentVariables =
        {
            "TF_BUILD",
            "GITHUB_ACTIONS",
            "GITLAB_CI",
            "JENKINS_URL",
            "TEAMCITY_VERSION",
            "APPVEYOR",
            "TRAVIS",
            "CIRCLECI",
            "BITBUCKET_BUILD_NUMBER",
            "CODEBUILD_BUILD_ID",
            "BUILDKITE",
            "DRONE",
        };

        private static TracerProvider s_tracerProvider;
        private static ILoggerFactory s_loggerFactory;

        /// <summary>
        /// The <see cref="ActivitySource"/> used for the per-command activity.
        /// </summary>
        internal static readonly ActivitySource Source = new(ActivitySourceName);

        /// <summary>
        /// Gets a value indicating whether the telemetry pipeline is active.
        /// </summary>
        internal static bool IsEnabled => s_tracerProvider is not null;

        /// <summary>
        /// Builds the telemetry pipeline, if a connection string is available and the user hasn't opted out.
        /// </summary>
        /// <param name="connectionString">Application Insights connection string.</param>
        /// <param name="version">nanoff informational version.</param>
        /// <returns><see langword="true"/> if telemetry is enabled.</returns>
        internal static bool Initialize(string connectionString, string version)
        {
            if (IsEnabled)
            {
                return true;
            }

            if (NanoTelemetry.IsOptedOut
                || string.IsNullOrWhiteSpace(connectionString)
                || connectionString.IndexOf("InstrumentationKey=", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            try
            {
                // don't let the exporter send its own SDK usage statistics
                SetEnvironmentVariableIfUnset("APPLICATIONINSIGHTS_STATSBEAT_DISABLED", "true");
                SetEnvironmentVariableIfUnset("APPLICATIONINSIGHTS_SDKSTATS_DISABLED_ALL", "true");

                KeyValuePair<string, object>[] commonAttributes = GetCommonAttributes(GetOrCreateInstallId());

                // service.instance.id is set explicitly because the exporter defaults it to the machine name
                ResourceBuilder resource = ResourceBuilder
                    .CreateEmpty()
                    .AddService(ServiceName, serviceVersion: version, autoGenerateServiceInstanceId: false, serviceInstanceId: "-");

                void configureExporter(AzureMonitorExporterOptions options)
                {
                    options.ConnectionString = connectionString;

                    // send everything, the volume is tiny (one command per run)
                    options.SamplingRatio = 1.0F;
                    options.TracesPerSecond = null;
                    options.EnableTraceBasedLogsSampler = false;

                    // none of these make sense for a short lived CLI
                    options.EnableLiveMetrics = false;
                    options.EnablePerformanceCounters = false;
                    options.EnableStandardMetrics = false;

                    // telemetry that fails to be sent is stored and retried on a later run
                    options.StorageDirectory = Path.Combine(s_nanoFrameworkDirectory, "telemetry");

                    // keep network retries short so that the tool never hangs on exit
                    options.Retry.MaxRetries = 1;
                    options.Retry.NetworkTimeout = TimeSpan.FromSeconds(10);
                }

                s_tracerProvider = Sdk.CreateTracerProviderBuilder()
                    .SetResourceBuilder(resource)
                    .AddSource(ActivitySourceName)
                    .SetSampler(new AlwaysOnSampler())
                    .AddProcessor(new CommonAttributesActivityProcessor(commonAttributes))
                    .AddAzureMonitorTraceExporter(configureExporter)
                    .Build();

                s_loggerFactory = LoggerFactory.Create(builder =>
                {
                    builder.SetMinimumLevel(LogLevel.Information);
                    builder.AddOpenTelemetry(options =>
                    {
                        options.SetResourceBuilder(resource);
                        options.AddProcessor(new CommonAttributesLogProcessor(commonAttributes));
                        options.AddAzureMonitorLogExporter(configureExporter);
                    });
                });

                NanoTelemetry.LoggerFactory = s_loggerFactory;

                return true;
            }
            catch
            {
                // telemetry is not mandatory, e.g. an invalid connection string
                Shutdown(TimeSpan.Zero);

                return false;
            }
        }

        /// <summary>
        /// Flushes pending telemetry, waiting at most <paramref name="timeout"/>, and tears down the pipeline.
        /// </summary>
        internal static void Shutdown(TimeSpan timeout)
        {
            TracerProvider tracerProvider = s_tracerProvider;
            ILoggerFactory loggerFactory = s_loggerFactory;

            s_tracerProvider = null;
            s_loggerFactory = null;
            NanoTelemetry.LoggerFactory = null;

            if (tracerProvider is null && loggerFactory is null)
            {
                return;
            }

            // disposing the providers flushes their pending batches;
            // bound the wait so a slow or absent network never hangs the tool
            Task shutdown = Task.Run(() =>
            {
                try
                {
                    tracerProvider?.Dispose();
                    loggerFactory?.Dispose();
                }
                catch
                {
                    // telemetry must never break the tool
                }
            });

            try
            {
                shutdown.Wait(timeout);
            }
            catch
            {
                // telemetry must never break the tool
            }
        }

        /// <summary>
        /// Shows, once per nanoff major version, a notice informing that telemetry is collected and how to opt out.
        /// </summary>
        internal static void ShowFirstRunNoticeIfNeeded(VerbosityLevel verbosity, string version) =>
            ShowFirstRunNoticeIfNeeded(verbosity, version, IsEnabled, s_nanoFrameworkDirectory);

        internal static bool ShowFirstRunNoticeIfNeeded(VerbosityLevel verbosity, string version, bool telemetryEnabled, string sentinelDirectory)
        {
            if (!telemetryEnabled || verbosity < VerbosityLevel.Normal)
            {
                return false;
            }

            try
            {
                string majorVersion = new string((version ?? string.Empty).TakeWhile(char.IsDigit).ToArray());
                string sentinelPath = Path.Combine(sentinelDirectory, $"telemetry-notice-v{(majorVersion.Length > 0 ? majorVersion : "0")}");

                if (File.Exists(sentinelPath))
                {
                    return false;
                }

                Directory.CreateDirectory(sentinelDirectory);
                File.WriteAllText(sentinelPath, DateTime.UtcNow.ToString("O"));
            }
            catch
            {
                // can't persist the sentinel: skip the notice rather than showing it on every run
                return false;
            }

            OutputWriter.ForegroundColor = ConsoleColor.DarkGray;
            OutputWriter.WriteLine("Telemetry: nanoff collects anonymous usage data (commands and options used, firmware packages downloaded and errors) to help improve the tool.");
            OutputWriter.WriteLine($"No file paths, serial numbers or other personal data are collected. To opt out set the environment variable {NanoTelemetry.OptOutEnvironmentVariable}=1 (or {NanoTelemetry.DoNotTrackEnvironmentVariable}=1).");
            OutputWriter.WriteLine($"Read more at {NoticeUrl}");
            OutputWriter.ForegroundColor = ConsoleColor.White;
            OutputWriter.WriteLine();

            return true;
        }

        /// <summary>
        /// Detects whether nanoff is running in a CI environment.
        /// </summary>
        internal static bool IsRunningInCi(Func<string, string> getEnvironmentVariable)
        {
            string ci = getEnvironmentVariable("CI");

            if (!string.IsNullOrEmpty(ci)
                && !ci.Equals("false", StringComparison.OrdinalIgnoreCase)
                && ci != "0")
            {
                return true;
            }

            return s_ciEnvironmentVariables.Any(name => !string.IsNullOrEmpty(getEnvironmentVariable(name)));
        }

        /// <summary>
        /// Attributes added to every telemetry item.
        /// </summary>
        internal static KeyValuePair<string, object>[] GetCommonAttributes(string installId) => new KeyValuePair<string, object>[]
        {
            // mapped by the Azure Monitor exporter to user_Id, which lights up the Users/Retention views
            new("enduser.pseudo.id", installId),
            new("os.family", GetOsFamily()),
            new("os.arch", RuntimeInformation.OSArchitecture.ToString()),
            new("dotnet.runtime", RuntimeInformation.FrameworkDescription),
            new("nf.is_ci", IsRunningInCi(Environment.GetEnvironmentVariable)),
        };

        /// <summary>
        /// Gets the anonymous install ID: a random GUID generated on first use and persisted in the user's
        /// nanoFramework folder. It's not derived from any hardware or user information.
        /// </summary>
        internal static string GetOrCreateInstallId() => GetOrCreateInstallId(Path.Combine(s_nanoFrameworkDirectory, "telemetry.id"));

        internal static string GetOrCreateInstallId(string filePath)
        {
            try
            {
                if (File.Exists(filePath)
                    && Guid.TryParse(File.ReadAllText(filePath).Trim(), out Guid existingId))
                {
                    return existingId.ToString("D");
                }

                string newId = Guid.NewGuid().ToString("D");

                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                File.WriteAllText(filePath, newId);

                return newId;
            }
            catch
            {
                // can't persist it: use a per-run ID
                return Guid.NewGuid().ToString("D");
            }
        }

        private static string GetOsFamily()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return "Windows";
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return "macOS";
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return "Linux";
            }

            return "Other";
        }

        private static void SetEnvironmentVariableIfUnset(string name, string value)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)))
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        /// <summary>
        /// Adds the common attributes to every activity.
        /// </summary>
        private sealed class CommonAttributesActivityProcessor : BaseProcessor<Activity>
        {
            private readonly KeyValuePair<string, object>[] _attributes;

            public CommonAttributesActivityProcessor(KeyValuePair<string, object>[] attributes) => _attributes = attributes;

            public override void OnStart(Activity data)
            {
                foreach (KeyValuePair<string, object> attribute in _attributes)
                {
                    data.SetTag(attribute.Key, attribute.Value);
                }
            }
        }

        /// <summary>
        /// Adds the common attributes to every log record.
        /// </summary>
        private sealed class CommonAttributesLogProcessor : BaseProcessor<LogRecord>
        {
            private readonly KeyValuePair<string, object>[] _attributes;

            public CommonAttributesLogProcessor(KeyValuePair<string, object>[] attributes) => _attributes = attributes;

            public override void OnEnd(LogRecord data)
            {
                var attributes = new List<KeyValuePair<string, object>>(data.Attributes ?? Array.Empty<KeyValuePair<string, object>>());
                attributes.AddRange(_attributes);
                data.Attributes = attributes;
            }
        }
    }
}
