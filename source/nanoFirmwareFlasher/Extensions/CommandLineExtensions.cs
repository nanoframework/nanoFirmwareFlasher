//
// Copyright (c) 2019 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using CommandLine;
using System;
using System.Collections.Generic;
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

        //public static async Task<ParserResult<T>> WithNotParsedAsync<T>(this Task<ParserResult<T>> task, Action<IEnumerable<Error>> action)
        //{
        //    var result = await task;
        //    if (result is NotParsed<T> notParsed)
        //    {
        //        return result.WithNotParsed(action);
        //    }
        //    return result;
        //}

        //public static async Task<ParserResult<T>> WithNotParsedAsync<T>(this ParserResult<T> result, Func<IEnumerable<Error>, Task> errorFunc)
        //{
        //    if (result is NotParsed<T> notParsed)
        //    {
        //        await errorFunc(notParsed.Errors);
        //    }
        //    return result;
        //}

        /// <summary>
        ///     Async version of the WithNotParsed taking a Task as input for dot-appending to the WithParsedAsync extension method.
        /// </summary>
        /// <typeparam name="T">Type of the target instance built with parsed value.</typeparam>
        /// <param name="task">A Task of <see cref="CommandLine.ParserResult{T}"/>.</param>
        /// <param name="errorFunc">The <see cref="Func{<IEnumerable<Error>, Task}"/> to execute.</param>
        /// <returns>The same <paramref name="task"/> instance as a <see cref="Task"/> instance.</returns>
        public static async Task<ParserResult<T>> WithNotParsedAsync<T>(this Task<ParserResult<T>> task, Func<IEnumerable<Error>, Task> errorFunc)
        {
            var result = await task;
            if (result is NotParsed<T> notParsed)
            {
                await errorFunc(notParsed.Errors);
            }
            return result;
        }

    }
}
