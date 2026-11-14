using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UserAdmin.Api.Auth;
using UserAdmin.Api.Contracts;

namespace UserAdmin.Api.Controllers;

/// <summary>The seller's details printed at the top of orders, set in the Company configuration section.</summary>
[ApiController]
[Route("api/company")]
public class CompanyController(IConfiguration configuration) : ControllerBase
{
    [HttpGet]
    [Authorize(Permissions.OrdersRead)]
    public CompanyDto Get()
    {
        var company = configuration.GetSection("Company");
        return new CompanyDto(
            Input.Optional(company["Name"]) ?? "Your company",
            Input.Optional(company["Address"]),
            Input.Optional(company["Phone"]),
            Input.Optional(company["Email"]),
            Input.Optional(company["TaxNumber"]));
    }
}
