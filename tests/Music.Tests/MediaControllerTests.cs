using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Music.Controllers;
using Music.Services;
using StackExchange.Redis;

namespace Music.Tests;

public class MediaControllerTests
{
    [Fact]
    public async Task RedisFailureFallsBackToBlobStorage()
    {
        var redis = new Mock<IDatabase>();
        redis.Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "test failure"));

        var blob = new Mock<IAzureBlobService>();
        blob.Setup(x => x.DownloadFileAsync("images", "song.png"))
            .ReturnsAsync(new MemoryStream(Encoding.UTF8.GetBytes("image")));

        var controller = CreateController(blob, redis);

        var result = await controller.GetImage("https://storage.example/images/song.png");

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("image/png", file.ContentType);
        Assert.Equal("image", Encoding.UTF8.GetString(file.FileContents));
        blob.Verify(x => x.DownloadFileAsync("images", "song.png"), Times.Once);
    }

    [Fact]
    public async Task BlobFailureReturnsNotFound()
    {
        var blob = new Mock<IAzureBlobService>();
        blob.Setup(x => x.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("blob unavailable"));

        var controller = CreateController(blob, new Mock<IDatabase>());

        var result = await controller.GetImage("https://storage.example/images/missing.png");

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("Image not found", notFound.Value);
    }

    [Fact]
    public async Task EmptyImageUrlIsBadRequest()
    {
        var controller = CreateController(new Mock<IAzureBlobService>(), new Mock<IDatabase>());

        var result = await controller.GetImage(string.Empty);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    private static MediaController CreateController(Mock<IAzureBlobService> blob, Mock<IDatabase> redis)
    {
        var controller = new MediaController(
            blob.Object,
            new MemoryCache(new MemoryCacheOptions()),
            redis.Object,
            new ConfigurationBuilder().Build(),
            NullLogger<MediaController>.Instance);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }
}
