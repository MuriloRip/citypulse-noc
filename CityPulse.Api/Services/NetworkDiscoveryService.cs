using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using CityPulse.Api.Security;

namespace CityPulse.Api.Services;

public sealed class NetworkDiscoveryService
{
    private static readonly int[] PortsToCheck = [22, 80, 443, 445, 554, 3389, 9100];
    private readonly SemaphoreSlim _scanLock = new(1, 1);

    public async Task<DiscoveryScanResult> ScanAsync(
        string cidr,
        bool authorized,
        CancellationToken cancellationToken)
    {
        if (!authorized)
        {
            throw new DiscoveryValidationException("Confirme que você tem autorização para verificar essa rede.");
        }

        if (!TryGetRange(cidr, out var addresses, out var network))
        {
            throw new DiscoveryValidationException(
                "Informe uma faixa IPv4 privada CIDR válida, com até 254 endereços utilizáveis (por exemplo, 192.168.1.0/24).");
        }

        if (!await _scanLock.WaitAsync(0, cancellationToken))
        {
            throw new DiscoveryBusyException();
        }

        try
        {
            using var concurrency = new SemaphoreSlim(24, 24);
            var observations = await Task.WhenAll(addresses.Select(async address =>
            {
                await concurrency.WaitAsync(cancellationToken);
                try
                {
                    return await InspectAddressAsync(address, cancellationToken);
                }
                finally
                {
                    concurrency.Release();
                }
            }));

            var devices = observations
                .Where(observation => observation.IsReachable)
                .OrderBy(observation => observation.Address, IpAddressComparer.Instance)
                .ToArray();

            return new DiscoveryScanResult(
                network,
                addresses.Length,
                devices.Length,
                DateTimeOffset.UtcNow,
                devices);
        }
        finally
        {
            _scanLock.Release();
        }
    }

    private static async Task<DiscoveredDevice> InspectAddressAsync(
        IPAddress address,
        CancellationToken cancellationToken)
    {
        using var ping = new Ping();
        var pingTask = CheckPingAsync(ping, address, cancellationToken);
        var portTasks = PortsToCheck.Select(port => CheckPortAsync(address, port, cancellationToken));
        var openPorts = (await Task.WhenAll(portTasks)).Where(result => result.IsOpen).Select(result => result.Port).ToArray();
        var pingLatencyMs = await pingTask;
        var (category, icon, confidence, evidence) = ClassifyDevice(openPorts, pingLatencyMs is not null);
        var isReachable = pingLatencyMs is not null || openPorts.Length > 0;

        return new DiscoveredDevice(
            address.ToString(),
            isReachable,
            pingLatencyMs,
            openPorts,
            category,
            icon,
            confidence,
            evidence);
    }

    private static async Task<long?> CheckPingAsync(Ping ping, IPAddress address, CancellationToken cancellationToken)
    {
        try
        {
            var reply = await ping.SendPingAsync(address, TimeSpan.FromMilliseconds(600), new byte[16], new PingOptions(), cancellationToken);
            return reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        catch (PingException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static async Task<(int Port, bool IsOpen)> CheckPortAsync(
        IPAddress address,
        int port,
        CancellationToken cancellationToken)
    {
        using var client = new TcpClient(AddressFamily.InterNetwork);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(400));

        try
        {
            await client.ConnectAsync(address, port, timeout.Token);
            return (port, true);
        }
        catch (SocketException)
        {
            return (port, false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (port, false);
        }

    }

    private static (string Category, string Icon, string Confidence, string Evidence) ClassifyDevice(
        int[] openPorts,
        bool respondsToPing)
    {
        if (openPorts.Contains(9100))
        {
            return ("Impressora provável", "printer", "medium", "Porta de impressão 9100 acessível.");
        }

        if (openPorts.Contains(554))
        {
            return ("Câmera ou mídia provável", "camera", "low", "Porta RTSP 554 acessível; pode ser outro serviço de mídia.");
        }

        if (openPorts.Contains(3389) || openPorts.Contains(445))
        {
            return ("Computador ou servidor provável", "computer", "medium",
                $"Serviço compatível com Windows acessível: {string.Join(", ", openPorts.Where(port => port is 3389 or 445))}.");
        }

        if (openPorts.Contains(22) && !openPorts.Contains(80) && !openPorts.Contains(443))
        {
            return ("Servidor ou dispositivo de rede", "server", "low", "Porta SSH 22 acessível; o tipo exato não pode ser confirmado.");
        }

        if (openPorts.Length > 0)
        {
            return ("Dispositivo com serviço de rede", openPorts.Contains(22) ? "network" : "unknown", "low",
                $"Portas TCP acessíveis: {string.Join(", ", openPorts)}.");
        }

        return ("Dispositivo não identificado", "unknown", "low",
            respondsToPing ? "Respondeu a ICMP; não foi possível determinar o tipo." : "Respondeu à verificação de conectividade.");
    }

    private static bool TryGetRange(string? cidr, out IPAddress[] addresses, out string normalizedNetwork)
    {
        addresses = [];
        normalizedNetwork = string.Empty;
        if (string.IsNullOrWhiteSpace(cidr) || cidr.Length > 20)
        {
            return false;
        }

        var parts = cidr.Split('/');
        if (parts.Length != 2
            || !IPAddress.TryParse(parts[0], out var address)
            || address.AddressFamily != AddressFamily.InterNetwork
            || !address.ToString().Equals(parts[0], StringComparison.Ordinal)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var prefix)
            || prefix is < 24 or > 32
            || !TargetPolicy.IsPrivateIpv4(address))
        {
            return false;
        }

        var addressBytes = address.GetAddressBytes();
        var addressValue = ToUInt32(addressBytes);
        var mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
        var networkValue = addressValue & mask;
        var broadcastValue = networkValue | ~mask;
        var first = prefix <= 30 ? networkValue + 1 : networkValue;
        var last = prefix <= 30 ? broadcastValue - 1 : broadcastValue;
        var count = (long)last - first + 1;
        if (count is < 1 or > 254)
        {
            return false;
        }

        var result = new IPAddress(FromUInt32(networkValue));
        normalizedNetwork = $"{result}/{prefix}";
        addresses = Enumerable.Range(0, (int)count)
            .Select(offset => new IPAddress(FromUInt32(first + (uint)offset)))
            .ToArray();
        return true;
    }

    private static uint ToUInt32(byte[] bytes) =>
        ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];

    private static byte[] FromUInt32(uint value) =>
        [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    private sealed class IpAddressComparer : IComparer<string>
    {
        public static IpAddressComparer Instance { get; } = new();

        public int Compare(string? left, string? right)
        {
            if (left is null || right is null) return string.Compare(left, right, StringComparison.Ordinal);
            return ToUInt32(IPAddress.Parse(left).GetAddressBytes()).CompareTo(ToUInt32(IPAddress.Parse(right).GetAddressBytes()));
        }
    }
}

public sealed record DiscoveryScanResult(
    string Network,
    int AddressesChecked,
    int DevicesFound,
    DateTimeOffset ScannedAtUtc,
    IReadOnlyList<DiscoveredDevice> Devices);

public sealed record DiscoveredDevice(
    string Address,
    bool IsReachable,
    long? PingLatencyMs,
    IReadOnlyList<int> OpenTcpPorts,
    string Category,
    string Icon,
    string Confidence,
    string Evidence);

public sealed class DiscoveryValidationException(string message) : Exception(message);

public sealed class DiscoveryBusyException() : Exception("Já existe uma descoberta em andamento. Aguarde antes de iniciar outra.");
