using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace nanoFramework.Tools.FirmwareFlasher.FileDeployment
{
    internal class DeploymentFile
    {
        /// <summary>
        /// Gets or sets the destination fully qualified file path including file name.
        /// </summary>
        public string DestinationFilePath { get; set; }

        /// <summary>
        /// Gets or sets the source fully qualified file path including file name.
        /// </summary>
        public string SourceFilePath { get; set; }
    }
}
