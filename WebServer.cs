// <copyright file="WebServer.cs" company="UofU-CS3500">
// Copyright (c) 2024 UofU-CS3500. All rights reserved.
// </copyright>

using System;
using System.Text;
using CS3500.Networking;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// This class represents a simple web server capable of handling HTTP requests, connecting to a database, and generating dynamic HTML pages. 
/// It uses a logger for event tracking and supports routes like /, /games, and /games?gid={id} to display game-related data.
/// </summary>
public partial class WebServer
{
    /// <summary>
    /// Stores the connection string used to establish a connection to the database.
    /// It is configured during the static initialization of the <see cref="WebServer"/> class,
    /// using secure credentials from user secrets. If the configuration fails, it is set to an empty
    /// string to prevent unintended usage.
    /// </summary>
    private static readonly string ConnectionString = string.Empty;

    /// <summary>
    /// Logger instance for logging server events.
    /// If not set, defaults to a <see cref="NullLogger"/> that performs no logging.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// The entry point of the application. Configures logging, initializes the <see cref="WebServer"/> instance,
    /// and starts the server on a specified port.
    /// </summary>
    /// <param name="args">Command-line arguments passed to the application.</param>
    public static void Main(string[] args)
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        ILogger logger = loggerFactory.CreateLogger<WebServer>();

