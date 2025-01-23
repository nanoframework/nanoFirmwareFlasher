// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using nanoFramework.Tools.Debugger;
using nanoFramework.Tools.FirmwareFlasher.DeploymentHelpers;
using Newtonsoft.Json;

namespace nanoFramework.Tools.FirmwareFlasher.NetworkDeployment
{
    /// <summary>
    /// Network Deployment Configuration class.
    /// </summary>
    public class NetworkDeploymentManager
    {
        private readonly VerbosityLevel _verbosity;
        private readonly string _serialPort;
        private readonly NetworkDeploymentConfiguration _configuration;

        /// <summary>
        /// Creates an instance of NetworkDeploymentManager.
        /// </summary>
        /// <param name="configFilePath">The configuration file path.</param>
        /// <param name="originalPort">The original serial port used if none is provided in the json configuration.</param>
        /// <param name="verbosity">The verbosity level.</param>
        public NetworkDeploymentManager(string configFilePath, string originalPort, VerbosityLevel verbosity)
        {
            _configuration = JsonConvert.DeserializeObject<NetworkDeploymentConfiguration>(File.ReadAllText(configFilePath));
            _serialPort = string.IsNullOrEmpty(_configuration.SerialPort) ? originalPort : _configuration.SerialPort;
            _verbosity = verbosity;
        }

