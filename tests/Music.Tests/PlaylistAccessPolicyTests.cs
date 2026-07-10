using System.Security.Claims;
using Music.Authorization;

namespace Music.Tests;

public class PlaylistAccessPolicyTests
{
    [Fact]
    public void OwnerCanManagePlaylist()
    {
        var user = Principal((ClaimTypes.NameIdentifier, "user-1"));

        Assert.True(PlaylistAccessPolicy.CanManage("user-1", user));
    }

    [Fact]
    public void DifferentUserCannotManagePlaylist()
    {
        var user = Principal((ClaimTypes.NameIdentifier, "user-2"));

        Assert.False(PlaylistAccessPolicy.CanManage("user-1", user));
    }

    [Fact]
    public void AdminCanManageAnyPlaylist()
    {
        var admin = Principal((ClaimTypes.Role, "Admin"));

        Assert.True(PlaylistAccessPolicy.CanManage("user-1", admin));
    }

    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "test"));
}
