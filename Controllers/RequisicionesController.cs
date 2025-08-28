using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using BackendRequisicionPersonal.Services;
using BackendRequisicionPersonal.Models;

namespace BackendRequisicionPersonal.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class RequisicionesController : ControllerBase
    {
        private readonly SolicitudesPersonalService _service;
        private readonly ILogger<RequisicionesController> _logger;

        public RequisicionesController(SolicitudesPersonalService service, ILogger<RequisicionesController> logger)
        {
            _service = service;
            _logger = logger;
        }

        [HttpGet("test-connection")]
        public IActionResult TestConnection()
        {
            try
            {
                _logger.LogInformation("GET /api/requisiciones/test-connection");
                var ok = _service.TestConnection();
                return ok ? Ok("OK") : StatusCode(500, "Error de conexión");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en test-connection");
                return StatusCode(500, new { success = false, message = "Error interno." });
            }
        }

        [HttpPost("insertar")]
        public IActionResult Insertar([FromBody] SolicitudPersonalDto dto)
        {
            _logger.LogInformation("POST /api/requisiciones/insertar");
            if (dto == null)
                return BadRequest(new { success = false, message = "Payload inválido" });

            try
            {
                var id = _service.Insertar(dto);
                if (id <= 0)
                {
                    _logger.LogWarning("Insertar: no se insertó la solicitud");
                    return StatusCode(500, new { success = false, message = "No se insertó la solicitud" });
                }

                return Ok(new { success = true, id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en insertar");
                return StatusCode(500, new { success = false, message = "Error interno al insertar" });
            }
        }

        [HttpGet("listar")]
        public IActionResult Listar([FromQuery] string usuarioId)
        {
            _logger.LogInformation("GET /api/requisiciones/listar usuarioId={UsuarioId}", usuarioId);

            try
            {
                var data = _service.Listar(usuarioId);
                if (data == null || data.Count == 0)
                    return NotFound(new { success = false, message = "Sin registros" });

                return Ok(new { success = true, total = data.Count, data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en listar para usuario {UsuarioId}", usuarioId);
                return StatusCode(500, new { success = false, message = "Error interno al listar" });
            }
        }

        [HttpGet("canales")]
        public IActionResult ListarCanales()
        {
            _logger.LogInformation("GET /api/requisiciones/canales");

            var data = _service.ListarCanales();
            if (data == null || data.Count == 0)
                return NotFound(new { success = false, message = "Sin registros" });

            return Ok(new { success = true, total = data.Count, data });
        }

        [HttpGet("cargos-canales")]
        public IActionResult ListarCargosCanales([FromQuery] string? canal = null)
        {
            _logger.LogInformation("GET /api/requisiciones/cargos-canales canal={Canal}", canal);

            var data = _service.ListarCargosCanales(canal);
            if (data == null || data.Count == 0)
                return NotFound(new { success = false, message = "Sin registros" });

            return Ok(new { success = true, total = data.Count, data });
        }

        [HttpGet("accion")]
        public IActionResult Accion([FromQuery] int id, [FromQuery] string estado, [FromQuery] string? motivo = null)
        {
            _logger.LogInformation("GET /api/requisiciones/accion id={Id} estado={Estado}", id, estado);

            if (id <= 0 || string.IsNullOrWhiteSpace(estado))
                return BadRequest(new { success = false, message = "Parámetros inválidos" });

            try
            {
                var ok = _service.AplicarAccion(id, estado, motivo);
                if (!ok) return NotFound(new { success = false, message = "No se actualizó el registro" });

                return Ok(new { success = true, message = "Acción aplicada" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en accion id={Id} estado={Estado}", id, estado);
                return StatusCode(500, new { success = false, message = "Error interno al aplicar acción" });
            }
        }
    }
}
