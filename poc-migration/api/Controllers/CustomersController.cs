using System.Data;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PocApi.Models;

namespace PocApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CustomersController(IDbConnection db) : ControllerBase
{
    [HttpGet]
    public async Task<IEnumerable<Customer>> Get()
        => await db.QueryAsync<Customer>("poc.usp_ListCustomers",
            commandType: CommandType.StoredProcedure);
}
