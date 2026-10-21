using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FieldService.Api.Domain;
using FieldService.Api.Features.ChecklistTemplates.GetChecklistTemplates;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FieldService.Api.Tests.Features.ChecklistTemplates;

[Collection(DatabaseCollection.Name)]
public sealed class GetChecklistTemplatesTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    private const string TemplatesPath = "/api/checklist-templates";

    [Fact]
    public async Task AnAdministratorGetsTheFiveTemplatesOrderedByNameWithTradeAndItemCount()
    {
        using var client = Fixture.Factory.CreateApiClient();
        client.AccessToken = TestTokens.For(Fixture.Factory, UserRole.Administrator);

        using var response = await client.GetAsync(TemplatesPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var body = await response.Content.ReadFromJsonAsync<GetChecklistTemplatesResponse>(ApiClient.Json, CancellationToken);
        Assert.Equal(
            [
                new ChecklistTemplateResponse(Guid.Parse("01a15175-3c00-7000-8000-000103000000"), "Boiler service", Trade.Plumbing, 8),
                new ChecklistTemplateResponse(Guid.Parse("01a15175-3c00-7000-8000-000105000000"), "Electrical panel inspection", Trade.Electrical, 8),
                new ChecklistTemplateResponse(Guid.Parse("01a15175-3c00-7000-8000-000104000000"), "Fire extinguisher inspection", Trade.FireSafety, 7),
                new ChecklistTemplateResponse(Guid.Parse("01a15175-3c00-7000-8000-000101000000"), "General electrical inspection", Trade.Electrical, 8),
                new ChecklistTemplateResponse(Guid.Parse("01a15175-3c00-7000-8000-000102000000"), "Rooftop HVAC service", Trade.Hvac, 10),
            ],
            body!.Items);
        Fixture.Factory.Events.AssertEmpty();
    }

    [Fact]
    public async Task TheItemsMatchTheReferenceDataReadThroughAFreshDbContext()
    {
        using var client = Fixture.Factory.CreateApiClient();
        client.AccessToken = TestTokens.For(Fixture.Factory, UserRole.Administrator);

        using var response = await client.GetAsync(TemplatesPath);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var items = document.RootElement.GetProperty("items");
        Assert.Equal(["items"], document.RootElement.EnumerateObject().Select(member => member.Name));
        Assert.Equal(["id", "name", "trade", "itemCount"], items[0].EnumerateObject().Select(member => member.Name));
        await using var db = Fixture.CreateAppDbContext();
        var stored = await db.ChecklistTemplates
            .AsNoTracking()
            .Select(template => new { template.Id, template.Name, template.Trade, Count = template.Items.Count })
            .ToListAsync(CancellationToken);
        Assert.Equal(
            stored
                .OrderBy(template => template.Name, StringComparer.Ordinal)
                .Select(template => (template.Id, template.Name, template.Trade.ToString(), template.Count)),
            items.EnumerateArray().Select(item => (
                item.GetProperty("id").GetGuid(),
                item.GetProperty("name").GetString()!,
                item.GetProperty("trade").GetString()!,
                item.GetProperty("itemCount").GetInt32())));
    }

    [Fact]
    public async Task AnAnonymousCallerGets401()
    {
        using var client = Fixture.Factory.CreateApiClient();

        using var response = await client.GetAsync(TemplatesPath);

        await ApiClient.AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.unauthenticated");
    }

    [Fact]
    public async Task ATechnicianGets403()
    {
        using var client = Fixture.Factory.CreateApiClient();
        client.AccessToken = TestTokens.For(Fixture.Factory, UserRole.Technician);

        using var response = await client.GetAsync(TemplatesPath);

        await ApiClient.AssertProblemAsync(response, HttpStatusCode.Forbidden, "auth.forbidden");
    }
}
