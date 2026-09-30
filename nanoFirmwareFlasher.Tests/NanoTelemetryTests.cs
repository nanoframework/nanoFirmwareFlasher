// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CommandLine;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFirmwareFlasher.Tests.Helpers;
using nanoFramework.Tools.FirmwareFlasher;

namespace nanoFirmwareFlasher.Tests
{
    [TestClass]
    public class NanoTelemetryTests
    {
        public TestContext TestContext { get; set; } = null!;

        #region Opt-out and environment

        [TestMethod]
        [DataRow(null, null, false)]
        [DataRow("0", null, false)]
        [DataRow("1", null, true)]
        [DataRow("true", null, true)]
        [DataRow("TRUE", null, true)]
        [DataRow(null, "1", true)]
        [DataRow(null, "true", true)]
        [DataRow(null, "0", false)]
        public void IsOptedOut_HonoursBothEnvironmentVariables(string? optOut, string? doNotTrack, bool expected)
        {
            var environment = new Dictionary<string, string?>
            {
                [NanoTelemetry.OptOutEnvironmentVariable] = optOut,
                [NanoTelemetry.DoNotTrackEnvironmentVariable] = doNotTrack,
            };

            Assert.AreEqual(expected, NanoTelemetry.IsOptedOutCore(name => environment.TryGetValue(name, out string? value) ? value : null));
        }

        [TestMethod]
        [DataRow(null, null, false)]
        [DataRow("CI", "true", true)]
        [DataRow("CI", "1", true)]
        [DataRow("CI", "false", false)]
        [DataRow("TF_BUILD", "True", true)]
        [DataRow("GITHUB_ACTIONS", "true", true)]
        [DataRow("JENKINS_URL", "http://jenkins", true)]
        public void IsRunningInCi_DetectsKnownCiSystems(string? name, string? value, bool expected)
        {
            Assert.AreEqual(expected, TelemetrySetup.IsRunningInCi(n => n == name ? value : null));
        }

        [TestMethod]
        public void Initialize_WithoutValidConnectionString_DoesNotEnableTelemetry()
        {
            Assert.IsFalse(TelemetrySetup.Initialize(null, "1.0.0"));
            Assert.IsFalse(TelemetrySetup.Initialize("TELEMETRY_CONNECTION_STRING", "1.0.0"));
            Assert.IsFalse(TelemetrySetup.IsEnabled);
        }

        [TestMethod]
        public void GetOrCreateInstallId_IsPersistedAndReused()
        {
            string filePath = Path.Combine(TestDirectoryHelper.GetTestDirectory(TestContext), "telemetry.id");

            string first = TelemetrySetup.GetOrCreateInstallId(filePath);
            string second = TelemetrySetup.GetOrCreateInstallId(filePath);

            Assert.IsTrue(Guid.TryParse(first, out _));
            Assert.AreEqual(first, second);
        }

        [TestMethod]
        public void FirstRunNotice_IsShownOncePerMajorVersion()
        {
            using var output = new OutputWriterHelper();
            string directory = TestDirectoryHelper.GetTestDirectory(TestContext);

            Assert.IsTrue(TelemetrySetup.ShowFirstRunNoticeIfNeeded(VerbosityLevel.Normal, "3.0.12-preview", true, directory));
            StringAssert.Contains(output.Output, NanoTelemetry.OptOutEnvironmentVariable);

            Assert.IsFalse(TelemetrySetup.ShowFirstRunNoticeIfNeeded(VerbosityLevel.Normal, "3.1.0", true, directory));
            Assert.IsTrue(TelemetrySetup.ShowFirstRunNoticeIfNeeded(VerbosityLevel.Normal, "4.0.0", true, directory));
        }

        [TestMethod]
        public void FirstRunNotice_IsNotShownWhenDisabledOrQuiet()
        {
            using var output = new OutputWriterHelper();
            string directory = TestDirectoryHelper.GetTestDirectory(TestContext);

            Assert.IsFalse(TelemetrySetup.ShowFirstRunNoticeIfNeeded(VerbosityLevel.Normal, "3.0.0", false, directory));
            Assert.IsFalse(TelemetrySetup.ShowFirstRunNoticeIfNeeded(VerbosityLevel.Quiet, "3.0.0", true, directory));
            Assert.AreEqual(string.Empty, output.Output);
        }

