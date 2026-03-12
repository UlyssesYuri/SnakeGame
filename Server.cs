// <copyright file="Server.cs" company="UofU-CS3500">
// Copyright (c) 2024 UofU-CS3500. All rights reserved.
// </copyright>

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Sockets;

namespace CS3500.Networking;

/// <summary>
///   Represents a server task that waits for connections on a given
///   port and calls the provided delegate when a connection is made.
/// </summary>
public static class Server
{
    /// <summary>
    /// Logger instance for recording server events and messages. 
    /// If not set, defaults to a <see cref="NullLogger"/> that performs no logging.
    /// </summary>
    private static ILogger? _logger;

    /// <summary>
    /// TCP listener for accepting incoming client connections on a specified port.
    /// </summary>
    private static TcpListener? listener;

    /// <summary>
    ///   Gets or sets the logger used for server events.
    ///   If no logger is set, a NullLogger instance is used.
    /// </summary>
    public static ILogger Logger
    {
        get => _logger ??= NullLogger.Instance;
        set => _logger = value;
    }

    /// <summary>
    /// Initializes static resources for the <see cref="Server"/> class, 
    /// setting up a logger for console and debug output with a minimum logging level of Trace.
    /// </summary>
    static Server()
    {
        // Setup logging for a console application:
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();             builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Trace);
        });

        _logger = loggerFactory.CreateLogger<string>();
    }

    /// <summary>
    ///   Wait on a TcpListener for new connections. Alert the main program
    ///   via a callback (delegate) mechanism.
    /// </summary>
    /// <param name="handleConnect">
    ///   Handler for what the user wants to do when a connection is made.
    ///   This should be run asynchronously via a new thread.
    /// </param>
    /// <param name="port"> The port (e.g., 11000) to listen on. </param>
    public static void StartServer(Action<NetworkConnection> handleConnect, int port)
    {
        listener = new TcpListener(IPAddress.Any, port);
        Logger.LogInformation($"Server started on port {port}.");
        listener.Start();
        Logger.LogInformation("Server is listenting for connection...");

        try
        {
            while (true)
            {
                // Accept an incoming connection
                TcpClient client = listener.AcceptTcpClient();
                _logger?.LogInformation("Client connected");

                NetworkConnection connection = new NetworkConnection(client, Logger);
                // Call the provided handler in a new task
                new Thread(() => handleConnect(connection)).Start();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Server error: {ex.Message}");
        }
        finally
        {
            listener.Stop();
        }
    }
}

