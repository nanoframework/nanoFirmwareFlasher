using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace nanoFramework.Tools.FirmwareFlasher.FileDeployment
{
    internal class FileDeploymentConfiguration
    {
        /// <summary>
        /// Gets or sets the serial port to be used for the deployment.
        /// </summary>
        public string SerialPort { get; set; }

        /// <summary>
        /// A list of files to deploy and/or delete.
        /// </summary>
        public List<DeploymentFile> Files { get; set; }
    }
}
