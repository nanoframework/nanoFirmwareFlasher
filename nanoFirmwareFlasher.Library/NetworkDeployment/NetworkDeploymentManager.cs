// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using nanoFramework.Tools.Debugger;
using nanoFramework.Tools.FirmwareFlasher.DeploymentHelpers;

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
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            };

            _configuration = JsonSerializer.Deserialize<NetworkDeploymentConfiguration>(File.ReadAllText(configFilePath), options);
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
                //var devConf = device.DebugEngine.GetDeviceConfiguration(new CancellationTokenSource(5000).Token);
                var devConf = device.DebugEngine.GetDeviceConfiguration(default);

                // Deploy the network configuration for woreless client
                if (_configuration.WirelessClient != null)
                {
                    // Read the configuration for the wifi interface
                    var wifiConfigurations = devConf.Wireless80211Configurations;
                    if (wifiConfigurations == null || wifiConfigurations.Count == 0)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                        OutputWriter.WriteLine();
                        OutputWriter.WriteLine($"Cannot find any wireless configuration.");
                        OutputWriter.ForegroundColor = ConsoleColor.White;
                        return ExitCodes.E2002;
                    }

                    // So far, there is only 1 wifi interface, pick the first one
                    // Later, we can add more configurations if wanted
                    var wifiConfiguration = wifiConfigurations[0];
                    PopulateWirelessConfigurationProperties(wifiConfiguration, _configuration.WirelessClient);

                    // Find the wireless network configuration
                    (var networkConfigurationToSave, var blockId) = GetNetworkConfiguration(devConf, NetworkInterfaceType.Wireless80211);

                    if (networkConfigurationToSave == null)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                        OutputWriter.WriteLine();
                        OutputWriter.WriteLine($"Cannot find any network configuration for wireless client.");
                        OutputWriter.ForegroundColor = ConsoleColor.White;
                        return ExitCodes.E2002;
                    }

                    PopulateNetworkProperties(networkConfigurationToSave, _configuration.WirelessClient);

                    try
                    {
                        if (!string.IsNullOrEmpty(_configuration.WirelessClient.MacAddress))
                        {
                            networkConfigurationToSave.MacAddress = GetMacAddress(_configuration.WirelessClient.MacAddress);
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
                if (_configuration.WirelessAccessPoint != null)
                {
                    // Read the configuration for the wifi AP interface
                    var wifiApConfigurations = devConf.WirelessAPConfigurations;
                    if (wifiApConfigurations.Count == 0)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                        OutputWriter.WriteLine();
                        OutputWriter.WriteLine($"Cannot find any wireless AP configuration.");
                        OutputWriter.ForegroundColor = ConsoleColor.White;
                        return ExitCodes.E2002;
                    }

                    var wifiApConfiguration = wifiApConfigurations[0];                                       
                    PopulateWirelessConfigurationProperties(wifiApConfiguration, _configuration.WirelessAccessPoint);

                    // Those two ones are specific to the AP, default to None
                    wifiApConfiguration.WirelessAPOptions = GetWirelessAPOptions(_configuration.WirelessAccessPoint.AccessPointOptions ?? "None");
                    wifiApConfiguration.MaxConnections = _configuration.WirelessAccessPoint.MaxConnections;

                    // Find the wireless AP one
                    (var networkConfigurationToSave, var blockId) = GetNetworkConfiguration(devConf, NetworkInterfaceType.WirelessAP);

                    if (networkConfigurationToSave == null)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                        OutputWriter.WriteLine();
                        OutputWriter.WriteLine($"Cannot find any network configuration for wireless client.");
                        OutputWriter.ForegroundColor = ConsoleColor.White;
                        return ExitCodes.E2002;
                    }

                    networkConfigurationToSave.StartupAddressMode = AddressMode.Static;
                    networkConfigurationToSave.IPv4Address = IPAddress.Parse(_configuration.WirelessAccessPoint.IPv4Address);
                    networkConfigurationToSave.IPv4NetMask = IPAddress.Parse(_configuration.WirelessAccessPoint.IPv4NetMask);
                    // We will use the IP addess of the device itself if no gateway provided
                    networkConfigurationToSave.IPv4GatewayAddress = IPAddress.Parse(string.IsNullOrEmpty(_configuration.WirelessAccessPoint.IPv4Gateway) ? _configuration.WirelessAccessPoint.IPv4Address : _configuration.WirelessClient.IPv4Gateway);

                    networkConfigurationToSave.IPv4DNSAddress1 = string.IsNullOrEmpty(_configuration.WirelessAccessPoint.IPv4DNSAddress1) ? IPAddress.None : IPAddress.Parse(_configuration.WirelessAccessPoint.IPv4DNSAddress1);
                    networkConfigurationToSave.IPv4DNSAddress2 = string.IsNullOrEmpty(_configuration.WirelessAccessPoint.IPv4DNSAddress2) ? IPAddress.None : IPAddress.Parse(_configuration.WirelessAccessPoint.IPv4DNSAddress2);

                    try
                    {
                        if (!string.IsNullOrEmpty(_configuration.WirelessAccessPoint.MacAddress))
                        {
                            networkConfigurationToSave.MacAddress = GetMacAddress(_configuration.WirelessAccessPoint.MacAddress);
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

                    OutputWriter.Write($"Updating network AP configuration...");
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

                    OutputWriter.Write($"Updating wireless AP configuration...");
                    if (device.DebugEngine.UpdateDeviceConfiguration(wifiApConfiguration, 0) != Engine.UpdateDeviceResult.Sucess)
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

                if (_configuration.Ethernet != null)
                {
                    // Find the wireless one
                    (var networkConfigurationToSave, var blockId) = GetNetworkConfiguration(devConf, NetworkInterfaceType.Ethernet);

                    if (networkConfigurationToSave == null)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                        OutputWriter.WriteLine();
                        OutputWriter.WriteLine($"Cannot find any network configuration for ethernet.");
                        OutputWriter.ForegroundColor = ConsoleColor.White;
                        return ExitCodes.E2002;
                    }

                    PopulateNetworkProperties(networkConfigurationToSave, _configuration.Ethernet);

                    try
                    {
                        if (!string.IsNullOrEmpty(_configuration.Ethernet.MacAddress))
                        {
                            networkConfigurationToSave.MacAddress = GetMacAddress(_configuration.Ethernet.MacAddress);
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

                    OutputWriter.Write($"Updating network ethernet configuration...");
                    if (device.DebugEngine.UpdateDeviceConfiguration(networkConfigurationToSave, blockId) != Engine.UpdateDeviceResult.Sucess)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                        OutputWriter.WriteLine();
                        OutputWriter.WriteLine($"Error uploading network ethernet configuration.");
                        OutputWriter.ForegroundColor = ConsoleColor.White;
                        return ExitCodes.E2002;
                    }

                    OutputWriter.ForegroundColor = ConsoleColor.Green;
                    OutputWriter.WriteLine($"OK");
                    OutputWriter.ForegroundColor = ConsoleColor.White;
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
                else if (!string.IsNullOrEmpty(_configuration.DeviceCertificatesPath))
                {
                    // Read the device certificates from the file
                    deviceCertificatesBytes = File.ReadAllBytes(_configuration.DeviceCertificatesPath);
                }
                
                if (deviceCertificatesBytes != null)
                {
                    CheckNullPemTermination(deviceCertificatesBytes);
                    // deploy the client certificates
                    OutputWriter.Write($"Updating client certificates...");
                    var clientCertificates = device.DebugEngine.GetAllX509DeviceCertificates();
                    clientCertificates.Clear();
                    clientCertificates.Add(new DeviceConfiguration.X509DeviceCertificatesProperties()
                    {
                        Certificate = deviceCertificatesBytes,
                        CertificateSize = (uint)deviceCertificatesBytes.Length,
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
                
                if (caCertificatesBytes != null)
                {
                    CheckNullPemTermination(caCertificatesBytes);
                    // deploy the client certificates
                    OutputWriter.Write($"Updating client certificates...");
                    var caCertificates = device.DebugEngine.GetAllX509Certificates();
                    caCertificates.Clear();
                    caCertificates.Add(new DeviceConfiguration.X509CaRootBundleProperties()
                    {
                        Certificate = caCertificatesBytes,
                        CertificateSize = (uint)caCertificatesBytes.Length,
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

        /// <summary>
        /// Configures the authentication for the wireless client.
        /// </summary>
        /// <param name="authentication">The authentication type as a string.</param>
        /// <returns>The authentication type.</returns>
        private AuthenticationType GetConfigureAuthentication(string authentication) => authentication.ToUpper() switch
        {
            "EAP" => AuthenticationType.EAP,
            "PEAP" => AuthenticationType.PEAP,
            "WCN" => AuthenticationType.WCN,
            "OPEN" => AuthenticationType.Open,
            "SHARED" => AuthenticationType.Shared,
            "WEP" => AuthenticationType.WEP,
            "WPA" => AuthenticationType.WPA,
            "WPA2" => AuthenticationType.WPA2,
            "NONE" => AuthenticationType.None,
            _ => throw new ArgumentException($"{nameof(authentication)} must be either: None, EAP, PEPA, WCN, Open, Shared, WEP, WPA or WPA2"),
        };

        /// <summary>
        /// Configures the wireless configuration options.
        /// </summary>
        /// <param name="configurationOpton">The configuration option as a string.</param>
        /// <returns>The cifiguraiton option.</returns>
        private Wireless80211_ConfigurationOptions GetConfigurationOptions(string configurationOpton) => configurationOpton.ToUpper() switch
        {
            "NONE" => Wireless80211_ConfigurationOptions.None,
            "DISABLE" => Wireless80211_ConfigurationOptions.Disable,
            "ENABLE" => Wireless80211_ConfigurationOptions.Enable,
            "AUTOCONNECT" => Wireless80211_ConfigurationOptions.AutoConnect,
            "SMARTCONFIG" => Wireless80211_ConfigurationOptions.SmartConfig,
            _ => throw new ArgumentException($"{nameof(_configuration.WirelessClient.ConfigurationOption)} must be either: None, Disable, Enable, AutoConnect or SmartConfig"),
        };

        /// <summary>
        /// Configures the radio type.
        /// </summary>
        /// <param name="radioType">The radio type as a string</param>
        /// <returns>The radio type.</returns>
        private RadioType GetRadioType(string radioType) => radioType.ToUpper() switch
        {
            "802.11A" => RadioType._802_11a,
            "802.11B" => RadioType._802_11b,
            "802.11G" => RadioType._802_11g,
            "802.11N" => RadioType._802_11n,
            _ => throw new ArgumentException($"{nameof(radioType)} must be either: 802.11a, 802.11b, 802.11g or 802.11n"),
        };

        /// <summary>
        /// Configures the encryption type.
        /// </summary>
        /// <param name="encryptionType">The encryption type as a string.</param>
        /// <returns>The encryption type.</returns>
        private EncryptionType GetEncryptionType(string encryptionType) => encryptionType.ToUpper() switch
        {
            "WEP" => EncryptionType.WEP,
            "WPA" => EncryptionType.WPA,
            "WPA2" => EncryptionType.WPA2,
            "WPA_PSK" => EncryptionType.WPA_PSK,
            "WPA2_PSK2" => EncryptionType.WPA2_PSK2,
            "CERTIFICATE" => EncryptionType.Certificate,
            _ => EncryptionType.None,
        };

        private WirelessAP_ConfigurationOptions GetWirelessAPOptions(string wirelessAPOptions) => wirelessAPOptions.ToUpper() switch
        {
            "NONE" => WirelessAP_ConfigurationOptions.None,
            "DISABLE" => WirelessAP_ConfigurationOptions.Disable,
            "ENABLE" => WirelessAP_ConfigurationOptions.Enable,
            "AUTOSTART" => WirelessAP_ConfigurationOptions.AutoStart,
            "HIDDENSSID" => WirelessAP_ConfigurationOptions.HiddenSSID,
            _ => throw new ArgumentException($"{nameof(wirelessAPOptions)} must be either: None, Disable, Enable, AutoStart or HiddenSSID"),
        };

        /// <summary>
        /// Gets the MAC address.
        /// </summary>
        /// <param name="macAddress">The MAC address as a string.</param>
        /// <returns>The MAC address as a byte array.</returns>
        private byte[] GetMacAddress(string macAddress)
        {
            byte[] mac = new byte[6];
            if (macAddress.Contains(":"))
            {
                var macParts = macAddress.Split(':');
                for (int i = 0; i < 6; i++)
                {
                    mac[i] = Convert.ToByte(macParts[i], 16);
                }
            }
            else
            {
                if (macAddress.Length != 12)
                {
                    throw new ArgumentException($"Mac address has to be 6 bytes, so 12 characters long.");
                }

                for (int i = 0; i < macAddress.Length; i += 2)
                {
                    mac[i / 2] = Convert.ToByte(macAddress.Substring(i, 2), 16);
                }
            }

            return mac;
        }

        /// <summary>
        /// Gets the network configuration.
        /// </summary>
        /// <param name="devConf">The device configuration.</param>
        /// <param name="networkInterfaceType">The network type to find.</param>
        /// <returns></returns>
        private (DeviceConfiguration.NetworkConfigurationProperties networkConfigurationToSave, uint blockId) GetNetworkConfiguration(DeviceConfiguration devConf, NetworkInterfaceType networkInterfaceType)
        {
            // Read the configuration for the network interfaces
            var networkConfigurations = devConf.NetworkConfigurations;
            // Find the wireless one
            DeviceConfiguration.NetworkConfigurationProperties networkConfigurationToSave = null;
            uint blockId = 0;
            for (uint i = 0; i < networkConfigurations.Count; i++)
            {
                if (networkConfigurations[(int)i].InterfaceType == networkInterfaceType)
                {
                    networkConfigurationToSave = networkConfigurations[(int)i];
                    blockId = i;
                    break;
                }
            }
            return (networkConfigurationToSave, blockId);
        }

        /// <summary>
        /// Populates the network properties.
        /// </summary>
        /// <param name="networkConfigurationToSave">The network properties to populate.</param>
        /// <param name="ethernet">The ethernet elements.</param>
        private void PopulateNetworkProperties(DeviceConfiguration.NetworkConfigurationProperties networkConfigurationToSave, Ethernet ethernet)
        {
            // Checks the IP Addesses if DHCP is set IPv4 from DHCP
            if (ethernet.DhcpEnabled)
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
                networkConfigurationToSave.IPv4Address = IPAddress.Parse(ethernet.IPv4Address);
                networkConfigurationToSave.IPv4NetMask = IPAddress.Parse(ethernet.IPv4NetMask);
                networkConfigurationToSave.IPv4GatewayAddress = IPAddress.Parse(ethernet.IPv4Gateway);
            }

            if (networkConfigurationToSave.AutomaticDNS)
            {
                // clear DNS addresses
                networkConfigurationToSave.IPv4DNSAddress1 = IPAddress.None;
                networkConfigurationToSave.IPv4DNSAddress2 = IPAddress.None;
            }
            else
            {
                networkConfigurationToSave.IPv4DNSAddress1 = IPAddress.Parse(ethernet.IPv4DNSAddress1);
                networkConfigurationToSave.IPv4DNSAddress2 = IPAddress.Parse(ethernet.IPv4DNSAddress2);
            }
        }

        /// <summary>
        /// Checks if the PEM certificate has a null termination and add it if needed.
        /// </summary>
        /// <param name="cert">The cert byte array.</param>
        private void CheckNullPemTermination(byte[] cert)
        {
            // Check if it's a PEM certificate starting with -----BEGIN CERTIFICATE-----
            if(Encoding.ASCII.GetString(cert).Contains("-----BEGIN CERTIFICATE-----"))
            {
                // Check if the last byte is a null terminator
                if (cert[cert.Length - 1] != 0)
                {
                    // Add the PEM termination
                    Array.Resize(ref cert, cert.Length + 1);
                    cert[cert.Length - 1] = 0;
                }
            }            
        }

        /// <summary>
        /// Populates the wireless properties.
        /// </summary>
        /// <param name="wifiConfiguration">The wireless property to populate</param>
        /// <param name="wirelessClient">The wireless configuration</param>
        private void PopulateWirelessConfigurationProperties(WirelessConfigurationPropertiesBase wifiConfiguration, WirelessConfiguration wirelessClient)
        {
            if (!string.IsNullOrEmpty(wirelessClient.Authentication))
            {
                wifiConfiguration.Authentication = GetConfigureAuthentication(wirelessClient.Authentication);
            }
            wifiConfiguration.Password = wirelessClient.Password ?? string.Empty;
            wifiConfiguration.Ssid = wirelessClient.Ssid;
            if (!string.IsNullOrEmpty(wirelessClient.ConfigurationOption))
            {
                wifiConfiguration.Wireless80211Options = GetConfigurationOptions(wirelessClient.ConfigurationOption);
            }
            if (!string.IsNullOrEmpty(wirelessClient.RadioType))
            {
                wifiConfiguration.Radio = GetRadioType(wirelessClient.RadioType);
            }
            wifiConfiguration.Encryption = string.IsNullOrEmpty(wirelessClient.Encryption) ? EncryptionType.None : GetEncryptionType(wirelessClient.Encryption);
        }
    }
}