        #endregion

        #region Scrubbing

        [TestMethod]
        [DataRow(@"Could not find file 'C:\Users\jdoe\Documents\app.bin'.", @"Could not find file '~\Documents\app.bin'.")]
        [DataRow(@"Could not find file 'c:\users\JDOE\app.bin'.", @"Could not find file '~\app.bin'.")]
        [DataRow(@"Could not find file 'C:/Users/jdoe/app.bin'.", @"Could not find file '~/app.bin'.")]
        [DataRow(@"Access to 'C:\Users\someone\x.bin' denied", @"Access to 'C:\Users\<user>\x.bin' denied")]
        [DataRow(@"Can't open /home/alice/fw.bin", @"Can't open /home/<user>/fw.bin")]
        [DataRow(@"User jdoe on DESKTOP-42 failed", @"User <user> on <machine> failed")]
        [DataRow(@"Port COM3 is busy", @"Port COM3 is busy")]
        public void Scrub_RemovesPersonalData(string input, string expected)
        {
            Assert.AreEqual(expected, TelemetryScrubber.Scrub(input, @"C:\Users\jdoe", "jdoe", "DESKTOP-42"));
        }

        #endregion

        #region Library events

        [TestMethod]
        public void TrackException_EmitsScrubbedExceptionEvent()
        {
            var loggerFactory = new CapturingLoggerFactory();
            NanoTelemetry.SetTestLoggerFactory(loggerFactory);

            try
            {
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                Exception exception;

                try
                {
                    throw new InvalidOperationException(
                        $"Can't read '{Path.Combine(userProfile, "app.bin")}'",
                        new IOException("inner"));
                }
                catch (Exception ex)
                {
                    exception = ex;
                }

                NanoTelemetry.TrackException(exception, "testStage", [new("platform", "esp32")]);

                IReadOnlyDictionary<string, object> attributes = loggerFactory.Events.Single();

                Assert.AreEqual(NanoTelemetry.ExceptionThrownEventName, attributes[NanoTelemetry.CustomEventNameAttribute]);
                Assert.AreEqual("testStage", attributes["stage"]);
                Assert.AreEqual(typeof(InvalidOperationException).FullName, attributes["exception.type"]);
                Assert.AreEqual($"Can't read '{Path.Combine("~", "app.bin")}'", attributes["exception.message"]);
                Assert.AreEqual(typeof(IOException).FullName, attributes["exception.inner_type"]);
                Assert.AreEqual("esp32", attributes["platform"]);
                StringAssert.Contains((string)attributes["exception.stacktrace"], nameof(TrackException_EmitsScrubbedExceptionEvent));
            }
            finally
            {
                NanoTelemetry.SetTestLoggerFactory(null!);
            }
        }

        [TestMethod]
        public void FirmwarePackageDownloaded_OnlyRecordsPackageAndVersion()
        {
            var loggerFactory = new CapturingLoggerFactory();
            NanoTelemetry.SetTestLoggerFactory(loggerFactory);

            try
            {
                NanoTelemetry.FirmwarePackageDownloaded("ESP32_S3", "1.12.0.100");

                IReadOnlyDictionary<string, object> attributes = loggerFactory.Events.Single();

                CollectionAssert.AreEquivalent(
                    new[] { NanoTelemetry.CustomEventNameAttribute, "package", "version" },
                    attributes.Keys.ToArray());
                Assert.AreEqual(NanoTelemetry.FirmwarePackageDownloadedEventName, attributes[NanoTelemetry.CustomEventNameAttribute]);
                Assert.AreEqual("ESP32_S3", attributes["package"]);
                Assert.AreEqual("1.12.0.100", attributes["version"]);
            }
            finally
            {
                NanoTelemetry.SetTestLoggerFactory(null!);
            }
        }

        [TestMethod]
        public void TrackEvent_WithoutLoggerFactory_DoesNothing()
        {
            // default factory is a no-op: must not throw
            NanoTelemetry.FirmwarePackageDownloaded("ESP32_S3", "1.0.0");
            NanoTelemetry.TrackException(new InvalidOperationException(), "testStage");
        }

        #endregion

        #region Option capture

