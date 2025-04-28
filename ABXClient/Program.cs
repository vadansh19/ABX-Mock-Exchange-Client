using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AbxMockExchangeClient
{
    /// <summary>
    /// Represents a packet containing market data.
    /// </summary>
    public class Packet
    {
        /// <summary>
        /// Gets or sets the symbol of the asset.
        /// </summary>
        public string Symbol { get; set; }

        /// <summary>
        /// Gets or sets the buy/sell indicator ('B' or 'S').
        /// </summary>
        public string BuySellIndicator { get; set; }

        /// <summary>
        /// Gets or sets the quantity of the asset.
        /// </summary>
        public int Quantity { get; set; }

        /// <summary>
        /// Gets or sets the price of the asset.
        /// </summary>
        public int Price { get; set; }

        /// <summary>
        /// Gets or sets the sequence number of the packet.
        /// </summary>
        public int Sequence { get; set; }
    }

    /// <summary>
    /// Entry point of the AbxMockExchangeClient application.
    /// Connects to a server, receives packets, handles missing packets, and writes them to a JSON file.
    /// </summary>
    class Program
    {
        private const string ServerAddress = "127.0.0.1";
        private const int ServerPort = 3000;

        /// <summary>
        /// Main method to start the client application.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        static async Task Main(string[] args)
        {
            var packets = new Dictionary<int, Packet>();

            try
            {
                Console.WriteLine("Connecting to server...");
                using (var client = new TcpClient())
                {
                    await client.ConnectAsync(ServerAddress, ServerPort);
                    using (var stream = client.GetStream())
                    {
                        await SendStreamAllPacketsRequest(stream);
                        packets = await ReceivePackets(stream);
                    }
                }

                Console.WriteLine("Initial data received. Checking for missing sequences...");
                var missingSequences = FindMissingSequences(packets.Keys);

                if (missingSequences.Count > 0)
                {
                    Console.WriteLine($"Found {missingSequences.Count} missing sequences. Requesting missing packets...");
                    using (var client = new TcpClient())
                    {
                        await client.ConnectAsync(ServerAddress, ServerPort);
                        using (var stream = client.GetStream())
                        {
                            foreach (var seq in missingSequences)
                            {
                                await SendResendPacketRequest(stream, seq);
                                var packet = await ReceiveSinglePacket(stream);
                                packets[packet.Sequence] = packet;
                            }
                        }
                    }
                }

                Console.WriteLine("All packets collected. Writing to JSON...");
                var orderedPackets = packets.Values.OrderBy(p => p.Sequence).ToList();
                var json = JsonSerializer.Serialize(orderedPackets, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText("packets.json", json);
                Console.WriteLine("Done! File saved as packets.json");
            }
            catch (IOException ex)
            {
                Console.WriteLine($"IO Exception: {ex.Message}");
                LogError(ex);
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"Socket Exception: {ex.Message}");
                LogError(ex);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
                LogError(ex);
            }
        }

        /// <summary>
        /// Sends a request to the server to stream all available packets.
        /// </summary>
        /// <param name="stream">The network stream to write to.</param>
        private static async Task SendStreamAllPacketsRequest(NetworkStream stream)
        {
            try
            {
                byte[] request = new byte[2];
                request[0] = 1; // callType = 1
                request[1] = 0; // resendSeq (not used)
                await stream.WriteAsync(request, 0, request.Length);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error while sending stream all packets request: {ex.Message}");
                LogError(ex);
                throw;  // Re-throw to propagate the error
            }
        }

        /// <summary>
        /// Sends a request to the server to resend a specific packet by sequence number.
        /// </summary>
        /// <param name="stream">The network stream to write to.</param>
        /// <param name="sequence">The sequence number of the missing packet.</param>
        private static async Task SendResendPacketRequest(NetworkStream stream, int sequence)
        {
            try
            {
                byte[] request = new byte[2];
                request[0] = 2; // callType = 2
                request[1] = (byte)sequence; // resendSeq (1 byte only!)
                await stream.WriteAsync(request, 0, request.Length);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error while sending resend packet request for sequence {sequence}: {ex.Message}");
                LogError(ex);
                throw;  // Re-throw to propagate the error
            }
        }

        /// <summary>
        /// Receives multiple packets from the server and parses them into a dictionary.
        /// </summary>
        /// <param name="stream">The network stream to read from.</param>
        /// <returns>A dictionary of packets keyed by sequence number.</returns>
        private static async Task<Dictionary<int, Packet>> ReceivePackets(NetworkStream stream)
        {
            var packets = new Dictionary<int, Packet>();
            var buffer = new byte[17];

            try
            {
                while (true)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0)
                        break; // Server closed connection

                    if (bytesRead != 17)
                        throw new InvalidOperationException("Incomplete packet received!");

                    var packet = ParsePacket(buffer);
                    packets[packet.Sequence] = packet;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error while receiving packets: " + ex.Message);
                LogError(ex);
                throw;
            }

            return packets;
        }

        /// <summary>
        /// Receives a single packet from the server.
        /// </summary>
        /// <param name="stream">The network stream to read from.</param>
        /// <returns>The received packet.</returns>
        private static async Task<Packet> ReceiveSinglePacket(NetworkStream stream)
        {
            var buffer = new byte[17];
            int bytesRead = 0;

            try
            {
                while (bytesRead < buffer.Length)
                {
                    int read = await stream.ReadAsync(buffer, bytesRead, buffer.Length - bytesRead);
                    if (read == 0)
                        throw new IOException("Connection closed unexpectedly while reading a packet.");
                    bytesRead += read;
                }
            }
            catch (IOException ex)
            {
                Console.WriteLine("Error while receiving single packet: " + ex.Message);
                LogError(ex);
                throw;
            }

            return ParsePacket(buffer);
        }

        /// <summary>
        /// Parses a byte array into a <see cref="Packet"/> object.
        /// </summary>
        /// <param name="buffer">The byte array containing packet data.</param>
        /// <returns>A parsed <see cref="Packet"/> object.</returns>
        private static Packet ParsePacket(byte[] buffer)
        {
            try
            {
                string symbol = Encoding.ASCII.GetString(buffer, 0, 4);
                string buySellIndicator = Encoding.ASCII.GetString(buffer, 4, 1);

                int quantity = ReadInt32BigEndian(buffer, 5);
                int price = ReadInt32BigEndian(buffer, 9);
                int sequence = ReadInt32BigEndian(buffer, 13);

                return new Packet
                {
                    Symbol = symbol,
                    BuySellIndicator = buySellIndicator,
                    Quantity = quantity,
                    Price = price,
                    Sequence = sequence
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error parsing packet: " + ex.Message);
                LogError(ex);
                throw;
            }
        }

        /// <summary>
        /// Reads a 4-byte big-endian integer from a byte array at the specified offset.
        /// </summary>
        /// <param name="buffer">The byte array.</param>
        /// <param name="offset">The starting position within the array.</param>
        /// <returns>The 32-bit integer value.</returns>
        private static int ReadInt32BigEndian(byte[] buffer, int offset)
        {
            return (buffer[offset] << 24) |
                   (buffer[offset + 1] << 16) |
                   (buffer[offset + 2] << 8) |
                   (buffer[offset + 3]);
        }

        /// <summary>
        /// Logs error details to a file.
        /// </summary>
        /// <param name="ex">The exception to log.</param>
        private static void LogError(Exception ex)
        {
            string logPath = "error_log.txt";
            var logMessage = $"{DateTime.Now}: {ex.GetType().Name} - {ex.Message}\n{ex.StackTrace}\n\n";
            File.AppendAllText(logPath, logMessage);
        }

        /// <summary>
        /// Finds any missing sequence numbers within a collection of received sequences.
        /// </summary>
        /// <param name="sequences">The received sequence numbers.</param>
        /// <returns>A list of missing sequence numbers.</returns>
        private static List<int> FindMissingSequences(IEnumerable<int> sequences)
        {
            var sequenceList = sequences.OrderBy(s => s).ToList();
            var missing = new List<int>();

            if (!sequenceList.Any())
                return missing;

            for (int i = sequenceList.First(); i < sequenceList.Last(); i++)
            {
                if (!sequences.Contains(i))
                    missing.Add(i);
            }

            return missing;
        }
    }
}