        /// <summary>
        /// Deploys async the network configuration.
        /// </summary>
        /// <returns>An ExitCode error.</returns>
        public async Task<ExitCodes> DeployAsync()
        {
            var (device, exitCode, deviceIsInInitializeState) = await DeviceHelper.ConnectDevice(_serialPort, _verbosity);

            if (exitCode != ExitCodes.OK)
            {
                return exitCode;
            }

            // check if device is still in initialized state
            if (!deviceIsInInitializeState)
            {
                // Deploy the network configuration for woreless client
                if (_configuration.WirelessClient != null)
                {
                    // Read the configuration for the wifi interface
                    var wifiConfigurations = device.DebugEngine.GetAllWireless80211Configurations();
                    if (wifiConfigurations == null || wifiConfigurations.Count == 0)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                        OutputWriter.WriteLine();
                        OutputWriter.WriteLine($"Cannot find any wireless configuration.");
                        OutputWriter.ForegroundColor = ConsoleColor.White;
                        return ExitCodes.E2002;
                    }

                    // So far, there is only 1 wifi interface
                    var wifiConfiguration = wifiConfigurations[0];
                    if (!string.IsNullOrEmpty(_configuration.WirelessClient.Authentication))
                    {
                        switch (_configuration.WirelessClient.Authentication.ToUpper())
                        {
                            case "EAP":
                                wifiConfiguration.Authentication = AuthenticationType.EAP;
                                break;
                            case "PEAP":
                                wifiConfiguration.Authentication = AuthenticationType.PEAP;
                                break;
                            case "WCN":
                                wifiConfiguration.Authentication = AuthenticationType.WCN;
                                break;
                            case "OPEN":
                                wifiConfiguration.Authentication = AuthenticationType.Open;
                                break;
                            case "SHARED":
                                wifiConfiguration.Authentication = AuthenticationType.Shared;
                                break;
                            case "WEP":
                                wifiConfiguration.Authentication = AuthenticationType.WEP;
                                break;
                            case "WPA":
                                wifiConfiguration.Authentication = AuthenticationType.WPA;
                                break;
                            case "WPA2":
                                wifiConfiguration.Authentication = AuthenticationType.WPA2;
                                break;
                            case "NONE":
                                wifiConfiguration.Authentication = AuthenticationType.None;
                                break;
                            default:
                                throw new ArgumentException($"{nameof(_configuration.WirelessClient.Authentication)} must be either: None, EAP, PEPA, WCN, Open, Shared, WEP, WPA or WPA2");
                        }
                    }

                    wifiConfiguration.Password = _configuration.WirelessClient.Password;
                    wifiConfiguration.Ssid = _configuration.WirelessClient.Ssid;

                    if (!string.IsNullOrEmpty(_configuration.WirelessClient.ConfigurationOption))
                    {
                        switch (_configuration.WirelessClient.ConfigurationOption.ToUpper())
                        {
                            case "NONE":
                                wifiConfiguration.Wireless80211Options = Wireless80211_ConfigurationOptions.None;
                                break;
                            case "DISABLE":
                                wifiConfiguration.Wireless80211Options = Wireless80211_ConfigurationOptions.Disable;
                                break;
                            case "ENABLE":
                                wifiConfiguration.Wireless80211Options = Wireless80211_ConfigurationOptions.Enable;
                                break;
                            case "AUTOCONNECT":
                                wifiConfiguration.Wireless80211Options = Wireless80211_ConfigurationOptions.AutoConnect;
                                break;
                            case "SMARTCONFIG":
                                wifiConfiguration.Wireless80211Options = Wireless80211_ConfigurationOptions.SmartConfig;
                                break;
                            default:
                                throw new ArgumentException($"{nameof(_configuration.WirelessClient.ConfigurationOption)} must be either: None, Disable, Enable, AutoConnect or SmartConfig");
                        }

                    }

                    if (!string.IsNullOrEmpty(_configuration.WirelessClient.RadioType))
                    {
                        switch (_configuration.WirelessClient.RadioType)
                        {
                            case "802.11a":
                                wifiConfiguration.Radio = RadioType._802_11a;
                                break;
                            case "802.11b":
                                wifiConfiguration.Radio = RadioType._802_11b;
                                break;
                            case "802.11g":
                                wifiConfiguration.Radio = RadioType._802_11g;
                                break;
                            case "802.11n":
                                wifiConfiguration.Radio = RadioType._802_11n;
                                break;
                            default:
                                throw new ArgumentException($"{nameof(_configuration.WirelessClient.RadioType)} must be either: 802.11a, 802.11b, 802.11g or 802.11n");
                        }
                    }

                    switch (_configuration.WirelessClient.Encryption)
                    {
                        case "WEP":
                            wifiConfiguration.Encryption = EncryptionType.WEP;
                            break;
                        case "WPA":
                            wifiConfiguration.Encryption = EncryptionType.WPA;
                            break;
                        case "WPA2":
                            wifiConfiguration.Encryption = EncryptionType.WPA2;
                            break;
                        case "WPA_PSK":
                            wifiConfiguration.Encryption = EncryptionType.WPA_PSK;
                            break;
                        case "WPA2_PSK2":
                            wifiConfiguration.Encryption = EncryptionType.WPA2_PSK2;
                            break;
                        case "Certificate":
                            wifiConfiguration.Encryption = EncryptionType.Certificate;
                            break;
                        default:
                            wifiConfiguration.Encryption = EncryptionType.None;
                            break;
                    }

                    // Read the configuration for the network interfaces
                    var networkConfigurations = device.DebugEngine.GetAllNetworkConfigurations();

                    // Find the wireless one
                    DeviceConfiguration.NetworkConfigurationProperties networkConfigurationToSave = null;
                    uint blockId = 0;
                    for (uint i = 0; i < networkConfigurations.Count; i++)
                    {
                        if (networkConfigurations[(int)i].InterfaceType == NetworkInterfaceType.Wireless80211)
                        {
                            blockId = i;
                            networkConfigurationToSave = networkConfigurations[(int)i];
                            break;
                        }
                    }

                    if (networkConfigurationToSave == null)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                        OutputWriter.WriteLine();
                        OutputWriter.WriteLine($"Cannot find any network configuration for wireless client.");
                        OutputWriter.ForegroundColor = ConsoleColor.White;
                        return ExitCodes.E2002;
                    }

                    // Checks the IP Addesses if DHCP is set IPv4 from DHCP
                    if (_configuration.WirelessClient.DhcpEnabled)
                    {
                        networkConfigurationToSave.StartupAddressMode = AddressMode.DHCP;
                        // clear remaining options
                        networkConfigurationToSave.IPv4Address = IPAddress.None;
                        networkConfigurationToSave.IPv4NetMask = IPAddress.None;
                        networkConfigurationToSave.IPv4GatewayAddress = IPAddress.None;
                    }
                    else
                    {
                        networkConfigurationToSave.StartupAddressMode = AddressMode.Static;
                        networkConfigurationToSave.IPv4Address = IPAddress.Parse(_configuration.WirelessClient.IPv4Address);
                        networkConfigurationToSave.IPv4NetMask = IPAddress.Parse(_configuration.WirelessClient.IPv4NetMask);
                        networkConfigurationToSave.IPv4GatewayAddress = IPAddress.Parse(_configuration.WirelessClient.IPv4Gateway);
                    }

                    if (networkConfigurationToSave.AutomaticDNS)
                    {
                        // clear DNS addresses
                        networkConfigurationToSave.IPv4DNSAddress1 = IPAddress.None;
                        networkConfigurationToSave.IPv4DNSAddress2 = IPAddress.None;
                    }
                    else
                    {
                        networkConfigurationToSave.IPv4DNSAddress1 = IPAddress.Parse(_configuration.WirelessClient.IPv4DNSAddress1);
                        networkConfigurationToSave.IPv4DNSAddress2 = IPAddress.Parse(_configuration.WirelessClient.IPv4DNSAddress2);
                    }

                    try
                    {
                        if (!string.IsNullOrEmpty(_configuration.WirelessClient.MacAddress))
                        {
                            networkConfigurationToSave.MacAddress = new byte[6];
                            if (_configuration.WirelessClient.MacAddress.Contains(":"))
                            {
                                var macParts = _configuration.WirelessClient.MacAddress.Split(':');
                                for (int i = 0; i < 6; i++)
                                {
                                    networkConfigurationToSave.MacAddress[i] = Convert.ToByte(macParts[i], 16);
                                }
                            }
                            else
                            {
                                if (_configuration.WirelessClient.MacAddress.Length != 12)
                                {
                                    throw new ArgumentException($"Mac address has to be 6 bytes, so 12 characters long.");
                                }

                                for (int i = 0; i < _configuration.WirelessClient.MacAddress.Length; i += 2)
                                {
                                    networkConfigurationToSave.MacAddress[i / 2] = Convert.ToByte(_configuration.WirelessClient.MacAddress.Substring(i, 2), 16);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                        OutputWriter.WriteLine();
                        OutputWriter.WriteLine($"Error converting MAC address:{ex}");
                        OutputWriter.ForegroundColor = ConsoleColor.White;
                        return ExitCodes.E2002;
                    }

                    OutputWriter.Write($"Updating network configuration...");
                    if (device.DebugEngine.UpdateDeviceConfiguration(networkConfigurationToSave, blockId) != Engine.UpdateDeviceResult.Sucess)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                        OutputWriter.WriteLine();
                        OutputWriter.WriteLine($"Error uploading network configuration.");
                        OutputWriter.ForegroundColor = ConsoleColor.White;
                        return ExitCodes.E2002;
                    }

                    OutputWriter.ForegroundColor = ConsoleColor.Green;
                    OutputWriter.WriteLine($"OK");
                    OutputWriter.ForegroundColor = ConsoleColor.White;

                    OutputWriter.Write($"Updating wireless configuration...");
                    if (device.DebugEngine.UpdateDeviceConfiguration(wifiConfiguration, 0) != Engine.UpdateDeviceResult.Sucess)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                        OutputWriter.WriteLine();
                        OutputWriter.WriteLine($"Error uploading wireless configuration.");
                        OutputWriter.ForegroundColor = ConsoleColor.White;
                        return ExitCodes.E2002;
                    }

                    OutputWriter.ForegroundColor = ConsoleColor.Green;
                    OutputWriter.WriteLine($"OK");
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                }

                // Deploy Wireless Access Point configuration if any
                if(_configuration.WirelessAccessPoint != null)
                {
                    // TODO
                }

                // Deploy certificates if any
                // First checks if we have a path or a device certificate
                if (!string.IsNullOrEmpty(_configuration.DeviceCertificatesPath) && !string.IsNullOrEmpty(_configuration.DeviceCertificates))
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                    OutputWriter.WriteLine();
                    OutputWriter.WriteLine($"Both {nameof(_configuration.DeviceCertificatesPath)} and {nameof(_configuration.DeviceCertificates)} are set. Only one can be used.");
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                    return ExitCodes.E2002;
                }

