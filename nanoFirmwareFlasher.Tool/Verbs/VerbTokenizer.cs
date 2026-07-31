// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CommandLine;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Rewrites the bare-word "verbs + words" command line syntax (e.g.
    /// <c>flash platform esp32 masserase</c>) into the <c>--keyword value</c> syntax
    /// that <see cref="CommandLine.Parser"/> understands (e.g.
    /// <c>flash --platform esp32 --masserase</c>), so the existing per-verb option
    /// classes can keep using standard CommandLineParser attributes.
    /// </summary>
    public static class VerbTokenizer
    {
        /// <summary>
        /// Maps each known verb word to its option class.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, Type> KnownVerbs = new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            ["flash"] = typeof(FlashOptions),
            ["deploy"] = typeof(DeployOptions),
            ["list"] = typeof(ListOptions),
            ["details"] = typeof(DetailsOptions),
            ["identify"] = typeof(IdentifyOptions),
            ["drivers"] = typeof(DriversOptions),
            ["cache"] = typeof(CacheOptions),
        };

        /// <summary>
        /// Normalizes a raw, bare-word argument list into the <c>--keyword value</c> form.
        /// </summary>
        /// <param name="args">The raw command line arguments, e.g. <c>["flash", "target", "ESP_WROVER_KIT", "masserase"]</c>.</param>
        /// <returns>
        /// The normalized arguments, e.g. <c>["flash", "--target", "ESP_WROVER_KIT", "--masserase"]</c>.
        /// The verb word itself (first token) and any token already starting with <c>-</c> are
        /// passed through unchanged. The reserved words <c>help</c> and <c>version</c> are
        /// rewritten to <c>--help</c>/<c>--version</c> wherever they appear.
        /// </returns>
        public static string[] Normalize(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                return args ?? Array.Empty<string>();
            }

            var result = new List<string>(args.Length);
            IReadOnlyDictionary<string, bool> keywords = null;
            bool verbSeen = false;

            for (int i = 0; i < args.Length; i++)
            {
                string token = args[i];

                if (token == "help")
                {
                    result.Add("--help");
                    continue;
                }

                if (token == "version")
                {
                    result.Add("--version");
                    continue;
                }

                if (!verbSeen)
                {
                    verbSeen = true;
                    result.Add(token);

                    if (KnownVerbs.TryGetValue(token, out Type optionsType))
                    {
                        keywords = GetKeywordMap(optionsType);
                    }

                    continue;
                }

                if (token.Length > 0 && token[0] == '-')
                {
                    // already in --keyword form (or a negative-looking value); pass through untouched
                    result.Add(token);
                    continue;
                }

                if (keywords != null && keywords.ContainsKey(token))
                {
                    // "backup" and "restore" take an optional value: with no following value they
                    // still need to reach CommandLineParser as either a concrete value ("backup",
                    // which always needs a real file to write to) or not at all ("restore", where
                    // omitting it is already the default behavior).
                    if (token == "backup" && !NextTokenIsValue(args, i, keywords))
                    {
                        result.Add("--backup");
                        result.Add(GenerateRandomBackupFileName());
                        continue;
                    }

                    if (token == "restore" && !NextTokenIsValue(args, i, keywords))
                    {
                        // bare "restore": no persistent path requested, same as omitting the keyword
                        continue;
                    }

                    result.Add("--" + token);
                }
                else
                {
                    // not a recognized keyword for this verb: treat as a value
                    result.Add(token);
                }
            }

            return result.ToArray();
        }

        /// <summary>
        /// Determines whether the token following <paramref name="index"/> should be consumed as
        /// the current keyword's value, as opposed to being absent, another keyword, or an
        /// already-dashed token.
        /// </summary>
        private static bool NextTokenIsValue(string[] args, int index, IReadOnlyDictionary<string, bool> keywords)
        {
            if (index + 1 >= args.Length)
            {
                return false;
            }

            string next = args[index + 1];

            if (next.Length > 0 && next[0] == '-')
            {
                return false;
            }

            if (next == "help" || next == "version")
            {
                return false;
            }

            return keywords == null || !keywords.ContainsKey(next);
        }

        /// <summary>
        /// Generates a random file name for a bare "backup" keyword (no path given).
        /// </summary>
        private static string GenerateRandomBackupFileName()
        {
            return $"esp32-backup-{Path.GetRandomFileName().Replace(".", "-")}.bin";
        }


        private static readonly ConcurrentDictionary<Type, IReadOnlyDictionary<string, bool>> s_keywordMapCache = new ConcurrentDictionary<Type, IReadOnlyDictionary<string, bool>>();

        /// <summary>
        /// Builds the keyword vocabulary for a verb's option type: every property carrying an
        /// <see cref="OptionAttribute"/>, mapped to whether it's a standalone flag (<see langword="bool"/>
        /// properties) or a keyword that consumes a following value.
        /// </summary>
        internal static IReadOnlyDictionary<string, bool> GetKeywordMap(Type optionsType)
        {
            return s_keywordMapCache.GetOrAdd(optionsType, BuildKeywordMap);
        }

        private static IReadOnlyDictionary<string, bool> BuildKeywordMap(Type optionsType)
        {
            var map = new Dictionary<string, bool>(StringComparer.Ordinal);

            foreach (PropertyInfo property in optionsType.GetProperties())
            {
                OptionAttribute option = property.GetCustomAttribute<OptionAttribute>();

                if (option == null || string.IsNullOrEmpty(option.LongName))
                {
                    continue;
                }

                map[option.LongName] = property.PropertyType == typeof(bool);
            }

            return map;
        }
    }
}
