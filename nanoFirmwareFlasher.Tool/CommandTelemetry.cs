// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using CommandLine;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Records which command (verb) and options were used in a nanoff invocation, as tags on the per-command
    /// <see cref="Activity"/> (exported as a row in the Application Insights <c>requests</c> table).
    /// </summary>
    /// <remarks>
    /// Option <b>names</b> are always recorded. Option <b>values</b> are only recorded for the options in
    /// <see cref="s_valueAllowList"/>, which can't contain personal data. Every other option is recorded as
    /// <c>true</c> (switches) or <c>set</c> (e.g. paths, file names, serial ports, device IDs).
    /// </remarks>
    internal static class CommandTelemetry
    {
        internal const string VerbTag = "nf.verb";
        internal const string OptionsTag = "nf.options";
        internal const string OptionTagPrefix = "nf.opt.";
        internal const string PlatformTag = "nf.platform";
        internal const string HelpVerbTag = "nf.help_verb";
        internal const string AttemptedVerbTag = "nf.attempted_verb";
        internal const string UnknownVerbTag = "nf.unknown_verb";
        internal const string UnknownOptionsTag = "nf.unknown_options";
        internal const string ParseErrorsTag = "nf.parse_errors";
        internal const string ExitCodeTag = "nf.exit_code";

        internal const string NoneVerb = "none";
        internal const string HelpVerb = "help";
        internal const string VersionVerb = "version";
        internal const string ParseErrorVerb = "parseError";

        internal const string SetValue = "set";

        // Azure Monitor exporter overrides for the request name and result code
        private const string RequestNameTag = "microsoft.request.name";
        private const string RequestResultCodeTag = "microsoft.request.resultCode";

        private const int MaxValueLength = 64;
        private const int MaxTokenLength = 40;

        /// <summary>
        /// Options whose values are recorded. They identify boards, firmware and settings, never the user or their files.
        /// </summary>
        private static readonly HashSet<string> s_valueAllowList = new(StringComparer.Ordinal)
        {
            "platform",
            "target",
            "fwversion",
            "verbosity",
            "baud",
            "flashmode",
            "flashfreq",
            "partitiontablesize",
            "vcpbaud",
        };

        private static readonly Regex s_wordRegex = new("^[a-z][a-z0-9_-]*$", RegexOptions.Compiled);

        /// <summary>
        /// Starts the per-command activity. Returns <see langword="null"/> when telemetry is disabled.
        /// </summary>
        internal static Activity Start() => TelemetrySetup.Source.StartActivity(TelemetrySetup.ServiceName, ActivityKind.Server);

        /// <summary>
        /// Records an invocation without arguments.
        /// </summary>
        internal static void RecordNoArguments(Activity activity) => SetVerb(activity, NoneVerb);

        /// <summary>
        /// Records a successfully parsed verb and the options supplied with it.
        /// </summary>
        /// <param name="activity">The per-command activity.</param>
        /// <param name="verbOptions">The parsed verb options object (e.g. <see cref="FlashOptions"/>).</param>
        /// <param name="normalizedArgs">The arguments, after <see cref="VerbTokenizer.Normalize"/>.</param>
        internal static void RecordParsed(Activity activity, object verbOptions, string[] normalizedArgs)
        {
            if (activity is null || verbOptions is null)
            {
                return;
            }

            try
            {
                SetVerb(activity, GetVerbName(verbOptions.GetType()));

                foreach (KeyValuePair<string, string> tag in GetOptionTags(verbOptions.GetType(), verbOptions, normalizedArgs))
                {
                    activity.SetTag(tag.Key, tag.Value);
                }
            }
            catch
            {
                // telemetry must never break the tool
            }
        }

        /// <summary>
        /// Records a command line that couldn't be parsed, or a help/version request.
        /// </summary>
        /// <param name="activity">The per-command activity.</param>
        /// <param name="args">The raw arguments.</param>
        /// <param name="normalizedArgs">The arguments, after <see cref="VerbTokenizer.Normalize"/>.</param>
        /// <param name="errors">The parser errors.</param>
        internal static void RecordNotParsed(Activity activity, string[] args, string[] normalizedArgs, IEnumerable<Error> errors)
        {
            if (activity is null)
            {
                return;
            }

            try
            {
                List<Error> errorList = errors?.ToList() ?? new List<Error>();
                Type verbType = null;
                string verbName = null;

                if (args.Length > 0 && VerbTokenizer.KnownVerbs.TryGetValue(args[0], out verbType))
                {
                    verbName = args[0];
                }

                if (errorList.Any(e => e.Tag == ErrorType.VersionRequestedError))
                {
                    SetVerb(activity, VersionVerb);
                    return;
                }

                if (errorList.Any(e => e.Tag == ErrorType.HelpRequestedError || e.Tag == ErrorType.HelpVerbRequestedError))
                {
                    SetVerb(activity, HelpVerb);
                    activity.SetTag(HelpVerbTag, verbName);
                    return;
                }

                SetVerb(activity, ParseErrorVerb);
                activity.SetTag(AttemptedVerbTag, verbName);
                activity.SetTag(
                    ParseErrorsTag,
                    string.Join(",", errorList.Select(e => e.Tag.ToString()).Distinct().OrderBy(t => t, StringComparer.Ordinal)));

                if (verbType is not null)
                {
                    // still useful to know which (valid) options were combined when parsing failed
                    foreach (KeyValuePair<string, string> tag in GetOptionTags(verbType, null, normalizedArgs))
                    {
                        activity.SetTag(tag.Key, tag.Value);
                    }
                }

                string unknownVerb = errorList
                    .OfType<BadVerbSelectedError>()
                    .Select(e => SanitizeToken(e.Token))
                    .FirstOrDefault(t => t is not null);

                activity.SetTag(UnknownVerbTag, unknownVerb);

                string unknownOptions = string.Join(
                    ",",
                    errorList
                        .OfType<UnknownOptionError>()
                        .Select(e => SanitizeToken(e.Token))
                        .Where(t => t is not null)
                        .Distinct()
                        .OrderBy(t => t, StringComparer.Ordinal));

                activity.SetTag(UnknownOptionsTag, unknownOptions.Length > 0 ? unknownOptions : null);
            }
            catch
            {
                // telemetry must never break the tool
            }
        }

        /// <summary>
        /// Records the effective platform (explicit, or inferred from the target name or other options).
        /// </summary>
        internal static void SetPlatform(SupportedPlatform? platform)
        {
            Activity activity = Activity.Current;

            if (activity?.Source == TelemetrySetup.Source && platform.HasValue)
            {
                activity.SetTag(PlatformTag, platform.Value.ToString());
            }
        }

        /// <summary>
        /// Records the command outcome.
        /// </summary>
        internal static void Complete(Activity activity, ExitCodes exitCode)
        {
            if (activity is null)
            {
                return;
            }

            string exitCodeName = exitCode.ToString();

            activity.SetTag(ExitCodeTag, exitCodeName);
            activity.SetTag(RequestResultCodeTag, exitCodeName);
            activity.SetStatus(exitCode == ExitCodes.OK ? ActivityStatusCode.Ok : ActivityStatusCode.Error, exitCode == ExitCodes.OK ? null : exitCodeName);
        }

        /// <summary>
        /// Computes the option tags for the options explicitly supplied on the command line.
        /// </summary>
        /// <param name="verbType">The verb options type.</param>
        /// <param name="verbOptions">The parsed verb options, used to read allow-listed values. May be <see langword="null"/> (values are then not recorded).</param>
        /// <param name="normalizedArgs">The arguments, after <see cref="VerbTokenizer.Normalize"/>.</param>
        internal static IReadOnlyList<KeyValuePair<string, string>> GetOptionTags(Type verbType, object verbOptions, string[] normalizedArgs)
        {
            Dictionary<string, (string LongName, PropertyInfo Property)> optionMap = GetOptionMap(verbType);
            var supplied = new SortedDictionary<string, PropertyInfo>(StringComparer.Ordinal);

            // first token is the verb itself
            foreach (string token in (normalizedArgs ?? Array.Empty<string>()).Skip(1))
            {
                if (string.IsNullOrEmpty(token) || token[0] != '-')
                {
                    continue;
                }

                // support "--name=value" too
                string key = token.Split(new[] { '=' }, 2)[0];

                if (optionMap.TryGetValue(key, out (string LongName, PropertyInfo Property) option))
                {
                    supplied[option.LongName] = option.Property;
                }
            }

            var tags = new List<KeyValuePair<string, string>>
            {
                new(OptionsTag, string.Join(",", supplied.Keys)),
            };

            foreach (KeyValuePair<string, PropertyInfo> option in supplied)
            {
                tags.Add(new(OptionTagPrefix + option.Key, GetOptionValue(option.Key, option.Value, verbOptions)));
            }

            return tags;
        }

        /// <summary>
        /// Lower-cases a user-typed token and keeps it only if it looks like a keyword, to never record paths or other values.
        /// </summary>
        internal static string SanitizeToken(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return null;
            }

            string sanitized = token.TrimStart('-').ToLowerInvariant();

            if (sanitized.Length == 0 || sanitized.Length > MaxTokenLength || !s_wordRegex.IsMatch(sanitized))
            {
                return null;
            }

            return sanitized;
        }

        private static string GetOptionValue(string longName, PropertyInfo property, object verbOptions)
        {
            if (property.PropertyType == typeof(bool))
            {
                return "true";
            }

            if (verbOptions is null || !s_valueAllowList.Contains(longName))
            {
                return SetValue;
            }

            object value = property.GetValue(verbOptions);
            string text = value switch
            {
                null => SetValue,
                string s => s,
                IEnumerable items => string.Join(",", items.Cast<object>().Select(i => Convert.ToString(i, CultureInfo.InvariantCulture))),
                _ => Convert.ToString(value, CultureInfo.InvariantCulture),
            };

            return text.Length > MaxValueLength ? text.Substring(0, MaxValueLength) : text;
        }

        private static Dictionary<string, (string LongName, PropertyInfo Property)> GetOptionMap(Type verbType)
        {
            var map = new Dictionary<string, (string, PropertyInfo)>(StringComparer.Ordinal);

            foreach (PropertyInfo property in verbType.GetProperties())
            {
                OptionAttribute option = property.GetCustomAttribute<OptionAttribute>();

                if (option is null || string.IsNullOrEmpty(option.LongName))
                {
                    continue;
                }

                map["--" + option.LongName] = (option.LongName, property);

                if (!string.IsNullOrEmpty(option.ShortName))
                {
                    map["-" + option.ShortName] = (option.LongName, property);
                }
            }

            return map;
        }

        private static string GetVerbName(Type verbType) => verbType.GetCustomAttribute<VerbAttribute>()?.Name ?? verbType.Name;

        private static void SetVerb(Activity activity, string verb)
        {
            if (activity is null)
            {
                return;
            }

            string name = $"{TelemetrySetup.ServiceName} {verb}";

            activity.DisplayName = name;
            activity.SetTag(VerbTag, verb);
            activity.SetTag(RequestNameTag, name);
        }
    }
}
