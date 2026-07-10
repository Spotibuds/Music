using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Music.Controllers;
using Music.Data;
using Music.Services;

namespace Music.Tests;

public class DependencyFailureTests
{
    [Fact]
    public async Task SongsReturnServiceUnavailableWhenMongoIsDisconnected()
    {
        var controller = new SongsController(new MongoDbContext(null, "spotibuds"), new Mock<IAzureBlobService>().Object);

        var result = await controller.GetSongs();

        var response = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task SearchReturnsServiceUnavailableWhenMongoIsDisconnected()
    {
        var controller = new SearchController(new MongoDbContext(null, "spotibuds"));

        var result = await controller.Search("jazz");

        var response = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, response.StatusCode);
    }
}
