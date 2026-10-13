using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.TrueMusicLibrary.Compat;

/// <summary>
/// Liste des utilisateurs, compatible avec toutes les versions supportées.
/// Jellyfin 10.11.0 expose une propriété <c>Users</c>, remplacée par <c>GetUsers()</c> dans un correctif de 10.11
/// (vérifié sur 10.11.11) : un appel direct compilé contre 10.11.0 planterait sur les 10.11 récentes.
/// </summary>
public static class UserManagerCompat
{
    public static IReadOnlyList<User> GetUsers(IUserManager userManager)
    {
#if JF10
        var type = userManager.GetType();
        var users = type.GetMethod("GetUsers", Type.EmptyTypes)?.Invoke(userManager, null)
            ?? type.GetProperty("Users")?.GetValue(userManager);
        return (users as IEnumerable<User>)?.ToList() ?? [];
#else
        return userManager.GetUsers().ToList();
#endif
    }
}
