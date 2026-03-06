using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// Register services
builder.Services.AddSingleton<IUserService, UserService>();
builder.Services.AddSingleton<IAuthService, AuthService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Apply middleware
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<AuthenticationMiddleware>();

var userService = app.Services.GetRequiredService<IUserService>();

// GET: Retrieve all users
app.MapGet("/users", () => Results.Ok(userService.GetAllUsers()))
    .WithName("GetAllUsers");

// GET: Retrieve a specific user by ID
app.MapGet("/users/{id}", (int id) =>
{
    if (id <= 0)
        return Results.BadRequest("Invalid user ID. ID must be greater than 0.");

    var user = userService.GetUserById(id);
    return user is not null ? Results.Ok(user) : Results.NotFound($"User with ID {id} not found.");
})
    .WithName("GetUserById");

// POST: Add a new user
app.MapPost("/users", (UserDto userDto, IUserService service) =>
{
    var validationResult = ValidationHelper.ValidateUserDto(userDto);
    if (!validationResult.IsValid)
        return Results.BadRequest(validationResult.ErrorMessage);

    var user = service.CreateUser(userDto);
    return Results.Created($"/users/{user.Id}", user);
})
    .WithName("CreateUser");

// PUT: Update an existing user's details
app.MapPut("/users/{id}", (int id, UserDto userDto, IUserService service) =>
{
    if (id <= 0)
        return Results.BadRequest("Invalid user ID. ID must be greater than 0.");

    var validationResult = ValidationHelper.ValidateUserDto(userDto);
    if (!validationResult.IsValid)
        return Results.BadRequest(validationResult.ErrorMessage);

    var user = service.UpdateUser(id, userDto);
    return user is not null ? Results.Ok(user) : Results.NotFound($"User with ID {id} not found.");
})
    .WithName("UpdateUser");

// DELETE: Remove a user by ID
app.MapDelete("/users/{id}", (int id, IUserService service) =>
{
    if (id <= 0)
        return Results.BadRequest("Invalid user ID. ID must be greater than 0.");

    var deleted = service.DeleteUser(id);
    return deleted ? Results.NoContent() : Results.NotFound($"User with ID {id} not found.");
})
    .WithName("DeleteUser");

// Authentication endpoint
app.MapPost("/auth/login", (LoginDto loginDto, IAuthService authService) =>
{
    if (string.IsNullOrWhiteSpace(loginDto.Username) || string.IsNullOrWhiteSpace(loginDto.Password))
        return Results.BadRequest("Username and password are required.");

    var token = authService.Login(loginDto.Username, loginDto.Password);
    return token is not null ? Results.Ok(new { token }) : Results.Unauthorized();
})
    .WithName("Login")
    .AllowAnonymous();

app.Run();

// ========== MIDDLEWARE ==========

public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var requestId = Guid.NewGuid().ToString();

        _logger.LogInformation(
            "[{RequestId}] Incoming Request: {Method} {Path} from {RemoteIp}",
            requestId, context.Request.Method, context.Request.Path, context.Connection.RemoteIpAddress);

        await _next(context);

        stopwatch.Stop();
        _logger.LogInformation(
            "[{RequestId}] Outgoing Response: {StatusCode} - Duration: {Duration}ms",
            requestId, context.Response.StatusCode, stopwatch.ElapsedMilliseconds);
    }
}

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unhandled exception occurred: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;

        var response = new
        {
            statusCode = context.Response.StatusCode,
            message = "An error occurred while processing your request.",
            detail = exception.Message
        };

        await context.Response.WriteAsJsonAsync(response);
    }
}

public class AuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuthenticationMiddleware> _logger;

    public AuthenticationMiddleware(RequestDelegate next, ILogger<AuthenticationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IAuthService authService)
    {
        // Skip authentication for login endpoint
        if (context.Request.Path.StartsWithSegments("/auth/login") ||
            context.Request.Path.StartsWithSegments("/openapi"))
        {
            await _next(context);
            return;
        }

        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
        {
            _logger.LogWarning("Missing or invalid authorization header");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { message = "Unauthorized. Token required." });
            return;
        }

        var token = authHeader.Substring("Bearer ".Length).Trim();
        if (!authService.ValidateToken(token))
        {
            _logger.LogWarning("Invalid token provided");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { message = "Invalid or expired token." });
            return;
        }

        await _next(context);
    }
}

