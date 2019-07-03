//
// Copyright (c) 2019 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using CommandLine;
using System;
using System.Threading.Tasks;


//////////////////////////////////////////////////////////////////////
// This extension is required until CommandLine support async/await //
// This seems to be hanging depending on                            //
// https://github.com/commandlineparser/commandline/pull/390        //
//////////////////////////////////////////////////////////////////////

namespace nanoFramework.Tools.FirmwareFlasher.Extensions
{
    public static class CommandLineExtensions
    {
        /// <summary>
        /// Executes asynchronously <paramref name="action"/> if <see cref="CommandLine.ParserResult{T}"/> contains
        /// parsed values.
        /// </summary>
        /// <typeparam name="T">Type of the target instance built with parsed value.</typeparam>
        /// <param name="result">An <see cref="CommandLine.ParserResult{T}"/> instance.</param>
        /// <param name="action">The <see cref="Func{T, Task}"/> to execute.</param>
        /// <returns>The same <paramref name="result"/> instance as a <see cref="Task"/> instance.</returns>
        public static async Task<ParserResult<T>> WithParsedAsync<T>(this ParserResult<T> result, Func<T, Task> action)
        {
            if (result is Parsed<T> parsed)
            {
                await action(parsed.Value);
            }
            return result;
        }
    }
}
