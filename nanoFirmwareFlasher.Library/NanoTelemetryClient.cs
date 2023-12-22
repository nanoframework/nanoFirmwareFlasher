//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using Microsoft.ApplicationInsights;
using System;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Telemetry client for sending telemetry data to Application Insights.
    /// </summary>
    public class NanoTelemetryClient
    {
        private static TelemetryClient _myTelemetryClient;

        // flag to signal that the telemetry client field initialized has been processed
        private static bool notProcessed = true;

        /// <summary>
        /// Connection string for <see cref="TelemetryClient"/>.
        /// </summary>
        public static string ConnectionString;

        /// <summary>
        /// Gets the <see cref="TelemetryClient"/> to use for sending telemetry data.
        /// </summary>
        public static TelemetryClient TelemetryClient => GetTelemetryClient();

        private static TelemetryClient GetTelemetryClient()
        {
            if (notProcessed && _myTelemetryClient is null)
            {
                string optOutTelemetry = Environment.GetEnvironmentVariable("NANOFRAMEWORK_TELEMETRY_OPTOUT");

                if (!string.IsNullOrEmpty(ConnectionString)
                    && (optOutTelemetry is null || optOutTelemetry != "1"))
                {
                    // parsing the connection string could fail
                    try
                    {
                        _myTelemetryClient = new TelemetryClient(new Microsoft.ApplicationInsights.Extensibility.TelemetryConfiguration()
                        {
                            ConnectionString = ConnectionString
                        });
                    }
                    catch
                    {
                        // don't care, telemetry is not mandatory
                    };
                }

                // set flag to false to signal that the telemetry client field has been processed
                notProcessed = false;
            }

            return _myTelemetryClient;
        }
    }
}
