namespace UserManagementAPI.Middleware;

public class AuthenticationMiddleware
{
    private readonly RequestDelegate _next;

    public AuthenticationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Headers.ContainsKey("Authorization"))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("401 Unauthorized - Token Missing");
            return;
        }
        // Simple logic: Allow any token that starts with "Bearer "
        var token = context.Request.Headers["Authorization"].ToString();
        if (!token.StartsWith("Bearer "))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("401 Unauthorized - Invalid Token");
            return;
        }
        
        await _next(context);
    }
}