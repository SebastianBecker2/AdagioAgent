using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace AdagioMachineAgent.BootstrapperApplication
{
    /// <summary>
    /// Discovered installer context data for pre-population in wizard.
    /// </summary>
    public class DiscoveryData
    {
        [JsonPropertyName("hostName")]
        public string? HostName { get; set; }

        [JsonPropertyName("discoveredIPAddresses")]
        public List<string> DiscoveredIPAddresses { get; set; } = new();
    }

    /// <summary>
    /// Response file schema for silent installation configuration (schemaVersion=1).
    /// </summary>
    public class InstallerResponseFile
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; } = 1;

        [JsonPropertyName("timestamp")]
        public string? Timestamp { get; set; }

        [JsonPropertyName("security")]
        public SecurityOptions? Security { get; set; }

        [JsonPropertyName("network")]
        public NetworkOptions? Network { get; set; }

        [JsonPropertyName("agentOptions")]
        public AgentOptions? AgentOptions { get; set; }

        [JsonPropertyName("discovery")]
        public DiscoveryData? Discovery { get; set; }
    }

    /// <summary>
    /// Security configuration options (certificate mode, API key mode, HTTPS requirement).
    /// </summary>
    public class SecurityOptions
    {
        [JsonPropertyName("certificateMode")]
        public string? CertificateMode { get; set; }

        [JsonPropertyName("providedCertificatePath")]
        public string? ProvidedCertificatePath { get; set; }

        [JsonPropertyName("providedCertificatePassword")]
        public string? ProvidedCertificatePassword { get; set; }

        [JsonPropertyName("apiKeyMode")]
        public string? ApiKeyMode { get; set; }

        [JsonPropertyName("providedApiKey")]
        public string? ProvidedApiKey { get; set; }

        [JsonPropertyName("requireHttps")]
        public bool RequireHttps { get; set; } = true;

        [JsonPropertyName("requireApiKey")]
        public bool RequireApiKey { get; set; } = true;
    }

    /// <summary>
    /// Network configuration (service URLs and allowed hosts).
    /// </summary>
    public class NetworkOptions
    {
        [JsonPropertyName("urls")]
        public string? Urls { get; set; }

        [JsonPropertyName("allowedHosts")]
        public string? AllowedHosts { get; set; }
    }

    /// <summary>
    /// Agent security options (path allowlists).
    /// </summary>
    public class AgentOptions
    {
        [JsonPropertyName("allowedExecutablePaths")]
        public string? AllowedExecutablePaths { get; set; }

        [JsonPropertyName("allowedWritablePaths")]
        public string? AllowedWritablePaths { get; set; }

        [JsonPropertyName("allowedReadablePaths")]
        public string? AllowedReadablePaths { get; set; }
    }

    /// <summary>
    /// Installer context shared between MainWindow and screens.
    /// </summary>
    public class InstallerContext
    {
        /// <summary>
        /// Discovery data from pre-installation discovery pass.
        /// </summary>
        public DiscoveryData Discovery { get; }

        public InstallerContext()
        {
            this.Discovery = DiscoverInstallerContext();
        }

        /// <summary>
        /// Discover network configuration, hostname, and existing installation state.
        /// </summary>
        public static DiscoveryData DiscoverInstallerContext()
        {
            var discovery = new DiscoveryData();

            try
            {
                discovery.HostName = Dns.GetHostName();
            }
            catch { /* fallback to empty */ }

            try
            {
                var addresses = new List<string>();
                foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up)
                        continue;

                    var ipProps = adapter.GetIPProperties();
                    foreach (var unicastAddr in ipProps.UnicastAddresses)
                    {
                        if (unicastAddr.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            addresses.Add(unicastAddr.Address.ToString());
                        }
                    }
                }
                discovery.DiscoveredIPAddresses = addresses;
            }
            catch { /* fallback to empty list */ }

            return discovery;
        }
    }
}

