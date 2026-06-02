using System.Security.Claims;

namespace Tratoo.API.EndPoints
{
    public static class ClaimsHelper
    {
        public static int? ExtrairUserId(HttpContext http)
        {
            var claim = http.User.FindFirst(ClaimTypes.NameIdentifier)
                     ?? http.User.FindFirst("sub")
                     ?? http.User.FindFirst("id");

            return claim != null && int.TryParse(claim.Value, out var id) ? id : null;
        }
    }
}
