using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Moq;
using PocApi.Controllers;
using PocApi.DTOs;
using PocApi.Models;
using PocApi.Repositories;

namespace PocApi.Tests.Controllers;

public class CustomersControllerTests
{
    private readonly Mock<ICustomerRepository> _repo = new();

    private CustomersController Sut()
    {
        var controller = new CustomersController(_repo.Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        controller.ProblemDetailsFactory = BuildProblemDetailsFactory();
        return controller;
    }

    // Minimal factory that returns a ValidationProblemDetails with Status set so
    // ObjectResult.StatusCode is populated the same way the real middleware would.
    private static ProblemDetailsFactory BuildProblemDetailsFactory()
    {
        var mock = new Mock<ProblemDetailsFactory>();
        mock.Setup(f => f.CreateValidationProblemDetails(
                It.IsAny<HttpContext>(),
                It.IsAny<ModelStateDictionary>(),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>()))
            .Returns((HttpContext _, ModelStateDictionary ms, int? status, string? title,
                      string? type, string? detail, string? instance) =>
                new ValidationProblemDetails(ms) { Status = status ?? 400, Title = title, Detail = detail });
        return mock.Object;
    }

    // ── List ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_ValidParams_Returns200WithPagedResult()
    {
        var expected = new PagedResult<Customer>
        {
            Items      = new[] { new Customer(1, "Alice", "a@b.com", 500m, "Bronze") },
            Page       = 1,
            PageSize   = 10,
            TotalCount = 1
        };
        _repo.Setup(r => r.ListAsync(1, 10)).ReturnsAsync(expected);

        var result = await Sut().List(1, 10) as OkObjectResult;

        Assert.NotNull(result);
        Assert.Equal(200, result!.StatusCode);
        Assert.Same(expected, result.Value);
    }

    [Theory]
    [InlineData(0,  10)]
    [InlineData(-1, 10)]
    public async Task List_InvalidPage_Returns400(int page, int pageSize)
    {
        var result = await Sut().List(page, pageSize);

        var problem = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(400, ((ProblemDetails)problem.Value!).Status);
    }

    [Theory]
    [InlineData(1,   0)]
    [InlineData(1, 101)]
    [InlineData(1,  -5)]
    public async Task List_InvalidPageSize_Returns400(int page, int pageSize)
    {
        var result = await Sut().List(page, pageSize);

        var problem = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(400, ((ProblemDetails)problem.Value!).Status);
    }

    // ── GetById ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetById_Found_Returns200WithCustomer()
    {
        var customer = new Customer(1, "Alice", "a@b.com", 500m, "Bronze");
        _repo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(customer);

        var result = await Sut().GetById(1) as OkObjectResult;

        Assert.NotNull(result);
        Assert.Equal(200, result!.StatusCode);
        Assert.Same(customer, result.Value);
    }

    [Fact]
    public async Task GetById_NotFound_Returns404()
    {
        _repo.Setup(r => r.GetByIdAsync(99)).ReturnsAsync((Customer?)null);

        var result = await Sut().GetById(99);

        Assert.IsType<NotFoundResult>(result);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_ValidRequest_Returns201WithLocationHeader()
    {
        var request = new CustomerRequest { Name = "Bob", Email = "bob@b.com", TotalSpend = 1500m };
        var created = new Customer(42, "Bob", "bob@b.com", 1500m, "Silver");
        _repo.Setup(r => r.CreateAsync(request)).ReturnsAsync(created);

        var result = await Sut().Create(request) as CreatedAtActionResult;

        Assert.NotNull(result);
        Assert.Equal(201, result!.StatusCode);
        Assert.Equal("GetById", result.ActionName);
        Assert.Equal(42, result.RouteValues!["id"]);
        Assert.Same(created, result.Value);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_Found_Returns200WithUpdatedCustomer()
    {
        var request = new CustomerRequest { Name = "Alice2", Email = "a2@b.com", TotalSpend = 12000m };
        var updated = new Customer(1, "Alice2", "a2@b.com", 12000m, "Gold");
        _repo.Setup(r => r.UpdateAsync(1, request)).ReturnsAsync(updated);

        var result = await Sut().Update(1, request) as OkObjectResult;

        Assert.NotNull(result);
        Assert.Equal(200, result!.StatusCode);
        Assert.Same(updated, result.Value);
    }

    [Fact]
    public async Task Update_NotFound_Returns404()
    {
        var request = new CustomerRequest { Name = "X", Email = "x@x.com", TotalSpend = 0m };
        _repo.Setup(r => r.UpdateAsync(99, request)).ReturnsAsync((Customer?)null);

        var result = await Sut().Update(99, request);

        Assert.IsType<NotFoundResult>(result);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_Found_Returns204()
    {
        _repo.Setup(r => r.DeleteAsync(1)).ReturnsAsync(true);

        var result = await Sut().Delete(1);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Delete_NotFound_Returns404()
    {
        _repo.Setup(r => r.DeleteAsync(99)).ReturnsAsync(false);

        var result = await Sut().Delete(99);

        Assert.IsType<NotFoundResult>(result);
    }
}
