//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System.Threading.Tasks;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Interface for processing platform-specific operations.
    /// </summary>
    public interface IManager
    {
        /// <summary>
        /// Process operations depending on options provided.
        /// </summary>
        /// <returns>Return an <see cref="ExitCodes"/> value.</returns>
        Task<ExitCodes> ProcessAsync();
    }
}
