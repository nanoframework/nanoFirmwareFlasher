// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace nanoFramework.Tools.FirmwareFlasher.Esp32Serial
{
    /// <summary>
    /// Represents a pre-compiled stub loader image for an ESP32 chip variant.
    /// Stubs are small programs uploaded to chip RAM that provide faster flash operations.
    /// </summary>
    internal class Esp32StubImage
    {
        /// <summary>Text segment binary data (code).</summary>
        internal byte[] Text { get; }

        /// <summary>RAM address where the text segment should be loaded.</summary>
        internal uint TextStart { get; }

        /// <summary>Data segment binary data (initialized data).</summary>
        internal byte[] Data { get; }

        /// <summary>RAM address where the data segment should be loaded.</summary>
        internal uint DataStart { get; }

        /// <summary>Entry point address (where execution begins after upload).</summary>
        internal uint Entry { get; }

        internal Esp32StubImage(byte[] text, uint textStart, byte[] data, uint dataStart, uint entry)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
            TextStart = textStart;
            Data = data ?? Array.Empty<byte>();
            DataStart = dataStart;
            Entry = entry;
        }

        /// <summary>
        /// Try to load a stub image for the given chip type from embedded JSON resources.
        /// Returns null if no stub is available for this chip.
        /// </summary>
        /// <param name="chipType">Chip type string (e.g. "esp32", "esp32s3").</param>
        /// <returns>The stub image, or null if not found.</returns>
        internal static Esp32StubImage TryLoad(string chipType)
        {
            // Try embedded resource: "nanoFirmwareFlasher.Library.Esp32Serial.StubImages.stub_{chipType}.json"
            string resourceName = $"nanoFramework.Tools.FirmwareFlasher.Esp32Serial.StubImages.stub_{chipType}.json";

            var assembly = Assembly.GetExecutingAssembly();

            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    return null;
                }

                using (var reader = new StreamReader(stream))
                {
                    string json = reader.ReadToEnd();
                    return ParseJson(json);
                }
            }
        }

        /// <summary>
        /// Parse a stub image from the JSON format used by esptool/esp-flasher-stub.
        /// Format: { "text": "base64", "text_start": uint, "data": "base64", "data_start": uint, "entry": uint }
        /// </summary>
        /// <remarks>
        /// Uses simple regex parsing to avoid taking a dependency on a JSON library.
        /// </remarks>
        internal static Esp32StubImage ParseJson(string json)
        {
            byte[] text = ExtractBase64Field(json, "text");
            uint textStart = ExtractUIntField(json, "text_start");
            byte[] data = ExtractBase64Field(json, "data");
            uint dataStart = ExtractUIntField(json, "data_start");
            uint entry = ExtractUIntField(json, "entry");

            if (text == null || text.Length == 0)
            {
                throw new FormatException("Stub JSON missing or empty 'text' field.");
            }

            return new Esp32StubImage(text, textStart, data ?? Array.Empty<byte>(), dataStart, entry);
        }

        private static byte[] ExtractBase64Field(string json, string fieldName)
        {
            // Match "fieldName": "base64data"
            var match = Regex.Match(json, $"\"{Regex.Escape(fieldName)}\"\\s*:\\s*\"([A-Za-z0-9+/=]+)\"");

            if (!match.Success)
            {
                return null;
            }

            return Convert.FromBase64String(match.Groups[1].Value);
        }

        private static uint ExtractUIntField(string json, string fieldName)
        {
            // Match "fieldName": 12345 (decimal or hex)
            var match = Regex.Match(json, $"\"{Regex.Escape(fieldName)}\"\\s*:\\s*(\\d+)");

            if (!match.Success)
            {
                return 0;
            }

            // Parse as long first to handle values > int.MaxValue, then cast to uint
            return (uint)long.Parse(match.Groups[1].Value);
        }
    }
}