        var webServer = new WebServer(logger);
        webServer.Start(11000);
    }

    /// <summary>
    /// Initializes static members of the <see cref="WebServer"/> class.
    /// Configures the database connection string by retrieving credentials
    /// from user secrets. If the configuration fails, the connection string
    /// is set to an empty value, and any errors are logged.
    /// </summary>
    static WebServer()
    {
        var builder = new ConfigurationBuilder();

        builder.AddUserSecrets<WebServer>();
        IConfigurationRoot configuration = builder.Build();
        var selectedSecrets = configuration.GetSection("LabSecrets");

        try
        {
            ConnectionString = new SqlConnectionStringBuilder()
            {
                DataSource = selectedSecrets["ServerName"],
                InitialCatalog = selectedSecrets["DBName"],
                UserID = selectedSecrets["UserID"],
                Password = selectedSecrets["UserPassword"],
                ConnectTimeout = 15,
                Encrypt = false,
            }.ConnectionString;
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebServer"/> class with the specified logger.
    /// </summary>
    /// <param name="logger">The logger instance used to log server events and errors.</param>
    /// <exception cref="ArgumentNullException">Thrown when the provided logger is null.</exception>
    public WebServer(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Starts the web server and begins listening for incoming connections on the specified port.
    /// </summary>
    /// <param name="port">The port number on which the server will listen for client requests.</param>
    public void Start(int port)
    {
        _logger.LogInformation($"Starting server on port {port}...");
        Server.StartServer(HandleConnection, port);
    }

    /// <summary>
    /// Constructs an HTTP response with the provided HTML body and the necessary headers.
    /// </summary>
    /// <param name="body">The HTML content to be included in the response body.</param>
    /// <returns>A string representing a complete HTTP response with headers and body.</returns>
    private string BuildHttpResponse(string body)
    {
        string headers = "HTTP/1.1 200 OK\r\n" +
                         "Content-Type: text/html; charset=UTF-8\r\n" +
                         $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n" +
                         "Connection: close\r\n\r\n";
        return headers + body;
    }

    /// <summary>
    /// Processes the incoming HTTP request and determines the appropriate response based on the request path.
    /// </summary>
    /// <param name="request">The HTTP request string received from the client.</param>
    /// <returns>An HTML response string corresponding to the requested resource or an error page if the resource is not found.</returns>
    private string ProcessRequest(string request)
    {
        if (request.StartsWith("GET / HTTP/1.1"))
        {
            return BuildHttpResponse(@"
<html>
<head>
    <title>Snake Games Database</title>
    <style>
        body {
            font-family: Arial, sans-serif;
            margin: 0;
            padding: 0;
            background-color: #f4f4f4;
            text-align: center;
        }
        h3 {
            margin-top: 50px;
            color: #333;
        }
        a {
            display: inline-block;
            margin-top: 20px;
            text-decoration: none;
            font-size: 18px;
            color: white;
            background-color: #007bff;
            padding: 10px 20px;
            border-radius: 5px;
        }
        a:hover {
            background-color: #0056b3;
        }
    </style>
</head>
<body>
    <h3>Welcome to the Snake Games Database!</h3>
    <a href='/games'>View Games</a>
</body>
</html>");
        }
        else if (request.StartsWith("GET /games HTTP/1.1"))
        {
            return BuildHttpResponse(GenerateGamesTable());
        }
        else if (request.StartsWith("GET /games?gid="))
        {
            var gameId = ExtractGameId(request);
            return GenerateGameDetails(gameId);
        }
        else
        {
            return "<html><h3>404 Not Found</h3></html>";
        }
    }

    /// <summary>
    /// Handles an individual client connection by reading the HTTP request,
    /// processing it to generate the appropriate response, and sending the response back to the client.
    /// </summary>
    /// <param name="connection">The <see cref="NetworkConnection"/> representing the client connection.</param>
    private void HandleConnection(NetworkConnection connection)
    {
        try
        {
            // Read the request
            string request = connection.ReadLine();
            if (string.IsNullOrEmpty(request))
            {
                connection.Send("<html><h3>400 Bad Request</h3></html>");
                return;
            }

            _logger.LogInformation($"Received request: {request}");

            // Process the request and generate a response
            string responseContent = ProcessRequest(request);

            // Send the response
            connection.Send(responseContent);
        }
        catch (Exception ex)
        {
            _logger.LogInformation($"Error handling connection: {ex.Message}");
            connection.Dispose();
        }
    }

    /// <summary>
    /// Generates an HTML table containing information about all games retrieved from the database.
    /// Each game is displayed with its ID, start time, and end time, along with a link to view details for the specific game.
    /// </summary>
    /// <returns>An HTML string representing the games table.</returns>
    private string GenerateGamesTable()
    {
        var html = new StringBuilder("<html><table border='1'><thead><tr><td>ID</td><td>Start</td><td>End</td></tr></thead><tbody>");

        using (var connection = new SqlConnection(ConnectionString))
        {
            connection.Open();
            var command = new SqlCommand("SELECT ID, StartTime, EndTime FROM Game", connection);
            var reader = command.ExecuteReader();
            while (reader.Read())
            {
                html.Append($"<tr><td><a href='/games?gid={reader["ID"]}'>{reader["ID"]}</a></td><td>{reader["StartTime"]}</td><td>{reader["EndTime"]}</td></tr>");
            }
        }

        html.Append("</tbody></table></html>");
        return html.ToString();
    }

    /// <summary>
    /// Extracts the game ID from the query string of an HTTP request.
    /// </summary>
    /// <param name="request">The HTTP request string containing the query string with the game ID.</param>
    /// <returns>The extracted game ID as an integer, or -1 if the ID cannot be parsed.</returns>
    private int ExtractGameId(string request)
    {
        var startIndex = request.IndexOf("?gid=") + 5;
        var endIndex = request.IndexOf(" ", startIndex); // HTTP request ends the path with a space
        var gameIdStr = request[startIndex..endIndex];
        return int.TryParse(gameIdStr, out int gameId) ? gameId : -1;
    }

    /// <summary>
    /// Generates an HTML page containing detailed information about a specific game,
    /// including player statistics such as Player ID, Name, Max Score, Enter Time, and Leave Time.
    /// </summary>
    /// <param name="gameId">The ID of the game for which details are to be retrieved.</param>
    /// <returns>An HTML string representing the game details table, or an error message if the game ID is invalid or data retrieval fails.</returns>
    private string GenerateGameDetails(int gameId)
    {
        if (gameId < 0)
        {
            return BuildHttpResponse("<html><h3>Invalid Game ID</h3></html>");
        }

        var html = new StringBuilder($"<html><h3>Stats for Game {gameId}</h3><table border='1'><thead><tr><td>Player ID</td><td>Name</td><td>Max Score</td><td>Enter Time</td><td>Leave Time</td></tr></thead><tbody>");

        try
        {
            using (var connection = new SqlConnection(ConnectionString))
            {
                connection.Open();
                var command = new SqlCommand("SELECT ID, Name, MaxScore, EnterTime, LeaveTime FROM Players WHERE GameID = @GameID", connection);
                command.Parameters.AddWithValue("@GameID", gameId);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        html.Append(
                            $"<tr>" +
                            $"<td>{reader["ID"]}</td>" +
                            $"<td>{reader["Name"]}</td>" +
                            $"<td>{reader["MaxScore"]}</td>" +
                            $"<td>{reader["EnterTime"]}</td>" +
                            $"<td>{reader["LeaveTime"]}</td>" +
                            $"</tr>");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            html.Append($"<tr><td colspan='5'>Error loading game details: {ex.Message}</td></tr>");
        }

        html.Append("</tbody></table></html>");
        return BuildHttpResponse(html.ToString());
    }
}