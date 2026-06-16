using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PocApi.DTOs;
using PocApi.Models;
using PocApi.Repositories;

namespace PocApi.Controllers;

/// <summary>CRUD operations for Customers. All endpoints require a valid JWT Bearer token.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[Produces("application/json")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public class CustomersController(ICustomerRepository repo) : ControllerBase
{
    /// <summary>List customers with pagination.</summary>
    /// <param name="page">1-based page number (default: 1).</param>
    /// <param name="pageSize">Records per page, 1–100 (default: 10).</param>
    /// <returns>Paginated list of customers, sorted by TotalSpend descending.</returns>
    /// <response code="200">Paged result containing matching customers.</response>
    /// <response code="400">page is less than 1, or pageSize is outside 1–100.</response>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<Customer>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(
        [FromQuery] int page     = 1,
        [FromQuery] int pageSize = 10)
    {
        if (page < 1)
            return ValidationProblem(detail: "page must be 1 or greater.", title: "Invalid parameter");

        if (pageSize < 1 || pageSize > 100)
            return ValidationProblem(detail: "pageSize must be between 1 and 100.", title: "Invalid parameter");

        return Ok(await repo.ListAsync(page, pageSize));
    }

    /// <summary>Get a single customer by ID.</summary>
    /// <param name="id">The customer's primary key.</param>
    /// <returns>The matching customer record.</returns>
    /// <response code="200">Customer found.</response>
    /// <response code="404">No customer with the given ID exists.</response>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(Customer), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id)
    {
        var customer = await repo.GetByIdAsync(id);
        return customer is null ? NotFound() : Ok(customer);
    }

    /// <summary>Create a new customer.</summary>
    /// <param name="request">Customer data. Tier is computed from TotalSpend (≥10 000 → Gold, ≥1 000 → Silver, else Bronze).</param>
    /// <returns>The created customer, including its generated ID and computed Tier.</returns>
    /// <response code="201">Customer created. Location header points to GET /api/customers/{id}.</response>
    /// <response code="400">Validation errors in the request body.</response>
    [HttpPost]
    [ProducesResponseType(typeof(Customer), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CustomerRequest request)
    {
        var created = await repo.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = created.CustomerId }, created);
    }

    /// <summary>Replace all fields of an existing customer.</summary>
    /// <param name="id">The customer's primary key.</param>
    /// <param name="request">New customer data. Tier is recomputed from the new TotalSpend.</param>
    /// <returns>The updated customer record.</returns>
    /// <response code="200">Customer updated successfully.</response>
    /// <response code="400">Validation errors in the request body.</response>
    /// <response code="404">No customer with the given ID exists.</response>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(Customer), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] CustomerRequest request)
    {
        var updated = await repo.UpdateAsync(id, request);
        return updated is null ? NotFound() : Ok(updated);
    }

    /// <summary>Delete a customer permanently.</summary>
    /// <param name="id">The customer's primary key.</param>
    /// <response code="204">Customer deleted. No content returned.</response>
    /// <response code="404">No customer with the given ID exists.</response>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await repo.DeleteAsync(id);
        return deleted ? NoContent() : NotFound();
    }
}