                // Checks if have a path or to the CAcertificates
                if (!string.IsNullOrEmpty(_configuration.CACertificatesPath) && !string.IsNullOrEmpty(_configuration.CACertificates))
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                    OutputWriter.WriteLine();
                    OutputWriter.WriteLine($"Both {nameof(_configuration.CACertificatesPath)} and {nameof(_configuration.CACertificates)} are set. Only one can be used.");
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                    return ExitCodes.E2002;
                }

                byte[] deviceCertificatesBytes = null;

                if (!string.IsNullOrEmpty(_configuration.DeviceCertificates))
                {
                    // Decode the Base64 encoded device certificates
                    deviceCertificatesBytes = Convert.FromBase64String(_configuration.DeviceCertificates);
                }
                else if(!string.IsNullOrEmpty(_configuration.DeviceCertificatesPath))
                {
                    // Read the device certificates from the file
                    deviceCertificatesBytes = File.ReadAllBytes(_configuration.DeviceCertificatesPath);
                }

                if (deviceCertificatesBytes!=null)
                {
                    // deploy the client certificates
                    OutputWriter.Write($"Updating client certificates...");
                    var clientCertificates = device.DebugEngine.GetAllX509DeviceCertificates();
                    clientCertificates.Clear();
                    clientCertificates.Add(new DeviceConfiguration.X509DeviceCertificatesProperties()
                    {
                        Certificate = deviceCertificatesBytes,
                    });

                    if (device.DebugEngine.UpdateDeviceConfiguration(clientCertificates, 0) != Engine.UpdateDeviceResult.Sucess)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                        OutputWriter.WriteLine();
                        OutputWriter.WriteLine($"Error uploading device certificates.");
                        OutputWriter.ForegroundColor = ConsoleColor.White;
                        return ExitCodes.E2002;
                    }