        [TestMethod]
        public void GetOptionTags_RecordsAllSuppliedOptions_AndOnlyAllowListedValues()
        {
            IReadOnlyDictionary<string, string> tags = GetOptionTags(
                "flash", "target", "ESP32_S3", "serialport", "COM7", "image", @"C:\Users\jdoe\nanoclr.bin", "masserase", "baud", "1500000");

            Assert.AreEqual("baud,image,masserase,serialport,target", tags[CommandTelemetry.OptionsTag]);
            Assert.AreEqual("ESP32_S3", tags["nf.opt.target"]);

            // explicitly supplied, even though it's the default value
            Assert.AreEqual("1500000", tags["nf.opt.baud"]);
            Assert.AreEqual("true", tags["nf.opt.masserase"]);

            // values that may identify the user or their files are never recorded
            Assert.AreEqual(CommandTelemetry.SetValue, tags["nf.opt.serialport"]);
            Assert.AreEqual(CommandTelemetry.SetValue, tags["nf.opt.image"]);
            Assert.IsFalse(tags.Values.Any(v => v.Contains("COM7") || v.Contains("jdoe")));

            // options not supplied aren't recorded
            Assert.IsFalse(tags.ContainsKey("nf.opt.platform"));
        }

        [TestMethod]
        public void GetOptionTags_SupportsDashedAndShortForms()
        {
            IReadOnlyDictionary<string, string> tags = GetOptionTags("list", "--targets", "--platform", "esp32", "-v", "d");

            Assert.AreEqual("platform,targets,verbosity", tags[CommandTelemetry.OptionsTag]);
            Assert.AreEqual("esp32", tags["nf.opt.platform"]);
            Assert.AreEqual("d", tags["nf.opt.verbosity"]);
            Assert.AreEqual("true", tags["nf.opt.targets"]);
        }

        [TestMethod]
        public void GetOptionTags_SubCommand_IsRecordedAsOption()
        {
            IReadOnlyDictionary<string, string> tags = GetOptionTags("cache", "clear");

            Assert.AreEqual("clear", tags[CommandTelemetry.OptionsTag]);
            Assert.AreEqual(2, tags.Count);
        }

        [TestMethod]
        [DataRow("--update", "update")]
        [DataRow("-Target", "target")]
        [DataRow(@"C:\Users\jdoe\app.bin", null)]
        [DataRow("COM3:115200", null)]
        [DataRow("", null)]
        public void SanitizeToken_KeepsOnlyKeywordLikeTokens(string token, string? expected)
        {
            Assert.AreEqual(expected, CommandTelemetry.SanitizeToken(token));
        }

        private static IReadOnlyDictionary<string, string> GetOptionTags(params string[] args)
        {
            string[] normalizedArgs = VerbTokenizer.Normalize(args);

            ParserResult<object> result = new Parser(config => config.HelpWriter = null)
                .ParseArguments<FlashOptions, DeployOptions, ListOptions, DetailsOptions, IdentifyOptions, DriversOptions, CacheOptions>(normalizedArgs);

            object options = ((Parsed<object>)result).Value;

            return CommandTelemetry.GetOptionTags(options.GetType(), options, normalizedArgs)
                .ToDictionary(t => t.Key, t => t.Value);
        }

        #endregion

        #region Test helpers

        /// <summary>
        /// Captures the attributes of every event logged through <see cref="NanoTelemetry"/>.
        /// </summary>
        internal sealed class CapturingLoggerFactory : ILoggerFactory, ILogger
        {
            public ConcurrentQueue<IReadOnlyDictionary<string, object>> EventQueue { get; } = new();

            public IReadOnlyList<IReadOnlyDictionary<string, object>> Events => EventQueue.ToList();

            public ILogger CreateLogger(string categoryName) => this;

            public void AddProvider(ILoggerProvider provider)
            {
            }

            public void Dispose()
            {
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (state is IReadOnlyList<KeyValuePair<string, object>> attributes)
                {
                    EventQueue.Enqueue(attributes.ToDictionary(a => a.Key, a => a.Value));
                }
            }
        }

        #endregion
    }

    /// <summary>
    /// End-to-end tests of the per-command activity recorded by <see cref="Program.Main"/>.
    /// </summary>
    [TestClass]
    [DoNotParallelize] // because of static variables in the programs
    public sealed class CommandTelemetryProgramTests
    {
        private const string TestSourceName = "nanoff.tests";

        private static readonly ActivitySource s_testSource = new(TestSourceName);

