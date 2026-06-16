using System.Data;
using Dapper;
using PocApi.DTOs;
using PocApi.Models;

namespace PocApi.Repositories;

public sealed class CustomerRepository(IDbConnection db) : ICustomerRepository
{
    // Holds an extra TotalCount column returned by usp_ListCustomers for pagination metadata.
    private sealed record PaginatedRow(
        int CustomerId, string Name, string Email, decimal TotalSpend, string Tier, int TotalCount);

    public async Task<PagedResult<Customer>> ListAsync(int page, int pageSize)
    {
        var rows = (await db.QueryAsync<PaginatedRow>(
            "poc.usp_ListCustomers",
            new { Page = page, PageSize = pageSize },
            commandType: CommandType.StoredProcedure)).ToList();

        var totalCount = rows.Count > 0 ? rows[0].TotalCount : 0;
        var items = rows
            .Select(r => new Customer(r.CustomerId, r.Name, r.Email, r.TotalSpend, r.Tier))
            .ToList();

        return new PagedResult<Customer>
        {
            Items      = items,
            Page       = page,
            PageSize   = pageSize,
            TotalCount = totalCount
        };
    }

    public async Task<Customer?> GetByIdAsync(int id)
        => await db.QuerySingleOrDefaultAsync<Customer>(
            "poc.usp_GetCustomer",
            new { CustomerId = id },
            commandType: CommandType.StoredProcedure);

    public async Task<Customer> CreateAsync(CustomerRequest request)
    {
        // usp_CreateCustomer inserts and returns the full row (including computed Tier).
        var created = await db.QuerySingleAsync<Customer>(
            "poc.usp_CreateCustomer",
            new { request.Name, request.Email, request.TotalSpend },
            commandType: CommandType.StoredProcedure);
        return created;
    }

    public async Task<Customer?> UpdateAsync(int id, CustomerRequest request)
    {
        // usp_UpdateCustomer returns the updated row only when the customer exists.
        var updated = await db.QuerySingleOrDefaultAsync<Customer>(
            "poc.usp_UpdateCustomer",
            new { CustomerId = id, request.Name, request.Email, request.TotalSpend },
            commandType: CommandType.StoredProcedure);
        return updated;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        // Execute returns rows-affected; > 0 means the record existed.
        var affected = await db.ExecuteAsync(
            "poc.usp_DeleteCustomer",
            new { CustomerId = id },
            commandType: CommandType.StoredProcedure);
        return affected > 0;
    }
}
