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
        Assert.Equal(1, records.GetProperty("totalCount").GetInt32());
        Assert.Single(records.GetProperty("records").EnumerateArray());

        HttpResponseMessage deleteRecord = await client.DeleteAsync($"/tables/users/records/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteRecord.StatusCode);

        HttpResponseMessage missingRecord = await client.GetAsync($"/tables/users/records/{id}");
        Assert.Equal(HttpStatusCode.NotFound, missingRecord.StatusCode);
    }

    [Fact]
    public async Task RecordsCanBePagedAndFilteredThroughRest()
    {
        HttpClient client = factory.CreateClient();

        await client.PostAsJsonAsync("/tables", new
        {
            name = "users",
            columns = new[]
            {
                new { name = "name", type = "TEXT" },
                new { name = "role", type = "TEXT" },
                new { name = "age", type = "INTEGER" }
            }
        });
        await client.PostAsJsonAsync("/tables/users/records", new { name = "Alice", role = "admin", age = 31 });
        await client.PostAsJsonAsync("/tables/users/records", new { name = "Bob", role = "reader", age = 22 });
        await client.PostAsJsonAsync("/tables/users/records", new { name = "Carol", role = "admin", age = 40 });

        JsonElement response = await client.GetFromJsonAsync<JsonElement>(
            "/tables/users/records?page=1&pageSize=1&filterColumn=role&filterValue=admin&sortColumn=name&sortDirection=desc");

        Assert.Equal(1, response.GetProperty("page").GetInt32());
        Assert.Equal(1, response.GetProperty("pageSize").GetInt32());
        Assert.Equal(2, response.GetProperty("totalCount").GetInt32());
        JsonElement record = Assert.Single(response.GetProperty("records").EnumerateArray());
        Assert.Equal("Carol", record.GetProperty("name").GetString());

        JsonElement containsResponse = await client.GetFromJsonAsync<JsonElement>(
            "/tables/users/records?filterColumn=name&filterOperator=contains&filterValue=o&sortColumn=name");

        Assert.Equal(2, containsResponse.GetProperty("totalCount").GetInt32());
        JsonElement firstContainsRecord = containsResponse.GetProperty("records").EnumerateArray().First();
        Assert.Equal("Bob", firstContainsRecord.GetProperty("name").GetString());

        JsonElement comparisonResponse = await client.GetFromJsonAsync<JsonElement>(
            "/tables/users/records?filterColumn=age&filterOperator=gte&filterValue=31&sortColumn=age");

        Assert.Equal(2, comparisonResponse.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task TableSchemaCanBeReadThroughRest()
    {
        HttpClient client = factory.CreateClient();

        await client.PostAsJsonAsync("/tables", new
        {
            name = "users",
            columns = new[] { new { name = "name", type = "TEXT" } }
        });

        JsonElement schema = await client.GetFromJsonAsync<JsonElement>("/tables/users/schema");

        Assert.Equal("users", schema.GetProperty("name").GetString());
        Assert.Contains(schema.GetProperty("columns").EnumerateArray(), column => column.GetProperty("name").GetString() == "name");

        HttpResponseMessage addColumn = await client.PostAsJsonAsync("/tables/users/columns", new
        {
            name = "job",
            type = "TEXT"
        });
        Assert.Equal(HttpStatusCode.OK, addColumn.StatusCode);

        JsonDocument migratedSchema = await JsonDocument.ParseAsync(await addColumn.Content.ReadAsStreamAsync());
        Assert.Contains(migratedSchema.RootElement.GetProperty("columns").EnumerateArray(), column => column.GetProperty("name").GetString() == "job");

        HttpResponseMessage renameColumn = await client.PostAsJsonAsync("/tables/users/columns/job/rename", new
        {
            name = "title"
        });
        Assert.Equal(HttpStatusCode.OK, renameColumn.StatusCode);

        JsonDocument renamedSchema = await JsonDocument.ParseAsync(await renameColumn.Content.ReadAsStreamAsync());
        Assert.Contains(renamedSchema.RootElement.GetProperty("columns").EnumerateArray(), column => column.GetProperty("name").GetString() == "title");
        Assert.DoesNotContain(renamedSchema.RootElement.GetProperty("columns").EnumerateArray(), column => column.GetProperty("name").GetString() == "job");

        HttpResponseMessage dropColumn = await client.DeleteAsync("/tables/users/columns/title");
        Assert.Equal(HttpStatusCode.OK, dropColumn.StatusCode);

        JsonDocument droppedSchema = await JsonDocument.ParseAsync(await dropColumn.Content.ReadAsStreamAsync());
        Assert.DoesNotContain(droppedSchema.RootElement.GetProperty("columns").EnumerateArray(), column => column.GetProperty("name").GetString() == "title");
    }

    [Fact]
    public async Task ApiKeyIsRequiredWhenConfigured()
    {
        string protectedDatabasePath = Path.Combine(Path.GetTempPath(), $"restdb-{Guid.NewGuid():N}.db");
        using WebApplicationFactory<Program> protectedFactory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:RestDb"] = $"Data Source={protectedDatabasePath};Version=3;",
                        ["RestDb:ApiKey"] = "test-key"
                    });
                });
            });

        HttpClient client = protectedFactory.CreateClient();

        HttpResponseMessage unauthorized = await client.GetAsync("/tables");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        HttpResponseMessage health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        using HttpRequestMessage authorizedRequest = new HttpRequestMessage(HttpMethod.Get, "/tables");
        authorizedRequest.Headers.Add("X-API-Key", "test-key");
        HttpResponseMessage authorized = await client.SendAsync(authorizedRequest);
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);

        using HttpRequestMessage bearerRequest = new HttpRequestMessage(HttpMethod.Get, "/tables");
        bearerRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-key");
        HttpResponseMessage bearerAuthorized = await client.SendAsync(bearerRequest);
        Assert.Equal(HttpStatusCode.OK, bearerAuthorized.StatusCode);

        if (File.Exists(protectedDatabasePath))
        {
            File.Delete(protectedDatabasePath);
        }
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