        [TestMethod]
        public void Main_ListPorts_RecordsVerbOptionsAndExitCode()
        {
            Activity activity = RunMain("list", "ports", "suppressnanoffversioncheck");

            Assert.AreEqual("nanoff list", activity.DisplayName);
            Assert.AreEqual(ActivityKind.Server, activity.Kind);
            Assert.AreEqual("list", activity.GetTagItem(CommandTelemetry.VerbTag));
            Assert.AreEqual("ports,suppressnanoffversioncheck", activity.GetTagItem(CommandTelemetry.OptionsTag));
            Assert.AreEqual("true", activity.GetTagItem("nf.opt.ports"));
            Assert.AreEqual(nameof(ExitCodes.OK), activity.GetTagItem(CommandTelemetry.ExitCodeTag));
            Assert.AreEqual(ActivityStatusCode.Ok, activity.Status);
        }

        [TestMethod]
        public void Main_LegacySyntax_RecordsParseErrorAndUnknownOptions()
        {
            Activity activity = RunMain("flash", "--update", "target", "ESP32_S3");

            Assert.AreEqual(CommandTelemetry.ParseErrorVerb, activity.GetTagItem(CommandTelemetry.VerbTag));
            Assert.AreEqual("flash", activity.GetTagItem(CommandTelemetry.AttemptedVerbTag));
            Assert.AreEqual("update", activity.GetTagItem(CommandTelemetry.UnknownOptionsTag));
            Assert.AreEqual("target", activity.GetTagItem(CommandTelemetry.OptionsTag));
            Assert.AreEqual(nameof(ExitCodes.E9000), activity.GetTagItem(CommandTelemetry.ExitCodeTag));
            Assert.AreEqual(ActivityStatusCode.Error, activity.Status);
        }

        [TestMethod]
        public void Main_VerbHelp_RecordsHelpVerb()
        {
            Activity activity = RunMain("flash", "help");

            Assert.AreEqual(CommandTelemetry.HelpVerb, activity.GetTagItem(CommandTelemetry.VerbTag));
            Assert.AreEqual("flash", activity.GetTagItem(CommandTelemetry.HelpVerbTag));
        }

        [TestMethod]
        public void Main_Flash_RecordsInferredPlatformWithoutPersonalValues()
        {
            // the serial port doesn't exist, so the command fails fast without touching any device
            Activity activity = RunMain("flash", "target", "ESP32_S3", "masserase", "serialport", "NANOFF_TEST_NO_PORT", "suppressnanoffversioncheck");

            Assert.AreEqual("flash", activity.GetTagItem(CommandTelemetry.VerbTag));
            Assert.AreEqual("masserase,serialport,suppressnanoffversioncheck,target", activity.GetTagItem(CommandTelemetry.OptionsTag));
            Assert.AreEqual(nameof(SupportedPlatform.esp32), activity.GetTagItem(CommandTelemetry.PlatformTag));
            Assert.AreEqual(CommandTelemetry.SetValue, activity.GetTagItem("nf.opt.serialport"));
            Assert.AreNotEqual(nameof(ExitCodes.OK), activity.GetTagItem(CommandTelemetry.ExitCodeTag));
            Assert.IsFalse(activity.TagObjects.Any(t => t.Value?.ToString()?.Contains("NANOFF_TEST_NO_PORT") == true));
        }

        [TestMethod]
        public void Main_NoArguments_RecordsNoneVerb()
        {
            Activity activity = RunMain();

            Assert.AreEqual(CommandTelemetry.NoneVerb, activity.GetTagItem(CommandTelemetry.VerbTag));
        }

        /// <summary>
        /// Runs <see cref="Program.Main"/> under a test parent activity and returns the command activity it recorded.
        /// </summary>
        private static Activity RunMain(params string[] args)
        {
            var stopped = new ConcurrentQueue<Activity>();

            using var listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == TelemetrySetup.ActivitySourceName || source.Name == TestSourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = stopped.Enqueue,
            };
            ActivitySource.AddActivityListener(listener);

            using var output = new OutputWriterHelper();
            using Activity parent = s_testSource.StartActivity("test")!;

            Program.Main(args).GetAwaiter().GetResult();

            return stopped.Single(a => a.Source.Name == TelemetrySetup.ActivitySourceName && a.TraceId == parent.TraceId);
        }
    }
}
