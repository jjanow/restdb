using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace RestDb.Tests;

public class RestApiTests : IDisposable
{
    private readonly string databasePath;
    private readonly WebApplicationFactory<Program> factory;

    public RestApiTests()
    {
        databasePath = Path.Combine(Path.GetTempPath(), $"restdb-{Guid.NewGuid():N}.db");
        string connectionString = $"Data Source={databasePath};Version=3;";

        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:RestDb"] = connectionString
                    });
                });
            });
    }

    [Fact]
    public async Task HealthEndpointReturnsOk()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RecordsCanBeCreatedReadUpdatedAndDeletedThroughRest()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage createTable = await client.PostAsJsonAsync("/tables", new
        {
            name = "users",
            columns = new[]
            {
                new { name = "name", type = "TEXT" },
                new { name = "age", type = "INTEGER" },
                new { name = "job", type = "TEXT" }
            }
        });
        Assert.Equal(HttpStatusCode.Created, createTable.StatusCode);

        HttpResponseMessage createRecord = await client.PostAsJsonAsync("/tables/users/records", new
        {
            name = "Alice",
            age = 31,
            job = "Engineer"
        });
        Assert.Equal(HttpStatusCode.Created, createRecord.StatusCode);

        JsonDocument createdRecord = await JsonDocument.ParseAsync(await createRecord.Content.ReadAsStreamAsync());
        long id = createdRecord.RootElement.GetProperty("id").GetInt64();

        JsonElement readRecord = await client.GetFromJsonAsync<JsonElement>($"/tables/users/records/{id}");
        Assert.Equal("Alice", readRecord.GetProperty("name").GetString());
        Assert.Equal(31, readRecord.GetProperty("age").GetInt64());

        HttpResponseMessage updateRecord = await client.PutAsJsonAsync($"/tables/users/records/{id}", new
        {
            job = "Architect"
        });
        Assert.Equal(HttpStatusCode.OK, updateRecord.StatusCode);

        JsonElement updatedRecord = await client.GetFromJsonAsync<JsonElement>($"/tables/users/records/{id}");
        Assert.Equal("Architect", updatedRecord.GetProperty("job").GetString());

        JsonElement records = await client.GetFromJsonAsync<JsonElement>("/tables/users/records");
        Assert.Single(records.EnumerateArray());

        HttpResponseMessage deleteRecord = await client.DeleteAsync($"/tables/users/records/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteRecord.StatusCode);

        HttpResponseMessage missingRecord = await client.GetAsync($"/tables/users/records/{id}");
        Assert.Equal(HttpStatusCode.NotFound, missingRecord.StatusCode);
    }

    [Fact]
    public async Task InvalidTableNamesReturnBadRequest()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/tables", new
        {
            name = "bad-table-name",
            columns = new[] { new { name = "name", type = "TEXT" } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    public void Dispose()
    {
        factory.Dispose();

        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }
    }
}
