using Microsoft.AspNetCore.Mvc;
using BackendRequisicionPersonal.Services.Auth;
using BackendRequisicionPersonal.Models.Auth;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BackendRequisicionPersonal.Controllers.Auth
{
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class AuthController : ControllerBase
    {
        private readonly AuthService _authService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(AuthService authService, ILogger<AuthController> logger)
        {
            _authService = authService;
            _logger = logger;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            _logger.LogInformation("POST /api/auth/login");

            if (request == null ||
                string.IsNullOrWhiteSpace(request.Identificacion) ||
                string.IsNullOrWhiteSpace(request.Correo))
            {
                _logger.LogWarning("Login fallido: datos incompletos.");
                return BadRequest(new { success = false, message = "Datos de login incompletos." });
            }

            try
            {
                var usuario = await _authService.ValidarUsuarioAsync(
                    request.Identificacion.Trim(),
                    request.Correo.Trim()
                );

                if (usuario == null)
                {
                    _logger.LogWarning("Login fallido para identificacion={Identificacion}, correo={Correo}",
                        request.Identificacion, request.Correo);
                    return Unauthorized(new { success = false, message = "Credenciales inválidas." });
                }

                _logger.LogInformation("Login exitoso para identificacion={Identificacion}", request.Identificacion);

                return Ok(new
                {
                    success = true,
                    identificacion = usuario.Identificacion,
                    jefeInmediatoSolicitante = usuario.JefeInmediatoSolicitante,
                    cargo = usuario.Cargo,
                    correo = usuario.Correo,
                    canal = usuario.Canal,
                    area = usuario.Area,
                    aprobador1 = usuario.Aprobador1,
                    correoAprobador1 = usuario.CorreoAprobador1,
                    aprobador2 = usuario.Aprobador2,
                    correoAprobador2 = usuario.CorreoAprobador2,
                    aprobador3 = usuario.Aprobador3,
                    correoAprobador3 = usuario.CorreoAprobador3,
                    centroCosto = usuario.CentroCosto,
                    vp = usuario.Vp,
                    novedad = usuario.Novedad,
                    roles = usuario.Roles
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error interno en login identificacion={Identificacion}", request.Identificacion);
                return StatusCode(500, new { success = false, message = "Error interno al procesar login." });
            }
        }
    }
}
