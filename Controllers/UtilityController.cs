using Microsoft.AspNetCore.Mvc;
using OCR_BACKEND.Services;

[Route("api/[controller]")]
[ApiController]
public class UtilityController : ControllerBase
{
    private readonly IUtilityService _service;

    public UtilityController(IUtilityService service)
    {
        _service = service;
    }

    [HttpGet("allDropdown")]
    public async Task<IActionResult> AllDropdown(
      string? searchTerm,
      int page,
      int type,
      int parentId)
    {
        if (string.IsNullOrEmpty(searchTerm))
            searchTerm = "";

        int pageSize = 50;

        var result = await _service.AllDropdown(searchTerm, page, pageSize, type, parentId);

        return Ok(result);
    }
}