using PocApi.DTOs;
using PocApi.Models;

namespace PocApi.Repositories;

public interface ICustomerRepository
{
    Task<PagedResult<Customer>> ListAsync(int page, int pageSize);
    Task<Customer?>             GetByIdAsync(int id);
    Task<Customer>              CreateAsync(CustomerRequest request);
    Task<Customer?>             UpdateAsync(int id, CustomerRequest request);
    /// <returns><c>true</c> if the record existed and was deleted; <c>false</c> if not found.</returns>
    Task<bool>                  DeleteAsync(int id);
}
