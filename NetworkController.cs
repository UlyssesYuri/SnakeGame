using CS3500.Networking;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;


namespace Snake.Client;

public class NetworkController
{
    /// <summary>
    /// Gets the client ID assigned by the server.
    /// </summary>
    public int ClientId { get; private set; }

    /// <summary>
    /// Represents the connection to the game server.
    /// </summary>
    private readonly NetworkConnection server;

    /// <summary>
    /// Used for logging network activity and errors.
    /// </summary>
    private readonly ILogger logger;

    private readonly DatabaseService databaseService;

    private HashSet<int> processedSnakeIds = new();

    private Dictionary<int, (int maxScore, bool isAlive)> playerStateCache = new();

    private int currentGameId;

    private WorldClass? world;

    /// <summary>
    /// Gets a value indicating whether the client is currently connected to the server.
    /// </summary>
    public bool IsConnected => server.IsConnected;

    /// <summary>
    /// Initializes a new instance of the <see cref="NetworkController"/> class with the specified logger.
    /// </summary>
    /// <param name="NetWorklogger">The logger used for logging network activity.</param>
    public NetworkController(ILogger NetWorklogger)
    {
        server = new NetworkConnection(NetWorklogger);
        logger = NetWorklogger;
        databaseService = new DatabaseService();
    }

    public void SetGameId(int gameId)
    {
        currentGameId = gameId;
    }

    /// <summary>
    /// Connects to the server, handles incoming data, and updates the game world.
    /// </summary>
    /// <param name="world">The game world to update with server data.</param>
    /// <param name="serverUrl">The URL of the server.</param>
    /// <param name="port">The port number of the server.</param>
    /// <param name="playerName">The name of the player.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task HandleNetwork(WorldClass world, string serverUrl, int port, string playerName)
    {
        try
        {
            this.world = world;
            await Task.Run(() => server.Connect(serverUrl, port));
            logger.LogInformation($"Connected to server at {serverUrl}:{port}");

            server.Send(playerName);
            logger.LogInformation($"Player name sent: {playerName}");

            string clientIdData = server.ReadLine();
            if (int.TryParse(clientIdData, out int clientId))
            {
                logger.LogInformation($"Client ID received: {clientId}");
                this.ClientId = clientId;

            }
            else
            {
                logger.LogWarning($"Failed to parse Client ID: {clientIdData}");
            }

            while (server.IsConnected)
            {
                try
                {
                    string data = server.ReadLine();

                    if (int.TryParse(data, out int worldSize))
                    {
                        world.Width = worldSize;
                        world.Height = worldSize;
                    }


                    if (data.StartsWith("{\"snake\":"))
                    {
                        await HandleNetworkLoop(data, world, world.Snakes, s => s.SnakeId);
                        await Task.Run(() => UpdatePlayerData(world));
                    }

                    else if (data.StartsWith("{\"wall\":"))
                    {
                        await HandleNetworkLoop(data, world, world.Walls, s => s.WallId);

                    }

                    else if (data.StartsWith("{\"power\":"))
                    {

                        //HandleNetworkLoop(data, world, world.PowerUps, p => p.PowerId);
                        var powerUp = JsonSerializer.Deserialize<PowerUpClass>(data);
                        if (powerUp != null)
                        {
                            if (powerUp.IsActive)
                            {
                                lock (world)
                                {
                                    world.PowerUps.Remove(powerUp.PowerId);
                                }
                            }
                            else
                            {
                                lock (world)
                                {
                                    world.PowerUps[powerUp.PowerId] = powerUp;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error processing server data.");
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to handle network connection.");
        }
    }

    /// <summary>
    /// Disconnects from the server and cleans up resources.
    /// </summary>
    public async Task Disconnect()
    {
        if (server.IsConnected)
        {
            server.Disconnect();
            if (this.world != null)
            {
                await UpdatePlayerData(this.world, true);
            }
            else
            {
                logger.LogWarning("World is null. Skipping player data update.");
            }
            logger.LogInformation("Disconnected from server.");
        }
        else
        {
            logger.LogError("Could not disconnect from the server");
        }
    }

    /// <summary>
    /// Sends a movement direction command to the server.
    /// </summary>
    /// <param name="direction">The direction to send (e.g., "up", "down", "left", "right").</param>
    public void SendDirection(string direction)
    {
        if (!server.IsConnected)
        {
            logger.LogWarning("Server is not connected. Cannot send direction.");
            return;
        }

        var command = new { moving = direction };
        string jsonCommand = JsonSerializer.Serialize(command);

        try
        {
            server.Send(jsonCommand);
            logger.LogInformation($"Sent direction: {jsonCommand}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send direction to server.");
            throw;
        }
    }

    /// <summary>
    /// Processes JSON data received from the network, deserializes it into an object of type <typeparamref name="T"/>, 
    /// and updates the specified collection in a thread-safe manner.
    /// </summary>
    /// <typeparam name="T">The type of object to deserialize the JSON data into.</typeparam>
    /// <param name="jsonData">The JSON string representing the object to be deserialized.</param>
    /// <param name="world">The world object to lock for thread-safe updates.</param>
    /// <param name="collection">The collection to be updated with the deserialized object.</param>
    /// <param name="getId">
    /// A function that extracts a unique identifier from the deserialized object of type <typeparamref name="T"/>.
    /// This is used to ensure the object is stored correctly in the collection.
    /// </param>
    private async Task HandleNetworkLoop<T>(string jsonData, WorldClass world, Dictionary<int, T> collection, Func<T, int> getId)
    {
        await Task.Run(() =>
        {
            var data = JsonSerializer.Deserialize<T>(jsonData);
            if (data != null)
            {
                lock (world)
                {
                    collection[getId(data)] = data;
                }
            }
        });
    }

    private async Task UpdatePlayerData(WorldClass world, bool disconnected = false)
    {
        var tasks = new List<Task>(); // Collect tasks for parallel execution

        foreach (var snake in world.Snakes.Values)
        {
            bool isNewPlayer = !playerStateCache.ContainsKey(snake.SnakeId);
            bool hasScoreChanged = isNewPlayer || playerStateCache[snake.SnakeId].maxScore < snake.Score;

            if (isNewPlayer)
            {
                // Add a new player to the database
                await databaseService.AddPlayerAsync(
                    snake.SnakeId,      // playerId
                    snake.Name,         // name
                    0,                  // initial score
                    DateTime.Now,       // enterTime
                    currentGameId       // gameId
                );
                //tasks.Add(addTask);
            }
            else if (hasScoreChanged)
            {
                // Update the player's score in the database
                var updateScoreTask = databaseService.UpdatePlayerScoreAsync(
                    snake.SnakeId,      // playerId
                    snake.Score,        // maxScore
                    currentGameId       // gameId
                );
                tasks.Add(updateScoreTask);
            }

            if (disconnected)
            {
                // Update the player's leave time if they have disconnected
                var updateLeaveTimeTask = databaseService.UpdatePlayerLeaveTimeAsync(
                    snake.SnakeId,
                    DateTime.Now,
                    currentGameId
                );
                tasks.Add(updateLeaveTimeTask);
            }

            // Update the local cache for score and alive status
            playerStateCache[snake.SnakeId] = (snake.Score, snake.IsAlive);
        }

        await Task.WhenAll(tasks); // Await all tasks to complete
    }

}

