using Microsoft.AspNetCore.Mvc;
using UserManagementAPI.Models;
using UserManagementAPI.Services;

namespace UserManagementAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IUsersService _service;

    public UsersController(IUsersService service)
    {
        _service = service;
    }

    [HttpGet]
    public IActionResult GetAll()
        => Ok(_service.GetAll());

    [HttpGet("{id}")]
    public IActionResult Get(int id)
    {
        var user = _service.Get(id);
        if (user == null) return NotFound();
        return Ok(user);
    }

    [HttpPost]
    public IActionResult Create(User user)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        return Ok(_service.Create(user));
    }

    [HttpPut("{id}")]
    public IActionResult Update(int id, User user)
    {
        if (!_service.Update(id, user))
            return NotFound();

        return Ok(user);
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        if (!_service.Delete(id))
            return NotFound();

        return Ok("Deleted");
    }
}