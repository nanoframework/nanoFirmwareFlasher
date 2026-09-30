// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Telemetry entry point for the firmware flasher library.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The library only instruments through the .NET in-box logging API (<see cref="ILogger"/>) and has
    /// no dependency on any telemetry backend. Nothing is collected or sent unless the host application
    /// sets <see cref="LoggerFactory"/> (e.g. to one wired to an OpenTelemetry pipeline).
    /// </para>
    /// <para>
    /// Custom events are emitted as log records carrying the <c>microsoft.custom_event.name</c>
    /// attribute, which the Azure Monitor exporter maps to the <c>customEvents</c> table.
    /// </para>
    /// </remarks>
    public static class NanoTelemetry
    {
        /// <summary>
        /// Logger category used by the library for telemetry events.
        /// </summary>
        public const string CategoryName = "nanoFramework.Tools.FirmwareFlasher";

        /// <summary>
        /// Environment variable that disables telemetry when set to <c>1</c> or <c>true</c>.
        /// </summary>
        public const string OptOutEnvironmentVariable = "NANOFRAMEWORK_TELEMETRY_OPTOUT";

        /// <summary>
        /// Cross-tool convention (https://github.com/heliomass/ConsoleDoNotTrack) that disables telemetry when set to <c>1</c> or <c>true</c>.
        /// </summary>
        public const string DoNotTrackEnvironmentVariable = "DO_NOT_TRACK";

        internal const string CustomEventNameAttribute = "microsoft.custom_event.name";
        internal const string FirmwarePackageDownloadedEventName = "FirmwarePackageDownloaded";
        internal const string ExceptionThrownEventName = "ExceptionThrown";

        private const int MaxMessageLength = 1024;
        private const int MaxStackTraceLength = 8000;

        private static readonly AsyncLocal<ILoggerFactory> s_testLoggerFactory = new();
        private static ILoggerFactory s_loggerFactory = NullLoggerFactory.Instance;

        /// <summary>
        /// Logger factory used to emit telemetry events. Defaults to a no-op factory.
        /// Set by the host application to route events to its OpenTelemetry pipeline.
        /// </summary>
        public static ILoggerFactory LoggerFactory
        {
            get => s_testLoggerFactory.Value ?? s_loggerFactory;
            set => s_loggerFactory = value ?? NullLoggerFactory.Instance;
        }

        /// <summary>
        /// Gets a value indicating whether the user has opted out of telemetry through
        /// <see cref="OptOutEnvironmentVariable"/> or <see cref="DoNotTrackEnvironmentVariable"/>.
        /// </summary>
        public static bool IsOptedOut => IsOptedOutCore(Environment.GetEnvironmentVariable);

        internal static bool IsOptedOutCore(Func<string, string> getEnvironmentVariable) =>
            IsTruthy(getEnvironmentVariable(OptOutEnvironmentVariable))
            || IsTruthy(getEnvironmentVariable(DoNotTrackEnvironmentVariable));

        /// <summary>
        /// Overrides <see cref="LoggerFactory"/> for the current async flow. For tests only; <see langword="null"/> restores the default.
        /// </summary>
        internal static void SetTestLoggerFactory(ILoggerFactory loggerFactory) => s_testLoggerFactory.Value = loggerFactory;

        /// <summary>
        /// Records that a firmware package was downloaded from the online repository.
        /// </summary>
        /// <param name="package">The package (target) name.</param>
        /// <param name="version">The package version.</param>
        internal static void FirmwarePackageDownloaded(string package, string version)
        {
            TrackEvent(
                FirmwarePackageDownloadedEventName,
                new KeyValuePair<string, object>("package", package),
                new KeyValuePair<string, object>("version", version));
        }

        /// <summary>
        /// Records an exception. Only the exception type, a scrubbed message and a scrubbed stack trace are sent
        /// (user profile paths, user name and machine name are removed).
        /// </summary>
        /// <param name="exception">The exception.</param>
        /// <param name="stage">Short, fixed identifier of where the exception was caught (e.g. <c>firmwareDownload</c>).</param>
        /// <param name="properties">Optional extra properties. Values must not contain personal data.</param>
        public static void TrackException(Exception exception, string stage, IEnumerable<KeyValuePair<string, object>> properties = null)
        {
            if (exception is null)
            {
                return;
            }

            try
            {
                var attributes = new List<KeyValuePair<string, object>>
                {
                    new("stage", stage),
                    new("exception.type", exception.GetType().FullName),
                    new("exception.message", Truncate(TelemetryScrubber.Scrub(exception.Message), MaxMessageLength)),
                    new("exception.stacktrace", Truncate(TelemetryScrubber.Scrub(exception.StackTrace), MaxStackTraceLength)),
                    new("exception.hresult", exception.HResult.ToString("X8", CultureInfo.InvariantCulture)),
                };

                Exception inner = exception.InnerException;

                if (inner is not null)
                {
                    // report the innermost exception, which is usually the root cause
                    while (inner.InnerException is not null)
                    {
                        inner = inner.InnerException;
                    }

                    attributes.Add(new("exception.inner_type", inner.GetType().FullName));
                    attributes.Add(new("exception.inner_message", Truncate(TelemetryScrubber.Scrub(inner.Message), MaxMessageLength)));
                }

                if (properties is not null)
                {
                    attributes.AddRange(properties);
                }

                TrackEvent(ExceptionThrownEventName, attributes.ToArray());
            }
            catch
            {
                // telemetry must never break the tool
            }
        }

        /// <summary>
        /// Emits a custom event (a log record with the custom event name attribute).
        /// </summary>
        internal static void TrackEvent(string name, params KeyValuePair<string, object>[] attributes)
        {
            try
            {
                ILogger logger = LoggerFactory.CreateLogger(CategoryName);

                if (!logger.IsEnabled(LogLevel.Information))
                {
                    return;
                }

                logger.Log(
                    LogLevel.Information,
                    default,
                    new TelemetryEventState(name, attributes),
                    null,
                    (state, _) => state.Name);
            }
            catch
            {
                // telemetry must never break the tool
            }
        }

        private static bool IsTruthy(string value) =>
            value is not null
            && (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase));

        private static string Truncate(string value, int maxLength) =>
            value is null || value.Length <= maxLength ? value : value.Substring(0, maxLength);

        /// <summary>
        /// Log state for a custom event. Implements <see cref="IReadOnlyList{T}"/> so that
        /// logging providers (e.g. OpenTelemetry) pick up the entries as attributes.
        /// </summary>
        internal sealed class TelemetryEventState : IReadOnlyList<KeyValuePair<string, object>>
        {
            private readonly List<KeyValuePair<string, object>> _attributes;

            public TelemetryEventState(string name, KeyValuePair<string, object>[] attributes)
            {
                Name = name;
                _attributes = new List<KeyValuePair<string, object>>(attributes.Length + 1)
                {
                    new(CustomEventNameAttribute, name),
                };

                foreach (KeyValuePair<string, object> attribute in attributes)
                {
                    if (!string.IsNullOrEmpty(attribute.Key) && attribute.Value is not null)
                    {
                        _attributes.Add(attribute);
                    }
                }
            }

            public string Name { get; }

            public int Count => _attributes.Count;

            public KeyValuePair<string, object> this[int index] => _attributes[index];

            public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => _attributes.GetEnumerator();

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

            public override string ToString() => Name;
        }
    }
}
