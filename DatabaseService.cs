namespace Snake.Client;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

public class DatabaseService
{
    public static string ConnectionString = string.Empty;

    static DatabaseService()
    {
        var builder = new ConfigurationBuilder();

        builder.AddUserSecrets<DatabaseService>();
        IConfigurationRoot configuration = builder.Build();
        var selectedSecrets = configuration.GetSection("LabSecrets");

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

    public void AddGame(DateTime startTime, out int gameId)
    {
        using SqlConnection con = new SqlConnection(ConnectionString);
        con.Open();
        using SqlCommand command = new SqlCommand(
            "INSERT INTO Game (StartTime) VALUES (@StartTime); SELECT SCOPE_IDENTITY();", con);
        command.Parameters.AddWithValue("@StartTime", startTime);

        var result = command.ExecuteScalar(); // Use ExecuteScalarAsync for async operations
        gameId = Convert.ToInt32(result);
        //logger.LogInformation($"Game added with ID: {gameId}");
    }

    public async Task AddPlayerAsync(int playerId, string name, int maxScore, DateTime enterTime, int gameId)
    {
        using SqlConnection con = new SqlConnection(ConnectionString);
        await con.OpenAsync();
        using SqlCommand command = new SqlCommand(
            @"INSERT INTO Players (ID, Name, EnterTime, GameID)
          VALUES (@PlayerId, @Name, @EnterTime, @GameId);", con);

        command.Parameters.AddWithValue("@PlayerId", playerId);
        command.Parameters.AddWithValue("@Name", name);
        command.Parameters.AddWithValue("@EnterTime", enterTime);
        command.Parameters.AddWithValue("@GameId", gameId);

        await command.ExecuteNonQueryAsync();
    }

    public async Task UpdatePlayerScoreAsync(int playerId, int maxScore, int gameId)
    {
        using SqlConnection con = new SqlConnection(ConnectionString);
        await con.OpenAsync();
        using SqlCommand command = new SqlCommand(
            @"UPDATE Players
          SET MaxScore = @MaxScore
          WHERE ID = @PlayerId AND GameID = @GameId;", con);

        command.Parameters.AddWithValue("@PlayerId", playerId);
        command.Parameters.AddWithValue("@MaxScore", maxScore);
        command.Parameters.AddWithValue("@GameId", gameId);

        await command.ExecuteNonQueryAsync();
    }

    public async Task UpdatePlayerLeaveTimeAsync(int playerId, DateTime? leaveTime, int gameId)
    {
        using SqlConnection con = new SqlConnection(ConnectionString);
        await con.OpenAsync();
        using SqlCommand command = new SqlCommand(
            @"UPDATE Players
          SET LeaveTime = @LeaveTime
          WHERE ID = @PlayerId AND GameID = @GameId;", con);

        command.Parameters.AddWithValue("@PlayerId", playerId);
        command.Parameters.AddWithValue("@LeaveTime", (object?)leaveTime ?? DBNull.Value);
        command.Parameters.AddWithValue("@GameId", gameId);

        await command.ExecuteNonQueryAsync();
    }


    public void UpdateGameEndTime(int gameId, DateTime endTime)
    {
        using SqlConnection con = new SqlConnection(ConnectionString);
        con.Open();
        using SqlCommand command = new SqlCommand(
            "UPDATE Game SET EndTime = @EndTime WHERE ID = @GameID", con);
        command.Parameters.AddWithValue("@GameID", gameId);
        command.Parameters.AddWithValue("@EndTime", endTime);

        command.ExecuteNonQuery();
        //logger.LogInformation($"Game {gameId} end time updated to {endTime}");
    }

}
