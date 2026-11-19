using System.Net.Mime;
using Knit.WebApi.Contracts;
using Microsoft.AspNetCore.Http;

namespace Knit.WebApi;

/// <summary>The API's error reply: RFC 9457 problem details, the code doubling as title.</summary>
internal static class ApiProblem
{
    public static IResult Problem(int status, string code, string detail)
        => Results.Json(
            new ProblemDto(code, status, detail, code),
            ApiJsonContext.Default.ProblemDto,
            statusCode: status,
            contentType: MediaTypeNames.Application.ProblemJson);
}