                    OutputWriter.ForegroundColor = ConsoleColor.Green;
                    OutputWriter.WriteLine($"OK");
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                }

                byte[] caCertificatesBytes = null;

                if (!string.IsNullOrEmpty(_configuration.CACertificates))
                {
                    // Decode the Base64 encoded CA certificates
                    caCertificatesBytes = Convert.FromBase64String(_configuration.CACertificates);
                }
                else if (!string.IsNullOrEmpty(_configuration.CACertificatesPath))
                {
                    // Read the CA certificates from the file
                    caCertificatesBytes = File.ReadAllBytes(_configuration.CACertificatesPath);
                }

                if (caCertificatesBytes!=null)
                {
                    // deploy the client certificates
                    OutputWriter.Write($"Updating client certificates...");
                    var caCertificates = device.DebugEngine.GetAllX509Certificates();
                    caCertificates.Clear();
                    caCertificates.Add(new DeviceConfiguration.X509CaRootBundleProperties()
                    {
                        Certificate = caCertificatesBytes,
                    });

                    if (device.DebugEngine.UpdateDeviceConfiguration(caCertificates, 0) != Engine.UpdateDeviceResult.Sucess)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                        OutputWriter.WriteLine();
                        OutputWriter.WriteLine($"Error uploading ca  certificates.");
                        OutputWriter.ForegroundColor = ConsoleColor.White;
                        return ExitCodes.E2002;
                    }

                    OutputWriter.ForegroundColor = ConsoleColor.Green;
                    OutputWriter.WriteLine($"OK");
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                }
            }
            else
            {
                return ExitCodes.E2002;
            }

            return ExitCodes.OK;
        }
    }
}