// ========== SERVICES ==========

public interface IUserService
{
    List<User> GetAllUsers();
    User? GetUserById(int id);
    User CreateUser(UserDto userDto);
    User? UpdateUser(int id, UserDto userDto);
    bool DeleteUser(int id);
}

public class UserService : IUserService
{
    private readonly ConcurrentDictionary<int, User> _users = new();
    private int _nextId = 3;

    public UserService()
    {
        _users.TryAdd(1, new User(1, "John Doe", "john@example.com"));
        _users.TryAdd(2, new User(2, "Jane Smith", "jane@example.com"));
    }

    public List<User> GetAllUsers() => _users.Values.ToList();

    public User? GetUserById(int id) => _users.TryGetValue(id, out var user) ? user : null;

    public User CreateUser(UserDto userDto)
    {
        var id = Interlocked.Increment(ref _nextId) - 1;
        var user = new User(id, userDto.Name.Trim(), userDto.Email.Trim().ToLower());

        if (!_users.TryAdd(id, user))
            throw new InvalidOperationException("Failed to add user.");

        return user;
    }

    public User? UpdateUser(int id, UserDto userDto)
    {
        if (!_users.ContainsKey(id))
            return null;

        var updatedUser = new User(id, userDto.Name.Trim(), userDto.Email.Trim().ToLower());
        _users[id] = updatedUser;
        return updatedUser;
    }

    public bool DeleteUser(int id) => _users.TryRemove(id, out _);
}

public interface IAuthService
{
    string? Login(string username, string password);
    bool ValidateToken(string token);
}

public class AuthService : IAuthService
{
    private readonly ConcurrentDictionary<string, DateTime> _tokens = new();
    private readonly TimeSpan _tokenExpiration = TimeSpan.FromHours(1);

    public string? Login(string username, string password)
    {
        // Simple demo authentication - replace with real authentication
        if (username == "admin" && password == "password123")
        {
            var token = Guid.NewGuid().ToString();
            _tokens[token] = DateTime.UtcNow.Add(_tokenExpiration);
            return token;
        }

        return null;
    }

    public bool ValidateToken(string token)
    {
        if (_tokens.TryGetValue(token, out var expiration))
        {
            if (DateTime.UtcNow <= expiration)
                return true;

            // Remove expired token
            _tokens.TryRemove(token, out _);
        }

        return false;
    }
}

// ========== HELPERS ==========

public static class ValidationHelper
{
    public static ValidationResult ValidateUserDto(UserDto userDto)
    {
        if (userDto is null)
            return new ValidationResult(false, "User data is required.");

        if (string.IsNullOrWhiteSpace(userDto.Name))
            return new ValidationResult(false, "Name is required and cannot be empty.");

        if (userDto.Name.Trim().Length < 2)
            return new ValidationResult(false, "Name must be at least 2 characters long.");

        if (userDto.Name.Trim().Length > 100)
            return new ValidationResult(false, "Name cannot exceed 100 characters.");

        if (string.IsNullOrWhiteSpace(userDto.Email))
            return new ValidationResult(false, "Email is required and cannot be empty.");

        if (!IsValidEmail(userDto.Email.Trim()))
            return new ValidationResult(false, "Invalid email format.");

        return new ValidationResult(true, null);
    }

    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        try
        {
            var emailPattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";
            return Regex.IsMatch(email, emailPattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
        }
        catch
        {
            return false;
        }
    }
}

// ========== MODELS ==========

public record User(int Id, string Name, string Email);
public record UserDto(string Name, string Email);
public record LoginDto(string Username, string Password);
public record ValidationResult(bool IsValid, string? ErrorMessage);

// Extension method for AllowAnonymous
public static class EndpointExtensions
{
    public static RouteHandlerBuilder AllowAnonymous(this RouteHandlerBuilder builder) => builder;
}
