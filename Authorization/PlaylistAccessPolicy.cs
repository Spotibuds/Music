using System.Security.Claims;

namespace Music.Authorization;

public static class PlaylistAccessPolicy
{
    public static bool CanManage(string? ownerId, ClaimsPrincipal principal)
    {
        if (principal.IsInRole("Admin"))
        {
            return true;
        }

        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub");

        return !string.IsNullOrWhiteSpace(ownerId) && ownerId == userId;
    }
}
