namespace SimpleChat;

using System;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Grpc.Core.Interceptors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using System.IO;

class Program
{
    static async Task<int> Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  dotnet run -- server <ServerName> [<Port>]");
            Console.WriteLine("  dotnet run -- client <ClientName> <ServerAddress> [<Port>]");
            return 1;
        }

        string mode = args[0].ToLower();

        if (mode == "server")
        {
            string serverName = args.Length >= 2 ? args[1] : "Server";
            int port = args.Length >= 3 && int.TryParse(args[2], out var p) ? p : 50051;
            Console.WriteLine($"Starting server '{serverName}' on port {port}");
            await RunServerAsync(serverName, port);
        }
        else if (mode == "client")
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Client mode usage:");
                Console.WriteLine("  dotnet run -- client <ClientName> <ServerAddress> [<Port>]");
                return 1;
            }

            string clientName = args[1];
            string serverAddress = args[2];
            int port = args.Length >= 4 && int.TryParse(args[3], out var p) ? p : 50051;
            Console.WriteLine($"Starting client '{clientName}', connecting to {serverAddress}:{port}");
            await RunClientAsync(clientName, serverAddress, port);
        }
        else
        {
            Console.WriteLine("Invalid mode. Use 'server' or 'client'.");
            return 1;
        }

        return 0;
    }

    // Server code
    static async Task RunServerAsync(string serverName, int port)
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Warning); // Only warnings and error
            })
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.ConfigureKestrel(options =>
                {
                    options.ListenAnyIP(port, listenOptions =>
                    {
                        listenOptions.Protocols = HttpProtocols.Http2;
                    });
                });
                webBuilder.ConfigureServices(services =>
                {
                    services.AddGrpc();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGrpcService<ChatService>();
                    });
                });
            })
            .Build();

        // Pass serverName to ChatService
        ChatService.SetServerName(serverName);

        Console.WriteLine("Server running. Waiting for client to connect...");
        await host.RunAsync();
    }

    // Client code
    static async Task RunClientAsync(string clientName, string serverAddress, int port)
    {
        while (true)
        {
            try
            {
                var loggerFactory = LoggerFactory.Create(builder => {
                    builder
                        .AddConsole()
                        .SetMinimumLevel(LogLevel.Warning); // Only warnings and above)
                });

                var channel = GrpcChannel.ForAddress($"http://{serverAddress}:{port}", new GrpcChannelOptions
                {
                    LoggerFactory = loggerFactory
                });
                var client = new Chat.ChatClient(channel);

                using var chat = client.ChatStream();

                var cts = new CancellationTokenSource();

                // Start reading messages from server
                var readTask = Task.Run(async () =>
                {
                    try
                    {
                        await foreach (var serverMsg in chat.ResponseStream.ReadAllAsync(cts.Token))
                        {
                            if (serverMsg.Sender != clientName) // avoid showing own sent messages twice
                                Console.WriteLine($"{serverMsg.Sender}: {serverMsg.Body}");
                        }
                    }
                    catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
                    {
                        // Expected on cancellation
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Warning: Connection lost: {ex.Message}");
                    }
                });

                // Start reading user input and sending messages
                var writeTask = Task.Run(async () =>
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        string? line = Console.ReadLine();
                        if (line == null) break;

                        await chat.RequestStream.WriteAsync(new ChatMessage { Sender = clientName, Body = line });
                    }
                    await chat.RequestStream.CompleteAsync();
                });

                await Task.WhenAny(readTask, writeTask);

                cts.Cancel();

                // Dispose channel
                await channel.ShutdownAsync();

                Console.WriteLine("Disconnected. Reconnecting in 60 seconds...");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not connect or lost connection: {ex.Message}");
                Console.WriteLine("Reconnecting in 60 seconds...");
            }

            await Task.Delay(TimeSpan.FromSeconds(60));
        }
    }
}

// The server implementation of the gRPC Chat service
public class ChatService : Chat.ChatBase
{
    private static string _serverName = "Server";

    // To set the server name from Program.cs
    public static void SetServerName(string name)
    {
        _serverName = name;
    }

    private IServerStreamWriter<ChatMessage>? _responseStream;
    private IAsyncStreamReader<ChatMessage>? _requestStream;
    private CancellationTokenSource _cts = new();

    public override async Task ChatStream(IAsyncStreamReader<ChatMessage> requestStream, IServerStreamWriter<ChatMessage> responseStream, ServerCallContext context)
    {
        _requestStream = requestStream;
        _responseStream = responseStream;

        Console.WriteLine("Client connected.");

        var readTask = ReadIncomingMessages();

        var writeTask = WriteOutgoingMessages();

        await Task.WhenAny(readTask, writeTask);

        _cts.Cancel();

        Console.WriteLine("Client disconnected.");
    }

    // Queue to hold messages typed on server console
    private readonly System.Collections.Concurrent.BlockingCollection<string> _serverMessageQueue = new();

    public ChatService()
    {
        // Start console input reading task once per connection
        Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                var line = Console.ReadLine();
                if (line == null)
                {
                    await Task.Delay(100);
                    continue;
                }
                _serverMessageQueue.Add(line);
            }
        });
    }

    private async Task ReadIncomingMessages()
    {
        try
        {
            await foreach (var msg in _requestStream!.ReadAllAsync(_cts.Token))
            {
                Console.WriteLine($"{msg.Sender}: {msg.Body}");
            }
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
        {
            // Expected client disconnect, no error log needed
            Console.WriteLine("Client disconnected gracefully.");
        }
        catch (OperationCanceledException)
        {
            // Cancellation requested, normal shutdown
            Console.WriteLine("Server read operation cancelled.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Client disconnected or error: {ex.Message}");
        }
    }

    private async Task WriteOutgoingMessages()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var body = _serverMessageQueue.Take(_cts.Token);
                if (_responseStream != null)
                {
                    await _responseStream.WriteAsync(new ChatMessage { Sender = _serverName, Body = body });
                }
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Error sending message to client: {ex.Message}");
        }
    }
}
