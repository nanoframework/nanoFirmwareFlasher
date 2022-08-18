﻿//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Runtime.Serialization;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Error executing TI Uniflash CLI command.
    /// </summary>
    [Serializable]
    public class UniflashCliExecutionException : Exception
    {
        /// <summary>
        /// Error message from UniFlash CLI.
        /// </summary>
        public string ExecutionError;

        public UniflashCliExecutionException()
        {
        }

        public UniflashCliExecutionException(string message) : base(message)
        {
            ExecutionError = message;
        }

        public UniflashCliExecutionException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected UniflashCliExecutionException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}