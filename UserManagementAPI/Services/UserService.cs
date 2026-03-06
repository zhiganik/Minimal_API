using UserManagementAPI.Models;

namespace UserManagementAPI.Services;

public interface IUsersService
{
    List<User> GetAll();
    User? Get(int id);
    User Create(User user);
    bool Update(int id, User updated);
    bool Delete(int id);
}

public class UsersService : IUsersService
{
    private static List<User> users = new();
    private static int nextId = 1;

    public List<User> GetAll() => users;

    public User? Get(int id) =>
        users.FirstOrDefault(u => u.Id == id);

    public User Create(User user)
    {
        user.Id = nextId++;
        users.Add(user);
        return user;
    }

    public bool Update(int id, User updated)
    {
        var user = Get(id);
        if (user == null) return false;

        user.Name = updated.Name;
        user.Email = updated.Email;
        user.Age = updated.Age;

        return true;
    }

    public bool Delete(int id)
    {
        var user = Get(id);
        if (user == null) return false;

        users.Remove(user);
        return true;
    }
}