// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Writes output to the console. In unit tests the output can be directed to
    /// a per-test implementation to make the library and tool testable.
    /// Parallel running of unit tests is supported in that case.
    /// </summary>
    public static class OutputWriter
    {
        private static readonly AsyncLocal<IOutputWriter> s_outputWriter = new();

        #region Properties
        /// <summary>
        /// Get or set the foreground color
        /// </summary>
        public static ConsoleColor ForegroundColor
        {
            get => s_outputWriter.Value?.ForegroundColor ?? Console.ForegroundColor;
            set
            {
                if (s_outputWriter.Value is null)
                {
                    Console.ForegroundColor = value;
                }
                else
                {
                    s_outputWriter.Value.ForegroundColor = value;
                }
            }
        }
        #endregion

        #region Methods
        /// <summary>
        /// Write a line to the standard output.
        /// </summary>
        /// <param name="text">Text to write</param>
        public static void Write(string text)
        {
            if (s_outputWriter.Value is null)
            {
                Console.Write(text);
            }
            else
            {
                s_outputWriter.Value.Write(text);
            }
        }

        /// <summary>
        /// Write a line to the standard output.
        /// </summary>
        /// <param name="text">Text to write</param>
        public static void WriteLine(string text = null)
        {
            if (s_outputWriter.Value is null)
            {
                if (text is null)
                {
                    Console.WriteLine();
                }
                else
                {
                    Console.WriteLine(text);
                }
            }
            else
            {
                if (text is not null)
                {
                    s_outputWriter.Value.Write(text);
                }
                s_outputWriter.Value.Write(Environment.NewLine);
            }
        }
        #endregion

        #region Test support
        /// <summary>
        /// Assign an alternative output writer for the current execution
        /// context and all async tasks started from this context.
        /// </summary>
        /// <param name="writer">Writer to use instead of the console. Pass <c>null</c>
        /// to start using the console again.</param>
        internal static void SetOutputWriter(IOutputWriter writer)
        {
            s_outputWriter.Value = writer;
        }

        /// <summary>
        /// Interface to be implemented by test software to capture the output of the library and tool
        /// </summary>
        internal interface IOutputWriter
        {
            /// <summary>
            /// Get or set the foreground color.
            /// </summary>
            ConsoleColor ForegroundColor
            {
                get; set;
            }

            /// <summary>
            /// Write text to the standard output.
            /// </summary>
            /// <param name="text">Text to write; can be <c>null</c>.</param>
            void Write(string text);
        }
        #endregion
    }
}
